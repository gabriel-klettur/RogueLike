using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Scans python/assets/ and generates asset_map.csv for full asset traceability.
    /// Maps each Python asset to its Unity target path, PPU, pivot, atlas group, and migration status.
    /// Menu: Valkur > Assets > Generate Asset Map CSV
    /// </summary>
    public static partial class AssetMapGenerator
    {
        private const string CSV_HEADER =
            "asset_id,source_path_python,target_path_unity,asset_type,pixels_per_unit,pivot,filter_mode,compression,atlas_group,owner_system,migration_status";

        private static readonly string[] ExcludedTopFolders = { "AAA_in_process", "download", "inspiration" };

        [MenuItem("Valkur/Assets/Generate Asset Map CSV")]
        public static void Generate()
        {
            string pythonAssetsRoot = FindPythonAssetsRoot();
            if (string.IsNullOrEmpty(pythonAssetsRoot))
            {
                Debug.LogError("[AssetMapGenerator] Could not find python/assets/ folder.");
                return;
            }

            string outputPath = Path.Combine(Application.dataPath,
                "../../docs/Migration_python_to_unity/02_assets/asset_map.csv");
            outputPath = Path.GetFullPath(outputPath);

            var entries = new List<AssetEntry>();
            ScanDirectory(pythonAssetsRoot, pythonAssetsRoot, entries);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            WriteCsv(outputPath, entries);

            int migrated = 0, pending = 0;
            foreach (var e in entries)
            {
                if (e.migrationStatus == "migrated") migrated++;
                else pending++;
            }

            Debug.Log($"[AssetMapGenerator] Generated asset_map.csv with {entries.Count} entries " +
                      $"({migrated} migrated, {pending} pending). Path: {outputPath}");
        }

        private static string FindPythonAssetsRoot()
        {
            // Walk up from Unity project to workspace root
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string candidate = Path.Combine(dir, "python", "assets");
            if (Directory.Exists(candidate)) return candidate;

            candidate = Path.Combine(dir, "..", "python", "assets");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);

            return null;
        }

        private static void ScanDirectory(string root, string pythonRoot, List<AssetEntry> entries)
        {
            string[] extensions = { "*.png", "*.wav", "*.ogg", "*.mp3", "*.flac" };

            foreach (string ext in extensions)
            {
                foreach (string file in Directory.GetFiles(root, ext, SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(pythonRoot.Length + 1).Replace('\\', '/');
                    string topFolder = relativePath.Split('/')[0].ToLowerInvariant();

                    if (Array.IndexOf(ExcludedTopFolders, topFolder) >= 0)
                        continue;

                    var entry = ClassifyAsset(relativePath, file);
                    entries.Add(entry);
                }
            }
        }

        // ClassifyAsset, GenerateAssetId, WriteCsv, EscapeCsv, AssetEntry → AssetMapGenerator.Classifier.cs
    }
}
