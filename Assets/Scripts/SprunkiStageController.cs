using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SprunkiKids.Data;

/// <summary>
/// Controls Header UI (Auto Random 2-Minute DJ Showcase, Snowstorm Boost,
/// Santa's Magic toggle, Reset Stage, Beat Visualizer LEDs + circular progress pie),
/// Ghost Icon / Virtual DJ Hand tweening from IconTray to CharacterSlots,
/// snowy Christmas Stage lighting, Northern Lights Aurora ribbons, pulsing LED string lights,
/// and ambient Snowfall & Snowflake Fireworks VFX for "Sprunki: Christmas Kids Wonderland".
/// </summary>
public class SprunkiStageController : MonoBehaviour
{

    [Header("Core References")]
    [SerializeField] private AudioSyncManager syncManager;
    [SerializeField] private List<CharacterSlot> characterSlots = new List<CharacterSlot>();
    [SerializeField] private List<CharacterSoundData> availableCharacters = new List<CharacterSoundData>();

    [Header("Header UI Controls")]
    [SerializeField] private Button resetStageButton = null;
    [SerializeField] private Button partyModeButton = null;
    [SerializeField] private Image partyButtonBackground = null;
    [SerializeField] private Text partyButtonLabel = null;
    [SerializeField] private Text activeCountText = null;

    [Header("Auto Random 2-Minute DJ Showcase")]
    [SerializeField] private Button autoRandomButton = null;
    [SerializeField] private Image autoRandomButtonBackground = null;
    [SerializeField] private Text autoRandomButtonLabel = null;

    [SerializeField] private Button snowstormButton = null;
    [SerializeField] private Image snowstormButtonBackground = null;
    [SerializeField] private Text snowstormButtonLabel = null;

    [Header("DJ Ghost Icon (Virtual Hand Tween)")]
    [SerializeField] private RectTransform djGhostRoot = null;
    [SerializeField] private Image djGhostBadge = null;
    [SerializeField] private Image djGhostPortrait = null;
    [SerializeField] private Text djGhostTagText = null;

    [Header("DSP Beat Visualizer")]
    [SerializeField] private Image[] beatLeds = Array.Empty<Image>();
    [SerializeField] private Image loopProgressFill = null;
    [SerializeField] private Color ledIdleColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color ledNormalBeatColor = new Color(0.32f, 0.90f, 1f, 1f);
    [SerializeField] private Color ledPartyBeatColor = new Color(1f, 0.85f, 0.22f, 1f);

    [Header("Christmas Stage Backdrop & Aurora Magic VFX")]
    [SerializeField] private Image skyBaseImage = null;
    [SerializeField] private Image grassBaseImage = null;
    [SerializeField] private Image stageArtworkImage = null;
    [SerializeField] private Image partyRainbowOverlay = null;
    [SerializeField] private Image[] partySpotlights = Array.Empty<Image>();
    [SerializeField] private Image[] snowyHillImages = Array.Empty<Image>();
    [SerializeField] private Image[] pineTreeImages = Array.Empty<Image>();
    [SerializeField] private Image[] auroraRibbons = Array.Empty<Image>();
    [SerializeField] private Image[] christmasLedBulbs = Array.Empty<Image>();
    [SerializeField] private ParticleSystem partyConfettiParticles = null;
    [SerializeField] private ParticleSystem snowfallParticles = null;
    [SerializeField] private ParticleSystem snowflakeFireworksParticles = null;

    [Header("Winter Day -> Santa's Magic Aurora Palette")]
    [SerializeField] private Color normalSkyColor = new Color(0.08f, 0.14f, 0.34f, 1f);
    [SerializeField] private Color normalGrassColor = new Color(0.80f, 0.88f, 0.98f, 1f);
    [SerializeField] private Color partySkyBaseColor = new Color(0.04f, 0.08f, 0.22f, 1f);
    [SerializeField] private Color partyGrassTint = new Color(0.62f, 0.78f, 0.96f, 1f);
    [SerializeField] private Color normalSnowHillColor = new Color(0.88f, 0.93f, 1f, 1f);
    [SerializeField] private Color auroraSnowHillColor = new Color(0.68f, 0.82f, 0.98f, 1f);
    [SerializeField] private Color normalPineColor = new Color(0.90f, 0.98f, 0.95f, 1f);
    [SerializeField] private Color auroraPineColor = new Color(0.52f, 0.86f, 0.82f, 1f);

    [Header("Snowstorm Boost Tuning")]
    [SerializeField] private float normalSnowEmissionRate = 70f;
    [SerializeField] private float boostSnowEmissionRate = 260f;

    [Header("Kid-Friendly Magic Trigger Rules")]
    [Tooltip("Automatically triggers Santa's Magic Mode when all 7 singers fill the stage.")]
    [SerializeField, Min(2)] private int partyComboSingerCount = 7;
    [SerializeField] private bool autoTriggerPartyOnFullStage = true;

    [Header("UI SFX")]
    [SerializeField] private AudioSource uiSfxSource = null;
    [SerializeField] private AudioClip uiClickClip = null;

    private static readonly Color[] FestiveBulbPalette =
    {
        new Color(1f, 0.24f, 0.28f, 1f), // Festive Red
        new Color(0.22f, 0.95f, 0.45f, 1f), // Holly Green
        new Color(1f, 0.84f, 0.18f, 1f), // Golden Bell
        new Color(0.30f, 0.88f, 1f, 1f) // Ice Cyan
    };


    private float[] _ledDecayLevels = Array.Empty<float>();
    private Vector3[] _pineBaseRotations = Array.Empty<Vector3>();
    private Vector2[] _auroraBasePositions = Array.Empty<Vector2>();
    private float _spotlightBeatPulse;
    private float _stageBeatPulse;
    private int _lastBeatIndex;
    private bool _isPartyMode;
    private bool _isSnowstormBoosted;
    private bool _userOverrodePartyOff;
    private int _previousActiveCount;
    private bool _previousHasTriggerItem;
    private bool _isSubscribed;
    private Vector3 _stageArtworkBaseScale = Vector3.one;

    private Coroutine _autoRandomCoroutine;
    private bool _isAutoRandomRunning;
    private float _autoRandomRemainingSeconds = 120f;

    // Anti-duplication state (Lần sau khác lần trước 100%)
    private readonly List<CharacterSoundData> _previousLineup = new List<CharacterSoundData>();
    private readonly CharacterSoundData[] _lastCharacterPerSlot = new CharacterSoundData[7];

    public bool IsSnowstormBoosted => _isSnowstormBoosted;
    public bool IsSantaMagicActive => _isPartyMode;
    public bool IsAutoRandomRunning => _isAutoRandomRunning;
    public bool IsAutoPlayDjRunning => _isAutoRandomRunning; // Backward compat
    public float AutoRandomRemainingSeconds => _autoRandomRemainingSeconds;

