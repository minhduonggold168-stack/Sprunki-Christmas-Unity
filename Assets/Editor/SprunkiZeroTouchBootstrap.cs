using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SprunkiKids.Editor
{
    /// <summary>
    /// Zero-touch automation bootstrapper.
    /// Automatically detects Assets/RawAssets/project.json on Editor domain reload,
    /// executes ScratchAssetImporter + SprunkiAutoSetup on SampleScene, saves the scene & assets,
    /// writes a persistent completion marker to prevent infinite Play-mode domain-reload loops,
    /// and enters Play Mode automatically.
    /// </summary>
    [InitializeOnLoad]
    public static class SprunkiZeroTouchBootstrap
    {
        private const string LegacyMarkerV1RelativePath = "ProjectSettings/SprunkiZeroTouchBuild_v1.done";
        private const string LegacyMarkerV2RelativePath = "ProjectSettings/SprunkiZeroTouchBuild_v2.done";
        private const string LegacyChristmasV2RelativePath = "ProjectSettings/SprunkiChristmas_v2.done";
        private const string LegacyChristmasV3RelativePath = "ProjectSettings/SprunkiChristmas_v3_PNGFix.done";
        private const string LegacyChristmasV4RelativePath = "ProjectSettings/SprunkiChristmas_v4_AuthenticBeatAndUI.done";
        private const string LegacyChristmasV5RelativePath = "ProjectSettings/SprunkiChristmas_v5_HD_AutoDJ_NoelTree.done";
        private const string MarkerRelativePath = "ProjectSettings/SprunkiChristmas_v6_AutoRandom2Min.done";
        private const string TargetScenePath = "Assets/Scenes/SampleScene.unity";

        static SprunkiZeroTouchBootstrap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                return;
            }

            if (File.Exists(GetMarkerFullPath()))
            {
                return;
            }

            string rawAssetsDir = Path.Combine(Application.dataPath, "RawAssets");
            if (!HasRawSourceFiles(rawAssetsDir))
            {
                return;
            }

            EditorApplication.delayCall += TryRunZeroTouchPipeline;
        }

        [MenuItem("Tools/Sprunki/Re-Run Zero-Touch Build & Play", priority = 1)]
        public static void ForceReRunZeroTouchPipeline()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += ForceReRunZeroTouchPipeline;
                return;
            }

            DeleteMarkerIfExists(GetFullPathForRelative(LegacyMarkerV1RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyMarkerV2RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV2RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV3RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV4RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV5RelativePath));
            DeleteMarkerIfExists(GetMarkerFullPath());

            TryRunZeroTouchPipeline();
        }

        private static void TryRunZeroTouchPipeline()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                return;
            }

            if (File.Exists(GetMarkerFullPath()))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRunZeroTouchPipeline;
                return;
            }

            string rawAssetsDir = Path.Combine(Application.dataPath, "RawAssets");
            if (!HasRawSourceFiles(rawAssetsDir))
            {
                return;
            }

            DeleteMarkerIfExists(GetFullPathForRelative(LegacyMarkerV1RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyMarkerV2RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV2RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV3RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV4RelativePath));
            DeleteMarkerIfExists(GetFullPathForRelative(LegacyChristmasV5RelativePath));

            Debug.Log("[SprunkiZeroTouchBootstrap] Starting automated Sprunki Christmas Kids Wonderland migration, Auto Random 2-Minute DJ Showcase (v6)...");

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                bool importOk = ScratchAssetImporter.RunHeadlessImport(rawAssetsDir);
                if (!importOk)
                {
                    Debug.LogError("[SprunkiZeroTouchBootstrap] ScratchAssetImporter.RunHeadlessImport failed (no playable assets recovered). Aborting auto Play Mode.");
                    return;
                }

                if (File.Exists(GetDiskPathForUnityAsset(TargetScenePath)))
                {
                    var activeScene = EditorSceneManager.GetActiveScene();
                    if (!string.Equals(activeScene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
                    }
                }

                bool sceneOk = SprunkiAutoSetup.BuildCompleteScene(silentMode: true);
                if (!sceneOk)
                {
                    Debug.LogError("[SprunkiZeroTouchBootstrap] SprunkiAutoSetup.BuildCompleteScene failed. Aborting auto Play Mode.");
                    return;
                }

                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();

                // Write SprunkiChristmas_v2 marker BEFORE entering Play Mode so the Play-mode domain reload does not re-trigger!
                string markerPath = GetMarkerFullPath();
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath) ?? "ProjectSettings");
                File.WriteAllText(markerPath, $"Sprunki Christmas Kids Wonderland built cleanly at {DateTime.UtcNow:O}\n");

                Debug.Log("[SprunkiZeroTouchBootstrap] Christmas A-Z build & scene save succeeded! Entering Play Mode...");

                EditorApplication.delayCall += () =>
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        EditorApplication.isPlaying = true;
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static bool HasRawSourceFiles(string rawAssetsDir)
        {
            if (string.IsNullOrWhiteSpace(rawAssetsDir) || !Directory.Exists(rawAssetsDir)) return false;
            return File.Exists(Path.Combine(rawAssetsDir, "sprunki_clean_manifest.json")) ||
                   File.Exists(Path.Combine(rawAssetsDir, "sprunki_manifest.json")) ||
                   File.Exists(Path.Combine(rawAssetsDir, "project.json"));
        }

        private static void DeleteMarkerIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetFullPathForRelative(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        }

        private static string GetMarkerFullPath()
        {
            return GetFullPathForRelative(MarkerRelativePath);
        }

        private static string GetDiskPathForUnityAsset(string unityAssetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, unityAssetPath));
        }
    }
}
