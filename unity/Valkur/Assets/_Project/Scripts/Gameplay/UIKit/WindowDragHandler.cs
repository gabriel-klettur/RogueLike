using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.UIKit
{
    /// <summary>
    /// Generic "Windows-style" window drag handler.
    /// Attach to the chrome / header strip of a panel; set <see cref="Target"/>
    /// to the panel's RectTransform. Dragging the header moves the target.
    /// Optionally constrains the panel inside the parent canvas rect.
    /// </summary>
    public class WindowDragHandler : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        public RectTransform Target { get; set; }
        public bool ClampToParent { get; set; } = true;

        private Vector2 _grabOffset;
        private Canvas  _canvas;

        public void OnPointerDown(PointerEventData ev)
        {
            // Bring window to front on click.
            if (Target != null) Target.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData ev)
        {
            if (Target == null) return;
            _canvas = Target.GetComponentInParent<Canvas>();

            Vector2 localMouse;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Target.parent as RectTransform, ev.position, ev.pressEventCamera, out localMouse);
            _grabOffset = (Vector2)Target.localPosition - localMouse;
        }

        public void OnDrag(PointerEventData ev)
        {
            if (Target == null) return;
            var parentRt = Target.parent as RectTransform;
            if (parentRt == null) return;

            Vector2 localMouse;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRt, ev.position, ev.pressEventCamera, out localMouse))
                return;

            Vector2 next = localMouse + _grabOffset;
            if (ClampToParent) next = Clamp(next, parentRt);
            Target.localPosition = new Vector3(next.x, next.y, Target.localPosition.z);
        }

        public void OnEndDrag(PointerEventData ev) { }

        private Vector2 Clamp(Vector2 desired, RectTransform parent)
        {
            if (Target == null) return desired;
            Vector2 size  = Target.rect.size;
            Vector2 piv   = Target.pivot;
            Vector2 pSize = parent.rect.size;
            Vector2 pPiv  = parent.pivot;

            float minX = -pSize.x * pPiv.x       + size.x * piv.x;
            float maxX =  pSize.x * (1f - pPiv.x) - size.x * (1f - piv.x);
            float minY = -pSize.y * pPiv.y       + size.y * piv.y;
            float maxY =  pSize.y * (1f - pPiv.y) - size.y * (1f - piv.y);

            return new Vector2(Mathf.Clamp(desired.x, minX, maxX),
                               Mathf.Clamp(desired.y, minY, maxY));
        }
    }
}
