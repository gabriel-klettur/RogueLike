// Regression coverage for the Resources/Tiles/_categories.json GENERATOR itself
// (Scripts/Editor/Asset/TileCategoryManifestBuilder.cs), not just its consumers.
//
// TileCategoryDiskParityTests already proves TileCatalog and OverlayLoader read the SAME
// baked manifest and that ITS CONTENT matches disk — but nothing in the suite ever calls
// TileCategoryManifestBuilder.Generate() itself. Two properties that only the generator
// owns are asserted here:
//
//   1. Generate() PRODUCES a manifest matching a fresh, independent AssetDatabase scan —
//      folder categories as a set, root files as a set, and the folder-category ordering
//      invariant (sprite count non-increasing) TileCategoryManifestBuilder's own doc
//      comment promises.
//
//   2. Generate() is idempotent — calling it twice in a row when disk hasn't changed
//      writes byte-identical content and does NOT touch the file a second time
//      (WriteIfChanged's content-equality short circuit). This is what lets
//      ValkurAssetPostprocessor call Generate() on every Tiles/ import without looping
//      the AssetDatabase or dirtying git on every domain reload.
//
// Calling Generate() from a test is deliberate, not an accident: the tool is designed to
// be safely re-run at any time (that is the whole idempotency contract under test), and if
// the checked-in manifest has drifted from disk this test SHOULD regenerate it — that is
// the self-healing behaviour the postprocessor hook relies on, not a side effect to guard
// against.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Editor;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    [TestFixture]
    public class TileCategoryManifestGeneratorTests
    {
        private const string TILES_ROOT = "Assets/_Project/Resources/Tiles";
        private const string MANIFEST_ASSET_PATH = TILES_ROOT + "/_categories.json";

        private static readonly HashSet<string> ExcludedFolderNames = new HashSet<string>
        {
            "_backups", "_raw", "_source",
        };

        // ── Ground truth, independent of TileCategoryManifestBuilder's own code ──────

        private static Dictionary<string, int> DiscoverDiskFolderCategoryCounts()
        {
            var result = new Dictionary<string, int>();
            foreach (var sub in AssetDatabase.GetSubFolders(TILES_ROOT))
            {
                string name = Path.GetFileName(sub);
                if (ExcludedFolderNames.Contains(name)) continue;
                int count = AssetDatabase.FindAssets("t:Sprite", new[] { sub }).Length;
                if (count > 0) result[name] = count;
            }
            return result;
        }

        private static HashSet<string> DiscoverDiskRootFiles()
        {
            var result = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { TILES_ROOT }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string parentDir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (parentDir != TILES_ROOT) continue;
                result.Add(Path.GetFileNameWithoutExtension(assetPath));
            }
            return result;
        }

        private static TileCategoryManifest ReadManifestFromDisk()
        {
            string text = File.ReadAllText(MANIFEST_ASSET_PATH);
            var manifest = JsonUtility.FromJson<TileCategoryManifest>(text);
            Assert.IsNotNull(manifest, $"{MANIFEST_ASSET_PATH} failed to parse after Generate().");
            return manifest;
        }

        // ── Requirement: manifest content matches live disk ─────────────────────────

        [Test]
        public void Generate_FolderCategories_MatchLiveDiskEnumeration_AsSet()
        {
            TileCategoryManifestBuilder.Generate();
            var manifest = ReadManifestFromDisk();

            var diskCounts = DiscoverDiskFolderCategoryCounts();
            var manifestSet = new HashSet<string>(manifest.folderCategories ?? Array.Empty<string>());

            CollectionAssert.AreEquivalent(diskCounts.Keys, manifestSet,
                "Generate() must emit exactly the sprite-bearing Resources/Tiles/ subfolders that " +
                "exist on disk right now — no more, no fewer.");
        }

        [Test]
        public void Generate_FolderCategories_OrderedByDescendingSpriteCount()
        {
            TileCategoryManifestBuilder.Generate();
            var manifest = ReadManifestFromDisk();
            var diskCounts = DiscoverDiskFolderCategoryCounts();

            var categories = manifest.folderCategories ?? Array.Empty<string>();
            Assert.Greater(categories.Length, 0, "Sanity: manifest must list at least one category.");

            for (int i = 1; i < categories.Length; i++)
            {
                bool haveA = diskCounts.TryGetValue(categories[i - 1], out int prevCount);
                bool haveB = diskCounts.TryGetValue(categories[i], out int curCount);
                Assert.IsTrue(haveA && haveB,
                    $"Manifest lists '{categories[i - 1]}' / '{categories[i]}' but a live disk scan " +
                    "doesn't recognize one of them as a sprite-bearing folder — manifest has drifted " +
                    "from disk (see Generate_FolderCategories_MatchLiveDiskEnumeration_AsSet).");
                Assert.GreaterOrEqual(prevCount, curCount,
                    $"folderCategories must be ordered by descending sprite count — " +
                    $"'{categories[i - 1]}' ({prevCount}) precedes '{categories[i]}' ({curCount}).");
            }
        }

        [Test]
        public void Generate_RootFiles_MatchLiveDiskEnumeration_AsSet()
        {
            TileCategoryManifestBuilder.Generate();
            var manifest = ReadManifestFromDisk();

            var diskRootFiles = DiscoverDiskRootFiles();
            var manifestRootFiles = new HashSet<string>(manifest.rootFiles ?? Array.Empty<string>());

            CollectionAssert.AreEquivalent(diskRootFiles, manifestRootFiles,
                "Generate() must emit exactly the loose sprite files directly under Resources/Tiles/.");
        }

        [Test]
        public void Generate_SyntheticRootCategory_IsAlwaysBasics()
        {
            TileCategoryManifestBuilder.Generate();
            var manifest = ReadManifestFromDisk();

            Assert.AreEqual("basics", manifest.syntheticRootCategory,
                "The synthetic root category name is a public contract read by both TileCatalog " +
                "and OverlayLoader — WorldLoader also depends on the literal 'Tiles/wall' / " +
                "'Tiles/floor' resource paths staying selectable under it. Changing it silently " +
                "is a regression.");
        }

        [Test]
        public void Generate_CheckedInManifest_AlreadyMatchesLiveDiskScan()
        {
            // Captures the committed file BEFORE calling Generate() so a failure here means the
            // manifest checked into git has drifted from disk — someone edited Resources/Tiles/
            // without running Valkur > Tiles > Regenerate Category Manifest.
            string committedContent = File.ReadAllText(MANIFEST_ASSET_PATH);

            TileCategoryManifestBuilder.Generate();
            string freshContent = File.ReadAllText(MANIFEST_ASSET_PATH);

            Assert.AreEqual(committedContent, freshContent,
                $"{MANIFEST_ASSET_PATH} as checked into git does not match a fresh disk scan. Run " +
                "'Valkur > Tiles > Regenerate Category Manifest' and commit the result.");
        }

        // ── Requirement: idempotency ─────────────────────────────────────────────────

        [Test]
        public void Generate_CalledTwiceConsecutively_SecondCallIsANoOp()
        {
            TileCategoryManifestBuilder.Generate();
            string contentAfterFirst = File.ReadAllText(MANIFEST_ASSET_PATH);
            DateTime timeAfterFirst = File.GetLastWriteTimeUtc(MANIFEST_ASSET_PATH);

            TileCategoryManifestBuilder.Generate();
            string contentAfterSecond = File.ReadAllText(MANIFEST_ASSET_PATH);
            DateTime timeAfterSecond = File.GetLastWriteTimeUtc(MANIFEST_ASSET_PATH);

            Assert.AreEqual(contentAfterFirst, contentAfterSecond,
                "Regenerating with no disk changes must produce byte-identical content.");
            Assert.AreEqual(timeAfterFirst, timeAfterSecond,
                "Regenerating with no disk changes must NOT rewrite the file — WriteIfChanged's " +
                "content-equality short circuit is what lets an AssetPostprocessor call Generate() " +
                "on every Tiles/ import without looping the AssetDatabase.");
        }

        [Test]
        public void Generate_CalledManyTimesInARow_StaysStable()
        {
            // Guards against a subtler non-idempotency: e.g. an ordering tie-break that isn't
            // actually stable across repeated calls (would only show up after 2+ iterations).
            TileCategoryManifestBuilder.Generate();
            string baseline = File.ReadAllText(MANIFEST_ASSET_PATH);

            for (int i = 0; i < 5; i++)
            {
                TileCategoryManifestBuilder.Generate();
                Assert.AreEqual(baseline, File.ReadAllText(MANIFEST_ASSET_PATH),
                    $"Manifest content changed on repeated call #{i + 2} with no disk changes.");
            }
        }
    }
}
