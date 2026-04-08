using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool that imports spell definitions from python/data/spells/spells.json
    /// and creates/updates SpellDefinition ScriptableObject assets + SpellCatalog.
    ///
    /// Unit conversions (Python → Unity):
    ///   Distances/radii (px)   → ÷ 16  (1 tile = 16 px = 1 world unit)
    ///   Speed (px/tick @60fps) → × 3.75 (= ×60/16 → world units/sec)
    ///   Lifetime (ticks @60fps)→ ÷ 60  (→ seconds)
    ///   Timings (seconds)      → keep as-is
    ///   Damage/HP/counts       → keep as-is
    ///   Angles (degrees)       → keep as-is
    ///
    /// Menu: Valkur > Spells > Import Spells from Python JSON
    /// </summary>
    public static class SpellDataImporter
    {
        private const string SPELLS_JSON_REL = "python/data/spells/spells.json";
        private const string SPELL_ASSET_DIR = "Assets/_Project/Data/Catalogs/Spells";
        private const string CATALOG_PATH    = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const float PX_TO_WORLD = 1f / 16f;        // 16 px = 1 world unit
        private const float SPEED_TO_WORLD = 60f / 16f;    // px/tick → world/sec (3.75)
        private const float TICK_TO_SEC = 1f / 60f;         // tick → seconds

        [MenuItem("Valkur/Spells/Import Spells from Python JSON")]
        public static void ImportAll()
        {
            string jsonPath = ResolveJsonPath();
            if (string.IsNullOrEmpty(jsonPath))
            {
                Debug.LogError("[SpellDataImporter] Could not find spells.json. Expected at: " + SPELLS_JSON_REL);
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var spellsDict = ParseSpellsJson(json);
            if (spellsDict == null || spellsDict.Count == 0)
            {
                Debug.LogError("[SpellDataImporter] Failed to parse spells.json or it is empty.");
                return;
            }

            // Add hardcoded hostile_slash_giant (malformed in JSON — embedded inside fireball's VFX block)
            if (!spellsDict.ContainsKey("hostile_slash_giant"))
                spellsDict["hostile_slash_giant"] = BuildHostileSlashGiant();

            if (!AssetDatabase.IsValidFolder(SPELL_ASSET_DIR))
                EnsureFolder(SPELL_ASSET_DIR);

            int created = 0, updated = 0;
            var allAssets = new List<SpellDefinition>();

            foreach (var kv in spellsDict)
            {
                string key = kv.Key;
                var data = kv.Value;
                string assetPath = $"{SPELL_ASSET_DIR}/{key}.asset";

                var asset = AssetDatabase.LoadAssetAtPath<SpellDefinition>(assetPath);
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<SpellDefinition>();
                    asset.name = key;
                }

                ApplyValues(asset, key, data);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    updated++;
                }

                allAssets.Add(asset);
            }

            // Build SpellCatalog
            BuildCatalog(allAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SpellDataImporter] Done! Created {created}, updated {updated}, total {allAssets.Count} spells. Catalog at {CATALOG_PATH}");
        }

        // ── JSON Parsing ──

        private static Dictionary<string, Dictionary<string, object>> ParseSpellsJson(string json)
        {
            // Use Unity's JsonUtility indirectly via a wrapper, or manual parsing.
            // Since Unity's JsonUtility doesn't handle Dictionary, use MiniJSON-style parsing.
            var root = Json.Deserialize(json) as Dictionary<string, object>;
            if (root == null) return null;

            var result = new Dictionary<string, Dictionary<string, object>>();
            foreach (var kv in root)
            {
                if (kv.Value is Dictionary<string, object> spellData)
                    result[kv.Key] = spellData;
            }
            return result;
        }

        // ── Value Application ──

        private static void ApplyValues(SpellDefinition spell, string key, Dictionary<string, object> data)
        {
            // Identity
            spell.spellKey = key;
            spell.displayName = GetString(data, "name", key);
            spell.type = ParseSpellType(GetString(data, "type", "projectile"));
            spell.manaCost = GetFloat(data, "mana_cost");

            // Timings
            var timings = GetDict(data, "timings");
            if (timings != null)
            {
                spell.prepareDuration = GetFloat(timings, "prepare");
                spell.channelDuration = GetFloat(timings, "channel");
                spell.cooldownDuration = GetFloat(timings, "cooldown");
            }

            // Rules
            var rules = GetDict(data, "rules");
            if (rules != null)
            {
                spell.lockCastDirection = GetBool(rules, "lock_cast_direction");
                spell.interruptible = GetBool(rules, "interruptible");
                spell.automaticCastPunish = GetFloat(rules, "automatic_cast_punish", 1f);
                spell.allowMovement = GetBool(rules, "allow_movement");
                spell.automatic = GetBool(rules, "automatic");
            }

            // Constraints
            var constraints = GetDict(data, "constraints");
            if (constraints != null)
            {
                spell.maxInstances = GetInt(constraints, "max_instances", 1);
                spell.allowOverlap = GetBool(constraints, "allow_overlap");
            }

            // Effect — varies widely by spell type
            var effect = GetDict(data, "effect");
            if (effect != null)
                ApplyEffect(spell, effect);

            // Meta
            var meta = GetDict(data, "meta");
            if (meta != null)
            {
                spell.speedMultiplier = GetFloat(meta, "speed_multiplier", 1f);
                spell.offset = GetFloat(meta, "offset") * PX_TO_WORLD;
            }

            // Telegraph
            var telegraphColor = GetArray(data, "telegraph_color");
            if (telegraphColor != null && telegraphColor.Count >= 3)
            {
                float r = GetFloat(telegraphColor, 0) / 255f;
                float g = GetFloat(telegraphColor, 1) / 255f;
                float b = GetFloat(telegraphColor, 2) / 255f;
                float a = GetFloat(data, "telegraph_alpha", 80f) / 255f;
                spell.telegraphColor = new Color(r, g, b, a);
                spell.telegraphAlpha = GetFloat(data, "telegraph_alpha", 80f);
            }

            // VFX
            var vfx = GetDict(data, "vfx");
            if (vfx != null)
                ApplyVfx(spell, vfx);

            // Particle color from meta (smoke_emitter)
            if (meta != null)
            {
                var metaColor = GetArray(meta, "particle_color");
                if (metaColor != null && metaColor.Count >= 3)
                {
                    spell.particleColor = new Color(
                        GetFloat(metaColor, 0) / 255f,
                        GetFloat(metaColor, 1) / 255f,
                        GetFloat(metaColor, 2) / 255f, 1f);
                }
            }
        }

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

        private static string GetString(Dictionary<string, object> dict, string key, string fallback = "")
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return val.ToString();
            return fallback;
        }

        private static float GetFloat(Dictionary<string, object> dict, string key, float fallback = 0f)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return ToFloat(val);
            return fallback;
        }

        private static float GetFloat(List<object> list, int index, float fallback = 0f)
        {
            if (list != null && index < list.Count && list[index] != null)
                return ToFloat(list[index]);
            return fallback;
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback = 0)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return Mathf.RoundToInt(ToFloat(val));
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key, bool fallback = false)
        {
            if (dict != null && dict.TryGetValue(key, out var val))
            {
                if (val is bool b) return b;
                if (val is string s) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
                return ToFloat(val) != 0f;
            }
            return fallback;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is Dictionary<string, object> d)
                return d;
            return null;
        }

        private static List<object> GetArray(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is List<object> list)
                return list;
            return null;
        }

        private static float ToFloat(object val)
        {
            if (val is double d) return (float)d;
            if (val is float f) return f;
            if (val is long l) return l;
            if (val is int i) return i;
            if (val is string s && float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return parsed;
            return 0f;
        }

        // ── Path Helpers ──

        private static string ResolveJsonPath()
        {
            // Try relative to project root (workspace root is 2 levels up from Assets)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string candidate = Path.Combine(projectRoot, SPELLS_JSON_REL.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            // Try from dataPath directly
            candidate = Path.Combine(Application.dataPath, "..", "..", "..", SPELLS_JSON_REL.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            // Fallback: try workspace patterns
            string[] searchPaths = {
                Path.Combine(projectRoot, "python", "data", "spells", "spells.json"),
                Path.Combine(Application.dataPath, "..", "..", "python", "data", "spells", "spells.json"),
            };
            foreach (var p in searchPaths)
            {
                string full = Path.GetFullPath(p);
                if (File.Exists(full)) return full;
            }

            return null;
        }

        private static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ── MiniJSON (embedded lightweight JSON parser) ──

        /// <summary>
        /// Minimal JSON parser. Returns Dictionary&lt;string,object&gt; for objects,
        /// List&lt;object&gt; for arrays, double for numbers, string, bool, or null.
        /// </summary>
        private static class Json
        {
            public static object Deserialize(string json)
            {
                if (string.IsNullOrEmpty(json)) return null;
                return new Parser(json).ParseValue();
            }

            private sealed class Parser
            {
                private readonly string _json;
                private int _pos;

                public Parser(string json) { _json = json; _pos = 0; }

                public object ParseValue()
                {
                    SkipWhitespace();
                    if (_pos >= _json.Length) return null;
                    char c = _json[_pos];
                    if (c == '{') return ParseObject();
                    if (c == '[') return ParseArray();
                    if (c == '"') return ParseString();
                    if (c == 't' || c == 'f') return ParseBool();
                    if (c == 'n') return ParseNull();
                    return ParseNumber();
                }

                private Dictionary<string, object> ParseObject()
                {
                    var dict = new Dictionary<string, object>();
                    _pos++; // skip '{'
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == '}') { _pos++; return dict; }

                    while (_pos < _json.Length)
                    {
                        SkipWhitespace();
                        string key = ParseString();
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ':') _pos++;
                        SkipWhitespace();
                        object val = ParseValue();
                        dict[key] = val;
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                        if (_pos < _json.Length && _json[_pos] == '}') { _pos++; break; }
                        break; // malformed
                    }
                    return dict;
                }

                private List<object> ParseArray()
                {
                    var list = new List<object>();
                    _pos++; // skip '['
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ']') { _pos++; return list; }

                    while (_pos < _json.Length)
                    {
                        SkipWhitespace();
                        list.Add(ParseValue());
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                        if (_pos < _json.Length && _json[_pos] == ']') { _pos++; break; }
                        break;
                    }
                    return list;
                }

                private string ParseString()
                {
                    if (_pos >= _json.Length || _json[_pos] != '"') return "";
                    _pos++; // skip opening "
                    int start = _pos;
                    var sb = new System.Text.StringBuilder();
                    while (_pos < _json.Length)
                    {
                        char c = _json[_pos];
                        if (c == '\\')
                        {
                            _pos++;
                            if (_pos < _json.Length)
                            {
                                char esc = _json[_pos];
                                switch (esc)
                                {
                                    case '"': sb.Append('"'); break;
                                    case '\\': sb.Append('\\'); break;
                                    case '/': sb.Append('/'); break;
                                    case 'n': sb.Append('\n'); break;
                                    case 'r': sb.Append('\r'); break;
                                    case 't': sb.Append('\t'); break;
                                    case 'u':
                                        if (_pos + 4 < _json.Length)
                                        {
                                            string hex = _json.Substring(_pos + 1, 4);
                                            sb.Append((char)Convert.ToInt32(hex, 16));
                                            _pos += 4;
                                        }
                                        break;
                                    default: sb.Append(esc); break;
                                }
                            }
                        }
                        else if (c == '"')
                        {
                            _pos++; // skip closing "
                            return sb.ToString();
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        _pos++;
                    }
                    return sb.ToString();
                }

                private object ParseNumber()
                {
                    int start = _pos;
                    if (_pos < _json.Length && _json[_pos] == '-') _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    if (_pos < _json.Length && _json[_pos] == '.')
                    {
                        _pos++;
                        while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    }
                    if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                    {
                        _pos++;
                        if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                        while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    }
                    string numStr = _json.Substring(start, _pos - start);
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return d;
                    return 0.0;
                }

                private bool ParseBool()
                {
                    if (_json.Substring(_pos, Math.Min(4, _json.Length - _pos)) == "true")
                    { _pos += 4; return true; }
                    if (_json.Substring(_pos, Math.Min(5, _json.Length - _pos)) == "false")
                    { _pos += 5; return false; }
                    _pos++;
                    return false;
                }

                private object ParseNull()
                {
                    if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null")
                    { _pos += 4; return null; }
                    _pos++;
                    return null;
                }

                private void SkipWhitespace()
                {
                    while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                        _pos++;
                }
            }
        }
    }
}
