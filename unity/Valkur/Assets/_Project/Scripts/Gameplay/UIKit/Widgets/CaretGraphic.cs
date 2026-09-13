using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>Which way a <see cref="CaretGraphic"/>'s apex points.</summary>
    public enum CaretDirection { Down, Up, Left, Right }

    /// <summary>
    /// A solid isoceles triangle filling its rect — the "there is a list behind this" caret.
    ///
    /// <para><b>WHY THIS IS DRAWN AND NOT TYPED.</b> Every dropdown in the project used to set
    /// its caret to the character <c>U+25BE</c>, and the shipped font does not have it: TMP
    /// substituted <c>U+25A1</c>, an empty box, and logged a warning per instance. Measured on
    /// the Entities editor alone that was <b>fourteen tofu boxes and fourteen console
    /// warnings</b> — every dropdown it owns — in a project whose first cardinal rule is that
    /// the console must be clean.</para>
    ///
    /// <para>Swapping in a different arrow character is not the fix, and the measurement says
    /// so rather than taste: asked directly, <c>LiberationSans SDF</c> carries <b>none</b> of
    /// <c>▾ ▼ ▽ ▴ ↓ ‸ ˅ ˇ ∨ ▪</c>. The font has no triangle at all, so any glyph chosen from
    /// the outside is one more guess that fails the same way — silently, at runtime, in a
    /// warning nobody reads until an audit counts them.</para>
    ///
    /// <para>It is deliberately NOT <see cref="TriangleHandleGraphic"/> with a fifth enum
    /// value. That class draws a RIGHT-triangle whose right-angle sits in a corner, and it
    /// shares <see cref="ResizeGripCorner"/> with <see cref="PanelResizeHandle"/> precisely so
    /// the glyph and the drag it advertises can never name different corners. Folding a
    /// dropdown caret into that enum would hand a resize gesture a value that means nothing to
    /// it, which is the coupling that guarantee exists to prevent.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class CaretGraphic : MaskableGraphic
    {
        [SerializeField, Tooltip("Which way the apex points.")]
        private CaretDirection direction = CaretDirection.Down;

        [SerializeField, Range(0.2f, 1f),
         Tooltip("Fraction of the rect the triangle fills, centred. Below 1 it gets padding.")]
        private float fill = 0.62f;

        public CaretDirection Direction
        {
            get => direction;
            set { direction = value; SetVerticesDirty(); }
        }

        /// <summary>
        /// Built from the rect's CENTRE outwards so the glyph stays centred whatever the host
        /// sizes it to — a caret is an ornament inside a control, not something anchored to an
        /// edge the way a resize grip is.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            float hw = r.width * fill * 0.5f;
            float hh = r.height * fill * 0.5f;
            float cx = r.center.x;
            float cy = r.center.y;

            Vector3 apex, baseA, baseB;
            switch (direction)
            {
                case CaretDirection.Up:
                    apex  = new Vector3(cx,      cy + hh, 0f);
                    baseA = new Vector3(cx - hw, cy - hh, 0f);
                    baseB = new Vector3(cx + hw, cy - hh, 0f);
                    break;
                case CaretDirection.Left:
                    apex  = new Vector3(cx - hw, cy,      0f);
                    baseA = new Vector3(cx + hw, cy + hh, 0f);
                    baseB = new Vector3(cx + hw, cy - hh, 0f);
                    break;
                case CaretDirection.Right:
                    apex  = new Vector3(cx + hw, cy,      0f);
                    baseA = new Vector3(cx - hw, cy - hh, 0f);
                    baseB = new Vector3(cx - hw, cy + hh, 0f);
                    break;
                default: // Down
                    apex  = new Vector3(cx,      cy - hh, 0f);
                    baseA = new Vector3(cx + hw, cy + hh, 0f);
                    baseB = new Vector3(cx - hw, cy + hh, 0f);
                    break;
            }

            vh.AddVert(baseA, color, Vector2.zero);
            vh.AddVert(apex,  color, Vector2.zero);
            vh.AddVert(baseB, color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
