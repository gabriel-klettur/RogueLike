using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    // ── Column category (drives the colored stripe atop each header cell) ───

    /// <summary>
    /// Logical group every <see cref="SpellTableColumn"/> belongs to. Used by
    /// the table header to draw a thin coloured stripe over each cell so the
    /// user can navigate a wide column grid by colour-pattern recognition.
    /// </summary>
    internal enum SpellColumnCategory
    {
        Identity,
        Casting,
        DamageRange,
        VFX,
        TypeSpecific
    }

    // ── Column descriptor ────────────────────────────────────────────────────

    /// <summary>
    /// Describes how a single <see cref="SpellDefinition"/> field is displayed
    /// and edited in the Spells table view. The registry is the <em>only</em>
    /// place that knows about specific field names; row-builder code iterates
    /// the list generically so adding a field = adding one entry here.
    /// </summary>
    internal sealed class SpellTableColumn
    {
        /// <summary>Column header displayed in the header strip.</summary>
        public string Header { get; }

        /// <summary>Preferred column width in canvas pixels.</summary>
        public float Width { get; }

        /// <summary>What kind of editor widget to build for cells in this column.</summary>
        public SpellTableEditorKind EditorKind { get; }

        /// <summary>Logical group used to colour the header stripe.</summary>
        public SpellColumnCategory Category { get; }

        /// <summary>Hover text shown in the status bar; explains what the column means.</summary>
        public string Tooltip { get; }

        /// <summary>Reads the formatted string representation of the field value.</summary>
        public Func<SpellDefinition, string>   GetString { get; }

        /// <summary>
        /// Writes a new value parsed from the string the user typed.
        /// Null when the column is read-only (e.g. sprite thumbnails).
        /// </summary>
        public Action<SpellDefinition, string> SetString { get; }

        /// <summary>
        /// For dropdown columns: the ordered list of option strings.
        /// Null for non-dropdown columns.
        /// </summary>
        public IReadOnlyList<string> DropdownOptions { get; }

        /// <summary>Reads the dropdown index from the SpellDefinition.</summary>
        public Func<SpellDefinition, int> GetDropdownIndex { get; }

        /// <summary>Writes the dropdown index into the SpellDefinition.</summary>
        public Action<SpellDefinition, int> SetDropdownIndex { get; }

        // ── Text / int / float / toggle constructor ───────────────────────────

        public SpellTableColumn(string header, float width, SpellTableEditorKind kind,
            SpellColumnCategory category, string tooltip,
            Func<SpellDefinition, string> getString, Action<SpellDefinition, string> setString = null)
        {
            Header     = header;
            Width      = width;
            EditorKind = kind;
            Category   = category;
            Tooltip    = tooltip;
            GetString  = getString;
            SetString  = setString;
        }

        // ── Dropdown constructor ──────────────────────────────────────────────

        public SpellTableColumn(string header, float width,
            SpellColumnCategory category, string tooltip,
            IReadOnlyList<string> options,
            Func<SpellDefinition, int> getIndex,
            Action<SpellDefinition, int> setIndex)
        {
            Header           = header;
            Width            = width;
            Category         = category;
            Tooltip          = tooltip;
            EditorKind       = SpellTableEditorKind.Dropdown;
            DropdownOptions  = options;
            GetDropdownIndex = getIndex;
            SetDropdownIndex = setIndex;
            GetString        = d => (options != null && options.Count > 0)
                                    ? options[getIndex(d)]
                                    : getIndex(d).ToString();
        }
    }

    // ── Editor kind enum ─────────────────────────────────────────────────────

    internal enum SpellTableEditorKind
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
    /// Static registry of every <see cref="SpellTableColumn"/> in left-to-right
    /// display order. Adding a new <see cref="SpellDefinition"/> field requires
    /// only a single new entry here — the row builder is generic.
    /// </summary>
    internal static class SpellTableColumns
    {
        // Column widths
        private const float W_KEY       = 160f;
        private const float W_NAME      = 150f;
        private const float W_TYPE      = 100f;
        private const float W_ELEMENT   = 80f;
        private const float W_SPRITE    = 52f;
        private const float W_BOOL      = 68f;
        private const float W_INT       = 68f;
        private const float W_FLOAT     = 72f;
        private const float W_FLOAT_BIG = 90f;
        private const float W_TEXT      = 120f;
        private const float W_COLOR     = 80f;

        private static readonly string[] _spellTypeNames =
            Enum.GetNames(typeof(SpellType));

        private static readonly string[] _elementOptions =
            { "", "Fire", "Ice", "Light", "Dark", "Arcane", "Lightning" };

        private static readonly IReadOnlyList<SpellTableColumn> _columns = BuildRegistry();

        public static IReadOnlyList<SpellTableColumn> All => _columns;

        // ── Category palette ──────────────────────────────────────────────────

        private static readonly Color C_IDENTITY    = new Color(0.78f, 0.78f, 0.82f, 1f); // light grey
        private static readonly Color C_CASTING     = new Color(0.30f, 0.62f, 0.95f, 1f); // blue
        private static readonly Color C_DAMAGERANGE = new Color(0.95f, 0.40f, 0.40f, 1f); // red
        private static readonly Color C_VFX         = new Color(0.80f, 0.55f, 0.95f, 1f); // lavender
        private static readonly Color C_TYPESPECIFIC = new Color(0.95f, 0.78f, 0.30f, 1f); // gold

        public static Color CategoryColor(SpellColumnCategory cat)
        {
            switch (cat)
            {
                case SpellColumnCategory.Identity:    return C_IDENTITY;
                case SpellColumnCategory.Casting:     return C_CASTING;
                case SpellColumnCategory.DamageRange: return C_DAMAGERANGE;
                case SpellColumnCategory.VFX:         return C_VFX;
                case SpellColumnCategory.TypeSpecific: return C_TYPESPECIFIC;
                default:                              return C_IDENTITY;
            }
        }

        private static List<SpellTableColumn> BuildRegistry()
        {
            return new List<SpellTableColumn>
            {
                // ── Identity ──────────────────────────────────────────────────
                ColText("spellKey", W_KEY, SpellColumnCategory.Identity,
                    "Stable key used by save data, NPC casts and spell bars. snake_case, must be unique.",
                    d => d.spellKey ?? ""),
                    // read-only: no setter — key is identity

                ColText("displayName", W_NAME, SpellColumnCategory.Identity,
                    "Player-facing name shown in the spell bar tooltip and editor.",
                    d => d.displayName ?? "",
                    (d, v) => d.displayName = v),

                ColDropdown("type", W_TYPE, SpellColumnCategory.Identity,
                    "Spell execution type. Determines which executor is dispatched at cast time.",
                    _spellTypeNames,
                    d => (int)d.type,
                    (d, i) => d.type = (SpellType)i),

                ColDropdown("element", W_ELEMENT, SpellColumnCategory.Identity,
                    "Elemental affinity. Used by resistances, DoT typing and visual tinting.",
                    _elementOptions,
                    d => ElementIndex(d),
                    (d, i) => d.element = _elementOptions[i]),

                ColSprite("sprite", W_SPRITE, SpellColumnCategory.Identity,
                    "HUD icon shown in the spell bar slot. Falls back to the legacy " +
                    "in-world sprite for spells not yet migrated. Null = procedural preview.",
                    d => d.iconSprite != null ? d.iconSprite : d.sprite),

                // ── Casting ───────────────────────────────────────────────────
                ColFloat("manaCost", W_FLOAT, SpellColumnCategory.Casting,
                    "Mana consumed when the spell is cast. Must be <= caster's current mana.",
                    d => d.manaCost, (d, v) => d.manaCost = v),

                ColInt("maxInstances", W_INT, SpellColumnCategory.Casting,
                    "How many simultaneous instances the caster may have active. 0 = unlimited.",
                    d => d.maxInstances, (d, v) => d.maxInstances = v),

                ColFloat("cooldownDuration", W_FLOAT_BIG, SpellColumnCategory.Casting,
                    "Seconds the spell is locked after casting. 0 = no cooldown.",
                    d => d.cooldownDuration, (d, v) => d.cooldownDuration = v),

                ColFloat("prepareDuration", W_FLOAT_BIG, SpellColumnCategory.Casting,
                    "Wind-up time before the effect fires. 0 = instant fire.",
                    d => d.prepareDuration, (d, v) => d.prepareDuration = v),

                ColFloat("channelDuration", W_FLOAT_BIG, SpellColumnCategory.Casting,
                    "Time the caster must keep channeling after the prepare phase.",
                    d => d.channelDuration, (d, v) => d.channelDuration = v),

                ColBool("automatic", W_BOOL, SpellColumnCategory.Casting,
                    "When true the spell auto-fires repeatedly while the bind key is held.",
                    d => d.automatic, (d, v) => d.automatic = v),

                ColBool("allowMovement", W_BOOL, SpellColumnCategory.Casting,
                    "When true the caster may move during prepare/channel phases.",
                    d => d.allowMovement, (d, v) => d.allowMovement = v),

                ColBool("interruptible", W_BOOL, SpellColumnCategory.Casting,
                    "When true any movement or hit during channel cancels the cast.",
                    d => d.interruptible, (d, v) => d.interruptible = v),

                // ── Damage / Range ────────────────────────────────────────────
                ColFloat("damage", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Base impact damage. Per-hit for projectiles; per-overlap for area.",
                    d => d.damage, (d, v) => d.damage = v),

                ColFloat("damagePerTick", W_FLOAT_BIG, SpellColumnCategory.DamageRange,
                    "Damage applied every tickPeriod seconds while the effect is active (DoT / aura).",
                    d => d.damagePerTick, (d, v) => d.damagePerTick = v),

                ColFloat("tickPeriod", W_FLOAT_BIG, SpellColumnCategory.DamageRange,
                    "Seconds between DoT / aura damage ticks. 0 = no ticking.",
                    d => d.tickPeriod, (d, v) => d.tickPeriod = v),

                ColFloat("range", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Maximum travel distance in world units. 0 = use system default.",
                    d => d.range, (d, v) => d.range = v),

                ColFloat("radius", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Outer blast / aura radius in world units.",
                    d => d.radius, (d, v) => d.radius = v),

                ColFloat("hitRadius", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Inner hit-detection radius (often smaller than visual radius).",
                    d => d.hitRadius, (d, v) => d.hitRadius = v),

                ColFloat("length", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Length of slash / wall / beam effect in world units.",
                    d => d.length, (d, v) => d.length = v),

                ColFloat("distance", W_FLOAT, SpellColumnCategory.DamageRange,
                    "Dash travel distance in world units.",
                    d => d.distance, (d, v) => d.distance = v),

                ColFloat("arcRangeDegrees", W_FLOAT_BIG, SpellColumnCategory.DamageRange,
                    "Full sweep arc in degrees for slash / cone attacks.",
                    d => d.arcRangeDegrees, (d, v) => d.arcRangeDegrees = v),

                // ── VFX ───────────────────────────────────────────────────────
                ColText("vfxPreset", W_TEXT, SpellColumnCategory.VFX,
                    "Key of the ParticlePresetDefinition used for the main cast VFX.",
                    d => d.vfxPreset ?? "",
                    (d, v) => d.vfxPreset = v),

                ColText("impactPreset", W_TEXT, SpellColumnCategory.VFX,
                    "Key of the ParticlePresetDefinition played on hit / detonation.",
                    d => d.impactPreset ?? "",
                    (d, v) => d.impactPreset = v),

                ColText("particleColor", W_COLOR, SpellColumnCategory.VFX,
                    "Override tint for procedural particle preview. Hex #RRGGBB — leave white to use preset/type color.",
                    d => "#" + ColorUtility.ToHtmlStringRGB(d.particleColor),
                    (d, v) =>
                    {
                        if (ColorUtility.TryParseHtmlString(v, out var c)) d.particleColor = c;
                    }),

                ColInt("particleCount", W_INT, SpellColumnCategory.VFX,
                    "Number of particles emitted per cast burst.",
                    d => d.particleCount, (d, v) => d.particleCount = v),

                ColFloat("scale", W_FLOAT, SpellColumnCategory.VFX,
                    "Uniform scale multiplier for the spell's visual effect.",
                    d => d.scale, (d, v) => d.scale = v),

                // ── Type-specific (hidden by default) ─────────────────────────

                // Meteor
                ColInt("meteorCount", W_INT, SpellColumnCategory.TypeSpecific,
                    "Number of meteor strikes in the volley.",
                    d => d.meteorCount, (d, v) => d.meteorCount = v),

                ColFloat("meteorInterval", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Seconds between successive meteor impacts.",
                    d => d.meteorInterval, (d, v) => d.meteorInterval = v),

                ColFloat("meteorAreaRadius", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Scatter radius for meteor landing positions.",
                    d => d.meteorAreaRadius, (d, v) => d.meteorAreaRadius = v),

                ColFloat("meteorImpactRadius", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Per-impact blast radius.",
                    d => d.meteorImpactRadius, (d, v) => d.meteorImpactRadius = v),

                // Mine
                ColFloat("armingTime", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Delay before the mine becomes active after placement.",
                    d => d.armingTime, (d, v) => d.armingTime = v),

                ColFloat("triggerRadius", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Proximity detection radius that detonates the mine.",
                    d => d.triggerRadius, (d, v) => d.triggerRadius = v),

                ColFloat("explosionRadius", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Blast radius on detonation.",
                    d => d.explosionRadius, (d, v) => d.explosionRadius = v),

                ColFloat("explosionDamage", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Damage dealt at the explosion origin.",
                    d => d.explosionDamage, (d, v) => d.explosionDamage = v),

                ColFloat("ttl", W_FLOAT, SpellColumnCategory.TypeSpecific,
                    "Time-to-live in seconds before the placed effect auto-despawns. 0 = infinite.",
                    d => d.ttl, (d, v) => d.ttl = v),

                // Wall
                ColFloat("wallWidth", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Wall width in world units.",
                    d => d.wallWidth, (d, v) => d.wallWidth = v),

                ColFloat("wallHeight", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Wall height in world units.",
                    d => d.wallHeight, (d, v) => d.wallHeight = v),

                ColFloat("wallHP", W_FLOAT, SpellColumnCategory.TypeSpecific,
                    "Wall hit-points. 0 = indestructible.",
                    d => d.wallHP, (d, v) => d.wallHP = v),

                // Summon
                ColText("summonTemplate", W_TEXT, SpellColumnCategory.TypeSpecific,
                    "Monster template key for summoned units.",
                    d => d.summonTemplate ?? "",
                    (d, v) => d.summonTemplate = v),

                ColInt("summonCount", W_INT, SpellColumnCategory.TypeSpecific,
                    "Number of units to summon.",
                    d => d.summonCount, (d, v) => d.summonCount = v),

                ColFloat("summonDuration", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Seconds before summoned units expire. 0 = permanent.",
                    d => d.summonDuration, (d, v) => d.summonDuration = v),

                // Totem
                ColText("totemKind", W_TEXT, SpellColumnCategory.TypeSpecific,
                    "Totem behaviour kind: heal, damage, buff, etc.",
                    d => d.totemKind ?? "",
                    (d, v) => d.totemKind = v),

                // Cone
                ColFloat("coneArc", W_FLOAT, SpellColumnCategory.TypeSpecific,
                    "Full cone arc in degrees for ConeBreath spells.",
                    d => d.coneArc, (d, v) => d.coneArc = v),

                ColFloat("coneLength", W_FLOAT_BIG, SpellColumnCategory.TypeSpecific,
                    "Cone length in world units for ConeBreath spells.",
                    d => d.coneLength, (d, v) => d.coneLength = v),

                // Vortex
                ColFloat("force", W_FLOAT, SpellColumnCategory.TypeSpecific,
                    "Force magnitude for vortex pull/push.",
                    d => d.force, (d, v) => d.force = v),

                ColText("forceMode", W_TEXT, SpellColumnCategory.TypeSpecific,
                    "Direction of vortex force: 'pull' or 'push'.",
                    d => d.forceMode ?? "",
                    (d, v) => d.forceMode = v),
            };
        }

        // ── Default hidden columns (TypeSpecific collapsed by default) ────────

        /// <summary>
        /// Headers of columns that are hidden by default on first open.
        /// Matches the TypeSpecific category — advanced fields that most
        /// designers only need when editing a specific spell type.
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> DefaultHidden
            = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "meteorCount", "meteorInterval", "meteorAreaRadius", "meteorImpactRadius",
                "armingTime", "triggerRadius", "explosionRadius", "explosionDamage", "ttl",
                "wallWidth", "wallHeight", "wallHP",
                "summonTemplate", "summonCount", "summonDuration",
                "totemKind",
                "coneArc", "coneLength",
                "force", "forceMode",
            };

        // ── Convenience factories ─────────────────────────────────────────────

        private static SpellTableColumn ColText(string header, float width,
            SpellColumnCategory cat, string tip,
            Func<SpellDefinition, string> get,
            Action<SpellDefinition, string> set = null)
            => new SpellTableColumn(header, width, SpellTableEditorKind.Text, cat, tip, get, set);

        private static SpellTableColumn ColInt(string header, float width,
            SpellColumnCategory cat, string tip,
            Func<SpellDefinition, int> getInt,
            Action<SpellDefinition, int> setInt)
            => new SpellTableColumn(header, width, SpellTableEditorKind.Int, cat, tip,
                d => getInt(d).ToString(),
                (d, v) => { if (int.TryParse(v, out var i)) setInt(d, i); });

        private static SpellTableColumn ColFloat(string header, float width,
            SpellColumnCategory cat, string tip,
            Func<SpellDefinition, float> getF,
            Action<SpellDefinition, float> setF)
            => new SpellTableColumn(header, width, SpellTableEditorKind.Float, cat, tip,
                d => getF(d).ToString("0.###"),
                (d, v) =>
                {
                    if (float.TryParse(v,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var f))
                        setF(d, f);
                });

        private static SpellTableColumn ColBool(string header, float width,
            SpellColumnCategory cat, string tip,
            Func<SpellDefinition, bool> getB,
            Action<SpellDefinition, bool> setB)
            => new SpellTableColumn(header, width, SpellTableEditorKind.Toggle, cat, tip,
                d => getB(d).ToString(),
                (d, v) => { if (bool.TryParse(v, out var b)) setB(d, b); });

        private static SpellTableColumn ColSprite(string header, float width,
            SpellColumnCategory cat, string tip,
            Func<SpellDefinition, Sprite> getSprite)
            => new SpellTableColumn(header, width, SpellTableEditorKind.SpriteThumbnail, cat, tip,
                d => { var s = getSprite(d); return s != null ? s.name : ""; });

        private static SpellTableColumn ColDropdown(string header, float width,
            SpellColumnCategory cat, string tip,
            string[] options,
            Func<SpellDefinition, int> getIdx,
            Action<SpellDefinition, int> setIdx)
            => new SpellTableColumn(header, width, cat, tip, options, getIdx, setIdx);

        // ── Element index helper ──────────────────────────────────────────────

        private static int ElementIndex(SpellDefinition d)
        {
            if (string.IsNullOrEmpty(d.element)) return 0;
            for (int i = 0; i < _elementOptions.Length; i++)
            {
                if (string.Equals(_elementOptions[i], d.element,
                    StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }
    }
}