    private void Awake()
    {
        if (syncManager == null)
        {
            syncManager = AudioSyncManager.Instance != null
                ? AudioSyncManager.Instance
                : FindFirstObjectByType<AudioSyncManager>();
        }

        if (characterSlots == null || characterSlots.Count == 0)
        {
            characterSlots = new List<CharacterSlot>(FindObjectsByType<CharacterSlot>(FindObjectsSortMode.None));
            characterSlots.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        }

        if (stageArtworkImage != null)
        {
            _stageArtworkBaseScale = stageArtworkImage.rectTransform.localScale;
        }

        CacheChristmasStageTransforms();
        _ledDecayLevels = new float[beatLeds != null ? beatLeds.Length : 0];

        if (djGhostRoot != null)
        {
            djGhostRoot.gameObject.SetActive(false);
        }

        WireHeaderButtons();
    }

    private void OnEnable()
    {
        CharacterSlot.OnAnySlotChanged += HandleAnySlotChanged;
        CharacterSlot.OnUserInteracted += HandleUserInteractedWithSlot;
        DraggableIcon.OnUserDragStarted += HandleUserBeganDraggingIcon;
        TrySubscribeSync();
    }

    private void Start()
    {
        TrySubscribeSync();
        ApplyPartyVisualsImmediate(_isPartyMode);
        ApplySnowstormState(_isSnowstormBoosted, updateSlots: true);
        ApplyAutoRandomVisuals();
        RefreshActiveCountLabel(CountActiveSlots());

        if (snowfallParticles != null && !snowfallParticles.isPlaying)
        {
            snowfallParticles.Play(true);
        }
    }

    private void OnDisable()
    {
        CharacterSlot.OnAnySlotChanged -= HandleAnySlotChanged;
        CharacterSlot.OnUserInteracted -= HandleUserInteractedWithSlot;
        DraggableIcon.OnUserDragStarted -= HandleUserBeganDraggingIcon;
        StopAutoRandomInternal();
        UnsubscribeSync();
    }

    private void Update()
    {
        UpdateBeatVisualizerVisuals();
        UpdateChristmasAtmosphere();

        if (_isAutoRandomRunning)
        {
            _autoRandomRemainingSeconds -= Time.deltaTime;
            if (_autoRandomRemainingSeconds <= 0f)
            {
                _autoRandomRemainingSeconds = 0f;
                StopAutoRandomInternal();
                RefreshActiveCountLabel(CountActiveSlots());
            }
            else
            {
                UpdateAutoRandomTimerDisplay();
            }
        }
    }

    // ------------------------------------------------------------------ Auto Random 2-Minute DJ Showcase API

    public void ToggleAutoRandom2Min()
    {
        PlayUiClickSfx();
        if (_isAutoRandomRunning)
        {
            StopAutoRandomInternal();
            RefreshActiveCountLabel(CountActiveSlots());
            return;
        }

        StartAutoRandom2Min();
    }

    public void StartAutoRandom2Min()
    {
        StopAutoRandomInternal();
        _isAutoRandomRunning = true;
        _autoRandomRemainingSeconds = 120f;
        ApplyAutoRandomVisuals();
        _autoRandomCoroutine = StartCoroutine(RunAutoRandom2MinRoutine());
    }

    // Backward compatibility redirects
    [Obsolete("Use ToggleAutoRandom2Min()")]
    public void ToggleAutoPlayShowcase1To3() => ToggleAutoRandom2Min();
    [Obsolete("Use ToggleAutoRandom2Min()")]
    public void AutoMixJingleGroove() => ToggleAutoRandom2Min();
    [Obsolete("Use ToggleAutoRandom2Min()")]
    public void AutoMixFrostyBeat() => ToggleAutoRandom2Min();
    [Obsolete("Use ToggleAutoRandom2Min()")]
    public void AutoMixSantasParty() => ToggleAutoRandom2Min();

    private void StopAutoRandomInternal()
    {
        if (_autoRandomCoroutine != null)
        {
            StopCoroutine(_autoRandomCoroutine);
            _autoRandomCoroutine = null;
        }
        _isAutoRandomRunning = false;
        _autoRandomRemainingSeconds = 120f;

        if (djGhostRoot != null)
        {
            djGhostRoot.gameObject.SetActive(false);
        }

        ApplyAutoRandomVisuals();
    }

    private void HandleUserBeganDraggingIcon(DraggableIcon icon)
    {
        if (!_isAutoRandomRunning) return;
        // Player grabbed an icon manually -> immediately hand full control over to the player!
        StopAutoRandomInternal();
        RefreshActiveCountLabel(CountActiveSlots());
    }

    private void HandleUserInteractedWithSlot(CharacterSlot slot)
    {
        if (!_isAutoRandomRunning) return;
        StopAutoRandomInternal();
        RefreshActiveCountLabel(CountActiveSlots());
    }

