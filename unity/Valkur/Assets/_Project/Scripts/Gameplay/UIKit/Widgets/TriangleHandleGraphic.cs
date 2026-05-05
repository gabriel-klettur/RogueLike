using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Lightweight UI <see cref="Graphic"/> that fills its rect with a single
    /// right-triangle whose right-angle sits at the bottom-right corner —
    /// canonical "resize" affordance. No sprite asset needed; the mesh is
    /// generated from the rect.
    ///
    /// Used by <see cref="PanelResizeHandle"/>; can be reused anywhere a
    /// triangular corner glyph is wanted.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class TriangleHandleGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var r = rectTransform.rect;
            // Local rect coords. Right-angle at bottom-right; hypotenuse from
            // (xMin, yMin) up to (xMax, yMax) so the triangle visually points
            // outward into the corner.
            var bl = new Vector3(r.xMin, r.yMin, 0f);
            var br = new Vector3(r.xMax, r.yMin, 0f);
            var tr = new Vector3(r.xMax, r.yMax, 0f);

            vh.AddVert(bl, color, Vector2.zero);
            vh.AddVert(br, color, Vector2.zero);
            vh.AddVert(tr, color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
