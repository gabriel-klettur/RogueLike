using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class SpellDataImporter
    {

        private static void ApplyEffect(SpellDefinition spell, Dictionary<string, object> effect)
        {
            // Common fields
            spell.damage = GetFloat(effect, "damage");
            spell.duration = GetFloat(effect, "duration");

            // Projectile/Boomerang
            spell.range = GetFloat(effect, "range") * PX_TO_WORLD;
            spell.speed = GetFloat(effect, "speed") * SPEED_TO_WORLD;
            spell.lifetime = GetFloat(effect, "lifetime") * TICK_TO_SEC;

            // Area/Slash
            spell.radius = GetFloat(effect, "radius") * PX_TO_WORLD;
            spell.hitRadius = GetFloat(effect, "hit_radius") * PX_TO_WORLD;
            spell.arcRangeDegrees = GetFloat(effect, "arc_range_degrees");
            spell.hitArcDegrees = GetFloat(effect, "hit_arc_degrees");
            spell.length = GetFloat(effect, "length") * PX_TO_WORLD;

            // Dash
            spell.distance = GetFloat(effect, "distance") * PX_TO_WORLD;
            spell.knockback = GetFloat(effect, "knockback");
            spell.collisionDamage = GetFloat(effect, "collision_damage");

            // DoT / Aura
            spell.damagePerTick = GetFloat(effect, "damage_per_tick");
            spell.tickPeriod = GetFloat(effect, "tick_period");
            spell.element = GetString(effect, "element", "");
            spell.healPerTick = GetFloat(effect, "heal_per_tick");

            // Heal from buff sub-object (healing_aura)
            var buff = GetDict(effect, "buff");
            if (buff != null)
                spell.healPerTick = GetFloat(buff, "heal_per_second");

            // Vortex / Force
            spell.force = GetFloat(effect, "force") * PX_TO_WORLD;
            string mode = GetString(effect, "mode", "");
            if (!string.IsNullOrEmpty(mode)) spell.forceMode = mode;
            spell.followCaster = GetBool(effect, "follow_caster");
            spell.spawnAtMouse = GetString(effect, "spawn_at", "") == "mouse";

            // Meteor
            spell.meteorCount = GetInt(effect, "count");
            spell.meteorInterval = GetFloat(effect, "interval");
            spell.meteorAreaRadius = GetFloat(effect, "area_radius") * PX_TO_WORLD;
            spell.meteorImpactRadius = GetFloat(effect, "impact_radius") * PX_TO_WORLD;
            if (effect.ContainsKey("impact_damage"))
                spell.damage = GetFloat(effect, "impact_damage");

            // Mine
            spell.armingTime = GetFloat(effect, "arming_time");
            spell.triggerRadius = GetFloat(effect, "trigger_radius") * PX_TO_WORLD;
            spell.ttl = GetFloat(effect, "ttl");
            var payload = GetDict(effect, "payload");
            if (payload != null)
            {
                var explosion = GetDict(payload, "explosion");
                if (explosion != null)
                {
                    spell.explosionRadius = GetFloat(explosion, "radius") * PX_TO_WORLD;
                    spell.explosionDamage = GetFloat(explosion, "damage");
                }
            }

            // Wall
            spell.wallWidth = GetFloat(effect, "width") * PX_TO_WORLD;
            spell.wallHeight = GetFloat(effect, "height") * PX_TO_WORLD;
            spell.wallHP = GetFloat(effect, "hp");
            spell.blockProjectiles = GetBool(effect, "blocks_projectiles");
            spell.blockUnits = GetBool(effect, "blocks_units");

            // Summon
            spell.summonTemplate = GetString(effect, "template_id", "");
            spell.summonCount = GetInt(effect, "count", effect.ContainsKey("template_id") ? 1 : 0);
            spell.summonDuration = GetFloat(effect, "duration");

            // Totem
            spell.totemKind = GetString(effect, "kind", "");

            // Cone Breath
            spell.coneArc = GetFloat(effect, "arc_range_degrees");
            spell.coneLength = GetFloat(effect, "length") * PX_TO_WORLD;

            // Boomerang extras
            // return_speed field not in SpellDefinition — ok to skip

            // Chain Lightning
            // max_bounces, damage_decay not in SpellDefinition yet — acceptable, executor uses defaults
        }

        private static void ApplyVfx(SpellDefinition spell, Dictionary<string, object> vfx)
        {
            spell.vfxPreset = GetString(vfx, "preset", "");

            // Impact preset
            var impact = GetDict(vfx, "impact");
            if (impact != null)
                spell.impactPreset = GetString(impact, "preset", "");

            // Sprite scale
            var sprite = GetDict(vfx, "sprite");
            if (sprite != null)
                spell.scale = GetFloat(sprite, "scale", 1f);

            // Particles
            var particles = GetDict(vfx, "particles");
            if (particles != null)
            {
                spell.particleCount = GetInt(particles, "count");
                spell.particleSpeed = GetFloat(particles, "speed");
                spell.particleLifespan = GetFloat(particles, "lifespan") * TICK_TO_SEC;

                var sizeRange = GetArray(particles, "size_range");
                if (sizeRange != null && sizeRange.Count >= 2)
                    spell.sizeRange = new List<float> { GetFloat(sizeRange, 0), GetFloat(sizeRange, 1) };

                var color = GetArray(particles, "color");
                if (color != null && color.Count >= 3)
                {
                    spell.particleColor = new Color(
                        GetFloat(color, 0) / 255f,
                        GetFloat(color, 1) / 255f,
                        GetFloat(color, 2) / 255f, 1f);
                }

                var colors = GetArray(particles, "colors");
                if (colors != null)
                {
                    spell.particleColors = new List<Color>();
                    foreach (var c in colors)
                    {
                        if (c is List<object> arr && arr.Count >= 3)
                        {
                            spell.particleColors.Add(new Color(
                                ToFloat(arr[0]) / 255f,
                                ToFloat(arr[1]) / 255f,
                                ToFloat(arr[2]) / 255f, 1f));
                        }
                    }
                }
            }
        }

        // ── Catalog Builder ──

        private static void BuildCatalog(List<SpellDefinition> allAssets)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SpellCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }

            catalog.SetSpells(allAssets.ToArray());
            EditorUtility.SetDirty(catalog);
            Debug.Log($"[SpellDataImporter] SpellCatalog updated at {CATALOG_PATH} with {allAssets.Count} spells.");
        }

        // ── Hostile Slash Giant (malformed in JSON) ──

        private static Dictionary<string, object> BuildHostileSlashGiant()
        {
            return new Dictionary<string, object>
            {
                ["id"] = "hostile_slash_giant",
                ["type"] = "slash",
                ["mana_cost"] = 0.0,
                ["name"] = "Hostile Slash (Giant)",
                ["timings"] = new Dictionary<string, object>
                {
                    ["prepare"] = 0.0, ["channel"] = 0.0, ["cooldown"] = 1.0
                },
                ["rules"] = new Dictionary<string, object>
                {
                    ["lock_cast_direction"] = true,
                    ["interruptible"] = true,
                    ["automatic_cast_punish"] = 2.0,
                    ["allow_movement"] = false,
                    ["automatic"] = false
                },
                ["constraints"] = new Dictionary<string, object>
                {
                    ["max_instances"] = 1.0, ["allow_overlap"] = false
                },
                ["effect"] = new Dictionary<string, object>
                {
                    ["damage"] = 50.0,
                    ["arc_range_degrees"] = 180.0,
                    ["radius"] = 180.0,
                    ["hit_radius"] = 320.0,
                    ["hit_arc_degrees"] = 180.0,
                    ["lifetime"] = 18.0
                },
                ["telegraph_color"] = new List<object> { 120.0, 230.0, 160.0 },
                ["telegraph_alpha"] = 80.0,
                ["vfx"] = new Dictionary<string, object>
                {
                    ["preset"] = "slash_emitter",
                    ["particles"] = new Dictionary<string, object>
                    {
                        ["count"] = 80.0,
                        ["size_range"] = new List<object> { 4.0, 9.0 },
                        ["color"] = new List<object> { 0.0, 255.0, 100.0 },
                        ["speed"] = 5.8
                    }
                },
                ["meta"] = new Dictionary<string, object>
                {
                    ["speed_multiplier"] = 1.0, ["offset"] = 40.0
                }
            };
        }

        // ── Type Mapping ──

        private static SpellType ParseSpellType(string pythonType)
        {
            switch (pythonType.ToLowerInvariant())
            {
                case "projectile":         return SpellType.Projectile;
                case "slash":              return SpellType.Slash;
                case "area":               return SpellType.Area;
                case "dash":               return SpellType.Dash;
                case "teleport":           return SpellType.Teleport;
                case "beam":               return SpellType.Beam;
                case "smoke":              return SpellType.Smoke;
                case "wall":               return SpellType.Wall;
                case "trap":               return SpellType.Trap;
                case "shield":             return SpellType.Shield;
                case "boomerang":          return SpellType.Boomerang;
                case "meteor_shower":      return SpellType.Meteor;
                case "lightning":          return SpellType.Lightning;
                case "chain_lightning":    return SpellType.ChainLightning;
                case "aura":               return SpellType.Aura;
                case "arcane_flame":       return SpellType.ArcaneFlame;
                case "firework_launch":    return SpellType.FireworkLaunch;
                case "smoke_emitter":      return SpellType.SmokeEmitter;
                case "sphere_magic_shield":return SpellType.SphereMagicShield;
                case "puddle":             return SpellType.Puddle;
                case "mine":               return SpellType.Mine;
                case "vortex_field":       return SpellType.VortexField;
                case "cone_breath":        return SpellType.ConeBreath;
                case "summon":             return SpellType.Summon;
                case "totem":              return SpellType.Totem;
                default:
                    Debug.LogWarning($"[SpellDataImporter] Unknown spell type '{pythonType}', defaulting to Projectile.");
                    return SpellType.Projectile;
            }
        }

        // ── JSON Helpers ──

    }
}