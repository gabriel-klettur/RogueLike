using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// What the player DOES to a slot: select, equip, take off, use, move, split, drop, sort —
    /// and what the window answers, including why it refused.
    /// </summary>
    public partial class InventoryUI
    {
        private int _dragSource = -1;
        private int _dragAmount;
        private ItemDefinition _dragItem;
        private int _dragHover = -1;
        private int _depositTarget = -1;
        private int _hoverSlot = -1;

        /// <summary>The unified index the equipment range starts at.</summary>
        private int EquipBase => _playerInventory != null ? _playerInventory.Capacity : _bagViews.Length;

        /// <summary>True while an item is being dragged out of a slot.</summary>
        public bool IsDragging => _dragSource >= 0;

        /// <summary>The view drawing unified index <paramref name="unified"/>, or null.</summary>
        internal InventorySlotView ViewFor(int unified)
        {
            if (unified < 0) return null;
            if (unified < _bagViews.Length) return _bagViews[unified];
            int e = unified - EquipBase;
            return e >= 0 && e < _equipViews.Length ? _equipViews[e] : null;
        }

        private InventorySlot SlotAt(int unified)
            => _playerInventory != null ? _playerInventory.GetSlotByIndex(unified) : default;

        // ── Activate: the one verb for right click and double click ────────

        /// <summary>Kept for callers that predate <see cref="ActivateSlot"/>.</summary>
        public void UseSlot(int slotIndex) => ActivateSlot(slotIndex);

        /// <summary>
        /// Does the obvious thing to the item in a slot: wears it, takes it off, or uses it.
        /// Refuses loudly (and says why) when none applies.
        /// </summary>
        public void ActivateSlot(int unified)
        {
            if (_playerInventory == null) return;
            var slot = SlotAt(unified);
            if (slot.IsEmpty) return;
            var item = slot.Item;
            bool equipped = _playerInventory.IsEquipmentIndex(unified);

            if (equipped)
            {
                BeginAction();
                int landed = _playerInventory.TryUnequip(unified - EquipBase);
                EndAction();
                if (landed >= 0) OnUnequipped(unified, landed, item);
                else Refuse(unified, EquipRefusal.BagFull, item);
                return;
            }

            int home = EquipmentLayout.IndexFor(item);
            if (home >= 0)
            {
                BeginAction();
                bool ok = _playerInventory.TryEquipFromBag(unified, out var why);
                EndAction();
                if (ok) OnEquipped(unified, EquipBase + home, item);
                else Refuse(unified, why, item);
                return;
            }

            if (item.GetCategory() == ItemCategory.Consumable && _playerConsumer != null)
            {
                BeginAction();
                bool consumed = _playerConsumer.TryConsume(item);
                EndAction();
                if (consumed) OnConsumed(unified, item);
                else Refuse(unified, EquipRefusal.None, item);
                return;
            }

            SelectSlot(unified);
        }

        // ── Hover ─────────────────────────────────────────────────────────

        public void OnSlotPointer(int unified, bool entered)
        {
            if (entered) SetHover(unified);
            else if (_hoverSlot == unified) SetHover(-1);
            if (IsDragging) _dragHover = entered ? unified : (_dragHover == unified ? -1 : _dragHover);
            UpdateMarks();
        }

        private void SetHover(int unified)
        {
            if (unified == _hoverSlot) return;
            _hoverSlot = unified;
            _hoverT = 0f;
            if (_cardSlot >= 0 && _cardSlot != unified) HideCard();
            // Looking at a new item is what makes it not new.
            var v = ViewFor(unified);
            if (v != null && v.IsNew) { v.SetNew(false, _theme); ClearNewFlag(unified); }
            UpdateMarks();
        }

        // ── Drag ──────────────────────────────────────────────────────────

        public void BeginSlotDrag(int unified, PointerEventData ev)
        {
            if (_playerInventory == null || _collapsed) return;
            var src = SlotAt(unified);
            if (src.IsEmpty) return;

            // Shift takes half the stack, Ctrl one; either way the source keeps the rest.
            int amount = src.Quantity;
            if (src.Item.stackable && src.Quantity > 1 && !_playerInventory.IsEquipmentIndex(unified))
            {
                if (KeyboardInputManager.IsShiftHeld()) amount = Mathf.Max(1, src.Quantity / 2);
                else if (KeyboardInputManager.IsCtrlHeld()) amount = 1;
            }

            _dragSource = unified;
            _dragItem = src.Item;
            _dragAmount = amount;
            _dragHover = unified;
            HideCard();

            ViewFor(unified)?.SetDragSource(true);
            ShowGhost(src.Item, amount < src.Quantity ? amount : src.Quantity);
            UpdateSlotDrag(ev);
            UpdateMarks();
        }

        public void UpdateSlotDrag(PointerEventData ev)
        {
            if (!IsDragging || ev == null) return;
            PlaceGhost(ev.position);
            int over = HitTestSlot(ev.position);
            if (over != _dragHover) { _dragHover = over; UpdateMarks(); }
        }

        public void EndSlotDrag(int unused, PointerEventData ev)
        {
            if (!IsDragging) return;
            int src = _dragSource;
            var item = _dragItem;
            int amount = _dragAmount;
            EndDragQuietly();
            if (_playerInventory == null || ev == null) return;

            var srcSlot = SlotAt(src);
            if (srcSlot.IsEmpty || srcSlot.Item != item) return;   // the bag changed under the drag
            bool partial = amount < srcSlot.Quantity;

            int dst = HitTestSlot(ev.position);
            if (dst >= 0 && dst != src)
            {
                bool srcEquip = _playerInventory.IsEquipmentIndex(src);
                bool dstEquip = _playerInventory.IsEquipmentIndex(dst);
                BeginAction();
                bool ok;
                if (partial && !dstEquip)
                    ok = _playerInventory.SplitStack(src, dst, amount) > 0;
                else
                    ok = _playerInventory.MoveSlotByIndex(src, dst);
                EndAction();

                if (!ok)
                {
                    Refuse(dst, partial ? EquipRefusal.None : _playerInventory.LastRefusal, item);
                    return;
                }
                if (dstEquip) OnEquipped(src, dst, item);
                else if (srcEquip) OnUnequipped(src, dst, item);
                else OnSettled(dst);
                SelectSlot(dst);
                return;
            }

            if (dst < 0 && !IsScreenPointOverPanel(ev.position))
                RequestWorldDrop(src, ResolveWorldDropPosition(ev), partial ? amount : 0);
        }

        /// <summary>
        /// Drops at once, or — for an Epic or Legendary item — asks first. The question sits over
        /// the grid, centred, until it is answered.
        /// </summary>
        internal void RequestWorldDrop(int unified, Vector3? worldPos, int amount)
        {
            var slot = SlotAt(unified);
            if (slot.IsEmpty) return;
            if (slot.Item.rarity < ItemRarity.Epic || _confirm == null)
            {
                DropSlotToWorld(unified, worldPos, amount);
                return;
            }
            var item = slot.Item;
            int qty = amount > 0 ? Mathf.Min(amount, slot.Quantity) : slot.Quantity;
            int x = (_widthTexels - 104) / 2;
            int y = _gridY + _style.GridHeightTexels(_bagRows) / 2 - 19;
            HideCard();
            _confirm.Ask(item, qty, _theme, x, y, () =>
            {
                // The bag may have changed while the question was up; drop only what is still there.
                var now = SlotAt(unified);
                if (!now.IsEmpty && now.Item == item) DropSlotToWorld(unified, worldPos, amount);
            });
        }

        /// <summary>Ends a drag without doing anything with it.</summary>
        private void EndDragQuietly()
        {
            if (_dragSource >= 0) ViewFor(_dragSource)?.SetDragSource(false);
            _dragSource = -1;
            _dragItem = null;
            _dragAmount = 0;
            _dragHover = -1;
            if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);
            UpdateMarks();
        }

        private void ShowGhost(ItemDefinition item, int quantity)
        {
            if (_ghostRoot == null) return;
            var sprite = item.icon != null ? item.icon : item.iconSmall;
            int inner = _style.slotTexels - _style.iconInsetTexels * 2;
            var baked = Valkur.UI.HUD.HudTextureBaker.Icon(sprite, inner * Mathf.Max(1, _pixelScale));
            _ghost.texture = baked;
            _ghost.enabled = baked != null;
            _ghostFallback.sprite = sprite;
            _ghostFallback.enabled = baked == null && sprite != null;
            if (_ghostCount != null) _ghostCount.SetText(quantity > 1 ? quantity.ToString() : "");
            _ghostRoot.SetAsLastSibling();
            _ghostRoot.gameObject.SetActive(true);
        }

        private void PlaceGhost(Vector2 screen)
        {
            if (_ghostRoot == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_pixels, screen, null, out var local)) return;
            int inner = _style.slotTexels - _style.iconInsetTexels * 2;
            // Lifted two texels above the pointer, as if held.
            _ghostRoot.anchoredPosition = new Vector2(Mathf.Floor(local.x - inner * 0.5f), Mathf.Floor(local.y - inner * 0.5f + 2f));
        }

        // ── Hit tests (also used by WorldDropInteractor) ───────────────────

        private int HitTestSlot(Vector2 screen)
        {
            for (int i = 0; i < _bagViews.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_bagViews[i].Root, screen, null))
                    return i;
            for (int i = 0; i < _equipViews.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_equipViews[i].Root, screen, null))
                    return EquipBase + i;
            return -1;
        }

        /// <summary>
        /// True when the window is open AND the given screen-space point falls inside it. Used by
        /// world-drop drags to detect a drop-into-inventory gesture.
        /// </summary>
        public bool IsScreenPointOverPanel(Vector2 screenPos)
        {
            if (!_visible || _panelRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_panelRect, screenPos, null);
        }

        /// <summary>
        /// The unified index of the slot under <paramref name="screenPos"/>, or -1 (also -1 when
        /// the window is closed or collapsed). Used by <c>WorldDropInteractor</c> to honour
        /// "deposit in the cell I want".
        /// </summary>
        public int HitTestSlotByScreenPos(Vector2 screenPos)
        {
            if (!_visible || _collapsed) return -1;
            return HitTestSlot(screenPos);
        }

        /// <summary>
        /// Marks the slot a world drag would deposit into. Pass -1 to clear. Idempotent and safe to
        /// call every frame.
        /// </summary>
        public void SetDepositTargetSlot(int slotIndex)
        {
            if (slotIndex == _depositTarget) return;
            _depositTarget = slotIndex;
            UpdateMarks();
        }

        // ── Marks: one pass decides every slot's highlight ─────────────────

        private void UpdateMarks()
        {
            if (!_built) return;
            for (int i = 0; i < _bagViews.Length; i++) _bagViews[i].SetMark(MarkFor(i), _theme);
            for (int i = 0; i < _equipViews.Length; i++) _equipViews[i].SetMark(MarkFor(EquipBase + i), _theme);
        }

        private InventorySlotMark MarkFor(int unified)
        {
            if (unified == _depositTarget) return InventorySlotMark.HoverTarget;
            if (IsDragging)
            {
                if (unified == _dragSource) return InventorySlotMark.None;
                bool can = _playerInventory != null && CanDropOn(unified);
                if (unified == _dragHover) return can ? InventorySlotMark.HoverTarget : InventorySlotMark.InvalidTarget;
                // Only the equipment slot the held item belongs in is lit ahead of time; lighting
                // all 25 bag slots gold would say nothing.
                if (can && _playerInventory.IsEquipmentIndex(unified)) return InventorySlotMark.ValidTarget;
                return InventorySlotMark.None;
            }
            if (unified == _selectedSlot) return InventorySlotMark.Selected;
            if (unified == _hoverSlot && !SlotAt(unified).IsEmpty) return InventorySlotMark.Hover;
            return InventorySlotMark.None;
        }

        private bool CanDropOn(int unified)
        {
            if (_dragItem == null) return false;
            if (!_playerInventory.CanPlaceAt(unified, _dragItem)) return false;
            // A swap puts the destination's item where the drag came from; that has to fit too.
            var d = SlotAt(unified);
            if (!d.IsEmpty && _playerInventory.IsEquipmentIndex(_dragSource) && d.Item != _dragItem)
                return _playerInventory.CanPlaceAt(_dragSource, d.Item);
            return true;
        }

        // ── World drop ───────────────────────────────────────────────────

        /// <param name="amount">How many to drop; 0 or less means the whole stack.</param>
        private void DropSlotToWorld(int unified, Vector3? worldDropPos, int amount = 0)
        {
            if (_playerInventory == null) return;
            var slot = SlotAt(unified);
            if (slot.IsEmpty) return;

            var item = slot.Item;
            int qty = amount > 0 ? Mathf.Min(amount, slot.Quantity) : slot.Quantity;
            BeginAction();
            int removed;
            if (_playerInventory.IsEquipmentIndex(unified))
            {
                _playerInventory.SetEquipmentSlot(unified - EquipBase, slot.Quantity - qty > 0 ? item : null,
                                                  slot.Quantity - qty);
                removed = qty;
            }
            else
            {
                int left = slot.Quantity - qty;
                _playerInventory.SetSlot(unified, left > 0 ? item : null, left);
                removed = qty;
            }
            EndAction();
            if (removed <= 0) return;

            var player = EntityRegistry.Player;
            if (player != null)
            {
                // A drag passes the clamped cursor position; the key path passes null and falls
                // back to a small random offset around the player so the drop doesn't stack on
                // the foot.
                Vector3 pos = worldDropPos
                              ?? player.transform.position
                                 + (Vector3)(Random.insideUnitCircle.normalized * 1.5f);
                DropSystem.SpawnDrop(item, removed, pos);
            }

            OnDropped(unified);
            if (_selectedSlot == unified && SlotAt(unified).IsEmpty) _selectedSlot = -1;
            UpdateMarks();
        }

        // Converts the pointer release position to a clamped world-space drop location, using the
        // player's WorldDropInteractor so the same reach bounds drag-from-ground and drop-to-ground.
        private Vector3 ResolveWorldDropPosition(PointerEventData ev)
        {
            var player = EntityRegistry.Player;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

            var cam = Camera.main;
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

        // ── Sort and filter ────────────────────────────────────────────────

        private void SortBag()
        {
            if (_playerInventory == null) return;
            BeginAction();
            bool moved = _playerInventory.SortBag();
            EndAction();
            _selectedSlot = -1;
            if (moved) OnSorted();
            UpdateMarks();
        }

        private void SetTab(int tab)
        {
            _activeTab = Mathf.Clamp(tab, 0, TabCategories.Length - 1);
            ApplyTabs();
            ApplyFilter();
        }

        private void ApplyTabs()
        {
            if (_tabFrame == null) return;
            for (int i = 0; i < _tabFrame.Length; i++)
            {
                bool on = i == _activeTab;
                _tabFrame[i].enabled = on;
                _tabGlyph[i].color = on ? _theme.gold : _theme.textDim;
            }
        }

        private void ApplyFilter()
        {
            if (_playerInventory == null) return;
            var slots = _playerInventory.Slots;
            for (int i = 0; i < _bagViews.Length; i++)
            {
                var item = i < slots.Count ? slots[i].Item : null;
                _bagViews[i].SetFiltered(item != null && !MatchesTab(item, _activeTab));
            }
        }

        internal static bool MatchesTab(ItemDefinition item, int tab)
        {
            if (item == null) return false;
            int cat = tab >= 0 && tab < TabCategories.Length ? TabCategories[tab] : -1;
            return cat < 0 || (int)item.GetCategory() == cat;
        }
    }
}
