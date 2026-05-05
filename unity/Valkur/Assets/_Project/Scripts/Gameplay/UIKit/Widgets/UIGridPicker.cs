using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Grid layout picker built on top of <see cref="UIFactory.MakeScrollView"/>.
    /// Replaces the scroll's <see cref="VerticalLayoutGroup"/> with a
    /// <see cref="GridLayoutGroup"/> so callers can drop slot buttons in a
    /// catalog/inventory shape.
    /// </summary>
    public static class UIGridPicker
    {
        public static (ScrollRect scroll, RectTransform content) Make(
            Transform parent, string name, int columns = 5, float cellSize = 64f, float spacing = 4f)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, name);
            // DestroyImmediate is required: Object.Destroy is deferred to
            // end-of-frame, so AddComponent<GridLayoutGroup> would fail
            // because Unity prevents two LayoutGroup components on the
            // same GameObject.
            var existingVlg = content.GetComponent<VerticalLayoutGroup>();
            if (existingVlg != null)
                Object.DestroyImmediate(existingVlg);
            var glg = content.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(cellSize, cellSize);
            glg.spacing = new Vector2(spacing, spacing);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = columns;
            glg.padding = new RectOffset(4, 4, 4, 4);
            return (scroll, content);
        }

        /// <summary>
        /// Responsive variant — attaches a <see cref="GridAutoSize"/> component
        /// so the cell size and column count reflow automatically whenever the
        /// host panel is resized. <paramref name="minCellSize"/> sets the
        /// smallest cell width allowed before the column count is reduced;
        /// <paramref name="maxCellSize"/> caps cell growth so very wide panels
        /// don't end up with gigantic cells.
        /// </summary>
        public static (ScrollRect scroll, RectTransform content, GridAutoSize autoSize) MakeResponsive(
            Transform parent, string name,
            float minCellSize = 56f, float maxCellSize = 96f, float spacing = 4f)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, name);
            var existingVlg = content.GetComponent<VerticalLayoutGroup>();
            if (existingVlg != null)
                Object.DestroyImmediate(existingVlg);
            // GridAutoSize requires a GridLayoutGroup sibling. Add it first; the
            // initial cellSize/constraint values are placeholders that will be
            // overwritten by GridAutoSize.Recompute() as soon as the layout
            // system assigns a real width.
            var glg = content.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize        = new Vector2(minCellSize, minCellSize);
            glg.spacing         = new Vector2(spacing, spacing);
            glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 1;
            glg.padding         = new RectOffset(4, 4, 4, 4);

            var autoSize = content.gameObject.AddComponent<GridAutoSize>();
            autoSize.MinCellSize = minCellSize;
            autoSize.MaxCellSize = maxCellSize;
            autoSize.Spacing     = spacing;
            return (scroll, content, autoSize);
        }
    }
}
