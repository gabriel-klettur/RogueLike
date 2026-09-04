using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Lightweight UI <see cref="Graphic"/> that fills its rect with a single
    /// right-triangle whose right-angle sits in one corner — the canonical
    /// "resize" affordance. No sprite asset needed; the mesh is generated from
    /// the rect.
    ///
    /// Used by <see cref="PanelResizeHandle"/>; can be reused anywhere a
    /// triangular corner glyph is wanted.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class TriangleHandleGraphic : MaskableGraphic
    {
        [SerializeField, Tooltip("Which corner the right-angle points into. Match the PanelResizeHandle beside it.")]
        private ResizeGripCorner corner = ResizeGripCorner.BottomRight;

        /// <summary>
        /// Which corner the right-angle occupies. Shares
        /// <see cref="ResizeGripCorner"/> with <see cref="PanelResizeHandle"/> on purpose:
        /// the glyph and the drag it advertises must never be able to name different corners.
        /// </summary>
        public ResizeGripCorner Corner
        {
            get => corner;
            set { corner = value; SetVerticesDirty(); }
        }

        /// <summary>
        /// Built by MIRRORING the vertices rather than by rotating or negatively scaling the
        /// transform. Both of those move the rect: the grip is pivoted in the panel's corner,
        /// so a rotation about that pivot swings the whole 16 px square outside the panel —
        /// measured, a top-right grip landed at x=[540..556] against a panel whose right edge
        /// is 540, i.e. entirely outside the window it resizes. A negative scale would also
        /// flip the triangle winding.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var r = rectTransform.rect;
            float near = corner == ResizeGripCorner.TopRight ? r.yMax : r.yMin;
            float far = corner == ResizeGripCorner.TopRight ? r.yMin : r.yMax;

            // Right-angle in the chosen corner, hypotenuse running to the opposite one, so
            // the triangle visually points outward into the corner it drags towards.
            var rightAngle = new Vector3(r.xMax, near, 0f);
            var alongEdge = new Vector3(r.xMin, near, 0f);
            var upTheSide = new Vector3(r.xMax, far, 0f);

            vh.AddVert(alongEdge, color, Vector2.zero);
            vh.AddVert(rightAngle, color, Vector2.zero);
            vh.AddVert(upTheSide, color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
