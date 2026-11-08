using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SprunkiKids.Data;

namespace SprunkiKids.Editor
{
    /// <summary>
    /// Complete zero-touch / one-click scene builder for "Sprunki: Christmas Kids Wonderland".
    /// Cleans legacy placeholders, generates procedural 2D Christmas costumes & winter scenery sprites,
    /// configures Main Camera, ScreenSpaceCamera Canvas (1920x1080), InputSystem EventSystem,
    /// snowy winter Stage + Aurora Magic night overlay + pulsing LED string lights + Snowfall &
    /// Snowflake Fireworks VFX, Header Bar (3 Christmas Auto-Mix presets, Snowstorm Boost,
    /// Santa's Magic toggle, Reset, Beat LEDs), 7 Polo Character Slots with festive hats/scarves,
    /// and the bottom 20-icon tray culminating with Icon #20 "Golden Christmas Star".
    /// </summary>
    public static class SprunkiAutoSetup
    {
        private const int SlotCount = 7;
        private const float SlotSpacingX = 236f;
        private const float SlotWidth = 236f;
        private const float SlotHeight = 470f;
        private const float SlotBottomY = -262f;
        private const float BodySpriteSize = 600f;
        private const float BodyFootOffsetY = -22f;
        private const float HatWidth = 140f;
        private const float HatHeight = 105f;
        private const float HatBottomY = 372f;
        private const float ControlsBarY = 412f;
        private const string QueueDiscTexturePath = "Assets/Art/UI/QueueDiscRing.png";

        private const string GeneratedUiArtFolder = "Assets/Art/UI";
        private const string ChristmasArtFolder = "Assets/Art/UI/Christmas";

        private const string SilhouetteTexturePath = "Assets/Art/UI/DefaultSlotSilhouette.png";
        private const string IconCircleTexturePath = "Assets/Art/UI/DefaultIconBadge.png";
        private const string RoundedPillTexturePath = "Assets/Art/UI/RoundedPill.png";
        private const string GlowRingTexturePath = "Assets/Art/UI/GlowRing.png";
        private const string SpotlightBeamTexturePath = "Assets/Art/UI/SpotlightBeam.png";
        private const string SparkleStarTexturePath = "Assets/Art/UI/SparkleStar.png";
        private const string ParticleMaterialPath = "Assets/Art/UI/SprunkiParticleMat.mat";

        private const string SantaHatTexturePath = "Assets/Art/UI/Christmas/SantaHat.png";
        private const string ReindeerAntlersTexturePath = "Assets/Art/UI/Christmas/ReindeerAntlers.png";
        private const string ElfHollyCapTexturePath = "Assets/Art/UI/Christmas/ElfHollyCap.png";
        private const string FestiveScarfTexturePath = "Assets/Art/UI/Christmas/FestiveScarf.png";
        private const string GoldenStarTexturePath = "Assets/Art/UI/Christmas/GoldenChristmasStar.png";
        private const string SnowyPineTexturePath = "Assets/Art/UI/Christmas/SnowyPineTree.png";
        private const string SnowHillTexturePath = "Assets/Art/UI/Christmas/SnowHillCurve.png";
        private const string SnowflakeTexturePath = "Assets/Art/UI/Christmas/SnowflakeParticle.png";
        private const string BulbGlowTexturePath = "Assets/Art/UI/Christmas/BulbGlow.png";
        private const string AuroraCurtainTexturePath = "Assets/Art/UI/Christmas/AuroraCurtain.png";
        private const string SnowParticleMaterialPath = "Assets/Art/UI/Christmas/SprunkiSnowParticleMat.mat";

        private struct ChristmasSprites
        {
            public Sprite SantaHat;
            public Sprite ReindeerAntlers;
            public Sprite ElfHollyCap;
            public Sprite FestiveScarf;
            public Sprite GoldenStarIcon;
            public Sprite SnowyPine;
            public Sprite SnowHill;
            public Sprite BulbGlow;
            public Sprite AuroraCurtain;
            public Material SnowParticleMaterial;
        }

        private struct HeaderUiRefs
        {
            public Button ResetButton;
            public Button PartyButton;
            public Image PartyButtonBg;
            public Text PartyButtonLabel;
            public Button AutoRandomButton;
            public Image AutoRandomButtonBg;
            public Text AutoRandomButtonLabel;
            public Button SnowstormButton;
            public Image SnowstormButtonBg;
            public Text SnowstormButtonLabel;
            public Text ActiveCountText;
            public Image[] BeatLeds;
            public Image LoopProgressFill;
            public RectTransform DjGhostRoot;
            public Image DjGhostBadge;
            public Image DjGhostPortrait;
            public Text DjGhostTagText;
        }

        private struct StageBackdropRefs
        {
            public Image SkyBase;
            public Image GrassBase;
            public Image StageArtwork;
            public Image PartyRainbowOverlay;
            public Image[] PartySpotlights;
            public Image[] SnowyHills;
            public Image[] PineTrees;
            public Image[] AuroraRibbons;
            public Image[] ChristmasLedBulbs;
            public ParticleSystem PartyConfettiParticles;
            public ParticleSystem SnowfallParticles;
            public ParticleSystem SnowflakeFireworksParticles;
        }

        [MenuItem("Tools/Sprunki/Auto Setup Entire Scene", priority = 50)]
        public static void AutoSetupEntireScene()
        {
            BuildCompleteScene(silentMode: false);
        }

        /// <summary>
        /// Builds or rebuilds the complete playable Christmas Wonderland scene cleanly from imported assets.
        /// Can run silently in zero-touch automation pipelines without modal dialogs.
        /// </summary>
        public static bool BuildCompleteScene(bool silentMode)
        {
            if (!silentMode)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Sprunki Auto Setup Entire Scene");
            }

