using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SprunkiKids.Data;

namespace SprunkiKids.Editor
{
    /// <summary>
    /// Editor migration tool that parses an extracted Scratch 3.0 (.sb3) project.json,
    /// extracts Stage backdrop (backdropcute), Polo base silhouette, all 20 original characters
    /// with their exact costume sequences, icon SVG colors, shared pop SFX, and Phase 1 loops,
    /// configures Audio/Sprite/SVG (TexturedSprite) importers, and generates 20 CharacterSoundData assets.
    /// </summary>
    public class ScratchAssetImporter : EditorWindow
    {
        private enum ImportMode
        {
            OneScriptableObjectPerTarget = 0,
            OneScriptableObjectPerSound = 1
        }

        private enum HorrorAssetHandling
        {
            SkipFromMigration = 0,
            MoveToExcludedSubfolder = 1,
            ImportNormally = 2
        }

#pragma warning disable CS0649
        [Serializable]
        private class ScratchProjectJson
        {
            public List<ScratchTargetJson> targets = new List<ScratchTargetJson>();
            public ScratchMetaJson meta;
        }

        [Serializable]
        private class ScratchMetaJson
        {
            public string semver;
            public string vm;
            public string agent;
        }

        [Serializable]
        private class ScratchTargetJson
        {
            public bool isStage;
            public string name;
            public int currentCostume;
            public List<ScratchCostumeJson> costumes = new List<ScratchCostumeJson>();
            public List<ScratchSoundJson> sounds = new List<ScratchSoundJson>();
            public float volume = 100f;
            public int layerOrder;
            public float tempo;
        }

        [Serializable]
        private class ScratchCostumeJson
        {
            public string name;
            public string assetId;
            public string md5ext;
            public string dataFormat;
            public int bitmapResolution = 1;
            public float rotationCenterX;
            public float rotationCenterY;
        }

        [Serializable]
        private class ScratchSoundJson
        {
            public string name;
            public string assetId;
            public string md5ext;
            public string dataFormat;
            public string format;
            public int rate;
            public int sampleCount;
        }

        [Serializable]
        private class SprunkiManifestJson
        {
            public float bpm = 100f;
            public List<ManifestFileJson> stageCostumes = new List<ManifestFileJson>();
            public string poloIdleFile;
            public string popSfxFile;
            public List<ManifestIconJson> icons = new List<ManifestIconJson>();
            public List<ManifestCharacterJson> characters = new List<ManifestCharacterJson>();
        }

        [Serializable]
        private class ManifestFileJson
        {
            public string name;
            public string file;
        }

        [Serializable]
        private class ManifestIconJson
        {
            public int index;
            public string name;
            public string file;
            public string themeHex;
        }

        [Serializable]
        private class ManifestCharacterJson
        {
            public int orderIndex;
            public string name;
            public bool isPartyTrigger;
            public List<ManifestFileJson> costumes = new List<ManifestFileJson>();
            public List<ManifestFileJson> sounds = new List<ManifestFileJson>();
        }
#pragma warning restore CS0649

        private sealed class TargetSelectionEntry
        {
            public ScratchTargetJson Target;
            public bool IncludeAsCharacter;
            public SoundCategory AssignedCategory;
            public int NonHorrorCostumeCount;
            public int NonHorrorSoundCount;
            public string PrimarySoundPreview;
        }

        public const string DefaultAudioRoot = "Assets/GameAssets/Audio";
        public const string DefaultSpritesRoot = "Assets/GameAssets/Sprites";
        public const string DefaultScriptableObjectsRoot = "Assets/GameAssets/ScriptableObjects/Characters";
        public const string CharactersPngRoot = "Assets/Art/CharactersPNG";

        public const string ExpectedStageDaytimePath = "Assets/Art/CharactersPNG/Stage/backdropcute.png";
        public const string ExpectedPoloIdlePath = "Assets/Art/CharactersPNG/Polo_Base/polo_idle.png";
        public const string ExpectedPopSfxPath = "Assets/GameAssets/Audio/UI/pop.wav";
        public const string ExpectedGoldenStarIconPath = "Assets/Art/CharactersPNG/Icons/20-a.png";
        public const string ExpectedChristmasStarIconPath = "Assets/Art/UI/Christmas/GoldenChristmasStar.png";

        private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly float[] KidFriendlyPentatonicSemitones =
        {
            0f, 2f, 4f, 7f, 9f, 12f, 4f, 7f, 9f, 12f,
            -5f, -3f, 0f, 2f, 4f, 7f, 9f, 12f, 7f, 12f
        };

