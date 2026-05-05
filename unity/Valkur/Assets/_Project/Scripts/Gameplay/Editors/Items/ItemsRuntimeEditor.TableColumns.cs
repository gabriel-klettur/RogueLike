using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Items
{
    // ── Column category (drives the colored stripe atop each header cell) ───

    /// <summary>
    /// Logical group every <see cref="ItemTableColumn"/> belongs to. Used by
    /// the table header to draw a thin coloured stripe over each cell so the
    /// user can navigate a 30+ column grid by colour-pattern recognition.
    /// </summary>
    internal enum ItemColumnCategory
    {
        Identity,
        Stack,
        Icons,
        Equip,
        Economy,
        XP,
        Effect,
        Quest,
        Visual,
        Consumable
    }

    // ── Column descriptor ────────────────────────────────────────────────────

    /// <summary>
    /// Describes how a single <see cref="ItemDefinition"/> field is displayed
    /// and edited in the Items table view. The registry is the <em>only</em>
    /// place that knows about specific field names; row-builder code iterates
    /// the list generically so adding a field = adding one entry here.
    /// </summary>
    internal sealed class ItemTableColumn
    {
        /// <summary>Column header displayed in the header strip.</summary>
        public string Header { get; }

        /// <summary>
        /// Preferred column width in canvas pixels. Keep these wide enough for
        /// the typical value so horizontal scrolling is comfortable.
        /// </summary>
        public float Width { get; }

        /// <summary>What kind of editor widget to build for cells in this column.</summary>
        public ItemTableEditorKind EditorKind { get; }

        /// <summary>Logical group used to colour the header stripe.</summary>
        public ItemColumnCategory Category { get; }

        /// <summary>Hover text shown in the status bar; explains what the column means.</summary>
        public string Tooltip { get; }

        /// <summary>Reads the formatted string representation of the field value.</summary>
        public Func<ItemDefinition, string>   GetString { get; }

        /// <summary>
        /// Writes a new value parsed from the string the user typed.
        /// Null when the column is read-only (e.g. Sprite thumbnails).
        /// </summary>
        public Action<ItemDefinition, string> SetString { get; }

        /// <summary>
        /// For dropdown columns: the ordered list of option strings.
        /// Null for non-dropdown columns.
        /// </summary>
        public IReadOnlyList<string> DropdownOptions { get; }

        /// <summary>Reads the dropdown index from the ItemDefinition.</summary>
        public Func<ItemDefinition, int> GetDropdownIndex { get; }

        /// <summary>Writes the dropdown index into the ItemDefinition.</summary>
        public Action<ItemDefinition, int> SetDropdownIndex { get; }

        // ── Text / int / float constructor ────────────────────────────────────

        public ItemTableColumn(string header, float width, ItemTableEditorKind kind,
            ItemColumnCategory category, string tooltip,
            Func<ItemDefinition, string> getString, Action<ItemDefinition, string> setString = null)
        {
            Header     = header;
            Width      = width;
            EditorKind = kind;
            Category   = category;
            Tooltip    = tooltip;
            GetString  = getString;
            SetString  = setString;
        }

        // ── Dropdown constructor ───────────────────────────────────────────────

        public ItemTableColumn(string header, float width,
            ItemColumnCategory category, string tooltip,
            IReadOnlyList<string> options,
            Func<ItemDefinition, int> getIndex,
            Action<ItemDefinition, int> setIndex)
        {
            Header            = header;
            Width             = width;
            Category          = category;
            Tooltip           = tooltip;
            EditorKind        = ItemTableEditorKind.Dropdown;
            DropdownOptions   = options;
            GetDropdownIndex  = getIndex;
            SetDropdownIndex  = setIndex;
            GetString         = d => options[getIndex(d)];
        }
    }

    // ── Editor kind enum ─────────────────────────────────────────────────────

    internal enum ItemTableEditorKind
    {
        Text,
        Int,
        Float,
        Toggle,
        Dropdown,
        SpriteThumbnail   // read-only in iteration 1
    }

    // ── Column registry ──────────────────────────────────────────────────────

    /// <summary>
    /// Static registry of every <see cref="ItemTableColumn"/> in left-to-right
    /// display order. Adding a new <see cref="ItemDefinition"/> field requires
    /// only a single new entry here — the row builder is generic.
    /// </summary>
    internal static class ItemTableColumns
    {
        // Column widths — sized to comfortably fit typical data + the header
        // text, so column titles never truncate. Keep these as named constants
        // so a single edit adjusts the whole table.
        private const float W_ID          = 140f;   // longest id ~ "wizard_staff_lvl_1"
        private const float W_NAME        = 160f;   // displayName, may be Spanish
        private const float W_DESC        = 220f;   // free-form description text
        private const float W_TYPE        =  90f;   // "blacksmith", "alchemy", "lumberjack", "magic", "food"
        private const float W_BOOL        =  72f;
        private const float W_INT         =  72f;
        private const float W_INT_BIG     =  92f;   // "buyPrice", "durability"
        private const float W_FLOAT       =  78f;
        private const float W_FLOAT_BIG   =  98f;   // "attackSpeed", "critChance"
        private const float W_SPRITE      =  60f;
        private const float W_SLOT        = 100f;   // "Accessory" + arrow
        private const float W_RARITY      = 100f;   // "Legendary" + arrow
        private const float W_EFFECT      = 130f;
        private const float W_QUEST       = 200f;   // UUIDs are long
        private const float W_BUFFSTAT    =  98f;

        private static readonly string[] _equipSlotNames =
            Enum.GetNames(typeof(EquipSlot));

        private static readonly string[] _rarityNames =
            Enum.GetNames(typeof(ItemRarity));

        private static readonly IReadOnlyList<ItemTableColumn> _columns = BuildRegistry();

        public static IReadOnlyList<ItemTableColumn> All => _columns;

        // ── Category palette ─────────────────────────────────────────────────
        // Slightly desaturated colours so they read at 3 px tall without
        // overpowering the header text. Matches the genre-typical UI palette
        // (RarityPalette uses similar hues).

        private static readonly Color C_IDENTITY   = new Color(0.78f, 0.78f, 0.82f, 1f); // light grey
        private static readonly Color C_STACK      = new Color(0.30f, 0.62f, 0.95f, 1f); // blue
        private static readonly Color C_ICONS      = new Color(0.80f, 0.55f, 0.95f, 1f); // lavender
        private static readonly Color C_EQUIP      = new Color(0.95f, 0.40f, 0.40f, 1f); // red
        private static readonly Color C_ECONOMY    = new Color(0.95f, 0.78f, 0.30f, 1f); // gold
        private static readonly Color C_XP         = new Color(0.40f, 0.85f, 0.50f, 1f); // green
        private static readonly Color C_EFFECT     = new Color(0.95f, 0.62f, 0.30f, 1f); // orange
        private static readonly Color C_QUEST      = new Color(0.30f, 0.85f, 0.95f, 1f); // cyan
        private static readonly Color C_VISUAL     = new Color(0.95f, 0.45f, 0.75f, 1f); // pink
        private static readonly Color C_CONSUMABLE = new Color(0.55f, 0.95f, 0.35f, 1f); // lime

        public static Color CategoryColor(ItemColumnCategory cat)
        {
            switch (cat)
            {
                case ItemColumnCategory.Identity:   return C_IDENTITY;
                case ItemColumnCategory.Stack:      return C_STACK;
                case ItemColumnCategory.Icons:      return C_ICONS;
                case ItemColumnCategory.Equip:      return C_EQUIP;
                case ItemColumnCategory.Economy:    return C_ECONOMY;
                case ItemColumnCategory.XP:         return C_XP;
                case ItemColumnCategory.Effect:     return C_EFFECT;
                case ItemColumnCategory.Quest:      return C_QUEST;
                case ItemColumnCategory.Visual:     return C_VISUAL;
                case ItemColumnCategory.Consumable: return C_CONSUMABLE;
                default:                            return C_IDENTITY;
            }
        }

        private static List<ItemTableColumn> BuildRegistry()
        {
            return new List<ItemTableColumn>
            {
                // ── Identity ──────────────────────────────────────────────────
                ColText("itemId", W_ID, ItemColumnCategory.Identity,
                    "Stable id used by save data, vendors and loot tables. snake_case, must be unique.",
                    d => d.itemId ?? "",
                    (d, v) => d.itemId = v),
                ColText("displayName", W_NAME, ItemColumnCategory.Identity,
                    "Player-facing name shown in tooltips, inventory and vendors.",
                    d => d.displayName ?? "",
                    (d, v) => d.displayName = v),
                ColText("description", W_DESC, ItemColumnCategory.Identity,
                    "Flavour text shown in tooltips and item popups.",
                    d => d.description ?? "",
                    (d, v) => d.description = v),
                ColText("itemType", W_TYPE, ItemColumnCategory.Identity,
                    "Free-form domain tag (food / magic / blacksmith / lumberjack / alchemy). Used by vendors and crafting.",
                    d => d.itemType ?? "",
                    (d, v) => d.itemType = v),

                // ── Stack ─────────────────────────────────────────────────────
                ColBool("stackable", W_BOOL, ItemColumnCategory.Stack,
                    "When true the item piles into a single inventory slot up to maxStack.",
                    d => d.stackable,
                    (d, v) => d.stackable = v),
                ColInt("maxStack", W_INT, ItemColumnCategory.Stack,
                    "Max units per inventory slot when stackable. Forced to 1 when stackable=false.",
                    d => d.maxStack,
                    (d, v) => d.maxStack = v),

                // ── Icons (read-only thumbnails) ──────────────────────────────
                ColSprite("icon", W_SPRITE, ItemColumnCategory.Icons,
                    "Default sprite shown in the world drop and inventory grid.",
                    d => d.icon),
                ColSprite("iconSmall", W_SPRITE, ItemColumnCategory.Icons,
                    "Small icon used in tight UI spaces (vendor lists, hot bars).",
                    d => d.iconSmall),
                ColSprite("iconLarge", W_SPRITE, ItemColumnCategory.Icons,
                    "Large icon used in the inspect popup.",
                    d => d.iconLarge),

                // ── Equip ─────────────────────────────────────────────────────
                ColDropdown("equipSlot", W_SLOT, ItemColumnCategory.Equip,
                    "Equipment slot the item occupies. None = not equippable.",
                    _equipSlotNames,
                    d => (int)d.equipSlot,
                    (d, i) => d.equipSlot = (EquipSlot)i),
                ColInt("damage", W_INT, ItemColumnCategory.Equip,
                    "Base damage per hit. Weapons need > 0 to be useful.",
                    d => d.damage, (d, v) => d.damage = v),
                ColFloat("attackSpeed", W_FLOAT_BIG, ItemColumnCategory.Equip,
                    "Hits per second. Higher = faster swings.",
                    d => d.attackSpeed, (d, v) => d.attackSpeed = v),
                ColInt("range", W_INT, ItemColumnCategory.Equip,
                    "Reach in tiles. 1 = melee, > 1 = ranged.",
                    d => d.range, (d, v) => d.range = v),
                ColFloat("critChance", W_FLOAT_BIG, ItemColumnCategory.Equip,
                    "Probability (0..1) of rolling a critical hit on a successful attack.",
                    d => d.critChance, (d, v) => d.critChance = v),
                ColFloat("critMul", W_FLOAT, ItemColumnCategory.Equip,
                    "Damage multiplier on a critical hit (e.g. 1.5 = +50% damage).",
                    d => d.critMultiplier, (d, v) => d.critMultiplier = v),
                ColInt("durability", W_INT_BIG, ItemColumnCategory.Equip,
                    "Hit-points of the item. 0 = indestructible. Drops to 0 = item breaks.",
                    d => d.durability, (d, v) => d.durability = v),

                // ── Economy ───────────────────────────────────────────────────
                ColInt("value", W_INT, ItemColumnCategory.Economy,
                    "Generic intrinsic value. Used for default sell pricing when sellPrice = 0.",
                    d => d.value, (d, v) => d.value = v),
                ColInt("buyPrice", W_INT_BIG, ItemColumnCategory.Economy,
                    "Gold cost when buying from a vendor.",
                    d => d.buyPrice, (d, v) => d.buyPrice = v),
                ColInt("sellPrice", W_INT_BIG, ItemColumnCategory.Economy,
                    "Gold paid when selling to a vendor. Should be <= buyPrice (typically half).",
                    d => d.sellPrice, (d, v) => d.sellPrice = v),
                ColDropdown("rarity", W_RARITY, ItemColumnCategory.Economy,
                    "Drop-rarity tier. Drives loot weights and the inventory border colour.",
                    _rarityNames,
                    d => (int)d.rarity,
                    (d, i) => d.rarity = (ItemRarity)i),
                ColInt("levelReq", W_INT, ItemColumnCategory.Economy,
                    "Minimum player level required to equip / use the item.",
                    d => d.levelRequirement, (d, v) => d.levelRequirement = v),
                ColFloat("weight", W_FLOAT, ItemColumnCategory.Economy,
                    "Encumbrance weight in kg. Sums against the player's carry capacity.",
                    d => d.weight, (d, v) => d.weight = v),

                // ── XP ────────────────────────────────────────────────────────
                ColInt("threshold", W_INT, ItemColumnCategory.XP,
                    "Minimum stack size required for the item to grant XP on pickup.",
                    d => d.threshold, (d, v) => d.threshold = v),
                ColInt("experience", W_INT, ItemColumnCategory.XP,
                    "XP awarded to the player when this item is picked up.",
                    d => d.experience, (d, v) => d.experience = v),

                // ── Effect ────────────────────────────────────────────────────
                ColText("effect", W_EFFECT, ItemColumnCategory.Effect,
                    "Optional effect identifier (e.g. 'explode_area', 'poison_area', 'light_source').",
                    d => d.effect ?? "",
                    (d, v) => d.effect = v),

                // ── Quest ─────────────────────────────────────────────────────
                ColText("questId", W_QUEST, ItemColumnCategory.Quest,
                    "If set, the item is classified as a Quest item and pinned to that quest UUID.",
                    d => d.questId ?? "",
                    (d, v) => d.questId = v),

                // ── Visual ────────────────────────────────────────────────────
                ColFloat("scaleEditor", W_FLOAT_BIG, ItemColumnCategory.Visual,
                    "Scale factor when drawn inside the editor's gizmos.",
                    d => d.scaleEditor, (d, v) => d.scaleEditor = v),
                ColFloat("scaleMap", W_FLOAT, ItemColumnCategory.Visual,
                    "Scale factor for the world drop sprite.",
                    d => d.scaleMap, (d, v) => d.scaleMap = v),
                ColFloat("scaleInv", W_FLOAT, ItemColumnCategory.Visual,
                    "Scale factor for the inventory grid icon.",
                    d => d.scaleInventory, (d, v) => d.scaleInventory = v),
                ColInt("zLayer", W_INT, ItemColumnCategory.Visual,
                    "Sorting offset within the sprite's sorting layer. Higher = drawn on top.",
                    d => d.zLayer, (d, v) => d.zLayer = v),
                ColFloat("despawn", W_FLOAT, ItemColumnCategory.Visual,
                    "TTL in seconds for world drops before they vanish. 0 = never.",
                    d => d.despawnTime, (d, v) => d.despawnTime = v),

                // ── Consumable ────────────────────────────────────────────────
                ColFloat("healing", W_FLOAT, ItemColumnCategory.Consumable,
                    "HP restored on consume.",
                    d => d.healing,   (d, v) => d.healing = v),
                ColFloat("mana", W_FLOAT, ItemColumnCategory.Consumable,
                    "Mana restored on consume.",
                    d => d.mana,      (d, v) => d.mana = v),
                ColFloat("energy", W_FLOAT, ItemColumnCategory.Consumable,
                    "Energy / stamina restored on consume.",
                    d => d.energy,    (d, v) => d.energy = v),
                ColFloat("hunger", W_FLOAT, ItemColumnCategory.Consumable,
                    "Hunger satiated on consume (food items).",
                    d => d.hunger,    (d, v) => d.hunger = v),
                ColText("buffStat", W_BUFFSTAT, ItemColumnCategory.Consumable,
                    "Stat key buffed on consume (e.g. 'poison_dot', 'speed_boost').",
                    d => d.buffStat ?? "",  (d, v) => d.buffStat = v),
                ColFloat("buffValue", W_FLOAT, ItemColumnCategory.Consumable,
                    "Magnitude applied to buffStat (per second for DoT, flat for buffs).",
                    d => d.buffValue,  (d, v) => d.buffValue = v),
                ColFloat("duration", W_FLOAT, ItemColumnCategory.Consumable,
                    "Buff duration in seconds. 0 = instant.",
                    d => d.duration,   (d, v) => d.duration = v),
            };
        }

        // ── Convenience factories ──────────────────────────────────────────────

        private static ItemTableColumn ColText(string header, float width,
            ItemColumnCategory cat, string tip,
            Func<ItemDefinition, string> get,
            Action<ItemDefinition, string> set = null)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Text, cat, tip, get, set);

        private static ItemTableColumn ColInt(string header, float width,
            ItemColumnCategory cat, string tip,
            Func<ItemDefinition, int> getInt,
            Action<ItemDefinition, int> setInt)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Int, cat, tip,
                d => getInt(d).ToString(),
                (d, v) => { if (int.TryParse(v, out var i)) setInt(d, i); });

        private static ItemTableColumn ColFloat(string header, float width,
            ItemColumnCategory cat, string tip,
            Func<ItemDefinition, float> getF,
            Action<ItemDefinition, float> setF)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Float, cat, tip,
                d => getF(d).ToString("0.###"),
                (d, v) => { if (float.TryParse(v,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f)) setF(d, f); });

        // Toggle columns store the value via string "true"/"false" so the generic
        // SetString path stays consistent; the row builder reads EditorKind to
        // create a Toggle widget instead of a text field.
        private static ItemTableColumn ColBool(string header, float width,
            ItemColumnCategory cat, string tip,
            Func<ItemDefinition, bool> getB,
            Action<ItemDefinition, bool> setB)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Toggle, cat, tip,
                d => getB(d).ToString(),
                (d, v) => { if (bool.TryParse(v, out var b)) setB(d, b); });

        private static ItemTableColumn ColSprite(string header, float width,
            ItemColumnCategory cat, string tip,
            Func<ItemDefinition, Sprite> getSprite)
            => new ItemTableColumn(header, width, ItemTableEditorKind.SpriteThumbnail, cat, tip,
                d => { var s = getSprite(d); return s != null ? s.name : ""; });

        private static ItemTableColumn ColDropdown(string header, float width,
            ItemColumnCategory cat, string tip,
            string[] options,
            Func<ItemDefinition, int> getIdx,
            Action<ItemDefinition, int> setIdx)
            => new ItemTableColumn(header, width, cat, tip, options, getIdx, setIdx);
    }
}
