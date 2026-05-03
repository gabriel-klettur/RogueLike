using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Valkur.Editor
{
    public static partial class AssetMapGenerator
    {
        private static AssetEntry ClassifyAsset(string relativePath, string filePath)
        {
            string lower = relativePath.ToLowerInvariant();
            string ext = Path.GetExtension(lower);

            var entry = new AssetEntry
            {
                sourcePath = relativePath
            };

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
