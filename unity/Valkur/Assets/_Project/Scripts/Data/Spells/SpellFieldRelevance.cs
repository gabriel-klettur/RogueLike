using System.Collections.Generic;
using Valkur.Core;

namespace Valkur.Data
{
    /// <summary>
    /// Which <see cref="SpellDefinition"/> fields actually do something for a given
    /// spell.
    ///
    /// The definition is one flat bag shared by 25 spell types, so most of it is inert
    /// for any single spell: <c>wallHeight</c> means nothing to a fireball, and
    /// <c>radius</c> means nothing to <c>slash_regular</c> because its executor reads
    /// <c>hitRadius</c> instead. Showing every field to a designer invites them to tune
    /// numbers that are never read — which reads as the spell ignoring their edit.
    ///
    /// The map is derived from what the executors and controllers genuinely read.
    /// <c>SpellFieldRelevanceTests</c> scans that source and fails if an executor starts
    /// reading a field this map hides, so the two cannot drift apart silently.
    ///
    /// When in doubt the map errs towards showing a field: a spare row is a small cost,
    /// a hidden one that mattered is a designer stuck wondering why nothing happens.
    /// </summary>
    public static class SpellFieldRelevance
    {
        /// <summary>Fields every spell uses, regardless of type.</summary>
        [SelfHealingStatic("Immutable lookup table built once from string literals. Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly HashSet<string> Always = new HashSet<string>
        {
            // Identity
            "spellKey", "displayName", "type", "audience", "iconSprite",
            // Casting — enforced by SpellCaster before any executor runs
            "manaCost", "maxInstances", "allowOverlap", "allowMovement",
            "interruptible", "automatic", "automaticCastPunish", "lockCastDirection",
            // Timings
            "prepareDuration", "channelDuration", "cooldownDuration",
            // Cast origin — resolved for every spell that places anything
            "castAnchor", "castForwardOffset",
            // Telegraph — drawn by the caster, not the executor
            "telegraphColor", "telegraphAlpha",
        };

        /// <summary>
        /// Fields no spell pipeline reads at all. Carried in the asset from the Python
        /// port and kept so old assets still deserialize, but nothing acts on them.
        /// </summary>
        [SelfHealingStatic("Immutable lookup table built once from string literals. Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly HashSet<string> DeadEverywhere = new HashSet<string>
        {
            "offset", "hitArcDegrees", "length",
            "particleCount", "particleDispersion", "particleLifespan",
            "particleSpeed", "particleColors", "sizeRange", "emitRate",
        };

        [SelfHealingStatic("Immutable lookup table built once from string literals. Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly Dictionary<SpellType, HashSet<string>> ByType =
            new Dictionary<SpellType, HashSet<string>>
        {
            { SpellType.Projectile, Set("damage", "speed", "range", "lifetime", "scale", "sprite",
                                        "particleColor", "explosionRadius", "explosionDamage",
                                        "distance", "vfxPreset", "impactPreset") },
            { SpellType.Slash,      Set("damage", "hitRadius", "range", "arcRangeDegrees",
                                        "lifetime", "particleColor", "vfxPreset", "impactPreset") },
            { SpellType.Area,       Set("damage", "radius", "particleColor", "vfxPreset", "impactPreset") },
            { SpellType.Dash,       Set("distance", "duration", "collisionDamage", "knockback",
                                        "particleColor", "vfxPreset") },
            { SpellType.Teleport,   Set("distance", "particleColor", "vfxPreset") },
            { SpellType.Beam,       Set("damage", "range", "scale", "particleColor", "vfxPreset", "impactPreset") },
            { SpellType.Smoke,      Set("duration", "radius", "vfxPreset") },
            { SpellType.SmokeEmitter, Set("duration", "radius", "vfxPreset") },
            { SpellType.Wall,       Set("distance", "duration", "infinite", "particleColor", "sprite",
                                        "wallWidth", "wallHeight", "wallHP",
                                        "blockProjectiles", "blockUnits") },
            { SpellType.Boomerang,  Set("damage", "speed", "range", "hitRadius", "sprite",
                                        "particleColor", "vfxPreset", "impactPreset") },
            { SpellType.Meteor,     Set("damage", "range", "meteorCount", "meteorInterval",
                                        "meteorAreaRadius", "meteorImpactRadius", "impactPreset") },
            { SpellType.Lightning,  Set("damage", "range", "radius", "particleColor", "vfxPreset") },
            { SpellType.ChainLightning, Set("damage", "range", "radius", "particleColor", "vfxPreset") },
            { SpellType.Aura,       Set("duration", "radius", "healPerTick", "tickPeriod", "vfxPreset") },
            { SpellType.ArcaneFlame, Set("duration", "radius", "damagePerTick", "tickPeriod", "vfxPreset") },
            { SpellType.FireworkLaunch, Set("particleColor", "vfxPreset", "impactPreset") },
            { SpellType.SphereMagicShield, Set("duration", "radius", "particleColor", "vfxPreset") },
            { SpellType.Puddle,     Set("duration", "radius", "range", "distance", "damagePerTick",
                                        "tickPeriod", "element", "particleColor", "sprite",
                                        "spawnAtMouse", "vfxPreset") },
            { SpellType.Mine,       Set("damage", "armingTime", "triggerRadius", "explosionRadius",
                                        "explosionDamage", "ttl", "infinite", "scale", "sprite",
                                        "impactPreset") },
            { SpellType.VortexField, Set("duration", "radius", "range", "force", "forceMode",
                                         "followCaster", "spawnAtMouse", "vfxPreset") },
            { SpellType.ConeBreath, Set("duration", "coneArc", "coneLength", "damagePerTick",
                                        "tickPeriod", "element", "particleColor", "vfxPreset") },
            { SpellType.Summon,     Set("distance", "scale", "sprite", "summonTemplate",
                                        "summonCount", "summonDuration", "infinite") },
            { SpellType.Totem,      Set("distance", "duration", "range", "radius", "healPerTick",
                                        "tickPeriod", "totemKind", "spawnAtMouse", "vfxPreset") },
        };

        /// <summary>
        /// Spells whose executor branches to a bespoke implementation and therefore reads
        /// a different set than its type suggests. <c>slash_regular</c> owns its whole
        /// crescent in code, so the catalog VFX presets and the second shape pair
        /// (<c>radius</c> / <c>hitArcDegrees</c>) never reach it.
        /// </summary>
        [SelfHealingStatic("Immutable lookup table built once from string literals. Holds no Unity objects and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly Dictionary<string, HashSet<string>> ByKey =
            new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "slash_regular", Set("damage", "hitRadius", "range", "arcRangeDegrees",
                                   "lifetime", "particleColor") },
        };

        private static HashSet<string> Set(params string[] names) => new HashSet<string>(names);

        /// <summary>
        /// True when <paramref name="fieldName"/> changes something for this spell.
        /// A null spell shows everything — a form with no selection has nothing to filter by.
        /// </summary>
        public static bool Applies(SpellDefinition spell, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return false;
            if (spell == null) return !DeadEverywhere.Contains(fieldName);

            if (Always.Contains(fieldName)) return true;
            if (DeadEverywhere.Contains(fieldName)) return false;

            // A per-key override replaces the type set outright; that is the point of it.
            if (!string.IsNullOrEmpty(spell.spellKey) &&
                ByKey.TryGetValue(spell.spellKey, out var keyed))
                return keyed.Contains(fieldName);

            return ByType.TryGetValue(spell.type, out var typed) && typed.Contains(fieldName);
        }

        /// <summary>Fields that do something for this spell's type, ignoring per-key overrides.</summary>
        public static IReadOnlyCollection<string> FieldsForType(SpellType type)
            => ByType.TryGetValue(type, out var set) ? set : new HashSet<string>();

        /// <summary>True when this type has no executor of its own and falls back to Projectile.</summary>
        public static bool HasOwnFieldSet(SpellType type) => ByType.ContainsKey(type);
    }
}
