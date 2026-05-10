using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.General
{
    /// <summary>
    /// Pins the responsive grid math implemented by <see cref="GridAutoSize"/>.
    ///
    /// The component sits next to a <see cref="GridLayoutGroup"/> and on every
    /// rect-dimensions change recomputes:
    ///   • <c>constraintCount</c> = max columns that fit at <c>minCellSize</c>;
    ///   • <c>cellSize</c>        = available width / cols, capped at <c>maxCellSize</c>.
    ///
    /// Algorithm:
    ///   available = width - paddingLeft - paddingRight
    ///   cols      = max(1, floor((available + spacing) / (minCellSize + spacing)))
    ///   cellW     = clamp((available - (cols-1)*spacing) / cols, 1, maxCellSize)
    ///
    /// Tests construct a fresh GameObject hierarchy per test so EditMode can
    /// drive sizeDelta directly and inspect the resulting GridLayoutGroup state.
    /// </summary>
    [TestFixture]
    public class GridAutoSizeTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Build a self-anchored RectTransform with an explicit width, attach
        /// GridLayoutGroup + GridAutoSize and return the auto-sizer ready for
        /// assertions. Uses a non-stretched anchor so <c>rect.width</c> reads
        /// directly from sizeDelta.x — deterministic for tests.
        /// </summary>
        private GridAutoSize BuildGrid(float width,
            float minCell = 56f, float maxCell = 96f, float spacing = 4f,
            int padL = 4, int padR = 4)
        {
            var go = new GameObject("GridAutoSizeHost", typeof(RectTransform));
            _scene.Add(go);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;
            rt.sizeDelta = new Vector2(width, 200f);

            go.AddComponent<GridLayoutGroup>();
            var auto = go.AddComponent<GridAutoSize>();
            auto.MinCellSize = minCell;
            auto.MaxCellSize = maxCell;
            auto.Spacing     = spacing;
            auto.Padding     = new RectOffset(padL, padR, 4, 4);
            auto.ForceRecompute();
            return auto;
        }

        // ── Behaviours ────────────────────────────────────────────────────────

        [Test]
        public void Recompute_AtNarrowWidth_FallsBackToOneColumn()
        {
            // 60 px - 8 padding = 52 available, less than the 56 min cell ⇒
            // floor((52+4)/(56+4)) = floor(0.93) = 0, clamped to 1 column.
            var auto = BuildGrid(width: 60f, minCell: 56f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(1, grid.constraintCount,
                "Cols must clamp to 1 even when the panel is narrower than minCellSize.");
            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint);
        }

        [Test]
        public void Recompute_AtMediumWidth_PicksReasonableColumnCount()
        {
            // 256 px - 8 = 248 available, (248+4)/(56+4) = 4.2 ⇒ 4 cols.
            var auto = BuildGrid(width: 256f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(4, grid.constraintCount,
                "256 px panel should fit 4 cols at minCellSize=56.");
            // cellW = (248 - 3*4) / 4 = 59. Square cells.
            Assert.AreEqual(59f, grid.cellSize.x, 0.01f);
            Assert.AreEqual(grid.cellSize.x, grid.cellSize.y,
                "Cells must stay square so icon + label keep the slot proportions.");
        }

        [Test]
        public void Recompute_AtWideWidth_GrowsColumnsButCellSizeStaysCapped()
        {
            // 1000 px - 8 = 992 available. (992+4)/60 = 16.6 ⇒ 16 cols.
            // cellW = (992 - 15*4)/16 = 58.25 — well under maxCell=96, no cap hit.
            var auto = BuildGrid(width: 1000f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.GreaterOrEqual(grid.constraintCount, 8,
                "Wide panels must grow column count, not just stretch a few cells.");
            Assert.LessOrEqual(grid.cellSize.x, auto.MaxCellSize,
                "Cell size must never exceed maxCellSize.");
        }

        [Test]
        public void Recompute_CapsCellSize_AtMaxCellSize()
        {
            // 220 - 8 = 212 available, with minCell=56:
            //   cols = floor((212+4)/60) = 3
            //   cellW = (212 - 8)/3 = 68
            // We set maxCell=64 so the cap is hit.
            var auto = BuildGrid(width: 220f, minCell: 56f, maxCell: 64f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(3, grid.constraintCount);
            Assert.AreEqual(64f, grid.cellSize.x, 0.01f,
                "When (available/cols) > maxCell, cellW must clamp to maxCell.");
        }

        [Test]
        public void Resize_TriggersRecompute_AndUpdatesColumnCount()
        {
            var auto = BuildGrid(width: 200f);
            var grid = auto.GetComponent<GridLayoutGroup>();
            int colsAt200 = grid.constraintCount;

            // Grow the rect — this fires OnRectTransformDimensionsChange in
            // EditMode (UIBehaviour callback) which recomputes the grid.
            var rt = auto.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(600f, 200f);
            auto.ForceRecompute();

            Assert.Greater(grid.constraintCount, colsAt200,
                "Growing the rect must add columns, not just enlarge cells.");

            // Shrink it back — column count must drop again.
            rt.sizeDelta = new Vector2(120f, 200f);
            auto.ForceRecompute();
            Assert.LessOrEqual(grid.constraintCount, colsAt200,
                "Shrinking the rect must reduce column count.");
        }

        [Test]
        public void Recompute_AtZeroWidth_LeavesGridStateUnchanged()
        {
            // First lay out at a real width so cellSize and constraintCount are
            // known, then collapse the rect to 0 — the recompute must early-out
            // (avoiding bogus 0-width cells that would flicker on next layout).
            var auto = BuildGrid(width: 200f);
            var grid = auto.GetComponent<GridLayoutGroup>();
            int  colsBefore  = grid.constraintCount;
            float cellBefore = grid.cellSize.x;

            var rt = auto.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 200f);
            auto.ForceRecompute();

            Assert.AreEqual(colsBefore,  grid.constraintCount,
                "Recompute at width<=1 must leave constraintCount alone.");
            Assert.AreEqual(cellBefore, grid.cellSize.x, 0.01f,
                "Recompute at width<=1 must leave cellSize alone.");
        }

        [Test]
        public void Padding_ReducesAvailableWidth_ForColumnComputation()
        {
            // 200 px width with padL=padR=4 → 192 available → cols = floor(196/60) = 3.
            var tight = BuildGrid(width: 200f, padL: 4, padR: 4);
            int colsTight = tight.GetComponent<GridLayoutGroup>().constraintCount;

            // Same width but heavy padding → only 160 available → cols = floor(164/60) = 2.
            var loose = BuildGrid(width: 200f, padL: 20, padR: 20);
            int colsLoose = loose.GetComponent<GridLayoutGroup>().constraintCount;

            Assert.Less(colsLoose, colsTight,
                "Larger padding must reduce the column count for the same panel width.");
        }

        [Test]
        public void Spacing_IsAppliedToBothAxes()
        {
            var auto = BuildGrid(width: 256f, spacing: 6f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(6f, grid.spacing.x, 0.01f);
            Assert.AreEqual(6f, grid.spacing.y, 0.01f,
                "Spacing must apply on both axes — vertical gap matches horizontal gap.");
        }

        [Test]
        public void Padding_ReachesGridLayoutGroup()
        {
            var auto = BuildGrid(width: 256f, padL: 12, padR: 8);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(12, grid.padding.left,  "Left padding must reach the GridLayoutGroup.");
            Assert.AreEqual( 8, grid.padding.right, "Right padding must reach the GridLayoutGroup.");
        }

        [Test]
        public void Constraint_AlwaysFixedColumnCount()
        {
            var auto = BuildGrid(width: 256f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint,
                "Constraint must be FixedColumnCount so reflow happens on the column axis.");
        }

        [Test]
        public void Setter_MinCellSize_TriggersRecompute()
        {
            var auto = BuildGrid(width: 256f, minCell: 56f);
            int colsAtMin56 = auto.GetComponent<GridLayoutGroup>().constraintCount;

            auto.MinCellSize = 100f;   // setter should call ForceRecompute()
            int colsAtMin100 = auto.GetComponent<GridLayoutGroup>().constraintCount;

            Assert.Less(colsAtMin100, colsAtMin56,
                "Increasing minCellSize must reduce column count without an explicit recompute call.");
        }

        [Test]
        public void NoExceptions_OnAttach_OrOnRectChange()
        {
            // The scariest historical bug for this class is RectOffset getting
            // constructed in a field initializer ('set_left is not allowed
            // from a MonoBehaviour constructor'). Construct + drive the rect
            // hard and assert the path stays exception-free.
            Assert.DoesNotThrow(() =>
            {
                var auto = BuildGrid(width: 100f);
                var rt   = auto.GetComponent<RectTransform>();
                for (int w = 80; w <= 800; w += 40)
                {
                    rt.sizeDelta = new Vector2(w, 200f);
                    auto.ForceRecompute();
                }
            });
        }

        // ── CellHeightOverride (rectangular cells) ──────────────────────────
        // Added to support the F8 Tile Editor's CATEGORIES list, which is a
        // responsive grid of 22-px-tall rows. Without an explicit height
        // override the cells would be square (height = width), which would
        // turn each category row into a fat 110-200 px-tall button.

        [Test]
        public void CellHeightOverride_Positive_ProducesRectangularCells()
        {
            var auto = BuildGrid(width: 200f, minCell: 50f, maxCell: 80f);
            auto.CellHeightOverride = 22f;  // setter triggers a recompute

            var grid = auto.GetComponent<GridLayoutGroup>();
            Assert.AreEqual(22f, grid.cellSize.y, 0.01f,
                "Cell height must match the override exactly.");
            Assert.AreNotEqual(grid.cellSize.x, grid.cellSize.y,
                "With cellHeightOverride > 0, cells are no longer square.");
        }

        [Test]
        public void CellHeightOverride_Zero_KeepsCellsSquare_BackwardsCompat()
        {
            // Default cellHeightOverride = 0 → square cells.
            var auto = BuildGrid(width: 256f);
            var grid = auto.GetComponent<GridLayoutGroup>();

            Assert.AreEqual(grid.cellSize.x, grid.cellSize.y, 0.01f,
                "Default cellHeightOverride (0) must preserve the historical " +
                "square-cell behaviour — every existing picker grid relies on it.");
        }

        [Test]
        public void CellHeightOverride_DoesNotAffectColumnCount()
        {
            // Two grids, identical width/minCell, different cellHeightOverride.
            // Only the Y axis should change; the column-count math reads
            // exclusively from width + paddingLeft + paddingRight + spacing.
            var square = BuildGrid(width: 400f, minCell: 100f);
            int colsSq = square.GetComponent<GridLayoutGroup>().constraintCount;

            var rect = BuildGrid(width: 400f, minCell: 100f);
            rect.CellHeightOverride = 22f;
            int colsRect = rect.GetComponent<GridLayoutGroup>().constraintCount;

            Assert.AreEqual(colsSq, colsRect,
                "cellHeightOverride must only affect the Y axis — column count " +
                "is derived from width and minCellSize alone.");
        }

        [Test]
        public void Setter_CellHeightOverride_TriggersRecompute()
        {
            var auto = BuildGrid(width: 200f);
            var grid = auto.GetComponent<GridLayoutGroup>();
            float widthBefore  = grid.cellSize.x;

            // Setter MUST call ForceRecompute internally — otherwise the new
            // cellHeight wouldn't reach the GridLayoutGroup until the next
            // OnRectTransformDimensionsChange.
            auto.CellHeightOverride = 30f;

            Assert.AreEqual(30f, grid.cellSize.y, 0.01f,
                "Setting CellHeightOverride must immediately update GridLayoutGroup.cellSize.y.");
            Assert.AreEqual(widthBefore, grid.cellSize.x, 0.01f,
                "Changing only CellHeightOverride must NOT alter cellSize.x.");
        }

        [Test]
        public void CellHeightOverride_NegativeValue_TreatedAsSquare()
        {
            // Guard against accidental negative values from inspector or
            // serialized data: < 0 must behave the same as 0 (square cells).
            var auto = BuildGrid(width: 200f);
            auto.CellHeightOverride = -50f;

            var grid = auto.GetComponent<GridLayoutGroup>();
            Assert.AreEqual(grid.cellSize.x, grid.cellSize.y, 0.01f,
                "Negative cellHeightOverride must collapse to the square-cell default.");
        }
    }
}
