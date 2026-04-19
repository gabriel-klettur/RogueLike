using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor menu items for managing tile-editor overlay overrides.
    /// Located under Valkur > Tile Editor.
    /// </summary>
    public static class TileOverlayEditorMenu
    {
        private const string OVERLAY_SOURCE_DIR = "../../../python/data/worlds/base/zones/overlays";
        private const string STREAMING_MAPS_DIR = "StreamingAssets/Maps";

        [MenuItem("Valkur/Tile Editor/Open Override Folder")]
        public static void OpenOverrideFolder()
        {
            string dir = TileOverlayPersistence.OverrideDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem("Valkur/Tile Editor/List Overrides")]
        public static void ListOverrides()
        {
            var files = TileOverlayPersistence.ListOverrideFiles();
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Tile Editor", "No overrides saved yet.", "OK");
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{files.Length} override(s) in:\n{TileOverlayPersistence.OverrideDirectory}\n");
            for (int i = 0; i < files.Length; i++)
                sb.AppendLine("  • " + Path.GetFileName(files[i]));
            EditorUtility.DisplayDialog("Tile Editor — Overrides", sb.ToString(), "OK");
        }

        [MenuItem("Valkur/Tile Editor/Bake Overrides into StreamingAssets")]
        public static void BakeOverridesToStreamingAssets()
        {
            var files = TileOverlayPersistence.ListOverrideFiles();
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Bake Overrides", "No overrides to bake.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Bake Overrides",
                    $"Copy {files.Length} override(s) into StreamingAssets/Maps/?\n\n" +
                    "Existing overlay files with the same name will be overwritten.\n" +
                    "(Original files are NOT touched in the Python source tree.)",
                    "Bake", "Cancel"))
                return;

            string streamingDir = Path.Combine(Application.dataPath, STREAMING_MAPS_DIR);
            if (!Directory.Exists(streamingDir))
                Directory.CreateDirectory(streamingDir);

            int baked = 0;
            var failed = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string fileName = Path.GetFileName(files[i]); // e.g. "Forest.overlay.json"
                    string dest = Path.Combine(streamingDir, fileName);
                    File.Copy(files[i], dest, overwrite: true);
                    baked++;
                }
                catch (System.Exception ex)
                {
                    failed.Add($"{Path.GetFileName(files[i])}: {ex.Message}");
                }
            }

            AssetDatabase.Refresh();
            string msg = $"Baked {baked}/{files.Length} override(s) to StreamingAssets/Maps/.";
            if (failed.Count > 0)
                msg += "\n\nFailures:\n" + string.Join("\n", failed);
            EditorUtility.DisplayDialog("Bake Overrides", msg, "OK");
            Debug.Log($"[TileOverlayEditorMenu] {msg}");
        }

        [MenuItem("Valkur/Tile Editor/Clear All Overrides")]
        public static void ClearAllOverrides()
        {
            var files = TileOverlayPersistence.ListOverrideFiles();
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("Tile Editor", "No overrides to clear.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Clear All Overrides",
                    $"Permanently delete {files.Length} override(s) from\n{TileOverlayPersistence.OverrideDirectory}?",
                    "Delete", "Cancel"))
                return;

            int deleted = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try { File.Delete(files[i]); deleted++; }
                catch (System.Exception ex) { Debug.LogError($"[TileOverlayEditorMenu] Could not delete {files[i]}: {ex.Message}"); }
            }
            EditorUtility.DisplayDialog("Tile Editor", $"Deleted {deleted}/{files.Length} override(s).", "OK");
        }
    }
}
