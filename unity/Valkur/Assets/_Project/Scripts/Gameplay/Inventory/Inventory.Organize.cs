using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>Splitting stacks and sorting the bag.</summary>
    public partial class Inventory
    {
        /// <summary>
        /// Moves <paramref name="amount"/> of the bag stack at <paramref name="src"/> into bag slot
        /// <paramref name="dst"/>, which must be empty or hold a partial stack of the same item.
        /// Returns the quantity moved (0 when nothing could move). The source keeps at least one:
        /// a split that empties its source is a move, and <see cref="MoveSlotByIndex"/> is that.
        /// </summary>
        public int SplitStack(int src, int dst, int amount)
        {
            if (src == dst || amount <= 0) return 0;
            if (src < 0 || dst < 0 || src >= capacity || dst >= capacity) return 0;
            EnsureSlotsSized();

            var s = _slots[src];
            if (s.IsEmpty || !s.Item.stackable || s.Quantity < 2) return 0;
            int take = Mathf.Min(amount, s.Quantity - 1);

            var d = _slots[dst];
            int room;
            if (d.IsEmpty) room = Mathf.Max(1, s.Item.maxStack);
            else if (d.Item == s.Item) room = Mathf.Max(0, s.Item.maxStack - d.Quantity);
            else return 0;

            int moved = Mathf.Min(take, room);
            if (moved <= 0) return 0;

            _slots[dst] = new InventorySlot(s.Item, (d.IsEmpty ? 0 : d.Quantity) + moved);
            _slots[src] = new InventorySlot(s.Item, s.Quantity - moved);
            OnInventoryChanged?.Invoke();
            return moved;
        }

        /// <summary>
        /// Packs the bag to the front: partial stacks of the same item are merged first, then
        /// every stack is ordered by category (equipment, consumables, materials, quest, other),
        /// rarity high to low, then name. The order is STABLE — sorting a sorted bag moves
        /// nothing — so pressing the button twice is not a way to shuffle the player's things.
        /// Returns true when anything moved.
        /// </summary>
        public bool SortBag()
        {
            EnsureSlotsSized();
            var before = new List<InventorySlot>(_slots);

            // Merge partial stacks of the same item, keeping the first occurrence's order.
            var merged = new List<InventorySlot>();
            var totals = new Dictionary<ItemDefinition, int>();
            var order = new List<ItemDefinition>();
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.IsEmpty) continue;
                if (!s.Item.stackable) { merged.Add(s); continue; }
                if (!totals.ContainsKey(s.Item)) { totals[s.Item] = 0; order.Add(s.Item); }
                totals[s.Item] += s.Quantity;
            }
            for (int i = 0; i < order.Count; i++)
            {
                var item = order[i];
                int left = totals[item];
                int cap = Mathf.Max(1, item.maxStack);
                while (left > 0)
                {
                    int n = Mathf.Min(cap, left);
                    merged.Add(new InventorySlot(item, n));
                    left -= n;
                }
            }

            // A stack saved above its maxStack re-packs into MORE slots than it came from, which
            // could need more room than the bag has. Refuse rather than drop anything.
            if (merged.Count > _slots.Count) return false;

            // Stable sort: List.Sort is not, so tie-break on the original position.
            var keyed = new List<(InventorySlot slot, int idx)>(merged.Count);
            for (int i = 0; i < merged.Count; i++) keyed.Add((merged[i], i));
            keyed.Sort((a, b) =>
            {
                int c = SortRank(a.slot.Item).CompareTo(SortRank(b.slot.Item));
                if (c != 0) return c;
                c = ((int)b.slot.Item.rarity).CompareTo((int)a.slot.Item.rarity);
                if (c != 0) return c;
                c = string.CompareOrdinal(NameOf(a.slot.Item), NameOf(b.slot.Item));
                if (c != 0) return c;
                c = b.slot.Quantity.CompareTo(a.slot.Quantity);
                return c != 0 ? c : a.idx.CompareTo(b.idx);
            });

            for (int i = 0; i < _slots.Count; i++)
                _slots[i] = i < keyed.Count ? keyed[i].slot : default;

            bool changed = false;
            for (int i = 0; i < _slots.Count && !changed; i++)
                changed = before[i].Item != _slots[i].Item || before[i].Quantity != _slots[i].Quantity;
            if (changed) OnInventoryChanged?.Invoke();
            return changed;
        }

        private static int SortRank(ItemDefinition item)
        {
            switch (item.GetCategory())
            {
                case ItemCategory.Equipment: return 0;
                case ItemCategory.Consumable: return 1;
                case ItemCategory.Material: return 2;
                case ItemCategory.Quest: return 3;
                default: return 4;
            }
        }

        private static string NameOf(ItemDefinition item)
            => !string.IsNullOrEmpty(item.displayName) ? item.displayName : item.itemId ?? "";
    }
}
