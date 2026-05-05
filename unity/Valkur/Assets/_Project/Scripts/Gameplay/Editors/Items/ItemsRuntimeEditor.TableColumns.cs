using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Items
{
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
            Func<ItemDefinition, string> getString, Action<ItemDefinition, string> setString = null)
        {
            Header       = header;
            Width        = width;
            EditorKind   = kind;
            GetString    = getString;
            SetString    = setString;
        }

        // ── Dropdown constructor ───────────────────────────────────────────────

        public ItemTableColumn(string header, float width,
            IReadOnlyList<string> options,
            Func<ItemDefinition, int> getIndex,
            Action<ItemDefinition, int> setIndex)
        {
            Header            = header;
            Width             = width;
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
        // Column widths — named constants so a single edit adjusts everything.
        private const float W_ID          = 120f;
        private const float W_NAME        = 130f;
        private const float W_DESC        = 180f;
        private const float W_TYPE        = 80f;
        private const float W_BOOL        =  60f;
        private const float W_INT_SMALL   =  60f;
        private const float W_SPRITE      =  56f;
        private const float W_SLOT        =  90f;
        private const float W_FLOAT       =  70f;
        private const float W_RARITY      = 100f;
        private const float W_EFFECT      = 120f;
        private const float W_QUEST       = 100f;

        private static readonly string[] _equipSlotNames =
            Enum.GetNames(typeof(EquipSlot));

        private static readonly string[] _rarityNames =
            Enum.GetNames(typeof(ItemRarity));

        private static readonly IReadOnlyList<ItemTableColumn> _columns = BuildRegistry();

        public static IReadOnlyList<ItemTableColumn> All => _columns;

        private static List<ItemTableColumn> BuildRegistry()
        {
            return new List<ItemTableColumn>
            {
                // ── Identity ──────────────────────────────────────────────────
                Col("itemId",      W_ID,   ItemTableEditorKind.Text,
                    d => d.itemId ?? "",
                    (d, v) => d.itemId = v),
                Col("displayName", W_NAME, ItemTableEditorKind.Text,
                    d => d.displayName ?? "",
                    (d, v) => d.displayName = v),
                Col("description", W_DESC, ItemTableEditorKind.Text,
                    d => d.description ?? "",
                    (d, v) => d.description = v),
                Col("itemType",    W_TYPE, ItemTableEditorKind.Text,
                    d => d.itemType ?? "",
                    (d, v) => d.itemType = v),

                // ── Stack ─────────────────────────────────────────────────────
                ColBool("stackable", W_BOOL,
                    d => d.stackable,
                    (d, v) => d.stackable = v),
                ColInt("maxStack", W_INT_SMALL,
                    d => d.maxStack,
                    (d, v) => d.maxStack = v),

                // ── Icons (read-only thumbnails) ──────────────────────────────
                ColSprite("icon",      W_SPRITE, d => d.icon),
                ColSprite("iconSmall", W_SPRITE, d => d.iconSmall),
                ColSprite("iconLarge", W_SPRITE, d => d.iconLarge),

                // ── Equip ─────────────────────────────────────────────────────
                ColDropdown("equipSlot", W_SLOT, _equipSlotNames,
                    d => (int)d.equipSlot,
                    (d, i) => d.equipSlot = (EquipSlot)i),
                ColInt("damage",     W_INT_SMALL,
                    d => d.damage,          (d, v) => d.damage = v),
                ColFloat("atkSpd",   W_FLOAT,
                    d => d.attackSpeed,     (d, v) => d.attackSpeed = v),
                ColInt("range",      W_INT_SMALL,
                    d => d.range,           (d, v) => d.range = v),
                ColFloat("crit%",    W_FLOAT,
                    d => d.critChance,      (d, v) => d.critChance = v),
                ColFloat("critMul",  W_FLOAT,
                    d => d.critMultiplier,  (d, v) => d.critMultiplier = v),
                ColInt("durability", W_INT_SMALL,
                    d => d.durability,      (d, v) => d.durability = v),

                // ── Economy ───────────────────────────────────────────────────
                ColInt("value",     W_INT_SMALL,
                    d => d.value,           (d, v) => d.value = v),
                ColInt("buyPrice",  W_INT_SMALL,
                    d => d.buyPrice,        (d, v) => d.buyPrice = v),
                ColInt("sellPrice", W_INT_SMALL,
                    d => d.sellPrice,       (d, v) => d.sellPrice = v),
                ColDropdown("rarity", W_RARITY, _rarityNames,
                    d => (int)d.rarity,
                    (d, i) => d.rarity = (ItemRarity)i),
                ColInt("lvlReq",   W_INT_SMALL,
                    d => d.levelRequirement, (d, v) => d.levelRequirement = v),
                ColFloat("weight",  W_FLOAT,
                    d => d.weight,          (d, v) => d.weight = v),

                // ── XP ────────────────────────────────────────────────────────
                ColInt("threshold",  W_INT_SMALL,
                    d => d.threshold,       (d, v) => d.threshold = v),
                ColInt("experience", W_INT_SMALL,
                    d => d.experience,      (d, v) => d.experience = v),

                // ── Effect ────────────────────────────────────────────────────
                Col("effect", W_EFFECT, ItemTableEditorKind.Text,
                    d => d.effect ?? "",
                    (d, v) => d.effect = v),

                // ── Quest ─────────────────────────────────────────────────────
                Col("questId", W_QUEST, ItemTableEditorKind.Text,
                    d => d.questId ?? "",
                    (d, v) => d.questId = v),

                // ── Visual ────────────────────────────────────────────────────
                ColFloat("scaleEd",  W_FLOAT,
                    d => d.scaleEditor,     (d, v) => d.scaleEditor = v),
                ColFloat("scaleMap", W_FLOAT,
                    d => d.scaleMap,        (d, v) => d.scaleMap = v),
                ColFloat("scaleInv", W_FLOAT,
                    d => d.scaleInventory,  (d, v) => d.scaleInventory = v),
                ColInt("zLayer",     W_INT_SMALL,
                    d => d.zLayer,          (d, v) => d.zLayer = v),
                ColFloat("despawn",  W_FLOAT,
                    d => d.despawnTime,     (d, v) => d.despawnTime = v),

                // ── Consumable ────────────────────────────────────────────────
                ColFloat("healing",  W_FLOAT, d => d.healing,   (d, v) => d.healing = v),
                ColFloat("mana",     W_FLOAT, d => d.mana,      (d, v) => d.mana = v),
                ColFloat("energy",   W_FLOAT, d => d.energy,    (d, v) => d.energy = v),
                ColFloat("hunger",   W_FLOAT, d => d.hunger,    (d, v) => d.hunger = v),
                Col("buffStat",      W_TYPE,  ItemTableEditorKind.Text,
                    d => d.buffStat ?? "",  (d, v) => d.buffStat = v),
                ColFloat("buffVal",  W_FLOAT, d => d.buffValue,  (d, v) => d.buffValue = v),
                ColFloat("duration", W_FLOAT, d => d.duration,   (d, v) => d.duration = v),
            };
        }

        // ── Convenience factories ──────────────────────────────────────────────

        private static ItemTableColumn Col(string header, float width,
            ItemTableEditorKind kind,
            Func<ItemDefinition, string> get,
            Action<ItemDefinition, string> set = null)
            => new ItemTableColumn(header, width, kind, get, set);

        private static ItemTableColumn ColInt(string header, float width,
            Func<ItemDefinition, int> getInt,
            Action<ItemDefinition, int> setInt)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Int,
                d => getInt(d).ToString(),
                (d, v) => { if (int.TryParse(v, out var i)) setInt(d, i); });

        private static ItemTableColumn ColFloat(string header, float width,
            Func<ItemDefinition, float> getF,
            Action<ItemDefinition, float> setF)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Float,
                d => getF(d).ToString("0.###"),
                (d, v) => { if (float.TryParse(v,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f)) setF(d, f); });

        // Toggle columns store the value via string "true"/"false" so the generic
        // SetString path stays consistent; the row builder reads EditorKind to
        // create a Toggle widget instead of a text field.
        private static ItemTableColumn ColBool(string header, float width,
            Func<ItemDefinition, bool> getB,
            Action<ItemDefinition, bool> setB)
            => new ItemTableColumn(header, width, ItemTableEditorKind.Toggle,
                d => getB(d).ToString(),
                (d, v) => { if (bool.TryParse(v, out var b)) setB(d, b); });

        private static ItemTableColumn ColSprite(string header, float width,
            Func<ItemDefinition, Sprite> getSprite)
            => new ItemTableColumn(header, width, ItemTableEditorKind.SpriteThumbnail,
                d => { var s = getSprite(d); return s != null ? s.name : ""; });

        private static ItemTableColumn ColDropdown(string header, float width,
            string[] options,
            Func<ItemDefinition, int> getIdx,
            Action<ItemDefinition, int> setIdx)
            => new ItemTableColumn(header, width, options, getIdx, setIdx);
    }
}
