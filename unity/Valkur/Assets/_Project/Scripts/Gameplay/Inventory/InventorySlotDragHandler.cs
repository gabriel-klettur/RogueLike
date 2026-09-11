using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Per-slot input for the inventory window, bag and equipment alike. Forwards to
    /// <see cref="InventoryUI"/>:
    /// <list type="bullet">
    /// <item>hover — the item card opens after a short rest;</item>
    /// <item>left click — select; double click — use or equip (the same verb right click has);</item>
    /// <item>right click — equip, take off or use, whichever the item supports;</item>
    /// <item>drag — move, swap or merge; Shift-drag takes half a stack, Ctrl-drag one; out of the
    /// window — drop into the world.</item>
    /// </list>
    /// </summary>
    public class InventorySlotDragHandler : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float DOUBLE_CLICK_SECONDS = 0.4f;

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
            if (Owner == null || ev.dragging) return;
            if (ev.button == PointerEventData.InputButton.Right)
            {
                Owner.ActivateSlot(SlotIndex);
                return;
            }
            if (ev.button != PointerEventData.InputButton.Left) return;

            float now = Time.unscaledTime;
            if (now - _lastClickTime <= DOUBLE_CLICK_SECONDS)
            {
                Owner.ActivateSlot(SlotIndex);
                _lastClickTime = -10f;
            }
            else
            {
                Owner.SelectSlot(SlotIndex);
                _lastClickTime = now;
            }
        }

        public void OnPointerEnter(PointerEventData ev) => Owner?.OnSlotPointer(SlotIndex, true);
        public void OnPointerExit(PointerEventData ev) => Owner?.OnSlotPointer(SlotIndex, false);

        public void OnBeginDrag(PointerEventData ev)
        {
            if (Owner == null || ev.button != PointerEventData.InputButton.Left) return;
            Owner.BeginSlotDrag(SlotIndex, ev);
        }

        public void OnDrag(PointerEventData ev) => Owner?.UpdateSlotDrag(ev);
        public void OnEndDrag(PointerEventData ev) => Owner?.EndSlotDrag(SlotIndex, ev);
    }
}
