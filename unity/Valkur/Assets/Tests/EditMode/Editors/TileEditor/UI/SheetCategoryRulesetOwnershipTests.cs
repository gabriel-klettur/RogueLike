// One picker CATEGORY per tileset, one RULESET per pack — and the two are not the same split.
//
// A category is a folder under Resources/Tiles/, and it is what an author browses by, so a pack
// cut from several sheets shows one tab per sheet. The auto-brush works the other way round:
// TerrainCatalog.FindPaintRuleset resolves a terrain NAME to exactly ONE ruleset (highest
// Priority, ties by list order), so four rulesets all claiming 'grass' would leave three of them
// permanently unreachable, silently — the trap CLAUDE.md records for rock_lava. The four
// grass_rock sheets and the three legacy tilesets therefore share ONE ruleset.asset, which lives
// in the pack folder and not in any of theirs.
//
// That leaves the "CONFIGURE TILESET" button with nothing to open on seven of the tabs, because
// it used to resolve the ruleset from the category NAME (Resources.Load "Tiles/{cat}/ruleset").
// TileEditorUI.FindOwningRuleset answers from the DATA instead — the ruleset whose slots hold
// this category's sprites — so a sheet renamed or moved keeps its wizard, and a category that
// belongs to no pack still correctly gets nothing.
//
// What this fixture pins is the COMPOSITION: the folder split on disk, and that every split
// sheet still reaches its pack. Either half alone reads correct while the pair is broken, which
// is the shape SPAWNER_COORDINATE_SPACE_DRIFT records.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    [TestFixture]
    public class SheetCategoryRulesetOwnershipTests
    {
        private const BindingFlags Instance =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        /// <summary>The sheets that were split out of the grass_rock pack folder, one tab each.</summary>
        private static readonly string[] SplitSheetCategories =
        {
            "grass_rock_1", "grass_rock_2", "grass_rock_3", "grass_rock_4",
            "tileset4", "tileset5", "tileset6",
        };

        /// <summary>
        /// The 2026-09-06 wave: five more grass/dirt island renders, cut by
        /// <c>tools/atlas/wave9/grass_dirt_sheets.py</c>. Same split as the grass_rock sheets and
        /// for the same two reasons — one CATEGORY each so the picker offers five distinct arts
        /// and a stroke draws from one coherent sheet, one RULESET between them because five
        /// packs claiming 'grass' would leave four permanently unreachable.
        /// </summary>
        private static readonly string[] GrassDirtSheetCategories =
        {
            "grass_dirt2", "grass_dirt3", "grass_dirt4", "grass_dirt5", "grass_dirt6",
        };

        private TileCatalog _catalog;
        private GameObject _host;
        private TileEditorUI _ui;
        private MethodInfo _loadRulesetForCategory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = TileCatalog.BuildFromResources();

            // Edit Mode never runs Awake on a component added from script, so the UI can be
            // hosted for a pure lookup without building a single widget.
            _host = new GameObject(nameof(SheetCategoryRulesetOwnershipTests));
            _ui = _host.AddComponent<TileEditorUI>();

            var catalogField = typeof(TileEditorUI).GetField("_catalog", Instance);
            Assert.IsNotNull(catalogField, "TileEditorUI._catalog is the field the lookup reads.");
            catalogField.SetValue(_ui, _catalog);

            _loadRulesetForCategory = typeof(TileEditorUI).GetMethod("LoadRulesetForCategory", Instance);
            Assert.IsNotNull(_loadRulesetForCategory,
                "TileEditorUI.LoadRulesetForCategory is what the CONFIGURE button asks.");
            Assert.IsFalse(_loadRulesetForCategory.IsStatic,
                "The lookup consults the live picker catalog, so it cannot be static.");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_catalog == null) return;
            foreach (var entry in _catalog.Entries)
                if (entry.tile != null) Object.DestroyImmediate(entry.tile);
        }

        private TilesetRuleset Resolve(string category) =>
            (TilesetRuleset)_loadRulesetForCategory.Invoke(_ui, new object[] { category });

        // ── The folder split ─────────────────────────────────────────────────────

        [Test]
        public void EverySplitSheet_IsItsOwnPickerCategory()
        {
            var categories = _catalog.GetCategories();
            foreach (var sheet in SplitSheetCategories)
                Assert.Contains(sheet, categories,
                    $"'{sheet}' must be its own tab. Folding the sheets back under one grass_rock " +
                    "category is what put 354 tiles from seven sheets behind a single tab.");
        }

        [Test]
        public void ASplitSheet_HoldsOnlyItsOwnSprites()
        {
            foreach (var sheet in SplitSheetCategories)
            {
                var tiles = _catalog.GetTilesForCategory(sheet);
                Assert.IsNotEmpty(tiles, $"'{sheet}' resolved to no tiles.");
                Assert.IsTrue(tiles.All(t => t.tileName.StartsWith(sheet)),
                    $"'{sheet}' holds sprites named for another sheet — Resources.LoadAll is " +
                    "RECURSIVE, so a sheet folder nested inside another pulls both into one tab.");
            }
        }

        [Test]
        public void TheFourNewSheets_KeepTheirGridMetadata()
        {
            // Without a per-sheet _manifest.json the picker falls back to the legacy flat list
            // and loses the grid view, the (r, c) coordinates and the dedup toggle.
            foreach (var sheet in new[] { "grass_rock_1", "grass_rock_2", "grass_rock_3", "grass_rock_4" })
            {
                var tiles = _catalog.GetTilesForCategory(sheet);
                Assert.AreEqual(64, tiles.Count, $"'{sheet}' is an 8x8 sheet.");
                Assert.IsTrue(tiles.All(t => t.gridR >= 0 && t.gridC >= 0),
                    $"'{sheet}' has cells with no grid coordinates — its _manifest.json is missing " +
                    "or does not name every file.");
                Assert.AreEqual(16, tiles.Select(t => t.uniqueId).Distinct().Count(),
                    $"'{sheet}' must declare exactly 16 unique tiles: it is a Corner16 island render.");
            }
        }

        // ── The ruleset each category answers to ─────────────────────────────────

        [Test]
        public void EverySplitSheet_StillReachesItsPack()
        {
            var pack = Resolve("grass_rock");
            Assert.IsNotNull(pack, "The grass_rock pack folder still owns the ruleset asset.");

            foreach (var sheet in SplitSheetCategories)
            {
                Assert.IsNull(Resources.Load<TilesetRuleset>($"Tiles/{sheet}/ruleset"),
                    $"'{sheet}' must NOT get a ruleset of its own: a second pack claiming " +
                    $"'{pack.TerrainPrimary}' is unreachable from the auto-brush, silently.");
                Assert.AreSame(pack, Resolve(sheet),
                    $"'{sheet}' lost its CONFIGURE wizard — the lookup fell back to the category " +
                    "NAME instead of asking which ruleset holds these sprites.");
            }
        }

        [Test]
        public void ACategoryWithItsOwnAsset_ResolvesToThatAsset()
        {
            foreach (var category in new[] { "grass_rock", "water_water_deep", "rock_lava" })
            {
                var own = Resources.Load<TilesetRuleset>($"Tiles/{category}/ruleset");
                Assert.IsNotNull(own, $"Fixture assumes '{category}' ships a ruleset.asset.");
                Assert.AreSame(own, Resolve(category),
                    "A category that has its own asset must never be answered by the membership scan.");
            }
        }

        [Test]
        public void ACategoryOutsideEveryPack_ResolvesToNothing()
        {
            // The button reads "NO RULESET FOR CATEGORY" here, which is the honest answer.
            foreach (var category in new[] { "castle_pandora", "basics", "", null })
                Assert.IsNull(Resolve(category),
                    $"'{category ?? "(null)"}' belongs to no auto-tile pack, so it has no wizard. " +
                    "A membership scan that answers here is matching on something other than sprites.");
        }

        // ── The grass_dirt wave, which arrived after all of the above ────────────

        [Test]
        public void EveryGrassDirtSheet_IsItsOwnPickerCategory()
        {
            var categories = _catalog.GetCategories();
            foreach (var sheet in GrassDirtSheetCategories)
                Assert.Contains(sheet, categories,
                    $"'{sheet}' must be its own tab: the five sheets are five visibly different " +
                    "arts, and merging them puts all of them behind one tab where a single stroke " +
                    "can scatter every one of them across neighbouring cells.");
        }

        [Test]
        public void EveryGrassDirtSheet_IsACompleteCorner16Island()
        {
            // A sheet missing even one of the sixteen signatures cannot draw every boundary, and
            // AutoTileSheetFilter would silently substitute another sheet for the whole stroke.
            foreach (var sheet in GrassDirtSheetCategories)
            {
                var tiles = _catalog.GetTilesForCategory(sheet);
                Assert.AreEqual(64, tiles.Count, $"'{sheet}' is an 8x8 sheet.");
                Assert.IsTrue(tiles.All(t => t.gridR >= 0 && t.gridC >= 0),
                    $"'{sheet}' has cells with no grid coordinates — its _manifest.json is missing " +
                    "or does not name every file.");
                Assert.AreEqual(16, tiles.Select(t => t.uniqueId).Distinct().Count(),
                    $"'{sheet}' must declare exactly 16 unique tiles: it is a Corner16 island render.");
                Assert.IsTrue(tiles.All(t => t.tileName.StartsWith(sheet)),
                    $"'{sheet}' holds sprites named for another sheet — Resources.LoadAll is RECURSIVE.");
            }
        }

        [Test]
        public void EveryGrassDirtSheet_StillReachesTheOnePack()
        {
            var pack = Resolve("grass_dirt");
            Assert.IsNotNull(pack, "The grass_dirt pack folder owns the ruleset asset.");

            foreach (var sheet in GrassDirtSheetCategories)
            {
                Assert.IsNull(Resources.Load<TilesetRuleset>($"Tiles/{sheet}/ruleset"),
                    $"'{sheet}' must NOT get a ruleset of its own: FindPaintRuleset resolves " +
                    $"'{pack.TerrainPrimary}' to exactly one ruleset, so a second claimant is " +
                    "unreachable from the auto-brush, silently.");
                Assert.AreSame(pack, Resolve(sheet),
                    $"'{sheet}' lost its CONFIGURE wizard — the lookup fell back to the category " +
                    "NAME instead of asking which ruleset holds these sprites.");
            }
        }

        [Test]
        public void ThePackHoldsEverySheetsSprites_InEverySlot()
        {
            // The importer only finds a sprite outside the pack folder through the ruleset's
            // sheetFolders list. A sheet missing from it imports as a category with no slots
            // behind it: visible in the picker, inert under AUTO, and nothing reports it.
            var pack = Resolve("grass_dirt");
            Assert.IsNotNull(pack);

            var slotted = new HashSet<string>(pack.CornerSlots
                .SelectMany(s => s.variants ?? System.Array.Empty<Sprite>())
                .Where(v => v != null).Select(v => v.name));

            foreach (var sheet in GrassDirtSheetCategories)
            {
                int reached = _catalog.GetTilesForCategory(sheet).Count(t => slotted.Contains(t.tileName));
                Assert.AreEqual(16, reached,
                    $"'{sheet}' has {reached} of its 16 signature tiles in the pack's slots. " +
                    "Check the ruleset's sheetFolders — a sheet it does not name is imported as " +
                    "a picker tab the auto-brush can never draw from.");
            }

            foreach (var slot in pack.CornerSlots)
            {
                var names = (slot.variants ?? System.Array.Empty<Sprite>()).Where(v => v != null)
                    .Select(v => v.name).ToList();
                foreach (var sheet in GrassDirtSheetCategories)
                    Assert.IsTrue(names.Any(n => n.StartsWith(sheet)),
                        $"slot {slot.slot} has no variant from '{sheet}', so a stroke filtered to " +
                        "that sheet cannot draw this boundary and falls back to another sheet.");
            }
        }

        [Test]
        public void TheMembershipScan_MatchesOnSprites_NotOnTheCategoryName()
        {
            // 'tileset4' shares no name fragment with 'grass_rock', so a name-based fallback
            // cannot produce this answer and a green result here means the scan is real.
            var byMembership = Resolve("tileset4");
            Assert.IsNotNull(byMembership);
            StringAssert.AreNotEqualIgnoringCase("tileset4", byMembership.FolderName);

            var owned = new HashSet<string>(_catalog.GetTilesForCategory("tileset4").Select(t => t.tileName));
            bool anySlotHoldsThem = byMembership.CornerSlots
                .SelectMany(s => s.variants ?? System.Array.Empty<Sprite>())
                .Concat(byMembership.Slots.SelectMany(s => s.variants ?? System.Array.Empty<Sprite>()))
                .Any(sprite => sprite != null && owned.Contains(sprite.name));

            Assert.IsTrue(anySlotHoldsThem,
                "The returned ruleset does not actually hold a sprite from this category, so the " +
                "lookup answered for some other reason.");
        }
    }
}