    private IEnumerator RunAutoRandom2MinRoutine()
    {
        List<CharacterSoundData> pool = ResolveCharacterPool();
        if (pool.Count == 0 || characterSlots == null || characterSlots.Count == 0)
        {
            StopAutoRandomInternal();
            yield break;
        }

        EnsureDjGhostVisualReady();
        ClearStageForNextDjSong();
        yield return new WaitForSeconds(0.18f);

        int waveNumber = 1;
        SoundCategory[] slotCategories = new SoundCategory[]
        {
            SoundCategory.Beat,
            SoundCategory.Beat,
            SoundCategory.Effect,
            SoundCategory.Melody,
            SoundCategory.Melody,
            SoundCategory.Voice,
            SoundCategory.Voice
        };

        // --- Wave 1: Initial 7-character assembly ---
        if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

        // Batch-start so slot 0 & slot 1 lock precisely into the upcoming downbeat
        if (syncManager != null)
        {
            syncManager.BeginBatchStart(0.95);
        }

        var currentLineup = new List<CharacterSoundData>();
        var currentOnStage = new HashSet<CharacterSoundData>();

        for (int slotIdx = 0; slotIdx < characterSlots.Count; slotIdx++)
        {
            if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

            CharacterSlot targetSlot = characterSlots[slotIdx];
            if (targetSlot == null) continue;

            SoundCategory cat = (slotIdx < slotCategories.Length) ? slotCategories[slotIdx] : SoundCategory.Melody;
            CharacterSoundData chosen = PickAntiDuplicateCharacter(pool, slotIdx, currentOnStage, cat);
            if (chosen == null) continue;

            currentOnStage.Add(chosen);
            currentLineup.Add(chosen);

            float flightDuration = slotIdx < 2 ? 0.42f : 0.54f;
            yield return AnimateGhostIconDragToSlot(chosen, targetSlot, waveNumber, flightDuration);

            if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

            targetSlot.SetSnowstormBoost(_isSnowstormBoosted);
            targetSlot.ActivateCharacter(chosen);
            _lastCharacterPerSlot[slotIdx] = chosen;
            RefreshActiveCountLabel(CountActiveSlots());

            float pause = slotIdx == 0 ? 0.06f : 1.15f;
            yield return WaitForSecondsOrCancel(pause);
        }

        _previousLineup.Clear();
        _previousLineup.AddRange(currentLineup);

        // --- Continuous 120-second loop ---
        while (_isAutoRandomRunning && _autoRandomRemainingSeconds > 0f)
        {
            waveNumber++;

            // Wait for queued singers to begin singing cleanly on the bar boundary
            float waitQueued = 0f;
            while (_isAutoRandomRunning && _autoRandomRemainingSeconds > 0f && AnySlotCurrentlyQueued() && waitQueued < 5.0f)
            {
                waitQueued += Time.deltaTime;
                yield return null;
            }

            if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

            // Let the current full groove play for 1 to 1.5 loop cycles (~10 - 14 seconds) to enjoy the music
            double loopSeconds = syncManager != null ? syncManager.LoopDurationSeconds : 9.6;
            float groovePlayDuration = Mathf.Clamp((float)(loopSeconds * 1.25), 10f, 14f);
            float elapsedGroove = 0f;
            while (_isAutoRandomRunning && _autoRandomRemainingSeconds > 0f && elapsedGroove < groovePlayDuration)
            {
                elapsedGroove += Time.deltaTime;
                yield return null;
            }

            if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

            // If time is running out (less than 8s left), let current song play out to the 00:00 mark
            if (_autoRandomRemainingSeconds < 8.0f)
            {
                while (_isAutoRandomRunning && _autoRandomRemainingSeconds > 0f)
                {
                    yield return null;
                }
                break;
            }

            // Alternate between:
            // Odd wave: Partial swap (tháo bớt 2-4 nhân vật cũ và kéo 2-4 nhân vật mới)
            // Even wave: Full breakdown & complete new lineup
            bool doFullRefresh = (waveNumber % 2 == 0);

            if (doFullRefresh)
            {
                // Full rhythmic breakdown
                int[] breakdownOrder = { 6, 5, 4, 2, 1, 3, 0 };
                for (int b = 0; b < breakdownOrder.Length; b++)
                {
                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;
                    int sIdx = breakdownOrder[b];
                    if (sIdx >= 0 && sIdx < characterSlots.Count && characterSlots[sIdx] != null && characterSlots[sIdx].IsActive)
                    {
                        characterSlots[sIdx].ResetSlot();
                        PlayUiClickSfx();
                        yield return WaitForSecondsOrCancel(0.18f);
                    }
                }

                yield return WaitForSecondsOrCancel(0.35f);
                if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

                // Re-build all 7 slots with brand new random lineup
                if (syncManager != null)
                {
                    syncManager.BeginBatchStart(0.95);
                }

                currentLineup.Clear();
                currentOnStage.Clear();

                for (int slotIdx = 0; slotIdx < characterSlots.Count; slotIdx++)
                {
                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

                    CharacterSlot targetSlot = characterSlots[slotIdx];
                    if (targetSlot == null) continue;

                    SoundCategory cat = (slotIdx < slotCategories.Length) ? slotCategories[slotIdx] : SoundCategory.Melody;
                    CharacterSoundData chosen = PickAntiDuplicateCharacter(pool, slotIdx, currentOnStage, cat);
                    if (chosen == null) continue;

                    currentOnStage.Add(chosen);
                    currentLineup.Add(chosen);

                    float flightDuration = slotIdx < 2 ? 0.42f : 0.54f;
                    yield return AnimateGhostIconDragToSlot(chosen, targetSlot, waveNumber, flightDuration);

                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

                    targetSlot.SetSnowstormBoost(_isSnowstormBoosted);
                    targetSlot.ActivateCharacter(chosen);
                    _lastCharacterPerSlot[slotIdx] = chosen;
                    RefreshActiveCountLabel(CountActiveSlots());

                    float pause = slotIdx == 0 ? 0.06f : 1.15f;
                    yield return WaitForSecondsOrCancel(pause);
                }

                _previousLineup.Clear();
                _previousLineup.AddRange(currentLineup);
            }
            else
            {
                // Partial swap: Pick 3 slots to replace (slots 2, 4, 6 or 1, 3, 5)
                int[] slotsToSwap = (waveNumber % 4 == 1) ? new int[] { 2, 4, 6 } : new int[] { 1, 3, 5 };

                // First, remove old characters from these slots
                for (int s = 0; s < slotsToSwap.Length; s++)
                {
                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;
                    int sIdx = slotsToSwap[s];
                    if (sIdx >= 0 && sIdx < characterSlots.Count && characterSlots[sIdx] != null)
                    {
                        CharacterSoundData oldChar = characterSlots[sIdx].ActiveData;
                        if (oldChar != null)
                        {
                            currentOnStage.Remove(oldChar);
                        }
                        characterSlots[sIdx].ResetSlot();
                        PlayUiClickSfx();
                        yield return WaitForSecondsOrCancel(0.20f);
                    }
                }

                yield return WaitForSecondsOrCancel(0.25f);

                // Now drop in new characters into these swapped slots
                for (int s = 0; s < slotsToSwap.Length; s++)
                {
                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;
                    int sIdx = slotsToSwap[s];
                    if (sIdx < 0 || sIdx >= characterSlots.Count) continue;

                    CharacterSlot targetSlot = characterSlots[sIdx];
                    if (targetSlot == null) continue;

                    SoundCategory cat = (sIdx < slotCategories.Length) ? slotCategories[sIdx] : SoundCategory.Melody;
                    CharacterSoundData chosen = PickAntiDuplicateCharacter(pool, sIdx, currentOnStage, cat);
                    if (chosen == null) continue;

                    currentOnStage.Add(chosen);

                    yield return AnimateGhostIconDragToSlot(chosen, targetSlot, waveNumber, 0.50f);

                    if (!_isAutoRandomRunning || _autoRandomRemainingSeconds <= 0f) yield break;

                    targetSlot.SetSnowstormBoost(_isSnowstormBoosted);
                    targetSlot.ActivateCharacter(chosen);
                    _lastCharacterPerSlot[sIdx] = chosen;
                    RefreshActiveCountLabel(CountActiveSlots());

                    yield return WaitForSecondsOrCancel(1.10f);
                }

                // Update currentLineup and _previousLineup with currently active stage characters
                currentLineup.Clear();
                for (int i = 0; i < characterSlots.Count; i++)
                {
                    if (characterSlots[i] != null && characterSlots[i].ActiveData != null)
                    {
                        currentLineup.Add(characterSlots[i].ActiveData);
                    }
                }
                _previousLineup.Clear();
                _previousLineup.AddRange(currentLineup);
            }
        }

        // When 120 seconds are up:
        StopAutoRandomInternal();
        RefreshActiveCountLabel(CountActiveSlots());
    }

