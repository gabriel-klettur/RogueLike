using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Forwards pointer events on one of the window's controls to delegates — the title bar's
    /// drag, a button's click and hover. One small relay rather than a MonoBehaviour per control
    /// kind, so every control reacts to the pointer the same way.
    /// </summary>
    public sealed class InventoryPointerRelay : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float DoubleClickSeconds = 0.4f;

        public Action<PointerEventData> Enter;
        public Action<PointerEventData> Exit;
        public Action<PointerEventData> Click;
        public Action<PointerEventData> DoubleClick;
        public Action<PointerEventData> BeginDrag;
        public Action<PointerEventData> Drag;
        public Action<PointerEventData> EndDrag;

        private float _lastClick = -10f;

        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke(e);
        public void OnPointerExit(PointerEventData e) => Exit?.Invoke(e);

        public void OnPointerClick(PointerEventData e)
        {
            Click?.Invoke(e);
            if (DoubleClick == null || e.button != PointerEventData.InputButton.Left) return;
            float now = Time.unscaledTime;
            if (now - _lastClick <= DoubleClickSeconds)
            {
                _lastClick = -10f;
                DoubleClick(e);
            }
            else _lastClick = now;
        }

        public void OnBeginDrag(PointerEventData e) => BeginDrag?.Invoke(e);
        public void OnDrag(PointerEventData e) => Drag?.Invoke(e);
        public void OnEndDrag(PointerEventData e) => EndDrag?.Invoke(e);
    }
}
