using System;
using System.Collections.Generic;
using UnityEngine;

namespace SprunkiKids.Data
{
    /// <summary>
    /// Musical role categories matching classic 20-character music-mixing layouts.
    /// </summary>
    public enum SoundCategory
    {
        Beat = 0,
        Effect = 1,
        Melody = 2,
        Voice = 3,
        Special = 4
    }

    /// <summary>
    /// Body anchor points used by Layer 2 cosmetic stickers/accessories.
    /// </summary>
    public enum StickerAnchorType
    {
        Head = 0,
        Eyes = 1,
        Body = 2,
        Hand = 3
    }

    /// <summary>
    /// Stores the audio loops, multi-frame Scratch costume animations, kid-friendly Party Mode
    /// metadata, and touch feedback assets for one playable Sprunki character item.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacterSoundData", menuName = "Sprunki Kids/Character Sound Data", order = 1)]
    public class CharacterSoundData : ScriptableObject
    {
        [Header("Identity & Classification")]
        [Tooltip("Unique sanitized identifier used for saves, combos, and lookups.")]
        [SerializeField] private string characterId = "character_01";

        [Tooltip("Kid-friendly display name shown in the UI.")]
        [SerializeField] private string displayName = "New Character";

        [Tooltip("Musical role group (Beat, Effect, Melody, Voice, or Special).")]
        [SerializeField] private SoundCategory soundCategory = SoundCategory.Beat;

        [Tooltip("Ordering index in the bottom 20-icon selection tray (0-19).")]
        [SerializeField, Min(0)] private int sortOrder;

        [Tooltip("True for the 20th mystery item that activates kid-friendly Party Mode when placed on stage.")]
        [SerializeField] private bool isPartyTriggerItem;

        [Tooltip("Vibrant primary theme color parsed from original icon SVG artwork.")]
        [SerializeField] private Color themeColor = new Color(0.29f, 0.78f, 1f, 1f);

        [Header("Audio Loops & Touch SFX")]
        [Tooltip("Primary rhythmic loop (Loop 1) played in sync with AudioSyncManager.")]
        [SerializeField] private AudioClip mainLoopClip;

        [Tooltip("Optional second-half phrase loop (Loop 2) alternated after Loop 1, matching Sprunki's 2-part phrase.")]
        [SerializeField] private AudioClip secondaryLoopClip;

        [Tooltip("Optional upbeat chorus loop played during Party Mode (falls back to Main/Secondary Loop if unset).")]
        [SerializeField] private AudioClip partyLoopClip;

        [Tooltip("Additional audio clips discovered on this Scratch target during migration.")]
        [SerializeField] private List<AudioClip> additionalClips = new List<AudioClip>();

        [Tooltip("Musical length of one loop phrase in beats. Set to 0 to auto-detect from clip duration (8 or 16 beats).")]
        [SerializeField, Min(0)] private int loopBeats = 0;

        [Tooltip("Default mix volume for this character loop.")]
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.9f;

        [Tooltip("One-shot SFX played when dropping or tapping the character (linked to original Scratch pop SFX).")]
        [SerializeField] private AudioClip tapSfxClip;

        [Tooltip("Semitone offset applied to the tap SFX so each character plays a pleasant pentatonic note.")]
        [SerializeField, Range(-12f, 12f)] private float harmonicSemitoneOffset;

        [Header("Layer 1 Visuals & Scratch Frame Animation")]
        [Tooltip("Icon displayed in the bottom 20-slot sound tray.")]
        [SerializeField] private Sprite iconSprite;

        [Tooltip("Standing/idle costume sprite when placed or muted on stage.")]
        [SerializeField] private Sprite idleSprite;

        [Tooltip("Original Scratch costume frames cycled smoothly at SingingFrameRate while singing.")]
        [SerializeField] private List<Sprite> singingFrames = new List<Sprite>();

        [Tooltip("Frame rate (FPS) for Scratch multi-frame costume cycling (original Sprunki runs ~10 FPS).")]
        [SerializeField, Range(1f, 30f)] private float singingFrameRate = 10f;

        [Header("Party Combo Mode Visuals")]
        [Tooltip("Optional alternate idle sprite used during Party Mode (falls back to Idle Sprite if unset).")]
        [SerializeField] private Sprite partySkinSprite;

        [Tooltip("Optional alternate singing frames used during Party Mode (falls back to Singing Frames if unset).")]
        [SerializeField] private List<Sprite> partySingingFrames = new List<Sprite>();

        [Header("Migrated Scratch Archive Reference")]
        [Tooltip("All imported sprites from this Scratch target for easy artist inspection.")]
        [SerializeField] private List<Sprite> allImportedCostumes = new List<Sprite>();

        [SerializeField, HideInInspector] private string originalScratchTargetName;
        [SerializeField, HideInInspector] private string importedAssetSubfolder;

        public string CharacterId => characterId;
        public string DisplayName => displayName;
        public SoundCategory Category => soundCategory;
        public int SortOrder => sortOrder;
        public bool IsPartyTriggerItem => isPartyTriggerItem;
        public Color ThemeColor => themeColor;

        public AudioClip MainLoopClip => mainLoopClip;
        public AudioClip SecondaryLoopClip => secondaryLoopClip;
        public AudioClip PartyLoopClip => partyLoopClip;
        public IReadOnlyList<AudioClip> AdditionalClips => additionalClips;
        public int LoopBeats => loopBeats;
        public float DefaultVolume => defaultVolume;
        public AudioClip TapSfxClip => tapSfxClip;
        public float HarmonicSemitoneOffset => harmonicSemitoneOffset;