            try
            {
                CleanupPlaceholderAndStaleObjects();
                ScratchAssetImporter.ConfigureCharactersPngTextures(100f);

                EnsureGeneratedUiSprites(
                    out Sprite fallbackSilhouette,
                    out Sprite defaultBadge,
                    out Sprite roundedPill,
                    out Sprite glowRing,
                    out Sprite spotlightBeam,
                    out Material particleMaterial,
                    out ChristmasSprites christmas);

                List<CharacterSoundData> characterAssets = LoadAllCharacterSoundData();
                Sprite magicStarBadge = ScratchAssetImporter.LoadSpriteAtPath(ScratchAssetImporter.ExpectedGoldenStarIconPath)
                                        ?? christmas.GoldenStarIcon;
                ApplyGoldenChristmasStarToTwentiethCharacter(characterAssets, magicStarBadge);

                Sprite stageDaytimeSprite = ScratchAssetImporter.LoadSpriteAtPath("Assets/Art/CharactersPNG/Stage/backdropcute.png")
                                            ?? ScratchAssetImporter.LoadSpriteAtPath(ScratchAssetImporter.ExpectedStageDaytimePath);
                Sprite poloBaseSprite = ScratchAssetImporter.LoadSpriteAtPath("Assets/Art/CharactersPNG/Polo_Base/polo_idle.png")
                                        ?? ScratchAssetImporter.LoadSpriteAtPath(ScratchAssetImporter.ExpectedPoloIdlePath)
                                        ?? fallbackSilhouette;
                AudioClip popClickSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(ScratchAssetImporter.ExpectedPopSfxPath);

                int validIcons = characterAssets.Count(c => c != null && c.IconSprite != null);
                int validBodies = characterAssets.Count(c => c != null && c.IdleSprite != null);
                Debug.Log($"[SprunkiAutoSetup] PNG Sprite Diagnostics -> PoloBase={(poloBaseSprite != null ? poloBaseSprite.name : "NULL")}, Stage={(stageDaytimeSprite != null ? stageDaytimeSprite.name : "NULL")}, CharactersWithIcon={validIcons}/{characterAssets.Count}, CharactersWithBody={validBodies}/{characterAssets.Count}");

                Camera mainCam = EnsureMainCamera();
                AudioSyncManager syncManager = EnsureGameManager(out GameObject gmGo);
                Canvas canvas = EnsureMainCanvas(mainCam);
                EnsureEventSystem();

                StageBackdropRefs stageRefs = BuildStageBackdrop(
                    canvas.transform,
                    stageDaytimeSprite,
                    spotlightBeam,
                    particleMaterial,
                    christmas);

                List<CharacterSlot> slots = BuildCharacterSlots(
                    canvas.transform,
                    poloBaseSprite,
                    glowRing,
                    roundedPill,
                    particleMaterial,
                    popClickSfx,
                    christmas);

                HeaderUiRefs headerRefs = BuildHeaderBar(canvas.transform, roundedPill);
                RectTransform iconTray = BuildIconTray(canvas, characterAssets, defaultBadge, roundedPill, magicStarBadge);

                WireStageController(gmGo, syncManager, slots, characterAssets, stageRefs, headerRefs, popClickSfx);

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                if (!silentMode)
                {
                    Selection.activeGameObject = canvas.gameObject;
                }

                int iconCount = iconTray.GetComponentsInChildren<DraggableIcon>(true).Length;
                string summary =
                    $"Christmas Wonderland Scene built! Canvas (1920x1080 ScreenSpaceCamera), " +
                    $"StageArtwork ({(stageDaytimeSprite != null ? stageDaytimeSprite.name : "winter")}), " +
                    $"PoloBase ({(poloBaseSprite != null ? poloBaseSprite.name : "fallback")}), {slots.Count} Festive Slots, " +
                    $"Aurora & String Lights + Snowfall/Fireworks VFX, 3 Auto-Mix + Snowstorm controls, and {iconCount} DraggableIcons ({syncManager.Bpm} BPM).";

                Debug.Log($"[SprunkiAutoSetup] {summary}");

                if (!silentMode)
                {
                    EditorUtility.DisplayDialog(
                        "Sprunki Christmas Auto Setup Complete",
                        $"Sprunki: Christmas Kids Wonderland scene built!\n\n" +
                        $"• Winter Backdrop: Snowy Hills, Pines, LED String Lights & Snowfall\n" +
                        $"• Stage Slots: Character_Slot_1..{slots.Count} with festive Santa Hat / Reindeer Antlers / Scarf & Mute/Solo/Remove buttons\n" +
                        $"• Header Controls: Jingle Groove, Frosty Beat, Santa's Party Auto-Mix, Snowstorm Boost, Santa Magic & Reset\n" +
                        $"• Bottom IconTray: {iconCount} icons (with #20 Golden Christmas Star)",
                        "Ho Ho Play!");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static void ApplyGoldenChristmasStarToTwentiethCharacter(List<CharacterSoundData> characters, Sprite starIcon)
        {
            if (characters == null || characters.Count == 0 || starIcon == null) return;

            CharacterSoundData starChar = characters.FirstOrDefault(c => c != null && c.IsPartyTriggerItem)
                                          ?? characters.LastOrDefault(c => c != null);
            if (starChar == null) return;

            starChar.ApplyChristmasStarOverride(starIcon, "Golden Christmas Star", new Color(1f, 0.843f, 0f, 1f));
            EditorUtility.SetDirty(starChar);
            AssetDatabase.SaveAssets();
        }

        private static void CleanupPlaceholderAndStaleObjects()
        {
            string[] staleNames =
            {
                "Sound 1", "Sound 2", "StageBackdrop", "StageSlots",
                "IconTray", "HeaderBar", "GameManager",
                "PartyConfettiParticles", "SnowfallParticles", "SnowflakeFireworksParticles",
                "ResetCluster", "DjGhostCursor"
            };

            foreach (string name in staleNames)
            {
                GameObject found = GameObject.Find(name);
                if (found != null)
                {
                    UnityEngine.Object.DestroyImmediate(found);
                }
            }

            for (int i = 1; i <= 12; i++)
            {
                GameObject stray = GameObject.Find($"Character_Slot_{i}");
                if (stray != null)
                {
                    UnityEngine.Object.DestroyImmediate(stray);
                }
            }

            foreach (CharacterSlot slot in UnityEngine.Object.FindObjectsByType<CharacterSlot>(FindObjectsSortMode.None))
            {
                if (slot != null)
                {
                    UnityEngine.Object.DestroyImmediate(slot.gameObject);
                }
            }

            foreach (AudioSyncManager mgr in UnityEngine.Object.FindObjectsByType<AudioSyncManager>(FindObjectsSortMode.None))
            {
                if (mgr != null)
                {
                    UnityEngine.Object.DestroyImmediate(mgr.gameObject);
                }
            }

            Canvas existingCanvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (existingCanvas != null)
            {
                UnityEngine.Object.DestroyImmediate(existingCanvas.gameObject);
            }
        }

        private static Camera EnsureMainCamera()
        {
            Camera cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }
            else if (cam.GetComponent<AudioListener>() == null)
            {
                cam.gameObject.AddComponent<AudioListener>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.14f, 0.34f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            return cam;
        }

        private static AudioSyncManager EnsureGameManager(out GameObject gmGo)
        {
            gmGo = new GameObject("GameManager");
            AudioSyncManager sync = gmGo.AddComponent<AudioSyncManager>();

            SerializedObject so = new SerializedObject(sync);
            so.FindProperty("bpm").floatValue = 100f;
            so.FindProperty("beatsPerBar").intValue = 8;
            so.FindProperty("barsPerLoop").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();

            return sync;
        }

        private static Canvas EnsureMainCanvas(Camera worldCam)
        {
            GameObject canvasGo = new GameObject("Canvas");
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) canvasGo.layer = uiLayer;

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = worldCam;
            canvas.planeDistance = 10f;
            canvas.sortingOrder = 0;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                eventSystem = esGo.AddComponent<EventSystem>();
            }

            Type inputSystemModuleType = FindTypeInLoadedAssemblies("UnityEngine.InputSystem.UI.InputSystemUIInputModule");

#if ENABLE_INPUT_SYSTEM
            if (inputSystemModuleType != null)
            {
                StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
                if (legacyModule != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacyModule);
                }

                Component uiModule = eventSystem.GetComponent(inputSystemModuleType);
                if (uiModule == null)
                {
                    uiModule = eventSystem.gameObject.AddComponent(inputSystemModuleType);
                    MethodInfo assignDefaults = inputSystemModuleType.GetMethod("AssignDefaultActions", BindingFlags.Public | BindingFlags.Instance);
                    assignDefaults?.Invoke(uiModule, null);
                }
                return;
            }
#endif

            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        private static StageBackdropRefs BuildStageBackdrop(
            Transform canvasTransform,
            Sprite stageDaytimeSprite,
            Sprite spotlightBeamSprite,
            Material sparkleMaterial,
            ChristmasSprites christmas)
        {
            var refs = new StageBackdropRefs();

            GameObject backdropGo = CreateUiObject("StageBackdrop", canvasTransform);
            RectTransform backdropRect = backdropGo.GetComponent<RectTransform>();
            StretchFull(backdropRect);
            backdropGo.AddComponent<RectMask2D>();

            // 1. Winter Frost Sky Fill
            GameObject skyGo = CreateUiObject("SkyFill", backdropGo.transform);
            StretchFull(skyGo.GetComponent<RectTransform>());
            Image skyImg = skyGo.AddComponent<Image>();
            skyImg.color = new Color(0.08f, 0.14f, 0.34f, 1f);
            skyImg.raycastTarget = false;
            refs.SkyBase = skyImg;

            // 2. Original Scratch backdropcute.svg Artwork blended softly under winter snow
            if (stageDaytimeSprite != null)
            {
                GameObject artworkContainer = CreateUiObject("StageArtworkFrame", backdropGo.transform);
                RectTransform frameRect = artworkContainer.GetComponent<RectTransform>();
                frameRect.anchorMin = new Vector2(0f, 0.16f);
                frameRect.anchorMax = new Vector2(1f, 1f);
                frameRect.offsetMin = Vector2.zero;
                frameRect.offsetMax = Vector2.zero;

                GameObject artGo = CreateUiObject("StageArtwork", artworkContainer.transform);
                RectTransform artRect = artGo.GetComponent<RectTransform>();
                StretchFull(artRect);

                Image artImg = artGo.AddComponent<Image>();
                artImg.sprite = stageDaytimeSprite;
                artImg.preserveAspect = true;
                artImg.color = new Color(0.55f, 0.68f, 0.98f, 0.30f);
                artImg.raycastTarget = false;

                AspectRatioFitter fitter = artGo.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                float spriteAspect = stageDaytimeSprite.rect.height > 0f
                    ? stageDaytimeSprite.rect.width / stageDaytimeSprite.rect.height
                    : 2.623f;
                fitter.aspectRatio = spriteAspect;

                refs.StageArtwork = artImg;
            }

            // 3. Northern Lights Aurora Borealis Ribbons (active in Santa's Magic Mode!)
            refs.AuroraRibbons = new Image[2];
            Vector2[] auroraPositions = { new Vector2(-140f, 250f), new Vector2(160f, 200f) };
            Vector2[] auroraSizes = { new Vector2(1580f, 360f), new Vector2(1420f, 300f) };
            for (int i = 0; i < 2; i++)
            {
                GameObject auroraGo = CreateUiObject($"AuroraRibbon_{i + 1}", backdropGo.transform);
                RectTransform aRect = auroraGo.GetComponent<RectTransform>();
                aRect.anchorMin = new Vector2(0.5f, 0.5f);
                aRect.anchorMax = new Vector2(0.5f, 0.5f);
                aRect.anchoredPosition = auroraPositions[i];
                aRect.sizeDelta = auroraSizes[i];
                aRect.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 4f : -5f);

                Image auroraImg = auroraGo.AddComponent<Image>();
                auroraImg.sprite = christmas.AuroraCurtain;
                auroraImg.color = new Color(0.3f, 1f, 0.78f, 0.16f);
                auroraImg.raycastTarget = false;
                auroraImg.enabled = true;
                refs.AuroraRibbons[i] = auroraImg;
            }

            // 4. Snow Ground Fill covering lower horizon
            GameObject grassGo = CreateUiObject("SnowFloorFill", backdropGo.transform);
            RectTransform grassRect = grassGo.GetComponent<RectTransform>();
            grassRect.anchorMin = new Vector2(0f, 0f);
            grassRect.anchorMax = new Vector2(1f, 0.36f);
            grassRect.offsetMin = Vector2.zero;
            grassRect.offsetMax = Vector2.zero;
            Image grassImg = grassGo.AddComponent<Image>();
            grassImg.color = new Color(0.80f, 0.88f, 0.98f, 1f);
            grassImg.raycastTarget = false;
            refs.GrassBase = grassImg;

            // 5. Snow-Dusted Pine Tree Forest behind the foreground snowy mounds so trunks tuck cleanly behind snowbanks
            var pinePositions = new[]
            {
                new Vector2(-840f, -150f),
                new Vector2(-620f, -135f),
                new Vector2(-280f, -162f),
                new Vector2(280f, -162f),
                new Vector2(620f, -135f),
                new Vector2(840f, -150f)
            };
            var pineScales = new[] { 1.15f, 0.98f, 0.82f, 0.82f, 0.98f, 1.15f };
            refs.PineTrees = new Image[pinePositions.Length];
            for (int i = 0; i < pinePositions.Length; i++)
            {
                GameObject pineGo = CreateUiObject($"SnowyPine_{i + 1}", backdropGo.transform);
                RectTransform pRect = pineGo.GetComponent<RectTransform>();
                pRect.anchorMin = new Vector2(0.5f, 0.5f);
                pRect.anchorMax = new Vector2(0.5f, 0.5f);
                pRect.pivot = new Vector2(0.5f, 0f);
                pRect.anchoredPosition = pinePositions[i];
                pRect.sizeDelta = new Vector2(220f * pineScales[i], 285f * pineScales[i]);

                Image pineImg = pineGo.AddComponent<Image>();
                pineImg.sprite = christmas.SnowyPine;
                pineImg.preserveAspect = true;
                pineImg.color = new Color(0.92f, 0.99f, 0.96f, 1f);
                pineImg.raycastTarget = false;
                refs.PineTrees[i] = pineImg;
            }

            // 6. Rolling Snowy Hills along foreground horizon
            var hillX = new[] { -640f, 0f, 640f };
            var hillY = new[] { -220f, -205f, -225f };
            var hillSizes = new[] { new Vector2(1150f, 340f), new Vector2(1280f, 380f), new Vector2(1150f, 340f) };
            refs.SnowyHills = new Image[hillX.Length];
            for (int i = 0; i < hillX.Length; i++)
            {
                GameObject hillGo = CreateUiObject($"SnowyHill_{i + 1}", backdropGo.transform);
                RectTransform hRect = hillGo.GetComponent<RectTransform>();
                hRect.anchorMin = new Vector2(0.5f, 0.5f);
                hRect.anchorMax = new Vector2(0.5f, 0.5f);
                hRect.anchoredPosition = new Vector2(hillX[i], hillY[i]);
                hRect.sizeDelta = hillSizes[i];

                Image hillImg = hillGo.AddComponent<Image>();
                hillImg.sprite = christmas.SnowHill;
                hillImg.color = new Color(0.88f, 0.93f, 1f, 1f);
                hillImg.raycastTarget = false;
                refs.SnowyHills[i] = hillImg;
            }

            // 7. Overhead Christmas LED String Lights Garland beneath HeaderBar
            GameObject lightsRoot = CreateUiObject("ChristmasStringLights", backdropGo.transform);
            RectTransform lightsRootRect = lightsRoot.GetComponent<RectTransform>();
            StretchFull(lightsRootRect);

            const int bulbCount = 18;
            refs.ChristmasLedBulbs = new Image[bulbCount];
            Vector2[] bulbCoords = new Vector2[bulbCount];
            for (int i = 0; i < bulbCount; i++)
            {
                float t = bulbCount > 1 ? (float)i / (bulbCount - 1) : 0.5f;
                float x = Mathf.Lerp(-860f, 860f, t);
                float u = (t - 0.5f) * 2f;
                float sag = (1f - (u * u)) * 28f;
                bulbCoords[i] = new Vector2(x, 428f - sag);
            }

            // Draw garland cord segments between bulbs
            for (int i = 0; i < bulbCount - 1; i++)
            {
                Vector2 a = bulbCoords[i];
                Vector2 b = bulbCoords[i + 1];
                Vector2 mid = (a + b) * 0.5f;
                Vector2 diff = b - a;
                float length = diff.magnitude + 4f;
                float angleDeg = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

                GameObject cordGo = CreateUiObject($"GarlandWire_{i + 1}", lightsRoot.transform);
                RectTransform cRect = cordGo.GetComponent<RectTransform>();
                cRect.anchorMin = new Vector2(0.5f, 0.5f);
                cRect.anchorMax = new Vector2(0.5f, 0.5f);
                cRect.anchoredPosition = mid;
                cRect.sizeDelta = new Vector2(length, 5f);
                cRect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

                Image cordImg = cordGo.AddComponent<Image>();
                cordImg.color = new Color(0.08f, 0.28f, 0.16f, 0.92f);
                cordImg.raycastTarget = false;
            }

