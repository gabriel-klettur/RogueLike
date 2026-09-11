using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>Why an item could not go where it was asked to.</summary>
    public enum EquipRefusal
    {
        None = 0,
        /// <summary>The item is not something a character can wear.</summary>
        NotWearable = 1,
        /// <summary>The item is wearable, but in a different slot.</summary>
        WrongSlot = 2,
        /// <summary>The character's level is under the item's requirement.</summary>
        LevelTooLow = 3,
        /// <summary>Taking it off needs a free bag slot and there is none.</summary>
        BagFull = 4,
    }

    /// <summary>
    /// The equipment half of the inventory: eight TYPED slots, one per
    /// <see cref="EquipmentSlotKind"/>. An item goes only in the slot its
    /// <see cref="ItemDefinition.equipSlot"/> names, and only once the character meets its level
    /// requirement. Both rules are enforced HERE, at the model, so every path that writes an
    /// equipment slot — the panel's drag, a double-click, a drag from the world — obeys them;
    /// a rule only the panel knew would be a rule the world-drop path could walk around.
    /// </summary>
    public partial class Inventory
    {
        private Experience _xp;
        private bool _xpLooked;

        /// <summary>Why the last refused equipment move was refused. For the panel's feedback.</summary>
        public EquipRefusal LastRefusal { get; private set; }

        /// <summary>The character's level, or -1 when nothing on this object tracks one.</summary>
        public int CharacterLevel
        {
            get
            {
                if (!_xpLooked) { _xpLooked = true; _xp = GetComponent<Experience>(); }
                return _xp != null ? _xp.Level : -1;
            }
        }

        // ── Rules ──────────────────────────────────────────────────────────

        /// <summary>
        /// Whether <paramref name="item"/> may be worn in equipment slot
        /// <paramref name="equipIndex"/>, and if not, why.
        /// </summary>
        public EquipRefusal CheckEquip(ItemDefinition item, int equipIndex)
        {
            if (item == null) return EquipRefusal.NotWearable;
            int home = EquipmentLayout.IndexFor(item);
            if (home < 0) return EquipRefusal.NotWearable;
            if (home != equipIndex) return EquipRefusal.WrongSlot;
            int need = EquipmentLayout.RequiredLevel(item);
            int level = CharacterLevel;
            // No Experience on this object (a bare fixture, an NPC's bag) means no gate.
            if (need > 0 && level >= 0 && level < need) return EquipRefusal.LevelTooLow;
            return EquipRefusal.None;
        }

        /// <summary>True when <paramref name="item"/> may be placed at the unified index.</summary>
        public bool CanPlaceAt(int unifiedIndex, ItemDefinition item)
        {
            if (unifiedIndex >= 0 && unifiedIndex < capacity) return true;
            if (!IsEquipmentIndex(unifiedIndex)) return false;
            return CheckEquip(item, unifiedIndex - capacity) == EquipRefusal.None;
        }

        // ── Direct access ─────────────────────────────────────────────────

        /// <summary>
        /// Raw setter for the save-restore path and the fixtures: bypasses every rule.
        /// Gameplay code wears items through <see cref="TryEquipFromBag"/> or
        /// <see cref="MoveSlotByIndex"/>.
        /// </summary>
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
        /// Restores one saved equipped item into the slot its kind names — whatever index it was
        /// saved at. A save written before the slots were typed stored nine untyped cells, so an
        /// item's old index says nothing about where it belongs now; a pre-typed save may also
        /// hold something that is not wearable at all (a potion in the old "Helmet" cell). Those,
        /// and anything whose slot is already taken, go back into the bag instead of vanishing.
        /// Returns the quantity that fitted nowhere.
        /// </summary>
        public int RestoreEquipped(ItemDefinition item, int quantity)
        {
            if (item == null || quantity <= 0) return 0;
            EnsureEquipSized();
            int home = EquipmentLayout.IndexFor(item);
            if (home >= 0 && _equipSlots[home].IsEmpty)
            {
                int wear = item.stackable ? Mathf.Min(quantity, Mathf.Max(1, item.maxStack)) : 1;
                _equipSlots[home] = new InventorySlot(item, wear);
                OnInventoryChanged?.Invoke();
                int rest = quantity - wear;
                return rest > 0 ? AddItem(item, rest) : 0;
            }
            return AddItem(item, quantity);
        }

        /// <summary>
        /// Place up to <paramref name="quantity"/> of <paramref name="item"/> at the given
        /// equipment slot. Refuses (returns 0) when the item does not belong in that slot, the
        /// level is too low, or the slot holds something else. Stackables stack up to maxStack.
        /// </summary>
        public int TryDepositInEquipmentSlot(int equipIndex, ItemDefinition item, int quantity)
        {
            if (item == null || quantity <= 0) return 0;
            if (equipIndex < 0 || equipIndex >= EquipmentCapacity) return 0;
            EnsureEquipSized();

            var why = CheckEquip(item, equipIndex);
            if (why != EquipRefusal.None) { LastRefusal = why; return 0; }

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

        // ── Wearing and taking off ─────────────────────────────────────────

        /// <summary>
        /// Wears the item in bag slot <paramref name="bagIndex"/> in the slot its kind names,
        /// swapping whatever was there back into that same bag slot.
        /// </summary>
        public bool TryEquipFromBag(int bagIndex, out EquipRefusal why)
        {
            why = EquipRefusal.None;
            if (bagIndex < 0 || bagIndex >= capacity) { why = EquipRefusal.NotWearable; return false; }
            EnsureSlotsSized();
            var s = _slots[bagIndex];
            if (s.IsEmpty) { why = EquipRefusal.NotWearable; return false; }
            int home = EquipmentLayout.IndexFor(s.Item);
            why = CheckEquip(s.Item, home);
            if (why != EquipRefusal.None) { LastRefusal = why; return false; }
            return MoveSlotByIndex(bagIndex, capacity + home);
        }

        /// <summary>
        /// Takes off the item in equipment slot <paramref name="equipIndex"/> into the first free
        /// bag slot (or onto a partial stack of the same item). Refuses when the bag is full.
        /// Returns the bag index it landed in, or -1.
        /// </summary>
        public int TryUnequip(int equipIndex)
        {
            if (equipIndex < 0 || equipIndex >= EquipmentCapacity) return -1;
            EnsureSlotsSized();
            EnsureEquipSized();
            var s = _equipSlots[equipIndex];
            if (s.IsEmpty) return -1;

            int target = -1;
            if (s.Item.stackable)
                for (int i = 0; i < _slots.Count && target < 0; i++)
                    if (_slots[i].Item == s.Item && _slots[i].Quantity + s.Quantity <= s.Item.maxStack) target = i;
            for (int i = 0; i < _slots.Count && target < 0; i++)
                if (_slots[i].IsEmpty) target = i;
            if (target < 0) { LastRefusal = EquipRefusal.BagFull; return -1; }

            _slots[target] = _slots[target].IsEmpty
                ? s
                : new InventorySlot(s.Item, _slots[target].Quantity + s.Quantity);
            _equipSlots[equipIndex] = default;
            OnInventoryChanged?.Invoke();
            return target;
        }

        // ── Unified index-space helpers ────────────────────────────────────
        // Visual slots are addressed by a single int across the panel:
        //   • [0 .. Capacity)               → bag.
        //   • [Capacity .. Capacity+Equip)  → equipment.
        // This lets WorldDropInteractor / drag handlers treat the panel as
        // one grid and route deposits without an extra "kind" parameter.

        public bool IsEquipmentIndex(int unifiedIndex)
            => unifiedIndex >= capacity && unifiedIndex < capacity + EquipmentCapacity;

        public InventorySlot GetSlotByIndex(int unifiedIndex)
        {
            EnsureSlotsSized();
            EnsureEquipSized();
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
        /// across both bag and equipment slots. If <paramref name="dst"/> is non-empty:
        /// stack-merge if compatible, else swap. A move that would put an item in an equipment
        /// slot it does not belong in — in EITHER direction of a swap — is refused, and
        /// <see cref="LastRefusal"/> says why. Returns true on any change.
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

            // Stack-merge same-item stackables. The destination already holds this item, so it
            // is already a place this item may be.
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

            if (IsEquipmentIndex(dst))
            {
                var why = CheckEquip(s.Item, dst - capacity);
                if (why != EquipRefusal.None) { LastRefusal = why; return false; }
            }
            if (!d.IsEmpty && IsEquipmentIndex(src))
            {
                var why = CheckEquip(d.Item, src - capacity);
                if (why != EquipRefusal.None) { LastRefusal = why; return false; }
            }

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

        private void EnsureEquipSized()
        {
            if (_equipSlots.Count == EquipmentCapacity) return;
            if (_equipSlots.Count > EquipmentCapacity)
                _equipSlots.RemoveRange(EquipmentCapacity, _equipSlots.Count - EquipmentCapacity);
            while (_equipSlots.Count < EquipmentCapacity) _equipSlots.Add(default);
        }
    }
}
