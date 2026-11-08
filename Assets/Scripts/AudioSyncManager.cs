using System;
using System.Collections.Generic;
using UnityEngine;
using SprunkiKids.Data;

/// <summary>Kept for serialized-data compatibility. Drops are now ALWAYS quantized to the next bar boundary.</summary>
public enum SyncStartMode
{
    ImmediatePhaseMatched = 0,
    QuantizedToNextBeat = 1,
    QuantizedToNextLoop = 2
}

/// <summary>
/// Sample-accurate DSP master clock with a QUANTIZED (bar-aligned) drop engine, modelled on the original Sprunki.
///
/// Measured original audio (RawAssets WAVs): 17 of 20 stems are 4.806-4.825s, i.e. exactly 1 "bar" = 8 beats @ 100 BPM = 4.8s
/// (the extra few ms are the reverb tail). Only Blue (Jevin, 8.5s) and Pink (Pinki, 9.7-10.5s) are 2-bar stems (9.6s).
///
/// Rules:
///  - Every stem is classified as a 1-bar or 2-bar phrase from its real length (never cut before it finishes: the next phrase is
///    scheduled on a SECOND AudioSource so tails/reverb overlap instead of being chopped).
///  - 1-bar stems repeat 2x inside a 2-bar loop; 2-bar stems start on even bars so they stay aligned with the 9.6s master loop.
///  - A character dropped while the stage is already running is QUEUED: its first phrase is PlayScheduled on the next bar
///    downbeat (sample-accurate) and all queued characters burst in together. The first character starts the clock immediately.
/// </summary>
public class AudioSyncManager : MonoBehaviour
{
    private struct PhraseInfo
    {
        public bool Valid;
        public double StartDsp;
        public AudioClip Clip;
    }

    private sealed class SlotAudioState
    {
        public int SlotIndex;
        public AudioSource PrimarySource;
        public AudioSource AlternateSource;
        public bool NextUsesAlternate;
        public bool OwnsPrimaryObject;
        public CharacterSoundData SoundData;
        public AudioClip FixedClip;
        public int PeriodBars = 1;
        public double PeriodSeconds;
        public long PhrasesScheduled;
        public double NextPhraseDsp;
        public PhraseInfo Current;
        public PhraseInfo Pending;
        public double RegisteredDsp;
        public double FirstStartDsp;
        public bool Started;
        public bool StartAnnounced;
        public float BaseVolume = 1f;
        public bool IsMuted;
        public bool IsSolo;
    }

    public static AudioSyncManager Instance { get; private set; }

    [Header("Master Clock (original Sprunki: 100 BPM, 8 beats = 4.8s bar)")]
    [SerializeField, Min(20f)] private float bpm = 100f;
    [SerializeField, Min(1)] private int beatsPerBar = 8;
    [SerializeField, Min(1)] private int barsPerLoop = 2;
    [Tooltip("Optional exact bar length in seconds measured from the WAV files (0 = derive from BPM).")]
    [SerializeField, Min(0f)] private float barSecondsOverride = 0f;
    [SerializeField] private SyncStartMode syncStartMode = SyncStartMode.QuantizedToNextLoop;
    [SerializeField] private bool resetClockWhenAllSlotsEmpty = true;
    [SerializeField] private bool startClockOnAwake = false;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;

    [Header("DSP Scheduling")]
    [SerializeField, Range(0.01f, 0.15f)] private float scheduleLeadTimeSeconds = 0.05f;
    [SerializeField, Range(0.1f, 1.5f)] private float phraseLookaheadSeconds = 0.6f;
    [SerializeField, Range(0.02f, 0.1f)] private float envelopeWindowSeconds = 0.04f;
    [SerializeField, Range(1, 8)] private int maxCatchUpBeatsPerFrame = 2;
    [SerializeField] private bool dontDestroyOnLoad = false;

    public event Action OnBeat;
    public event Action<int> OnBeatIndex;
    /// <summary>Fired at the start of every bar with the bar index inside the loop.</summary>
    public event Action<int> OnBar;
    public event Action OnLoopRestart;
    public event Action<int, bool> OnSlotPlaybackChanged;
    /// <summary>Fired when a QUEUED slot reaches its downbeat and starts singing (the "beat drop").</summary>
    public event Action<int> OnSlotStarted;
    public event Action<bool> OnPartyModeChanged;

