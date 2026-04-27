using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Read-only mapping of the player's inventory onto the 9 visual equipment
    /// slots used by the inventory panel (3×3 grid).
    /// Mirrors Python's `equip_map` table in inventory_ui_system.py
    /// (slots: weapon, offhand, helmet, chest, boots, extra1, extra2, unused, unused2).
    ///
    /// Phase-1 parity: equipment slots are *not* a separate storage; they are a
    /// rendered mirror of the first matching item found in the regular inventory.
    /// </summary>
    public static class EquipmentView
    {
        // 3×3 layout, row-major
        public const int SLOT_WEAPON  = 0;
        public const int SLOT_OFFHAND = 1;
        public const int SLOT_HELMET  = 2;
        public const int SLOT_CHEST   = 3;
        public const int SLOT_BOOTS   = 4;
        public const int SLOT_EXTRA1  = 5;
        public const int SLOT_EXTRA2  = 6;
        public const int SLOT_UNUSED1 = 7;
        public const int SLOT_UNUSED2 = 8;

        public const int SLOT_COUNT = 9;

        /// <summary>
        /// Fills <paramref name="dest"/> (length 9) with the icon-source items
        /// for each visual equipment slot. Empty slots are set to null.
        /// </summary>
        public static void Resolve(Inventory inventory, ItemDefinition[] dest)
        {
            if (dest == null || dest.Length < SLOT_COUNT) return;
            for (int i = 0; i < SLOT_COUNT; i++) dest[i] = null;
            if (inventory == null) return;

            var slots = inventory.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].IsEmpty) continue;
                int v = MapToVisualSlot(slots[i].Item, dest);
                if (v >= 0 && dest[v] == null)
                    dest[v] = slots[i].Item;
            }
        }

        private static int MapToVisualSlot(ItemDefinition item, ItemDefinition[] dest)
        {
            if (item == null) return -1;
            switch (item.equipSlot)
            {
                case EquipSlot.Weapon:
                    return SLOT_WEAPON;

                case EquipSlot.Offhand:
                case EquipSlot.Shield:
                case EquipSlot.Book:
                    return SLOT_OFFHAND;

                case EquipSlot.Helmet:
                case EquipSlot.Head:
                    return SLOT_HELMET;

                case EquipSlot.Chest:
                case EquipSlot.Body:
                    return SLOT_CHEST;

                case EquipSlot.Boots:
                    return SLOT_BOOTS;

                case EquipSlot.Ring:
                case EquipSlot.Trinket:
                case EquipSlot.Amulet:
                case EquipSlot.Accessory:
                    // First accessory → extra1, second → extra2
                    return dest[SLOT_EXTRA1] == null ? SLOT_EXTRA1 : SLOT_EXTRA2;

                default:
                    return -1;
            }
        }
    }
}
