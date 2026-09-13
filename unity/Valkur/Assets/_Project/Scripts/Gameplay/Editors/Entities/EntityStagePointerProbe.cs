using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Turns a click or a drag anywhere on the Animation panel's stage into a VIEWPORT point
    /// (0..1, origin bottom-left) — the space the preview camera's own
    /// <c>ViewportToWorldPoint</c> takes.
    ///
    /// <para>It converts and nothing else. The preview service knows about cameras and sprite
    /// bounds and knows nothing about RectTransforms; this knows about RectTransforms and
    /// nothing about muzzles. Putting the un-projection in the panel instead would have needed
    /// the panel to hold the camera, the frame's bounds and the facing rule, which is three
    /// things it has no other reason to know.</para>
    ///
    /// <para>Only armed while the author is actually placing a point. The stage is a
    /// <c>RawImage</c> in the middle of a draggable panel, so a permanently raycastable stage
    /// is a hole in that panel's own drag surface.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityStagePointerProbe : MonoBehaviour,
        IPointerDownHandler, IDragHandler
    {
        private RectTransform _rect;
        private Action<Vector2> _onPoint;

        public void Bind(Action<Vector2> onViewportPoint)
        {
            _rect = (RectTransform)transform;
            _onPoint = onViewportPoint;
        }

        public void OnPointerDown(PointerEventData eventData) => Report(eventData);

        public void OnDrag(PointerEventData eventData) => Report(eventData);

        private void Report(PointerEventData eventData)
        {
            if (_onPoint == null || _rect == null || eventData == null) return;

            // eventData.pressEventCamera is null for a Screen Space - Overlay canvas, which is
            // what every runtime editor uses, and that is the argument RectangleUtility wants
            // there. Passing Camera.main instead silently offsets every reading by the world
            // camera's own projection.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;

            Rect r = _rect.rect;
            if (r.width <= 0.0001f || r.height <= 0.0001f) return;

            // Local space is pivot-relative; the viewport wants 0..1 from the bottom-left.
            _onPoint(new Vector2((local.x - r.xMin) / r.width,
                                 (local.y - r.yMin) / r.height));
        }
    }
}
