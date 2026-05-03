#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// One-shot bulk re-importer that walks the project's pixel-art folders
    /// and forces every TextureImporter to re-run <see cref="ValkurAssetPostprocessor.OnPreprocessTexture"/>.
    ///
    /// Why it exists: changing the postprocessor (e.g. adding per-platform
    /// compression overrides) does NOT automatically re-import already-cached
    /// assets — Unity only triggers the preprocessor when the source PNG
    /// changes or the .meta is invalidated. Without this tool, designers had
    /// to right-click each folder and pick "Reimport" by hand. Running this
    /// once after a postprocessor change brings every existing asset in line
    /// with the new policy.
    ///
    /// Idempotent: re-importing an asset that already matches the policy is
    /// a fast no-op inside the postprocessor.
    /// </summary>
    public static class BulkReimportTool
    {
        // Folders the postprocessor cares about. Keeping this list in sync
        // with ValkurAssetPostprocessor.OnPreprocessTexture is the price of
        // not invoking the postprocessor's own selector publicly.
        private static readonly string[] TargetFolders =
        {
            "Assets/_Project/Art",
            "Assets/_Project/Resources/Tiles",
            "Assets/_Project/Resources/Buildings",
            "Assets/_Project/Audio",
        };

        [MenuItem("Valkur/Assets/Reimport All Pixel Art (apply postprocessor)")]
        public static void ReimportAll()
        {
            var paths = CollectAssetPaths();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[BulkReimportTool] No assets matched the target folders.");
                return;
            }

            int total = paths.Count;
            int done  = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var path in paths)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    done++;
                    if ((done & 511) == 0 || done == total)
                        EditorUtility.DisplayProgressBar(
                            "Bulk reimport",
                            $"{done}/{total}: {path}",
                            (float)done / total);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[BulkReimportTool] Reimported {done}/{total} assets across " +
                      $"{TargetFolders.Length} folders. The TextureImporter postprocessor " +
                      "ran on each, applying the current PPU / filter / compression policy.");
        }

        /// <summary>
        /// Used by tests / MCP-driven scripted runs that want the work done
        /// without the progress bar (the modal bar can deadlock headless mode).
        /// Returns the count of assets reimported.
        /// </summary>
        public static int ReimportAllSilent()
        {
            var paths = CollectAssetPaths();
            if (paths.Count == 0) return 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }

            return paths.Count;
        }

        private static List<string> CollectAssetPaths()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(8000);

            foreach (var folder in TargetFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                // textures + audio are the two asset classes the postprocessor
                // reads. We don't reimport SOs / prefabs — they have nothing
                // to gain from a fresh import pass.
                string[] guids = AssetDatabase.FindAssets("t:Texture2D t:AudioClip", new[] { folder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (seen.Add(path)) result.Add(path);
                }
            }
            return result;
        }
    }
}
#endif