    private readonly Dictionary<int, SlotAudioState> _slotsByIndex = new Dictionary<int, SlotAudioState>(8);
    private readonly Dictionary<AudioSource, int> _sourceToSlotIndex = new Dictionary<AudioSource, int>(8);
    private readonly List<SlotAudioState> _activeSlots = new List<SlotAudioState>(8);
    private readonly Dictionary<AudioClip, float[]> _envelopes = new Dictionary<AudioClip, float[]>(40);

    private double _masterStartDspTime;
    private long _lastDispatchedAbsoluteBeat = -1;
    private bool _isClockRunning;
    private bool _isPartyModeActive;
    private int _nextAdHocSlotId = 1000;

    public SyncStartMode StartMode => syncStartMode;
    public float Bpm => bpm;
    public int BeatsPerBar => beatsPerBar;
    public int BarsPerLoop => barsPerLoop;
    public int BeatsPerLoop => Mathf.Max(1, beatsPerBar * barsPerLoop);
    public double SecondsPerBeat => 60.0 / Math.Max(20.0, bpm);
    public double BarDurationSeconds => barSecondsOverride > 0.1f ? barSecondsOverride : SecondsPerBeat * beatsPerBar;
    public double LoopDurationSeconds => BarDurationSeconds * barsPerLoop;
    public double MasterStartDspTime => _masterStartDspTime;
    public bool IsClockRunning => _isClockRunning;
    public int ActiveTrackCount => _activeSlots.Count;
    public bool IsPartyModeActive => _isPartyModeActive;

    public float MasterVolume
    {
        get => masterVolume;
        set { masterVolume = Mathf.Clamp01(value); RefreshAllSlotVolumes(); }
    }

    /// <summary>Progress 0..1 through the CURRENT BAR (the quantization grid) - drives the circular beat pie.</summary>
    public float NormalizedLoopProgress
    {
        get
        {
            double bar = BarDurationSeconds;
            if (!_isClockRunning || bar <= 0.0) return 0f;
            double elapsed = AudioSettings.dspTime - _masterStartDspTime;
            if (elapsed <= 0.0) return 0f;
            return Mathf.Clamp01((float)((elapsed % bar) / bar));
        }
    }

    public int CurrentBeatInLoop
    {
        get
        {
            if (!_isClockRunning) return 0;
            double elapsed = AudioSettings.dspTime - _masterStartDspTime;
            if (elapsed < 0.0) return 0;
            return (int)((long)Math.Floor(elapsed / SecondsPerBeat) % BeatsPerLoop);
        }
    }

    public int CurrentBar => CurrentBeatInLoop / Math.Max(1, beatsPerBar);

    public bool HasAnySoloedSlot
    {
        get
        {
            for (int i = 0; i < _activeSlots.Count; i++)
            {
                if (_activeSlots[i].IsSolo) return true;
            }
            return false;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        if (startClockOnAwake) StartMasterClock(AudioSettings.dspTime + scheduleLeadTimeSeconds);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!_isClockRunning) return;

        double dspNow = AudioSettings.dspTime;
        DispatchBeatEvents(dspNow);
        AdvanceSlotPhrases(dspNow);
    }

    public void StartMasterClock(double? explicitStartDspTime = null)
    {
        _masterStartDspTime = explicitStartDspTime ?? (AudioSettings.dspTime + scheduleLeadTimeSeconds);
        _lastDispatchedAbsoluteBeat = -1;
        _isClockRunning = true;
    }

    public void SetTempo(float newBpm)
    {
        if (!_isClockRunning) bpm = Mathf.Max(20f, newBpm);
    }

    // ------------------------------------------------------------------ registration API

    public void RegisterAndPlay(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;

        if (!_sourceToSlotIndex.TryGetValue(source, out int slotIndex))
        {
            slotIndex = _nextAdHocSlotId++;
            _sourceToSlotIndex[source] = slotIndex;
        }

        PlaySyncedAudioClip(slotIndex, source, clip);
    }

