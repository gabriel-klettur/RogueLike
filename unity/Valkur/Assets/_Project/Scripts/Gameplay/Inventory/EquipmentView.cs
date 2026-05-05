using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// Helper that exposes the player's 3×3 equipment storage to the UI.
    /// Equipment is now a real, separately-stored slot array on
    /// <see cref="Inventory"/> — items only appear here when the player
    /// drags them in (from world or bag). No more auto-mirror from the bag.
    /// </summary>
    public static class EquipmentView
    {
        // 3×3 layout, row-major. The semantic labels live in InventoryUI's
        // EQUIP_SLOT_LABELS array; these constants exist only as fixed
        // grid positions for legacy callers.
        public const int SLOT_COUNT = 9;

        /// <summary>
        /// Fills <paramref name="dest"/> (length 9) with a snapshot of the
        /// items held in the player's equipment slots (or null when empty).
        /// </summary>
        public static void Resolve(Inventory inventory, ItemDefinition[] dest)
        {
            if (dest == null || dest.Length < SLOT_COUNT) return;
            for (int i = 0; i < SLOT_COUNT; i++) dest[i] = null;
            if (inventory == null) return;

            var slots = inventory.EquipmentSlots;
            int max = slots.Count < SLOT_COUNT ? slots.Count : SLOT_COUNT;
            for (int i = 0; i < max; i++)
            {
                if (!slots[i].IsEmpty) dest[i] = slots[i].Item;
            }
        }
    }
}
