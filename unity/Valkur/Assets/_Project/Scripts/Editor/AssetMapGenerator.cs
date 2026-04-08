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
    public static class AssetMapGenerator
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

        private static AssetEntry ClassifyAsset(string relativePath, string absolutePath)
        {
            string lower = relativePath.ToLowerInvariant();
            string ext = Path.GetExtension(lower);
            var entry = new AssetEntry { sourcePath = "assets/" + relativePath };

            // Determine asset type and target path
            if (lower.StartsWith("audio/"))
            {
                entry.assetType = ext == ".mp3" || lower.Contains("music") ? "music" : "sfx";
                entry.targetPath = "Assets/_Project/Audio/" + relativePath.Substring("audio/".Length);
                entry.ppu = "0";
                entry.pivot = "N/A";
                entry.filterMode = "N/A";
                entry.compression = entry.assetType == "music" ? "Streaming/Vorbis" : "DecompressOnLoad/PCM";
                entry.atlasGroup = "N/A";
                entry.ownerSystem = "AudioManager";
            }
            else if (lower.StartsWith("tiles/"))
            {
                entry.assetType = "tile";
                entry.targetPath = "Assets/_Project/Resources/Tiles/" + relativePath.Substring("tiles/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "env-tiles";
                entry.ownerSystem = "WorldGridBuilder";
            }
            else if (lower.StartsWith("characters/"))
            {
                entry.assetType = "character";
                entry.targetPath = "Assets/_Project/Art/Characters/" + relativePath.Substring("characters/".Length);
                entry.ppu = "16";
                entry.pivot = "Bottom-Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "characters";
                entry.ownerSystem = "DirectionalAnimator";
            }
            else if (lower.StartsWith("npc/"))
            {
                entry.assetType = "npc";
                entry.targetPath = "Assets/_Project/Art/NPC/" + relativePath.Substring("npc/".Length);
                entry.ppu = "16";
                entry.pivot = "Bottom-Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "npc";
                entry.ownerSystem = "EntityAnimationBinder";
            }
            else if (lower.StartsWith("buildings/"))
            {
                entry.assetType = "building";
                entry.targetPath = "Assets/_Project/Resources/Buildings/" + relativePath.Substring("buildings/".Length);
                entry.ppu = "32";
                entry.pivot = "Bottom-Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "buildings";
                entry.ownerSystem = "BuildingLoader";
            }
            else if (lower.StartsWith("items/"))
            {
                entry.assetType = "item";
                entry.targetPath = "Assets/_Project/Art/Items/" + relativePath.Substring("items/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "items";
                entry.ownerSystem = "Inventory";
            }
            else if (lower.StartsWith("projectiles/"))
            {
                entry.assetType = "projectile";
                entry.targetPath = "Assets/_Project/Art/Spells/" + relativePath.Substring("projectiles/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "spells";
                entry.ownerSystem = "SpellCaster";
            }
            else if (lower.StartsWith("explosions/"))
            {
                entry.assetType = "vfx";
                entry.targetPath = "Assets/_Project/Art/VFX/" + relativePath.Substring("explosions/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "vfx";
                entry.ownerSystem = "VFXManager";
            }
            else if (lower.StartsWith("spells/"))
            {
                entry.assetType = "spell_vfx";
                entry.targetPath = "Assets/_Project/Art/Spells/" + relativePath.Substring("spells/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "spells";
                entry.ownerSystem = "SpellCaster";
            }
            else if (lower.StartsWith("particles_sprites/") || lower.StartsWith("particles_sprites_2/"))
            {
                entry.assetType = "particle";
                entry.targetPath = "Assets/_Project/Art/VFX/Particles/" + Path.GetFileName(relativePath);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "vfx";
                entry.ownerSystem = "ParticleEmitter";
            }
            else if (lower.StartsWith("ui/"))
            {
                entry.assetType = "ui";
                entry.targetPath = "Assets/_Project/Art/UI/" + relativePath.Substring("ui/".Length);
                entry.ppu = "100";
                entry.pivot = "Center";
                entry.filterMode = "Bilinear";
                entry.compression = "None";
                entry.atlasGroup = "ui";
                entry.ownerSystem = "UI";
            }
            else if (lower.StartsWith("objects/"))
            {
                entry.assetType = "prop";
                entry.targetPath = "Assets/_Project/Art/Misc/" + relativePath.Substring("objects/".Length);
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "misc";
                entry.ownerSystem = "World";
            }
            else if (lower.StartsWith("views/"))
            {
                entry.assetType = "background";
                entry.targetPath = "Assets/_Project/Art/Backgrounds/" + relativePath.Substring("views/".Length);
                entry.ppu = "100";
                entry.pivot = "Center";
                entry.filterMode = "Bilinear";
                entry.compression = "None";
                entry.atlasGroup = "backgrounds";
                entry.ownerSystem = "World";
            }
            else
            {
                entry.assetType = "unknown";
                entry.targetPath = "Assets/_Project/Art/Unsorted/" + relativePath;
                entry.ppu = "16";
                entry.pivot = "Center";
                entry.filterMode = "Point";
                entry.compression = "None";
                entry.atlasGroup = "unsorted";
                entry.ownerSystem = "Unknown";
            }

            // Check if already migrated
            string unityFullPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", entry.targetPath));
            entry.migrationStatus = File.Exists(unityFullPath) ? "migrated" : "pending";
            entry.assetId = GenerateAssetId(relativePath);

            return entry;
        }

        private static string GenerateAssetId(string relativePath)
        {
            return Path.GetFileNameWithoutExtension(relativePath)
                .Replace(' ', '_')
                .Replace('-', '_')
                .ToLowerInvariant();
        }

        private static void WriteCsv(string path, List<AssetEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CSV_HEADER);

            foreach (var e in entries)
            {
                sb.Append(EscapeCsv(e.assetId)).Append(',');
                sb.Append(EscapeCsv(e.sourcePath)).Append(',');
                sb.Append(EscapeCsv(e.targetPath)).Append(',');
                sb.Append(EscapeCsv(e.assetType)).Append(',');
                sb.Append(EscapeCsv(e.ppu)).Append(',');
                sb.Append(EscapeCsv(e.pivot)).Append(',');
                sb.Append(EscapeCsv(e.filterMode)).Append(',');
                sb.Append(EscapeCsv(e.compression)).Append(',');
                sb.Append(EscapeCsv(e.atlasGroup)).Append(',');
                sb.Append(EscapeCsv(e.ownerSystem)).Append(',');
                sb.AppendLine(EscapeCsv(e.migrationStatus));
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private struct AssetEntry
        {
            public string assetId;
            public string sourcePath;
            public string targetPath;
            public string assetType;
            public string ppu;
            public string pivot;
            public string filterMode;
            public string compression;
            public string atlasGroup;
            public string ownerSystem;
            public string migrationStatus;
        }
    }
}
