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
    }
}