        private static readonly Regex ExactIconCostumeRegex = new Regex(@"^\d{2}-a$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SvgHexFillRegex = new Regex(@"fill\s*=\s*[""'](#[0-9a-fA-F]{6})[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [SerializeField] private string sourceFolderPath = string.Empty;
        [SerializeField] private string audioOutputRoot = DefaultAudioRoot;
        [SerializeField] private string spritesOutputRoot = DefaultSpritesRoot;
        [SerializeField] private string soOutputRoot = DefaultScriptableObjectsRoot;

        [SerializeField] private ImportMode importMode = ImportMode.OneScriptableObjectPerTarget;
        [SerializeField] private int maxCharacterSoCount = 20;
        [SerializeField] private bool includeStageAssets = true;
        [SerializeField] private bool importSharedIconsTarget = true;
        [SerializeField] private bool prefixOrderIndexOnFiles = true;

        [SerializeField] private HorrorAssetHandling horrorHandling = HorrorAssetHandling.SkipFromMigration;
        [SerializeField] private bool sprunkiPhase1Only = true;
        [SerializeField] private string horrorKeywordsCsv = "horror,phase2,phase 2,phase_2,scary,creepy,gore,blood,dead,death,evil,nightmare,corrupt,cursed,???,idle2";

        [SerializeField] private AudioCompressionFormat audioCompression = AudioCompressionFormat.ADPCM;
        [SerializeField] private float audioQuality = 0.85f;
        [SerializeField] private bool decompressAudioOnLoad = true;
        [SerializeField] private bool forceAudioMono;
        [SerializeField] private float spritePixelsPerUnit = 100f;

        private ScratchProjectJson _parsedProject;
        private readonly List<TargetSelectionEntry> _targetEntries = new List<TargetSelectionEntry>();
        private readonly Dictionary<int, Color> _manifestIconColorsByNumber = new Dictionary<int, Color>();
        private readonly Dictionary<string, bool> _manifestPartyTriggerByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _manifestOrderByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Vector2 _scrollPosition;
        private string _statusMessage = "Select an extracted Scratch (.sb3) folder containing project.json, or extract a .sb3 archive directly.";
        private MessageType _statusType = MessageType.Info;
        private bool _svgPackageMissingWarning;
        private float _detectedStageTempo;

        [MenuItem("Tools/Sprunki Kids/Scratch (.sb3) Asset Importer", priority = 100)]
        public static void ShowWindow()
        {
            ScratchAssetImporter window = GetWindow<ScratchAssetImporter>("Scratch Asset Importer");
            window.minSize = new Vector2(640f, 600f);
            window.TryAutoDetectRawAssetsFolder();
            window.Show();
        }

        /// <summary>
        /// Runs the entire Scratch (.sb3) extraction, SVG/Audio importer configuration, Stage/Polo/Icon
        /// extraction, and 20 CharacterSoundData ScriptableObject generation without modal dialogs.
        /// Resilient against individual missing/malformed files so valid assets never fail to build.
        /// Used by the zero-touch automation pipeline.
        /// </summary>
        public static bool RunHeadlessImport(string rawAssetsFolder = null)
        {
            ScratchAssetImporter importer = CreateInstance<ScratchAssetImporter>();
            importer.hideFlags = HideFlags.DontSave;

            try
            {
                importer.audioOutputRoot = DefaultAudioRoot;
                importer.spritesOutputRoot = DefaultSpritesRoot;
                importer.soOutputRoot = DefaultScriptableObjectsRoot;
                importer.importMode = ImportMode.OneScriptableObjectPerTarget;
                importer.maxCharacterSoCount = 20;
                importer.includeStageAssets = true;
                importer.importSharedIconsTarget = true;
                importer.prefixOrderIndexOnFiles = true;
                importer.horrorHandling = HorrorAssetHandling.SkipFromMigration;
                importer.sprunkiPhase1Only = true;
                importer.decompressAudioOnLoad = true;
                importer.spritePixelsPerUnit = 100f;

                if (!string.IsNullOrWhiteSpace(rawAssetsFolder) && Directory.Exists(rawAssetsFolder))
                {
                    importer.sourceFolderPath = rawAssetsFolder;
                    importer.TryParseProjectJson();
                }

                if (importer._parsedProject == null || importer._targetEntries.Count == 0)
                {
                    importer.TryAutoDetectRawAssetsFolder();
                }

                if (importer._parsedProject == null || importer._targetEntries.Count == 0)
                {
                    Debug.LogWarning($"[ScratchAssetImporter] Source manifest/project parse incomplete ({importer._statusMessage}). Attempting fallback re-link of existing assets...");
                    int relinked = importer.RelinkExistingScriptableObjectsInternal(showDialogs: false);
                    return relinked > 0;
                }

                return importer.ExecuteMigrationInternal(showDialogs: false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScratchAssetImporter] Headless import hit unexpected exception ({ex.Message}). Attempting fallback recovery of existing assets...");
                try
                {
                    int relinked = importer.RelinkExistingScriptableObjectsInternal(showDialogs: false);
                    if (relinked > 0) return true;
                }
                catch
                {
                    // Fall through to return false only if zero recoverable assets exist.
                }

                Debug.LogException(ex);
                return false;
            }
            finally
            {
                DestroyImmediate(importer);
            }
        }

        private void OnEnable()
        {
            TryAutoDetectRawAssetsFolder();
        }

        private void TryAutoDetectRawAssetsFolder()
        {
            if (!string.IsNullOrWhiteSpace(sourceFolderPath) &&
                Directory.Exists(sourceFolderPath) &&
                (File.Exists(Path.Combine(sourceFolderPath, "sprunki_clean_manifest.json")) ||
                 File.Exists(Path.Combine(sourceFolderPath, "sprunki_manifest.json")) ||
                 File.Exists(Path.Combine(sourceFolderPath, "project.json"))))
            {
                if (_parsedProject == null) TryParseProjectJson();
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string parentRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;

            string[] candidates =
            {
                Path.Combine(Application.dataPath, "RawAssets"),
                Path.Combine(parentRoot, "Assets", "RawAssets"),
                Path.Combine(projectRoot, "RawAssets")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string dir = candidates[i];
                if (!Directory.Exists(dir)) continue;

                if (File.Exists(Path.Combine(dir, "sprunki_clean_manifest.json")) ||
                    File.Exists(Path.Combine(dir, "sprunki_manifest.json")) ||
                    File.Exists(Path.Combine(dir, "project.json")))
                {
                    sourceFolderPath = dir;
                    TryParseProjectJson();
                    return;
                }
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSourceSection();
            DrawOutputAndOptionsSection();
            DrawParsedTargetsPreview();
            DrawActionButtons();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Sprunki Kids — Scratch (.sb3) to Unity Importer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            if (_svgPackageMissingWarning)
            {
                EditorGUILayout.HelpBox(
                    "Vector (.svg) costumes were copied, but Unity's Vector Graphics package (com.unity.vectorgraphics) did not produce Sprite sub-assets yet.\n" +
                    "Verify 'com.unity.vectorgraphics' in Package Manager, then click 'Re-link Assets to Existing SOs'.",
                    MessageType.Warning);
            }
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("1. Source Scratch Package", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    sourceFolderPath = EditorGUILayout.TextField("Extracted .sb3 Folder", sourceFolderPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        TryParseProjectJson();
                    }

                    if (GUILayout.Button("Browse...", GUILayout.Width(85f)))
                    {
                        string initial = string.IsNullOrEmpty(sourceFolderPath) ? Application.dataPath : sourceFolderPath;
                        string chosen = EditorUtility.OpenFolderPanel("Select Extracted .sb3 Folder (containing project.json)", initial, string.Empty);
                        if (!string.IsNullOrEmpty(chosen))
                        {
                            sourceFolderPath = chosen;
                            TryParseProjectJson();
                        }
                    }

                    if (GUILayout.Button("Extract .sb3...", GUILayout.Width(100f)))
                    {
                        ExtractSb3ArchiveAndLoad();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Parse / Refresh project.json", GUILayout.Width(190f)))
                    {
                        TryParseProjectJson();
                    }
                }
            }
        }

        private void DrawOutputAndOptionsSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("2. Output Paths & Kid-Friendly Migration Options", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                audioOutputRoot = EditorGUILayout.TextField("Audio Output Root", audioOutputRoot);
                spritesOutputRoot = EditorGUILayout.TextField("Sprites Output Root", spritesOutputRoot);
                soOutputRoot = EditorGUILayout.TextField("ScriptableObjects Root", soOutputRoot);

                EditorGUILayout.Space(4f);
                importMode = (ImportMode)EditorGUILayout.EnumPopup("SO Generation Mode", importMode);
                maxCharacterSoCount = EditorGUILayout.IntField("Max Character SOs", Mathf.Max(1, maxCharacterSoCount));
                includeStageAssets = EditorGUILayout.Toggle("Import Stage Assets", includeStageAssets);
                importSharedIconsTarget = EditorGUILayout.Toggle("Import Shared 'Icons' Target", importSharedIconsTarget);
                prefixOrderIndexOnFiles = EditorGUILayout.Toggle("Prefix Index on Character Costumes", prefixOrderIndexOnFiles);

                EditorGUILayout.Space(4f);
                EditorGUI.BeginChangeCheck();
                horrorHandling = (HorrorAssetHandling)EditorGUILayout.EnumPopup("Horror Asset Filtering", horrorHandling);
                sprunkiPhase1Only = EditorGUILayout.Toggle("Phase 1 Loops Only (First 2 Sounds)", sprunkiPhase1Only);
                if (horrorHandling != HorrorAssetHandling.ImportNormally)
                {
                    horrorKeywordsCsv = EditorGUILayout.TextField("Horror Keywords (CSV)", horrorKeywordsCsv);
                }
                if (EditorGUI.EndChangeCheck() && !string.IsNullOrWhiteSpace(sourceFolderPath))
                {
                    TryParseProjectJson();
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Importer Settings (Mobile, WebGL & UI Image)", EditorStyles.miniBoldLabel);
                audioCompression = (AudioCompressionFormat)EditorGUILayout.EnumPopup("Audio Compression", audioCompression);
                if (audioCompression == AudioCompressionFormat.Vorbis)
                {
                    audioQuality = EditorGUILayout.Slider("Vorbis Quality", audioQuality, 0.1f, 1f);
                }
                decompressAudioOnLoad = EditorGUILayout.Toggle("Decompress Audio On Load", decompressAudioOnLoad);
                forceAudioMono = EditorGUILayout.Toggle("Force Audio Mono", forceAudioMono);
                spritePixelsPerUnit = EditorGUILayout.FloatField("Sprite Pixels Per Unit", Mathf.Max(1f, spritePixelsPerUnit));
            }
        }

        private void DrawParsedTargetsPreview()
        {
            EditorGUILayout.Space(4f);
            int selectedCount = _targetEntries.Count(e => e.IncludeAsCharacter);
            string tempoInfo = _detectedStageTempo > 0f ? $" | Stage Tempo: {_detectedStageTempo} BPM" : string.Empty;
            EditorGUILayout.LabelField(
                $"3. Parsed Targets ({_targetEntries.Count} total | Selected Characters: {selectedCount}/{maxCharacterSoCount}{tempoInfo})",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Auto-Select Top 20 Characters"))
                    {
                        AutoSelectRecommendedTargets();
                    }

                    if (GUILayout.Button("Select All Non-Stage"))
                    {
                        foreach (TargetSelectionEntry entry in _targetEntries)
                        {
                            entry.IncludeAsCharacter = !entry.Target.isStage;
                        }
                    }

                    if (GUILayout.Button("Select None"))
                    {
                        foreach (TargetSelectionEntry entry in _targetEntries)
                        {
                            entry.IncludeAsCharacter = false;
                        }
                    }
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MinHeight(150f), GUILayout.ExpandHeight(true));

                if (_targetEntries.Count == 0)
                {
                    EditorGUILayout.LabelField("No targets parsed yet.", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    for (int i = 0; i < _targetEntries.Count; i++)
                    {
                        TargetSelectionEntry entry = _targetEntries[i];
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(entry.Target.isStage))
                            {
                                entry.IncludeAsCharacter = EditorGUILayout.Toggle(entry.IncludeAsCharacter, GUILayout.Width(20f));
                            }

                            string tag = entry.Target.isStage ? "[Stage] " : string.Empty;
                            EditorGUILayout.LabelField($"{tag}{entry.Target.name}", GUILayout.MinWidth(150f));

                            using (new EditorGUI.DisabledScope(!entry.IncludeAsCharacter))
                            {
                                entry.AssignedCategory = (SoundCategory)EditorGUILayout.EnumPopup(entry.AssignedCategory, GUILayout.Width(90f));
                            }

                            EditorGUILayout.LabelField(
                                $"Sprites: {entry.NonHorrorCostumeCount} | Audio: {entry.NonHorrorSoundCount}",
                                EditorStyles.miniLabel,
                                GUILayout.Width(135f));

                            EditorGUILayout.LabelField(entry.PrimarySoundPreview, EditorStyles.miniLabel, GUILayout.MinWidth(120f));
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_parsedProject == null || _targetEntries.Count == 0))
                {
                    GUI.backgroundColor = new Color(0.45f, 0.88f, 0.55f);
                    if (GUILayout.Button("Import Assets & Generate CharacterSoundData", GUILayout.Height(36f)))
                    {
                        ExecuteMigrationInternal(showDialogs: true);
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (GUILayout.Button("Re-link Assets to Existing SOs", GUILayout.Height(36f), GUILayout.Width(220f)))
                {
                    RelinkExistingScriptableObjects();
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void ExtractSb3ArchiveAndLoad()
        {
            string sb3Path = EditorUtility.OpenFilePanel("Select Scratch .sb3 Package", string.Empty, "sb3,zip");
            if (string.IsNullOrEmpty(sb3Path) || !File.Exists(sb3Path))
            {
                return;
            }

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                string packageName = SanitizeName(Path.GetFileNameWithoutExtension(sb3Path));
                string extractRoot = Path.Combine(projectRoot, "RawScratchExtracted", packageName);

                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, true);
                }

                Directory.CreateDirectory(extractRoot);
                string fullDestRoot = Path.GetFullPath(extractRoot + Path.DirectorySeparatorChar);

                using (FileStream fs = File.OpenRead(sb3Path))
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    for (int i = 0; i < total; i++)
                    {
                        ZipArchiveEntry entry = archive.Entries[i];
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        string targetPath = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
                        if (!targetPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException($"Blocked unsafe zip entry path: {entry.FullName}");
                        }

                        string dir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                        entry.ExtractToFile(targetPath, overwrite: true);
                        if (i % 25 == 0)
                        {
                            EditorUtility.DisplayProgressBar("Extracting .sb3 Archive", entry.FullName, (float)i / Math.Max(1, total));
                        }
                    }
                }

                sourceFolderPath = extractRoot;
                TryParseProjectJson();
            }
            catch (Exception ex)
            {
                _statusMessage = $"Failed to extract .sb3 archive: {ex.Message}";
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void TryParseProjectJson()
        {
            _parsedProject = null;
            _targetEntries.Clear();
            _manifestIconColorsByNumber.Clear();
            _manifestPartyTriggerByName.Clear();
            _manifestOrderByName.Clear();
            _detectedStageTempo = 0f;

            if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
            {
                _statusMessage = "Source folder does not exist. Please select an extracted .sb3 folder.";
                _statusType = MessageType.Warning;
                return;
            }

            string parsedSourceLabel = "project.json";

            try
            {
                // 1. Prefer preprocessed clean Christmas manifest, then standard manifest (100% deterministic).
                string manifestPath = FindSourceJsonFile(sourceFolderPath, "sprunki_clean_manifest.json");
                string manifestFileLabel = "sprunki_clean_manifest.json";
                if (string.IsNullOrEmpty(manifestPath))
                {
                    manifestPath = FindSourceJsonFile(sourceFolderPath, "sprunki_manifest.json");
                    manifestFileLabel = "sprunki_manifest.json";
                }

                if (!string.IsNullOrEmpty(manifestPath) && TryLoadFromSprunkiManifest(manifestPath, out ScratchProjectJson manifestProject))
                {
                    _parsedProject = manifestProject;
                    parsedSourceLabel = manifestFileLabel;
                    string manifestDir = Path.GetDirectoryName(manifestPath);
                    if (!string.IsNullOrEmpty(manifestDir))
                    {
                        sourceFolderPath = manifestDir;
                    }
                }

                // 2. Fallback to Scratch project.json via JsonUtility, then flexible bracket-aware scanner.
                if (_parsedProject == null || _parsedProject.targets == null || _parsedProject.targets.Count == 0)
                {
                    string projectJsonPath = FindSourceJsonFile(sourceFolderPath, "project.json");
                    if (string.IsNullOrEmpty(projectJsonPath))
                    {
                        _statusMessage = $"Could not find sprunki_manifest.json or project.json inside: {sourceFolderPath}";
                        _statusType = MessageType.Error;
                        return;
                    }

                    string projectDir = Path.GetDirectoryName(projectJsonPath);
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        sourceFolderPath = projectDir;
                    }

                    string json = File.ReadAllText(projectJsonPath, Encoding.UTF8);

                    try
                    {
                        _parsedProject = JsonUtility.FromJson<ScratchProjectJson>(json);
                    }
                    catch (Exception jsonEx)
                    {
                        Debug.LogWarning($"[ScratchAssetImporter] JsonUtility failed on project.json ({jsonEx.Message}); switching to resilient scanner.");
                        _parsedProject = null;
                    }

                    if (_parsedProject == null || _parsedProject.targets == null || _parsedProject.targets.Count == 0)
                    {
                        _parsedProject = TryParseProjectJsonWithScanner(json);
                        parsedSourceLabel = "project.json (resilient scanner)";
                    }
                }

                if (_parsedProject == null || _parsedProject.targets == null || _parsedProject.targets.Count == 0)
                {
                    _statusMessage = "Parsed source JSON, but no valid targets could be extracted.";
                    _statusType = MessageType.Error;
                    return;
                }

                string[] horrorKeywords = ParseHorrorKeywords();

                foreach (ScratchTargetJson target in _parsedProject.targets)
                {
                    if (target == null) continue;

                    if (target.isStage && target.tempo > 0f)
                    {
                        _detectedStageTempo = target.tempo;
                    }

                    List<ScratchCostumeJson> costumes = target.costumes ?? new List<ScratchCostumeJson>();
                    List<ScratchSoundJson> sounds = target.sounds ?? new List<ScratchSoundJson>();

                    int nonHorrorCostumes = horrorHandling == HorrorAssetHandling.SkipFromMigration
                        ? costumes.Count(c => !IsExcludedCostumeForKids(c?.name, horrorKeywords))
                        : costumes.Count;

                    List<ScratchSoundJson> validSounds = new List<ScratchSoundJson>();
                    for (int sIdx = 0; sIdx < sounds.Count; sIdx++)
                    {
                        if (sprunkiPhase1Only && sIdx >= 2) break;
                        ScratchSoundJson s = sounds[sIdx];
                        if (s == null) continue;
                        if (horrorHandling == HorrorAssetHandling.SkipFromMigration && IsHorrorName(s.name, horrorKeywords))
                        {
                            continue;
                        }
                        validSounds.Add(s);
                    }

                    bool isManifestParty = _manifestPartyTriggerByName.TryGetValue(target.name ?? string.Empty, out bool mParty) && mParty;
                    string previewSound = validSounds.Count > 0
                        ? validSounds[0].name
                        : ((isManifestParty || LooksLikePartyTrigger(target.name)) ? "(Party Trigger)" : "(no audio)");

                    _targetEntries.Add(new TargetSelectionEntry
                    {
                        Target = target,
                        IncludeAsCharacter = false,
                        AssignedCategory = SoundCategory.Beat,
                        NonHorrorCostumeCount = nonHorrorCostumes,
                        NonHorrorSoundCount = validSounds.Count,
                        PrimarySoundPreview = previewSound
                    });
                }

                AutoSelectRecommendedTargets();

                int selected = _targetEntries.Count(e => e.IncludeAsCharacter);
                _statusMessage = $"Parsed {parsedSourceLabel}: {_targetEntries.Count} targets found, {selected} character targets auto-selected.";
                _statusType = MessageType.Info;
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error parsing source package JSON: {ex.Message}";
                _statusType = MessageType.Warning;
                Debug.LogWarning($"[ScratchAssetImporter] {_statusMessage}");
            }
        }

        private static string FindSourceJsonFile(string rootFolder, string fileName)
        {
            string direct = Path.Combine(rootFolder, fileName);
            if (File.Exists(direct)) return direct;

            string[] found = Directory.GetFiles(rootFolder, fileName, SearchOption.AllDirectories);
            return found.Length > 0 ? found[0] : null;
        }

        private bool TryLoadFromSprunkiManifest(string manifestPath, out ScratchProjectJson convertedProject)
        {
            convertedProject = null;
            try
            {
                string raw = File.ReadAllText(manifestPath, Encoding.UTF8);
                SprunkiManifestJson manifest = JsonUtility.FromJson<SprunkiManifestJson>(raw);
                if (manifest == null || manifest.characters == null || manifest.characters.Count == 0)
                {
                    return false;
                }

                var project = new ScratchProjectJson { targets = new List<ScratchTargetJson>() };

                // Stage backdrop target
                if (manifest.stageCostumes != null && manifest.stageCostumes.Count > 0)
                {
                    var stageTarget = new ScratchTargetJson
                    {
                        isStage = true,
                        name = "Stage",
                        tempo = manifest.bpm > 0f ? manifest.bpm : 100f
                    };

                    foreach (ManifestFileJson sc in manifest.stageCostumes)
                    {
                        if (sc == null || string.IsNullOrWhiteSpace(sc.file)) continue;
                        stageTarget.costumes.Add(new ScratchCostumeJson
                        {
                            name = string.IsNullOrWhiteSpace(sc.name) ? Path.GetFileNameWithoutExtension(sc.file) : sc.name,
                            md5ext = sc.file,
                            dataFormat = Path.GetExtension(sc.file).TrimStart('.')
                        });
                    }
                    project.targets.Add(stageTarget);
                }

                // Polo base silhouette target
                if (!string.IsNullOrWhiteSpace(manifest.poloIdleFile))
                {
                    var poloTarget = new ScratchTargetJson { isStage = false, name = "Polo 1" };
                    poloTarget.costumes.Add(new ScratchCostumeJson
                    {
                        name = "0",
                        md5ext = manifest.poloIdleFile,
                        dataFormat = Path.GetExtension(manifest.poloIdleFile).TrimStart('.')
                    });
                    project.targets.Add(poloTarget);
                }

                // Shared Icons target + pre-extracted themeHex table
                if (manifest.icons != null && manifest.icons.Count > 0)
                {
                    var iconsTarget = new ScratchTargetJson { isStage = false, name = "Icons" };
                    foreach (ManifestIconJson icon in manifest.icons)
                    {
                        if (icon == null || string.IsNullOrWhiteSpace(icon.file)) continue;
                        iconsTarget.costumes.Add(new ScratchCostumeJson
                        {
                            name = string.IsNullOrWhiteSpace(icon.name) ? $"{icon.index:D2}-a" : icon.name,
                            md5ext = icon.file,
                            dataFormat = Path.GetExtension(icon.file).TrimStart('.')
                        });

                        if (!string.IsNullOrWhiteSpace(icon.themeHex) &&
                            ColorUtility.TryParseHtmlString(icon.themeHex, out Color parsedHexColor))
                        {
                            _manifestIconColorsByNumber[icon.index] = parsedHexColor;
                        }
                    }
                    project.targets.Add(iconsTarget);
                }

                // Shared Pop UI SFX carrier target
                if (!string.IsNullOrWhiteSpace(manifest.popSfxFile))
                {
                    var popTarget = new ScratchTargetJson { isStage = false, name = "UI_Pop_Source" };
                    popTarget.sounds.Add(new ScratchSoundJson
                    {
                        name = "pop",
                        md5ext = manifest.popSfxFile,
                        dataFormat = Path.GetExtension(manifest.popSfxFile).TrimStart('.')
                    });
                    project.targets.Add(popTarget);
                }

                // Playable Sprunki characters in canonical order
                foreach (ManifestCharacterJson mc in manifest.characters.OrderBy(c => c.orderIndex))
                {
                    if (mc == null || string.IsNullOrWhiteSpace(mc.name)) continue;

                    var charTarget = new ScratchTargetJson
                    {
                        isStage = false,
                        name = mc.name
                    };

                    if (mc.costumes != null)
                    {
                        foreach (ManifestFileJson c in mc.costumes)
                        {
                            if (c == null || string.IsNullOrWhiteSpace(c.file)) continue;
                            charTarget.costumes.Add(new ScratchCostumeJson
                            {
                                name = c.name,
                                md5ext = c.file,
                                dataFormat = Path.GetExtension(c.file).TrimStart('.')
                            });
                        }
                    }

                    if (mc.sounds != null)
                    {
                        foreach (ManifestFileJson s in mc.sounds)
                        {
                            if (s == null || string.IsNullOrWhiteSpace(s.file)) continue;
                            charTarget.sounds.Add(new ScratchSoundJson
                            {
                                name = s.name,
                                md5ext = s.file,
                                dataFormat = Path.GetExtension(s.file).TrimStart('.')
                            });
                        }
                    }

                    _manifestPartyTriggerByName[mc.name] = mc.isPartyTrigger;
                    _manifestOrderByName[mc.name] = mc.orderIndex;
                    project.targets.Add(charTarget);
                }

                convertedProject = project;
                return project.targets.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScratchAssetImporter] Could not read sprunki_manifest.json cleanly ({ex.Message}). Falling back to project.json.");
                return false;
            }
        }

        private static ScratchProjectJson TryParseProjectJsonWithScanner(string json)
        {
            var project = new ScratchProjectJson { targets = new List<ScratchTargetJson>() };
            if (string.IsNullOrEmpty(json)) return project;

            int targetsKeyIdx = json.IndexOf("\"targets\"", StringComparison.OrdinalIgnoreCase);
            if (targetsKeyIdx < 0) return project;

            int arrayStart = json.IndexOf('[', targetsKeyIdx);
            if (arrayStart < 0 || !TryFindMatchingDelimiter(json, arrayStart, '[', ']', out int arrayEnd))
            {
                return project;
            }

            List<string> targetBlocks = SplitTopLevelJsonObjects(json, arrayStart + 1, arrayEnd - 1);
            foreach (string block in targetBlocks)
            {
                string name = ExtractJsonStringField(block, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var target = new ScratchTargetJson
                {
                    name = name,
                    isStage = ExtractJsonBoolField(block, "isStage"),
                    tempo = ExtractJsonFloatField(block, "tempo", 100f)
                };

                if (TryExtractNamedJsonArraySlice(block, "costumes", out int cStart, out int cEnd))
                {
                    foreach (string cObj in SplitTopLevelJsonObjects(block, cStart + 1, cEnd - 1))
                    {
                        string md5 = ExtractJsonStringField(cObj, "md5ext");
                        string assetId = ExtractJsonStringField(cObj, "assetId");
                        string fmt = ExtractJsonStringField(cObj, "dataFormat");
                        if (string.IsNullOrEmpty(md5) && string.IsNullOrEmpty(assetId)) continue;

                        target.costumes.Add(new ScratchCostumeJson
                        {
                            name = ExtractJsonStringField(cObj, "name"),
                            md5ext = md5,
                            assetId = assetId,
                            dataFormat = fmt
                        });
                    }
                }

                if (TryExtractNamedJsonArraySlice(block, "sounds", out int sStart, out int sEnd))
                {
                    foreach (string sObj in SplitTopLevelJsonObjects(block, sStart + 1, sEnd - 1))
                    {
                        string md5 = ExtractJsonStringField(sObj, "md5ext");
                        string assetId = ExtractJsonStringField(sObj, "assetId");
                        string fmt = ExtractJsonStringField(sObj, "dataFormat");
                        if (string.IsNullOrEmpty(md5) && string.IsNullOrEmpty(assetId)) continue;

                        target.sounds.Add(new ScratchSoundJson
                        {
                            name = ExtractJsonStringField(sObj, "name"),
                            md5ext = md5,
                            assetId = assetId,
                            dataFormat = fmt
                        });
                    }
                }

                project.targets.Add(target);
            }

            return project;
        }

        private static bool TryFindMatchingDelimiter(string text, int openIndex, char openChar, char closeChar, out int closeIndex)
        {
            closeIndex = -1;
            if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != openChar) return false;

            int depth = 0;
            bool inString = false;
            for (int i = openIndex; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedJsonQuote(text, i))
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;

                if (ch == openChar) depth++;
                else if (ch == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeIndex = i;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsEscapedJsonQuote(string text, int index)
        {
            int backslashes = 0;
            for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                backslashes++;
            }
            return (backslashes & 1) == 1;
        }

        private static List<string> SplitTopLevelJsonObjects(string text, int start, int end)
        {
            var objects = new List<string>();
            int objStart = -1;
            int depth = 0;
            bool inString = false;

            for (int i = Math.Max(0, start); i <= Math.Min(end, text.Length - 1); i++)
            {
                char ch = text[i];
                if (ch == '"' && !IsEscapedJsonQuote(text, i))
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;

                if (ch == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        objects.Add(text.Substring(objStart, i - objStart + 1));
                        objStart = -1;
                    }
                }
            }
            return objects;
        }

        private static bool TryExtractNamedJsonArraySlice(string objectText, string key, out int openBracketIdx, out int closeBracketIdx)
        {
            openBracketIdx = -1;
            closeBracketIdx = -1;
            Match m = Regex.Match(objectText, $"\"{Regex.Escape(key)}\"\\s*:\\s*\\[");
            if (!m.Success) return false;

            openBracketIdx = m.Index + m.Length - 1;
            return TryFindMatchingDelimiter(objectText, openBracketIdx, '[', ']', out closeBracketIdx);
        }

        private static string ExtractJsonStringField(string jsonSlice, string fieldName)
        {
            Match m = Regex.Match(jsonSlice, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : string.Empty;
        }

        private static bool ExtractJsonBoolField(string jsonSlice, string fieldName)
        {
            Match m = Regex.Match(jsonSlice, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static float ExtractJsonFloatField(string jsonSlice, string fieldName, float fallback)
        {
            Match m = Regex.Match(jsonSlice, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }
            return fallback;
        }

        private void AutoSelectRecommendedTargets()
        {
            int picked = 0;
            int cap = Mathf.Max(1, maxCharacterSoCount);

            foreach (TargetSelectionEntry entry in _targetEntries)
            {
                entry.IncludeAsCharacter = false;
                if (entry.Target.isStage || IsUtilityOrUiTarget(entry.Target.name))
                {
                    continue;
                }

                bool fromManifest = _manifestOrderByName.ContainsKey(entry.Target.name ?? string.Empty);
                bool hasAudio = entry.NonHorrorSoundCount > 0;
                bool isMystery20thPartyTrigger =
                    (_manifestPartyTriggerByName.TryGetValue(entry.Target.name ?? string.Empty, out bool mTrigger) && mTrigger) ||
                    (LooksLikePartyTrigger(entry.Target.name) && entry.NonHorrorCostumeCount > 0);

                if ((fromManifest || hasAudio || isMystery20thPartyTrigger) && picked < cap)
                {
                    entry.IncludeAsCharacter = true;
                    entry.AssignedCategory = GuessCategoryForOrder(picked, entry.Target.name, cap);
                    picked++;
                }
            }

            if (picked == 0)
            {
                foreach (TargetSelectionEntry entry in _targetEntries)
                {
                    if (!entry.Target.isStage && !IsUtilityOrUiTarget(entry.Target.name) && entry.NonHorrorSoundCount > 0 && picked < cap)
                    {
                        entry.IncludeAsCharacter = true;
                        entry.AssignedCategory = GuessCategoryForOrder(picked, entry.Target.name, cap);
                        picked++;
                    }
                }
            }
        }

        private static bool IsUtilityOrUiTarget(string targetName)
        {
            return ContainsAny(
                targetName,
                "preview", "bars", "button", "reset", "settings", "dark mode",
                "accent color", "polo ", "mute", "controller", "gallery",
                "icons", "assets", "dark", "ui_pop_source");
        }

        private bool ExecuteMigrationInternal(bool showDialogs)
        {
            if (_parsedProject == null || string.IsNullOrEmpty(sourceFolderPath)) return false;

            _svgPackageMissingWarning = false;
            string[] horrorKeywords = ParseHorrorKeywords();
            Dictionary<string, string> sourceFileIndex = BuildSourceFileIndex(sourceFolderPath);

            EnsureUnityFolderExists(audioOutputRoot);
            EnsureUnityFolderExists(spritesOutputRoot);
            EnsureUnityFolderExists(soOutputRoot);

            int copiedAudioCount = 0;
            int copiedSpriteCount = 0;
            int generatedSoCount = 0;
            int missingFileCount = 0;
            int svgTotalCount = 0;
            int svgWithSpriteCount = 0;

            var importedAudioPaths = new List<string>();
            var importedBitmapPaths = new List<string>();
            var importedSvgPaths = new List<string>();

            var targetAudioPathsMap = new Dictionary<TargetSelectionEntry, List<string>>();
            var targetSpritePathsMap = new Dictionary<TargetSelectionEntry, List<string>>();
            var sharedIconPathsByNumber = new Dictionary<int, string>();
            var sharedIconColorsByNumber = new Dictionary<int, Color>();
            foreach (KeyValuePair<int, Color> kv in _manifestIconColorsByNumber)
            {
                sharedIconColorsByNumber[kv.Key] = kv.Value;
            }
            string extractedPopSfxPath = null;

            try
            {
                List<TargetSelectionEntry> entriesToProcess = _targetEntries
                    .Where(e => e.IncludeAsCharacter ||
                                (e.Target.isStage && includeStageAssets) ||
                                (importSharedIconsTarget && e.Target.name.Equals("Icons", StringComparison.OrdinalIgnoreCase)) ||
                                e.Target.name.Equals("Polo 1", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Extract shared UI pop SFX from Reset button / Preview / Mute Buttons if present.
                extractedPopSfxPath = TryExtractSharedPopSfx(sourceFileIndex, importedAudioPaths, ref copiedAudioCount);

                for (int i = 0; i < entriesToProcess.Count; i++)
                {
                    TargetSelectionEntry entry = entriesToProcess[i];
                    ScratchTargetJson target = entry.Target;
                    bool isSharedIcons = target.name.Equals("Icons", StringComparison.OrdinalIgnoreCase);
                    bool isPoloBase = target.name.Equals("Polo 1", StringComparison.OrdinalIgnoreCase);
                    bool isStage = target.isStage;

                    string safeTargetFolder = isPoloBase ? "Polo_Base" : SanitizeName(target.name);

                    if (showDialogs)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Migrating Scratch Assets",
                            $"Copying files for '{target.name}'...",
                            (float)i / Math.Max(1, entriesToProcess.Count));
                    }

                    string targetAudioFolder = CombineUnityPath(audioOutputRoot, safeTargetFolder);
                    string targetSpriteFolder = CombineUnityPath(spritesOutputRoot, safeTargetFolder);
                    EnsureUnityFolderExists(targetSpriteFolder);

                    var usedAudioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var usedSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var spritePaths = new List<string>();
                    var audioPaths = new List<string>();

                    List<ScratchCostumeJson> costumes = target.costumes ?? new List<ScratchCostumeJson>();
                    for (int cIdx = 0; cIdx < costumes.Count; cIdx++)
                    {
                        ScratchCostumeJson costume = costumes[cIdx];
                        if (costume == null) continue;

                        if (isPoloBase && cIdx > 0)
                        {
                            // We only need Polo's base neutral empty-slot costume ('0').
                            break;
                        }

                        if (isSharedIcons && horrorHandling == HorrorAssetHandling.SkipFromMigration)
                        {
                            // Strictly keep "01-a" .. "20-a" (excluding "fake20", "-b", "-c", "hitbox").
                            if (!ExactIconCostumeRegex.IsMatch(costume.name ?? string.Empty))
                            {
                                continue;
                            }
                        }

                        if (isStage && horrorHandling == HorrorAssetHandling.SkipFromMigration)
                        {
                            if (ContainsAny(costume.name, "evil", "scary", "dark", "horror"))
                            {
                                continue;
                            }
                        }

                        bool isHorror = IsExcludedCostumeForKids(costume.name, horrorKeywords);
                        if (isHorror && horrorHandling == HorrorAssetHandling.SkipFromMigration)
                        {
                            continue;
                        }

                        string md5FileName = ResolveMd5FileName(costume.md5ext, costume.assetId, costume.dataFormat);
                        if (!TryResolveSourceFile(sourceFileIndex, md5FileName, out string srcPath))
                        {
                            missingFileCount++;
                            Debug.LogWarning($"[ScratchAssetImporter] Missing costume file '{md5FileName}' for target '{target.name}'.");
                            continue;
                        }

                        try
                        {
                            string ext = Path.GetExtension(md5FileName);
                            if (string.IsNullOrEmpty(ext))
                            {
                                ext = Path.GetExtension(srcPath);
                            }
                            if (string.IsNullOrEmpty(ext))
                            {
                                ext = string.IsNullOrEmpty(costume.dataFormat) ? ".svg" : "." + costume.dataFormat.TrimStart('.');
                            }

                            string destFolder = (isHorror && horrorHandling == HorrorAssetHandling.MoveToExcludedSubfolder)
                                ? CombineUnityPath(targetSpriteFolder, "_FilteredHorror")
                                : targetSpriteFolder;
                            EnsureUnityFolderExists(destFolder);

                            string cleanBase;
                            if (isPoloBase)
                            {
                                cleanBase = "polo_idle";
                            }
                            else if (isSharedIcons || isStage)
                            {
                                cleanBase = SanitizeName(string.IsNullOrWhiteSpace(costume.name) ? $"costume_{cIdx:D2}" : costume.name);
                            }
                            else
                            {
                                cleanBase = BuildCleanAssetBaseName(costume.name, cIdx, "costume");
                            }

                            string uniqueName = GetUniqueFileName(cleanBase, ext, usedSpriteNames);
                            string unityDestPath = CombineUnityPath(destFolder, uniqueName);
                            string diskDestPath = UnityPathToDiskPath(unityDestPath);

                            File.Copy(srcPath, diskDestPath, overwrite: true);
                            copiedSpriteCount++;

                            if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                            {
                                importedSvgPaths.Add(unityDestPath);
                            }
                            else
                            {
                                importedBitmapPaths.Add(unityDestPath);
                            }

                            if (!isHorror || horrorHandling == HorrorAssetHandling.ImportNormally)
                            {
                                spritePaths.Add(unityDestPath);
                            }

                            if (isSharedIcons && TryParseLeadingNumber(costume.name, out int iconNumber))
                            {
                                if (!sharedIconPathsByNumber.ContainsKey(iconNumber))
                                {
                                    sharedIconPathsByNumber[iconNumber] = unityDestPath;
                                }
                                if (!sharedIconColorsByNumber.ContainsKey(iconNumber) &&
                                    ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) &&
                                    TryExtractPrimaryColorFromSvg(diskDestPath, out Color svgColor))
                                {
                                    sharedIconColorsByNumber[iconNumber] = svgColor;
                                }
                            }
                        }
                        catch (Exception copyEx)
                        {
                            missingFileCount++;
                            Debug.LogWarning($"[ScratchAssetImporter] Skipped costume '{costume.name}' for '{target.name}': {copyEx.Message}");
                        }
                    }

                    if (!isSharedIcons && !isPoloBase && !isStage)
                    {
                        EnsureUnityFolderExists(targetAudioFolder);
                        List<ScratchSoundJson> sounds = target.sounds ?? new List<ScratchSoundJson>();
                        for (int sIdx = 0; sIdx < sounds.Count; sIdx++)
                        {
                            if (sprunkiPhase1Only && entry.IncludeAsCharacter && sIdx >= 2)
                            {
                                break;
                            }

                            ScratchSoundJson sound = sounds[sIdx];
                            if (sound == null) continue;

                            bool isHorror = IsHorrorName(sound.name, horrorKeywords);
                            if (isHorror && horrorHandling == HorrorAssetHandling.SkipFromMigration)
                            {
                                continue;
                            }

                            string md5FileName = ResolveMd5FileName(sound.md5ext, sound.assetId, sound.dataFormat);
                            if (!TryResolveSourceFile(sourceFileIndex, md5FileName, out string srcPath))
                            {
                                missingFileCount++;
                                Debug.LogWarning($"[ScratchAssetImporter] Missing sound file '{md5FileName}' for target '{target.name}'.");
                                continue;
                            }

                            try
                            {
                                string ext = Path.GetExtension(md5FileName);
                                if (string.IsNullOrEmpty(ext))
                                {
                                    ext = Path.GetExtension(srcPath);
                                }
                                if (string.IsNullOrEmpty(ext))
                                {
                                    ext = string.IsNullOrEmpty(sound.dataFormat) ? ".wav" : "." + sound.dataFormat.TrimStart('.');
                                }

                                string destFolder = (isHorror && horrorHandling == HorrorAssetHandling.MoveToExcludedSubfolder)
                                    ? CombineUnityPath(targetAudioFolder, "_FilteredHorror")
                                    : targetAudioFolder;
                                EnsureUnityFolderExists(destFolder);

                                string cleanBase = BuildCleanAssetBaseName(sound.name, sIdx, "sound");
                                string uniqueName = GetUniqueFileName(cleanBase, ext, usedAudioNames);
                                string unityDestPath = CombineUnityPath(destFolder, uniqueName);
                                string diskDestPath = UnityPathToDiskPath(unityDestPath);

                                File.Copy(srcPath, diskDestPath, overwrite: true);
                                copiedAudioCount++;
                                importedAudioPaths.Add(unityDestPath);

                                if (!isHorror || horrorHandling == HorrorAssetHandling.ImportNormally)
                                {
                                    audioPaths.Add(unityDestPath);
                                }
                            }
                            catch (Exception audioEx)
                            {
                                missingFileCount++;
                                Debug.LogWarning($"[ScratchAssetImporter] Skipped sound '{sound.name}' for '{target.name}': {audioEx.Message}");
                            }
                        }
                    }

                    targetSpritePathsMap[entry] = spritePaths;
                    targetAudioPathsMap[entry] = audioPaths;
                }

                if (showDialogs)
                {
                    EditorUtility.DisplayProgressBar("Migrating Scratch Assets", "Importing assets into Unity...", 0.72f);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureCharactersPngTextures(spritePixelsPerUnit);

                // Ensure any pre-existing SVGs in Sprites root are also configured cleanly as TexturedSprite.
                var allSvgSet = new HashSet<string>(importedSvgPaths, StringComparer.OrdinalIgnoreCase);
                if (AssetDatabase.IsValidFolder(spritesOutputRoot))
                {
                    foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { spritesOutputRoot }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(path) && path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            allSvgSet.Add(path);
                        }
                    }
                }
                List<string> svgPathsToConfigure = allSvgSet.ToList();

                ConfigureImportedAssets(importedAudioPaths, importedBitmapPaths, svgPathsToConfigure);

                svgTotalCount = importedSvgPaths.Count;
                foreach (string svgPath in importedSvgPaths)
                {
                    if (LoadSpriteAtPath(svgPath) != null)
                    {
                        svgWithSpriteCount++;
                    }
                }

                bool hasCharactersPngFallback = AssetDatabase.IsValidFolder(CombineUnityPath(CharactersPngRoot, "Icons"));
                _svgPackageMissingWarning = !hasCharactersPngFallback && svgTotalCount > 0 && svgWithSpriteCount == 0;

                if (showDialogs)
                {
                    EditorUtility.DisplayProgressBar("Migrating Scratch Assets", "Generating CharacterSoundData ScriptableObjects...", 0.90f);
                }

                AudioClip sharedPopClip = !string.IsNullOrEmpty(extractedPopSfxPath)
                    ? AssetDatabase.LoadAssetAtPath<AudioClip>(extractedPopSfxPath)
                    : AssetDatabase.LoadAssetAtPath<AudioClip>(ExpectedPopSfxPath);

                generatedSoCount = GenerateScriptableObjects(
                    entriesToProcess,
                    targetAudioPathsMap,
                    targetSpritePathsMap,
                    sharedIconPathsByNumber,
                    sharedIconColorsByNumber,
                    sharedPopClip);

                if (generatedSoCount == 0)
                {
                    Debug.LogWarning("[ScratchAssetImporter] No new SOs generated from primary pass; trying re-link of any existing ScriptableObjects...");
                    generatedSoCount = RelinkExistingScriptableObjectsInternal(showDialogs: false);
                }

                AssetDatabase.SaveAssets();
                if (AssetDatabase.IsValidFolder("Assets/GameAssets/Sprites/Black"))
                {
                    AssetDatabase.DeleteAsset("Assets/GameAssets/Sprites/Black");
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                _statusMessage =
                    $"Migration complete! Copied {copiedAudioCount} audio clips, {copiedSpriteCount} sprites, " +
                    $"and generated {generatedSoCount} CharacterSoundData assets." +
                    (missingFileCount > 0 ? $" ({missingFileCount} referenced files skipped.)" : string.Empty);
                _statusType = missingFileCount > 0 || _svgPackageMissingWarning ? MessageType.Warning : MessageType.Info;

                Debug.Log($"[ScratchAssetImporter] {_statusMessage} SVG Sprites resolved: {svgWithSpriteCount}/{svgTotalCount}.");

                if (showDialogs)
                {
                    EditorUtility.DisplayDialog(
                        "Scratch Asset Importer",
                        $"Migration Complete!\n\n" +
                        $"• Audio clips migrated: {copiedAudioCount}\n" +
                        $"• Sprites migrated: {copiedSpriteCount} (SVG TexturedSprites: {svgWithSpriteCount}/{svgTotalCount})\n" +
                        $"• CharacterSoundData SOs generated: {generatedSoCount}\n" +
                        (missingFileCount > 0 ? $"• Skipped/Missing source files: {missingFileCount} (check Console)\n" : string.Empty) +
                        "\nNext Step: Run 'Tools > Sprunki > Auto Setup Entire Scene' to populate IconTray & 7 Character Slots automatically!",
                        "OK");
                }

                return generatedSoCount > 0;
            }
            catch (Exception ex)
            {
                _statusMessage = $"Migration warning: {ex.Message}";
                _statusType = MessageType.Warning;
                Debug.LogWarning($"[ScratchAssetImporter] Migration encountered exception: {ex.Message}\n{ex.StackTrace}");
                return generatedSoCount > 0;
            }
            finally
            {
                if (showDialogs)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        private string TryExtractSharedPopSfx(
            Dictionary<string, string> sourceFileIndex,
            List<string> importedAudioPaths,
            ref int copiedAudioCount)
        {
            if (_parsedProject == null || _parsedProject.targets == null) return null;

            foreach (ScratchTargetJson target in _parsedProject.targets)
            {
                if (target == null || target.sounds == null) continue;
                foreach (ScratchSoundJson s in target.sounds)
                {
                    if (s == null) continue;
                    if (string.Equals(s.name, "pop", StringComparison.OrdinalIgnoreCase))
                    {
                        string md5File = ResolveMd5FileName(s.md5ext, s.assetId, s.dataFormat);
                        if (TryResolveSourceFile(sourceFileIndex, md5File, out string srcPath))
                        {
                            try
                            {
                                string uiAudioFolder = CombineUnityPath(audioOutputRoot, "UI");
                                EnsureUnityFolderExists(uiAudioFolder);
                                string destUnity = ExpectedPopSfxPath;
                                string destDisk = UnityPathToDiskPath(destUnity);
                                File.Copy(srcPath, destDisk, overwrite: true);
                                copiedAudioCount++;
                                importedAudioPaths.Add(destUnity);
                                return destUnity;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[ScratchAssetImporter] Could not copy shared pop SFX: {ex.Message}");
                                return null;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private int GenerateScriptableObjects(
            List<TargetSelectionEntry> entriesToProcess,
            Dictionary<TargetSelectionEntry, List<string>> targetAudioPathsMap,
            Dictionary<TargetSelectionEntry, List<string>> targetSpritePathsMap,
            Dictionary<int, string> sharedIconPathsByNumber,
            Dictionary<int, Color> sharedIconColorsByNumber,
            AudioClip sharedPopClip)
        {
            List<TargetSelectionEntry> characterEntries = entriesToProcess
                .Where(e => e.IncludeAsCharacter &&
                            !e.Target.isStage &&
                            !e.Target.name.Equals("Icons", StringComparison.OrdinalIgnoreCase) &&
                            !e.Target.name.Equals("Polo 1", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int createdOrUpdated = 0;

            if (importMode == ImportMode.OneScriptableObjectPerTarget)
            {
                int count = Mathf.Min(characterEntries.Count, Mathf.Max(1, maxCharacterSoCount));
                var generatedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < count; i++)
                {
                    TargetSelectionEntry entry = characterEntries[i];
                    try
                    {
                        string safeName = SanitizeName(entry.Target.name);
                        List<AudioClip> clips = LoadClips(targetAudioPathsMap[entry]);

                        int numberKey = i + 1;
                        bool isPartyTrigger =
                            (_manifestPartyTriggerByName.TryGetValue(entry.Target.name, out bool mParty) && mParty) ||
                            i == count - 1 ||
                            LooksLikePartyTrigger(entry.Target.name);

                        // Prefer rasterized crisp 512x512 RGBA PNG sprites over Unity SVGImporter!
                        string pngFolder = CombineUnityPath(CharactersPngRoot, safeName);
                        if (!AssetDatabase.IsValidFolder(pngFolder) && isPartyTrigger)
                        {
                            pngFolder = CombineUnityPath(CharactersPngRoot, "Golden_Christmas_Star");
                        }
                        List<Sprite> sprites = FindSpritesInUnityFolder(pngFolder, out _, out _);
                        if (sprites.Count == 0)
                        {
                            sprites = LoadSprites(targetSpritePathsMap[entry]);
                        }

                        string pngIconPath = CombineUnityPath(CharactersPngRoot, $"Icons/{numberKey:D2}-a.png");
                        Sprite sharedIcon = LoadSpriteAtPath(pngIconPath);
                        if (sharedIcon == null && isPartyTrigger)
                        {
                            sharedIcon = LoadSpriteAtPath(ExpectedGoldenStarIconPath)
                                         ?? LoadSpriteAtPath(ExpectedChristmasStarIconPath);
                        }
                        if (sharedIcon == null && sharedIconPathsByNumber.TryGetValue(numberKey, out string iconPath))
                        {
                            sharedIcon = LoadSpriteAtPath(iconPath);
                        }

                        Color themeColor;
                        if (isPartyTrigger)
                        {
                            themeColor = new Color(1f, 0.843f, 0f, 1f); // Golden Christmas Star (#FFD700)
                        }
                        else if (sharedIconColorsByNumber.TryGetValue(numberKey, out Color parsedColor))
                        {
                            themeColor = parsedColor;
                        }
                        else
                        {
                            themeColor = GetKidFriendlyPaletteColor(i, Mathf.Max(count, 20));
                        }

                        string assetPath = CombineUnityPath(soOutputRoot, $"{i + 1:D2}_{safeName}.asset");
                        generatedPaths.Add(assetPath);

                        CreateOrUpdateCharacterSO(
                            assetPath: assetPath,
                            id: safeName.ToLowerInvariant(),
                            displayName: FormatKidFriendlyDisplayName(entry.Target.name),
                            category: entry.AssignedCategory,
                            orderIndex: i,
                            isPartyTrigger: isPartyTrigger,
                            color: themeColor,
                            semitone: KidFriendlyPentatonicSemitones[i % KidFriendlyPentatonicSemitones.Length],
                            scratchTargetName: entry.Target.name,
                            subfolder: safeName,
                            clips: clips,
                            sprites: sprites,
                            sharedIconOverride: sharedIcon,
                            tapSfx: sharedPopClip);

                        createdOrUpdated++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ScratchAssetImporter] Skipped CharacterSoundData creation for '{entry.Target.name}': {ex.Message}");
                    }
                }

                // Remove stale renamed SO assets (e.g., 20_Black.asset after upgrade to Golden Christmas Star)
                if (generatedPaths.Count == count && AssetDatabase.IsValidFolder(soOutputRoot))
                {
                    foreach (string existingGuid in AssetDatabase.FindAssets("t:CharacterSoundData", new[] { soOutputRoot }))
                    {
                        string existingPath = AssetDatabase.GUIDToAssetPath(existingGuid);
                        if (!string.IsNullOrEmpty(existingPath) && !generatedPaths.Contains(existingPath))
                        {
                            AssetDatabase.DeleteAsset(existingPath);
                        }
                    }
                }
            }
            else
            {
                int order = 0;
                int cap = Mathf.Max(1, maxCharacterSoCount);

                foreach (TargetSelectionEntry entry in characterEntries)
                {
                    if (order >= cap) break;

                    List<AudioClip> clips = LoadClips(targetAudioPathsMap[entry]);
                    List<Sprite> sprites = LoadSprites(targetSpritePathsMap[entry]);
                    string safeTarget = SanitizeName(entry.Target.name);

                    for (int c = 0; c < clips.Count && order < cap; c++)
                    {
                        try
                        {
                            AudioClip clip = clips[c];
                            string clipDisplay = clip != null ? clip.name : $"{entry.Target.name}_{order + 1}";
                            string safeId = SanitizeName(clipDisplay).ToLowerInvariant();
                            string assetPath = CombineUnityPath(soOutputRoot, $"{order + 1:D2}_{SanitizeName(clipDisplay)}.asset");

                            Sprite matchedSprite = c < sprites.Count ? sprites[c] : (sprites.Count > 0 ? sprites[0] : null);
                            var singleSpriteList = matchedSprite != null ? new List<Sprite> { matchedSprite } : new List<Sprite>();

                            CreateOrUpdateCharacterSO(
                                assetPath: assetPath,
                                id: safeId,
                                displayName: clipDisplay,
                                category: GuessCategoryForOrder(order, clipDisplay, cap),
                                orderIndex: order,
                                isPartyTrigger: order == cap - 1 || LooksLikePartyTrigger(clipDisplay),
                                color: GetKidFriendlyPaletteColor(order, cap),
                                semitone: KidFriendlyPentatonicSemitones[order % KidFriendlyPentatonicSemitones.Length],
                                scratchTargetName: entry.Target.name,
                                subfolder: safeTarget,
                                clips: new List<AudioClip> { clip },
                                sprites: singleSpriteList,
                                sharedIconOverride: null,
                                tapSfx: sharedPopClip);

                            order++;
                            createdOrUpdated++;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ScratchAssetImporter] Skipped clip SO creation for '{entry.Target.name}': {ex.Message}");
                        }
                    }
                }
            }

            return createdOrUpdated;
        }

        private void CreateOrUpdateCharacterSO(
            string assetPath,
            string id,
            string displayName,
            SoundCategory category,
            int orderIndex,
            bool isPartyTrigger,
            Color color,
            float semitone,
            string scratchTargetName,
            string subfolder,
            List<AudioClip> clips,
            List<Sprite> sprites,
            Sprite sharedIconOverride,
            AudioClip tapSfx)
        {
            CharacterSoundData asset = AssetDatabase.LoadAssetAtPath<CharacterSoundData>(assetPath);
            bool isNew = asset == null;

            if (isNew)
            {
                asset = CreateInstance<CharacterSoundData>();
            }

            SelectBestClipsAndSprites(
                clips,
                sprites,
                sharedIconOverride,
                out AudioClip mainLoop,
                out AudioClip secondaryLoop,
                out AudioClip partyLoop,
                out Sprite iconSprite,
                out Sprite idleSprite,
                out List<Sprite> singingFrames,
                out Sprite partySprite);

            asset.ConfigureFromScratchImport(
                id: id,
                label: displayName,
                category: category,
                orderIndex: orderIndex,
                partyTrigger: isPartyTrigger,
                color: color,
                semitoneOffset: semitone,
                scratchTargetName: scratchTargetName,
                assetSubfolder: subfolder,
                primaryLoop: mainLoop,
                partyLoop: partyLoop,
                extraClips: clips,
                icon: iconSprite,
                idle: idleSprite,
                singSprites: singingFrames,
                partySprite: partySprite,
                allCostumes: sprites,
                secondaryLoop: secondaryLoop,
                tapSfx: tapSfx,
                frameRate: 10f);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private void RelinkExistingScriptableObjects()
        {
            RelinkExistingScriptableObjectsInternal(showDialogs: true);
        }

        private int RelinkExistingScriptableObjectsInternal(bool showDialogs)
        {
            if (!AssetDatabase.IsValidFolder(soOutputRoot))
            {
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog("Re-link Assets", $"ScriptableObjects folder not found:\n{soOutputRoot}", "OK");
                }
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:CharacterSoundData", new[] { soOutputRoot });
            if (guids.Length == 0)
            {
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog("Re-link Assets", $"No CharacterSoundData assets found in:\n{soOutputRoot}", "OK");
                }
                return 0;
            }

            int updated = 0;
            int resolvedSvgSprites = 0;
            int totalSvgFound = 0;

            try
            {
                ConfigureCharactersPngTextures(spritePixelsPerUnit);

                var svgToConfigure = new List<string>();
                string[] allSpriteGuids = AssetDatabase.IsValidFolder(spritesOutputRoot)
                    ? AssetDatabase.FindAssets(string.Empty, new[] { spritesOutputRoot })
                    : Array.Empty<string>();

                foreach (string guid in allSpriteGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        svgToConfigure.Add(path);
                    }
                }

                if (svgToConfigure.Count > 0)
                {
                    ConfigureImportedAssets(Array.Empty<string>(), Array.Empty<string>(), svgToConfigure);
                }

                string pngIconsFolder = CombineUnityPath(CharactersPngRoot, "Icons");
                List<Sprite> sharedIcons = FindSpritesInUnityFolder(pngIconsFolder, out _, out _);
                if (sharedIcons.Count == 0)
                {
                    string sharedIconsFolder = CombineUnityPath(spritesOutputRoot, "Icons");
                    sharedIcons = FindSpritesInUnityFolder(sharedIconsFolder, out int iconSvgCount, out int iconSvgOk);
                    totalSvgFound += iconSvgCount;
                    resolvedSvgSprites += iconSvgOk;
                }

                AudioClip popClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ExpectedPopSfxPath);

                for (int i = 0; i < guids.Length; i++)
                {
                    try
                    {
                        string soPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                        CharacterSoundData so = AssetDatabase.LoadAssetAtPath<CharacterSoundData>(soPath);
                        if (so == null) continue;

                        string subfolder = !string.IsNullOrEmpty(so.ImportedAssetSubfolder)
                            ? so.ImportedAssetSubfolder
                            : SanitizeName(so.DisplayName);

                        string audioFolder = CombineUnityPath(audioOutputRoot, subfolder);
                        string pngCharacterFolder = CombineUnityPath(CharactersPngRoot, subfolder);
                        if (!AssetDatabase.IsValidFolder(pngCharacterFolder) && so.IsPartyTriggerItem)
                        {
                            pngCharacterFolder = CombineUnityPath(CharactersPngRoot, "Golden_Christmas_Star");
                        }
                        string spriteFolder = CombineUnityPath(spritesOutputRoot, subfolder);

                        List<AudioClip> clips = FindClipsInUnityFolder(audioFolder);
                        List<Sprite> sprites = FindSpritesInUnityFolder(pngCharacterFolder, out _, out _);
                        if (sprites.Count == 0)
                        {
                            sprites = FindSpritesInUnityFolder(spriteFolder, out int svgCount, out int svgSpriteOk);
                            totalSvgFound += svgCount;
                            resolvedSvgSprites += svgSpriteOk;
                        }

                        Sprite matchedIcon = sharedIcons.FirstOrDefault(s =>
                            TryParseLeadingNumber(s.name, out int num) && num == so.SortOrder + 1);

                        Color? parsedThemeColor = null;
                        if (_manifestIconColorsByNumber.TryGetValue(so.SortOrder + 1, out Color manifestColor))
                        {
                            parsedThemeColor = manifestColor;
                        }
                        else if (matchedIcon != null)
                        {
                            string iconUnityPath = AssetDatabase.GetAssetPath(matchedIcon);
                            if (!string.IsNullOrEmpty(iconUnityPath) &&
                                iconUnityPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
                                TryExtractPrimaryColorFromSvg(UnityPathToDiskPath(iconUnityPath), out Color c))
                            {
                                parsedThemeColor = c;
                            }
                        }

                        SelectBestClipsAndSprites(
                            clips,
                            sprites,
                            matchedIcon,
                            out AudioClip mainLoop,
                            out AudioClip secondaryLoop,
                            out AudioClip partyLoop,
                            out Sprite iconSprite,
                            out Sprite idleSprite,
                            out List<Sprite> singingFrames,
                            out Sprite partySprite);

                        AudioClip resolvedMainLoop = so.MainLoopClip != null ? so.MainLoopClip : mainLoop;
                        AudioClip resolvedSecondaryLoop = so.SecondaryLoopClip != null ? so.SecondaryLoopClip : secondaryLoop;
                        AudioClip resolvedPartyLoop = so.PartyLoopClip != null ? so.PartyLoopClip : partyLoop;

                        so.RelinkImportedMedia(
                            resolvedMainLoop,
                            resolvedPartyLoop,
                            clips,
                            iconSprite,
                            idleSprite,
                            singingFrames,
                            partySprite,
                            sprites,
                            resolvedSecondaryLoop,
                            popClip,
                            parsedThemeColor);
                        EditorUtility.SetDirty(so);
                        updated++;
                    }
                    catch (Exception itemEx)
                    {
                        Debug.LogWarning($"[ScratchAssetImporter] Re-link skipped item {i}: {itemEx.Message}");
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _svgPackageMissingWarning = totalSvgFound > 0 && resolvedSvgSprites == 0;
                _statusMessage = $"Re-linked media across {updated} CharacterSoundData assets.";
                _statusType = _svgPackageMissingWarning ? MessageType.Warning : MessageType.Info;
                return updated;
            }
            finally
            {
                if (showDialogs)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
        }

        /// <summary>
        /// Ensures all pre-rasterized 1024x1024 Ultra HD / Stage RGBA PNGs under Assets/Art/
        /// are imported as Uncompressed Sprite (Single) with maxTextureSize=2048, Bilinear filtering,
        /// AlphaIsTransparency, and MipMaps disabled for razor-sharp Canvas rendering.
        /// </summary>
        public static void ConfigureCharactersPngTextures(float pixelsPerUnit = 100f)
        {
            string artRootUnity = "Assets/Art";
            string diskRoot = UnityPathToDiskPath(artRootUnity);
            if (!Directory.Exists(diskRoot))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(CharactersPngRoot))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            string[] pngFiles = Directory.GetFiles(diskRoot, "*.png", SearchOption.AllDirectories);
            if (pngFiles.Length == 0)
            {
                return;
            }

            string dataRoot = Path.GetFullPath(Application.dataPath).TrimEnd('\\', '/');
            var pendingReimports = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string diskFile in pngFiles)
                {
                    string fullFile = Path.GetFullPath(diskFile);
                    if (!fullFile.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = fullFile.Substring(dataRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
                    string unityPath = $"Assets/{rel}";

                    TextureImporter importer = AssetImporter.GetAtPath(unityPath) as TextureImporter;
                    if (importer == null)
                    {
                        pendingReimports.Add(unityPath);
                        continue;
                    }

                    const int targetMaxSize = 2048;

                    bool needsChange =
                        importer.textureType != TextureImporterType.Sprite ||
                        importer.spriteImportMode != SpriteImportMode.Single ||
                        importer.mipmapEnabled ||
                        !importer.alphaIsTransparency ||
                        importer.filterMode != FilterMode.Bilinear ||
                        importer.textureCompression != TextureImporterCompression.Uncompressed ||
                        importer.maxTextureSize != targetMaxSize;

                    if (needsChange)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        if (unityPath.IndexOf("/CharactersPNG/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            importer.spritePixelsPerUnit = pixelsPerUnit;
                        }
                        importer.mipmapEnabled = false;
                        importer.alphaIsTransparency = true;
                        importer.filterMode = FilterMode.Bilinear;
                        importer.anisoLevel = 4;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.maxTextureSize = targetMaxSize;
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (pendingReimports.Count > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string unityPath in pendingReimports)
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(unityPath) as TextureImporter;
                        if (importer == null) continue;

                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        if (unityPath.IndexOf("/CharactersPNG/", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            importer.spritePixelsPerUnit = pixelsPerUnit;
                        }
                        importer.mipmapEnabled = false;
                        importer.alphaIsTransparency = true;
                        importer.filterMode = FilterMode.Bilinear;
                        importer.anisoLevel = 4;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.maxTextureSize = 2048;
                        importer.SaveAndReimport();
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private void ConfigureImportedAssets(
            IReadOnlyList<string> audioPaths,
            IReadOnlyList<string> bitmapPaths,
            IReadOnlyList<string> svgPaths)
        {
            Type svgImporterType = FindTypeInLoadedAssemblies("Unity.VectorGraphics.Editor.SVGImporter")
                                   ?? FindTypeInLoadedAssemblies("UnityEditor.VectorGraphics.SVGImporter");

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string audioPath in audioPaths)
                {
                    try
                    {
                        AudioImporter importer = AssetImporter.GetAtPath(audioPath) as AudioImporter;
                        if (importer == null) continue;

                        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                        settings.loadType = decompressAudioOnLoad
                            ? AudioClipLoadType.DecompressOnLoad
                            : AudioClipLoadType.CompressedInMemory;
                        settings.compressionFormat = audioCompression;
                        settings.quality = audioQuality;

                        importer.defaultSampleSettings = settings;
                        importer.forceToMono = forceAudioMono;
                        importer.loadInBackground = false;
                        importer.SaveAndReimport();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ScratchAssetImporter] AudioImporter warning on '{audioPath}': {ex.Message}");
                    }
                }

                foreach (string texPath in bitmapPaths)
                {
                    try
                    {
                        TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                        if (importer == null) continue;

                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.spritePixelsPerUnit = spritePixelsPerUnit;
                        importer.mipmapEnabled = false;
                        importer.alphaIsTransparency = true;
                        importer.SaveAndReimport();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ScratchAssetImporter] TextureImporter warning on '{texPath}': {ex.Message}");
                    }
                }

                if (svgImporterType != null && svgPaths != null && svgPaths.Count > 0)
                {
                    PropertyInfo svgTypeProp = svgImporterType.GetProperty("SvgType", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo textureSizeProp = svgImporterType.GetProperty("TextureSize", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo keepAspectProp = svgImporterType.GetProperty("KeepTextureAspectRatio", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo preserveSvgAspectProp = svgImporterType.GetProperty("PreserveSVGImageAspect", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo svgPpuProp = svgImporterType.GetProperty("SvgPixelsPerUnit", BindingFlags.Public | BindingFlags.Instance)
                                              ?? svgImporterType.GetProperty("PixelsPerUnit", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo useSvgPpuProp = svgImporterType.GetProperty("UseSVGPixelsPerUnit", BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo alignmentProp = svgImporterType.GetProperty("Alignment", BindingFlags.Public | BindingFlags.Instance);

                    TryResolveEnumValue(svgTypeProp?.PropertyType, "TexturedSprite", out object texturedSpriteEnum);

                    // Alignment uses Unity.VectorGraphics.VectorUtils+Alignment (NOT UnityEngine.SpriteAlignment!).
                    object centerAlignmentEnum = null;
                    object bottomCenterAlignmentEnum = null;
                    if (alignmentProp != null && alignmentProp.PropertyType.IsEnum)
                    {
                        if (!TryResolveEnumValue(alignmentProp.PropertyType, "Center", out centerAlignmentEnum))
                        {
                            TryResolveEnumValue(alignmentProp.PropertyType, "MiddleCenter", out centerAlignmentEnum);
                        }
                        if (!TryResolveEnumValue(alignmentProp.PropertyType, "BottomCenter", out bottomCenterAlignmentEnum))
                        {
                            bottomCenterAlignmentEnum = centerAlignmentEnum;
                        }
                    }

                    foreach (string svgPath in svgPaths)
                    {
                        try
                        {
                            AssetImporter importer = AssetImporter.GetAtPath(svgPath);
                            if (importer == null || !svgImporterType.IsInstanceOfType(importer)) continue;

                            bool isStageSvg = svgPath.IndexOf("/Stage/", StringComparison.OrdinalIgnoreCase) >= 0;
                            bool isIconSvg = svgPath.IndexOf("/Icons/", StringComparison.OrdinalIgnoreCase) >= 0;

                            // TexturedSprite generates a Texture2D-backed Sprite compatible with UnityEngine.UI.Image!
                            TrySetProperty(importer, svgTypeProp, texturedSpriteEnum, svgPath);
                            TrySetProperty(importer, textureSizeProp, isStageSvg ? 1024 : 512, svgPath);
                            TrySetProperty(importer, keepAspectProp, true, svgPath);
                            TrySetProperty(importer, preserveSvgAspectProp, true, svgPath);
                            TrySetProperty(importer, useSvgPpuProp, true, svgPath);
                            TrySetProperty(importer, svgPpuProp, spritePixelsPerUnit, svgPath);
                            TrySetProperty(importer, alignmentProp, (isStageSvg || isIconSvg) ? centerAlignmentEnum : bottomCenterAlignmentEnum, svgPath);

                            importer.SaveAndReimport();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ScratchAssetImporter] SVGImporter warning on '{svgPath}': {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool TryResolveEnumValue(Type enumType, string memberName, out object enumValue)
        {
            enumValue = null;
            if (enumType == null || !enumType.IsEnum || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            try
            {
                enumValue = Enum.Parse(enumType, memberName, ignoreCase: true);
                return enumValue != null;
            }
            catch
            {
                return false;
            }
        }

        private static void TrySetProperty(object target, PropertyInfo prop, object value, string contextPath)
        {
            if (target == null || prop == null || !prop.CanWrite || value == null) return;

            try
            {
                object finalValue = value;
                if (!prop.PropertyType.IsInstanceOfType(value))
                {
                    if (prop.PropertyType.IsEnum && value is string strVal)
                    {
                        if (!TryResolveEnumValue(prop.PropertyType, strVal, out finalValue))
                        {
                            return;
                        }
                    }
                    else
                    {
                        finalValue = Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture);
                    }
                }

                prop.SetValue(target, finalValue);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ScratchAssetImporter] Could not assign '{prop.Name}' on '{contextPath}': {ex.Message}");
            }
        }

        private static void SelectBestClipsAndSprites(
            List<AudioClip> clips,
            List<Sprite> sprites,
            Sprite sharedIconOverride,
            out AudioClip mainLoop,
            out AudioClip secondaryLoop,
            out AudioClip partyLoop,
            out Sprite iconSprite,
            out Sprite idleSprite,
            out List<Sprite> singingFrames,
            out Sprite partySprite)
        {
            AudioClip localMainLoop = null;
            AudioClip localSecondaryLoop = null;
            AudioClip localPartyLoop = null;
            Sprite localIconSprite = sharedIconOverride;
            Sprite localIdleSprite = null;
            Sprite localPartySprite = null;
            var localSingingFrames = new List<Sprite>();

            if (clips != null && clips.Count > 0)
            {
                localMainLoop = clips[0];
                if (clips.Count > 1)
                {
                    localSecondaryLoop = clips[1];
                }

                localPartyLoop = clips.FirstOrDefault(c =>
                    c != localMainLoop &&
                    c != localSecondaryLoop &&
                    ContainsAny(c.name, "party", "disco", "bonus", "chorus"));
            }

            if (sprites != null && sprites.Count > 0)
            {
                if (localIconSprite == null)
                {
                    localIconSprite = sprites.FirstOrDefault(s => ContainsAny(s.name, "icon", "thumb", "btn", "button", "portrait"));
                }

                localPartySprite = sprites.FirstOrDefault(s => ContainsAny(s.name, "party", "disco", "rainbow", "bonus", "hat"));
                localIdleSprite = sprites.FirstOrDefault(s =>
                                      s != localIconSprite &&
                                      s != localPartySprite &&
                                      ContainsAny(s.name, "idle", "stand", "normal", "base", "default"))
                                  ?? sprites.FirstOrDefault(s => s != localIconSprite && s != localPartySprite)
                                  ?? sprites[0];

                if (localIconSprite == null)
                {
                    localIconSprite = localIdleSprite;
                }

                // Collect Scratch animation frames (anim, anim2, anim3...) excluding idle when anim frames exist.
                foreach (Sprite s in sprites)
                {
                    if (s == null || s == localIconSprite || s == localPartySprite || s == localIdleSprite) continue;
                    localSingingFrames.Add(s);
                }

                if (localSingingFrames.Count == 0 && localIdleSprite != null)
                {
                    localSingingFrames.Add(localIdleSprite);
                }
            }

            mainLoop = localMainLoop;
            secondaryLoop = localSecondaryLoop;
            partyLoop = localPartyLoop;
            iconSprite = localIconSprite;
            idleSprite = localIdleSprite;
            singingFrames = localSingingFrames;
            partySprite = localPartySprite;
        }

        private static bool TryExtractPrimaryColorFromSvg(string svgFilePath, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(svgFilePath) || !File.Exists(svgFilePath)) return false;

            try
            {
                string content = File.ReadAllText(svgFilePath, Encoding.UTF8);
                MatchCollection matches = SvgHexFillRegex.Matches(content);
                if (matches.Count == 0) return false;

                Color? fallbackColor = null;
                foreach (Match match in matches)
                {
                    string hex = match.Groups[1].Value;
                    if (!ColorUtility.TryParseHtmlString(hex, out Color parsed)) continue;

                    if (fallbackColor == null) fallbackColor = parsed;

                    bool isPureBlack = hex.Equals("#000000", StringComparison.OrdinalIgnoreCase);
                    bool isPureWhite = hex.Equals("#ffffff", StringComparison.OrdinalIgnoreCase);
                    if (!isPureBlack && !isPureWhite)
                    {
                        color = parsed;
                        return true;
                    }
                }

                if (fallbackColor.HasValue)
                {
                    color = fallbackColor.Value;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string FormatKidFriendlyDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "Sprunki";
            string trimmed = rawName.Trim();
            if (trimmed.Equals("Black?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Black", StringComparison.OrdinalIgnoreCase))
            {
                return "Golden Christmas Star";
            }
            return trimmed;
        }

        private static bool TryParseLeadingNumber(string name, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;

            int len = 0;
            while (len < name.Length && char.IsDigit(name[len]))
            {
                len++;
            }

            if (len == 0) return false;
            return int.TryParse(name.Substring(0, len), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        private static List<AudioClip> LoadClips(IEnumerable<string> unityPaths)
        {
            var result = new List<AudioClip>();
            foreach (string path in unityPaths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) result.Add(clip);
            }
            return result;
        }

        private static List<Sprite> LoadSprites(IEnumerable<string> unityPaths)
        {
            var result = new List<Sprite>();
            foreach (string path in unityPaths)
            {
                Sprite sprite = LoadSpriteAtPath(path);
                if (sprite != null) result.Add(sprite);
            }
            return result;
        }

        public static Sprite LoadSpriteAtPath(string unityPath)
        {
            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(unityPath);
            if (direct != null) return direct;

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(unityPath);
            if (subAssets != null)
            {
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is Sprite sprite) return sprite;
                }
            }
            return null;
        }

        private static List<AudioClip> FindClipsInUnityFolder(string folder)
        {
            var list = new List<AudioClip>();
            if (!AssetDatabase.IsValidFolder(folder)) return list;

            string diskFolder = UnityPathToDiskPath(folder);
            if (!Directory.Exists(diskFolder)) return list;

            string[] files = Directory.GetFiles(diskFolder)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string diskFile in files)
            {
                string unityFile = CombineUnityPath(folder, Path.GetFileName(diskFile));
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(unityFile);
                if (clip != null) list.Add(clip);
            }
            return list;
        }

        private static List<Sprite> FindSpritesInUnityFolder(string folder, out int svgCount, out int resolvedSvgSprites)
        {
            svgCount = 0;
            resolvedSvgSprites = 0;
            var list = new List<Sprite>();

            if (!AssetDatabase.IsValidFolder(folder)) return list;

            string diskFolder = UnityPathToDiskPath(folder);
            if (!Directory.Exists(diskFolder)) return list;

            string[] files = Directory.GetFiles(diskFolder)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => TryParseLeadingNumber(Path.GetFileNameWithoutExtension(f), out int n) ? n : int.MaxValue)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string diskFile in files)
            {
                string unityFile = CombineUnityPath(folder, Path.GetFileName(diskFile));
                bool isSvg = diskFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
                if (isSvg) svgCount++;

                Sprite sprite = LoadSpriteAtPath(unityFile);
                if (sprite != null)
                {
                    list.Add(sprite);
                    if (isSvg) resolvedSvgSprites++;
                }
            }

            return list;
        }

        private string[] ParseHorrorKeywords()
        {
            if (string.IsNullOrWhiteSpace(horrorKeywordsCsv)) return Array.Empty<string>();

            return horrorKeywordsCsv
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();
        }

        private static bool IsExcludedCostumeForKids(string costumeName, string[] horrorKeywords)
        {
            if (string.IsNullOrWhiteSpace(costumeName)) return false;
            if (costumeName.Trim().Equals("bar", StringComparison.OrdinalIgnoreCase)) return true;
            return IsHorrorName(costumeName, horrorKeywords);
        }

        private static bool IsHorrorName(string assetName, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(assetName) || keywords == null || keywords.Length == 0) return false;

            for (int i = 0; i < keywords.Length; i++)
            {
                if (assetName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static SoundCategory GuessCategoryForOrder(int orderIndex, string targetName, int totalCount)
        {
            if (orderIndex == totalCount - 1 || LooksLikePartyTrigger(targetName)) return SoundCategory.Special;
            if (ContainsAny(targetName, "beat", "drum", "kick", "snare", "perc", "oren", "raddy", "clukr", "fun bot", "vineria")) return SoundCategory.Beat;
            if (ContainsAny(targetName, "fx", "effect", "synth", "bass", "gray", "brud", "garnold", "owakcx", "sky")) return SoundCategory.Effect;
            if (ContainsAny(targetName, "melody", "piano", "lead", "sun", "durple", "tree", "simon", "tunner")) return SoundCategory.Melody;
            if (ContainsAny(targetName, "voice", "vocal", "sing", "computer", "wenda", "pinki", "jevin")) return SoundCategory.Voice;

            if (orderIndex < 5) return SoundCategory.Beat;
            if (orderIndex < 10) return SoundCategory.Effect;
            if (orderIndex < 15) return SoundCategory.Melody;
            return orderIndex < 19 ? SoundCategory.Voice : SoundCategory.Special;
        }

        private static bool LooksLikePartyTrigger(string name)
        {
            return ContainsAny(name, "black", "mystery", "hat", "party", "special", "bonus", "star", "christmas", "20");
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static Color GetKidFriendlyPaletteColor(int index, int total)
        {
            float hue = (float)(index % Math.Max(1, total)) / Math.Max(1, total);
            return Color.HSVToRGB(hue, 0.72f, 0.96f);
        }

        private static Dictionary<string, string> BuildSourceFileIndex(string rootFolder)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories);

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                if (!index.ContainsKey(fileName))
                {
                    index[fileName] = files[i];
                }

                string stem = Path.GetFileNameWithoutExtension(fileName);
                if (!string.IsNullOrEmpty(stem) && !index.ContainsKey(stem))
                {
                    index[stem] = files[i];
                }
            }
            return index;
        }

        private static string ResolveMd5FileName(string md5ext, string assetId, string dataFormat)
        {
            if (!string.IsNullOrWhiteSpace(md5ext)) return md5ext.Trim();
            if (!string.IsNullOrWhiteSpace(assetId) && !string.IsNullOrWhiteSpace(dataFormat))
            {
                return $"{assetId.Trim()}.{dataFormat.Trim().TrimStart('.')}";
            }
            return string.Empty;
        }

        private static bool TryResolveSourceFile(Dictionary<string, string> index, string fileName, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(fileName)) return false;

            if (index.TryGetValue(fileName, out fullPath) && File.Exists(fullPath))
            {
                return true;
            }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrEmpty(stem) && index.TryGetValue(stem, out fullPath) && File.Exists(fullPath))
            {
                return true;
            }

            return false;
        }

        private string BuildCleanAssetBaseName(string rawName, int index, string fallbackPrefix)
        {
            string clean = SanitizeName(string.IsNullOrWhiteSpace(rawName) ? $"{fallbackPrefix}_{index:D2}" : rawName);
            return prefixOrderIndexOnFiles ? $"{index:D2}_{clean}" : clean;
        }

        private static string GetUniqueFileName(string baseName, string extensionWithDot, HashSet<string> usedFileNames)
        {
            string candidate = $"{baseName}{extensionWithDot}";
            if (usedFileNames.Add(candidate)) return candidate;

            int suffix = 2;
            while (true)
            {
                candidate = $"{baseName}_{suffix:D2}{extensionWithDot}";
                if (usedFileNames.Add(candidate)) return candidate;
                suffix++;
            }
        }

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unnamed";

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            bool lastWasUnderscore = false;

            foreach (char ch in raw.Trim())
            {
                bool invalid = Array.IndexOf(invalidChars, ch) >= 0 || ch == '/' || ch == '\\' || ch == ':' || ch == '.';
                if (invalid || char.IsWhiteSpace(ch))
                {
                    if (!lastWasUnderscore && sb.Length > 0)
                    {
                        sb.Append('_');
                        lastWasUnderscore = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    lastWasUnderscore = ch == '_';
                }
            }

            string result = sb.ToString().Trim('_', ' ', '.');
            if (string.IsNullOrEmpty(result)) result = "Unnamed";
            if (ReservedWindowsNames.Contains(result)) result = "_" + result;
            return result;
        }

        private static void EnsureUnityFolderExists(string unityFolderPath)
        {
            if (string.IsNullOrWhiteSpace(unityFolderPath)) return;

            string normalized = unityFolderPath.Replace('\\', '/').Trim().TrimEnd('/');
            if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"Assets/{normalized.TrimStart('/')}";
            }

            if (AssetDatabase.IsValidFolder(normalized)) return;

            string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
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

        private static string CombineUnityPath(string left, string right)
        {
            string cleanLeft = (left ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            string cleanRight = (right ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return string.IsNullOrEmpty(cleanLeft) ? cleanRight : $"{cleanLeft}/{cleanRight}";
        }

        private static string UnityPathToDiskPath(string unityPath)
        {
            string normalized = unityPath.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(Application.dataPath, normalized.Substring("Assets/".Length));
            }
            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Application.dataPath;
            }
            return Path.GetFullPath(normalized);
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
