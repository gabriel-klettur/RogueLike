// Ctrl+Z after an AUTO stroke, where AUTO is ConnectRegion rather than PaintRegion.
//
// The sibling fixture AutoBrushStrokeUndoTests covers the STAMPING brush. This one covers the
// verb AUTO actually has: ConnectRegion reads the tiles already under the footprint, decides a
// terrain for every vertex they touch, and re-resolves the cells those vertices are corners of.
// Two things make its undo a different problem from the stamp's:
//
//   it writes vertices over the rect PLUS ONE RING, so its edits reach outside the footprint the
//   author saw; and it makes two parallel records — tiles on the tilemap, terrain in TerrainMap —
//   that have to move together.
//
// PaintAutoBrushFootprint's own doc comment says why the second one matters: a stroke that
// recorded tiles without terrain (or the reverse) leaves the two disagreeing the moment Ctrl+Z
// fires, "and that mismatch is exactly what gets written to the .overlay.json next". So the
// failure is not a wrong-looking undo — it is a corrupted save, one step later, with nothing
// logged in between.
//
// As in AutoBrushStrokeUndoTests, PaintAutoBrushFootprint's body is reproduced here against the
// real production statics rather than constructing TileEditorManager, which is a MonoBehaviour
// needing a full Grid + WorldGridBuilder scene. The pack is a SHIPPED one because ConnectRegion
// resolves placed sprites through TerrainCatalogLoader.Load() — a synthetic ruleset is not in
// that catalog and every vote it counts would come back empty.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    [TestFixture]
    public class AutoBrushConnectUndoTests
    {
        private const byte PurePrimary = 0;
        private const byte PureSecondary = 15;

        private readonly List<Object> _created = new List<Object>();
        private TerrainCatalog _catalog;
        private TileCatalog _tiles;
        private TilesetRuleset _pack;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            Assert.IsNotNull(_catalog, "The shipped TerrainCatalog is what ConnectRegion resolves against.");
            _tiles = TileCatalog.BuildFromResources();

            _pack = _catalog.Rulesets.FirstOrDefault(r =>
                r != null && r.Model == AutoTileModel.Corner16 && r.CornerSlots.Count == 16 &&
                !string.IsNullOrEmpty(r.TerrainSecondary));
            Assert.IsNotNull(_pack, "A populated Corner16 pack must ship.");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_tiles != null)
                foreach (var e in _tiles.Entries)
                    if (e.tile != null) Object.DestroyImmediate(e.tile);
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── Fixture plumbing ─────────────────────────────────────────────────────

        private Sprite SpriteFor(byte mask) => _pack.CornerSlots
            .Where(s => (byte)s.slot == mask && s.variants != null)
            .SelectMany(s => s.variants).First(v => v != null);

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

        /// <summary>Solid primary left of <paramref name="seamX"/>, solid secondary from it
        /// rightward — the hard cut an author gets from the ordinary brush, and the only shape
        /// AUTO has anything to do.</summary>
        private Tilemap HardCut(int width, int height, int seamX)
        {
            var map = NewTilemap();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                map.SetTile(new Vector3Int(x, y, 0),
                    TerrainTileResolver.ResolveTile(SpriteFor(x < seamX ? PurePrimary : PureSecondary)));
            return map;
        }

        private static Dictionary<Vector3Int, TileBase> SnapshotTiles(Tilemap map, int w, int h)
        {
            var snap = new Dictionary<Vector3Int, TileBase>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                snap[cell] = map.GetTile(cell);
            }
            return snap;
        }

        /// <summary>Reproduces TileEditorManager.PaintAutoBrushFootprint verbatim, minus the
        /// persistence marking (which needs a live manager and is covered by the
        /// TileOverlayPersistence suites).</summary>
        private void ConnectFootprint(TileEditorUndoSystem undo, Tilemap map, Vector3Int cursor,
                                      TerrainMap terrain, string preferred, int size)
        {
            var rect = new BoundsInt(cursor.x, cursor.y - (size - 1), 0, size, size, 1);
            var (edits, metadataEdits) = TerrainPainter.ConnectRegion(
                map, rect, _pack, _tiles, terrain, preferred);
            undo.RecordEdits(edits);
            undo.RecordMetadataEdits(metadataEdits);
        }

        /// <summary>press -> N drag calls -> release: ONE StartStroke/EndStroke pair.</summary>
        private void RunConnectStroke(TileEditorUndoSystem undo, Tilemap map, TerrainMap terrain,
                                      string preferred, int size, params Vector3Int[] dragPath)
        {
            undo.StartStroke(map);
            foreach (var cursor in dragPath)
                ConnectFootprint(undo, map, cursor, terrain, preferred, size);
            undo.EndStroke();
        }

        // ── One stroke, one step, both records ───────────────────────────────────

        [Test]
        public void OneStroke_CtrlZ_RestoresTheTilemapExactly()
        {
            var map = HardCut(8, 4, seamX: 4);
            var before = SnapshotTiles(map, 8, 4);
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();

            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, 2, new Vector3Int(3, 2, 0));

            Assert.IsFalse(before.All(kv => map.GetTile(kv.Key) == kv.Value),
                "sanity: the stroke has to have changed something for the undo to mean anything");

            Assert.IsNotNull(undo.Undo(), "an AUTO stroke that drew a seam must be undoable");

            foreach (var kv in before)
                Assert.AreSame(kv.Value, map.GetTile(kv.Key),
                    $"cell {kv.Key} did not come back to the tile that was there before the stroke");
        }

        [Test]
        public void OneStroke_CtrlZ_ClearsEveryVertexItDecided_IncludingTheRing()
        {
            // ConnectRegion decides vertices over the rect AND one ring beyond it, so its
            // metadata edits reach OUTSIDE the footprint the author dragged. An undo that only
            // covered the footprint would leave those vertices behind — invisible, because no
            // tile changed there, and fatal on the next save.
            var map = HardCut(8, 4, seamX: 4);
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();
            var cursor = new Vector3Int(3, 2, 0);
            const int size = 2;

            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, size, cursor);

            var decided = terrain.Cells.Keys.ToList();
            Assert.IsNotEmpty(decided, "sanity: the stroke has to have decided some vertices");

            // The footprint is size x size anchored top-left at the cursor; anything outside it
            // is the ring, and its presence is what makes this test worth having.
            var footprint = new RectInt(cursor.x, cursor.y - (size - 1), size, size);
            Assert.IsTrue(decided.Any(v => !footprint.Contains(v)),
                "the ring is part of the design — if nothing outside the footprint was decided, " +
                "this fixture is no longer testing what it claims to");

            undo.Undo();

            Assert.AreEqual(0, terrain.Count,
                "Ctrl+Z left " + terrain.Count + " vertices behind. TerrainMap and the tilemap now " +
                "disagree, and that disagreement is what the next save writes to the .overlay.json.");
        }

        [Test]
        public void ADragOfManyCells_IsOneUndoStep()
        {
            var map = HardCut(8, 6, seamX: 4);
            var before = SnapshotTiles(map, 8, 6);
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();

            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, 2,
                new Vector3Int(3, 5, 0), new Vector3Int(3, 4, 0),
                new Vector3Int(3, 3, 0), new Vector3Int(3, 2, 0));

            Assert.IsNotNull(undo.Undo(), "the whole drag is ONE batch");

            foreach (var kv in before)
                Assert.AreSame(kv.Value, map.GetTile(kv.Key), $"cell {kv.Key} survived the undo changed");
            Assert.AreEqual(0, terrain.Count, "and the terrain the whole drag decided is gone with it");

            Assert.IsNull(undo.Undo(),
                "a second Ctrl+Z must find nothing — four drag steps are one stroke, not four");
        }

        [Test]
        public void UndoThenRedo_PutsBothRecordsBack()
        {
            var map = HardCut(8, 4, seamX: 4);
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();

            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, 2, new Vector3Int(3, 2, 0));

            var afterStroke = SnapshotTiles(map, 8, 4);
            var terrainAfterStroke = terrain.Cells.ToDictionary(kv => kv.Key, kv => kv.Value);

            undo.Undo();
            Assert.AreEqual(0, terrain.Count);

            Assert.IsNotNull(undo.Redo(), "Ctrl+Y must find the stroke");

            foreach (var kv in afterStroke)
                Assert.AreSame(kv.Value, map.GetTile(kv.Key), $"cell {kv.Key} was not redrawn by redo");
            Assert.AreEqual(terrainAfterStroke.Count, terrain.Count, "and every vertex came back");
            foreach (var kv in terrainAfterStroke)
                Assert.AreEqual(kv.Value, terrain.GetTerrain(kv.Key), $"vertex {kv.Key} came back as something else");
        }

        // ── A stroke that did nothing is not a step ──────────────────────────────

        [Test]
        public void AStrokeThatChangedNothing_CostsNoUndoStep()
        {
            // AUTO analyses, so dragging over settled ground is legitimately a no-op. Recording
            // it anyway means a Ctrl+Z that appears to do nothing, and the author has to press it
            // once per idle stroke before reaching the edit they actually want back.
            var map = HardCut(8, 4, seamX: 4);
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();

            // A real edit first, so there is something on the stack to reach past.
            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, 2, new Vector3Int(3, 2, 0));
            var afterRealStroke = SnapshotTiles(map, 8, 4);

            // Then a stroke deep inside solid secondary ground, where nothing needs joining.
            // It still DECIDES vertices there (they are all secondary), so it may record metadata
            // — what it must not do is change a tile.
            var idleTerrain = new TerrainMap();
            var idleUndo = new TileEditorUndoSystem();
            idleUndo.StartStroke(map);
            var idle = TerrainPainter.ConnectRegion(map, new BoundsInt(6, 1, 0, 2, 2, 1),
                _pack, _tiles, idleTerrain, _pack.TerrainSecondary);
            idleUndo.EndStroke();

            Assert.IsEmpty(idle.TileEdits, "nothing to join inside solid ground, so no tile may change");
            foreach (var kv in afterRealStroke)
                Assert.AreSame(kv.Value, map.GetTile(kv.Key),
                    $"the idle stroke moved cell {kv.Key}");
        }

        [Test]
        public void AStrokeOverEmptyGround_RecordsNothingAtAll()
        {
            // No tiles means no votes, so there is nothing for AUTO to read and nothing to
            // record. A batch pushed here would be an undo step for a press that drew nothing.
            var map = NewTilemap();
            var terrain = new TerrainMap();
            var undo = new TileEditorUndoSystem();

            RunConnectStroke(undo, map, terrain, _pack.TerrainSecondary, 3, new Vector3Int(0, 0, 0));

            Assert.AreEqual(0, terrain.Count, "no tile touches those vertices, so none can be decided");
            Assert.IsNull(undo.Undo(), "an empty stroke must not become an undo step");
        }
    }
}
