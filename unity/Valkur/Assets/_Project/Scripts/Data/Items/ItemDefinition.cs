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

        [Tooltip("Arbitrary stat modifiers granted while this item is EQUIPPED. The " +
                 "open-ended authoring path beside the fixed damage/attackSpeed/critChance " +
                 "fields above: a helmet that grants Max HP or a book that grants Spell " +
                 "Power has nowhere to say so otherwise. Both feed the same Equipment stat " +
                 "layer, so a designer may use either or both.")]
        public StatModifier[] statModifiers = System.Array.Empty<StatModifier>();

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
        [Tooltip("LEGACY. A stat name as a string, from the Python build. Only two shipped " +
                 "items carry one and neither names a stat — 'explosion_damage' and " +
                 "'poison_dot' are throwable effects, not character buffs — so nothing " +
                 "could ever have consumed it. Parsed on a best effort basis and warned " +
                 "about once; author buffModifiers instead.")]
        public string buffStat;
        public float buffValue;
        public float duration;

        [Tooltip("Stat modifiers applied for `duration` seconds when this item is " +
                 "consumed. The typed replacement for buffStat/buffValue: a StatKind " +
                 "cannot be misspelled and an enum renders as a dropdown.")]
        public StatModifier[] buffModifiers = System.Array.Empty<StatModifier>();

        [Header("Tool")]
        [Tooltip("How this item's blows are classified against a destructible building. " +
                 "None means it is not a tool and never wins the resolution, which is the " +
                 "right answer for every item that is not swung at scenery.")]
        public DamageClass toolClass = DamageClass.None;

        [Tooltip("Tool tier, compared against DestructionProfile.requiredToolTier. A stone " +
                 "axe is tier 1, a steel one tier 2. Below the requirement a physical blow " +
                 "is scaled down to the profile's chip fraction rather than refused.")]
        public int toolTier;
    }
}
