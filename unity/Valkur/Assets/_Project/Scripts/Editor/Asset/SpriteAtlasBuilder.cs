using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Valkur.Editor
{
    /// <summary>
    /// Creates SpriteAtlas assets per domain group to reduce draw calls.
    /// Groups: env-tiles, characters, npc, spells, items, vfx, buildings, ui, misc.
    /// Menu: Valkur > Assets > Build Sprite Atlases
    /// 
    /// Policy:
    ///   - Max atlas size: 2048×2048 (pixel art stays crisp)
    ///   - Padding: 2 (prevents bleed)
    ///   - FilterMode: Point (no filtering for pixel art), Bilinear for UI
    ///   - No rotation/tight packing (pixel art)
    /// </summary>
    public static class SpriteAtlasBuilder
    {
        private static readonly AtlasGroupDef[] AtlasGroups = new[]
        {
            new AtlasGroupDef("env-tiles",   "Assets/_Project/Resources/Tiles",    2048, false),
            new AtlasGroupDef("characters",  "Assets/_Project/Art/Characters",      2048, false),
            new AtlasGroupDef("npc",         "Assets/_Project/Art/NPC",             2048, false),
            new AtlasGroupDef("spells",      "Assets/_Project/Art/Spells",          2048, false),
            new AtlasGroupDef("items",       "Assets/_Project/Art/Items",           2048, false),
            new AtlasGroupDef("vfx",         "Assets/_Project/Art/VFX",             2048, false),
            new AtlasGroupDef("buildings",   "Assets/_Project/Resources/Buildings", 4096, false),
            new AtlasGroupDef("ui",          "Assets/_Project/Art/UI",              2048, true),
            new AtlasGroupDef("misc",        "Assets/_Project/Art/Misc",            2048, false),
            new AtlasGroupDef("backgrounds", "Assets/_Project/Art/Backgrounds",     4096, true),
        };

        private const string ATLAS_OUTPUT_DIR = "Assets/_Project/SpriteAtlases";

        [MenuItem("Valkur/Assets/Build Sprite Atlases")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(ATLAS_OUTPUT_DIR))
            {
                string parent = Path.GetDirectoryName(ATLAS_OUTPUT_DIR).Replace('\\', '/');
                string folder = Path.GetFileName(ATLAS_OUTPUT_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }

            int created = 0, updated = 0;

            foreach (var group in AtlasGroups)
            {
                string atlasPath = $"{ATLAS_OUTPUT_DIR}/{group.name}.spriteatlas";
                bool exists = File.Exists(Path.Combine(Application.dataPath, "..",
                    atlasPath.Replace('/', Path.DirectorySeparatorChar)));

                if (!AssetDatabase.IsValidFolder(group.sourceFolder))
                {
                    Debug.LogWarning($"[SpriteAtlasBuilder] Source folder not found: {group.sourceFolder} — skipping {group.name}");
                    continue;
                }

                var atlas = exists
                    ? AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath)
                    : new SpriteAtlas();

                if (atlas == null)
                    atlas = new SpriteAtlas();

                // Configure packing settings
                var packSettings = new SpriteAtlasPackingSettings
                {
                    blockOffset = 1,
                    padding = 2,
                    enableRotation = false,
                    enableTightPacking = false,
                    enableAlphaDilation = true,
                };
                atlas.SetPackingSettings(packSettings);

                // Configure texture settings
                var texSettings = new SpriteAtlasTextureSettings
                {
                    readable = false,
                    generateMipMaps = false,
                    sRGB = true,
                    filterMode = group.bilinear ? FilterMode.Bilinear : FilterMode.Point,
                };
                atlas.SetTextureSettings(texSettings);

                // Platform settings
                var platformSettings = atlas.GetPlatformSettings("DefaultTexturePlatform");
                platformSettings.overridden = true;
                platformSettings.maxTextureSize = group.maxSize;
                platformSettings.format = UnityEditor.TextureImporterFormat.RGBA32;
                platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
                atlas.SetPlatformSettings(platformSettings);

                // Set the source folder as the packable
                var folderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(group.sourceFolder);
                if (folderObj != null)
                {
                    atlas.Remove(atlas.GetPackables());
                    atlas.Add(new Object[] { folderObj });
                }

                if (!exists)
                {
                    AssetDatabase.CreateAsset(atlas, atlasPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(atlas);
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SpriteAtlasBuilder] {created} atlases created, {updated} updated.");
        }

        private struct AtlasGroupDef
        {
            public string name;
            public string sourceFolder;
            public int maxSize;
            public bool bilinear;

            public AtlasGroupDef(string name, string sourceFolder, int maxSize, bool bilinear)
            {
                this.name = name;
                this.sourceFolder = sourceFolder;
                this.maxSize = maxSize;
                this.bilinear = bilinear;
            }
        }
    }
}
