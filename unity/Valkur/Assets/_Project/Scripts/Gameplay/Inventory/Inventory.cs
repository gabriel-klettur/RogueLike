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
