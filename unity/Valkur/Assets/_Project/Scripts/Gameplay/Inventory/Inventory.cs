using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Runtime inventory matching Python's InventoryPlayerSchema.
    /// Supports slots with stacking, capacity limits, and serialization to SaveData.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [SerializeField] private int capacity = 20;

        private readonly List<InventorySlot> _slots = new List<InventorySlot>();

        public int Capacity => capacity;
        public int UsedSlots => _slots.Count;
        public IReadOnlyList<InventorySlot> Slots => _slots;

        public event Action OnInventoryChanged;

        public void Initialize(int cap)
        {
            capacity = cap;
            _slots.Clear();
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Add an item. Returns the quantity that could NOT be added (overflow).
        /// </summary>
        public int AddItem(ItemDefinition item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return quantity;

            int remaining = quantity;

            // Try stacking into existing slots
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

            // Add to new slots
            while (remaining > 0 && _slots.Count < capacity)
            {
                int stackSize = item.stackable ? Mathf.Min(remaining, item.maxStack) : 1;
                _slots.Add(new InventorySlot(item, stackSize));
                remaining -= stackSize;
            }

            if (remaining < quantity)
                OnInventoryChanged?.Invoke();

            return remaining;
        }

        /// <summary>
        /// Remove quantity of an item. Returns actual amount removed.
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

                if (newQty <= 0)
                    _slots.RemoveAt(i);
                else
                    _slots[i] = new InventorySlot(item, newQty);

                toRemove -= canRemove;
            }

            int removed = quantity - toRemove;
            if (removed > 0)
                OnInventoryChanged?.Invoke();

            return removed;
        }

        public bool HasItem(ItemDefinition item, int quantity = 1)
        {
            if (item == null) return false;
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Item == item)
                    count += _slots[i].Quantity;
            }
            return count >= quantity;
        }

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

        public bool IsFull => _slots.Count >= capacity;

        /// <summary>
        /// Swap two slots by visual index. Indices outside the live list are
        /// treated as empty cells (compact-list semantics: dropping onto an
        /// empty visual cell is a no-op for now).
        /// Returns true if anything changed.
        /// </summary>
        public bool SwapSlots(int a, int b)
        {
            if (a == b) return false;
            int n = _slots.Count;
            if (a < 0 || b < 0) return false;
            if (a >= n && b >= n) return false;

            // If one index is past the live list, move the other to its end
            // (drag-to-empty-cell visual behaviour).
            if (a >= n)
            {
                _slots.Add(_slots[b]);
                _slots.RemoveAt(b);
            }
            else if (b >= n)
            {
                _slots.Add(_slots[a]);
                _slots.RemoveAt(a);
            }
            else
            {
                (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
            }
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
            int n = _slots.Count;
            if (src < 0 || dst < 0 || src >= n || dst >= n) return false;

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
            if (newSrcQty <= 0)
                _slots.RemoveAt(src);
            else
                _slots[src] = new InventorySlot(s.Item, newSrcQty);

            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Convert to serializable InventoryData for save/load.
        /// </summary>
        public InventoryData ToSaveData(string playerId)
        {
            var data = new InventoryData
            {
                playerId = playerId,
                capacity = capacity,
                schemaVersion = "1.0"
            };

            foreach (var slot in _slots)
            {
                data.slots.Add(new InventorySlotData
                {
                    itemId = slot.Item != null ? slot.Item.itemId : "",
                    quantity = slot.Quantity,
                    stackId = ""
                });
            }

            return data;
        }

        public void Clear()
        {
            _slots.Clear();
            OnInventoryChanged?.Invoke();
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
