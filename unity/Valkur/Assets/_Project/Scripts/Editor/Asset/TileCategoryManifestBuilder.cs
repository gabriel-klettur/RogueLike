using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Editor
{
    /// <summary>
    /// Bakes Resources/Tiles/_categories.json — the editor-time discovery of
    /// tile categories consumed at runtime by TileCatalog.BuildFromResources()
    /// (the tile picker) and OverlayLoader (the world-paint sprite resolver).
    ///
    /// Resources/ ships flat in a build: there is no runtime directory listing
    /// API, which is why the category folder list used to be hand-maintained
    /// as two separate hardcoded arrays that silently diverged from disk and
    /// from each other. This generator is the one place allowed to call
    /// AssetDatabase to see the real folder tree, and it is the ONLY writer of
    /// _categories.json — same convention as the per-tilesheet _manifest.json
    /// files produced by tools/atlas/migrate_tilesheet.py.
    /// </summary>
    public static class TileCategoryManifestBuilder
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";
        private const string OUTPUT_PATH = TILES_ROOT + "/_categories.json";
        private const string SYNTHETIC_ROOT_CATEGORY = "basics";

        private static readonly HashSet<string> ExcludedFolderNames = new HashSet<string>
        {
            "_backups", "_raw", "_source",
        };

        [MenuItem("Valkur/Tiles/Regenerate Category Manifest")]
        private static void GenerateMenuItem() => Generate();

        /// <summary>
        /// Rescans Resources/Tiles/ and rewrites _categories.json. No-ops
        /// (does not touch disk) when the freshly computed content is
        /// byte-identical to what's already there, so calling this from
        /// OnPostprocessAllAssets on every Tiles/ import can't loop.
        /// </summary>
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(TILES_ROOT))
            {
                Debug.LogWarning($"[TileCategoryManifestBuilder] '{TILES_ROOT}' does not exist, skipping.");
                return;
            }

            var manifest = new TileCategoryManifest
            {
                schemaVersion = 1,
                folderCategories = DiscoverFolderCategories(),
                syntheticRootCategory = SYNTHETIC_ROOT_CATEGORY,
                rootFiles = DiscoverRootFiles(),
            };

            string json = JsonUtility.ToJson(manifest, true);
            WriteIfChanged(OUTPUT_PATH, json);
        }

        /// <summary>
        /// Immediate subfolders of Resources/Tiles/ containing >= 1 sprite
        /// anywhere below them, ordered by sprite count descending (largest
        /// category first — mirrors the "check the common case soonest"
        /// ordering already documented on OverlayLoader's folder probe).
        /// Ties keep AssetDatabase.GetSubFolders' alphabetical order via a
        /// stable sort.
        /// </summary>
        private static string[] DiscoverFolderCategories()
        {
            var counted = new List<(string name, int count)>();
            foreach (string sub in AssetDatabase.GetSubFolders(TILES_ROOT))
            {
                string folderName = Path.GetFileName(sub);
                if (ExcludedFolderNames.Contains(folderName)) continue;

                int spriteCount = AssetDatabase.FindAssets("t:Sprite", new[] { sub }).Length;
                if (spriteCount <= 0) continue;

                counted.Add((folderName, spriteCount));
            }

            // OrderByDescending is a stable sort in LINQ-to-Objects, so equal
            // counts preserve the alphabetical order GetSubFolders returned them in.
            return counted
                .OrderByDescending(f => f.count)
                .Select(f => f.name)
                .ToArray();
        }

        /// <summary>
        /// Sprites that sit directly under Resources/Tiles/ with no owning
        /// subfolder (e.g. floor, wall, dungeon_floor) — there is no folder to
        /// enumerate for these, so they are listed explicitly by file name.
        /// </summary>
        private static string[] DiscoverRootFiles()
        {
            var rootFiles = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { TILES_ROOT }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string parentDir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parentDir != TILES_ROOT) continue; // only direct children, not subfolders

                rootFiles.Add(Path.GetFileNameWithoutExtension(assetPath));
            }
            return rootFiles.ToArray();
        }

        private static void WriteIfChanged(string assetPath, string content)
        {
            string existing = File.Exists(assetPath) ? File.ReadAllText(assetPath) : null;
            if (existing == content) return;

            File.WriteAllText(assetPath, content);
            AssetDatabase.ImportAsset(assetPath);
        }
    }
}