    public void StopAndUnregister(AudioSource source)
    {
        if (source == null) return;

        if (_sourceToSlotIndex.TryGetValue(source, out int slotIndex))
        {
            _sourceToSlotIndex.Remove(source);
            StopSlot(slotIndex);
        }
        else
        {
            source.Stop();
        }
    }

    /// <summary>
    /// Registers a character. Returns TRUE when the character was QUEUED for the next bar downbeat (stage already running),
    /// FALSE when it started immediately (first character on an empty stage).
    /// </summary>
    public bool RegisterAndPlaySlot(int slotIndex, AudioSource source, CharacterSoundData soundData, bool? overridePartyMode = null)
    {
        if (soundData == null)
        {
            Debug.LogWarning($"[AudioSyncManager] Slot {slotIndex}: CharacterSoundData is null.");
            return false;
        }

        bool party = overridePartyMode ?? _isPartyModeActive;
        AudioClip first = soundData.GetLoopClip(party, 0);
        if (first == null)
        {
            Debug.LogWarning($"[AudioSyncManager] Character '{soundData.DisplayName}' has no AudioClip assigned.");
            return false;
        }

        SlotAudioState state = BeginRegistration(slotIndex, source);
        state.SoundData = soundData;
        state.BaseVolume = soundData.DefaultVolume;

        int bars = ClassifyBars(first, soundData.GetLoopClip(party, 1), soundData.LoopBeats);
        return FinishRegistration(state, bars);
    }

    public bool PlaySyncedAudioClip(int slotIndex, AudioSource source, AudioClip clip, int explicitBeats = 0, float volume = 1f)
    {
        if (clip == null) return false;

        SlotAudioState state = BeginRegistration(slotIndex, source);
        state.FixedClip = clip;
        state.BaseVolume = Mathf.Clamp01(volume);

        int bars = ClassifyBars(clip, null, explicitBeats);
        return FinishRegistration(state, bars);
    }

    public void EnsureClockStartedForSilentSlot()
    {
        if (!_isClockRunning) StartMasterClock(AudioSettings.dspTime + scheduleLeadTimeSeconds);
    }

    /// <summary>
    /// Call before registering several characters at once (Auto-Mix / DJ intro pair): they all start together on the first downbeat
    /// instead of the first one singing alone while the rest queue for the next bar.
    /// </summary>
    public void BeginBatchStart(double leadSeconds = 0.12)
    {
        if (_activeSlots.Count == 0)
        {
            StartMasterClock(AudioSettings.dspTime + scheduleLeadTimeSeconds + Math.Max(0.05, leadSeconds));
        }
    }