            // Create glowing LED bulbs
            Color[] initPalette =
            {
                new Color(1f, 0.24f, 0.28f, 1f),
                new Color(0.22f, 0.95f, 0.45f, 1f),
                new Color(1f, 0.84f, 0.18f, 1f),
                new Color(0.30f, 0.88f, 1f, 1f)
            };
            for (int i = 0; i < bulbCount; i++)
            {
                GameObject bulbGo = CreateUiObject($"ChristmasBulb_{i + 1}", lightsRoot.transform);
                RectTransform bRect = bulbGo.GetComponent<RectTransform>();
                bRect.anchorMin = new Vector2(0.5f, 0.5f);
                bRect.anchorMax = new Vector2(0.5f, 0.5f);
                bRect.anchoredPosition = bulbCoords[i] + new Vector2(0f, -12f);
                bRect.sizeDelta = new Vector2(36f, 36f);

                Image bulbImg = bulbGo.AddComponent<Image>();
                bulbImg.sprite = christmas.BulbGlow;
                bulbImg.color = initPalette[i % initPalette.Length];
                bulbImg.raycastTarget = false;
                refs.ChristmasLedBulbs[i] = bulbImg;
            }

            // 8. Santa's Magic Atmosphere Tint Overlay
            GameObject rainbowGo = CreateUiObject("SantaMagicAtmosphereOverlay", backdropGo.transform);
            StretchFull(rainbowGo.GetComponent<RectTransform>());
            Image rainbowImg = rainbowGo.AddComponent<Image>();
            rainbowImg.color = new Color(0.18f, 0.85f, 0.72f, 0f);
            rainbowImg.raycastTarget = false;
            rainbowImg.enabled = false;
            refs.PartyRainbowOverlay = rainbowImg;

            // 9. Santa's Magic Sweeping Northern Spotlights
            refs.PartySpotlights = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject beamGo = CreateUiObject($"MagicSpotlight_{i + 1}", backdropGo.transform);
                RectTransform beamRect = beamGo.GetComponent<RectTransform>();
                beamRect.anchorMin = new Vector2(0.5f, 1f);
                beamRect.anchorMax = new Vector2(0.5f, 1f);
                beamRect.pivot = new Vector2(0.5f, 1f);
                beamRect.anchoredPosition = new Vector2(i == 0 ? -520f : 520f, 40f);
                beamRect.sizeDelta = new Vector2(440f, 1250f);
                beamRect.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 18f : -18f);

