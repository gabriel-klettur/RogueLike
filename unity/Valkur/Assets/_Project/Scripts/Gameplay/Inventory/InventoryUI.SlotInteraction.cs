using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.Inventory
{
    public partial class InventoryUI
    {
        // Drag state
        private GameObject _dragGhost;
        private Image      _dragGhostImg;
        private int        _dragSourceIndex = -1;
        private RectTransform _dragGhostRt;

        // ─────────────────────────────────────────────────────────────────────
        //  Slot interactions (called from InventorySlotDragHandler)
        // ─────────────────────────────────────────────────────────────────────

        public void UseSlot(int slotIndex)
        {
            if (_playerInventory == null) return;
            if (slotIndex < 0 || slotIndex >= _playerInventory.Slots.Count) return;
            var slot = _playerInventory.Slots[slotIndex];
            if (slot.IsEmpty) return;

            if (slot.Item.GetCategory() == ItemCategory.Consumable && _playerConsumer != null)
            {
                _playerConsumer.TryConsume(slot.Item);
                _selectedSlot = -1;
                UpdateSlotHighlights();
                UpdateTooltip();
            }
            else
            {
                // Non-consumable: just keep selection.
                SelectSlot(slotIndex);
            }
        }

        public void BeginSlotDrag(int srcIndex, PointerEventData ev)
        {
            if (_playerInventory == null) return;
            var src = _playerInventory.GetSlotByIndex(srcIndex);
            if (src.IsEmpty) return;

            _dragSourceIndex = srcIndex;
            CreateDragGhost(src.Item);
            UpdateSlotDrag(ev);
        }

        public void UpdateSlotDrag(PointerEventData ev)
        {
            if (_dragGhostRt == null) return;
            _dragGhostRt.position = ev.position;
        }

        // Drag end-routing across the unified slot space:
        //   • src ↔ dst within the bag           → existing merge / swap.
        //   • bag ↔ equipment, or eq ↔ eq        → MoveSlotByIndex (swap or
        //     stack-merge depending on item compatibility).
        //   • dst outside any slot but inside    → no-op (cancels the drag).
        //     the panel
        //   • dst outside the panel altogether   → world drop at cursor.
        public void EndSlotDrag(int srcIndex, PointerEventData ev)
        {
            DestroyDragGhost();
            if (_dragSourceIndex < 0) return;
            int src = _dragSourceIndex;
            _dragSourceIndex = -1;
            if (_playerInventory == null) return;

            int dst = HitTestSlot(ev);
            if (dst >= 0 && dst != src)
            {
                if (_playerInventory.IsEquipmentIndex(src) || _playerInventory.IsEquipmentIndex(dst))
                {
                    _playerInventory.MoveSlotByIndex(src, dst);
                }
                else if (!_playerInventory.TryMergeStacks(src, dst))
                {
                    _playerInventory.SwapSlots(src, dst);
                }
                SelectSlot(dst);
                return;
            }

            if (!IsPointerOverPanel(ev))
            {
                DropSlotToWorld(src, ResolveWorldDropPosition(ev));
            }
        }

        // Tests both grids (bag first, then equipment) and returns the unified
        // index — caller routes by Inventory.IsEquipmentIndex.
        private int HitTestSlot(PointerEventData ev)
        {
            if (_slotObjects != null)
            {
                for (int i = 0; i < _slotObjects.Length; i++)
                {
                    var rt = _slotObjects[i].GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, ev.position, ev.pressEventCamera))
                        return i;
                }
            }
            if (_equipObjects != null)
            {
                for (int i = 0; i < _equipObjects.Length; i++)
                {
                    var go = _equipObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, ev.position, ev.pressEventCamera))
                        return Inventory.DefaultBagCapacity + i;
                }
            }
            return -1;
        }

        private bool IsPointerOverPanel(PointerEventData ev)
        {
            if (_panelRect == null) return true;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, ev.position, ev.pressEventCamera);
        }

        /// <summary>
        /// True when the inventory window is open AND the given screen-space point
        /// falls inside the panel. Used by world-drop drag systems to detect a
        /// drop-into-inventory gesture without going through PointerEventData.
        /// Canvas is ScreenSpaceOverlay so the camera arg is null.
        /// </summary>
        public bool IsScreenPointOverPanel(Vector2 screenPos)
        {
            if (!_visible || _panelRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screenPos, null);
        }

        /// <summary>
        /// Returns the index of the slot whose rect contains <paramref name="screenPos"/>,
        /// or -1 if no slot is hit (or the panel isn't visible). Used by
        /// <c>WorldDropInteractor</c> to honour "deposit in the cell I want".
        /// </summary>
        public int HitTestSlotByScreenPos(Vector2 screenPos)
        {
            if (!_visible) return -1;
            if (_slotObjects != null)
            {
                for (int i = 0; i < _slotObjects.Length; i++)
                {
                    var go = _slotObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                        return i;
                }
            }
            if (_equipObjects != null)
            {
                for (int i = 0; i < _equipObjects.Length; i++)
                {
                    var go = _equipObjects[i];
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                        return Inventory.DefaultBagCapacity + i;
                }
            }
            return -1;
        }

        // Yellow border drawn on the slot that AddItem would deposit into,
        // refreshed every frame by WorldDropInteractor while a world drag is
        // active. Reuses each slot's existing Outline component to avoid extra
        // GameObjects — only color/distance get swapped.
        private int _depositTargetSlot = -1;
        private static readonly Color s_depositTargetColor    = new Color(1.00f, 0.86f, 0.20f, 1f);
        private static readonly Vector2 s_depositTargetOffset = new Vector2(3f, 3f);

        /// <summary>
        /// Tag a slot as the current deposit target so it stands out with a
        /// yellow border. Pass -1 to clear. Slot indices outside the grid are
        /// ignored. Idempotent and per-frame safe.
        /// </summary>
        public void SetDepositTargetSlot(int slotIndex)
        {
            if (slotIndex == _depositTargetSlot) return;

            ResetSlotOutline(_depositTargetSlot);
            _depositTargetSlot = slotIndex;
            ApplyDepositOutline(_depositTargetSlot);
        }

        private Outline GetOutlineByIndex(int unifiedIndex)
        {
            if (unifiedIndex < 0) return null;
            if (unifiedIndex < Inventory.DefaultBagCapacity)
                return (_slotOutlines != null && unifiedIndex < _slotOutlines.Length)
                    ? _slotOutlines[unifiedIndex] : null;
            int eq = unifiedIndex - Inventory.DefaultBagCapacity;
            return (_equipOutlines != null && eq >= 0 && eq < _equipOutlines.Length)
                ? _equipOutlines[eq] : null;
        }

        private void ResetSlotOutline(int unifiedIndex)
        {
            var ol = GetOutlineByIndex(unifiedIndex);
            if (ol == null) return;
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(1f, 1f);
        }

        private void ApplyDepositOutline(int unifiedIndex)
        {
            var ol = GetOutlineByIndex(unifiedIndex);
            if (ol == null) return;
            ol.effectColor    = s_depositTargetColor;
            ol.effectDistance = s_depositTargetOffset;
        }

        private void DropSlotToWorld(int srcIndex, Vector3? worldDropPos = null)
        {
            if (_playerInventory == null) return;
            if (srcIndex < 0 || srcIndex >= _playerInventory.Slots.Count) return;
            var slot = _playerInventory.Slots[srcIndex];
            if (slot.IsEmpty) return;

            var item = slot.Item;
            int qty  = slot.Quantity;
            int removed = _playerInventory.RemoveItem(item, qty);
            if (removed <= 0) return;

            var player = EntityRegistry.Player;
            if (player != null)
            {
                // Drag-from-inventory passes the (clamped) cursor world position;
                // the Q-key path passes null and falls back to a small random
                // offset around the player so the drop doesn't stack on the foot.
                Vector3 pos = worldDropPos
                              ?? player.transform.position
                                 + (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                DropSystem.SpawnDrop(item, removed, pos);
            }

            _selectedSlot = -1;
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        // Convert the pointer release position to a clamped world-space drop
        // location. Uses the player's WorldDropInteractor to enforce the same
        // interaction range that bounds drag-from-ground, so the player can
        // always reach back to whatever they just placed.
        private Vector3 ResolveWorldDropPosition(PointerEventData ev)
        {
            var player = EntityRegistry.Player;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

            var cam = ev.pressEventCamera != null ? ev.pressEventCamera : Camera.main;
            if (cam == null) return playerPos;

            Vector3 sp = new Vector3(ev.position.x, ev.position.y, -cam.transform.position.z);
            Vector3 worldCursor = cam.ScreenToWorldPoint(sp);
            worldCursor.z = 0f;

            if (player != null)
            {
                var interactor = player.GetComponent<WorldDropInteractor>();
                if (interactor != null) return interactor.ClampToReach(worldCursor);
            }
            return worldCursor;
        }

        private void CreateDragGhost(ItemDefinition item)
        {
            if (item == null || _canvas == null) return;
            DestroyDragGhost();

            _dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup));
            _dragGhost.transform.SetParent(_canvas.transform, false);
            _dragGhostRt = _dragGhost.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(SLOT_PX, SLOT_PX);

            var cg = _dragGhost.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.alpha = 0.85f;

            _dragGhostImg = _dragGhost.AddComponent<Image>();
            _dragGhostImg.sprite         = item.icon ?? item.iconSmall;
            _dragGhostImg.preserveAspect = true;
            _dragGhostImg.raycastTarget  = false;
        }

        private void DestroyDragGhost()
        {
            if (_dragGhost != null) Destroy(_dragGhost);
            _dragGhost    = null;
            _dragGhostImg = null;
            _dragGhostRt  = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Drop selected (Q key)
        // ─────────────────────────────────────────────────────────────────────

        private void DropSelectedItem()
        {
            DropSlotToWorld(_selectedSlot);
        }

        private void OnDisable()
        {
            _toggleAction?.Disable();
            _dropAction?.Disable();
        }

        protected override void OnDestroy()
        {
            UnsubscribePlayer();
            DestroyDragGhost();
            _toggleAction?.Dispose();
            _dropAction?.Dispose();
            base.OnDestroy();
        }
    }
}
