using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SprunkiKids.Data;

/// <summary>
/// One of the 7 interactive stage slots.
/// States: Empty (gray Polo that blinks) -> Queued (waiting for the next bar downbeat) -> Singing.
/// Controls (Mute / Solo / Remove) only appear on hover; a mouse click on the body removes the character like the original.
/// While singing, the mouth follows the real RMS loudness of the stem (breaths = closed mouth).
/// Every character wears a fitted Christmas headpiece on the top layer, and Mr. Tree transforms into a glowing Noel Tree
/// whose top star and colorful baubles pulse in sync with the beat!
/// </summary>
public class CharacterSlot : MonoBehaviour, IDropHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public static event Action<CharacterSlot> OnAnySlotChanged;
    /// <summary>Fired when the human player manually drops onto or clicks a slot (yields Auto-Play DJ to player).</summary>
    public static event Action<CharacterSlot> OnUserInteracted;

    private static readonly List<CharacterSlot> AllSlots = new List<CharacterSlot>(8);

    /// <summary>True while the given character currently occupies (or is queued on) any stage slot.</summary>
    public static bool IsCharacterOnStage(CharacterSoundData data)
    {
        if (data == null) return false;
        for (int i = 0; i < AllSlots.Count; i++)
        {
            CharacterSlot s = AllSlots[i];
            if (s != null && s._isActive && s._activeData == data) return true;
        }
        return false;
    }

    [Header("Slot Identity")]
    [SerializeField, Range(0, 6)] private int slotIndex;

    [Header("UI & Audio Components")]
    public Image characterImage;
    [Tooltip("Transform that performs Squash & Stretch (CharacterVisual).")]
    public RectTransform visualTransform;
    public AudioSource audioSource;
    public Sprite defaultIdleSprite;
    [SerializeField, Range(0.1f, 1f)] private float emptyAlpha = 1f;
    [SerializeField, Range(0.3f, 1f)] private float queuedAlpha = 0.75f;

    [Header("Empty Polo Blink (from Polo 1 costumes)")]
    [SerializeField] private Sprite emptyBlinkHalfSprite = null;
    [SerializeField] private Sprite emptyBlinkClosedSprite = null;

    [Header("Stage Juice (Glow Ring & Queue Pie)")]
    public Image glowRingImage;
    public Shadow characterShadow;
    [Tooltip("Radial pie that fills while the character waits for the next bar downbeat.")]
    public Image queuePieImage;
    [SerializeField] private Color emptyGlowColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField, Range(0.1f, 1f)] private float activeGlowBaseAlpha = 0.45f;
    [SerializeField, Range(0.2f, 1f)] private float activeGlowBeatPeakAlpha = 0.92f;

    [Header("Layer 2 Sticker Anchors")]
    public Image headAnchorImage;
    public Image eyesAnchorImage;
    public Image bodyAnchorImage;
    public Image handAnchorImage;

    [Header("Christmas Headpieces & Mr. Tree Noel Lights")]
    public Image christmasHatOverlay;
    public Image christmasScarfOverlay;
    public Sprite defaultChristmasHatSprite;
    public Sprite reindeerAntlersSprite;
    public Sprite elfHollyCapSprite;
    public Sprite defaultChristmasScarfSprite;
    [SerializeField] private bool showChristmasCostumeWhenEmpty = true;
    [SerializeField] private bool showChristmasCostumeWhenActive = true;

    [Tooltip("Overlay root inside CharacterVisual that lights up Mr. Tree's star & baubles on every beat.")]
    public RectTransform mrTreeNoelRoot;
    public Image mrTreeTopStarGlow;
    public Image[] mrTreeBaubleGlows = Array.Empty<Image>();

    [Header("Quick Controls (hover only)")]
    public GameObject controlsRoot;
    public Button muteButton;
    public Button soloButton;
    public Button removeButton;
    [SerializeField] private bool alwaysShowControlsWhenActive = false;
    [SerializeField] private bool removeOnMouseClick = true;

    [Header("Animation, Squash & Stretch + VFX")]
    [SerializeField] private ParticleSystem tapParticles = null;
    [SerializeField] private AudioSource sfxOneShotSource = null;
    [SerializeField] private AudioClip fallbackTapSfx = null;
    [SerializeField, Range(1f, 30f)] private float fallbackSingingFps = 10f;
    [SerializeField, Min(0.05f)] private float tweenDuration = 0.22f;
    [SerializeField, Range(0.02f, 0.25f)] private float beatBounceStrength = 0.10f;
    [SerializeField, Range(0.05f, 0.45f)] private float tapBounceStrength = 0.24f;
    [SerializeField, Range(0.02f, 0.4f)] private float mouthOpenLoudness = 0.14f;

    [SerializeField]
    private AnimationCurve stretchXCurve = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.22f, 1.18f), new Keyframe(0.58f, 0.94f), new Keyframe(1f, 1f));

    [SerializeField]
    private AnimationCurve stretchYCurve = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.22f, 0.84f), new Keyframe(0.58f, 1.12f), new Keyframe(1f, 1f));

    private static readonly Color[] NoelBaubleColors =
    {
        new Color(1f, 0.18f, 0.24f, 1f), // Ruby Red
        new Color(1f, 0.86f, 0.16f, 1f), // Gold
        new Color(0.16f, 0.80f, 1f, 1f), // Ice Blue
        new Color(0.75f, 0.28f, 1f, 1f), // Royal Purple
        new Color(0.16f, 0.80f, 1f, 1f), // Sapphire Blue
        new Color(1f, 0.18f, 0.24f, 1f), // Ruby Center
        new Color(1f, 0.86f, 0.16f, 1f), // Gold
        new Color(1f, 0.28f, 0.68f, 1f), // Magenta
        new Color(0.18f, 0.95f, 0.48f, 1f), // Emerald
    };

    private CharacterSoundData _activeData;
    private bool _isActive;
    private bool _isQueued;
    private bool _isMuted;
    private bool _isSolo;
    private bool _isSubscribedToSync;
    private bool _isPointerHovered;
    private bool _controlsPinnedOpen;
    private bool _isMrTreeActive;

    private int _singFrameCursor;
    private float _singFrameTimer;
    private float _voiceHold;
    private float _glowPulse;
    private bool _wasEffectivelyMuted;

    private float _nextBlinkTime;
    private float _blinkTimer = -1f;

    private Vector2 _hatBasePos = new Vector2(18f, 372f);
    private float _hatBaseRotZ = -9f;

    private Vector3 _visualBaseScale = Vector3.one;
    private Vector3 _glowBaseScale = Vector3.one;
    private Coroutine _bounceCoroutine;
    private float _externalBounceMultiplier = 1f;

    public int SlotIndex => slotIndex;
    /// <summary>Occupied (queued or singing).</summary>
    public bool IsActive => _isActive;
    public bool IsQueued => _isQueued;
    public bool IsMuted => _isMuted;
    public bool IsSolo => _isSolo;
    public CharacterSoundData ActiveData => _activeData;

    private RectTransform TargetVisualTransform =>
        visualTransform != null ? visualTransform : (characterImage != null ? characterImage.rectTransform : transform as RectTransform);

    private bool IsEffectivelyMuted
    {
        get
        {
            if (!_isActive || _isQueued) return true;
            if (_isMuted) return true;
            return AudioSyncManager.Instance != null && AudioSyncManager.Instance.HasAnySoloedSlot && !_isSolo;
        }
    }

    private void Awake()
    {
        if (characterImage == null) characterImage = GetComponentInChildren<Image>();
        if (visualTransform == null) visualTransform = characterImage != null ? characterImage.rectTransform : transform as RectTransform;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        _visualBaseScale = TargetVisualTransform != null ? TargetVisualTransform.localScale : Vector3.one;
        _glowBaseScale = glowRingImage != null ? glowRingImage.rectTransform.localScale : Vector3.one;

        WireControlButtons();
        HideAllStickers();
        ScheduleNextBlink();
    }

    private void Start()
    {
        ResetSlot();
        TrySubscribeAudioSync();
    }

    private void OnEnable()
    {
        if (!AllSlots.Contains(this)) AllSlots.Add(this);
        TrySubscribeAudioSync();
    }

    private void OnDisable()
    {
        AllSlots.Remove(this);
        UnsubscribeAudioSync();
        StopBounceImmediate();
    }

    private void OnDestroy()
    {
        AllSlots.Remove(this);
        UnsubscribeAudioSync();
    }

    private void Update()
    {
        AudioSyncManager sync = AudioSyncManager.Instance;

        if (!_isActive)
        {
            UpdateEmptyBlink();
            return;
        }

        bool partyMode = sync != null && sync.IsPartyModeActive;

        if (_isQueued)
        {
            if (sync != null && sync.IsSlotPlaying(slotIndex))
            {
                HandleSlotStarted(slotIndex);
            }
            else
            {
                UpdateQueuedVisuals(sync);
                return;
            }
        }

        bool effectivelyMuted = IsEffectivelyMuted;
        if (effectivelyMuted != _wasEffectivelyMuted)
        {
            _wasEffectivelyMuted = effectivelyMuted;
            UpdateMutedVisualTint(effectivelyMuted, partyMode);
        }

        float loudness = 0f;
        if (_activeData != null && characterImage != null)
        {
            Sprite idle = _activeData.GetIdleSprite(partyMode) ?? defaultIdleSprite;
            bool mouthOpen = false;

            if (!effectivelyMuted)
            {
                loudness = sync != null ? sync.GetSlotLoudness01(slotIndex) : 1f;
                if (loudness >= mouthOpenLoudness) _voiceHold = 0.08f;
                else _voiceHold -= Time.deltaTime;
                mouthOpen = _voiceHold > 0f;
            }

            if (!mouthOpen)
            {
                if (idle != null && characterImage.sprite != idle) characterImage.sprite = idle;
            }
            else
            {
                IReadOnlyList<Sprite> frames = _activeData.GetSingingFrames(partyMode);
                if (frames != null && frames.Count > 1)
                {
                    float fps = Mathf.Max(1f, _activeData.SingingFrameRate > 0f ? _activeData.SingingFrameRate : fallbackSingingFps);
                    float frameDuration = 1f / fps;
                    _singFrameTimer += Time.deltaTime;
                    if (_singFrameTimer >= frameDuration)
                    {
                        int advance = Mathf.FloorToInt(_singFrameTimer / frameDuration);
                        _singFrameTimer -= advance * frameDuration;
                        _singFrameCursor = (_singFrameCursor + advance) % frames.Count;
                    }
                    Sprite frameSprite = frames[_singFrameCursor % frames.Count];
                    if (frameSprite != null && characterImage.sprite != frameSprite) characterImage.sprite = frameSprite;
                }
                else
                {
                    Sprite target = (frames != null && frames.Count == 1 && frames[0] != null) ? frames[0] : idle;
                    if (target != null && characterImage.sprite != target) characterImage.sprite = target;
                }
            }
        }

        UpdateHatAndNoelTreeAnimation(effectivelyMuted, partyMode, loudness);

        if (glowRingImage != null)
        {
            _glowPulse = Mathf.MoveTowards(_glowPulse, 0f, Time.deltaTime * 2.8f);
            UpdateGlowRingVisual(effectivelyMuted, partyMode);
        }
    }

    public void SetSlotIndex(int index)
    {
        slotIndex = Mathf.Max(0, index);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isPointerHovered = true;
        RefreshControlsVisibility();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isPointerHovered = false;
        RefreshControlsVisibility();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerDrag == null) return;

        DraggableIcon icon = eventData.pointerDrag.GetComponent<DraggableIcon>();
        if (icon == null) return;

        OnUserInteracted?.Invoke(this);

        if (icon.IsSticker)
        {
            if (_isActive)
            {
                ApplySticker(icon.StickerAnchor, icon.ResolvedStickerSprite);
                TriggerInteractiveTapFeedback();
            }
            return;
        }

        if (icon.CharacterData != null) ActivateCharacter(icon.CharacterData);
        else ActivateCharacter(icon.ResolvedCharacterSprite, icon.ResolvedAudioClip);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_isActive) return;

        OnUserInteracted?.Invoke(this);

        if (eventData != null && eventData.pointerEnter != null)
        {
            Transform hit = eventData.pointerEnter.transform;
            if ((muteButton != null && hit.IsChildOf(muteButton.transform)) ||
                (soloButton != null && hit.IsChildOf(soloButton.transform)) ||
                (removeButton != null && hit.IsChildOf(removeButton.transform)))
            {
                return;
            }
        }

        bool isTouch = eventData != null && eventData.pointerId >= 0;
        if (!isTouch && removeOnMouseClick)
        {
            ResetSlot(); // original Sprunki: click the character to take it down
            return;
        }

        if (isTouch)
        {
            _controlsPinnedOpen = !_controlsPinnedOpen;
            RefreshControlsVisibility();
        }

        TriggerInteractiveTapFeedback();
    }

    /// <summary>Drops a full CharacterSoundData onto the slot; it is QUEUED if the stage is already singing.</summary>
    public void ActivateCharacter(CharacterSoundData data)
    {
        if (data == null) return;

        TrySubscribeAudioSync();
        BeginOccupy();
        _activeData = data;

        AudioSyncManager sync = AudioSyncManager.Instance;
        bool partyMode = sync != null && sync.IsPartyModeActive;
        bool queued = false;

        if (sync != null)
        {
            if (data.HasValidAudio && audioSource != null)
            {
                queued = sync.RegisterAndPlaySlot(slotIndex, audioSource, data, partyMode);
            }
            else
            {
                sync.StopSlot(slotIndex);
                sync.EnsureClockStartedForSilentSlot();
            }
        }

        _isQueued = queued;
        Sprite idle = data.GetIdleSprite(partyMode) ?? defaultIdleSprite;
        IReadOnlyList<Sprite> frames = data.GetSingingFrames(partyMode);
        Sprite initial = _isQueued ? idle : ((frames != null && frames.Count > 0 && frames[0] != null) ? frames[0] : idle);

        ApplyVisualState(initial, _isQueued ? new Color(1f, 1f, 1f, queuedAlpha) : Color.white);
        ApplyChristmasCostumes(data, true, emptySlot: false);
        RefreshQueuePie();
        UpdateGlowRingVisual(false, partyMode);
        RefreshControlsVisibility();
        UpdateControlButtonVisuals();

        if (_isQueued)
        {
            TriggerSquashAndStretch(0.08f);
            PlayTapVfxAndSfx(data, particles: false);
        }
        else
        {
            TriggerSquashAndStretch(tapBounceStrength);
            PlayTapVfxAndSfx(data);
        }

        OnAnySlotChanged?.Invoke(this);
    }

    public void ActivateCharacter(Sprite sprite, AudioClip audioClip)
    {
        if (audioClip == null && sprite == null) return;

        TrySubscribeAudioSync();
        BeginOccupy();
        _activeData = null;

        bool queued = false;
        if (AudioSyncManager.Instance != null && audioSource != null && audioClip != null)
        {
            queued = AudioSyncManager.Instance.PlaySyncedAudioClip(slotIndex, audioSource, audioClip);
        }

        _isQueued = queued;
        ApplyVisualState(sprite != null ? sprite : defaultIdleSprite, _isQueued ? new Color(1f, 1f, 1f, queuedAlpha) : Color.white);
        ApplyChristmasCostumes(null, true, emptySlot: false);
        RefreshQueuePie();
        UpdateGlowRingVisual(false, false);
        RefreshControlsVisibility();
        UpdateControlButtonVisuals();

        TriggerSquashAndStretch(_isQueued ? 0.08f : tapBounceStrength);
        PlayTapVfxAndSfx(null, particles: !_isQueued);
        OnAnySlotChanged?.Invoke(this);
    }

    private void BeginOccupy()
    {
        _isActive = true;
        _singFrameCursor = 0;
        _singFrameTimer = 0f;
        _voiceHold = 0.08f;
        _isMuted = false;
        _isSolo = false;
        _wasEffectivelyMuted = false;
        _controlsPinnedOpen = false;
        _glowPulse = 1f;
        _blinkTimer = -1f;
    }

    /// <summary>Called when the queued character reaches its downbeat: the BEAT DROP.</summary>
    private void HandleSlotStarted(int startedSlotIndex)
    {
        if (startedSlotIndex != slotIndex || !_isActive || !_isQueued) return;

        _isQueued = false;
        _singFrameCursor = 0;
        _singFrameTimer = 0f;
        _voiceHold = 0.1f;
        _glowPulse = 1f;
        _wasEffectivelyMuted = false;

        if (characterImage != null) characterImage.color = Color.white;
        if (christmasHatOverlay != null && christmasHatOverlay.enabled) christmasHatOverlay.color = Color.white;
        RefreshQueuePie();
        UpdateMutedVisualTint(IsEffectivelyMuted, AudioSyncManager.Instance != null && AudioSyncManager.Instance.IsPartyModeActive);
        TriggerSquashAndStretch(Mathf.Min(0.36f, tapBounceStrength * 1.4f));
        PlayTapVfxAndSfx(_activeData);
        OnAnySlotChanged?.Invoke(this);
    }

    public void ResetSlot()
    {
        bool wasOccupied = _isActive;
        _isActive = false;
        _isQueued = false;
        _activeData = null;
        _isMuted = false;
        _isSolo = false;
        _wasEffectivelyMuted = false;
        _controlsPinnedOpen = false;
        _isMrTreeActive = false;
        _singFrameCursor = 0;
        _singFrameTimer = 0f;
        _glowPulse = 0f;
        _blinkTimer = -1f;

        StopBounceImmediate();
        HideAllStickers();
        RefreshQueuePie();
        if (showChristmasCostumeWhenEmpty) ApplyChristmasCostumes(null, true, emptySlot: true);
        else HideChristmasCostumes();
        RefreshControlsVisibility();
        ApplyVisualState(defaultIdleSprite, new Color(1f, 1f, 1f, emptyAlpha));
        UpdateGlowRingVisual(true, false);
        ScheduleNextBlink();

        if (AudioSyncManager.Instance != null) AudioSyncManager.Instance.StopSlot(slotIndex);
        else if (audioSource != null) audioSource.Stop();

        if (wasOccupied || OnAnySlotChanged != null) OnAnySlotChanged?.Invoke(this);
    }

    public void ApplySticker(StickerAnchorType anchor, Sprite sticker)
    {
        if (sticker == null) return;

        Image target = anchor switch
        {
            StickerAnchorType.Head => headAnchorImage,
            StickerAnchorType.Eyes => eyesAnchorImage,
            StickerAnchorType.Body => bodyAnchorImage,
            StickerAnchorType.Hand => handAnchorImage,
            _ => headAnchorImage
        };

        if (target != null)
        {
            target.sprite = sticker;
            target.preserveAspect = true;
            target.enabled = true;
            target.color = Color.white;
            if (anchor == StickerAnchorType.Head && christmasHatOverlay != null) christmasHatOverlay.enabled = false;
        }
    }

    public void SetSnowstormBoost(bool boosted)
    {
        _externalBounceMultiplier = boosted ? 1.42f : 1f;
    }

    public void ToggleMute()
    {
        if (!_isActive) return;
        OnUserInteracted?.Invoke(this);
        _isMuted = !_isMuted;
        if (AudioSyncManager.Instance != null) AudioSyncManager.Instance.SetSlotMuted(slotIndex, _isMuted);
        UpdateControlButtonVisuals();
        UpdateMutedVisualTint(IsEffectivelyMuted, AudioSyncManager.Instance != null && AudioSyncManager.Instance.IsPartyModeActive);
        TriggerSquashAndStretch(beatBounceStrength);
    }

    public void ToggleSolo()
    {
        if (!_isActive) return;
        OnUserInteracted?.Invoke(this);
        _isSolo = !_isSolo;
        if (AudioSyncManager.Instance != null) AudioSyncManager.Instance.SetSlotSolo(slotIndex, _isSolo);
        UpdateControlButtonVisuals();
        UpdateMutedVisualTint(IsEffectivelyMuted, AudioSyncManager.Instance != null && AudioSyncManager.Instance.IsPartyModeActive);
        TriggerSquashAndStretch(beatBounceStrength);
    }

    public void TriggerInteractiveTapFeedback()
    {
        _glowPulse = 1f;
        TriggerSquashAndStretch(tapBounceStrength);
        PlayTapVfxAndSfx(_activeData, particles: !_isQueued);
    }

    public void RefreshVisualThemeForPartyMode(bool partyMode)
    {
        if (!_isActive || _activeData == null || _isQueued) return;
        UpdateMutedVisualTint(IsEffectivelyMuted, partyMode);
        UpdateGlowRingVisual(IsEffectivelyMuted, partyMode);
    }

    private void HandleBeat(int beatIndex)
    {
        if (!_isActive) return;

        if (_isQueued)
        {
            float progress = AudioSyncManager.Instance != null ? AudioSyncManager.Instance.GetQueuedProgress01(slotIndex) : 0f;
            TriggerSquashAndStretch(Mathf.Lerp(0.04f, 0.10f, progress));
            _glowPulse = 0.6f;
            return;
        }

        bool muted = IsEffectivelyMuted;
        _glowPulse = muted ? 0.25f : 1f;
        if (!muted) TriggerSquashAndStretch(beatBounceStrength);
    }

    private void HandleLoopRestart()
    {
        if (!_isActive || _isQueued) return;
        _singFrameCursor = 0;
        _singFrameTimer = 0f;
    }

    private void UpdateQueuedVisuals(AudioSyncManager sync)
    {
        float progress = sync != null ? sync.GetQueuedProgress01(slotIndex) : 0f;
        if (queuePieImage != null)
        {
            queuePieImage.fillAmount = progress;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (6f + progress * 10f));
            queuePieImage.color = new Color(1f, 0.86f, 0.28f, pulse);
        }

        if (glowRingImage != null)
        {
            Color c = _activeData != null ? _activeData.ThemeColor : new Color(1f, 0.86f, 0.28f, 1f);
            if (c.maxColorComponent < 0.18f) c = new Color(1f, 0.86f, 0.28f, 1f);
            c.a = Mathf.Lerp(0.30f, 0.95f, progress);
            glowRingImage.color = c;
            float s = Mathf.Lerp(0.9f, 1.32f, progress);
            glowRingImage.rectTransform.localScale = new Vector3(_glowBaseScale.x * s, _glowBaseScale.y * s, _glowBaseScale.z);
        }

        if (_isMrTreeActive)
        {
            UpdateHatAndNoelTreeAnimation(false, false, progress * 0.6f);
        }
    }

    private void RefreshQueuePie()
    {
        if (queuePieImage == null) return;
        queuePieImage.gameObject.SetActive(_isActive && _isQueued);
        queuePieImage.fillAmount = 0f;
    }

    private void ScheduleNextBlink()
    {
        _nextBlinkTime = Time.time + UnityEngine.Random.Range(2.2f, 5.0f);
        _blinkTimer = -1f;
    }

    private void UpdateEmptyBlink()
    {
        if (characterImage == null || defaultIdleSprite == null || emptyBlinkClosedSprite == null) return;

        if (_blinkTimer < 0f)
        {
            if (Time.time >= _nextBlinkTime) _blinkTimer = 0f;
            else return;
        }

        _blinkTimer += Time.deltaTime;
        Sprite frame;
        if (_blinkTimer < 0.07f) frame = emptyBlinkHalfSprite != null ? emptyBlinkHalfSprite : emptyBlinkClosedSprite;
        else if (_blinkTimer < 0.17f) frame = emptyBlinkClosedSprite;
        else if (_blinkTimer < 0.24f) frame = emptyBlinkHalfSprite != null ? emptyBlinkHalfSprite : defaultIdleSprite;
        else
        {
            characterImage.sprite = defaultIdleSprite;
            ScheduleNextBlink();
            return;
        }

        characterImage.sprite = frame;
    }

    private void TriggerSquashAndStretch(float intensity)
    {
        if (!gameObject.activeInHierarchy || TargetVisualTransform == null) return;

        float boosted = Mathf.Clamp(intensity * _externalBounceMultiplier, 0.04f, 0.36f);
        if (_bounceCoroutine != null) StopCoroutine(_bounceCoroutine);
        _bounceCoroutine = StartCoroutine(SquashAndStretchRoutine(boosted));
    }

    private IEnumerator SquashAndStretchRoutine(float intensity)
    {
        RectTransform target = TargetVisualTransform;
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, tweenDuration);
        float blend = Mathf.Clamp01(intensity / 0.24f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float cx = Mathf.Lerp(1f, stretchXCurve.Evaluate(t), blend);
            float cy = Mathf.Lerp(1f, stretchYCurve.Evaluate(t), blend);
            target.localScale = new Vector3(_visualBaseScale.x * cx, _visualBaseScale.y * cy, _visualBaseScale.z);
            yield return null;
        }

        target.localScale = _visualBaseScale;
        _bounceCoroutine = null;
    }

    private void StopBounceImmediate()
    {
        if (_bounceCoroutine != null)
        {
            StopCoroutine(_bounceCoroutine);
            _bounceCoroutine = null;
        }
        if (TargetVisualTransform != null) TargetVisualTransform.localScale = _visualBaseScale;
    }

    private void PlayTapVfxAndSfx(CharacterSoundData data, bool particles = true)
    {
        if (particles && tapParticles != null)
        {
            ParticleSystem.MainModule main = tapParticles.main;
            Color particleColor = data != null ? data.ThemeColor : new Color(0.45f, 0.9f, 1f, 1f);
            if (particleColor.maxColorComponent < 0.2f) particleColor = new Color(0.95f, 0.72f, 0.25f, 1f);
            main.startColor = particleColor;
            tapParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            tapParticles.Play(true);
        }

        AudioClip sfx = data != null && data.TapSfxClip != null ? data.TapSfxClip : fallbackTapSfx;
        if (sfx != null && !_isQueued)
        {
            AudioSource target = sfxOneShotSource != null ? sfxOneShotSource : audioSource;
            if (target != null)
            {
                float prev = target.pitch;
                target.pitch = data != null ? data.GetTapPitchMultiplier() : 1f;
                target.PlayOneShot(sfx, 0.6f);
                target.pitch = prev;
            }
        }
    }

    private void ApplyVisualState(Sprite sprite, Color color)
    {
        if (characterImage == null) return;

        // Oversized stems (Mr. Tree's canopy) are slightly scaled down so neighbours are not covered; feet stay planted (bottom pivot).
        float bodyScale = 1f;
        if (_isActive && _activeData != null)
        {
            string id = (_activeData.CharacterId ?? string.Empty).ToLowerInvariant();
            if (id.Contains("tree")) bodyScale = 0.88f;
        }
        characterImage.rectTransform.localScale = new Vector3(bodyScale, bodyScale, 1f);

        if (sprite != null) characterImage.sprite = sprite;
        characterImage.enabled = characterImage.sprite != null;
        characterImage.preserveAspect = true;
        characterImage.color = color;

        if (characterShadow != null) characterShadow.enabled = false;
    }

    private void UpdateMutedVisualTint(bool effectivelyMuted, bool partyMode)
    {
        if (characterImage == null || !_isActive || _isQueued) return;

        Color tint = effectivelyMuted
            ? new Color(0.75f, 0.78f, 0.86f, 0.84f)
            : Color.white;

        characterImage.color = tint;
        if (christmasHatOverlay != null && christmasHatOverlay.enabled)
        {
            christmasHatOverlay.color = tint;
        }

        UpdateGlowRingVisual(effectivelyMuted, partyMode);
    }

    private void UpdateGlowRingVisual(bool effectivelyMuted, bool partyMode)
    {
        if (glowRingImage == null) return;

        if (!_isActive)
        {
            glowRingImage.color = emptyGlowColor;
            glowRingImage.rectTransform.localScale = _glowBaseScale;
            return;
        }

        Color baseColor = _activeData != null ? _activeData.ThemeColor : new Color(0.35f, 0.85f, 1f, 1f);
        if (baseColor.maxColorComponent < 0.18f) baseColor = new Color(0.55f, 0.35f, 0.95f, 1f);

        if (partyMode)
        {
            float hue = Mathf.Repeat((Time.time * 0.45f) + (slotIndex * 0.14f), 1f);
            baseColor = Color.Lerp(baseColor, Color.HSVToRGB(hue, 0.78f, 1f), 0.55f);
        }

        float alpha = effectivelyMuted ? 0.18f : Mathf.Lerp(activeGlowBaseAlpha, activeGlowBeatPeakAlpha, _glowPulse);
        baseColor.a = alpha;

        glowRingImage.color = baseColor;
        float scaleMul = effectivelyMuted ? 0.95f : Mathf.Lerp(1f, 1.18f, _glowPulse);
        glowRingImage.rectTransform.localScale = new Vector3(_glowBaseScale.x * scaleMul, _glowBaseScale.y * scaleMul, _glowBaseScale.z);
    }

    private void HideAllStickers()
    {
        DisableStickerImage(headAnchorImage);
        DisableStickerImage(eyesAnchorImage);
        DisableStickerImage(bodyAnchorImage);
        DisableStickerImage(handAnchorImage);
    }

    private void ResolveHeadpieceForCharacter(
        CharacterSoundData data,
        bool emptySlot,
        out Sprite headpieceSprite,
        out Vector2 anchoredPos,
        out Vector2 size,
        out float rotZ)
    {
        Sprite santa = defaultChristmasHatSprite;
        Sprite elf = elfHollyCapSprite != null ? elfHollyCapSprite : santa;
        Sprite antlers = reindeerAntlersSprite != null ? reindeerAntlersSprite : santa;

        // Default Empty Polo crown
        headpieceSprite = (slotIndex % 2 == 0) ? santa : elf;
        anchoredPos = new Vector2(18f, 372f);
        size = new Vector2(140f, 105f);
        rotZ = -9f;

        if (emptySlot || data == null) return;

        string id = $"{data.CharacterId} {data.DisplayName}".ToLowerInvariant();

        if (id.Contains("sun"))
        {
            // Mr. Sun: cute tilted Santa Hat perched on the top-right edge of his golden rays!
            headpieceSprite = santa;
            anchoredPos = new Vector2(58f, 412f);
            size = new Vector2(142f, 106f);
            rotZ = -24f;
        }
        else if (id.Contains("computer"))
        {
            // Mr. Fun Computer: cute tilted Santa Hat on the top-right corner of the monitor!
            headpieceSprite = santa;
            anchoredPos = new Vector2(46f, 342f);
            size = new Vector2(132f, 98f);
            rotZ = -18f;
        }
        else if (id.Contains("brud") || id.Contains("brown"))
        {
            // Brud wears his Santa Hat right on top of his metal bucket!
            headpieceSprite = santa;
            anchoredPos = new Vector2(16f, 408f);
            size = new Vector2(138f, 104f);
            rotZ = -10f;
        }
        else if (id.Contains("raddy") || id.Contains("durple") || id.Contains("simon"))
        {
            // Horned characters look awesome with festive Reindeer Antlers or a crown-fitted Santa Hat
            headpieceSprite = antlers != null ? antlers : santa;
            anchoredPos = new Vector2(0f, 384f);
            size = new Vector2(168f, 112f);
            rotZ = 0f;
        }
        else if (id.Contains("fun bot") || id.Contains("fun_bot"))
        {
            headpieceSprite = elf;
            anchoredPos = new Vector2(30f, 376f);
            size = new Vector2(134f, 100f);
            rotZ = -15f;
        }
        else if (id.Contains("vineria") || id.Contains("green"))
        {
            headpieceSprite = santa;
            anchoredPos = new Vector2(34f, 382f);
            size = new Vector2(134f, 100f);
            rotZ = -18f;
        }
        else if (id.Contains("pinki") || id.Contains("pink"))
        {
            headpieceSprite = santa;
            anchoredPos = new Vector2(34f, 382f);
            size = new Vector2(132f, 98f);
            rotZ = -16f;
        }
        else if (id.Contains("owakcx") || id.Contains("lime"))
        {
            headpieceSprite = elf;
            anchoredPos = new Vector2(22f, 400f);
            size = new Vector2(136f, 102f);
            rotZ = -12f;
        }
        else if (id.Contains("wenda") || id.Contains("white") || id.Contains("gray") || id.Contains("oren") || id.Contains("sky"))
        {
            headpieceSprite = (slotIndex % 3 == 1) ? elf : santa;
            anchoredPos = new Vector2(18f, 376f);
            size = new Vector2(138f, 104f);
            rotZ = -10f;
        }
        else
        {
            // Clukr, Garnold, Tunner, Jevin, Golden Christmas Star
            headpieceSprite = (slotIndex % 2 == 0) ? santa : elf;
            anchoredPos = new Vector2(18f, 374f);
            size = new Vector2(140f, 105f);
            rotZ = -9f;
        }
    }

    private void ApplyChristmasCostumes(CharacterSoundData data, bool enabledByState, bool emptySlot = false)
    {
        if (christmasScarfOverlay != null) christmasScarfOverlay.enabled = false;

        string id = data != null ? $"{data.CharacterId} {data.DisplayName}".ToLowerInvariant() : string.Empty;
        _isMrTreeActive = !emptySlot && enabledByState && id.Contains("tree");

        if (mrTreeNoelRoot != null)
        {
            mrTreeNoelRoot.gameObject.SetActive(_isMrTreeActive);
            if (_isMrTreeActive)
            {
                mrTreeNoelRoot.SetAsLastSibling();
                UpdateHatAndNoelTreeAnimation(false, false, 1f);
            }
        }

        if (christmasHatOverlay == null) return;

        if (_isMrTreeActive)
        {
            // Mr. Tree wears the Golden Star Topper & Noel Tree Baubles instead of a small cap!
            christmasHatOverlay.enabled = false;
            return;
        }

        bool wantsHat = emptySlot ? showChristmasCostumeWhenEmpty : showChristmasCostumeWhenActive;
        bool customHead = headAnchorImage != null && headAnchorImage.enabled && headAnchorImage.sprite != null;

        ResolveHeadpieceForCharacter(data, emptySlot, out Sprite chosenSprite, out Vector2 pos, out Vector2 size, out float rotZ);
        _hatBasePos = pos;
        _hatBaseRotZ = rotZ;

        RectTransform hatRect = christmasHatOverlay.rectTransform;
        hatRect.anchoredPosition = pos;
        hatRect.sizeDelta = size;
        hatRect.localRotation = Quaternion.Euler(0f, 0f, rotZ);

        christmasHatOverlay.sprite = chosenSprite;
        christmasHatOverlay.preserveAspect = true;
        christmasHatOverlay.color = (_isActive && _isQueued) ? new Color(1f, 1f, 1f, queuedAlpha) : Color.white;
        christmasHatOverlay.enabled = enabledByState && wantsHat && !customHead && chosenSprite != null;

        if (christmasHatOverlay.enabled)
        {
            // Force top-most layer inside CharacterVisual so the character body never covers the hat!
            christmasHatOverlay.transform.SetAsLastSibling();
        }
    }

    private void UpdateHatAndNoelTreeAnimation(bool effectivelyMuted, bool partyMode, float loudness)
    {
        if (_isMrTreeActive && mrTreeNoelRoot != null)
        {
            mrTreeNoelRoot.SetAsLastSibling();
            float energy = effectivelyMuted ? 0.15f : Mathf.Clamp01(Mathf.Max(_glowPulse, loudness * 1.15f));

            if (mrTreeTopStarGlow != null)
            {
                float starScale = Mathf.Lerp(0.92f, 1.32f, energy);
                mrTreeTopStarGlow.rectTransform.localScale = new Vector3(starScale, starScale, 1f);
                float tilt = Mathf.Sin(Time.time * 4.5f) * 6f;
                mrTreeTopStarGlow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                mrTreeTopStarGlow.color = new Color(1f, 0.94f, 0.42f, Mathf.Lerp(0.45f, 0.98f, energy));
            }

            if (mrTreeBaubleGlows != null)
            {
                for (int i = 0; i < mrTreeBaubleGlows.Length; i++)
                {
                    Image bulb = mrTreeBaubleGlows[i];
                    if (bulb == null) continue;

                    Color baseCol = NoelBaubleColors[i % NoelBaubleColors.Length];
                    float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * (partyMode ? 9.0f : 5.5f) + i * 1.1f);
                    float bLevel = effectivelyMuted ? 0.2f : Mathf.Clamp01(energy * 0.75f + wave * 0.45f);

                    Color c = Color.Lerp(baseCol, Color.white, bLevel * 0.42f);
                    c.a = Mathf.Lerp(0.35f, 0.96f, bLevel);
                    bulb.color = c;

                    float s = Mathf.Lerp(0.82f, 1.38f, bLevel);
                    bulb.rectTransform.localScale = new Vector3(s, s, 1f);
                }
            }
            return;
        }

        if (christmasHatOverlay != null && christmasHatOverlay.enabled)
        {
            christmasHatOverlay.transform.SetAsLastSibling();
            float bobY = (!effectivelyMuted && loudness >= mouthOpenLoudness)
                ? Mathf.Sin(Time.time * 12f + slotIndex) * 3.5f + (_glowPulse * 4.0f)
                : (_glowPulse * 2.5f);
            float tiltWobble = (!effectivelyMuted && loudness >= mouthOpenLoudness)
                ? Mathf.Sin(Time.time * 8f + slotIndex) * 2.8f
                : 0f;

            RectTransform hatRect = christmasHatOverlay.rectTransform;
            hatRect.anchoredPosition = _hatBasePos + new Vector2(0f, bobY);
            hatRect.localRotation = Quaternion.Euler(0f, 0f, _hatBaseRotZ + tiltWobble);
        }
    }

    private void HideChristmasCostumes()
    {
        if (christmasHatOverlay != null) christmasHatOverlay.enabled = false;
        if (christmasScarfOverlay != null) christmasScarfOverlay.enabled = false;
        if (mrTreeNoelRoot != null) mrTreeNoelRoot.gameObject.SetActive(false);
    }

    private static void DisableStickerImage(Image img)
    {
        if (img != null)
        {
            img.sprite = null;
            img.enabled = false;
        }
    }

    private void WireControlButtons()
    {
        if (muteButton != null)
        {
            muteButton.onClick.RemoveListener(ToggleMute);
            muteButton.onClick.AddListener(ToggleMute);
        }
        if (soloButton != null)
        {
            soloButton.onClick.RemoveListener(ToggleSolo);
            soloButton.onClick.AddListener(ToggleSolo);
        }
        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(ResetSlot);
            removeButton.onClick.AddListener(ResetSlot);
        }
    }

    private void RefreshControlsVisibility()
    {
        if (controlsRoot == null) return;
        bool visible = _isActive && (alwaysShowControlsWhenActive || _isPointerHovered || _controlsPinnedOpen);
        if (controlsRoot.activeSelf != visible) controlsRoot.SetActive(visible);
    }

    private void UpdateControlButtonVisuals()
    {
        if (muteButton != null && muteButton.targetGraphic is Image muteImg)
        {
            muteImg.color = _isMuted
                ? new Color(1f, 0.36f, 0.36f, 0.98f)
                : new Color(0.95f, 0.97f, 1f, 0.94f);
        }

        if (soloButton != null && soloButton.targetGraphic is Image soloImg)
        {
            soloImg.color = _isSolo
                ? new Color(1f, 0.84f, 0.20f, 0.98f)
                : new Color(0.95f, 0.97f, 1f, 0.94f);
        }
    }

    private void TrySubscribeAudioSync()
    {
        if (_isSubscribedToSync || AudioSyncManager.Instance == null) return;
        AudioSyncManager.Instance.OnBeatIndex += HandleBeat;
        AudioSyncManager.Instance.OnLoopRestart += HandleLoopRestart;
        AudioSyncManager.Instance.OnSlotStarted += HandleSlotStarted;
        _isSubscribedToSync = true;
    }

    private void UnsubscribeAudioSync()
    {
        if (!_isSubscribedToSync || AudioSyncManager.Instance == null) return;
        AudioSyncManager.Instance.OnBeatIndex -= HandleBeat;
        AudioSyncManager.Instance.OnLoopRestart -= HandleLoopRestart;
        AudioSyncManager.Instance.OnSlotStarted -= HandleSlotStarted;
        _isSubscribedToSync = false;
    }
}
