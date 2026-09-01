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
            new AtlasGroupDef("env-tiles",   2048, false, "Assets/_Project/Resources/Tiles"),
            new AtlasGroupDef("characters",  2048, false, "Assets/_Project/Art/Characters"),
            new AtlasGroupDef("npc",         2048, false, "Assets/_Project/Art/NPC"),
            new AtlasGroupDef("spells",      2048, false, "Assets/_Project/Art/Spells"),
            new AtlasGroupDef("items",       2048, false, "Assets/_Project/Art/Items"),
            // VFX must NOT pack its whole Art/VFX tree: that folder contains
            // Vendor/SlashVFX/Demo, whose demo scene art (mannequin diffuse, EXR
            // reflection probe) would ship in the build. List the texture folders
            // explicitly — see SpriteAtlasPackablesTests.
            new AtlasGroupDef("vfx",         2048, false,
                "Assets/_Project/Art/VFX/explosions",
                "Assets/_Project/Art/VFX/flame",
                "Assets/_Project/Art/VFX/smoke",
                "Assets/_Project/Art/VFX/Vendor/SlashVFX/Textures"),
            new AtlasGroupDef("buildings",   4096, false, "Assets/_Project/Resources/Buildings"),
            new AtlasGroupDef("ui",          2048, true,  "Assets/_Project/Art/UI"),
            new AtlasGroupDef("misc",        2048, false, "Assets/_Project/Art/Misc"),
            new AtlasGroupDef("backgrounds", 4096, true,  "Assets/_Project/Art/Backgrounds"),
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

                // Validate every source folder up front and collect the valid ones.
                // A missing folder is skipped, but an in-folder atlas or a foreign
                // atlas claiming one of these folders still aborts the whole group —
                // those mean the project is double-packing and need a human decision.
                var validFolders = new List<string>();
                bool abortGroup = false;

                foreach (var sourceFolder in group.sourceFolders)
                {
                    if (!AssetDatabase.IsValidFolder(sourceFolder))
                    {
                        Debug.LogWarning($"[SpriteAtlasBuilder] Source folder not found: {sourceFolder} — skipping it for {group.name}");
                        continue;
                    }

                    // If a SpriteAtlas is already living inside the source folder
                    // (typically a hand-curated one like Atlas_Characters_Players),
                    // skip this group: producing a second atlas covering the same
                    // sprites makes Unity log a "matches more than one built-in
                    // atlases" warning per sprite per LoadAssetAtPath, which can
                    // cascade into editor freezes once the player spawn pipeline
                    // touches dozens of walking frames.
                    var existingInFolder = AssetDatabase.FindAssets("t:SpriteAtlas",
                        new[] { sourceFolder });
                    if (existingInFolder != null && existingInFolder.Length > 0)
                    {
                        string existingName = AssetDatabase.GUIDToAssetPath(existingInFolder[0]);
                        Debug.LogWarning(
                            $"[SpriteAtlasBuilder] '{sourceFolder}' already contains " +
                            $"a SpriteAtlas ({existingName}). Skipping '{group.name}' to avoid " +
                            "duplicate-packing conflicts. Delete the in-folder atlas if you want " +
                            "the convention-named one at SpriteAtlases/ to take over.");
                        abortGroup = true;
                        break;
                    }

                    // The check above only catches an atlas sitting INSIDE the source
                    // folder. A stray atlas anywhere else that lists the same folder as
                    // a packable is just as damaging and slipped through for months:
                    // Art/Tiles/Atlas_Tiles.spriteatlas packed Resources/Tiles, the same
                    // folder as this 'env-tiles' group, producing 3077 "matches more than
                    // one built-in atlases" warnings and a duplicated atlas in the build.
                    string foreignAtlas = FindForeignAtlasPacking(sourceFolder, atlasPath);
                    if (foreignAtlas != null)
                    {
                        Debug.LogError(
                            $"[SpriteAtlasBuilder] '{foreignAtlas}' already packs " +
                            $"'{sourceFolder}'. Skipping '{group.name}' — two atlases over " +
                            "the same sprites warn once per sprite and ship the atlas twice. " +
                            "Delete the stray atlas, then re-run this menu item.");
                        abortGroup = true;
                        break;
                    }

                    validFolders.Add(sourceFolder);
                }

                if (abortGroup || validFolders.Count == 0)
                {
                    if (!abortGroup)
                        Debug.LogWarning($"[SpriteAtlasBuilder] No valid source folders for '{group.name}' — skipping.");
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

                // Set every valid source folder as a packable.
                var folderObjs = new List<Object>(validFolders.Count);
                foreach (var sourceFolder in validFolders)
                {
                    var folderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(sourceFolder);
                    if (folderObj != null)
                        folderObjs.Add(folderObj);
                }

                atlas.Remove(atlas.GetPackables());
                if (folderObjs.Count > 0)
                    atlas.Add(folderObjs.ToArray());

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
            public string[] sourceFolders;
            public int maxSize;
            public bool bilinear;

            public AtlasGroupDef(string name, int maxSize, bool bilinear, params string[] sourceFolders)
            {
                this.name = name;
                this.maxSize = maxSize;
                this.bilinear = bilinear;
                this.sourceFolders = sourceFolders;
            }
        }
        /// <summary>
        /// Returns the path of any SpriteAtlas OTHER than <paramref name="selfPath"/>
        /// that lists <paramref name="folderPath"/> among its packables, or null when
        /// the folder is claimed by at most this group's own atlas.
        /// </summary>
        private static string FindForeignAtlasPacking(string folderPath, string selfPath)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == selfPath) continue;
                var other = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (other == null) continue;
                foreach (var packable in other.GetPackables())
                {
                    if (packable == null) continue;
                    if (AssetDatabase.GetAssetPath(packable) == folderPath) return path;
                }
            }
            return null;
        }

    }
}
