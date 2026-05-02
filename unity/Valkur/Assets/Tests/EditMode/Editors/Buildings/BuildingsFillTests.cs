using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Tests for the two pure-compute pieces of the Buildings Fill tool:
    ///
    ///   1. <see cref="TileBrush.ComputeFloodFillCells"/> — 4-connected BFS flood fill.
    ///   2. <see cref="BuildingsFillSpacingFilter.Apply"/> — greedy row-major spacing filter.
    ///
    /// Neither test requires a live editor session; all Unity objects are created and torn
    /// down per test.
    /// </summary>
    [TestFixture]
    public class BuildingsFillTests
    {
        // ── Cleanup tracking ──────────────────────────────────────────────────────

        private readonly List<GameObject>      _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets       = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
        }

        // ── Tilemap factory ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a Grid + Tilemap pair with cellSize = (1,1,0) at world origin.
        /// <see cref="Tilemap.GetCellCenterWorld(Vector3Int)"/> returns (x+0.5, y+0.5, 0)
        /// for cell (x, y, 0) when the Grid is at origin with unit cell size.
        /// </summary>
        private Tilemap CreateTilemap()
        {
            var gridGo = new GameObject("Grid");
            _sceneObjects.Add(gridGo);
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            var tmGo = new GameObject("Tilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            _sceneObjects.Add(tmGo);
            return tmGo.AddComponent<Tilemap>();
        }

        /// <summary>
        /// Creates a <see cref="Tile"/> ScriptableObject with a null sprite.
        /// Reference equality on the TileBase instance is what flood-fill compares.
        /// </summary>
        private Tile CreateTile()
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            // sprite intentionally left null — flood-fill uses reference equality on the
            // TileBase object itself, not on the sprite.
            _assets.Add(tile);
            return tile;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  PART 1 — TileBrush.ComputeFloodFillCells
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A fully enclosed 3×3 patch of identical tiles surrounded by a different tile
        /// (ring of 16 border cells).  Flood from center must return exactly the 9 inner cells.
        ///
        /// Layout (A = tileA, B = tileB):
        ///   B B B B B
        ///   B A A A B
        ///   B A A A B   ← center (2,2)
        ///   B A A A B
        ///   B B B B B
        /// </summary>
        [Test]
        public void ComputeFloodFillCells_3x3PatchSurroundedByOtherTile_Returns9Cells()
        {
            var tilemap = CreateTilemap();
            var tileA   = CreateTile();
            var tileB   = CreateTile();

            // Paint 5×5 area with tileB, then overwrite 3×3 interior with tileA.
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), tileB);

            for (int x = 1; x <= 3; x++)
                for (int y = 1; y <= 3; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), tileA);

            var center = new Vector3Int(2, 2, 0);
            var result = TileBrush.ComputeFloodFillCells(tilemap, center);

            Assert.AreEqual(9, result.Count,
                "Flood fill from center of a 3×3 tileA patch enclosed by tileB must return exactly 9 cells.");

            // Every cell in the inner 3×3 must be present.
            for (int x = 1; x <= 3; x++)
                for (int y = 1; y <= 3; y++)
                    Assert.IsTrue(result.Contains(new Vector3Int(x, y, 0)),
                        $"Cell ({x},{y}) must be in the flood-fill result.");
        }

        /// <summary>
        /// Two same-tile regions that are diagonally adjacent but NOT 4-connected.
        /// Flood from one region must return ONLY that region, never crossing the diagonal.
        ///
        /// Layout (A = tileA, B = tileB, . = empty/null):
        ///   A . A
        ///   . A .     ← cell (1,1) is tileA but only diagonally connected to (0,0) and (2,0)
        ///   A . A
        ///
        /// Start from (0,0): only (0,0) is reachable via 4-connectivity since (1,0) is null.
        /// </summary>
        [Test]
        public void ComputeFloodFillCells_DiagonalRegions_DoesNotCrossDiagonal()
        {
            var tilemap = CreateTilemap();
            var tileA   = CreateTile();

            // Checkerboard of tileA on even-sum positions (x+y even).
            // Each tileA cell is only diagonally adjacent to other tileA cells; 4-connected
            // neighbors are all empty (null tile).
            tilemap.SetTile(new Vector3Int(0, 0, 0), tileA);
            tilemap.SetTile(new Vector3Int(2, 0, 0), tileA);
            tilemap.SetTile(new Vector3Int(1, 1, 0), tileA);
            tilemap.SetTile(new Vector3Int(0, 2, 0), tileA);
            tilemap.SetTile(new Vector3Int(2, 2, 0), tileA);
            // Positions with x+y odd are left null (the "gap" between diagonal cells).

            var result = TileBrush.ComputeFloodFillCells(tilemap, new Vector3Int(0, 0, 0));

            Assert.AreEqual(1, result.Count,
                "Flood fill on an isolated diagonal island must return only 1 cell — " +
                "4-connectivity never crosses a diagonal gap.");
            Assert.IsTrue(result.Contains(new Vector3Int(0, 0, 0)),
                "The single result must be the start cell (0,0).");
        }

        /// <summary>
        /// <see cref="TileBrush.ComputeFloodFillCells"/> accepts a <c>canEditCell</c> predicate.
        /// Cells for which the predicate returns false must be masked out, even if their tile
        /// matches the flood tile, AND must not be traversed as stepping-stones.
        /// </summary>
        [Test]
        public void ComputeFloodFillCells_CanEditCellPredicateFalse_MasksCells()
        {
            var tilemap = CreateTilemap();
            var tileA   = CreateTile();

            // 5×1 horizontal strip of tileA at y=0.
            for (int x = 0; x < 5; x++)
                tilemap.SetTile(new Vector3Int(x, 0, 0), tileA);

            // Only allow x < 3 (block x=3 and x=4).
            Func<Vector3Int, bool> onlyLeft = pos => pos.x < 3;

            var result = TileBrush.ComputeFloodFillCells(tilemap, new Vector3Int(0, 0, 0),
                canEditCell: onlyLeft);

            Assert.AreEqual(3, result.Count,
                "canEditCell blocking x>=3 must yield only 3 cells (x=0,1,2).");
            Assert.IsFalse(result.Contains(new Vector3Int(3, 0, 0)),
                "Cell (3,0) must be excluded by canEditCell.");
            Assert.IsFalse(result.Contains(new Vector3Int(4, 0, 0)),
                "Cell (4,0) must be excluded by canEditCell.");
        }

        /// <summary>
        /// The <c>maxCells</c> cap must be honoured: when a large region is flood-filled
        /// with a cap smaller than the full region, the result count must not exceed the cap.
        /// </summary>
        [Test]
        public void ComputeFloodFillCells_MaxCellsCap_LimitsResult()
        {
            var tilemap = CreateTilemap();
            var tileA   = CreateTile();

            // 3×3 region (9 cells) of tileA.
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), tileA);

            const int cap = 5;
            var result = TileBrush.ComputeFloodFillCells(
                tilemap, new Vector3Int(0, 0, 0), maxCells: cap);

            Assert.LessOrEqual(result.Count, cap,
                $"Result count must not exceed the maxCells cap of {cap}.");
            Assert.Greater(result.Count, 0,
                "At least one cell must be returned when the region is non-empty.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  PART 2 — BuildingsFillSpacingFilter.Apply
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// An empty candidate set must always return an empty list — no crash, no allocation.
        /// </summary>
        [Test]
        public void SpacingFilter_EmptyCandidates_ReturnsEmpty()
        {
            var tilemap = CreateTilemap();

            var result = BuildingsFillSpacingFilter.Apply(
                new HashSet<Vector3Int>(),
                spacingTiles: 2,
                tilemap: tilemap,
                existingPositions: new List<Vector2>());

            Assert.IsNotNull(result, "Apply must never return null.");
            Assert.AreEqual(0, result.Count, "Empty candidates must yield empty result.");
        }

        /// <summary>
        /// 5 candidates in a horizontal row, spacing = 1.
        /// Adjacent cell centers are 1.0 world unit apart; the filter check is strict-less-than,
        /// so distance == spacing is accepted (not blocked).  All 5 must survive.
        /// </summary>
        [Test]
        public void SpacingFilter_5CellsInRow_Spacing1_AllAccepted()
        {
            var tilemap = CreateTilemap();

            // Cells (0,0) to (4,0). World centers: (0.5,0.5) … (4.5,0.5).
            // Adjacent distance = 1.0 == spacing → NOT < spacing → all accepted.
            var candidates = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(3, 0, 0),
                new Vector3Int(4, 0, 0),
            };

            var result = BuildingsFillSpacingFilter.Apply(
                candidates,
                spacingTiles: 1,
                tilemap: tilemap,
                existingPositions: new List<Vector2>());

            Assert.AreEqual(5, result.Count,
                "Spacing 1 with adjacent cells at exactly distance 1 must accept all 5 — " +
                "boundary condition: distance >= spacing is accepted.");
        }

        /// <summary>
        /// 5 candidates in a horizontal row, spacing = 2.
        /// Adjacent distance = 1.0 < 2.0 → every other cell is blocked.
        /// Greedy row-major: cells 0, 2, 4 accepted; cells 1, 3 rejected.
        /// </summary>
        [Test]
        public void SpacingFilter_5CellsInRow_Spacing2_Returns3Accepted()
        {
            var tilemap = CreateTilemap();

            var candidates = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(3, 0, 0),
                new Vector3Int(4, 0, 0),
            };

            var result = BuildingsFillSpacingFilter.Apply(
                candidates,
                spacingTiles: 2,
                tilemap: tilemap,
                existingPositions: new List<Vector2>());

            Assert.AreEqual(3, result.Count,
                "Spacing 2 on a 5-cell row must produce 3 accepted cells (every other one).");

            // Verify the specific cells that survive (greedy picks leftmost first in row-major).
            Assert.IsTrue(result.Contains(new Vector3Int(0, 0, 0)), "Cell (0,0) must be accepted.");
            Assert.IsTrue(result.Contains(new Vector3Int(2, 0, 0)), "Cell (2,0) must be accepted.");
            Assert.IsTrue(result.Contains(new Vector3Int(4, 0, 0)), "Cell (4,0) must be accepted.");
            Assert.IsFalse(result.Contains(new Vector3Int(1, 0, 0)), "Cell (1,0) must be rejected.");
            Assert.IsFalse(result.Contains(new Vector3Int(3, 0, 0)), "Cell (3,0) must be rejected.");
        }

        /// <summary>
        /// Spacing = 3; an existing building is placed at the world center of candidate cell (2,0).
        /// Cell center for (2,0) = (2.5, 0.5) with default cellSize=1.
        /// All 5 cells in the row are within distance 3 of that position, so all are blocked.
        /// </summary>
        [Test]
        public void SpacingFilter_ExistingBuildingAtCenterCell_BlocksNearbyAccepts()
        {
            var tilemap = CreateTilemap();

            // Candidate cells (0,0) … (4,0).
            var candidates = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(3, 0, 0),
                new Vector3Int(4, 0, 0),
            };

            // Existing building at the world center of cell (2,0).
            // GetCellCenterWorld((2,0,0)) = (2.5, 0.5, 0) for a unit-cell Grid at origin.
            var existingAtCellCenter = tilemap.GetCellCenterWorld(new Vector3Int(2, 0, 0));
            var existingPositions = new List<Vector2>
            {
                new Vector2(existingAtCellCenter.x, existingAtCellCenter.y)
            };

            var result = BuildingsFillSpacingFilter.Apply(
                candidates,
                spacingTiles: 3,
                tilemap: tilemap,
                existingPositions: existingPositions);

            // All 5 cell centers are within distance 3 of (2.5, 0.5):
            //   (0,0)→(0.5,0.5): dist=2.0 < 3  blocked
            //   (1,0)→(1.5,0.5): dist=1.0 < 3  blocked
            //   (2,0)→(2.5,0.5): dist=0.0 < 3  blocked
            //   (3,0)→(3.5,0.5): dist=1.0 < 3  blocked
            //   (4,0)→(4.5,0.5): dist=2.0 < 3  blocked
            Assert.AreEqual(0, result.Count,
                "With an existing building at the center cell and spacing=3, " +
                "all row cells fall within distance 3 and must be blocked.");
        }

        /// <summary>
        /// Row-major ordering sanity check: 3×3 grid, spacing = 2, no existing buildings.
        ///
        /// Sort order (Y desc, X asc):
        ///   (0,2),(1,2),(2,2),(0,1),(1,1),(2,1),(0,0),(1,0),(2,0)
        /// World centers (+0.5 offset):
        ///   (0.5,2.5),(1.5,2.5),(2.5,2.5),(0.5,1.5),(1.5,1.5),(2.5,1.5),(0.5,0.5),(1.5,0.5),(2.5,0.5)
        ///
        /// Greedy accept/reject trace (minDist=2, strict-less-than):
        ///   (0,2)  accept → placed=(0.5,2.5)
        ///   (1,2)  dist(0,2)=1.0 < 2 → reject
        ///   (2,2)  dist(0,2)=2.0 NOT < 2 → accept → placed adds (2.5,2.5)
        ///   (0,1)  dist(0,2)=1.0 < 2 → reject
        ///   (1,1)  dist(0,2)=√2≈1.41 < 2 → reject
        ///   (2,1)  dist(0,2)=√5≈2.24 ok, dist(2,2)=1.0 < 2 → reject
        ///   (0,0)  dist(0,2)=2.0 ok, dist(2,2)=√8≈2.83 ok → accept
        ///   (1,0)  dist(0,0)=1.0 < 2 → reject
        ///   (2,0)  dist(0,2)=√(4+4)=2.83 ok, dist(2,2)=2.0 ok, dist(0,0)=2.0 ok → accept
        ///
        /// Expected survivors: (0,2), (2,2), (0,0), (2,0) = 4 cells.
        /// </summary>
        [Test]
        public void SpacingFilter_3x3Grid_Spacing2_Returns4CornerCells()
        {
            var tilemap = CreateTilemap();

            var candidates = new HashSet<Vector3Int>();
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    candidates.Add(new Vector3Int(x, y, 0));

            var result = BuildingsFillSpacingFilter.Apply(
                candidates,
                spacingTiles: 2,
                tilemap: tilemap,
                existingPositions: new List<Vector2>());

            Assert.AreEqual(4, result.Count,
                "3×3 grid with spacing=2 must produce 4 survivors " +
                "(the four corners: (0,2),(2,2),(0,0),(2,0)).");

            Assert.IsTrue(result.Contains(new Vector3Int(0, 2, 0)), "Top-left (0,2) must survive.");
            Assert.IsTrue(result.Contains(new Vector3Int(2, 2, 0)), "Top-right (2,2) must survive.");
            Assert.IsTrue(result.Contains(new Vector3Int(0, 0, 0)), "Bottom-left (0,0) must survive.");
            Assert.IsTrue(result.Contains(new Vector3Int(2, 0, 0)), "Bottom-right (2,0) must survive.");
        }
    }
}
