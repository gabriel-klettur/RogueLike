using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Copies pending assets from python/assets/ to Unity project using asset_map.csv as source of truth.
    /// Applies import policies (PPU, pivot, filter) via ValkurAssetPostprocessor after copy.
    /// Menu: Valkur > Assets > Migrate Pending Assets
    /// Menu: Valkur > Assets > Migrate Pending Assets (Dry Run)
    /// </summary>
    public static class AssetMigrator
    {
        [MenuItem("Valkur/Assets/Migrate Pending Assets")]
        public static void MigratePending() => DoMigration(false);

        [MenuItem("Valkur/Assets/Migrate Pending Assets (Dry Run)")]
        public static void MigratePendingDryRun() => DoMigration(true);

        private static void DoMigration(bool dryRun)
        {
            string csvPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../docs/Migration_python_to_unity/02_assets/asset_map.csv"));

            if (!File.Exists(csvPath))
            {
                Debug.LogError("[AssetMigrator] asset_map.csv not found. Run 'Valkur > Assets > Generate Asset Map CSV' first.");
                return;
            }

            string pythonAssetsRoot = FindPythonAssetsRoot();
            if (string.IsNullOrEmpty(pythonAssetsRoot))
            {
                Debug.LogError("[AssetMigrator] python/assets/ folder not found.");
                return;
            }

            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] lines = File.ReadAllLines(csvPath);
            int copied = 0, skipped = 0, errors = 0;
            var pendingRefreshPaths = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] cols = ParseCsvLine(line);
                if (cols.Length < 11) continue;

                string status = cols[10].Trim();
                if (status == "migrated") { skipped++; continue; }

                string sourcePath = cols[1].Trim();       // e.g. assets/tiles/foo.png
                string targetPath = cols[2].Trim();       // e.g. Assets/_Project/Resources/Tiles/foo.png

                // Source: python/assets/... but CSV has assets/...
                string pythonSource = Path.Combine(
                    Path.GetDirectoryName(pythonAssetsRoot),
                    sourcePath.Replace('/', Path.DirectorySeparatorChar));
                string unityTarget = Path.Combine(unityProjectRoot,
                    targetPath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(pythonSource))
                {
                    errors++;
                    if (!dryRun)
                        Debug.LogWarning($"[AssetMigrator] Source not found: {pythonSource}");
                    continue;
                }

                if (File.Exists(unityTarget)) { skipped++; continue; }

                if (dryRun)
                {
                    copied++;
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(unityTarget));
                    File.Copy(pythonSource, unityTarget, false);
                    copied++;

                    // Collect relative path for AssetDatabase refresh
                    string relAssetPath = targetPath;
                    pendingRefreshPaths.Add(relAssetPath);
                }
                catch (Exception ex)
                {
                    errors++;
                    Debug.LogError($"[AssetMigrator] Failed to copy {sourcePath}: {ex.Message}");
                }
            }

            string mode = dryRun ? "DRY RUN" : "COMPLETE";
            Debug.Log($"[AssetMigrator] {mode}: {copied} copied, {skipped} skipped, {errors} errors.");

            if (!dryRun && pendingRefreshPaths.Count > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log($"[AssetMigrator] AssetDatabase refreshed. ValkurAssetPostprocessor will apply import policies.");

                // Update CSV status to 'migrated'
                UpdateCsvStatus(csvPath);
            }
        }

        private static void UpdateCsvStatus(string csvPath)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] lines = File.ReadAllLines(csvPath);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cols = ParseCsvLine(lines[i]);
                if (cols.Length < 11) continue;

                string targetPath = cols[2].Trim();
                string unityTarget = Path.Combine(unityProjectRoot,
                    targetPath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(unityTarget) && cols[10].Trim() != "migrated")
                {
                    cols[10] = "migrated";
                    lines[i] = string.Join(",", cols);
                }
            }

            File.WriteAllLines(csvPath, lines, System.Text.Encoding.UTF8);
        }

        private static string FindPythonAssetsRoot()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string candidate = Path.Combine(dir, "python", "assets");
            if (Directory.Exists(candidate)) return candidate;
            return null;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
