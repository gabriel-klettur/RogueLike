using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Brush
{
    /// <summary>
    /// Comprehensive coverage of <see cref="TileBrush"/> — the core painting/erasing/
    /// flood-fill primitive used by the in-game tile editor.
    ///
    /// Verifies: footprint orientation (top-left anchor, extends right/down),
    /// brushSize bounds, no-op when painting same tile, edit constraint short-circuit,
    /// flood-fill connectivity (4-neighbour), max-cells cap, and erase semantics.
    /// </summary>
    [TestFixture]
    public class TileBrushExhaustiveTests
    {
        private GameObject _root;
        private Tilemap _tilemap;
        private Tile _tileA;
        private Tile _tileB;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TilemapRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;
            var go = new GameObject("Tilemap");
            go.transform.SetParent(_root.transform, false);
            _tilemap = go.AddComponent<Tilemap>();

            _tileA = MakeTile(Color.red);
            _tileB = MakeTile(Color.blue);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_tileA);
            Object.DestroyImmediate(_tileB);
        }

        private static Tile MakeTile(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            return t;
        }

        // ── Single-cell paint ────────────────────────────────────────────

        [Test]
        public void Paint_BrushSize1_AffectsOnlyOneCell()
        {
            var edits = TileBrush.Paint(_tilemap, new Vector3Int(3, 4, 0), _tileA, brushSize: 1);

            Assert.AreEqual(1, edits.Count);
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(3, 4, 0)));
        }

        [Test]
        public void Paint_SameTileTwice_SecondCallReturnsZeroEdits()
        {
            TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, 1);
            var edits2 = TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, 1);

            Assert.IsEmpty(edits2,
                "Painting the same tile on a cell that already has it must not record an edit.");
        }

        [Test]
        public void Paint_OverDifferentTile_RecordsOldTile()
        {
            TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, 1);
            var edits = TileBrush.Paint(_tilemap, Vector3Int.zero, _tileB, 1);

            Assert.AreEqual(1, edits.Count);
            Assert.AreEqual(_tileA, edits[0].OldTile);
            Assert.AreEqual(_tileB, edits[0].NewTile);
        }

        // ── Footprint orientation: anchor = top-left, grows right + DOWN ─

        [Test]
        public void Paint_BrushSize3_Footprint_TopLeftAnchor_GrowsRightAndDown()
        {
            // Anchor (10, 10). Brush 3×3 must paint:
            //   x = 10, 11, 12   (right)
            //   y = 10, 9, 8     (down)
            TileBrush.Paint(_tilemap, new Vector3Int(10, 10, 0), _tileA, brushSize: 3);

            for (int dy = 0; dy < 3; dy++)
            {
                for (int dx = 0; dx < 3; dx++)
                {
                    var p = new Vector3Int(10 + dx, 10 - dy, 0);
                    Assert.AreEqual(_tileA, _tilemap.GetTile(p),
                        $"Brush footprint missing at {p}");
                }
            }

            // Cells just outside the footprint must remain empty
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(9, 10, 0)),  "Cell to the left should not be painted.");
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(10, 11, 0)), "Cell above should not be painted.");
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(13, 10, 0)), "Cell beyond right edge should not be painted.");
            Assert.IsNull(_tilemap.GetTile(new Vector3Int(10, 7, 0)),  "Cell beyond bottom edge should not be painted.");
        }

        // ── Edit constraint ──────────────────────────────────────────────

        [Test]
        public void Paint_WithEditConstraint_SkipsDisallowedCells()
        {
            bool AllowOnlyEvenX(Vector3Int p) => p.x % 2 == 0;

            var edits = TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA,
                brushSize: 4, canEditCell: AllowOnlyEvenX);

            // Brush at (0,0) of size 4 → x ∈ {0,1,2,3}, y ∈ {0,-1,-2,-3}
            // Only x=0 and x=2 pass the predicate → 2 columns × 4 rows = 8 cells.
            Assert.AreEqual(8, edits.Count);
            foreach (var e in edits)
                Assert.IsTrue(e.Position.x % 2 == 0,
                    $"Edit at {e.Position} should have been skipped by constraint.");
        }

        [Test]
        public void Paint_ConstraintRejectsAll_ReturnsEmpty()
        {
            var edits = TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, 5,
                canEditCell: _ => false);

            Assert.IsEmpty(edits);
        }

        // ── Erase ────────────────────────────────────────────────────────

        [Test]
        public void Erase_RemovesTilesUnderBrushFootprint()
        {
            TileBrush.Paint(_tilemap, Vector3Int.zero, _tileA, brushSize: 2);
            Assert.AreEqual(_tileA, _tilemap.GetTile(Vector3Int.zero));

            var edits = TileBrush.Erase(_tilemap, Vector3Int.zero, brushSize: 2);

            Assert.AreEqual(4, edits.Count);
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    Assert.IsNull(_tilemap.GetTile(new Vector3Int(dx, -dy, 0)));
        }

        [Test]
        public void Erase_OnEmptyCell_ReturnsZeroEdits()
        {
            var edits = TileBrush.Erase(_tilemap, new Vector3Int(99, 99, 0), brushSize: 1);
            Assert.IsEmpty(edits);
        }

        // ── Flood fill ───────────────────────────────────────────────────

        [Test]
        public void FloodFill_SameTile_Noop()
        {
            _tilemap.SetTile(Vector3Int.zero, _tileA);
            var edits = TileBrush.FloodFill(_tilemap, Vector3Int.zero, _tileA);

            Assert.IsEmpty(edits, "Flood fill with the same tile must early-out.");
        }

        [Test]
        public void FloodFill_FillsContiguousRegion_4Connected()
        {
            // Paint a 3×3 block of tileA, plus one isolated cell of tileA at (10,10).
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    _tilemap.SetTile(new Vector3Int(x, y, 0), _tileA);
            _tilemap.SetTile(new Vector3Int(10, 10, 0), _tileA);

            var edits = TileBrush.FloodFill(_tilemap, new Vector3Int(0, 0, 0), _tileB);

            Assert.AreEqual(9, edits.Count, "Only the contiguous 3x3 region should be filled.");
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(10, 10, 0)),
                "Isolated cell across a gap must NOT be filled.");
        }

        [Test]
        public void FloodFill_RespectsEditConstraint()
        {
            for (int x = 0; x < 5; x++)
                _tilemap.SetTile(new Vector3Int(x, 0, 0), _tileA);

            // Only allow x ≤ 2 → fill must stop at x=2.
            var edits = TileBrush.FloodFill(_tilemap, Vector3Int.zero, _tileB,
                canEditCell: p => p.x <= 2);

            Assert.AreEqual(3, edits.Count);
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(3, 0, 0)));
            Assert.AreEqual(_tileA, _tilemap.GetTile(new Vector3Int(4, 0, 0)));
        }

        [Test]
        public void FloodFill_RespectsMaxCells()
        {
            // Long horizontal strip of 100 cells.
            for (int x = 0; x < 100; x++)
                _tilemap.SetTile(new Vector3Int(x, 0, 0), _tileA);

            var edits = TileBrush.FloodFill(_tilemap, Vector3Int.zero, _tileB, maxCells: 10);

            Assert.LessOrEqual(edits.Count, 10,
                "Flood fill must honour the maxCells cap.");
        }
    }
}
