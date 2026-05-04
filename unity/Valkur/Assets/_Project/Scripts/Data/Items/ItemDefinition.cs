using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Rarity tiers matching Python's item schema enum.
    /// </summary>
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    /// <summary>
    /// Equipment slot matching Python's equip_slot enum.
    /// Order kept stable so existing serialized assets do not shift values.
    /// </summary>
    public enum EquipSlot
    {
        None    = 0,
        Head    = 1,   // legacy alias of Helmet
        Body    = 2,   // legacy alias of Chest
        Weapon  = 3,
        Helmet  = 4,
        Chest   = 5,
        Boots   = 6,
        Offhand = 7,
        Shield  = 8,
        Book    = 9,
        Ring    = 10,
        Amulet  = 11,
        Trinket = 12,
        Accessory = 13
    }

    /// <summary>
    /// High-level inventory tab category, derived from ItemDefinition fields.
    /// Mirrors Python's pick_category_for_item rules.
    /// </summary>
    public enum ItemCategory
    {
        Equipment,
        Material,
        Consumable,
        Quest,
        Other
    }

    /// <summary>
    /// ScriptableObject defining an item type.
    /// Maps to Python's schemas/items/common.json -> definitions/item.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "Valkur/Data/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string itemId;
        public string displayName;
        [TextArea(2, 4)]
        public string description;

        // Free-form domain tag mirroring Python's `items.type` column
        // (food / magic / blacksmith / null). Used by vendors and crafting
        // surfaces to group items independently of `category` and `equipSlot`.
        public string itemType;

        [Header("Stacking")]
        public bool stackable;
        public int maxStack = 1;

        [Header("Icons")]
        public Sprite icon;
        public Sprite iconSmall;
        public Sprite iconLarge;

        [Header("Equipment")]
        public EquipSlot equipSlot;
        public int damage;
        public float attackSpeed;
        public int range;
        public float critChance;
        public float critMultiplier = 1f;
        public int durability;

        [Header("Economy")]
        public int value;
        public int buyPrice;
        public int sellPrice;
        public ItemRarity rarity;
        public int levelRequirement = 1;
        public float weight;

        [Header("Experience")]
        public int threshold;
        public int experience;

        [Header("Effect")]
        public string effect;

        [Header("Quest")]
        public string questId;

        [Header("Visual")]
        public float scaleEditor = 1f;
        public float scaleMap = 1f;
        public float scaleInventory = 1f;
        public int zLayer;
        public float despawnTime;

        [Header("Params (consumable effects)")]
        public float healing;
        public float mana;
        public float energy;
        public float hunger;
        public string buffStat;
        public float buffValue;
        public float duration;
    }
}