    public void StopSlot(int slotIndex)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState state)) return;

        StopAndCleanup(state);
        _slotsByIndex.Remove(slotIndex);
        _activeSlots.Remove(state);

        if (_activeSlots.Count == 0 && resetClockWhenAllSlotsEmpty)
        {
            _isClockRunning = false;
            _lastDispatchedAbsoluteBeat = -1;
        }
        else
        {
            RefreshAllSlotVolumes();
        }

        OnSlotPlaybackChanged?.Invoke(slotIndex, false);
    }

    public void StopAllSlots(bool resetClock = true, bool resetPartyMode = false)
    {
        for (int i = _activeSlots.Count - 1; i >= 0; i--)
        {
            SlotAudioState state = _activeSlots[i];
            StopAndCleanup(state);
            OnSlotPlaybackChanged?.Invoke(state.SlotIndex, false);
        }

        _activeSlots.Clear();
        _slotsByIndex.Clear();
        _sourceToSlotIndex.Clear();

        if (resetClock)
        {
            _isClockRunning = false;
            _lastDispatchedAbsoluteBeat = -1;
        }

        if (resetPartyMode) SetPartyModeActive(false);
    }

    /// <summary>Party Mode swaps stems on the next phrase boundary - a phrase that is playing is never cut.</summary>
    public void SetPartyModeActive(bool partyActive)
    {
        if (_isPartyModeActive == partyActive) return;
        _isPartyModeActive = partyActive;
        OnPartyModeChanged?.Invoke(partyActive);
    }

    public void SetSlotMuted(int slotIndex, bool muted)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState state)) return;
        state.IsMuted = muted;
        RefreshAllSlotVolumes();
    }

    public void SetSlotSolo(int slotIndex, bool solo)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState state)) return;
        state.IsSolo = solo;
        RefreshAllSlotVolumes();
    }

    public void ClearAllSolo()
    {
        for (int i = 0; i < _activeSlots.Count; i++) _activeSlots[i].IsSolo = false;
        RefreshAllSlotVolumes();
    }

    public bool IsSlotPlaying(int slotIndex) => _slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) && s.Started;
    public bool IsSlotQueued(int slotIndex) => _slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) && !s.Started;
    public bool IsSlotMuted(int slotIndex) => _slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) && s.IsMuted;
    public bool IsSlotSolo(int slotIndex) => _slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) && s.IsSolo;

    /// <summary>0..1 progress of a queued slot toward its downbeat (1 = about to drop).</summary>
    public float GetQueuedProgress01(int slotIndex)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) || s.Started) return 0f;
        double total = s.FirstStartDsp - s.RegisteredDsp;
        if (total <= 0.001) return 1f;
        return Mathf.Clamp01((float)((AudioSettings.dspTime - s.RegisteredDsp) / total));
    }

    public float GetQueuedSecondsRemaining(int slotIndex)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) || s.Started) return 0f;
        return Mathf.Max(0f, (float)(s.FirstStartDsp - AudioSettings.dspTime));
    }

    /// <summary>
    /// Normalised (0..1, relative to the clip peak) RMS loudness of what the slot is playing RIGHT NOW,
    /// so the mouth can close while the stem breathes. Returns 1 when the envelope is unavailable.
    /// </summary>
    public float GetSlotLoudness01(int slotIndex)
    {
        if (!_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState s) || !s.Started || !s.Current.Valid || s.Current.Clip == null)
        {
            return 0f;
        }

        float[] env = GetEnvelope(s.Current.Clip);
        if (env == null || env.Length == 0) return 1f;

        double pos = AudioSettings.dspTime - s.Current.StartDsp;
        if (pos < 0.0) return 0f;
        int idx = (int)(pos / envelopeWindowSeconds);
        if (idx >= env.Length) return 0f;
        float v = env[idx];
        if (idx > 0) v = Mathf.Max(v, env[idx - 1] * 0.85f);
        return v;
    }

    // ------------------------------------------------------------------ internals

    private SlotAudioState BeginRegistration(int slotIndex, AudioSource providedSource)
    {
        AudioSource preservedAlternate = null;

        if (_slotsByIndex.TryGetValue(slotIndex, out SlotAudioState existing))
        {
            preservedAlternate = existing.AlternateSource;
            if (existing.PrimarySource != null)
            {
                existing.PrimarySource.Stop();
                if (existing.OwnsPrimaryObject && existing.PrimarySource != providedSource)
                {
                    Destroy(existing.PrimarySource.gameObject);
                }
            }
            if (existing.AlternateSource != null) existing.AlternateSource.Stop();

            _activeSlots.Remove(existing);
            _slotsByIndex.Remove(slotIndex);
        }

        bool ownsPrimary = false;
        AudioSource primary = providedSource;
        if (primary == null)
        {
            primary = CreateManagedAudioSource(slotIndex);
            ownsPrimary = true;
        }

        primary.loop = false;
        primary.playOnAwake = false;

        return new SlotAudioState
        {
            SlotIndex = slotIndex,
            PrimarySource = primary,
            AlternateSource = preservedAlternate,
            OwnsPrimaryObject = ownsPrimary
        };
    }

    private bool FinishRegistration(SlotAudioState state, int bars)
    {
        double dspNow = AudioSettings.dspTime;
        double bar = BarDurationSeconds;

        state.PeriodBars = bars;
        state.PeriodSeconds = bar * bars;
        state.RegisteredDsp = dspNow;

        bool clockPending = _isClockRunning && _masterStartDspTime > dspNow;
        bool freshStart = !_isClockRunning || (_activeSlots.Count == 0 && !clockPending);
        double startDsp;
        if (freshStart)
        {
            StartMasterClock(dspNow + scheduleLeadTimeSeconds);
            startDsp = _masterStartDspTime;
        }
        else
        {
            // Next bar downbeat strictly after (now + lead). 2-bar stems wait for an even bar to stay on the 9.6s loop.
            long nextBar = (long)Math.Ceiling((dspNow + scheduleLeadTimeSeconds - _masterStartDspTime) / bar);
            if (nextBar < 0) nextBar = 0;
            if (bars >= 2 && (nextBar % 2L) != 0L) nextBar++;
            startDsp = _masterStartDspTime + nextBar * bar;
        }

        state.FirstStartDsp = startDsp;
        // Queued = waits for a LATER downbeat than the clock's very first one (batch / first-character starts are immediate).
        bool queued = (startDsp - _masterStartDspTime) > 0.001;

        _slotsByIndex[state.SlotIndex] = state;
        _activeSlots.Add(state);
        if (state.PrimarySource != null) _sourceToSlotIndex[state.PrimarySource] = state.SlotIndex;

        SchedulePhrase(state, startDsp);
        RefreshAllSlotVolumes();

        if (!queued)
        {
            // Immediate start: promote on the next AdvanceSlotPhrases tick without announcing a "drop".
            state.StartAnnounced = true;
        }

        OnSlotPlaybackChanged?.Invoke(state.SlotIndex, true);
        return queued;
    }

    private void SchedulePhrase(SlotAudioState state, double startDsp)
    {
        AudioClip clip = state.SoundData != null
            ? state.SoundData.GetLoopClip(_isPartyModeActive, (int)(state.PhrasesScheduled & 1L))
            : state.FixedClip;
        if (clip == null) clip = state.FixedClip ?? (state.SoundData != null ? state.SoundData.GetLoopClip(_isPartyModeActive, 0) : null);
        if (clip == null) return;

        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();

        AudioSource source;
        if (state.NextUsesAlternate)
        {
            if (state.AlternateSource == null) state.AlternateSource = CreateAlternateSource(state.PrimarySource, state.SlotIndex);
            source = state.AlternateSource;
        }
        else
        {
            source = state.PrimarySource;
        }

        if (source == null) return;

        source.Stop();
        source.clip = clip;
        source.loop = false;
        source.timeSamples = 0;
        source.PlayScheduled(startDsp);

        state.NextUsesAlternate = !state.NextUsesAlternate;
        state.PhrasesScheduled++;
        state.NextPhraseDsp = startDsp + state.PeriodSeconds;
        state.Pending = new PhraseInfo { Valid = true, StartDsp = startDsp, Clip = clip };
        RefreshAllSlotVolumes();
    }

    private void AdvanceSlotPhrases(double dspNow)
    {
        for (int i = 0; i < _activeSlots.Count; i++)
        {
            SlotAudioState s = _activeSlots[i];

            if (s.Pending.Valid && dspNow >= s.Pending.StartDsp)
            {
                s.Current = s.Pending;
                s.Pending = default;
                if (!s.Started)
                {
                    s.Started = true;
                    if (!s.StartAnnounced)
                    {
                        s.StartAnnounced = true;
                        OnSlotStarted?.Invoke(s.SlotIndex);
                    }
                }
            }

            if (!s.Pending.Valid && dspNow + phraseLookaheadSeconds >= s.NextPhraseDsp)
            {
                double next = s.NextPhraseDsp;
                while (next < dspNow + scheduleLeadTimeSeconds) next += s.PeriodSeconds; // frame-hitch guard
                SchedulePhrase(s, next);
            }
        }
    }

    private void DispatchBeatEvents(double dspNow)
    {
        double elapsed = dspNow - _masterStartDspTime;
        if (elapsed < 0.0) return;

        long currentAbsoluteBeat = (long)Math.Floor(elapsed / SecondsPerBeat);
        if (currentAbsoluteBeat <= _lastDispatchedAbsoluteBeat) return;

        long pending = currentAbsoluteBeat - _lastDispatchedAbsoluteBeat;
        long startBeat = currentAbsoluteBeat - Math.Min(pending, maxCatchUpBeatsPerFrame) + 1;
        int totalBeats = BeatsPerLoop;
        int barLength = Math.Max(1, beatsPerBar);

        for (long beat = startBeat; beat <= currentAbsoluteBeat; beat++)
        {
            int beatInLoop = (int)(beat % totalBeats);
            if (beatInLoop == 0) OnLoopRestart?.Invoke();
            if (beatInLoop % barLength == 0) OnBar?.Invoke(beatInLoop / barLength);

            OnBeatIndex?.Invoke(beatInLoop);
            OnBeat?.Invoke();
        }

        _lastDispatchedAbsoluteBeat = currentAbsoluteBeat;
    }

    /// <summary>Classifies a stem as 1 bar or 2 bars from its REAL measured length (tail included).</summary>
    private int ClassifyBars(AudioClip a, AudioClip b, int explicitBeats)
    {
        if (explicitBeats > 0)
        {
            return Mathf.Clamp(Mathf.CeilToInt(explicitBeats / (float)Math.Max(1, beatsPerBar)), 1, Mathf.Max(1, barsPerLoop));
        }

        double bar = BarDurationSeconds;
        double longest = Math.Max(a != null ? a.length : 0.0, b != null ? b.length : 0.0);
        int bars = (int)Math.Round(longest / bar);
        return Mathf.Clamp(bars, 1, Mathf.Max(1, barsPerLoop));
    }

    private float[] GetEnvelope(AudioClip clip)
    {
        if (clip == null) return null;
        if (_envelopes.TryGetValue(clip, out float[] cached)) return cached;

        float[] env = null;
        try
        {
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();

            int channels = Mathf.Max(1, clip.channels);
            float[] data = new float[clip.samples * channels];
            if (clip.GetData(data, 0))
            {
                int windowFrames = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * envelopeWindowSeconds));
                int windows = Mathf.CeilToInt(clip.samples / (float)windowFrames);
                env = new float[windows];
                float peak = 1e-6f;
                for (int w = 0; w < windows; w++)
                {
                    int f0 = w * windowFrames;
                    int f1 = Mathf.Min(clip.samples, f0 + windowFrames);
                    double sum = 0.0;
                    for (int f = f0; f < f1; f++)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            float v = data[f * channels + c];
                            sum += v * v;
                        }
                    }
                    float rms = Mathf.Sqrt((float)(sum / Math.Max(1, (f1 - f0) * channels)));
                    env[w] = rms;
                    if (rms > peak) peak = rms;
                }
                for (int w = 0; w < windows; w++) env[w] /= peak;
            }
        }
        catch (Exception)
        {
            env = null;
        }

        _envelopes[clip] = env;
        return env;
    }

    private void RefreshAllSlotVolumes()
    {
        bool anySolo = HasAnySoloedSlot;

        for (int i = 0; i < _activeSlots.Count; i++)
        {
            SlotAudioState state = _activeSlots[i];
            bool audible = !state.IsMuted && (!anySolo || state.IsSolo);
            float volume = audible ? state.BaseVolume * masterVolume : 0f;

            if (state.PrimarySource != null)
            {
                state.PrimarySource.mute = !audible;
                state.PrimarySource.volume = volume;
            }
            if (state.AlternateSource != null)
            {
                state.AlternateSource.mute = !audible;
                state.AlternateSource.volume = volume;
            }
        }
    }

    private AudioSource CreateManagedAudioSource(int slotIndex)
    {
        GameObject child = new GameObject($"SlotAudio_{slotIndex:D2}");
        child.transform.SetParent(transform, false);
        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        return source;
    }

    private AudioSource CreateAlternateSource(AudioSource primary, int slotIndex)
    {
        Transform parent = primary != null ? primary.transform : transform;
        GameObject child = new GameObject($"TailAudio_{slotIndex:D2}");
        child.transform.SetParent(parent, false);
        AudioSource alt = child.AddComponent<AudioSource>();
        alt.playOnAwake = false;
        alt.loop = false;
        alt.spatialBlend = 0f;
        if (primary != null) alt.outputAudioMixerGroup = primary.outputAudioMixerGroup;
        return alt;
    }

    private static void StopAndCleanup(SlotAudioState state)
    {
        if (state.PrimarySource != null)
        {
            state.PrimarySource.Stop();
            if (state.OwnsPrimaryObject) Destroy(state.PrimarySource.gameObject);
        }
        if (state.AlternateSource != null) state.AlternateSource.Stop();
    }
}
