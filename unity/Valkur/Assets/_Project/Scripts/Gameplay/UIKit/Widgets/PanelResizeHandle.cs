using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UIKit
{
    /// <summary>
    /// Drag-to-resize handle. Place this component (typically alongside a
    /// <see cref="TriangleHandleGraphic"/>) on a small child anchored to the
    /// bottom-right corner of the panel you want to resize; assign the panel's
    /// <see cref="RectTransform"/> as <see cref="Target"/>.
    ///
    /// Designed for panels with <c>pivot = (0, 1)</c> (top-left), which is what
    /// every Items / Tile / Buildings runtime editor panel uses via
    /// <c>EditorUIHelpers.MakeDropPanel</c>. Cursor delta in screen pixels maps
    /// directly to <c>sizeDelta</c> with a Y-axis flip.
    /// </summary>
    public class PanelResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField, Tooltip("RectTransform of the panel this handle resizes. Must use top-left pivot.")]
        private RectTransform target;

        [SerializeField, Tooltip("Smallest size (width, height) the panel can shrink to.")]
        private Vector2 minSize = new Vector2(220f, 160f);

        [SerializeField, Tooltip("Largest size (width, height) the panel can grow to.")]
        private Vector2 maxSize = new Vector2(2400f, 1600f);

        public RectTransform Target { get => target; set => target = value; }
        public Vector2 MinSize { get => minSize; set => minSize = value; }
        public Vector2 MaxSize { get => maxSize; set => maxSize = value; }

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

            Vector2 delta = ev.position - _startPointer;
            Vector2 size  = _startSize + new Vector2(delta.x, -delta.y);
            size.x = Mathf.Clamp(size.x, minSize.x, maxSize.x);
            size.y = Mathf.Clamp(size.y, minSize.y, maxSize.y);

            target.sizeDelta = size;
        }
    }
}