        public Sprite IconSprite => iconSprite;
        public Sprite IdleSprite => idleSprite;
        public IReadOnlyList<Sprite> SingingFrames => singingFrames;
        public float SingingFrameRate => singingFrameRate;
        public Sprite PartySkinSprite => partySkinSprite;
        public IReadOnlyList<Sprite> PartySingingFrames => partySingingFrames;
        public IReadOnlyList<Sprite> AllImportedCostumes => allImportedCostumes;

        public string OriginalScratchTargetName => originalScratchTargetName;
        public string ImportedAssetSubfolder => importedAssetSubfolder;

        public bool HasValidAudio => mainLoopClip != null || secondaryLoopClip != null || partyLoopClip != null;
        public bool HasSingingAnimation => singingFrames != null && singingFrames.Count > 1;

        public AudioClip GetLoopClip(bool isPartyMode, int phraseIndex = 0)
        {
            if (isPartyMode && partyLoopClip != null)
            {
                return partyLoopClip;
            }

            if ((phraseIndex & 1) == 1 && secondaryLoopClip != null)
            {
                return secondaryLoopClip;
            }

            return mainLoopClip != null ? mainLoopClip : (secondaryLoopClip != null ? secondaryLoopClip : partyLoopClip);
        }

        public Sprite GetIdleSprite(bool isPartyMode)
        {
            if (isPartyMode && partySkinSprite != null)
            {
                return partySkinSprite;
            }

            return idleSprite != null ? idleSprite : iconSprite;
        }

        public IReadOnlyList<Sprite> GetSingingFrames(bool isPartyMode)
        {
            if (isPartyMode && partySingingFrames != null && partySingingFrames.Count > 0)
            {
                return partySingingFrames;
            }

            return singingFrames;
        }

        public int ResolveLoopBeats(int masterBeatsPerLoop)
        {
            return loopBeats > 0 ? loopBeats : Mathf.Max(1, masterBeatsPerLoop);
        }

        public float GetTapPitchMultiplier()
        {
            return Mathf.Pow(2f, harmonicSemitoneOffset / 12f);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                characterId = SanitizeId(name);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            defaultVolume = Mathf.Clamp01(defaultVolume);
            singingFrameRate = Mathf.Clamp(singingFrameRate, 1f, 30f);
        }

        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "character";
            }

            char[] buffer = raw.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char c = buffer[i];
                bool isValid = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!isValid)
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer);
        }

#if UNITY_EDITOR
        public void ConfigureFromScratchImport(
            string id,
            string label,
            SoundCategory category,
            int orderIndex,
            bool partyTrigger,
            Color color,
            float semitoneOffset,
            string scratchTargetName,
            string assetSubfolder,
            AudioClip primaryLoop,
            AudioClip partyLoop,
            List<AudioClip> extraClips,
            Sprite icon,
            Sprite idle,
            List<Sprite> singSprites,
            Sprite partySprite,
            List<Sprite> allCostumes,
            AudioClip secondaryLoop = null,
            AudioClip tapSfx = null,
            float frameRate = 10f)
        {
            characterId = id;
            displayName = label;
            soundCategory = category;
            sortOrder = orderIndex;
            isPartyTriggerItem = partyTrigger;
            themeColor = color;
            harmonicSemitoneOffset = Mathf.Clamp(semitoneOffset, -12f, 12f);
            originalScratchTargetName = scratchTargetName;
            importedAssetSubfolder = assetSubfolder;

            mainLoopClip = primaryLoop;
            secondaryLoopClip = secondaryLoop;
            partyLoopClip = partyLoop;
            additionalClips = extraClips != null ? new List<AudioClip>(extraClips) : new List<AudioClip>();
            if (tapSfx != null) tapSfxClip = tapSfx;

            iconSprite = icon;
            idleSprite = idle;
            singingFrames = singSprites != null ? new List<Sprite>(singSprites) : new List<Sprite>();
            singingFrameRate = Mathf.Clamp(frameRate, 1f, 30f);
            partySkinSprite = partySprite;
            allImportedCostumes = allCostumes != null ? new List<Sprite>(allCostumes) : new List<Sprite>();
        }

        public void RelinkImportedMedia(
            AudioClip primaryLoop,
            AudioClip partyLoop,
            List<AudioClip> extraClips,
            Sprite icon,
            Sprite idle,
            List<Sprite> singSprites,
            Sprite partySprite,
            List<Sprite> allCostumes,
            AudioClip secondaryLoop = null,
            AudioClip tapSfx = null,
            Color? parsedThemeColor = null)
        {
            if (primaryLoop != null) mainLoopClip = primaryLoop;
            if (secondaryLoop != null) secondaryLoopClip = secondaryLoop;
            if (partyLoop != null) partyLoopClip = partyLoop;
            if (extraClips != null && extraClips.Count > 0) additionalClips = new List<AudioClip>(extraClips);
            if (tapSfx != null && tapSfxClip == null) tapSfxClip = tapSfx;

            if (icon != null) iconSprite = icon;
            if (idle != null) idleSprite = idle;
            if (singSprites != null && singSprites.Count > 0) singingFrames = new List<Sprite>(singSprites);
            if (partySprite != null) partySkinSprite = partySprite;
            if (allCostumes != null && allCostumes.Count > 0) allImportedCostumes = new List<Sprite>(allCostumes);
            if (parsedThemeColor.HasValue) themeColor = parsedThemeColor.Value;
        }

        public void ApplyChristmasStarOverride(Sprite starIcon, string newDisplayName, Color starColor)
        {
            if (starIcon != null) iconSprite = starIcon;
            if (!string.IsNullOrWhiteSpace(newDisplayName)) displayName = newDisplayName;
            themeColor = starColor;
            isPartyTriggerItem = true;
        }
#endif
    }
}
