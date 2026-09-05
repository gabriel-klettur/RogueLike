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
            // Status effects — rolled by StatusApplicationFactory.ApplyAll wherever a spell's
            // damage seam already runs (Projectile, Area, Slash, Lightning, Dash, Meteor,
            // Mine, Puddle, Beam, Boomerang today). Kept universal rather than per-type: a
            // status application is a general-purpose combat knob any damaging spell could
            // grow, the same reasoning that keeps the telegraph fields universal above.
            "statusApplications",
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
            // The six mechanics fields and the four charge dials are all read by
            // ProjectileExecutor / Projectile, so all ten belong here. hitRadius joins them:
            // it was missing while the executor has always been free to use it, and the four
            // new projectile spells author it.
            { SpellType.Projectile, Set("damage", "speed", "range", "lifetime", "scale", "sprite",
                                        "particleColor", "explosionRadius", "explosionDamage",
                                        "distance", "hitRadius", "vfxPreset", "impactPreset",
                                        "pierceCount", "pierceDamageFalloff",
                                        "homingStrength", "homingRange",
                                        "projectileCount", "spreadDegrees",
                                        "chargeMaxSeconds", "chargeMinFraction",
                                        "chargeDamageMultiplier", "chargeScaleMultiplier") },
            { SpellType.Slash,      Set("damage", "hitRadius", "range", "arcRangeDegrees",
                                        "lifetime", "particleColor", "vfxPreset", "impactPreset") },
            // spawnAtMouse: AreaExecutor now aims through SpellTargeting instead of always
            // detonating `radius` in front of the caster, so where the burst lands is authored.
            { SpellType.Area,       Set("damage", "radius", "particleColor", "vfxPreset", "impactPreset",
                                        "spawnAtMouse", "range", "castAnchor", "statusApplications") },
            { SpellType.Dash,       Set("distance", "duration", "collisionDamage", "knockback",
                                        "particleColor", "vfxPreset") },
            // radius/damage/statusApplications: a blink now applies its authored area at BOTH
            // ends (glacial_step froze nothing for its whole life), so those three stopped
            // being decoration on the asset and became the spell.
            { SpellType.Teleport,   Set("distance", "particleColor", "vfxPreset",
                                        "radius", "damage", "spawnAtMouse", "range",
                                        "duration", "statusApplications") },
            // No damage, no geometry, no lifetime: it changes which sprites the caster is
            // drawn with and nothing else.
            { SpellType.WeaponLoadout, Set("loadoutKey", "vfxPreset") },
            // A probe has no geometry, no damage and no lifetime — only which animation it
            // asks the preview to play.
            { SpellType.AnimationProbe, Set("animState", "loadoutAnimKey") },
            { SpellType.Beam,       Set("damage", "range", "scale", "particleColor", "vfxPreset", "impactPreset") },
            // `particleColor` is on EVERY type below that shows a cast flourish, whether or
            // not that type's executor reads it: since SpellCastFlourishFX started taking its
            // hue from the swatch, the field is live for all of them, and a live field the F4
            // panel hides is one a designer cannot author. The two types deliberately without
            // it are WeaponLoadout and AnimationProbe, which AppliesTo refuses outright.
            { SpellType.Smoke,      Set("duration", "radius", "vfxPreset", "particleColor") },
            // The cloud does damage now. SmokeEmitterExecutor was 32 lines with no Physics2D
            // call at all, so spore_cloud's authored damagePerTick/tickPeriod/Poison/Slow
            // reached zero code — the panel hid them because nothing read them.
            { SpellType.SmokeEmitter, Set("duration", "radius", "vfxPreset", "particleColor",
                                          "damagePerTick", "tickPeriod", "element",
                                          "spawnAtMouse", "range", "maxInstances",
                                          "statusApplications") },
            { SpellType.Wall,       Set("distance", "duration", "infinite", "particleColor", "sprite",
                                        "wallWidth", "wallHeight", "wallHP",
                                        "blockProjectiles", "blockUnits") },
            { SpellType.Boomerang,  Set("damage", "speed", "range", "hitRadius", "sprite",
                                        "particleColor", "vfxPreset", "impactPreset") },
            { SpellType.Meteor,     Set("damage", "range", "meteorCount", "meteorInterval",
                                        "meteorAreaRadius", "meteorImpactRadius", "impactPreset", "particleColor") },
            { SpellType.Lightning,  Set("damage", "range", "radius", "particleColor", "vfxPreset") },
            { SpellType.ChainLightning, Set("damage", "range", "radius", "particleColor", "vfxPreset") },
            // damagePerTick is the DISCRIMINATOR between a healing aura and a damaging one,
            // so hiding it would hide the control that chooses which spell this is.
            { SpellType.Aura,       Set("duration", "radius", "healPerTick", "damagePerTick",
                                        "tickPeriod", "vfxPreset", "particleColor") },
            { SpellType.ArcaneFlame, Set("duration", "radius", "damagePerTick", "tickPeriod", "vfxPreset", "particleColor") },
            // range is the APEX HEIGHT, speed the CLIMB SPEED and radius the BURST RADIUS,
            // all in world units — FireworkLaunchExecutor reads exactly these three plus the
            // swatch. No vfxPreset: the shell is drawn by FireworkShellController and
            // FireworkBurstFX off that swatch, so a preset spawned on top of it would be an
            // uncoordinated extra layer, the same reason VortexField carries none. It used to
            // list vfxPreset and impactPreset and NOT the three numbers that actually aim the
            // spell, so the panel showed two dead controls and hid every live one.
            { SpellType.FireworkLaunch, Set("range", "speed", "radius", "particleColor") },
            // wallHP is reused as the ABSORB POOL: how much damage the shell turns away
            // before it breaks. Zero keeps the historical pure-timer shield.
            { SpellType.SphereMagicShield, Set("duration", "radius", "wallHP",
                                               "particleColor", "vfxPreset") },
            // No vfxPreset: PuddleExecutor has never read it. It was used as a BEHAVIOUR
            // SWITCH (the string "root_whip", a preset that never existed), which left a
            // permanently unresolved reference in the catalog and a control in F4 that
            // could not do anything. The discriminator is the spell key, and the root
            // field draws itself, so a preset spawned on top would be an uncoordinated
            // extra layer — the same reason VortexField and FireworkLaunch carry none.
            // ttl and followCaster turn a standing pool into a TRAIL: the emitter rides the
            // caster for `duration` and each patch it drops lives `ttl`.
            { SpellType.Puddle,     Set("duration", "radius", "range", "distance", "damagePerTick",
                                        "tickPeriod", "element", "particleColor", "sprite",
                                        "spawnAtMouse", "ttl", "followCaster") },
            { SpellType.Mine,       Set("damage", "armingTime", "triggerRadius", "explosionRadius",
                                        "explosionDamage", "ttl", "infinite", "scale", "sprite",
                                        "impactPreset", "particleColor") },
            // No vfxPreset: the funnel is built by VortexFunnelFX off the swatch, and the
            // preset the executor used to spawn on top of it was a fourth uncoordinated layer.
            { SpellType.VortexField, Set("duration", "radius", "range", "force", "forceMode",
                                         "followCaster", "spawnAtMouse", "particleColor") },
            { SpellType.ConeBreath, Set("duration", "coneArc", "coneLength", "damagePerTick",
                                        "tickPeriod", "element", "particleColor", "vfxPreset") },
            { SpellType.Summon,     Set("distance", "scale", "sprite", "summonTemplate",
                                        "summonCount", "summonDuration", "infinite", "particleColor") },
            { SpellType.Totem,      Set("distance", "duration", "range", "radius", "healPerTick",
                                        "tickPeriod", "totemKind", "spawnAtMouse", "vfxPreset", "particleColor") },
            // Visual only. `scale` is the INTENSITY dial (see EnergyChargeExecutor) and
            // `particleColor` is the one swatch the whole aura palette is derived from, so
            // those two are the entire authoring surface for a charge.
            { SpellType.EnergyCharge, Set("duration", "infinite", "radius", "scale", "particleColor") },
            // A buff has no geometry at all: what it does is entirely statModifiers, and how
            // long it does it for is duration. buffKey is the refresh key, which a designer
            // needs to see in order to make two spells deliberately replace each other.
            // particleColor drives BuffAuraFX's whole palette, so it is as load-bearing here
            // as it is on a charge.
            { SpellType.Buff,       Set("duration", "statModifiers", "buffKey", "particleColor") },
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
