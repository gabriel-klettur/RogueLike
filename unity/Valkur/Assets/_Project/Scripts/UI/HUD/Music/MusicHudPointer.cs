using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Relays every pointer event on its object to plain delegates, so the music panel's widgets
    /// stay plain classes an EditMode test can drive by calling the same delegates — Unity sends
    /// no pointer events in Edit Mode.
    ///
    /// <para>Only the delegates that are SET make the object a handler in practice: a widget
    /// that sets <see cref="Drag"/> captures the drag (the groove, the notches), and one that
    /// does not lets the drag reach the plaque, which moves the window. uGUI finds the drag
    /// handler by walking UP from the pressed object, so an unset delegate here would still
    /// swallow the drag — <see cref="CapturesDrag"/> is therefore a flag the owner states, and
    /// an object that does not capture forwards the drag to its parent's handler.</para>
    /// </summary>
    public sealed class MusicHudPointer : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler, IPointerMoveHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public Action Enter;
        public Action Exit;
        public Action<Vector2> Down;
        public Action<Vector2> Up;
        public Action Click;
        /// <summary>The pointer moving over the object, in its texels.</summary>
        public Action<Vector2> Move;
        public Action<Vector2> Drag;
        /// <summary>The same drag as a delta in SCREEN pixels, for moving the window itself.</summary>
        public Action<Vector2> DragDelta;
        public Action EndDrag;
        public Action<float> Scroll;

        /// <summary>When false, drags are forwarded up the hierarchy (to the window).</summary>
        public bool CapturesDrag;

        /// <summary>The last pointer position, in the owner's texel space (bottom-left origin).</summary>
        public static Vector2 LocalTexel(RectTransform rt, PointerEventData e)
        {
            if (rt == null || e == null) return Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera ?? e.enterEventCamera, out var local);
            var r = rt.rect;
            return new Vector2(local.x - r.xMin, local.y - r.yMin);
        }

        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        public void OnPointerDown(PointerEventData e) => Down?.Invoke(LocalTexel((RectTransform)transform, e));
        public void OnPointerUp(PointerEventData e) => Up?.Invoke(LocalTexel((RectTransform)transform, e));
        public void OnPointerMove(PointerEventData e) => Move?.Invoke(LocalTexel((RectTransform)transform, e));

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) Click?.Invoke();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!CapturesDrag) { Forward(e, ExecuteEvents.beginDragHandler); return; }
            Drag?.Invoke(LocalTexel((RectTransform)transform, e));
        }

        public void OnDrag(PointerEventData e)
        {
            if (!CapturesDrag) { Forward(e, ExecuteEvents.dragHandler); return; }
            Drag?.Invoke(LocalTexel((RectTransform)transform, e));
            DragDelta?.Invoke(e.delta);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!CapturesDrag) { Forward(e, ExecuteEvents.endDragHandler); return; }
            EndDrag?.Invoke();
        }

        public void OnScroll(PointerEventData e)
        {
            if (Scroll != null) { Scroll(e.scrollDelta.y); return; }
            Forward(e, ExecuteEvents.scrollHandler);
        }

        private void Forward<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn) where T : IEventSystemHandler
        {
            var parent = transform.parent;
            if (parent == null) return;
            var target = ExecuteEvents.GetEventHandler<T>(parent.gameObject);
            if (target != null) ExecuteEvents.Execute(target, e, fn);
        }
    }
}
