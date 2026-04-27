using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Per-slot input handler attached to each main-grid slot in the inventory UI.
    /// Forwards click / double-click / drag callbacks to <see cref="InventoryUI"/>.
    /// Mirrors Python's combined click+drag behaviour
    /// (single-click selects, double-click within 500 ms uses the item,
    /// drag-onto-another-slot swaps/merges, drop outside panel = world drop).
    /// </summary>
    public class InventorySlotDragHandler : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float DOUBLE_CLICK_SECONDS = 0.5f;

        public int SlotIndex { get; private set; }
        public InventoryUI Owner { get; private set; }

        private float _lastClickTime = -10f;

        public void Bind(InventoryUI owner, int slotIndex)
        {
            Owner = owner;
            SlotIndex = slotIndex;
        }

        public void OnPointerClick(PointerEventData ev)
        {
            if (Owner == null) return;
            if (ev.button != PointerEventData.InputButton.Left) return;

            float now = Time.unscaledTime;
            if (now - _lastClickTime <= DOUBLE_CLICK_SECONDS)
            {
                Owner.UseSlot(SlotIndex);
                _lastClickTime = -10f;
            }
            else
            {
                Owner.SelectSlot(SlotIndex);
                _lastClickTime = now;
            }
        }

        public void OnBeginDrag(PointerEventData ev)
        {
            if (Owner == null) return;
            if (ev.button != PointerEventData.InputButton.Left) return;
            Owner.BeginSlotDrag(SlotIndex, ev);
        }

        public void OnDrag(PointerEventData ev)
        {
            if (Owner == null) return;
            Owner.UpdateSlotDrag(ev);
        }

        public void OnEndDrag(PointerEventData ev)
        {
            if (Owner == null) return;
            Owner.EndSlotDrag(SlotIndex, ev);
        }
    }
}
