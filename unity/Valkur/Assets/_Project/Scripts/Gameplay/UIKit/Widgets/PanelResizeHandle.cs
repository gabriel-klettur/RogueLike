using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UIKit
{
    /// <summary>
    /// Which corner of the panel the grip sits in — which is the same question as which
    /// corner the panel's pivot is NOT in.
    ///
    /// <para>A grip can only pull the edges its panel is free to move. A pivot pins two of
    /// them: the panel grows away from the pivot and never towards it. So the grip belongs
    /// diagonally opposite the pivot, and the sign of the cursor delta follows from that —
    /// which is the whole of what this enum selects.</para>
    /// </summary>
    public enum ResizeGripCorner
    {
        /// <summary>
        /// Bottom-right grip on a top-left-pivoted panel: it grows right and DOWN. Every
        /// runtime editor panel is this, via <c>EditorUIHelpers.MakeDropPanel</c>, and it is
        /// the default so that adding this option changed none of them.
        /// </summary>
        BottomRight = 0,

        /// <summary>
        /// Top-right grip on a bottom-left-pivoted panel: it grows right and UP.
        ///
        /// <para>For a panel pinned near the BOTTOM of the screen, which is where a chat
        /// window belongs. Such a panel has its bottom edge nailed to the pivot, so a
        /// bottom-right grip could only ever change its width — dragging down would move an
        /// edge that cannot move.</para>
        /// </summary>
        TopRight = 1,
    }

    /// <summary>
    /// Drag-to-resize handle. Place this component (typically alongside a
    /// <see cref="TriangleHandleGraphic"/>) on a small child anchored to the resize corner
    /// of the panel you want to resize; assign the panel's <see cref="RectTransform"/> as
    /// <see cref="Target"/> and set <see cref="Corner"/> to match its pivot.
    ///
    /// Cursor delta in screen pixels maps directly to <c>sizeDelta</c>, with the Y axis
    /// signed by <see cref="Corner"/>.
    /// </summary>
    public class PanelResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField, Tooltip("RectTransform of the panel this handle resizes. Its pivot must be the corner opposite Corner.")]
        private RectTransform target;

        [SerializeField, Tooltip("Smallest size (width, height) the panel can shrink to.")]
        private Vector2 minSize = new Vector2(220f, 160f);

        [SerializeField, Tooltip("Largest size (width, height) the panel can grow to.")]
        private Vector2 maxSize = new Vector2(2400f, 1600f);

        [SerializeField, Tooltip("Which corner this grip occupies. Must be diagonally opposite the panel's pivot.")]
        private ResizeGripCorner corner = ResizeGripCorner.BottomRight;

        public RectTransform Target { get => target; set => target = value; }
        public Vector2 MinSize { get => minSize; set => minSize = value; }
        public Vector2 MaxSize { get => maxSize; set => maxSize = value; }
        public ResizeGripCorner Corner { get => corner; set => corner = value; }

        /// <summary>
        /// Raised once when the drag ENDS, with the size the panel settled on.
        ///
        /// <para>End rather than every frame, because the one thing a listener reliably wants
        /// to do here is persist the result, and a write per frame is a file write per frame.
        /// A listener that wants live feedback can read <see cref="Target"/> instead.</para>
        /// </summary>
        public event Action<Vector2> Resized;

        private Vector2 _startSize;
        private Vector2 _startPointer;

        public void OnPointerDown(PointerEventData ev)
        {
            if (target == null) return;
            _startSize    = target.sizeDelta;
            _startPointer = ev.position;
        }

        public void OnDrag(PointerEventData ev)
        {
            if (target == null) return;
            target.sizeDelta = Resolve(ev.position);
        }

        public void OnEndDrag(PointerEventData ev)
        {
            if (target == null) return;

            Vector2 size = Resolve(ev.position);
            target.sizeDelta = size;
            Resized?.Invoke(size);
        }

        /// <summary>
        /// The size the panel would take for a cursor at <paramref name="pointer"/>.
        ///
        /// Measured from where the drag STARTED rather than accumulated per frame, so a
        /// clamp against <see cref="MinSize"/> never eats travel: dragging well past the
        /// minimum and back returns the panel to the size the cursor is actually over,
        /// instead of leaving it offset by however far the clamp swallowed.
        /// </summary>
        private Vector2 Resolve(Vector2 pointer)
        {
            Vector2 delta = pointer - _startPointer;

            // The panel grows away from its pivot, so the axis that runs towards the pivot is
            // the one that inverts. X is the same for both corners because both are on the
            // right; only the vertical differs.
            float dy = corner == ResizeGripCorner.TopRight ? delta.y : -delta.y;

            Vector2 size = _startSize + new Vector2(delta.x, dy);
            size.x = Mathf.Clamp(size.x, minSize.x, maxSize.x);
            size.y = Mathf.Clamp(size.y, minSize.y, maxSize.y);
            return size;
        }
    }
}