    private CharacterSoundData PickAntiDuplicateCharacter(
        List<CharacterSoundData> pool,
        int slotIndex,
        HashSet<CharacterSoundData> currentOnStage,
        SoundCategory preferredCategory)
    {
        CharacterSoundData lastForSlot = (slotIndex >= 0 && slotIndex < _lastCharacterPerSlot.Length)
            ? _lastCharacterPerSlot[slotIndex]
            : null;

        // 1. Candidates that are NOT on stage right now AND NOT the character just removed from this slot
        var eligible = new List<CharacterSoundData>();
        for (int i = 0; i < pool.Count; i++)
        {
            CharacterSoundData c = pool[i];
            if (c == null) continue;
            if (currentOnStage.Contains(c)) continue;
            if (lastForSlot != null && c == lastForSlot) continue;
            eligible.Add(c);
        }

        // Fallback: relax lastForSlot if eligible is empty
        if (eligible.Count == 0)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                CharacterSoundData c = pool[i];
                if (c != null && !currentOnStage.Contains(c))
                {
                    eligible.Add(c);
                }
            }
        }

        if (eligible.Count == 0) return null;

        // 2. Partition into Group A (Fresh - not in _previousLineup) and Group B (Repeats from _previousLineup)
        var freshGroup = new List<CharacterSoundData>();
        var repeatGroup = new List<CharacterSoundData>();

        for (int i = 0; i < eligible.Count; i++)
        {
            CharacterSoundData c = eligible[i];
            if (_previousLineup.Count > 0 && !_previousLineup.Contains(c))
            {
                freshGroup.Add(c);
            }
            else
            {
                repeatGroup.Add(c);
            }
        }

        // Priority: use freshGroup if available to rotate through characters not yet seen
        List<CharacterSoundData> candidatePool = (freshGroup.Count > 0) ? freshGroup : repeatGroup;

        // 3. Within candidatePool, prioritize preferredCategory for musical harmony
        var categoryMatches = new List<CharacterSoundData>();
        for (int i = 0; i < candidatePool.Count; i++)
        {
            if (candidatePool[i].Category == preferredCategory)
            {
                categoryMatches.Add(candidatePool[i]);
            }
        }

        if (categoryMatches.Count > 0)
        {
            return categoryMatches[UnityEngine.Random.Range(0, categoryMatches.Count)];
        }

        // Try category matches across any eligible
        for (int i = 0; i < eligible.Count; i++)
        {
            if (eligible[i].Category == preferredCategory)
            {
                categoryMatches.Add(eligible[i]);
            }
        }
        if (categoryMatches.Count > 0)
        {
            return categoryMatches[UnityEngine.Random.Range(0, categoryMatches.Count)];
        }

        return candidatePool[UnityEngine.Random.Range(0, candidatePool.Count)];
    }

    private IEnumerator WaitForSecondsOrCancel(float seconds)
    {
        float elapsed = 0f;
        while (_isAutoRandomRunning && _autoRandomRemainingSeconds > 0f && elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private bool AnySlotCurrentlyQueued()
    {
        if (characterSlots == null) return false;
        for (int i = 0; i < characterSlots.Count; i++)
        {
            if (characterSlots[i] != null && characterSlots[i].IsActive && characterSlots[i].IsQueued)
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerator AnimateGhostIconDragToSlot(
        CharacterSoundData character,
        CharacterSlot targetSlot,
        int songNumber,
        float duration)
    {
        EnsureDjGhostVisualReady();
        if (djGhostRoot == null || targetSlot == null)
        {
            yield break;
        }

        Canvas canvas = djGhostRoot.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : djGhostRoot.parent as RectTransform;
        if (canvasRect == null) yield break;

        DraggableIcon sourceIcon = FindTrayIconForCharacter(character);

        Vector2 startLocal = new Vector2(0f, -410f);
        if (sourceIcon != null)
        {
            RectTransform srcRect = sourceIcon.GetComponent<RectTransform>();
            startLocal = WorldToCanvasLocal(canvasRect, srcRect.TransformPoint(srcRect.rect.center));
            sourceIcon.transform.localScale = Vector3.one * 1.16f;
        }

        RectTransform slotRect = targetSlot.GetComponent<RectTransform>();
        Vector3 slotWorldTarget = slotRect.TransformPoint(new Vector3(0f, slotRect.rect.height * 0.44f, 0f));
        Vector2 endLocal = WorldToCanvasLocal(canvasRect, slotWorldTarget);

        if (djGhostBadge != null)
        {
            Color badgeCol = character.IsPartyTriggerItem
                ? new Color(1f, 0.84f, 0.08f, 0.98f)
                : character.ThemeColor;
            if (badgeCol.maxColorComponent < 0.2f) badgeCol = new Color(0.30f, 0.85f, 1f, 0.98f);
            badgeCol.a = 0.96f;
            djGhostBadge.color = badgeCol;
        }

        if (djGhostPortrait != null)
        {
            Sprite spr = character.IconSprite != null ? character.IconSprite : character.IdleSprite;
            djGhostPortrait.sprite = spr;
            djGhostPortrait.preserveAspect = true;
            djGhostPortrait.color = Color.white;
        }

        if (djGhostTagText != null)
        {
            djGhostTagText.text = $"🎲 AUTO #{songNumber}";
        }

        djGhostRoot.gameObject.SetActive(true);
        djGhostRoot.SetAsLastSibling();
        djGhostRoot.anchoredPosition = startLocal;
        djGhostRoot.localScale = Vector3.one * 0.92f;

        // Smooth curved Bezier arc from the bottom IconTray up to the stage slot
        Vector2 mid = (startLocal + endLocal) * 0.5f;
        float arcSide = (endLocal.x - startLocal.x) * -0.18f;
        Vector2 controlPoint = mid + new Vector2(arcSide, 135f);

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.15f, duration);
        while (elapsed < safeDuration)
        {
            if (!_isAutoRandomRunning)
            {
                if (sourceIcon != null) sourceIcon.transform.localScale = Vector3.one;
                djGhostRoot.gameObject.SetActive(false);
                yield break;
            }

            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / safeDuration);
            // Smooth cubic ease-in-out
            float t = u * u * (3f - 2f * u);

            float oneMinusT = 1f - t;
            Vector2 pos = (oneMinusT * oneMinusT * startLocal) +
                          (2f * oneMinusT * t * controlPoint) +
                          (t * t * endLocal);
            djGhostRoot.anchoredPosition = pos;

            float scale = Mathf.Lerp(1.02f, 1.24f, Mathf.Sin(u * Mathf.PI));
            djGhostRoot.localScale = new Vector3(scale, scale, 1f);
            float tilt = Mathf.Sin(u * Mathf.PI * 2f) * 7f;
            djGhostRoot.localRotation = Quaternion.Euler(0f, 0f, tilt);

            yield return null;
        }

        if (sourceIcon != null)
        {
            sourceIcon.transform.localScale = Vector3.one;
        }

        djGhostRoot.anchoredPosition = endLocal;
        djGhostRoot.localRotation = Quaternion.identity;
        djGhostRoot.gameObject.SetActive(false);
    }

    private static Vector2 WorldToCanvasLocal(RectTransform canvasRect, Vector3 worldPos)
    {
        Vector3 local3 = canvasRect.InverseTransformPoint(worldPos);
        return new Vector2(local3.x, local3.y);
    }

    private DraggableIcon FindTrayIconForCharacter(CharacterSoundData targetData)
    {
        if (targetData == null) return null;
        DraggableIcon[] icons = FindObjectsByType<DraggableIcon>(FindObjectsSortMode.None);
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] != null && !icons[i].IsSticker && icons[i].CharacterData == targetData)
            {
                return icons[i];
            }
        }
        return null;
    }

    private void EnsureDjGhostVisualReady()
    {
        if (djGhostRoot != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject ghostGo = new GameObject("DjGhostCursor", typeof(RectTransform), typeof(CanvasGroup));
        ghostGo.transform.SetParent(canvas.rootCanvas.transform, false);
        djGhostRoot = ghostGo.GetComponent<RectTransform>();
        djGhostRoot.anchorMin = new Vector2(0.5f, 0.5f);
        djGhostRoot.anchorMax = new Vector2(0.5f, 0.5f);
        djGhostRoot.pivot = new Vector2(0.5f, 0.5f);
        djGhostRoot.sizeDelta = new Vector2(114f, 114f);

        CanvasGroup cg = ghostGo.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
        cg.alpha = 0.96f;

        djGhostBadge = ghostGo.AddComponent<Image>();
        djGhostBadge.raycastTarget = false;
        djGhostBadge.color = new Color(1f, 0.84f, 0.22f, 0.96f);

        Outline ring = ghostGo.AddComponent<Outline>();
        ring.effectColor = new Color(1f, 1f, 1f, 0.92f);
        ring.effectDistance = new Vector2(3f, -3f);

        GameObject portraitGo = new GameObject("Portrait", typeof(RectTransform));
        portraitGo.transform.SetParent(ghostGo.transform, false);
        RectTransform pRect = portraitGo.GetComponent<RectTransform>();
        pRect.anchorMin = new Vector2(0.06f, 0.06f);
        pRect.anchorMax = new Vector2(0.94f, 0.94f);
        pRect.offsetMin = Vector2.zero;
        pRect.offsetMax = Vector2.zero;
        djGhostPortrait = portraitGo.AddComponent<Image>();
        djGhostPortrait.preserveAspect = true;
        djGhostPortrait.raycastTarget = false;

        GameObject tagGo = new GameObject("DjTag", typeof(RectTransform));
        tagGo.transform.SetParent(ghostGo.transform, false);
        RectTransform tRect = tagGo.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0.5f, 0f);
        tRect.anchorMax = new Vector2(0.5f, 0f);
        tRect.pivot = new Vector2(0.5f, 1f);
        tRect.anchoredPosition = new Vector2(0f, -4f);
        tRect.sizeDelta = new Vector2(104f, 26f);
        Image tagBg = tagGo.AddComponent<Image>();
        tagBg.color = new Color(0.06f, 0.12f, 0.28f, 0.94f);
        tagBg.raycastTarget = false;

        GameObject txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(tagGo.transform, false);
        RectTransform txtRect = txtGo.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;
        djGhostTagText = txtGo.AddComponent<Text>();
        djGhostTagText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        djGhostTagText.fontSize = 14;
        djGhostTagText.fontStyle = FontStyle.Bold;
        djGhostTagText.alignment = TextAnchor.MiddleCenter;
        djGhostTagText.color = new Color(1f, 0.92f, 0.36f, 1f);
        djGhostTagText.text = "🎧 DJ";
        djGhostTagText.raycastTarget = false;

        ghostGo.SetActive(false);
    }


    // ------------------------------------------------------------------ Stage & Party Controls

    public void TogglePartyMode()
    {
        PlayUiClickSfx();
        bool nextState = !_isPartyMode;
        _userOverrodePartyOff = !nextState;
        SetPartyMode(nextState);
    }

    public void SetPartyMode(bool enabled)
    {
        if (_isPartyMode == enabled) return;
        _isPartyMode = enabled;

        if (syncManager != null)
        {
            syncManager.SetPartyModeActive(enabled);
        }

        for (int i = 0; i < characterSlots.Count; i++)
        {
            if (characterSlots[i] != null)
            {
                characterSlots[i].RefreshVisualThemeForPartyMode(enabled);
            }
        }

        if (partyConfettiParticles != null)
        {
            if (enabled) partyConfettiParticles.Play(true);
            else partyConfettiParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (enabled && snowflakeFireworksParticles != null)
        {
            snowflakeFireworksParticles.Emit(18);
        }

        ApplyPartyButtonVisuals(enabled);
        RefreshActiveCountLabel(CountActiveSlots());
    }

    public void ToggleSnowstormBoost()
    {
        PlayUiClickSfx();
        ApplySnowstormState(!_isSnowstormBoosted, updateSlots: true);
    }

    public void SetSnowstormBoost(bool boosted)
    {
        ApplySnowstormState(boosted, updateSlots: true);
    }

    public void ResetEntireStage()
    {
        StopAutoRandomInternal();
        ClearStageForNextDjSong();
    }

    private void ClearStageForNextDjSong()
    {
        _userOverrodePartyOff = false;
        _previousActiveCount = 0;
        _previousHasTriggerItem = false;

        for (int i = 0; i < characterSlots.Count; i++)
        {
            if (characterSlots[i] != null && characterSlots[i].IsActive)
            {
                characterSlots[i].ResetSlot();
            }
        }

        if (syncManager != null)
        {
            syncManager.StopAllSlots(resetClock: true, resetPartyMode: true);
        }

        SetPartyMode(false);
        RefreshActiveCountLabel(0);

        for (int i = 0; i < _ledDecayLevels.Length; i++)
        {
            _ledDecayLevels[i] = 0f;
        }
    }

    private List<CharacterSoundData> ResolveCharacterPool()
    {
        if (availableCharacters != null && availableCharacters.Count > 0)
        {
            return availableCharacters;
        }

        var fallbackList = new List<CharacterSoundData>();
        var seen = new HashSet<CharacterSoundData>();
        DraggableIcon[] icons = FindObjectsByType<DraggableIcon>(FindObjectsSortMode.None);
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] != null && !icons[i].IsSticker && icons[i].CharacterData != null && seen.Add(icons[i].CharacterData))
            {
                fallbackList.Add(icons[i].CharacterData);
            }
        }
        fallbackList.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return fallbackList;
    }

    private static CharacterSoundData PickCharacterFromPool(
        List<CharacterSoundData> pool,
        string keyword,
        SoundCategory fallbackCategory,
        HashSet<CharacterSoundData> used)
    {
        if (!string.IsNullOrEmpty(keyword))
        {
            for (int i = 0; i < pool.Count; i++)
            {
                CharacterSoundData c = pool[i];
                if (c == null || used.Contains(c)) continue;

                if (fallbackCategory == SoundCategory.Special && c.IsPartyTriggerItem)
                {
                    return c;
                }

                if ((!string.IsNullOrEmpty(c.DisplayName) && c.DisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(c.CharacterId) && c.CharacterId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return c;
                }
            }
        }

        for (int i = 0; i < pool.Count; i++)
        {
            CharacterSoundData c = pool[i];
            if (c != null && !used.Contains(c) && c.Category == fallbackCategory)
            {
                return c;
            }
        }

        for (int i = 0; i < pool.Count; i++)
        {
            CharacterSoundData c = pool[i];
            if (c != null && !used.Contains(c))
            {
                return c;
            }
        }

        return null;
    }

    private void HandleResetClicked()
    {
        PlayUiClickSfx();
        ResetEntireStage();
    }

    private void HandleAnySlotChanged(CharacterSlot changedSlot)
    {
        int activeCount = 0;
        bool hasPartyTriggerItem = false;

        for (int i = 0; i < characterSlots.Count; i++)
        {
            CharacterSlot slot = characterSlots[i];
            if (slot != null && slot.IsActive)
            {
                activeCount++;
                if (slot.ActiveData != null && slot.ActiveData.IsPartyTriggerItem)
                {
                    hasPartyTriggerItem = true;
                }
            }
        }

        RefreshActiveCountLabel(activeCount);

        bool freshlyPlacedTrigger = hasPartyTriggerItem && !_previousHasTriggerItem;
        bool freshlyReachedCombo = autoTriggerPartyOnFullStage &&
                                   activeCount >= partyComboSingerCount &&
                                   _previousActiveCount < partyComboSingerCount;

        if (hasPartyTriggerItem)
        {
            if (freshlyPlacedTrigger)
            {
                _userOverrodePartyOff = false;
                SetPartyMode(true);
            }
        }
        else if (freshlyReachedCombo && !_userOverrodePartyOff)
        {
            SetPartyMode(true);
        }
        else if (activeCount == 0 && _isPartyMode)
        {
            _userOverrodePartyOff = false;
            SetPartyMode(false);
        }
        else if (!hasPartyTriggerItem && _previousHasTriggerItem && activeCount < partyComboSingerCount)
        {
            _userOverrodePartyOff = false;
            SetPartyMode(false);
        }

        _previousActiveCount = activeCount;
        _previousHasTriggerItem = hasPartyTriggerItem;
    }

    private void HandleBeatIndex(int beatIndex)
    {
        _lastBeatIndex = beatIndex;

        if (beatLeds != null && beatLeds.Length > 0)
        {
            int idx = Mathf.Abs(beatIndex) % beatLeds.Length;
            _ledDecayLevels[idx] = 1f;
        }

        _spotlightBeatPulse = 1f;
        _stageBeatPulse = 1f;

        if (_isPartyMode && snowflakeFireworksParticles != null && (beatIndex % 2 == 0))
        {
            int burstCount = _isSnowstormBoosted ? 22 : 14;
            snowflakeFireworksParticles.Emit(burstCount);
        }
    }

    private void HandlePartyChangedFromAudioManager(bool partyActive)
    {
        if (_isPartyMode != partyActive)
        {
            SetPartyMode(partyActive);
        }
    }

    private void UpdateBeatVisualizerVisuals()
    {
        bool clockRunning = syncManager != null && syncManager.IsClockRunning;

        if (loopProgressFill != null)
        {
            float progress = clockRunning ? syncManager.NormalizedLoopProgress : 0f;
            loopProgressFill.fillAmount = progress;
            loopProgressFill.color = _isPartyMode
                ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.35f, 1f), 0.85f, 1f)
                : ledNormalBeatColor;
        }

        if (beatLeds != null && beatLeds.Length > 0)
        {
            Color activeColor = _isPartyMode ? ledPartyBeatColor : ledNormalBeatColor;
            for (int i = 0; i < beatLeds.Length; i++)
            {
                if (_ledDecayLevels[i] > 0f)
                {
                    _ledDecayLevels[i] = Mathf.MoveTowards(_ledDecayLevels[i], 0f, Time.deltaTime * 2.4f);
                }

                Image led = beatLeds[i];
                if (led == null) continue;

                float level = clockRunning ? _ledDecayLevels[i] : 0f;
                led.color = Color.Lerp(ledIdleColor, activeColor, level);
                float scale = Mathf.Lerp(1f, 1.24f, level);
                led.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    private void UpdateChristmasAtmosphere()
    {
        float dt = Time.deltaTime;
        _spotlightBeatPulse = Mathf.MoveTowards(_spotlightBeatPulse, 0f, dt * 2.6f);
        _stageBeatPulse = Mathf.MoveTowards(_stageBeatPulse, 0f, dt * 4.0f);

        Color targetSky = normalSkyColor;
        Color targetGrass = normalGrassColor;
        Color targetStageTint = new Color(0.55f, 0.68f, 0.98f, 0.30f);
        Color targetSnowHill = normalSnowHillColor;
        Color targetPine = normalPineColor;

        if (_isPartyMode)
        {
            float auroraHue = 0.45f + Mathf.Sin(Time.time * 0.45f) * 0.12f;
            Color auroraGlow = Color.HSVToRGB(Mathf.Repeat(auroraHue, 1f), 0.72f, 0.58f);
            targetSky = Color.Lerp(partySkyBaseColor, auroraGlow, 0.38f);
            targetGrass = auroraSnowHillColor;
            targetSnowHill = Color.Lerp(auroraSnowHillColor, auroraGlow, 0.18f);
            targetPine = auroraPineColor;
            targetStageTint = new Color(0.32f, 0.52f, 0.88f, 0.28f);
        }

        if (skyBaseImage != null)
        {
            skyBaseImage.color = Color.Lerp(skyBaseImage.color, targetSky, dt * 4.2f);
        }

        if (grassBaseImage != null)
        {
            grassBaseImage.color = Color.Lerp(grassBaseImage.color, targetGrass, dt * 4.2f);
        }

        if (stageArtworkImage != null)
        {
            stageArtworkImage.color = Color.Lerp(stageArtworkImage.color, targetStageTint, dt * 4.2f);
            float scalePulse = _isPartyMode ? 1f + (_stageBeatPulse * 0.012f) : 1f;
            stageArtworkImage.rectTransform.localScale = _stageArtworkBaseScale * scalePulse;
        }

        if (snowyHillImages != null)
        {
            for (int i = 0; i < snowyHillImages.Length; i++)
            {
                if (snowyHillImages[i] != null)
                {
                    snowyHillImages[i].color = Color.Lerp(snowyHillImages[i].color, targetSnowHill, dt * 4.2f);
                }
            }
        }

        if (pineTreeImages != null)
        {
            float windStrength = _isSnowstormBoosted ? 3.8f : 1.1f;
            float windSpeed = _isSnowstormBoosted ? 4.4f : 1.5f;
            for (int i = 0; i < pineTreeImages.Length; i++)
            {
                Image pine = pineTreeImages[i];
                if (pine == null) continue;
                pine.color = Color.Lerp(pine.color, targetPine, dt * 4.2f);

                if (i < _pineBaseRotations.Length)
                {
                    float sway = Mathf.Sin((Time.time * windSpeed) + (i * 0.9f)) * windStrength;
                    pine.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _pineBaseRotations[i].z + sway);
                }
            }
        }

        if (auroraRibbons != null)
        {
            for (int i = 0; i < auroraRibbons.Length; i++)
            {
                Image ribbon = auroraRibbons[i];
                if (ribbon == null) continue;

                float hue = Mathf.Repeat((i == 0 ? 0.44f : 0.76f) + Mathf.Sin(Time.time * 0.35f + i) * 0.08f, 1f);
                Color auroraColor = Color.HSVToRGB(hue, 0.78f, 1f);
                auroraColor.a = _isPartyMode ? Mathf.Lerp(0.24f, 0.56f, _spotlightBeatPulse) : 0.16f;
                ribbon.color = Color.Lerp(ribbon.color, auroraColor, dt * 5f);
                ribbon.enabled = ribbon.color.a > 0.005f;

                if (i < _auroraBasePositions.Length)
                {
                    float driftX = Mathf.Sin((Time.time * 0.65f) + (i * 1.4f)) * 42f;
                    ribbon.rectTransform.anchoredPosition = _auroraBasePositions[i] + new Vector2(driftX, 0f);
                }
            }
        }

        if (partyRainbowOverlay != null)
        {
            float hue = Mathf.Repeat(0.48f + (Mathf.Sin(Time.time * 0.4f) * 0.14f), 1f);
            Color overlayTarget = Color.HSVToRGB(hue, 0.75f, 0.95f);
            overlayTarget.a = _isPartyMode ? Mathf.Lerp(0.08f, 0.22f, _spotlightBeatPulse) : 0f;
            partyRainbowOverlay.color = Color.Lerp(partyRainbowOverlay.color, overlayTarget, dt * 5f);
            partyRainbowOverlay.enabled = partyRainbowOverlay.color.a > 0.005f;
        }

        if (partySpotlights != null)
        {
            for (int i = 0; i < partySpotlights.Length; i++)
            {
                Image beam = partySpotlights[i];
                if (beam == null) continue;

                float phase = Time.time * 1.4f + (i * 1.8f);
                float angle = (i % 2 == 0 ? 18f : -18f) + Mathf.Sin(phase) * 14f;
                beam.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

                Color c = Color.HSVToRGB(Mathf.Repeat(0.42f + (i * 0.26f) + (Mathf.Sin(Time.time * 0.45f) * 0.1f), 1f), 0.80f, 1f);
                c.a = _isPartyMode ? Mathf.Lerp(0.18f, 0.42f, _spotlightBeatPulse) : 0f;
                beam.color = Color.Lerp(beam.color, c, dt * 6f);
                beam.enabled = beam.color.a > 0.005f;
            }
        }

        UpdateChristmasStringLights();
    }

    private void UpdateChristmasStringLights()
    {
        if (christmasLedBulbs == null || christmasLedBulbs.Length == 0) return;

        bool clockRunning = syncManager != null && syncManager.IsClockRunning;
        for (int i = 0; i < christmasLedBulbs.Length; i++)
        {
            Image bulb = christmasLedBulbs[i];
            if (bulb == null) continue;

            Color paletteColor = FestiveBulbPalette[i % FestiveBulbPalette.Length];
            float twinkle = 0.55f + 0.45f * Mathf.Sin((Time.time * (_isPartyMode ? 7.5f : 3.2f)) + (i * 0.85f));
            float beatBoost = (_isPartyMode && ((i + _lastBeatIndex) % 2 == 0))
                ? Mathf.Lerp(0.25f, 1f, _spotlightBeatPulse)
                : twinkle * (clockRunning ? 0.92f : 0.72f);

            Color finalColor = Color.Lerp(paletteColor * 0.48f, Color.Lerp(paletteColor, Color.white, 0.28f), beatBoost);
            finalColor.a = Mathf.Lerp(0.68f, 1f, beatBoost);
            bulb.color = finalColor;

            float pulseScale = Mathf.Lerp(0.88f, _isPartyMode ? 1.28f : 1.08f, beatBoost);
            bulb.rectTransform.localScale = new Vector3(pulseScale, pulseScale, 1f);
        }
    }

    private void ApplySnowstormState(bool boosted, bool updateSlots)
    {
        _isSnowstormBoosted = boosted;

        if (snowfallParticles != null)
        {
            ParticleSystem.EmissionModule emission = snowfallParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = boosted ? boostSnowEmissionRate : normalSnowEmissionRate;

            ParticleSystem.VelocityOverLifetimeModule vel = snowfallParticles.velocityOverLifetime;
            vel.enabled = true;
            vel.x = boosted
                ? new ParticleSystem.MinMaxCurve(-185f, -65f)
                : new ParticleSystem.MinMaxCurve(-28f, 28f);
            vel.y = boosted
                ? new ParticleSystem.MinMaxCurve(-390f, -230f)
                : new ParticleSystem.MinMaxCurve(-160f, -92f);

            if (!snowfallParticles.isPlaying)
            {
                snowfallParticles.Play(true);
            }
        }

        if (updateSlots && characterSlots != null)
        {
            for (int i = 0; i < characterSlots.Count; i++)
            {
                if (characterSlots[i] != null)
                {
                    characterSlots[i].SetSnowstormBoost(boosted);
                }
            }
        }

        ApplySnowstormButtonVisuals(boosted);
    }

    private void ApplyPartyVisualsImmediate(bool partyActive)
    {
        if (skyBaseImage != null) skyBaseImage.color = partyActive ? partySkyBaseColor : normalSkyColor;
        if (grassBaseImage != null) grassBaseImage.color = partyActive ? partyGrassTint : normalGrassColor;
        if (stageArtworkImage != null)
        {
            stageArtworkImage.color = partyActive
                ? new Color(0.32f, 0.52f, 0.88f, 0.28f)
                : new Color(0.55f, 0.68f, 0.98f, 0.30f);
        }
        if (snowyHillImages != null)
        {
            Color hillColor = partyActive ? auroraSnowHillColor : normalSnowHillColor;
            for (int i = 0; i < snowyHillImages.Length; i++)
            {
                if (snowyHillImages[i] != null) snowyHillImages[i].color = hillColor;
            }
        }
        if (pineTreeImages != null)
        {
            Color pineColor = partyActive ? auroraPineColor : normalPineColor;
            for (int i = 0; i < pineTreeImages.Length; i++)
            {
                if (pineTreeImages[i] != null) pineTreeImages[i].color = pineColor;
            }
        }
        if (auroraRibbons != null)
        {
            for (int i = 0; i < auroraRibbons.Length; i++)
            {
                if (auroraRibbons[i] != null)
                {
                    auroraRibbons[i].color = new Color(0.3f, 1f, 0.78f, partyActive ? 0.35f : 0.16f);
                    auroraRibbons[i].enabled = true;
                }
            }
        }
        if (partyRainbowOverlay != null)
        {
            partyRainbowOverlay.color = new Color(1f, 1f, 1f, 0f);
            partyRainbowOverlay.enabled = partyActive;
        }
        if (partySpotlights != null)
        {
            for (int i = 0; i < partySpotlights.Length; i++)
            {
                if (partySpotlights[i] != null)
                {
                    partySpotlights[i].color = new Color(1f, 1f, 1f, 0f);
                    partySpotlights[i].enabled = partyActive;
                }
            }
        }
        if (partyConfettiParticles != null && !partyActive)
        {
            partyConfettiParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        ApplyPartyButtonVisuals(partyActive);
    }

    private void ApplyPartyButtonVisuals(bool partyActive)
    {
        if (partyButtonBackground != null)
        {
            partyButtonBackground.color = partyActive
                ? new Color(1f, 0.78f, 0.16f, 1f)
                : new Color(0.18f, 0.64f, 0.44f, 1f);
        }

        if (partyButtonLabel != null)
        {
            partyButtonLabel.text = partyActive ? "Aurora ON!" : "Santa Magic";
        }
    }

    private void ApplySnowstormButtonVisuals(bool boosted)
    {
        if (snowstormButtonBackground != null)
        {
            snowstormButtonBackground.color = boosted
                ? new Color(0.24f, 0.86f, 1f, 1f)
                : new Color(0.26f, 0.56f, 0.86f, 1f);
        }

        if (snowstormButtonLabel != null)
        {
            snowstormButtonLabel.text = boosted ? "Blizzard ON" : "Snowstorm";
        }
    }

    private void ApplyAutoRandomVisuals()
    {
        if (autoRandomButtonBackground != null)
        {
            autoRandomButtonBackground.color = _isAutoRandomRunning
                ? new Color(0.18f, 0.72f, 0.52f, 1f)
                : new Color(0.95f, 0.50f, 0.12f, 1f);
        }
        UpdateAutoRandomTimerDisplay();
    }

    private void UpdateAutoRandomTimerDisplay()
    {
        if (autoRandomButtonLabel != null)
        {
            if (_isAutoRandomRunning)
            {
                int totalSec = Mathf.CeilToInt(Mathf.Max(0f, _autoRandomRemainingSeconds));
                int m = totalSec / 60;
                int s = totalSec % 60;
                autoRandomButtonLabel.text = $"⏹ Auto Random ({m:00}:{s:00})";
            }
            else
            {
                autoRandomButtonLabel.text = "▶ Auto Random (02:00)";
            }
            autoRandomButtonLabel.color = Color.white;
        }
    }

    private int CountActiveSlots()
    {
        int count = 0;
        if (characterSlots == null) return 0;
        for (int i = 0; i < characterSlots.Count; i++)
        {
            if (characterSlots[i] != null && characterSlots[i].IsActive) count++;
        }
        return count;
    }

    private void RefreshActiveCountLabel(int count)
    {
        if (activeCountText != null)
        {
            int maxCount = Mathf.Max(7, characterSlots.Count);
            if (_isAutoRandomRunning)
            {
                int totalSec = Mathf.CeilToInt(Mathf.Max(0f, _autoRandomRemainingSeconds));
                int m = totalSec / 60;
                int s = totalSec % 60;
                activeCountText.text = $"🎲 Auto ({m:00}:{s:00}) • {count}/{maxCount}";
            }
            else
            {
                activeCountText.text = _isPartyMode
                    ? $"{count}/{maxCount} SANTA MAGIC!"
                    : $"{count}/{maxCount} Singers";
            }
        }
    }

    private void CacheChristmasStageTransforms()
    {
        if (pineTreeImages != null && pineTreeImages.Length > 0)
        {
            _pineBaseRotations = new Vector3[pineTreeImages.Length];
            for (int i = 0; i < pineTreeImages.Length; i++)
            {
                if (pineTreeImages[i] != null)
                {
                    _pineBaseRotations[i] = pineTreeImages[i].rectTransform.localEulerAngles;
                }
            }
        }

        if (auroraRibbons != null && auroraRibbons.Length > 0)
        {
            _auroraBasePositions = new Vector2[auroraRibbons.Length];
            for (int i = 0; i < auroraRibbons.Length; i++)
            {
                if (auroraRibbons[i] != null)
                {
                    _auroraBasePositions[i] = auroraRibbons[i].rectTransform.anchoredPosition;
                }
            }
        }
    }

    private void WireHeaderButtons()
    {
        if (resetStageButton != null)
        {
            resetStageButton.onClick.RemoveListener(HandleResetClicked);
            resetStageButton.onClick.AddListener(HandleResetClicked);
        }
        if (partyModeButton != null)
        {
            partyModeButton.onClick.RemoveListener(TogglePartyMode);
            partyModeButton.onClick.AddListener(TogglePartyMode);
        }
        if (autoRandomButton != null)
        {
            autoRandomButton.onClick.RemoveListener(ToggleAutoRandom2Min);
            autoRandomButton.onClick.AddListener(ToggleAutoRandom2Min);
        }
        if (snowstormButton != null)
        {
            snowstormButton.onClick.RemoveListener(ToggleSnowstormBoost);
            snowstormButton.onClick.AddListener(ToggleSnowstormBoost);
        }
    }

    private void PlayUiClickSfx()
    {
        if (uiSfxSource != null && uiClickClip != null)
        {
            uiSfxSource.PlayOneShot(uiClickClip, 0.85f);
        }
    }

    private void TrySubscribeSync()
    {
        if (syncManager == null)
        {
            syncManager = AudioSyncManager.Instance ?? FindFirstObjectByType<AudioSyncManager>();
        }
        if (_isSubscribed || syncManager == null) return;

        syncManager.OnBeatIndex += HandleBeatIndex;
        syncManager.OnPartyModeChanged += HandlePartyChangedFromAudioManager;
        _isSubscribed = true;
    }

    private void UnsubscribeSync()
    {
        if (!_isSubscribed || syncManager == null) return;
        syncManager.OnBeatIndex -= HandleBeatIndex;
        syncManager.OnPartyModeChanged -= HandlePartyChangedFromAudioManager;
        _isSubscribed = false;
    }
}
