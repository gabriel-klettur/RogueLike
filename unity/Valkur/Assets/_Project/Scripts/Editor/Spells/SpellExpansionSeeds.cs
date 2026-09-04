using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Spells
{
    /// <summary>
    /// The authored data for the 27-spell expansion, as one table.
    ///
    /// <para>A table rather than 27 hand-made assets for the same reason
    /// <c>SpellTreeSeeds</c> and <c>classify.py</c> are tables: the values are reviewable
    /// side by side, a balance pass is a diff, and nothing depends on someone remembering to
    /// fill the same field on every one of them. The seeder that consumes this fills a field
    /// only when it CREATES the asset — an authored value always wins on a re-run, which is
    /// the same contract <c>TilesetRulesetImporter</c> and the persona importer use.</para>
    ///
    /// <para>Every entry authors <c>range</c>, <c>element</c> and <c>particleColor</c>
    /// deliberately. Those three are the ones this project has repeatedly left at a default
    /// that quietly means something else: <c>Projectile.range</c> defaults to 20 and has
    /// silently truncated two shipped spells, an empty <c>element</c> falls through a legacy
    /// key switch whose own comment says not to grow it, and opaque white is the
    /// "nobody authored this" sentinel that ten shipped spells are still sitting on.</para>
    /// </summary>
    internal static class SpellExpansionSeeds
    {
        internal sealed class Spec
        {
            public string Key;
            public string Name;
            public SpellType Type;
            public string School;
            public SpellRole Role;

            public int NodeCost = 1;
            public int NodeLevel;
            public string Prerequisite;     // spell key of the node before it

            public float Mana;
            public float Cooldown;
            public float Damage;
            public float Speed;
            public float Range;
            public float Radius;
            public float HitRadius;
            public float Duration;
            public float DamagePerTick;
            public float TickPeriod;
            public float HealPerTick;
            public float Lifetime;
            public float Distance;
            public float Knockback;
            public float Scale = 1f;
            public int MaxInstances;
            public bool SpawnAtMouse;
            public bool UsesAttackAnimation;
            public string Element = "";
            public Color Swatch = Color.white;
            public SpellCastAnchor Anchor = SpellCastAnchor.Hands;

            // Projectile mechanics
            public int PierceCount;
            public float PierceFalloff;
            public float HomingStrength;
            public float HomingRange;
            public int ProjectileCount = 1;
            public float SpreadDegrees;

            // Charge
            public float ChargeMaxSeconds;
            public float ChargeMinFraction = 0.45f;
            public float ChargeDamageMultiplier = 1f;
            public float ChargeScaleMultiplier = 1f;
            public float ExplosionRadius;
            public float ExplosionDamage;

            // Buff
            public StatModifier[] Mods = System.Array.Empty<StatModifier>();
            public string BuffKey;

            // Wall
            public float WallWidth;
            public float WallHeight;
            public float WallHP;
            public bool BlockProjectiles;
            public bool BlockUnits;

            // Totem / summon
            public string TotemKind;
            public string SummonTemplate;
            public int SummonCount = 1;
            public float SummonDuration;

            // Ttl for a trailing patch
            public float Ttl;
            public bool FollowCaster;

            public StatusApplication[] Statuses = System.Array.Empty<StatusApplication>();

            public string Description;
        }

        private static StatusApplication St(StatusEffectKind kind, float duration,
                                           float magnitude, float chance)
            => new StatusApplication { type = kind, duration = duration, magnitude = magnitude, chance = chance };

        private static StatModifier[] Mods(params StatModifier[] m) => m;

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // ── The 27 ───────────────────────────────────────────────────────────
        //
        // Ordered by school so the table reads the way the grimoire does. Costs and level
        // gates rise with depth, so going deep in one school and broad across several are
        // genuinely different builds rather than the same points spent in a different order.

        public static readonly Spec[] All =
        {
            // ══ Cryomancy: 2 nodes to 7 ═════════════════════════════════════
            new Spec {
                Key = "frost_nova", Name = "Frost Nova", Type = SpellType.Area,
                School = "cryomancy", Role = SpellRole.Control,
                NodeCost = 1, NodeLevel = 5, Prerequisite = "iceball",
                Mana = 14, Cooldown = 6, Damage = 18, Radius = 3.2f,
                Element = "Ice", Swatch = C(0.62f, 0.88f, 1f), Anchor = SpellCastAnchor.Feet,
                Statuses = new[] {
                    St(StatusEffectKind.Slow, 3.0f, 0.55f, 1.0f),
                    St(StatusEffectKind.Freeze, 1.2f, 0f, 0.30f),
                },
                Description = "A ring of ice travels out across the floor. Everything it " +
                              "reaches is slowed; a few freeze outright.",
            },
            new Spec {
                Key = "ice_lance", Name = "Ice Lance", Type = SpellType.Projectile,
                School = "cryomancy", Role = SpellRole.Damage,
                NodeCost = 2, NodeLevel = 10, Prerequisite = "frost_nova",
                Mana = 16, Cooldown = 3.5f, Damage = 30, Speed = 22, Range = 14, HitRadius = 0.5f,
                PierceCount = 4, PierceFalloff = 0.20f,
                Element = "Ice", Swatch = C(0.70f, 0.92f, 1f),
                Statuses = new[] { St(StatusEffectKind.Slow, 2.0f, 0.70f, 0.60f) },
                Description = "A shard that passes through everything in a line, losing a " +
                              "fifth of its bite with each body.",
            },
            new Spec {
                Key = "glacial_step", Name = "Glacial Step", Type = SpellType.Teleport,
                School = "cryomancy", Role = SpellRole.Mobility,
                NodeCost = 2, NodeLevel = 12, Prerequisite = "frost_nova",
                Mana = 18, Cooldown = 9, Range = 7, Damage = 10, Radius = 1.9f, Duration = 3f,
                Distance = 7, SpawnAtMouse = true,
                Element = "Ice", Swatch = C(0.55f, 0.85f, 1f),
                Statuses = new[] { St(StatusEffectKind.Slow, 2.5f, 0.6f, 1.0f) },
                Description = "Blink, and leave both ends of the step frozen.",
            },
            new Spec {
                Key = "frozen_ward", Name = "Frozen Ward", Type = SpellType.Buff,
                School = "cryomancy", Role = SpellRole.Protection,
                NodeCost = 2, NodeLevel = 16, Prerequisite = "ice_lance",
                Mana = 20, Cooldown = 14, Duration = 8,
                Element = "Ice", Swatch = C(0.60f, 0.86f, 1f), BuffKey = "frozen_ward",
                Mods = Mods(
                    StatModifier.Flat(StatKind.Defense, 8f),
                    StatModifier.Percent(StatKind.MoveSpeed, -0.20f),
                    StatModifier.Percent(StatKind.MeleeDamage, 0.10f)),
                Description = "Armour of ice. Harder to hurt, slower to move — a trade, not " +
                              "a gift.",
            },
            new Spec {
                Key = "blizzard", Name = "Blizzard", Type = SpellType.Puddle,
                School = "cryomancy", Role = SpellRole.Control,
                NodeCost = 3, NodeLevel = 22, Prerequisite = "frozen_ward",
                Mana = 26, Cooldown = 16, DamagePerTick = 6, TickPeriod = 0.5f,
                Radius = 4.0f, Duration = 8, Range = 9, SpawnAtMouse = true, MaxInstances = 1,
                Element = "Ice", Swatch = C(0.72f, 0.90f, 1f),
                Statuses = new[] {
                    St(StatusEffectKind.Slow, 1.5f, 0.45f, 1.0f),
                    St(StatusEffectKind.Freeze, 1.0f, 0f, 0.12f),
                },
                Description = "A standing storm. Little damage, a great deal of ground you " +
                              "would rather not cross.",
            },

            // ══ Verdant Rites: 3 nodes to 8 ═════════════════════════════════
            new Spec {
                Key = "thorn_burst", Name = "Thorn Burst", Type = SpellType.Area,
                School = "verdant", Role = SpellRole.Damage,
                NodeCost = 1, NodeLevel = 5, Prerequisite = "laser_beam_green",
                Mana = 12, Cooldown = 5, Damage = 16, Radius = 2.8f,
                Element = "Nature", Swatch = C(0.50f, 0.88f, 0.42f), Anchor = SpellCastAnchor.Feet,
                Statuses = new[] { St(StatusEffectKind.Poison, 5.0f, 4f, 0.85f) },
                Description = "The ground cracks, and thorns come up through it.",
            },
            new Spec {
                Key = "entangle", Name = "Entangle", Type = SpellType.Area,
                School = "verdant", Role = SpellRole.Control,
                NodeCost = 2, NodeLevel = 9, Prerequisite = "thorn_burst",
                Mana = 16, Cooldown = 10, Damage = 0, Radius = 2.6f, Range = 8, SpawnAtMouse = true,
                Element = "Nature", Swatch = C(0.42f, 0.80f, 0.36f),
                Statuses = new[] { St(StatusEffectKind.Root, 3.0f, 0f, 1.0f) },
                Description = "Roots take the feet and nothing else. No damage at all — a " +
                              "control spell that also hurts is a damage spell.",
            },
            new Spec {
                Key = "barkskin", Name = "Barkskin", Type = SpellType.Buff,
                School = "verdant", Role = SpellRole.Protection,
                NodeCost = 2, NodeLevel = 13, Prerequisite = "entangle",
                Mana = 18, Cooldown = 18, Duration = 12,
                Element = "Nature", Swatch = C(0.55f, 0.78f, 0.35f), BuffKey = "barkskin",
                Mods = Mods(
                    StatModifier.Flat(StatKind.MaxHp, 40f),
                    StatModifier.Flat(StatKind.Defense, 5f),
                    StatModifier.Percent(StatKind.ManaRegen, 0.15f)),
                Description = "Bark grows up from the feet. The school's answer to a long fight.",
            },
            new Spec {
                Key = "spore_cloud", Name = "Spore Cloud", Type = SpellType.SmokeEmitter,
                School = "verdant", Role = SpellRole.Control,
                NodeCost = 2, NodeLevel = 16, Prerequisite = "entangle",
                Mana = 20, Cooldown = 12, DamagePerTick = 4, TickPeriod = 0.6f,
                Radius = 3.0f, Duration = 7, Range = 8, SpawnAtMouse = true, MaxInstances = 2,
                Element = "Nature", Swatch = C(0.62f, 0.80f, 0.38f),
                Statuses = new[] {
                    St(StatusEffectKind.Poison, 4.0f, 5f, 0.9f),
                    St(StatusEffectKind.Slow, 1.2f, 0.8f, 0.5f),
                },
                Description = "A cloud that lingers, poisons, and makes the corridor behind " +
                              "it somebody else's problem.",
            },
            new Spec {
                Key = "summon_wolf", Name = "Summon Wolf", Type = SpellType.Summon,
                School = "verdant", Role = SpellRole.Summon,
                NodeCost = 3, NodeLevel = 20, Prerequisite = "barkskin",
                Mana = 28, Cooldown = 25, Range = 4, Distance = 2.5f, SpawnAtMouse = true,
                SummonCount = 1, SummonDuration = 20, MaxInstances = 1,
                Element = "Nature", Swatch = C(0.48f, 0.86f, 0.44f),
                Description = "It rises through the floor, hunts what you are hunting, and " +
                              "sinks back when its time is up.",
            },

            // ══ Umbramancy: 4 nodes to 8 ═══════════════════════════════════
            new Spec {
                Key = "shadow_step", Name = "Shadow Step", Type = SpellType.Teleport,
                School = "shadow", Role = SpellRole.Mobility,
                NodeCost = 2, NodeLevel = 10, Prerequisite = "smoke",
                Mana = 16, Cooldown = 8, Range = 8, Distance = 8, Duration = 0.5f, SpawnAtMouse = true,
                Element = "Dark", Swatch = C(0.55f, 0.40f, 0.72f),
                Description = "Step through the dark and arrive half a second behind the world.",
            },
            new Spec {
                Key = "void_lance", Name = "Void Lance", Type = SpellType.Projectile,
                School = "shadow", Role = SpellRole.Damage,
                NodeCost = 2, NodeLevel = 14, Prerequisite = "laser_beam_black",
                Mana = 18, Cooldown = 4, Damage = 26, Speed = 19, Range = 13, HitRadius = 0.45f,
                PierceCount = 2, PierceFalloff = 0f,
                Element = "Dark", Swatch = C(0.48f, 0.30f, 0.68f),
                Statuses = new[] { St(StatusEffectKind.Poison, 4.0f, 5f, 0.7f) },
                Description = "Fewer bodies than the ice lance, no falloff at all, and it " +
                              "leaves something in the wound.",
            },
            new Spec {
                Key = "curse_of_frailty", Name = "Curse of Frailty", Type = SpellType.Projectile,
                School = "shadow", Role = SpellRole.Control,
                NodeCost = 3, NodeLevel = 18, Prerequisite = "void_lance",
                Mana = 20, Cooldown = 12, Damage = 5, Speed = 24, Range = 12, Duration = 8,
                Element = "Dark", Swatch = C(0.62f, 0.32f, 0.60f),
                Statuses = new[] { St(StatusEffectKind.Vulnerable, 8.0f, 0.30f, 1.0f) },
                Description = "One target takes more from everything, for a while. The party " +
                              "spell in a game with no party.",
            },
            new Spec {
                Key = "raise_thrall", Name = "Raise Thrall", Type = SpellType.Projectile,
                School = "shadow", Role = SpellRole.Summon,
                NodeCost = 3, NodeLevel = 24, Prerequisite = "curse_of_frailty",
                Mana = 30, Cooldown = 28, Damage = 0, Speed = 20, Range = 11,
                Duration = 10, SummonDuration = 18, MaxInstances = 1,
                Element = "Dark", Swatch = C(0.60f, 0.35f, 0.75f),
                // duration is the MARK's window; magnitude is how long the thrall serves.
                // Two clocks on one application, which is the honest shape: the bet has a
                // deadline and the payout has a length.
                Statuses = new[] { St(StatusEffectKind.Marked, 10.0f, 18f, 1.0f) },
                Description = "Mark a living enemy. Kill it before the mark fades and it " +
                              "gets up on your side. Let it live and the cast was wasted.",
            },

            // ══ Radiance: 5 nodes to 9 ═════════════════════════════════════
            new Spec {
                Key = "radiant_burst", Name = "Radiant Burst", Type = SpellType.Area,
                School = "radiance", Role = SpellRole.Damage,
                NodeCost = 1, NodeLevel = 6, Prerequisite = "lightball",
                Mana = 15, Cooldown = 7, Damage = 22, Radius = 3.0f, HealPerTick = 18,
                Element = "Light", Swatch = C(1f, 0.93f, 0.68f), Anchor = SpellCastAnchor.Center,
                Description = "Light detonates on the caster. It hurts everything near and " +
                              "gives some of it back.",
            },
            new Spec {
                Key = "blessing", Name = "Blessing", Type = SpellType.Buff,
                School = "radiance", Role = SpellRole.Healing,
                NodeCost = 2, NodeLevel = 11, Prerequisite = "radiant_burst",
                Mana = 18, Cooldown = 20, Duration = 15,
                Element = "Light", Swatch = C(1f, 0.95f, 0.75f), BuffKey = "blessing",
                Mods = Mods(
                    StatModifier.Percent(StatKind.SpellPower, 0.18f),
                    StatModifier.Flat(StatKind.ManaRegen, 2f),
                    StatModifier.Percent(StatKind.XpGain, 0.05f)),
                Description = "A shaft of light, and then fifteen quiet seconds of being " +
                              "better at this.",
            },
            new Spec {
                Key = "sanctuary", Name = "Sanctuary", Type = SpellType.Totem,
                School = "radiance", Role = SpellRole.Healing,
                NodeCost = 3, NodeLevel = 17, Prerequisite = "blessing",
                Mana = 24, Cooldown = 22, HealPerTick = 10, TickPeriod = 1.0f,
                Radius = 3.4f, Duration = 10, Range = 6, Distance = 3, SpawnAtMouse = true,
                MaxInstances = 1, TotemKind = "heal",
                Element = "Light", Swatch = C(1f, 0.94f, 0.70f),
                Description = "A pillar and a circle. Everything friendly inside it mends, " +
                              "on a beat you can count.",
            },
            new Spec {
                Key = "guardian_light", Name = "Guardian Light", Type = SpellType.SphereMagicShield,
                School = "radiance", Role = SpellRole.Protection,
                NodeCost = 3, NodeLevel = 21, Prerequisite = "sanctuary",
                Mana = 22, Cooldown = 24, Duration = 10, Radius = 1.1f, WallHP = 120,
                Element = "Light", Swatch = C(1f, 0.96f, 0.80f),
                Description = "A shell that absorbs a fixed amount and then breaks. Not " +
                              "invincibility — a number you can watch running out.",
            },

            // ══ Stormcalling: 4 nodes to 7 ═════════════════════════════════
            new Spec {
                Key = "seeking_shard", Name = "Seeking Shard", Type = SpellType.Projectile,
                School = "storm", Role = SpellRole.Damage,
                NodeCost = 2, NodeLevel = 9, Prerequisite = "lightning",
                Mana = 14, Cooldown = 3, Damage = 22, Speed = 11, Range = 16, HitRadius = 0.4f,
                HomingStrength = 220f, HomingRange = 6f,
                Element = "Lightning", Swatch = C(1f, 0.95f, 0.45f),
                Statuses = new[] { St(StatusEffectKind.Stun, 0.5f, 0f, 0.20f) },
                Description = "Slower than anything else you can throw, and it does not miss.",
            },
            new Spec {
                Key = "thunderclap", Name = "Thunderclap", Type = SpellType.Area,
                School = "storm", Role = SpellRole.Control,
                NodeCost = 2, NodeLevel = 14, Prerequisite = "seeking_shard",
                Mana = 18, Cooldown = 11, Damage = 20, Radius = 3.6f,
                Element = "Lightning", Swatch = C(1f, 0.97f, 0.60f), Anchor = SpellCastAnchor.Center,
                Statuses = new[] { St(StatusEffectKind.Stun, 1.4f, 0f, 0.85f) },
                Description = "A flat crack of sound and light. Little damage, and everyone " +
                              "near it stops.",
            },
            new Spec {
                Key = "static_field", Name = "Static Field", Type = SpellType.Aura,
                School = "storm", Role = SpellRole.Damage,
                NodeCost = 3, NodeLevel = 19, Prerequisite = "thunderclap",
                Mana = 22, Cooldown = 15, DamagePerTick = 7, TickPeriod = 0.5f,
                Radius = 2.6f, Duration = 8, MaxInstances = 1,
                Element = "Lightning", Swatch = C(0.95f, 0.92f, 0.50f),
                Statuses = new[] { St(StatusEffectKind.Slow, 0.8f, 0.85f, 0.35f) },
                Description = "A dome you carry. Whatever stays near you gets shocked for it.",
            },

            // ══ Martial Forms: 5 nodes to 8 ════════════════════════════════
            new Spec {
                Key = "scatter_volley", Name = "Scatter Volley", Type = SpellType.Projectile,
                School = "martial", Role = SpellRole.Damage,
                NodeCost = 2, NodeLevel = 10, Prerequisite = "dash",
                Mana = 12, Cooldown = 5, Damage = 11, Speed = 20, Range = 9, HitRadius = 0.35f,
                ProjectileCount = 5, SpreadDegrees = 46f, UsesAttackAnimation = true,
                // element deliberately EMPTY: steel is not an element, and setting one would
                // silently couple the spell to that resistance through Health.MitigateDamage.
                Element = "", Swatch = C(0.86f, 0.86f, 0.82f),
                Description = "Five blades in a fan. Each one is nothing; the cone is not.",
            },
            new Spec {
                Key = "war_cry", Name = "War Cry", Type = SpellType.Buff,
                School = "martial", Role = SpellRole.Protection,
                NodeCost = 2, NodeLevel = 15, Prerequisite = "scatter_volley",
                Mana = 14, Cooldown = 20, Duration = 10,
                Element = "", Swatch = C(0.95f, 0.72f, 0.45f), BuffKey = "war_cry",
                Mods = Mods(
                    StatModifier.Percent(StatKind.MeleeDamage, 0.25f),
                    StatModifier.Percent(StatKind.MoveSpeed, 0.12f),
                    StatModifier.Percent(StatKind.MeleeCooldown, -0.10f)),
                Description = "No projectile, no target, no subtlety.",
            },
            new Spec {
                Key = "leap_slam", Name = "Leap Slam", Type = SpellType.Dash,
                School = "martial", Role = SpellRole.Mobility,
                NodeCost = 3, NodeLevel = 20, Prerequisite = "war_cry",
                Mana = 20, Cooldown = 12, Distance = 6, Range = 6, Damage = 30, Radius = 2.6f,
                Knockback = 6, Duration = 0.35f, SpawnAtMouse = true, UsesAttackAnimation = true,
                Element = "", Swatch = C(0.90f, 0.80f, 0.62f),
                Statuses = new[] { St(StatusEffectKind.Stun, 0.8f, 0f, 0.5f) },
                Description = "Go there. Land hard.",
            },

            // ══ Pyromancy: 6 nodes to 8 ════════════════════════════════════
            new Spec {
                Key = "charged_bolt", Name = "Charged Bolt", Type = SpellType.Projectile,
                School = "pyromancy", Role = SpellRole.Damage,
                NodeCost = 2, NodeLevel = 8, Prerequisite = "laser_beam_red",
                Mana = 12, Cooldown = 4, Damage = 24, Speed = 15, Range = 15, HitRadius = 0.55f,
                ChargeMaxSeconds = 1.6f, ChargeMinFraction = 0.45f,
                ChargeDamageMultiplier = 2.6f, ChargeScaleMultiplier = 2.0f,
                ExplosionRadius = 1.8f, ExplosionDamage = 18,
                Element = "Fire", Swatch = C(1f, 0.48f, 0.14f),
                Statuses = new[] { St(StatusEffectKind.Burn, 4.0f, 6f, 0.75f) },
                Description = "Hold it to grow it. Let go early and it is a worse fireball; " +
                              "hold it out and it is the hardest single hit in the school.",
            },
            new Spec {
                Key = "cinder_trail", Name = "Cinder Trail", Type = SpellType.Puddle,
                School = "pyromancy", Role = SpellRole.Control,
                NodeCost = 3, NodeLevel = 18, Prerequisite = "charged_bolt",
                Mana = 22, Cooldown = 16, DamagePerTick = 7, TickPeriod = 0.5f,
                Radius = 1.2f, Duration = 8, Ttl = 3.5f, FollowCaster = true, MaxInstances = 1,
                Element = "Fire", Swatch = C(1f, 0.42f, 0.10f),
                Statuses = new[] { St(StatusEffectKind.Burn, 3.0f, 5f, 0.8f) },
                Description = "For a while, the ground behind you burns. The only spell here " +
                              "that rewards running away.",
            },

            // ══ Arcana: 8 nodes to 9 ═══════════════════════════════════════
            new Spec {
                Key = "arcane_barrier", Name = "Arcane Barrier", Type = SpellType.Wall,
                School = "arcane", Role = SpellRole.Protection,
                NodeCost = 2, NodeLevel = 16, Prerequisite = "mine_basic",
                Mana = 16, Cooldown = 10, Duration = 7, Range = 5, Distance = 5,
                SpawnAtMouse = true, MaxInstances = 1,
                // WORLD UNITS. wall_ice authored 12.5 x 3.125 against an executor dividing by
                // 32 and resolved to 0.78 u by 0.049 u -- twelve screen pixels by less than
                // one. The first of five Python-pixel sightings in this project.
                WallWidth = 4.5f, WallHeight = 0.35f, WallHP = 80,
                BlockProjectiles = true, BlockUnits = false,
                Element = "Arcane", Swatch = C(0.72f, 0.48f, 1f),
                Description = "Stops shots and lets bodies through. Cover against casters, " +
                              "useless against a rush.",
            },
        };

        /// <summary>Every key this table authors, for the coverage check.</summary>
        public static HashSet<string> Keys()
        {
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in All) set.Add(s.Key);
            return set;
        }
    }
}
