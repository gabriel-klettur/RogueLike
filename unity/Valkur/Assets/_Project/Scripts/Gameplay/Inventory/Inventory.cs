using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Runtime inventory matching Python's InventoryPlayerSchema.
    ///
    /// Storage model: a fixed-size <see cref="List{T}"/> of length
    /// <see cref="Capacity"/>. Empty visual cells are represented by
    /// <see cref="InventorySlot.IsEmpty"/> entries — they are NOT removed
    /// from the list, so visual slot positions are stable across add /
    /// remove / swap operations. This is what lets the UI honour
    /// "drop in the cell I want": cells 0 and 5 can both be filled while
    /// cells 1..4 stay empty.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        /// <summary>Number of equipment slots (paper-doll 3×3).</summary>
        public const int EquipmentCapacity = 9;

        /// <summary>Default bag capacity matching the UI's 5×5 grid.</summary>
        public const int DefaultBagCapacity = 25;

        [SerializeField] private int capacity = DefaultBagCapacity;

        private readonly List<InventorySlot> _slots = new List<InventorySlot>();
        private readonly List<InventorySlot> _equipSlots =
            new List<InventorySlot>(EquipmentCapacity);

        public int Capacity => capacity;
        public IReadOnlyList<InventorySlot> Slots => _slots;
        public IReadOnlyList<InventorySlot> EquipmentSlots => _equipSlots;

        /// <summary>Number of non-empty slots. Always &lt;= Capacity.</summary>
        public int UsedSlots
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Count; i++)
                    if (!_slots[i].IsEmpty) n++;
                return n;
            }
        }

        public bool IsFull => UsedSlots >= capacity;

        public event Action OnInventoryChanged;

        /// <summary>
        /// Resize the inventory and reset every slot to empty. Called by both
        /// runtime spawn and save-restore (which then fills specific slots via
        /// <see cref="SetSlot"/>).
        /// </summary>
        public void Initialize(int cap)
        {
            capacity = cap;
            _slots.Clear();
            for (int i = 0; i < cap; i++) _slots.Add(default);
            _equipSlots.Clear();
            for (int i = 0; i < EquipmentCapacity; i++) _equipSlots.Add(default);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Add an item using auto-pick semantics: stack into existing partial
        /// stacks first, then place into the lowest-index empty cell. Returns
        /// the quantity that could NOT be added (overflow).
        /// </summary>
        public int AddItem(ItemDefinition item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return quantity;

            EnsureSlotsSized();
            int remaining = quantity;

            if (item.stackable)
            {
                for (int i = 0; i < _slots.Count && remaining > 0; i++)
                {
                    if (_slots[i].Item == item && _slots[i].Quantity < item.maxStack)
                    {
                        int canAdd = Mathf.Min(remaining, item.maxStack - _slots[i].Quantity);
                        _slots[i] = new InventorySlot(item, _slots[i].Quantity + canAdd);
                        remaining -= canAdd;
                    }
                }
            }

            // Fill the first empty visual cells until we run out of room.
            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                int stackSize = item.stackable ? Mathf.Min(remaining, item.maxStack) : 1;
                _slots[i] = new InventorySlot(item, stackSize);
                remaining -= stackSize;
            }

            if (remaining < quantity)
                OnInventoryChanged?.Invoke();

            return remaining;
        }

        /// <summary>
        /// Place up to <paramref name="quantity"/> of <paramref name="item"/>
        /// at the explicit visual <paramref name="slotIndex"/>. Used by the
        /// "drop on the cell I want" gesture in <c>WorldDropInteractor</c>.
        ///
        /// Rules:
        ///   • Empty slot → place there (clamped to maxStack for stackables).
        ///   • Same item &amp; stackable &amp; has room → stack into it.
        ///   • Different item / non-stackable mismatch → reject (returns 0).
        ///
        /// Returns the quantity actually deposited (0..quantity). Caller is
        /// expected to handle leftover (typically by routing through
        /// <see cref="AddItem"/>).
        /// </summary>
        public int TryDepositInSlot(int slotIndex, ItemDefinition item, int quantity)
        {
            if (item == null || quantity <= 0) return 0;
            if (slotIndex < 0 || slotIndex >= capacity) return 0;
            EnsureSlotsSized();

            int placed;
            var current = _slots[slotIndex];
            if (current.IsEmpty)
            {
                placed = item.stackable ? Mathf.Min(quantity, Mathf.Max(1, item.maxStack)) : 1;
                _slots[slotIndex] = new InventorySlot(item, placed);
            }
            else if (current.Item == item && item.stackable)
            {
                int room = Mathf.Max(0, item.maxStack - current.Quantity);
                placed = Mathf.Min(quantity, room);
                if (placed <= 0) return 0;
                _slots[slotIndex] = new InventorySlot(item, current.Quantity + placed);
            }
            else
            {
                return 0;
            }

            OnInventoryChanged?.Invoke();
            return placed;
        }

        /// <summary>
        /// Direct setter used by the save-restore path so items reload at the
        /// same visual indices they were saved at. Bypasses stack/auto-pick
        /// rules — caller is responsible for valid arguments.
        /// </summary>
        public void SetSlot(int slotIndex, ItemDefinition item, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= capacity) return;
            EnsureSlotsSized();
            _slots[slotIndex] = (item != null && quantity > 0)
                ? new InventorySlot(item, quantity)
                : default;
            OnInventoryChanged?.Invoke();
        }

        // ── Equipment slots ────────────────────────────────────────────────

        public void SetEquipmentSlot(int equipIndex, ItemDefinition item, int quantity)
        {
            if (equipIndex < 0 || equipIndex >= EquipmentCapacity) return;
            EnsureEquipSized();
            _equipSlots[equipIndex] = (item != null && quantity > 0)
                ? new InventorySlot(item, quantity)
                : default;
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Place up to <paramref name="quantity"/> of <paramref name="item"/>
        /// at the given equipment slot. Returns the count actually placed.
        /// Same rules as <see cref="TryDepositInSlot"/>: empty cell accepts any
        /// item; same-item stackable cell stacks until <c>maxStack</c>; mismatch
        /// rejects (returns 0). Equipment cells do not auto-fill — only manual
        /// drag drops items here.
        /// </summary>
        public int TryDepositInEquipmentSlot(int equipIndex, ItemDefinition item, int quantity)
        {
            if (item == null || quantity <= 0) return 0;
            if (equipIndex < 0 || equipIndex >= EquipmentCapacity) return 0;
            EnsureEquipSized();

            int placed;
            var current = _equipSlots[equipIndex];
            if (current.IsEmpty)
            {
                placed = item.stackable ? Mathf.Min(quantity, Mathf.Max(1, item.maxStack)) : 1;
                _equipSlots[equipIndex] = new InventorySlot(item, placed);
            }
            else if (current.Item == item && item.stackable)
            {
                int room = Mathf.Max(0, item.maxStack - current.Quantity);
                placed = Mathf.Min(quantity, room);
                if (placed <= 0) return 0;
                _equipSlots[equipIndex] = new InventorySlot(item, current.Quantity + placed);
            }
            else
            {
                return 0;
            }

            OnInventoryChanged?.Invoke();
            return placed;
        }

        // ── Unified index-space helpers ────────────────────────────────────
        // Visual slots are addressed by a single int across the panel:
        //   • [0 .. Capacity-1)             → bag.
        //   • [Capacity .. Capacity+Equip)  → equipment.
        // This lets WorldDropInteractor / drag handlers treat the panel as
        // one grid and route deposits without an extra "kind" parameter.

        public bool IsEquipmentIndex(int unifiedIndex)
            => unifiedIndex >= capacity && unifiedIndex < capacity + EquipmentCapacity;

        public InventorySlot GetSlotByIndex(int unifiedIndex)
        {
            if (unifiedIndex >= 0 && unifiedIndex < capacity)
                return _slots[unifiedIndex];
            if (IsEquipmentIndex(unifiedIndex))
                return _equipSlots[unifiedIndex - capacity];
            return default;
        }

        /// <summary>
        /// Index-space-aware deposit. Routes to <see cref="TryDepositInSlot"/>
        /// or <see cref="TryDepositInEquipmentSlot"/> depending on the unified
        /// index range. Returns the count placed.
        /// </summary>
        public int TryDepositInIndex(int unifiedIndex, ItemDefinition item, int quantity)
        {
            if (unifiedIndex < 0) return 0;
            if (unifiedIndex < capacity) return TryDepositInSlot(unifiedIndex, item, quantity);
            if (IsEquipmentIndex(unifiedIndex))
                return TryDepositInEquipmentSlot(unifiedIndex - capacity, item, quantity);
            return 0;
        }

        /// <summary>
        /// Move the entire stack at <paramref name="src"/> into <paramref name="dst"/>,
        /// across both bag and equipment slots. If <paramref name="dst"/> is
        /// non-empty: stack-merge if compatible, else swap. Returns true on
        /// any change. Used by the in-panel drag-and-drop handler.
        /// </summary>
        public bool MoveSlotByIndex(int src, int dst)
        {
            if (src == dst) return false;
            EnsureSlotsSized();
            EnsureEquipSized();

            if (!IsValidUnifiedIndex(src) || !IsValidUnifiedIndex(dst)) return false;

            var s = GetSlotByIndex(src);
            if (s.IsEmpty) return false;

            var d = GetSlotByIndex(dst);

            // Stack-merge same-item stackables.
            if (!d.IsEmpty && d.Item == s.Item && s.Item.stackable)
            {
                int cap = Mathf.Max(1, s.Item.maxStack);
                int room = cap - d.Quantity;
                if (room > 0)
                {
                    int moved = Mathf.Min(s.Quantity, room);
                    WriteSlotByIndex(dst, new InventorySlot(d.Item, d.Quantity + moved));
                    int srcLeft = s.Quantity - moved;
                    WriteSlotByIndex(src, srcLeft <= 0 ? default : new InventorySlot(s.Item, srcLeft));
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            // Otherwise raw swap (works for empty dst too).
            WriteSlotByIndex(dst, s);
            WriteSlotByIndex(src, d);
            OnInventoryChanged?.Invoke();
            return true;
        }

        private bool IsValidUnifiedIndex(int idx)
            => (idx >= 0 && idx < capacity) || IsEquipmentIndex(idx);

        private void WriteSlotByIndex(int idx, InventorySlot slot)
        {
            if (idx >= 0 && idx < capacity) _slots[idx] = slot;
            else if (IsEquipmentIndex(idx)) _equipSlots[idx - capacity] = slot;
        }

        /// <summary>
        /// Remove quantity of an item starting from the highest visual index.
        /// Returns actual amount removed.
        /// </summary>
        public int RemoveItem(ItemDefinition item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return 0;

            int toRemove = quantity;

            for (int i = _slots.Count - 1; i >= 0 && toRemove > 0; i--)
            {
                if (_slots[i].Item != item) continue;

                int canRemove = Mathf.Min(toRemove, _slots[i].Quantity);
                int newQty = _slots[i].Quantity - canRemove;

                _slots[i] = (newQty <= 0)
                    ? default
                    : new InventorySlot(item, newQty);

                toRemove -= canRemove;
            }

            int removed = quantity - toRemove;
            if (removed > 0)
                OnInventoryChanged?.Invoke();

            return removed;
        }

        public bool HasItem(ItemDefinition item, int quantity = 1)
            => GetItemCount(item) >= quantity;

        public int GetItemCount(ItemDefinition item)
        {
            if (item == null) return 0;
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Item == item)
                    count += _slots[i].Quantity;
            }
            return count;
        }

        /// <summary>
        /// Returns the visual slot index where <see cref="AddItem"/> would
        /// deposit (or start depositing) the given item / quantity. Used by the
        /// UI to telegraph the destination slot while a world drag is in
        /// progress. Returns -1 when the inventory has no room for it.
        /// </summary>
        public int PredictAddTargetSlot(ItemDefinition item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return -1;

            if (item.stackable)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Item == item && _slots[i].Quantity < item.maxStack)
                        return i;
                }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty) return i;
            }
            return -1;
        }

        /// <summary>
        /// Swap two slots by visual index. Either side may be empty.
        /// Returns true if anything changed.
        /// </summary>
        public bool SwapSlots(int a, int b)
        {
            if (a == b) return false;
            if (a < 0 || b < 0 || a >= capacity || b >= capacity) return false;
            EnsureSlotsSized();
            (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Try to merge stack at <paramref name="src"/> into <paramref name="dst"/>
        /// when both hold the same stackable item. Honours <c>maxStack</c>.
        /// Returns true if any quantity was moved.
        /// </summary>
        public bool TryMergeStacks(int src, int dst)
        {
            if (src == dst) return false;
            if (src < 0 || dst < 0 || src >= capacity || dst >= capacity) return false;
            EnsureSlotsSized();

            var s = _slots[src];
            var d = _slots[dst];
            if (s.IsEmpty || d.IsEmpty) return false;
            if (s.Item != d.Item || !s.Item.stackable) return false;

            int cap = Mathf.Max(1, s.Item.maxStack);
            if (d.Quantity >= cap) return false;

            int canMove = Mathf.Min(s.Quantity, cap - d.Quantity);
            if (canMove <= 0) return false;

            _slots[dst] = new InventorySlot(d.Item, d.Quantity + canMove);
            int newSrcQty = s.Quantity - canMove;
            _slots[src] = (newSrcQty <= 0)
                ? default
                : new InventorySlot(s.Item, newSrcQty);

            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Serialise to <see cref="InventoryData"/>. Length of
        /// <c>data.slots</c> equals <see cref="Capacity"/> so each entry
        /// preserves its visual index across save/load (empty cells stay
        /// empty, non-empty cells stay where the player put them).
        /// </summary>
        public InventoryData ToSaveData(string playerId)
        {
            EnsureSlotsSized();
            EnsureEquipSized();
            var data = new InventoryData
            {
                playerId      = playerId,
                capacity      = capacity,
                schemaVersion = "2.0"
            };

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                data.slots.Add(new InventorySlotData
                {
                    itemId   = slot.Item != null ? slot.Item.itemId : "",
                    quantity = slot.IsEmpty ? 0 : slot.Quantity,
                    stackId  = ""
                });
            }

            for (int i = 0; i < _equipSlots.Count; i++)
            {
                var slot = _equipSlots[i];
                data.equipmentSlots.Add(new InventorySlotData
                {
                    itemId   = slot.Item != null ? slot.Item.itemId : "",
                    quantity = slot.IsEmpty ? 0 : slot.Quantity,
                    stackId  = ""
                });
            }

            return data;
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Count; i++)      _slots[i]      = default;
            for (int i = 0; i < _equipSlots.Count; i++) _equipSlots[i] = default;
            OnInventoryChanged?.Invoke();
        }

        // Defensive: callers that bypassed Initialize would otherwise see a
        // zero-length list. Keeps every other method honest about list length.
        private void EnsureSlotsSized()
        {
            if (_slots.Count == capacity) return;
            if (_slots.Count > capacity) _slots.RemoveRange(capacity, _slots.Count - capacity);
            while (_slots.Count < capacity) _slots.Add(default);
        }

        private void EnsureEquipSized()
        {
            if (_equipSlots.Count == EquipmentCapacity) return;
            if (_equipSlots.Count > EquipmentCapacity)
                _equipSlots.RemoveRange(EquipmentCapacity, _equipSlots.Count - EquipmentCapacity);
            while (_equipSlots.Count < EquipmentCapacity) _equipSlots.Add(default);
        }
    }

    [Serializable]
    public struct InventorySlot
    {
        public ItemDefinition Item;
        public int Quantity;

        public InventorySlot(ItemDefinition item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        public bool IsEmpty => Item == null || Quantity <= 0;
    }
}