                Image beamImg = beamGo.AddComponent<Image>();
                beamImg.sprite = spotlightBeamSprite;
                beamImg.color = new Color(1f, 1f, 1f, 0f);
                beamImg.raycastTarget = false;
                beamImg.enabled = false;
                refs.PartySpotlights[i] = beamImg;
            }

            // 10. Gentle Winter Snowfall ParticleSystem (continuous, ramps up cleanly on Snowstorm Boost)
            GameObject snowGo = CreateUiObject("SnowfallParticles", canvasTransform);
            RectTransform snowRect = snowGo.GetComponent<RectTransform>();
            snowRect.anchorMin = new Vector2(0.5f, 1f);
            snowRect.anchorMax = new Vector2(0.5f, 1f);
            snowRect.anchoredPosition = new Vector2(0f, 24f);
            snowRect.sizeDelta = new Vector2(1940f, 20f);
            snowRect.localPosition = new Vector3(0f, 560f, -1f);
            refs.SnowfallParticles = ConfigureSnowfallParticles(snowGo, christmas.SnowParticleMaterial);

            // 11. Santa's Magic Confetti & Sparkle Rain
            GameObject confettiGo = CreateUiObject("PartyConfettiParticles", canvasTransform);
            RectTransform confettiRect = confettiGo.GetComponent<RectTransform>();
            confettiRect.anchorMin = new Vector2(0.5f, 1f);
            confettiRect.anchorMax = new Vector2(0.5f, 1f);
            confettiRect.anchoredPosition = new Vector2(0f, 20f);
            confettiRect.sizeDelta = new Vector2(1880f, 20f);
            confettiRect.localPosition = new Vector3(0f, 560f, -1f);
            refs.PartyConfettiParticles = ConfigurePartyConfettiParticles(confettiGo, sparkleMaterial);

            // 12. Beat-Synced Snowflake Fireworks Bursts (triggered in Santa's Magic Mode)
            GameObject fireworksGo = CreateUiObject("SnowflakeFireworksParticles", canvasTransform);
            RectTransform fwRect = fireworksGo.GetComponent<RectTransform>();
            fwRect.anchorMin = new Vector2(0.5f, 0.5f);
            fwRect.anchorMax = new Vector2(0.5f, 0.5f);
            fwRect.anchoredPosition = new Vector2(0f, 250f);
            fwRect.sizeDelta = new Vector2(20f, 20f);
            fwRect.localPosition = new Vector3(0f, 250f, -1f);
            refs.SnowflakeFireworksParticles = ConfigureSnowflakeFireworksParticles(fireworksGo, christmas.SnowParticleMaterial);

            return refs;
        }

        private static List<CharacterSlot> BuildCharacterSlots(
            Transform canvasTransform,
            Sprite poloBaseSprite,
            Sprite glowRingSprite,
            Sprite roundedPillSprite,
            Material particleMaterial,
            AudioClip popClickSfx,
            ChristmasSprites christmas)
        {
            GameObject containerGo = CreateUiObject("StageSlots", canvasTransform);
            StretchFull(containerGo.GetComponent<RectTransform>());

            Font uiFont = ResolveBuiltinUiFont();
            Sprite discRing = LoadOrCreateDiscRingSprite();
            Sprite blinkHalf = ScratchAssetImporter.LoadSpriteAtPath("Assets/Art/CharactersPNG/Polo_Base/polo_blink_half.png");
            Sprite blinkClosed = ScratchAssetImporter.LoadSpriteAtPath("Assets/Art/CharactersPNG/Polo_Base/polo_blink_closed.png");

            var createdSlots = new List<CharacterSlot>(SlotCount);
            float centerOffset = (SlotCount - 1) * 0.5f;

            // Small hats only (no scarves): they sit on the crown and never cover the face.
            Sprite[] hatCycle = { christmas.SantaHat, christmas.ElfHollyCap };

            for (int i = 0; i < SlotCount; i++)
            {
                GameObject slotGo = CreateUiObject($"Character_Slot_{i + 1}", containerGo.transform);
                RectTransform slotRect = slotGo.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0f);
                slotRect.anchoredPosition = new Vector2((i - centerOffset) * SlotSpacingX, SlotBottomY);
                slotRect.sizeDelta = new Vector2(SlotWidth, SlotHeight);

                Image hitBox = slotGo.AddComponent<Image>();
                hitBox.color = new Color(1f, 1f, 1f, 0f);
                hitBox.raycastTarget = true;

                // 1. Beat glow ring planted in the snow under the feet
                GameObject ringGo = CreateUiObject("GlowRing", slotGo.transform);
                RectTransform ringRect = ringGo.GetComponent<RectTransform>();
                ringRect.anchorMin = new Vector2(0.5f, 0f);
                ringRect.anchorMax = new Vector2(0.5f, 0f);
                ringRect.pivot = new Vector2(0.5f, 0.5f);
                ringRect.anchoredPosition = new Vector2(0f, 14f);
                ringRect.sizeDelta = new Vector2(250f, 66f);

                Image ringImg = ringGo.AddComponent<Image>();
                ringImg.sprite = glowRingSprite;
                ringImg.color = new Color(1f, 1f, 1f, 0.18f);
                ringImg.raycastTarget = false;

                // 2. CharacterVisual = squash & stretch container (pivot at the feet)
                GameObject visualGo = CreateUiObject("CharacterVisual", slotGo.transform);
                RectTransform visualRect = visualGo.GetComponent<RectTransform>();
                visualRect.anchorMin = Vector2.zero;
                visualRect.anchorMax = Vector2.one;
                visualRect.pivot = new Vector2(0.5f, 0f);
                visualRect.offsetMin = Vector2.zero;
                visualRect.offsetMax = Vector2.zero;

                // Large BodySprite (600x600 for a 512px PNG => ~360px tall singers, shoulder to shoulder, feet in the snow)
                GameObject bodyGo = CreateUiObject("BodySprite", visualGo.transform);
                RectTransform bodyRect = bodyGo.GetComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0.5f, 0f);
                bodyRect.anchorMax = new Vector2(0.5f, 0f);
                bodyRect.pivot = new Vector2(0.5f, 0f);
                bodyRect.anchoredPosition = new Vector2(0f, BodyFootOffsetY);
                bodyRect.sizeDelta = new Vector2(BodySpriteSize, BodySpriteSize);

                Image charImage = bodyGo.AddComponent<Image>();
                charImage.sprite = poloBaseSprite;
                charImage.enabled = poloBaseSprite != null;
                charImage.preserveAspect = true;
                charImage.color = Color.white;
                charImage.raycastTarget = false;

                Shadow charShadow = bodyGo.AddComponent<Shadow>();
                charShadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
                charShadow.effectDistance = new Vector2(0f, -8f);
                charShadow.enabled = false;

                // Layer 2 sticker anchors (bottom-anchored so they follow the larger body)
                Image headAnchor = CreateBottomSticker("Anchor_Head", visualGo.transform, new Vector2(0f, 330f), new Vector2(160f, 120f), 0f);
                Image eyesAnchor = CreateBottomSticker("Anchor_Eyes", visualGo.transform, new Vector2(0f, 276f), new Vector2(130f, 56f), 0f);
                Image bodyAnchor = CreateBottomSticker("Anchor_Body", visualGo.transform, new Vector2(0f, 120f), new Vector2(140f, 120f), 0f);
                Image handAnchor = CreateBottomSticker("Anchor_Hand", visualGo.transform, new Vector2(92f, 120f), new Vector2(80f, 80f), 0f);

                // Christmas Hat / Elf Cap / Reindeer Antlers on top of CharacterVisual
                Image hatOverlay = CreateBottomSticker("ChristmasHatOverlay", visualGo.transform, new Vector2(18f, HatBottomY), new Vector2(HatWidth, HatHeight), -9f);

                // Mr. Tree Noel Overlay (Golden Star Topper Glow + 9 Beat-Pulsing Bauble Lights across the canopy)
                RectTransform mrTreeRoot = BuildMrTreeNoelOverlay(
                    visualGo.transform,
                    christmas.GoldenStarIcon,
                    christmas.BulbGlow,
                    out Image mrTreeStarGlow,
                    out Image[] mrTreeBaubles);

                // 3. Hover-only controls bar (Mute / Solo / Remove) above the head
                GameObject controlsBar = CreateUiObject("ControlsBar", slotGo.transform);
                RectTransform barRect = controlsBar.GetComponent<RectTransform>();
                barRect.anchorMin = new Vector2(0.5f, 0f);
                barRect.anchorMax = new Vector2(0.5f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.anchoredPosition = new Vector2(0f, ControlsBarY);
                barRect.sizeDelta = new Vector2(150f, 38f);

                HorizontalLayoutGroup barLayout = controlsBar.AddComponent<HorizontalLayoutGroup>();
                barLayout.childAlignment = TextAnchor.MiddleCenter;
                barLayout.spacing = 6f;
                barLayout.childControlWidth = false;
                barLayout.childControlHeight = false;

                Button muteBtn = CreateMiniControlButton("MuteButton", "M", new Color(0.96f, 0.97f, 1f, 0.96f), controlsBar.transform, uiFont, roundedPillSprite);
                Button soloBtn = CreateMiniControlButton("SoloButton", "S", new Color(0.96f, 0.97f, 1f, 0.96f), controlsBar.transform, uiFont, roundedPillSprite);
                Button removeBtn = CreateMiniControlButton("RemoveButton", "X", new Color(1f, 0.54f, 0.54f, 0.96f), controlsBar.transform, uiFont, roundedPillSprite);
                controlsBar.SetActive(false);

                // 4. Queue countdown pie (visible only while the drop waits for the next bar downbeat)
                GameObject pieGo = CreateUiObject("QueuePie", slotGo.transform);
                RectTransform pieRect = pieGo.GetComponent<RectTransform>();
                pieRect.anchorMin = new Vector2(0.5f, 0f);
                pieRect.anchorMax = new Vector2(0.5f, 0f);
                pieRect.pivot = new Vector2(0.5f, 0f);
                pieRect.anchoredPosition = new Vector2(0f, ControlsBarY + 4f);
                pieRect.sizeDelta = new Vector2(64f, 64f);
                Image pieImg = pieGo.AddComponent<Image>();
                pieImg.sprite = discRing;
                pieImg.type = Image.Type.Filled;
                pieImg.fillMethod = Image.FillMethod.Radial360;
                pieImg.fillOrigin = (int)Image.Origin360.Top;
                pieImg.fillClockwise = true;
                pieImg.fillAmount = 0f;
                pieImg.color = new Color(1f, 0.86f, 0.28f, 1f);
                pieImg.raycastTarget = false;
                pieGo.SetActive(false);

                // 5. Drop & tap confetti burst
                GameObject burstGo = CreateUiObject("DropBurstParticles", slotGo.transform);
                RectTransform burstRect = burstGo.GetComponent<RectTransform>();
                burstRect.anchorMin = new Vector2(0.5f, 0f);
                burstRect.anchorMax = new Vector2(0.5f, 0f);
                burstRect.anchoredPosition = new Vector2(0f, 190f);
                burstRect.localPosition = new Vector3(0f, 190f, -1f);
                ParticleSystem burstPs = ConfigureDropBurstParticles(burstGo, particleMaterial);

                // 6. Audio sources
                AudioSource audioSource = slotGo.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.loop = false;
                audioSource.spatialBlend = 0f;

                GameObject sfxChild = new GameObject("TapSfxSource");
                sfxChild.transform.SetParent(slotGo.transform, false);
                AudioSource tapSfxSource = sfxChild.AddComponent<AudioSource>();
                tapSfxSource.playOnAwake = false;
                tapSfxSource.loop = false;
                tapSfxSource.spatialBlend = 0f;

                CharacterSlot slot = slotGo.AddComponent<CharacterSlot>();

                SerializedObject so = new SerializedObject(slot);
                so.FindProperty("slotIndex").intValue = i;
                so.FindProperty("characterImage").objectReferenceValue = charImage;
                so.FindProperty("visualTransform").objectReferenceValue = visualRect;
                so.FindProperty("audioSource").objectReferenceValue = audioSource;
                so.FindProperty("defaultIdleSprite").objectReferenceValue = poloBaseSprite;
                so.FindProperty("emptyAlpha").floatValue = 1f;
                so.FindProperty("emptyBlinkHalfSprite").objectReferenceValue = blinkHalf;
                so.FindProperty("emptyBlinkClosedSprite").objectReferenceValue = blinkClosed;
                so.FindProperty("glowRingImage").objectReferenceValue = ringImg;
                so.FindProperty("characterShadow").objectReferenceValue = charShadow;
                so.FindProperty("queuePieImage").objectReferenceValue = pieImg;
                so.FindProperty("headAnchorImage").objectReferenceValue = headAnchor;
                so.FindProperty("eyesAnchorImage").objectReferenceValue = eyesAnchor;
                so.FindProperty("bodyAnchorImage").objectReferenceValue = bodyAnchor;
                so.FindProperty("handAnchorImage").objectReferenceValue = handAnchor;
                so.FindProperty("christmasHatOverlay").objectReferenceValue = hatOverlay;
                so.FindProperty("defaultChristmasHatSprite").objectReferenceValue = christmas.SantaHat;
                so.FindProperty("reindeerAntlersSprite").objectReferenceValue = christmas.ReindeerAntlers;
                so.FindProperty("elfHollyCapSprite").objectReferenceValue = christmas.ElfHollyCap;
                so.FindProperty("mrTreeNoelRoot").objectReferenceValue = mrTreeRoot;
                so.FindProperty("mrTreeTopStarGlow").objectReferenceValue = mrTreeStarGlow;
                AssignObjectArrayProperty(so.FindProperty("mrTreeBaubleGlows"), mrTreeBaubles);
                so.FindProperty("showChristmasCostumeWhenEmpty").boolValue = true;
                so.FindProperty("showChristmasCostumeWhenActive").boolValue = true;
                so.FindProperty("alwaysShowControlsWhenActive").boolValue = false;
                so.FindProperty("removeOnMouseClick").boolValue = true;
                so.FindProperty("controlsRoot").objectReferenceValue = controlsBar;
                so.FindProperty("muteButton").objectReferenceValue = muteBtn;
                so.FindProperty("soloButton").objectReferenceValue = soloBtn;
                so.FindProperty("removeButton").objectReferenceValue = removeBtn;
                so.FindProperty("tapParticles").objectReferenceValue = burstPs;
                so.FindProperty("sfxOneShotSource").objectReferenceValue = tapSfxSource;
                so.FindProperty("fallbackTapSfx").objectReferenceValue = popClickSfx;
                so.ApplyModifiedPropertiesWithoutUndo();

                createdSlots.Add(slot);
            }

            return createdSlots;
        }

        private static RectTransform BuildMrTreeNoelOverlay(
            Transform visualParent,
            Sprite goldenStarSprite,
            Sprite bulbGlowSprite,
            out Image topStarGlow,
            out Image[] baubleGlows)
        {
            GameObject rootGo = CreateUiObject("MrTreeNoelOverlay", visualParent);
            RectTransform rootRect = rootGo.GetComponent<RectTransform>();
            StretchFull(rootRect);

            // Top Star Glow aligned with the Golden Star at the peak of Mr. Tree's canopy
            GameObject starGo = CreateUiObject("TopStarGlow", rootGo.transform);
            RectTransform starRect = starGo.GetComponent<RectTransform>();
            starRect.anchorMin = new Vector2(0.5f, 0f);
            starRect.anchorMax = new Vector2(0.5f, 0f);
            starRect.pivot = new Vector2(0.5f, 0.5f);
            starRect.anchoredPosition = new Vector2(3f, 438f);
            starRect.sizeDelta = new Vector2(88f, 88f);
            topStarGlow = starGo.AddComponent<Image>();
            topStarGlow.sprite = goldenStarSprite != null ? goldenStarSprite : bulbGlowSprite;
            topStarGlow.preserveAspect = true;
            topStarGlow.color = new Color(1f, 0.94f, 0.42f, 0.85f);
            topStarGlow.raycastTarget = false;

            // 9 Bauble Glow lights aligned with the 9 ornaments across Mr. Tree's green canopy
            Vector2[] baubleOffsets =
            {
                new Vector2(-91f, 346f), // Ruby Red Left
                new Vector2(-34f, 378f), // Gold Upper-Left
                new Vector2(42f,  376f), // Ice Blue Upper-Right
                new Vector2(98f,  342f), // Royal Purple Right
                new Vector2(-64f, 290f), // Sapphire Lower-Left
                new Vector2(3f,   335f), // Ruby Center Jewel
                new Vector2(72f,  288f), // Gold Lower-Right
                new Vector2(-110f, 298f), // Magenta Outer-Left
                new Vector2(116f,  296f), // Emerald Outer-Right
            };

            baubleGlows = new Image[baubleOffsets.Length];
            for (int i = 0; i < baubleOffsets.Length; i++)
            {
                GameObject bGo = CreateUiObject($"NoelBaubleGlow_{i + 1}", rootGo.transform);
                RectTransform bRect = bGo.GetComponent<RectTransform>();
                bRect.anchorMin = new Vector2(0.5f, 0f);
                bRect.anchorMax = new Vector2(0.5f, 0f);
                bRect.pivot = new Vector2(0.5f, 0.5f);
                bRect.anchoredPosition = baubleOffsets[i];
                bRect.sizeDelta = new Vector2(44f, 44f);

                Image bImg = bGo.AddComponent<Image>();
                bImg.sprite = bulbGlowSprite;
                bImg.preserveAspect = true;
                bImg.raycastTarget = false;
                baubleGlows[i] = bImg;
            }

            rootGo.SetActive(false);
            return rootRect;
        }

        private static Image CreateBottomSticker(string name, Transform parent, Vector2 bottomPos, Vector2 size, float rotZ)
        {
            GameObject go = CreateUiObject(name, parent);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = bottomPos;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;
            return img;
        }

        private static HeaderUiRefs BuildHeaderBar(Transform canvasTransform, Sprite roundedPillSprite)
        {
            var refs = new HeaderUiRefs();
            Font uiFont = ResolveBuiltinUiFont();
            Sprite discRing = LoadOrCreateDiscRingSprite();

            // Slim, elegant header
            GameObject headerGo = CreateUiObject("HeaderBar", canvasTransform);
            RectTransform headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 1f);
            headerRect.anchorMax = new Vector2(0.5f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -8f);
            headerRect.sizeDelta = new Vector2(1880f, 58f);

            Image headerBg = headerGo.AddComponent<Image>();
            headerBg.sprite = roundedPillSprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = new Color(0.03f, 0.07f, 0.18f, 0.82f);

            Shadow headerShadow = headerGo.AddComponent<Shadow>();
            headerShadow.effectColor = new Color(0f, 0f, 0f, 0.30f);
            headerShadow.effectDistance = new Vector2(0f, -4f);

            GameObject leftGroup = CreateUiObject("TitleGroup", headerGo.transform);
            RectTransform leftRect = leftGroup.GetComponent<RectTransform>();
            leftRect.anchorMin = new Vector2(0f, 0.5f);
            leftRect.anchorMax = new Vector2(0f, 0.5f);
            leftRect.pivot = new Vector2(0f, 0.5f);
            leftRect.anchoredPosition = new Vector2(24f, 0f);
            leftRect.sizeDelta = new Vector2(290f, 48f);

            Text titleText = CreateUiObject("BrandTitle", leftGroup.transform).AddComponent<Text>();
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0.45f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            titleText.font = uiFont;
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(1f, 0.95f, 0.72f, 1f);
            titleText.text = "SPRUNKI CHRISTMAS";
            titleText.raycastTarget = false;

            Text countText = CreateUiObject("ActiveCountText", leftGroup.transform).AddComponent<Text>();
            RectTransform countRect = countText.rectTransform;
            countRect.anchorMin = new Vector2(0f, 0f);
            countRect.anchorMax = new Vector2(1f, 0.45f);
            countRect.offsetMin = Vector2.zero;
            countRect.offsetMax = Vector2.zero;
            countText.font = uiFont;
            countText.fontSize = 14;
            countText.fontStyle = FontStyle.Bold;
            countText.alignment = TextAnchor.MiddleLeft;
            countText.color = new Color(0.62f, 0.94f, 1f, 1f);
            countText.text = "0/7 Singers";
            countText.raycastTarget = false;
            refs.ActiveCountText = countText;

            // Compact beat LEDs
            GameObject ledRowGo = CreateUiObject("BeatLeds", headerGo.transform);
            RectTransform ledRowRect = ledRowGo.GetComponent<RectTransform>();
            ledRowRect.anchorMin = new Vector2(0f, 0.5f);
            ledRowRect.anchorMax = new Vector2(0f, 0.5f);
            ledRowRect.pivot = new Vector2(0f, 0.5f);
            ledRowRect.anchoredPosition = new Vector2(280f, 0f);
            ledRowRect.sizeDelta = new Vector2(196f, 22f);

            HorizontalLayoutGroup ledLayout = ledRowGo.AddComponent<HorizontalLayoutGroup>();
            ledLayout.childAlignment = TextAnchor.MiddleCenter;
            ledLayout.spacing = 6f;
            ledLayout.childControlWidth = false;
            ledLayout.childControlHeight = false;

            const int ledCount = 8;
            refs.BeatLeds = new Image[ledCount];
            for (int i = 0; i < ledCount; i++)
            {
                GameObject ledGo = CreateUiObject($"BeatLED_{i + 1}", ledRowGo.transform);
                ledGo.GetComponent<RectTransform>().sizeDelta = new Vector2(18f, 14f);
                Image ledImg = ledGo.AddComponent<Image>();
                ledImg.sprite = roundedPillSprite;
                ledImg.type = Image.Type.Sliced;
                ledImg.color = new Color(1f, 1f, 1f, 0.25f);
                ledImg.raycastTarget = false;
                refs.BeatLeds[i] = ledImg;
            }

            GameObject rightGroup = CreateUiObject("HeaderActions", headerGo.transform);
            RectTransform rightRect = rightGroup.GetComponent<RectTransform>();
            rightRect.anchorMin = new Vector2(1f, 0.5f);
            rightRect.anchorMax = new Vector2(1f, 0.5f);
            rightRect.pivot = new Vector2(1f, 0.5f);
            rightRect.anchoredPosition = new Vector2(-12f, 0f);
            rightRect.sizeDelta = new Vector2(800f, 42f);

            HorizontalLayoutGroup rightLayout = rightGroup.AddComponent<HorizontalLayoutGroup>();
            rightLayout.childAlignment = TextAnchor.MiddleRight;
            rightLayout.spacing = 8f;
            rightLayout.childControlWidth = false;
            rightLayout.childControlHeight = false;

            refs.AutoRandomButton = CreatePillActionButton("AutoRandomButton", "▶ Auto Random (02:00)", new Color(0.95f, 0.50f, 0.12f, 1f),
                new Vector2(220f, 38f), rightGroup.transform, uiFont, roundedPillSprite, out refs.AutoRandomButtonBg, out refs.AutoRandomButtonLabel, fontSize: 15);
            refs.SnowstormButton = CreatePillActionButton("SnowstormButton", "Snowstorm", new Color(0.26f, 0.52f, 0.84f, 1f),
                new Vector2(132f, 38f), rightGroup.transform, uiFont, roundedPillSprite, out refs.SnowstormButtonBg, out refs.SnowstormButtonLabel, fontSize: 15);
            refs.PartyButton = CreatePillActionButton("PartyModeButton", "Santa Magic", new Color(0.18f, 0.60f, 0.44f, 1f),
                new Vector2(138f, 38f), rightGroup.transform, uiFont, roundedPillSprite, out refs.PartyButtonBg, out refs.PartyButtonLabel, fontSize: 15);

            // Top-right stage corner: Reset button wrapped by the circular bar/beat progress pie
            GameObject clusterGo = CreateUiObject("ResetCluster", canvasTransform);
            RectTransform clusterRect = clusterGo.GetComponent<RectTransform>();
            clusterRect.anchorMin = new Vector2(1f, 1f);
            clusterRect.anchorMax = new Vector2(1f, 1f);
            clusterRect.pivot = new Vector2(1f, 1f);
            clusterRect.anchoredPosition = new Vector2(-26f, -78f);
            clusterRect.sizeDelta = new Vector2(112f, 112f);

            GameObject trackGo = CreateUiObject("BeatPieTrack", clusterGo.transform);
            StretchFull(trackGo.GetComponent<RectTransform>());
            Image trackImg = trackGo.AddComponent<Image>();
            trackImg.sprite = discRing;
            trackImg.color = new Color(0.03f, 0.07f, 0.18f, 0.55f);
            trackImg.raycastTarget = false;

            GameObject pieGo = CreateUiObject("BeatPieFill", clusterGo.transform);
            StretchFull(pieGo.GetComponent<RectTransform>());
            Image pieImg = pieGo.AddComponent<Image>();
            pieImg.sprite = discRing;
            pieImg.type = Image.Type.Filled;
            pieImg.fillMethod = Image.FillMethod.Radial360;
            pieImg.fillOrigin = (int)Image.Origin360.Top;
            pieImg.fillClockwise = true;
            pieImg.fillAmount = 0f;
            pieImg.color = new Color(0.32f, 0.90f, 1f, 1f);
            pieImg.raycastTarget = false;
            refs.LoopProgressFill = pieImg;

            refs.ResetButton = CreatePillActionButton("ResetStageButton", "RESET", new Color(0.90f, 0.32f, 0.34f, 1f),
                new Vector2(66f, 38f), clusterGo.transform, uiFont, roundedPillSprite, out _, out _, fontSize: 15);
            RectTransform resetRect = refs.ResetButton.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0.5f, 0.5f);
            resetRect.anchorMax = new Vector2(0.5f, 0.5f);
            resetRect.anchoredPosition = Vector2.zero;

            // Virtual DJ Hand / Ghost Icon for Auto-Play DJ Showcase
            Sprite defaultBadge = AssetDatabase.LoadAssetAtPath<Sprite>(IconCircleTexturePath);
            GameObject ghostGo = CreateUiObject("DjGhostCursor", canvasTransform);
            RectTransform ghostRect = ghostGo.GetComponent<RectTransform>();
            ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRect.anchorMax = new Vector2(0.5f, 0.5f);
            ghostRect.pivot = new Vector2(0.5f, 0.5f);
            ghostRect.sizeDelta = new Vector2(114f, 114f);

            CanvasGroup ghostCg = ghostGo.AddComponent<CanvasGroup>();
            ghostCg.blocksRaycasts = false;
            ghostCg.interactable = false;
            ghostCg.alpha = 0.96f;

            Image ghostBadge = ghostGo.AddComponent<Image>();
            ghostBadge.sprite = defaultBadge != null ? defaultBadge : roundedPillSprite;
            ghostBadge.preserveAspect = true;
            ghostBadge.raycastTarget = false;
            ghostBadge.color = new Color(1f, 0.84f, 0.22f, 0.96f);

            Outline ghostOutline = ghostGo.AddComponent<Outline>();
            ghostOutline.effectColor = new Color(1f, 1f, 1f, 0.92f);
            ghostOutline.effectDistance = new Vector2(3f, -3f);

            GameObject ghostPortraitGo = CreateUiObject("Portrait", ghostGo.transform);
            RectTransform gpRect = ghostPortraitGo.GetComponent<RectTransform>();
            gpRect.anchorMin = new Vector2(0.05f, 0.05f);
            gpRect.anchorMax = new Vector2(0.95f, 0.95f);
            gpRect.offsetMin = Vector2.zero;
            gpRect.offsetMax = Vector2.zero;
            Image ghostPortrait = ghostPortraitGo.AddComponent<Image>();
            ghostPortrait.preserveAspect = true;
            ghostPortrait.raycastTarget = false;

            GameObject ghostTagGo = CreateUiObject("DjTag", ghostGo.transform);
            RectTransform gtRect = ghostTagGo.GetComponent<RectTransform>();
            gtRect.anchorMin = new Vector2(0.5f, 0f);
            gtRect.anchorMax = new Vector2(0.5f, 0f);
            gtRect.pivot = new Vector2(0.5f, 1f);
            gtRect.anchoredPosition = new Vector2(0f, -4f);
            gtRect.sizeDelta = new Vector2(106f, 26f);
            Image ghostTagBg = ghostTagGo.AddComponent<Image>();
            ghostTagBg.sprite = roundedPillSprite;
            ghostTagBg.type = Image.Type.Sliced;
            ghostTagBg.color = new Color(0.06f, 0.12f, 0.28f, 0.95f);
            ghostTagBg.raycastTarget = false;

            GameObject ghostTxtGo = CreateUiObject("Text", ghostTagGo.transform);
            StretchFull(ghostTxtGo.GetComponent<RectTransform>());
            Text ghostTagTxt = ghostTxtGo.AddComponent<Text>();
            ghostTagTxt.font = uiFont;
            ghostTagTxt.fontSize = 14;
            ghostTagTxt.fontStyle = FontStyle.Bold;
            ghostTagTxt.alignment = TextAnchor.MiddleCenter;
            ghostTagTxt.color = new Color(1f, 0.92f, 0.36f, 1f);
            ghostTagTxt.text = "🎧 DJ";
            ghostTagTxt.raycastTarget = false;

            ghostGo.SetActive(false);
            refs.DjGhostRoot = ghostRect;
            refs.DjGhostBadge = ghostBadge;
            refs.DjGhostPortrait = ghostPortrait;
            refs.DjGhostTagText = ghostTagTxt;

            return refs;
        }

        private static RectTransform BuildIconTray(
            Canvas canvas,
            List<CharacterSoundData> characterAssets,
            Sprite defaultBadge,
            Sprite roundedPillSprite,
            Sprite goldenStarSprite)
        {
            const int columns = 10;
            const float cell = 108f;
            const float gap = 12f;
            const int pad = 16;
            float width = columns * cell + (columns - 1) * gap + pad * 2;
            float height = 2 * cell + gap + pad * 2;

            GameObject trayGo = CreateUiObject("IconTray", canvas.transform);
            RectTransform trayRect = trayGo.GetComponent<RectTransform>();
            trayRect.anchorMin = new Vector2(0.5f, 0f);
            trayRect.anchorMax = new Vector2(0.5f, 0f);
            trayRect.pivot = new Vector2(0.5f, 0f);
            trayRect.anchoredPosition = new Vector2(0f, 8f);
            trayRect.sizeDelta = new Vector2(width, height);

            Image trayBg = trayGo.AddComponent<Image>();
            trayBg.sprite = roundedPillSprite;
            trayBg.type = Image.Type.Sliced;
            trayBg.color = new Color(0.02f, 0.04f, 0.10f, 0.95f);

            Outline trayOutline = trayGo.AddComponent<Outline>();
            trayOutline.effectColor = new Color(0.35f, 0.50f, 0.85f, 0.35f);
            trayOutline.effectDistance = new Vector2(2f, -2f);

            Shadow trayShadow = trayGo.AddComponent<Shadow>();
            trayShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            trayShadow.effectDistance = new Vector2(0f, -6f);

            // Fixed 2 x 10 grid dock: no scrolling, every one of the 20 icons is always visible
            GameObject gridGo = CreateUiObject("IconGrid", trayGo.transform);
            StretchFull(gridGo.GetComponent<RectTransform>());

            GridLayoutGroup grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(pad, pad, pad, pad);
            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(gap, gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            PopulateTrayIcons(gridGo.transform, canvas, characterAssets, defaultBadge, goldenStarSprite, cell);
            return trayRect;
        }

        private static void PopulateTrayIcons(
            Transform gridParent,
            Canvas rootCanvas,
            List<CharacterSoundData> characterAssets,
            Sprite defaultBadge,
            Sprite goldenStarSprite,
            float cellSize)
        {
            int count = 20;
            for (int i = 0; i < count; i++)
            {
                CharacterSoundData data = i < characterAssets.Count ? characterAssets[i] : null;
                bool isMagicStar = (data != null && data.IsPartyTriggerItem) || i == count - 1;
                string itemName = data != null ? $"Icon_{i + 1:D2}_{data.DisplayName}" : $"Icon_{i + 1:D2}";

                // Placeholder cell keeps the grid stable while an icon is being dragged out of it
                GameObject cellGo = CreateUiObject($"Cell_{i + 1:D2}", gridParent);

                GameObject iconGo = CreateUiObject(itemName, cellGo.transform);
                RectTransform iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(cellSize, cellSize);

                Image badgeImg = iconGo.AddComponent<Image>();
                badgeImg.sprite = defaultBadge;
                badgeImg.preserveAspect = true;
                badgeImg.color = isMagicStar
                    ? new Color(1f, 0.843f, 0f, 1f)
                    : (data != null ? data.ThemeColor : Color.HSVToRGB((float)i / Math.Max(1, count), 0.72f, 0.96f));

                GameObject portraitGo = CreateUiObject("Portrait", iconGo.transform);
                RectTransform portraitRect = portraitGo.GetComponent<RectTransform>();
                portraitRect.anchorMin = new Vector2(0.03f, 0.03f);
                portraitRect.anchorMax = new Vector2(0.97f, 0.97f);
                portraitRect.offsetMin = Vector2.zero;
                portraitRect.offsetMax = Vector2.zero;

                Image portraitImg = portraitGo.AddComponent<Image>();
                portraitImg.preserveAspect = true;
                portraitImg.raycastTarget = false;
                portraitImg.enabled = true;

                Sprite portrait = null;
                if (isMagicStar)
                {
                    portrait = goldenStarSprite != null ? goldenStarSprite : (data != null ? (data.IconSprite ?? data.IdleSprite) : null);
                }
                else if (data != null)
                {
                    portrait = data.IconSprite != null ? data.IconSprite : data.IdleSprite;
                }

                portraitImg.sprite = portrait != null ? portrait : defaultBadge;
                portraitImg.color = portrait != null ? Color.white : new Color(1f, 1f, 1f, 0.35f);

                iconGo.AddComponent<CanvasGroup>();
                DraggableIcon draggable = iconGo.AddComponent<DraggableIcon>();

                SerializedObject so = new SerializedObject(draggable);
                so.FindProperty("characterData").objectReferenceValue = data;
                so.FindProperty("characterSprite").objectReferenceValue = data != null ? (data.IdleSprite ?? data.IconSprite) : null;
                so.FindProperty("loopAudio").objectReferenceValue = data != null ? data.MainLoopClip : null;
                so.FindProperty("iconImage").objectReferenceValue = portraitImg;
                so.FindProperty("backgroundBadge").objectReferenceValue = badgeImg;
                so.FindProperty("rootCanvas").objectReferenceValue = rootCanvas;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Sprite LoadOrCreateDiscRingSprite()
        {
            EnsureUnityFolderExists(GeneratedUiArtFolder);
            WriteTextureIfMissing(QueueDiscTexturePath, () => BuildDiscRingTexture(128), Vector4.zero);
            return AssetDatabase.LoadAssetAtPath<Sprite>(QueueDiscTexturePath);
        }

        private static Texture2D BuildDiscRingTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                float ny = (y / (float)(size - 1)) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x / (float)(size - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float outer = 1f - Mathf.SmoothStep(0.90f, 1.0f, r);
                    float inner = Mathf.SmoothStep(0.56f, 0.64f, r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(outer * inner)));
                }
            }
            tex.Apply();
            return tex;
        }

        private static void WireStageController(
            GameObject gmGo,
            AudioSyncManager syncManager,
            List<CharacterSlot> slots,
            List<CharacterSoundData> availableCharacters,
            StageBackdropRefs stageRefs,
            HeaderUiRefs headerRefs,
            AudioClip popClickSfx)
        {
            AudioSource uiSfxSource = gmGo.AddComponent<AudioSource>();
            uiSfxSource.playOnAwake = false;
            uiSfxSource.loop = false;
            uiSfxSource.spatialBlend = 0f;

            SprunkiStageController controller = gmGo.AddComponent<SprunkiStageController>();

            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("syncManager").objectReferenceValue = syncManager;

            AssignObjectArrayProperty(so.FindProperty("characterSlots"), slots);
            AssignObjectArrayProperty(so.FindProperty("availableCharacters"), availableCharacters);

            so.FindProperty("resetStageButton").objectReferenceValue = headerRefs.ResetButton;
            so.FindProperty("partyModeButton").objectReferenceValue = headerRefs.PartyButton;
            so.FindProperty("partyButtonBackground").objectReferenceValue = headerRefs.PartyButtonBg;
            so.FindProperty("partyButtonLabel").objectReferenceValue = headerRefs.PartyButtonLabel;
            so.FindProperty("autoRandomButton").objectReferenceValue = headerRefs.AutoRandomButton;
            so.FindProperty("autoRandomButtonBackground").objectReferenceValue = headerRefs.AutoRandomButtonBg;
            so.FindProperty("autoRandomButtonLabel").objectReferenceValue = headerRefs.AutoRandomButtonLabel;
            so.FindProperty("snowstormButton").objectReferenceValue = headerRefs.SnowstormButton;
            so.FindProperty("snowstormButtonBackground").objectReferenceValue = headerRefs.SnowstormButtonBg;
            so.FindProperty("snowstormButtonLabel").objectReferenceValue = headerRefs.SnowstormButtonLabel;
            so.FindProperty("activeCountText").objectReferenceValue = headerRefs.ActiveCountText;
            so.FindProperty("djGhostRoot").objectReferenceValue = headerRefs.DjGhostRoot;
            so.FindProperty("djGhostBadge").objectReferenceValue = headerRefs.DjGhostBadge;
            so.FindProperty("djGhostPortrait").objectReferenceValue = headerRefs.DjGhostPortrait;
            so.FindProperty("djGhostTagText").objectReferenceValue = headerRefs.DjGhostTagText;

            AssignObjectArrayProperty(so.FindProperty("beatLeds"), headerRefs.BeatLeds);
            so.FindProperty("loopProgressFill").objectReferenceValue = headerRefs.LoopProgressFill;

            so.FindProperty("skyBaseImage").objectReferenceValue = stageRefs.SkyBase;
            so.FindProperty("grassBaseImage").objectReferenceValue = stageRefs.GrassBase;
            so.FindProperty("stageArtworkImage").objectReferenceValue = stageRefs.StageArtwork;
            so.FindProperty("partyRainbowOverlay").objectReferenceValue = stageRefs.PartyRainbowOverlay;

            AssignObjectArrayProperty(so.FindProperty("partySpotlights"), stageRefs.PartySpotlights);
            AssignObjectArrayProperty(so.FindProperty("snowyHillImages"), stageRefs.SnowyHills);
            AssignObjectArrayProperty(so.FindProperty("pineTreeImages"), stageRefs.PineTrees);
            AssignObjectArrayProperty(so.FindProperty("auroraRibbons"), stageRefs.AuroraRibbons);
            AssignObjectArrayProperty(so.FindProperty("christmasLedBulbs"), stageRefs.ChristmasLedBulbs);

            so.FindProperty("partyConfettiParticles").objectReferenceValue = stageRefs.PartyConfettiParticles;
            so.FindProperty("snowfallParticles").objectReferenceValue = stageRefs.SnowfallParticles;
            so.FindProperty("snowflakeFireworksParticles").objectReferenceValue = stageRefs.SnowflakeFireworksParticles;
            so.FindProperty("uiSfxSource").objectReferenceValue = uiSfxSource;
            so.FindProperty("uiClickClip").objectReferenceValue = popClickSfx;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignObjectArrayProperty<T>(SerializedProperty prop, IReadOnlyList<T> items) where T : UnityEngine.Object
        {
            if (prop == null) return;
            prop.arraySize = items != null ? items.Count : 0;
            for (int i = 0; i < prop.arraySize; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        private static ParticleSystem ConfigureDropBurstParticles(GameObject targetGo, Material particleMat)
        {
            ParticleSystem ps = targetGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.7f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(180f, 340f);
            main.startSize = new ParticleSystem.MinMaxCurve(18f, 34f);
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 32;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 45f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.8f),
                new Keyframe(0.25f, 1.15f),
                new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer renderer = targetGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMat;
            renderer.sortingOrder = 15;

            return ps;
        }

        private static ParticleSystem ConfigureSnowfallParticles(GameObject targetGo, Material snowMat)
        {
            ParticleSystem ps = targetGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.88f, 0.96f, 1f, 0.72f),
                new Color(1f, 1f, 1f, 0.95f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 700;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 70f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1960f, 24f, 1f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-28f, 28f);
            vel.y = new ParticleSystem.MinMaxCurve(-160f, -92f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var fadeGrad = new Gradient();
            fadeGrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.92f, 0.1f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(fadeGrad);

            ParticleSystemRenderer renderer = targetGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = snowMat;
            renderer.sortingOrder = 11;

            return ps;
        }

        private static ParticleSystem ConfigureSnowflakeFireworksParticles(GameObject targetGo, Material snowMat)
        {
            ParticleSystem ps = targetGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(220f, 480f);
            main.startSize = new ParticleSystem.MinMaxCurve(20f, 38f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 96;

            var festiveGrad = new Gradient();
            festiveGrad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.88f, 0.28f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.96f, 1f), 0.35f),
                    new GradientColorKey(new Color(0.42f, 1f, 0.65f), 0.7f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.65f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(festiveGrad);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 280f;

            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3.5f, 3.5f);

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var fadeGrad = new Gradient();
            fadeGrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(fadeGrad);

            ParticleSystemRenderer renderer = targetGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = snowMat;
            renderer.sortingOrder = 14;

            return ps;
        }

        private static ParticleSystem ConfigurePartyConfettiParticles(GameObject targetGo, Material particleMat)
        {
            ParticleSystem ps = targetGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.6f, 5.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(18f, 34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 160;

            var festiveColors = new Gradient();
            festiveColors.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.25f, 0.38f), 0.0f),
                    new GradientColorKey(new Color(1f, 0.85f, 0.15f), 0.30f),
                    new GradientColorKey(new Color(0.25f, 0.95f, 0.52f), 0.65f),
                    new GradientColorKey(new Color(0.32f, 0.88f, 1.0f), 1.0f)
                },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.95f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(festiveColors);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 24f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1860f, 20f, 1f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-55f, 55f);
            vel.y = new ParticleSystem.MinMaxCurve(-270f, -150f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var fadeGrad = new Gradient();
            fadeGrad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.95f, 0.82f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(fadeGrad);

            ParticleSystemRenderer renderer = targetGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMat;
            renderer.sortingOrder = 13;

            return ps;
        }

        private static List<CharacterSoundData> LoadAllCharacterSoundData()
        {
            string[] guids = AssetDatabase.FindAssets("t:CharacterSoundData");
            var list = new List<CharacterSoundData>(guids.Length);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CharacterSoundData asset = AssetDatabase.LoadAssetAtPath<CharacterSoundData>(path);
                if (asset != null)
                {
                    list.Add(asset);
                }
            }

            return list.OrderBy(c => c.SortOrder).ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ShortenDisplayLabel(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "Sprunki";
            int openParen = fullName.IndexOf('(');
            int closeParen = fullName.IndexOf(')');
            if (openParen >= 0 && closeParen > openParen + 1)
            {
                return fullName.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            }
            return fullName.Trim();
        }

        private static Image CreateStickerAnchor(string name, Transform parent, Vector2 offset, Vector2 size)
        {
            GameObject anchorGo = CreateUiObject(name, parent);
            RectTransform rect = anchorGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;

            Image img = anchorGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;
            return img;
        }

        private static Button CreateMiniControlButton(string name, string caption, Color bgColor, Transform parent, Font font, Sprite pillSprite)
        {
            GameObject btnGo = CreateUiObject(name, parent);
            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(46f, 40f);

            Image img = btnGo.AddComponent<Image>();
            img.sprite = pillSprite;
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            Shadow shadow = btnGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -3f);

            Button btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = img;

            GameObject textGo = CreateUiObject("Text", btnGo.transform);
            StretchFull(textGo.GetComponent<RectTransform>());

            Text txt = textGo.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 19;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(0.12f, 0.16f, 0.28f, 1f);
            txt.text = caption;
            txt.raycastTarget = false;

            return btn;
        }

        private static Button CreatePillActionButton(
            string name,
            string caption,
            Color bgColor,
            Vector2 size,
            Transform parent,
            Font font,
            Sprite pillSprite,
            out Image backgroundImage,
            out Text labelText,
            int fontSize = 21)
        {
            GameObject btnGo = CreateUiObject(name, parent);
            btnGo.GetComponent<RectTransform>().sizeDelta = size;

            backgroundImage = btnGo.AddComponent<Image>();
            backgroundImage.sprite = pillSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = bgColor;

            Shadow shadow = btnGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.32f);
            shadow.effectDistance = new Vector2(0f, -4f);

            Button btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = backgroundImage;

            GameObject textGo = CreateUiObject("Text", btnGo.transform);
            StretchFull(textGo.GetComponent<RectTransform>());

            labelText = textGo.AddComponent<Text>();
            labelText.font = font;
            labelText.fontSize = fontSize;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;
            labelText.text = caption;
            labelText.raycastTarget = false;

            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            return btn;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font ResolveBuiltinUiFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("Arial", 22);
            }
            return font;
        }

        private static void EnsureGeneratedUiSprites(
            out Sprite silhouette,
            out Sprite badge,
            out Sprite roundedPill,
            out Sprite glowRing,
            out Sprite spotlightBeam,
            out Material particleMaterial,
            out ChristmasSprites christmas)
        {
            EnsureUnityFolderExists(GeneratedUiArtFolder);
            EnsureUnityFolderExists(ChristmasArtFolder);

            WriteTextureIfMissing(SilhouetteTexturePath, () => BuildSprunkiSilhouetteTexture(256, 440), Vector4.zero);
            WriteTextureIfMissing(IconCircleTexturePath, () => BuildRoundedRectTexture(192, 192, 38), Vector4.zero);
            WriteTextureIfMissing(RoundedPillTexturePath, () => BuildRoundedRectTexture(192, 96, 36), new Vector4(42f, 42f, 42f, 42f));
            WriteTextureIfMissing(GlowRingTexturePath, () => BuildGlowRingTexture(256, 128), Vector4.zero);
            WriteTextureIfMissing(SpotlightBeamTexturePath, () => BuildSpotlightBeamTexture(128, 256), Vector4.zero);
            WriteTextureIfMissing(SparkleStarTexturePath, () => BuildSparkleStarTexture(128, 128), Vector4.zero);

            WriteTextureIfMissing(SantaHatTexturePath, () => BuildSantaHatTexture(256, 192), Vector4.zero);
            WriteTextureIfMissing(ReindeerAntlersTexturePath, () => BuildReindeerAntlersTexture(256, 160), Vector4.zero);
            WriteTextureIfMissing(ElfHollyCapTexturePath, () => BuildElfHollyCapTexture(256, 192), Vector4.zero);
            WriteTextureIfMissing(FestiveScarfTexturePath, () => BuildFestiveScarfTexture(256, 128), Vector4.zero);
            WriteTextureIfMissing(GoldenStarTexturePath, () => BuildGoldenChristmasStarTexture(256, 256), Vector4.zero);
            WriteTextureIfMissing(SnowyPineTexturePath, () => BuildSnowyPineTreeTexture(256, 320), Vector4.zero);
            WriteTextureIfMissing(SnowHillTexturePath, () => BuildSnowHillTexture(512, 192), Vector4.zero);
            WriteTextureIfMissing(SnowflakeTexturePath, () => BuildSnowflakeTexture(128, 128), Vector4.zero);
            WriteTextureIfMissing(BulbGlowTexturePath, () => BuildBulbGlowTexture(96, 96), Vector4.zero);
            WriteTextureIfMissing(AuroraCurtainTexturePath, () => BuildAuroraCurtainTexture(512, 256), Vector4.zero);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            silhouette = AssetDatabase.LoadAssetAtPath<Sprite>(SilhouetteTexturePath);
            badge = AssetDatabase.LoadAssetAtPath<Sprite>(IconCircleTexturePath);
            roundedPill = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPillTexturePath);
            glowRing = AssetDatabase.LoadAssetAtPath<Sprite>(GlowRingTexturePath);
            spotlightBeam = AssetDatabase.LoadAssetAtPath<Sprite>(SpotlightBeamTexturePath);
            Texture2D sparkleTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SparkleStarTexturePath);
            Texture2D snowflakeTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowflakeTexturePath);

            particleMaterial = EnsureParticleMaterial(ParticleMaterialPath, sparkleTex);
            christmas = new ChristmasSprites
            {
                SantaHat = AssetDatabase.LoadAssetAtPath<Sprite>(SantaHatTexturePath),
                ReindeerAntlers = AssetDatabase.LoadAssetAtPath<Sprite>(ReindeerAntlersTexturePath),
                ElfHollyCap = AssetDatabase.LoadAssetAtPath<Sprite>(ElfHollyCapTexturePath),
                FestiveScarf = AssetDatabase.LoadAssetAtPath<Sprite>(FestiveScarfTexturePath),
                GoldenStarIcon = AssetDatabase.LoadAssetAtPath<Sprite>(GoldenStarTexturePath),
                SnowyPine = AssetDatabase.LoadAssetAtPath<Sprite>(SnowyPineTexturePath),
                SnowHill = AssetDatabase.LoadAssetAtPath<Sprite>(SnowHillTexturePath),
                BulbGlow = AssetDatabase.LoadAssetAtPath<Sprite>(BulbGlowTexturePath),
                AuroraCurtain = AssetDatabase.LoadAssetAtPath<Sprite>(AuroraCurtainTexturePath),
                SnowParticleMaterial = EnsureParticleMaterial(SnowParticleMaterialPath, snowflakeTex)
            };
        }

        private static Material EnsureParticleMaterial(string matPath, Texture2D mainTex)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                                ?? Shader.Find("Sprites/Default");
                mat = new Material(shader) { mainTexture = mainTex };
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            else if (mainTex != null && mat.mainTexture != mainTex)
            {
                mat.mainTexture = mainTex;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static void WriteTextureIfMissing(string unityPath, Func<Texture2D> factory, Vector4 spriteBorder)
        {
            string diskPath = UnityPathToDiskPath(unityPath);
            if (!File.Exists(diskPath))
            {
                Texture2D tex = factory();
                File.WriteAllBytes(diskPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(unityPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureAsUiSprite(unityPath, spriteBorder);
            }
            else if (AssetDatabase.LoadAssetAtPath<Sprite>(unityPath) == null)
            {
                ConfigureAsUiSprite(unityPath, spriteBorder);
            }
        }

        private static void ConfigureAsUiSprite(string assetPath, Vector4 spriteBorder)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = spriteBorder;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static Texture2D BuildSantaHatTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color velvetLight = new Color(0.95f, 0.14f, 0.20f, 1f);
            Color velvetDark = new Color(0.68f, 0.05f, 0.11f, 1f);
            Color whiteFur = new Color(0.98f, 0.99f, 1f, 1f);
            Color furShade = new Color(0.86f, 0.92f, 0.98f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    Color outColor = clear;

                    // Red velvet slanted cone (bottom y=0.22..0.90)
                    if (ny >= 0.20f && ny <= 0.90f)
                    {
                        float t = (ny - 0.20f) / 0.70f;
                        float center = Mathf.Lerp(0.46f, 0.72f, t * t * 0.85f);
                        float halfW = Mathf.Lerp(0.32f, 0.03f, t);
                        float dist = Mathf.Abs(nx - center);
                        if (dist <= halfW)
                        {
                            float shade = Mathf.Clamp01((nx - (center - halfW)) / Math.Max(0.01f, halfW * 2f));
                            outColor = Color.Lerp(velvetLight, velvetDark, shade * 0.65f);
                        }
                    }

                    // Pom-pom at dangling right-top tip
                    float pomDist = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.76f, 0.85f));
                    if (pomDist <= 0.11f)
                    {
                        float k = pomDist / 0.11f;
                        outColor = Color.Lerp(whiteFur, furShade, k * 0.55f);
                    }

                    // Fluffy scalloped white brim along bottom
                    if (ny >= 0.08f && ny <= 0.31f && nx >= 0.11f && nx <= 0.83f)
                    {
                        float puffX = Mathf.Repeat((nx - 0.11f) * 9.5f, 1f) - 0.5f;
                        float puffCurve = 0.028f * (1f - puffX * puffX * 2.5f);
                        float dy = Mathf.Abs(ny - 0.195f);
                        if (dy <= 0.085f + puffCurve)
                        {
                            float s = Mathf.Clamp01((0.25f - ny) / 0.18f);
                            outColor = Color.Lerp(whiteFur, furShade, s * 0.45f);
                        }
                    }

                    tex.SetPixel(x, y, outColor);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildReindeerAntlersTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color woodLight = new Color(0.64f, 0.37f, 0.18f, 1f);
            Color woodDark = new Color(0.42f, 0.22f, 0.10f, 1f);
            Color hollyGreen = new Color(0.14f, 0.64f, 0.28f, 1f);
            Color berryRed = new Color(0.95f, 0.16f, 0.20f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    float mx = Mathf.Abs(nx - 0.5f); // symmetric antlers
                    Color c = clear;

                    // Main stem sweeping out and up
                    float stemDist = DistanceToSegment(
                        new Vector2(mx, ny),
                        new Vector2(0.14f, 0.12f),
                        new Vector2(0.36f, 0.84f));
                    // Outer prong
                    float prong1 = DistanceToSegment(
                        new Vector2(mx, ny),
                        new Vector2(0.24f, 0.44f),
                        new Vector2(0.45f, 0.60f));
                    // Inner tine
                    float prong2 = DistanceToSegment(
                        new Vector2(mx, ny),
                        new Vector2(0.28f, 0.56f),
                        new Vector2(0.20f, 0.86f));

                    float minDist = Mathf.Min(stemDist, Mathf.Min(prong1, prong2));
                    if (minDist <= 0.042f)
                    {
                        c = Color.Lerp(woodLight, woodDark, Mathf.Clamp01(minDist / 0.042f));
                    }

                    // Center headband + holly berry accent
                    float berry = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.50f, 0.18f));
                    float leafL = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.43f, 0.17f));
                    float leafR = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.57f, 0.17f));
                    if (leafL <= 0.06f || leafR <= 0.06f) c = hollyGreen;
                    if (berry <= 0.055f) c = berryRed;

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildElfHollyCapTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color greenLight = new Color(0.16f, 0.72f, 0.34f, 1f);
            Color greenDark = new Color(0.08f, 0.46f, 0.20f, 1f);
            Color redTrim = new Color(0.92f, 0.16f, 0.22f, 1f);
            Color goldBell = new Color(1f, 0.85f, 0.22f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    Color outColor = clear;

                    if (ny >= 0.22f && ny <= 0.90f)
                    {
                        float t = (ny - 0.22f) / 0.68f;
                        float center = Mathf.Lerp(0.52f, 0.24f, t * t * 0.85f);
                        float halfW = Mathf.Lerp(0.30f, 0.025f, t);
                        if (Mathf.Abs(nx - center) <= halfW)
                        {
                            float shade = Mathf.Clamp01((nx - (center - halfW)) / Math.Max(0.01f, halfW * 2f));
                            outColor = Color.Lerp(greenLight, greenDark, shade * 0.65f);
                        }
                    }

                    if (Vector2.Distance(new Vector2(nx, ny), new Vector2(0.22f, 0.86f)) <= 0.085f)
                    {
                        outColor = goldBell;
                    }

                    // Festive zigzag / red brim
                    if (ny >= 0.08f && ny <= 0.28f && nx >= 0.14f && nx <= 0.86f)
                    {
                        float zig = 0.045f * Mathf.Abs(Mathf.Repeat((nx - 0.14f) * 8f, 1f) - 0.5f);
                        if (ny <= 0.23f + zig)
                        {
                            outColor = redTrim;
                        }
                    }

                    tex.SetPixel(x, y, outColor);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildFestiveScarfTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color red = new Color(0.90f, 0.15f, 0.22f, 1f);
            Color cream = new Color(0.98f, 0.98f, 1f, 1f);
            Color green = new Color(0.14f, 0.68f, 0.34f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    Color outColor = clear;

                    // Neck collar wrap across top
                    if (ny >= 0.50f && ny <= 0.90f && nx >= 0.12f && nx <= 0.88f)
                    {
                        float curveY = 0.70f - 0.07f * Mathf.Cos((nx - 0.5f) * Mathf.PI);
                        if (Mathf.Abs(ny - curveY) <= 0.16f)
                        {
                            int stripe = Mathf.FloorToInt((nx + ny * 0.4f) * 14f) % 3;
                            outColor = stripe == 0 ? red : (stripe == 1 ? green : cream);
                        }
                    }

                    // Hanging scarf tail on right side
                    if (ny >= 0.06f && ny <= 0.66f)
                    {
                        float tailCenter = Mathf.Lerp(0.76f, 0.68f, ny);
                        if (Mathf.Abs(nx - tailCenter) <= 0.105f)
                        {
                            int stripe = Mathf.FloorToInt((ny * 10f)) % 3;
                            outColor = stripe == 0 ? red : (stripe == 1 ? green : cream);
                        }
                    }

                    tex.SetPixel(x, y, outColor);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildGoldenChristmasStarTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color brightGold = new Color(1f, 0.96f, 0.62f, 1f);
            Color richGold = new Color(1f, 0.80f, 0.08f, 1f);
            Color deepGold = new Color(0.86f, 0.52f, 0.02f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float angle = Mathf.Atan2(ny, nx) - Mathf.PI * 0.5f;
                    float sector = Mathf.Abs(Mathf.Repeat((angle / (Mathf.PI * 2f)) * 5f + 0.5f, 1f) - 0.5f) * 2f;
                    float starRadius = Mathf.Lerp(0.84f, 0.36f, Mathf.Pow(sector, 0.72f));

                    if (r <= starRadius)
                    {
                        float centerGlow = Mathf.Clamp01(1f - (r / starRadius));
                        Color baseCol = Color.Lerp(richGold, brightGold, Mathf.Clamp01(centerGlow * 1.25f));
                        if (nx > 0.05f && ny < -0.05f)
                        {
                            baseCol = Color.Lerp(baseCol, deepGold, 0.32f);
                        }
                        tex.SetPixel(x, y, baseCol);
                    }
                    else
                    {
                        float halo = Mathf.Exp(-(r - starRadius) * (r - starRadius) * 28f) * 0.58f * Mathf.Clamp01(1f - r);
                        Color haloCol = brightGold;
                        haloCol.a = halo;
                        tex.SetPixel(x, y, halo > 0.01f ? haloCol : clear);
                    }
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSnowyPineTreeTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color trunkCol = new Color(0.38f, 0.22f, 0.12f, 1f);
            Color pineDark = new Color(0.06f, 0.34f, 0.20f, 1f);
            Color pineMid = new Color(0.11f, 0.48f, 0.28f, 1f);
            Color snowWhite = new Color(0.97f, 0.99f, 1f, 1f);

            // Tiers: (baseY, topY, halfWidth)
            var tiers = new[]
            {
                (0.16f, 0.54f, 0.45f),
                (0.40f, 0.76f, 0.35f),
                (0.62f, 0.96f, 0.24f)
            };

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    float dx = Mathf.Abs(nx - 0.5f);
                    Color outColor = clear;

                    if (ny >= 0.02f && ny <= 0.18f && dx <= 0.09f)
                    {
                        outColor = trunkCol;
                    }

                    for (int t = 0; t < tiers.Length; t++)
                    {
                        float bY = tiers[t].Item1;
                        float tY = tiers[t].Item2;
                        float halfW = tiers[t].Item3;
                        if (ny >= bY && ny <= tY)
                        {
                            float progress = (ny - bY) / (tY - bY);
                            float curHalfW = Mathf.Lerp(halfW, 0.01f, progress);
                            if (dx <= curHalfW)
                            {
                                bool isSnowCap = progress > 0.68f || (progress > 0.56f && dx < curHalfW * 0.72f);
                                outColor = isSnowCap
                                    ? snowWhite
                                    : Color.Lerp(pineDark, pineMid, 1f - (dx / Math.Max(0.01f, curHalfW)) * 0.5f);
                            }
                        }
                    }

                    tex.SetPixel(x, y, outColor);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSnowHillTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color topSnow = new Color(1f, 1f, 1f, 1f);
            Color lowerIce = new Color(0.84f, 0.93f, 1f, 1f);

            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float crestY = 0.88f * (1f - nx * nx * 0.82f);
                    float alpha = Mathf.Clamp01((crestY - ny) * h * 0.25f);
                    Color col = Color.Lerp(lowerIce, topSnow, Mathf.Clamp01(ny / Math.Max(0.05f, crestY)));
                    col.a = alpha;
                    tex.SetPixel(x, y, col);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSnowflakeTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);

            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 0.96f)
                    {
                        tex.SetPixel(x, y, clear);
                        continue;
                    }

                    float angle = Mathf.Atan2(ny, nx);
                    float fold = Mathf.Abs(Mathf.Sin(angle * 3f));
                    float arm = Mathf.Exp(-fold * 9.5f) * Mathf.Clamp01(1f - r / 0.88f);
                    float ringBranches = Mathf.Exp(-Mathf.Abs(r - 0.52f) * 14f) * Mathf.Clamp01(1f - fold * 1.4f);
                    float centerDot = Mathf.Exp(-r * r * 14f);
                    float a = Mathf.Clamp01(arm + ringBranches * 0.8f + centerDot * 0.9f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildBulbGlowTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Clamp01(1f - Mathf.InverseLerp(0.32f, 0.44f, r));
                    float halo = Mathf.Exp(-r * r * 5.2f) * 0.72f * Mathf.Clamp01(1f - r);
                    float a = Mathf.Clamp01(Mathf.Max(core, halo));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildAuroraCurtainTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)(h - 1);
                float vertMask = Mathf.Sin(ny * Mathf.PI);
                vertMask = Mathf.Pow(vertMask, 1.35f);
                for (int x = 0; x < w; x++)
                {
                    float nx = x / (float)(w - 1);
                    float horizFade = Mathf.Sin(nx * Mathf.PI);
                    float waves = 0.55f + 0.45f * Mathf.Sin(nx * Mathf.PI * 6f + ny * 3.5f);
                    float a = Mathf.Clamp01(vertMask * horizFade * waves);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom <= 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            Vector2 proj = a + t * ab;
            return Vector2.Distance(p, proj);
        }

        private static Texture2D BuildSprunkiSilhouetteTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color clear = new Color(1f, 1f, 1f, 0f);
            Color fill = Color.white;

            Vector2 headCenter = new Vector2(w * 0.5f, h * 0.69f);
            float headRadius = w * 0.34f;
            float bodyBottomY = h * 0.06f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inHead = Vector2.Distance(new Vector2(x, y), headCenter) <= headRadius;
                    bool inBody = false;

                    if (y >= bodyBottomY && y <= headCenter.y)
                    {
                        float t = (y - bodyBottomY) / (headCenter.y - bodyBottomY);
                        float halfWidth = Mathf.Lerp(w * 0.36f, w * 0.22f, t);
                        inBody = Mathf.Abs(x - w * 0.5f) <= halfWidth;
                    }

                    tex.SetPixel(x, y, (inHead || inBody) ? fill : clear);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildRoundedRectTexture(int w, int h, int cornerRadius)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Max(0, Mathf.Max(cornerRadius - x, x - (w - 1 - cornerRadius)));
                    int dy = Mathf.Max(0, Mathf.Max(cornerRadius - y, y - (h - 1 - cornerRadius)));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(cornerRadius + 0.5f - dist);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildGlowRingTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.68f) * 3.8f, 2f));
                    float core = Mathf.Clamp01(1f - r) * 0.38f;
                    float edgeFade = Mathf.Clamp01(1f - Mathf.InverseLerp(0.78f, 1.0f, r));
                    float a = Mathf.Clamp01((ring + core) * edgeFade);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSpotlightBeamTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float v = 1f - (y / (float)(h - 1));
                float halfWidth = Mathf.Lerp(0.08f, 0.48f, v);
                float verticalFade = Mathf.Pow(1f - v, 1.4f);

                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs((x / (float)(w - 1)) - 0.5f);
                    float edge = Mathf.Clamp01(1f - (nx / halfWidth));
                    float a = Mathf.Pow(edge, 1.6f) * verticalFade;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D BuildSparkleStarTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float halo = Mathf.Exp(-r * r * 6f);
                    float spikes = Mathf.Max(
                        Mathf.Exp(-Mathf.Abs(nx) * 7f) * Mathf.Exp(-Mathf.Abs(ny) * 2.2f),
                        Mathf.Exp(-Mathf.Abs(ny) * 7f) * Mathf.Exp(-Mathf.Abs(nx) * 2.2f));
                    float edgeFade = Mathf.Clamp01(1f - Mathf.InverseLerp(0.75f, 1f, r));
                    float a = Mathf.Clamp01((halo * 0.65f + spikes) * edgeFade);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static void EnsureUnityFolderExists(string unityFolderPath)
        {
            string normalized = unityFolderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized)) return;

            string[] parts = normalized.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static string UnityPathToDiskPath(string unityPath)
        {
            string relative = unityPath.Substring("Assets/".Length);
            return Path.Combine(Application.dataPath, relative);
        }

        private static Type FindTypeInLoadedAssemblies(string fullTypeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullTypeName, throwOnError: false);
                if (type != null) return type;
            }
            return null;
        }
    }
}