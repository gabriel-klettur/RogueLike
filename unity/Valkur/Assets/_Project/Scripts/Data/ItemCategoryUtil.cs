namespace Valkur.Data
{
    /// <summary>
    /// Inventory tab classification helpers. Mirrors Python's
    /// `pick_category_for_item` (inventory_categories.py) and
    /// `_in_active_category` (inventory_ui_system.py).
    /// </summary>
    public static class ItemCategoryUtil
    {
        // Python tab order: Equipo / Materiales / Consumibles / Otros / Quest
        public const int TAB_EQUIPMENT  = 0;
        public const int TAB_MATERIAL   = 1;
        public const int TAB_CONSUMABLE = 2;
        public const int TAB_OTHER      = 3;
        public const int TAB_QUEST      = 4;

        public static ItemCategory GetCategory(this ItemDefinition item)
        {
            if (item == null) return ItemCategory.Other;

            // Quest takes priority over everything else.
            if (!string.IsNullOrEmpty(item.questId))
                return ItemCategory.Quest;

            // Equipment: anything wearable or with durability.
            if (item.equipSlot != EquipSlot.None || item.durability > 0)
                return ItemCategory.Equipment;

            // Consumable: has any consume effect (string or numeric).
            if (!string.IsNullOrEmpty(item.effect)
                || item.healing > 0 || item.mana > 0
                || item.energy != 0 || item.hunger != 0)
                return ItemCategory.Consumable;

            // Material: stackable plain resource.
            if (item.stackable)
                return ItemCategory.Material;

            return ItemCategory.Other;
        }

        public static bool MatchesTab(this ItemDefinition item, int tabIndex)
        {
            var c = GetCategory(item);
            switch (tabIndex)
            {
                case TAB_EQUIPMENT:  return c == ItemCategory.Equipment;
                case TAB_MATERIAL:   return c == ItemCategory.Material;
                case TAB_CONSUMABLE: return c == ItemCategory.Consumable;
                case TAB_OTHER:      return c == ItemCategory.Other;
                case TAB_QUEST:      return c == ItemCategory.Quest;
                default:             return true;
            }
        }
    }
}
