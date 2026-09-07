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
            // 4096 rather than 2048 because of what this group actually holds. Four of the
            // six character folders are claimed by players.spriteatlas, so the nested-overlap
            // branch below hands this group the REMAINDER — today the vampire and the mague,
            // the two characters baked at their own pixel budget (256 px at PPU 96, not the
            // shared 115 at 64) to keep the detail their 331-681 px source cells carry. That
            // pair is 630 frames and ~44.9 Mpx, where a 2048 page holds 4.19 and a 4096 page
            // 16.78, so it spans three pages. Unity picks the smallest power of two that
            // fits, so this is a ceiling and not an allocation: the group would drop back on
            // its own if either character left it.
            //
            // The split is not cosmetic. Those two are 2.667 world units against the
            // roster's 1.797, and at the shipped camera (ortho 5, 960 px viewport = 96 screen
            // px per world unit) PPU 96 is exactly one texel per screen pixel while PPU 64 is
            // a 1.5x upscale. Packing them beside the shared-budget four would take
            // players.spriteatlas from one page to three and put a 256 px character in the
            // same draw as a 115 px one.
            new AtlasGroupDef("characters",  4096, false, "Assets/_Project/Art/Characters"),
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

                    // The two checks above compare PATHS FOR EQUALITY, and the overlap
                    // that actually shipped is a NESTED one: 'characters' packed
                    // Art/Characters while players.spriteatlas packed five of its
                    // subfolders, so 780 player sprites lived in two atlases at once and
                    // every one of them logged "matches more than one built-in atlases".
                    // Neither exact-match test can see that, and it survived for as long
                    // as the two builders existed.
                    //
                    // A nested overlap is not the same problem as an identical one and
                    // must not get the same answer. Two atlases claiming the SAME folder
                    // is a contradiction only a human can settle; a folder whose CHILD
                    // somebody else owns is a division of labour, so this group takes
                    // what is left rather than dropping the lot — aborting would leave
                    // the non-player characters (vampire, 180 sprites) in no atlas at
                    // all, silently.
                    ExpandAroundForeignAtlases(sourceFolder, atlasPath, validFolders);
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
                // Entries may be folders OR individual sprites: a group narrowed around
                // another atlas keeps the loose art at a level whose siblings it gave up,
                // and DefaultAsset only loads the folders.
                var folderObjs = new List<Object>(validFolders.Count);
                foreach (var sourceFolder in validFolders)
                {
                    Object packable = AssetDatabase.IsValidFolder(sourceFolder)
                        ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(sourceFolder)
                        : AssetDatabase.LoadAssetAtPath<Sprite>(sourceFolder) as Object;
                    if (packable != null)
                        folderObjs.Add(packable);
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
        /// Adds <paramref name="folderPath"/> to <paramref name="results"/> when no other
        /// atlas packs anything inside it, and otherwise adds the parts of it that nobody
        /// else owns: the sprite assets sitting directly in it, plus each subfolder that
        /// is not itself claimed, resolved the same way.
        ///
        /// This is what keeps "one owner per sprite" true under NESTING. The equality
        /// checks in the caller only see an atlas naming the very same folder; the
        /// overlap this project shipped was a parent/child pair, which they cannot
        /// detect at all.
        /// </summary>
        private static void ExpandAroundForeignAtlases(string folderPath, string selfPath,
                                                       List<string> results)
        {
            if (!AnyForeignAtlasPacksInside(folderPath, selfPath))
            {
                results.Add(folderPath);
                return;
            }

            // Loose sprites at this level belong to nobody else — take them individually
            // so narrowing a folder never silently drops the art sitting directly in it.
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folderPath }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parent == folderPath && !results.Contains(assetPath))
                    results.Add(assetPath);
            }

            foreach (string sub in AssetDatabase.GetSubFolders(folderPath))
            {
                string owner = FindForeignAtlasPacking(sub, selfPath);
                if (owner != null)
                {
                    Debug.Log(
                        $"[SpriteAtlasBuilder] '{sub}' is packed by '{owner}' — left out of " +
                        "this group so the sprites under it have exactly one atlas.");
                    continue;
                }
                ExpandAroundForeignAtlases(sub, selfPath, results);
            }
        }

        /// <summary>
        /// True when some other atlas packs a folder at or below
        /// <paramref name="folderPath"/>. Answers "is this folder partially spoken for",
        /// which is the question the equality checks cannot ask.
        /// </summary>
        private static bool AnyForeignAtlasPacksInside(string folderPath, string selfPath)
        {
            string prefix = folderPath + "/";
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == selfPath) continue;
                var other = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (other == null) continue;
                foreach (var packable in other.GetPackables())
                {
                    if (packable == null) continue;
                    string packed = AssetDatabase.GetAssetPath(packable);
                    if (!string.IsNullOrEmpty(packed) && packed.StartsWith(prefix,
                            System.StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
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
