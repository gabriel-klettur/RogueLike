// The AUTO brush, exercised through the REAL production method against the REAL shipped
// catalog — the two things its existing coverage does neither of.
//
// AutoBrushTerrainResolutionTests builds synthetic rulesets and re-derives the resolution in
// a local `ResolveAutoBrushTerrainLike` helper. That shape compares one half of the project
// against a copy of itself, and it let a live defect through end to end: splitting the
// grass_rock pack into one picker category per sheet left the production lookup matching
// `TilesetRuleset.FolderName` against a category name that no longer existed, so the AUTO
// brush went dead on seven tabs at once, in silence, with that fixture still green.
//
// So this file asks the other question. It calls the private method the toggle and every
// stroke actually call, over Resources/TerrainCatalog.asset and the picker's own catalog, and
// it pins the behaviour the feature exists for: painting two areas of one pack must produce
// the boundary art between them.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.AutoTile
{
    [TestFixture]
    public class AutoBrushShippedPackTests
    {
        private const BindingFlags Instance =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        private readonly List<Object> _created = new List<Object>();

        private TerrainCatalog _catalog;
        private TileCatalog _tiles;
        private TileEditorManager _manager;
        private MethodInfo _resolveAutoBrushTerrain;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            Assert.IsNotNull(_catalog, "Resources/TerrainCatalog.asset must ship.");
            _tiles = TileCatalog.BuildFromResources();

            // Edit Mode never runs Awake on a component added from script, so the manager can
            // be hosted for a pure lookup without building a single widget.
            var host = new GameObject(nameof(AutoBrushShippedPackTests));
            _created.Add(host);
            _manager = host.AddComponent<TileEditorManager>();

            _resolveAutoBrushTerrain = typeof(TileEditorManager)
                .GetMethod("ResolveAutoBrushTerrain", Instance);
            Assert.IsNotNull(_resolveAutoBrushTerrain,
                "TileEditorManager.ResolveAutoBrushTerrain is what the AUTO toggle and every " +
                "AUTO stroke ask. A fixture that re-implements it instead is grading its own copy.");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_tiles != null)
                foreach (var entry in _tiles.Entries)
                    if (entry.tile != null) Object.DestroyImmediate(entry.tile);

            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>Invokes the production resolver with <paramref name="sprite"/> selected.</summary>
        private (TilesetRuleset Ruleset, string Terrain, string Reason) Resolve(Sprite sprite)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            _created.Add(tile);
            tile.sprite = sprite;

            _manager.State.SelectedTile = sprite == null ? null : tile;

            object boxed = _resolveAutoBrushTerrain.Invoke(_manager, null);
            var type = boxed.GetType();
            return ((TilesetRuleset)type.GetField("Item1").GetValue(boxed),
                    (string)type.GetField("Item2").GetValue(boxed),
                    (string)type.GetField("Item3").GetValue(boxed));
        }

        private Tilemap NewTilemap()
        {
            var gridGo = new GameObject("Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(gridGo.transform);
            _created.Add(tilemapGo);
            return tilemapGo.AddComponent<Tilemap>();
        }

        /// <summary>Every Corner16 pack in the shipped catalog that actually has slots.</summary>
        private static IEnumerable<TilesetRuleset> PopulatedCornerPacks(TerrainCatalog catalog) =>
            catalog.Rulesets.Where(r =>
                r != null && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16);

        private static Sprite FirstSpriteInSlot(TilesetRuleset pack, byte mask) =>
            pack.CornerSlots
                .Where(s => (byte)s.slot == mask && s.variants != null)
                .SelectMany(s => s.variants)
                .FirstOrDefault(v => v != null);

        // ── The lookup, over shipped data ────────────────────────────────────────

        [Test]
        public void NoTileSelected_IsRefusedWithTheHint()
        {
            var (ruleset, terrain, reason) = Resolve(null);
            Assert.IsNull(ruleset);
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoTileSelectedHint, reason);
        }

        [Test]
        public void ATileFromNoPack_IsRefusedWithTheHint()
        {
            var loose = _tiles.GetTilesForCategory("castle_pandora").FirstOrDefault(t => t.preview != null);
            Assert.IsNotNull(loose, "Fixture assumes castle_pandora ships sprites.");

            var (ruleset, terrain, reason) = Resolve(loose.preview);
            Assert.IsNull(ruleset, "castle_pandora is not an auto-tile pack.");
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoRulesetForCategoryHint, reason);
        }

        [Test]
        public void EveryPopulatedPack_ResolvesFromItsOwnPureTiles()
        {
            // The regression this exists for: the lookup used to match a folder NAME, so a pack
            // whose sheets became separate picker categories stopped resolving from any of them.
            // Reading the pack off the SPRITE cannot drift that way.
            var checkedAny = false;

            foreach (var pack in PopulatedCornerPacks(_catalog))
            {
                var primaryTile = FirstSpriteInSlot(pack, AutoBrushPackLookup.PurePrimaryMask);
                var secondaryTile = FirstSpriteInSlot(pack, AutoBrushPackLookup.PureSecondaryMask);
                Assert.IsNotNull(primaryTile, $"[{pack.FolderName}] has no sprite in its solid-primary slot.");
                Assert.IsNotNull(secondaryTile, $"[{pack.FolderName}] has no sprite in its solid-secondary slot.");

                var fromPrimary = Resolve(primaryTile);
                Assert.AreSame(pack, fromPrimary.Ruleset, $"[{pack.FolderName}] primary tile resolved elsewhere.");
                Assert.AreEqual(pack.TerrainPrimary, fromPrimary.Terrain,
                    $"[{pack.FolderName}] the solid-primary tile must paint the PRIMARY terrain.");

                var fromSecondary = Resolve(secondaryTile);
                Assert.AreSame(pack, fromSecondary.Ruleset, $"[{pack.FolderName}] secondary tile resolved elsewhere.");
                Assert.AreEqual(pack.TerrainSecondary, fromSecondary.Terrain,
                    $"[{pack.FolderName}] the solid-secondary tile must paint the SECONDARY terrain. " +
                    "Without this an author can only ever lay down one of the pack's two terrains, " +
                    "and a boundary needs both sides.");

                checkedAny = true;
            }

            Assert.IsTrue(checkedAny, "No populated Corner16 pack found — the fixture is vacuous.");
        }

        [Test]
        public void APackWhoseTerrainNameAnotherPackOwns_IsStillReachable()
        {
            // TerrainCatalog.FindPaintRuleset resolves a terrain NAME to exactly ONE ruleset, so
            // a second pack claiming the same primary is unreachable through it — measured on the
            // shipped data, 'grass' goes to grass_dirt and 'sand' to sand_grass. An author who
            // clicked a tile has already said which pack they mean, so the brush must not ask
            // the name.
            var shadowed = PopulatedCornerPacks(_catalog)
                .Where(p => _catalog.FindPaintRuleset(p.TerrainPrimary) != p)
                .ToList();

            if (shadowed.Count == 0)
                Assert.Ignore("No shipped pack is currently shadowed on its primary terrain.");

            foreach (var pack in shadowed)
            {
                var tile = FirstSpriteInSlot(pack, AutoBrushPackLookup.PurePrimaryMask);
                Assert.AreSame(pack, Resolve(tile).Ruleset,
                    $"[{pack.FolderName}] is shadowed by " +
                    $"'{_catalog.FindPaintRuleset(pack.TerrainPrimary)?.FolderName}' on the terrain " +
                    "name, so a name-based lookup would silently paint the wrong pack.");
            }
        }

        // ── The thing the feature exists for ─────────────────────────────────────

        [Test]
        public void TwoAreasOfOnePack_MeetingAlongAStraightBorder_ProduceTransitionTiles()
        {
            foreach (var pack in PopulatedCornerPacks(_catalog))
            {
                var tilemap = NewTilemap();
                var map = new TerrainMap();

                // A field of the primary, then the secondary over its right half.
                TerrainPainter.PaintRegion(tilemap, new BoundsInt(0, 0, 0, 10, 6, 1),
                    pack.TerrainPrimary, _catalog, map, null, pack);
                TerrainPainter.PaintRegion(tilemap, new BoundsInt(5, 0, 0, 5, 6, 1),
                    pack.TerrainSecondary, _catalog, map, null, pack);

                var signatures = new HashSet<byte>();
                for (int y = 0; y < 6; y++)
                for (int x = 0; x < 10; x++)
                {
                    var mask = BitmaskCalculator.CornerMask(map.Cells, new Vector2Int(x, y), pack.TerrainSecondary);
                    signatures.Add(mask);
                }

                int boundary = signatures.Count(m => m != AutoBrushPackLookup.PurePrimaryMask &&
                                                     m != AutoBrushPackLookup.PureSecondaryMask);
                Assert.Greater(boundary, 0,
                    $"[{pack.FolderName}] a straight border produced only the two PURE signatures, " +
                    "so the two areas meet as a hard cut with no transition art. That is the exact " +
                    "failure the corner model exists to prevent, and it is what a cell-keyed " +
                    "majority vote produced for the life of this feature.");
            }
        }

        [Test]
        public void OneAutoBrushClick_LaysDownAFullyBoundedIsland()
        {
            // A 2x2 click sets the 3x3 vertices around it, which fully determines the clicked
            // cells AND gives every one of the eight neighbours a correct boundary tile. With a
            // 1x1 footprint three of the four cells meeting at each painted corner stay unset,
            // which is why the toggle forces the size.
            var pack = PopulatedCornerPacks(_catalog).FirstOrDefault(p => p.FolderName == "water_water_deep")
                       ?? PopulatedCornerPacks(_catalog).First();

            var tilemap = NewTilemap();
            var map = new TerrainMap();
            TerrainPainter.PaintRegion(tilemap, new BoundsInt(0, 0, 0, 12, 12, 1),
                pack.TerrainPrimary, _catalog, map, null, pack);

            int size = TileEditorConstants.AutoBrushSize;
            TerrainPainter.PaintRegion(tilemap, new BoundsInt(5, 5, 0, size, size, 1),
                pack.TerrainSecondary, _catalog, map, null, pack);

            var byMask = new Dictionary<byte, int>();
            for (int y = 4; y <= 7; y++)
            for (int x = 4; x <= 7; x++)
            {
                var mask = BitmaskCalculator.CornerMask(map.Cells, new Vector2Int(x, y), pack.TerrainSecondary);
                byMask.TryGetValue(mask, out int n);
                byMask[mask] = n + 1;
            }

            Assert.AreEqual(size * size, byMask.TryGetValue(AutoBrushPackLookup.PureSecondaryMask, out int core) ? core : 0,
                $"The {size}x{size} footprint itself must be solid secondary.");
            Assert.AreEqual(4, byMask.Count(kv => kv.Key != AutoBrushPackLookup.PureSecondaryMask &&
                                                  kv.Key != AutoBrushPackLookup.PurePrimaryMask &&
                                                  CountBits(kv.Key) == 1),
                "The four diagonal neighbours must each show a single-corner tile.");
            Assert.AreEqual(4, byMask.Count(kv => CountBits(kv.Key) == 2),
                "The four edge neighbours must each show a half-and-half tile.");
        }

        private static int CountBits(byte b)
        {
            int n = 0;
            for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) n++;
            return n;
        }

        // ── One stroke, one sheet ────────────────────────────────────────────────

        [Test]
        public void AStroke_UsesOnlyTheSheetTheAuthorPickedFrom()
        {
            // A pack merged from several sheets holds eight or nine variants per slot, and the
            // solver picks one per cell by hash — so before the sheet filter, a single stroke on
            // grass_rock scattered SEVEN visually different arts cell by cell, grass from one
            // sheet against grass from another. Every number in the pack was right and only the
            // screen disagreed. The author already chose by clicking a tile.
            foreach (var category in _tiles.GetCategories())
            {
                var picked = FirstPureTileOfCategory(category, out var pack);
                if (picked == null) continue;

                // The PACK matters: without it the filter cannot know whether this sheet covers
                // every slot, and a partial one silently reopens the scatter it exists to close.
                var filter = AutoTileSheetFilter.ForSprite(_tiles, picked, pack);
                Assert.IsNotNull(filter, $"'{category}' holds an auto-tile tile but produced no filter.");

                var tilemap = NewTilemap();
                var map = new TerrainMap();
                TerrainPainter.PaintRegion(tilemap, new BoundsInt(0, 0, 0, 10, 6, 1),
                    pack.TerrainPrimary, _catalog, map, null, pack, filter);
                TerrainPainter.PaintRegion(tilemap, new BoundsInt(5, 0, 0, 5, 6, 1),
                    pack.TerrainSecondary, _catalog, map, null, pack, filter);

                var placed = new HashSet<string>();
                int cells = 0;
                for (int y = 0; y < 6; y++)
                for (int x = 0; x < 10; x++)
                {
                    var tile = tilemap.GetTile(new Vector3Int(x, y, 0)) as Tile;
                    if (tile == null || tile.sprite == null) continue;
                    cells++;
                    placed.Add(CategoryOf(tile.sprite.name));
                }

                Assert.AreEqual(60, cells, $"'{category}': the stroke must fill every cell it covers.");
                Assert.AreEqual(1, placed.Count,
                    $"'{category}': the stroke drew from {placed.Count} sheets ({string.Join(", ", placed)}). " +
                    "Picking a tile is the author saying which art they want; scattering the pack's " +
                    "other sheets across the same stroke reads as noise, not as variety.");

                var complete = AutoTileSheetFilter.CompleteSheetsOf(_tiles, pack);
                if (complete.Contains(category))
                    Assert.AreEqual(category, placed.First(),
                        $"'{category}' covers every slot, so the stroke must use it and nothing else.");
                else
                    Assert.Contains(placed.First(), complete,
                        $"'{category}' covers only part of the pack, so it cannot drive a stroke on its " +
                        "own — the substitute must be a sheet that covers all of it, or the cells it " +
                        "cannot fill fall back to the whole pack and the scatter returns.");
            }
        }

        [Test]
        public void ASheetWithNoTileForASlot_FallsBackToThePack_RatherThanLeavingAHole()
        {
            // A hole in the middle of a stroke reads as a broken editor; a neighbouring sheet's
            // tile reads as a variant. So the filter is a preference, never a veto.
            var pack = PopulatedCornerPacks(_catalog).First();
            var empty = AutoTileSheetFilter.ForNames("nothing_matches", new[] { "no_such_sprite" });
            Assert.IsNotNull(empty);

            for (byte mask = 0; mask < 16; mask++)
                Assert.IsNotNull(RulesetSolver.ResolveCorner(pack, mask, 0, empty),
                    $"[{pack.FolderName}] slot {mask} resolved to nothing under a filter that " +
                    "admits none of its variants.");
        }

        /// <summary>The solid-primary tile of <paramref name="category"/>, plus the pack it belongs
        /// to — or null when that category is not part of an auto-tile pack.</summary>
        private Sprite FirstPureTileOfCategory(string category, out TilesetRuleset pack)
        {
            foreach (var entry in _tiles.GetTilesForCategory(category))
            {
                if (entry.preview == null) continue;
                var found = AutoBrushPackLookup.FindPackAndSlot(_catalog, entry.preview);
                if (found.Ruleset == null || found.Ruleset.CornerSlots.Count != 16) continue;
                if (found.SlotMask != AutoBrushPackLookup.PurePrimaryMask) continue;
                pack = found.Ruleset;
                return entry.preview;
            }
            pack = null;
            return null;
        }

        private string CategoryOf(string spriteName)
        {
            foreach (var entry in _tiles.Entries)
                if (entry.tileName == spriteName) return entry.category;
            return "(unknown)";
        }

        // ── The footprint the toggle forces ──────────────────────────────────────

        [Test]
        public void EachModeKeepsItsOwnBrushSize()
        {
            // The manual brush and the AUTO brush want different footprints and neither should
            // have to give way. One shared number meant using AUTO once overwrote the manual
            // size — in memory, and then on disk through the workspace — after which the manual
            // brush was stuck at 2 and looked like it had stopped responding to its own control.
            var state = _manager.State;
            var toggle = typeof(TileEditorManager).GetMethod("OnAutoBrushToggleClicked", Instance);
            var setSize = typeof(TileEditorManager).GetMethod("OnBrushSizeChanged", Instance);
            Assert.IsNotNull(toggle);
            Assert.IsNotNull(setSize);

            state.CurrentTool = TileEditorState.Tool.Brush;
            state.AutoBrushMode = false;

            setSize.Invoke(_manager, new object[] { 5 });
            Assert.AreEqual(5, state.BrushSize);
            Assert.AreEqual(5, state.ActiveBrushSize, "the manual brush is the one being driven");

            toggle.Invoke(_manager, null);
            Assert.IsTrue(state.AutoBrushMode);
            Assert.AreEqual(TileEditorConstants.AutoBrushSize, state.ActiveBrushSize,
                "AUTO brings its OWN footprint, it does not resize the manual brush.");
            Assert.AreEqual(5, state.BrushSize, "the manual size is untouched while AUTO is lit");

            setSize.Invoke(_manager, new object[] { 4 });
            Assert.AreEqual(4, state.AutoBrushSize, "+/- moves whichever footprint is active");
            Assert.AreEqual(5, state.BrushSize, "and still not the other one");

            toggle.Invoke(_manager, null);
            Assert.AreEqual(5, state.ActiveBrushSize, "the manual brush comes back as the author left it");

            toggle.Invoke(_manager, null);
            Assert.AreEqual(4, state.ActiveBrushSize, "and so does AUTO's");
        }

        [Test]
        public void AutoBrushFootprint_BelongsToTheBrushToolOnly()
        {
            // AUTO is a modifier on the Brush tool. The eraser, the collider paint and the
            // layer-jump stamp all read BrushSize, so the checkbox being lit must not change
            // the footprint they get.
            var state = _manager.State;
            state.AutoBrushMode = true;
            state.BrushSize = 5;
            state.AutoBrushSize = 2;

            state.CurrentTool = TileEditorState.Tool.Brush;
            Assert.IsTrue(state.IsAutoBrushActive);
            Assert.AreEqual(2, state.ActiveBrushSize);

            state.CurrentTool = TileEditorState.Tool.Eraser;
            Assert.IsFalse(state.IsAutoBrushActive,
                "AUTO modifies the Brush tool and nothing else.");
            Assert.AreEqual(5, state.ActiveBrushSize);
        }

        [Test]
        public void AutoBrushSize_DefaultsToTwo()
        {
            Assert.AreEqual(2, TileEditorConstants.AutoBrushSize,
                "A corner is a vertex shared by four cells, so 2x2 is the smallest footprint " +
                "whose unit of action matches the model's unit of decision.");
            Assert.AreEqual(TileEditorConstants.AutoBrushSize, new TileEditorState().AutoBrushSize,
                "It is the DEFAULT for AUTO's own size, not a value forced over the author's.");
        }
    }
}
