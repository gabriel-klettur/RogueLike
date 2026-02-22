using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool that imports Python JSON data files into Unity ScriptableObjects.
    /// Menu: Valkur > Migration > Import Python Data
    /// </summary>
    public static class PythonDataMigrator
    {
        private const string PYTHON_DATA_ROOT = "../../../python/data";
        private const string SO_OUTPUT_ROOT = "Assets/_Project/Data/Catalogs";

        [MenuItem("Valkur/Migration/Import Monsters from Python JSON")]
        public static void ImportMonsters()
        {
            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "entities/new_hostiles.json"));

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[Migrator] File not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var root = JsonUtility.FromJson<HostilesRoot>(json);

            if (root?.hostiles?.classes == null)
            {
                // JsonUtility can't handle Dictionary, use manual parsing
                ImportMonstersManual(json);
                return;
            }

            Debug.Log("[Migrator] Monster import complete.");
        }

        private static void ImportMonstersManual(string json)
        {
            // Parse using Unity's built-in JSON as a raw approach
            // For complex nested dicts, we use a simplified manual parser
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                Debug.LogError("[Migrator] Failed to parse hostiles JSON.");
                return;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Monsters");
            if (!AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Monsters");
            }

            var hostiles = parsed.GetValueOrDefault("hostiles") as Dictionary<string, object>;
            if (hostiles == null) return;

            var classes = hostiles.GetValueOrDefault("classes") as Dictionary<string, object>;
            if (classes == null) return;

            float defaultDeathTime = Convert.ToSingle(parsed.GetValueOrDefault("DEFAULT_DEATH_DISSAPEAR_TIME") ?? 10f);
            float defaultDmgStopProb = Convert.ToSingle(parsed.GetValueOrDefault("DEFAULT_DAMAGE_STOP_PROBABILITY") ?? 0.25f);

            int count = 0;
            foreach (var kvp in classes)
            {
                string className = kvp.Key;
                var classCfg = kvp.Value as Dictionary<string, object>;
                if (classCfg == null) continue;

                var so = ScriptableObject.CreateInstance<MonsterDefinition>();
                so.monsterKey = className;
                so.displayName = classCfg.GetValueOrDefault("default_name") as string ?? className;
                so.fsmSet = classCfg.GetValueOrDefault("fsm_set") as string ?? "";
                so.useAttackTelegraph = Convert.ToBoolean(classCfg.GetValueOrDefault("use_attack_telegraph") ?? false);

                // Parse patrol
                var patrol = classCfg.GetValueOrDefault("patrol") as Dictionary<string, object>;
                if (patrol != null)
                    so.patrolType = patrol.GetValueOrDefault("id") as string ?? "";

                // Parse next_phase / phase_index
                so.nextPhase = classCfg.GetValueOrDefault("next_phase") as string ?? "";
                so.phaseIndex = Convert.ToInt32(classCfg.GetValueOrDefault("phase_index") ?? 0);
                so.autoCast = Convert.ToBoolean(classCfg.GetValueOrDefault("auto_cast") ?? false);

                // Parse stats
                var stats = classCfg.GetValueOrDefault("stats") as Dictionary<string, object>;
                if (stats != null)
                {
                    so.stats = new EntityStats
                    {
                        hp = GetInt(stats, "hp"),
                        speed = GetFloat(stats, "speed"),
                        faction = stats.GetValueOrDefault("faction") as string ?? "EVIL",
                        aggroRange = GetFloat(stats, "aggro_range"),
                        meleeRange = GetInt(stats, "melee_range"),
                        meleeDamage = GetInt(stats, "melee_damage"),
                        meleeCooldown = GetFloat(stats, "melee_cooldown"),
                        defense = GetInt(stats, "defense"),
                        power = GetInt(stats, "power"),
                        damageDuration = GetFloat(stats, "damage_duration"),
                        chasingSpeed = GetFloat(stats, "chasing_speed"),
                        feetWidthFactor = GetFloat(stats, "feet_width_factor"),
                        feetHeightFactor = GetFloat(stats, "feet_height_factor"),
                        spawnPadding = GetInt(stats, "spawn_padding"),
                        spawnCount = GetInt(stats, "spawn_count"),
                        spawnMargin = GetInt(stats, "spawn_margin"),
                        deathDisappearTime = GetFloat(stats, "death_dissapear_time", defaultDeathTime),
                        damageStopProbability = GetFloat(stats, "damage_stop_probability", defaultDmgStopProb),
                        attackWindupSeconds = GetFloat(stats, "attack_windup_s")
                    };
                }

                string assetPath = $"{outputDir}/{className}.asset";
                AssetDatabase.CreateAsset(so, assetPath);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Migrator] Imported {count} monster definitions to {outputDir}");
        }

        [MenuItem("Valkur/Migration/Import Spells from Python JSON")]
        public static void ImportSpells()
        {
            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "spells/spells.json"));

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[Migrator] File not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                Debug.LogError("[Migrator] Failed to parse spells JSON.");
                return;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Spells");
            if (!AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Spells");
            }

            int count = 0;
            foreach (var kvp in parsed)
            {
                string spellKey = kvp.Key;
                var spellData = kvp.Value as Dictionary<string, object>;
                if (spellData == null) continue;

                var so = ScriptableObject.CreateInstance<SpellDefinition>();
                so.spellKey = spellKey;
                so.displayName = spellData.GetValueOrDefault("name") as string ?? spellKey;

                // Type
                string typeStr = spellData.GetValueOrDefault("type") as string ?? "projectile";
                so.type = ParseSpellType(typeStr);

                so.manaCost = GetFloat(spellData, "mana_cost");

                // Timings
                var timings = spellData.GetValueOrDefault("timings") as Dictionary<string, object>;
                if (timings != null)
                {
                    so.prepareDuration = GetFloat(timings, "prepare");
                    so.channelDuration = GetFloat(timings, "channel");
                    so.cooldownDuration = GetFloat(timings, "cooldown");
                }

                // Rules
                var rules = spellData.GetValueOrDefault("rules") as Dictionary<string, object>;
                if (rules != null)
                {
                    so.lockCastDirection = GetBool(rules, "lock_cast_direction");
                    so.interruptible = GetBool(rules, "interruptible");
                    so.automaticCastPunish = GetFloat(rules, "automatic_cast_punish", 1f);
                    so.allowMovement = GetBool(rules, "allow_movement");
                    so.automatic = GetBool(rules, "automatic");
                }

                // Constraints
                var constraints = spellData.GetValueOrDefault("constraints") as Dictionary<string, object>;
                if (constraints != null)
                {
                    so.maxInstances = GetInt(constraints, "max_instances");
                    so.allowOverlap = GetBool(constraints, "allow_overlap", true);
                }

                // Effect
                var effect = spellData.GetValueOrDefault("effect") as Dictionary<string, object>;
                if (effect != null)
                {
                    so.damage = GetFloat(effect, "damage");
                    so.range = GetFloat(effect, "range");
                    so.speed = GetFloat(effect, "speed");
                    so.lifetime = GetFloat(effect, "lifetime");
                    so.radius = GetFloat(effect, "radius");
                    so.hitRadius = GetFloat(effect, "hit_radius");
                    so.arcRangeDegrees = GetFloat(effect, "arc_range_degrees");
                    so.hitArcDegrees = GetFloat(effect, "hit_arc_degrees");
                }

                // Meta
                var meta = spellData.GetValueOrDefault("meta") as Dictionary<string, object>;
                if (meta != null)
                {
                    so.speedMultiplier = GetFloat(meta, "speed_multiplier", 1f);
                    so.offset = GetFloat(meta, "offset");
                }

                string assetPath = $"{outputDir}/{spellKey}.asset";
                AssetDatabase.CreateAsset(so, assetPath);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Migrator] Imported {count} spell definitions to {outputDir}");
        }

        [MenuItem("Valkur/Migration/Import Players from Python JSON")]
        public static void ImportPlayers()
        {
            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "entities/new_players.json"));

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[Migrator] File not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                Debug.LogError("[Migrator] Failed to parse players JSON.");
                return;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Players");
            if (!AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Players");
            }

            var players = parsed.GetValueOrDefault("players") as Dictionary<string, object>;
            if (players == null) return;

            var classes = players.GetValueOrDefault("classes") as Dictionary<string, object>;
            if (classes == null) return;

            int count = 0;
            foreach (var kvp in classes)
            {
                string className = kvp.Key;
                var classCfg = kvp.Value as Dictionary<string, object>;
                if (classCfg == null) continue;

                var so = ScriptableObject.CreateInstance<PlayerDefinition>();
                so.playerKey = className;
                so.displayName = className;

                var stats = classCfg.GetValueOrDefault("stats") as Dictionary<string, object>;
                if (stats != null)
                {
                    so.maxStrength = GetInt(stats, "max_strength");
                    so.maxIntelligence = GetInt(stats, "max_intelligence");
                    so.maxDexterity = GetInt(stats, "max_dexterity");
                    so.initialStrength = GetInt(stats, "initial_strength");
                    so.initialIntelligence = GetInt(stats, "initial_intelligence");
                    so.initialDexterity = GetInt(stats, "initial_dexterity");
                    so.basicSpeed = GetFloat(stats, "basic_speed");
                    so.basicAttack = GetInt(stats, "basic_attack");
                    so.basicArmor = GetInt(stats, "basic_armor");
                    so.basicDeathTimerDuration = GetFloat(stats, "basic_death_timer_duration");
                    so.dragDropRange = GetFloat(stats, "drag_drop_range");
                    so.dashCharges = GetInt(stats, "dash_charges");
                    so.damageStopProbability = GetFloat(stats, "damage_stop_probability");
                    so.manaRegenPerSecond = GetFloat(stats, "mana_regen_per_second");
                }

                string assetPath = $"{outputDir}/{className}.asset";
                AssetDatabase.CreateAsset(so, assetPath);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Migrator] Imported {count} player definitions to {outputDir}");
        }

        [MenuItem("Valkur/Migration/Import All Python Data")]
        public static void ImportAll()
        {
            ImportMonsters();
            ImportSpells();
            ImportPlayers();
            Debug.Log("[Migrator] All Python data imported.");
        }

        #region Helpers

        private static SpellType ParseSpellType(string s)
        {
            return s?.ToLower() switch
            {
                "projectile" => SpellType.Projectile,
                "slash" => SpellType.Slash,
                "area" => SpellType.Area,
                "dash" => SpellType.Dash,
                "teleport" => SpellType.Teleport,
                "beam" => SpellType.Beam,
                "smoke" => SpellType.Smoke,
                "wall" => SpellType.Wall,
                "trap" => SpellType.Trap,
                "shield" => SpellType.Shield,
                "boomerang" => SpellType.Boomerang,
                "meteor" => SpellType.Meteor,
                _ => SpellType.Projectile
            };
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToSingle(v);
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToBoolean(v);
            return def;
        }

        #endregion

        #region Serialization helpers (JsonUtility fallback)

        [Serializable]
        private class HostilesRoot
        {
            public HostilesContainer hostiles;
        }

        [Serializable]
        private class HostilesContainer
        {
            public Dictionary<string, object> classes;
        }

        #endregion
    }

    /// <summary>
    /// Minimal JSON parser that handles nested dicts/lists.
    /// Unity's JsonUtility cannot deserialize Dictionary, so we use this.
    /// Based on Unity's MiniJSON (public domain).
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return Parser.Parse(json);
        }

        public static string Serialize(object obj)
        {
            return Serializer.Serialize(obj);
        }

        private sealed class Parser : IDisposable
        {
            private const string WORD_BREAK = "{}[],:\"";
            private StringReader _json;

            private Parser(string jsonString)
            {
                _json = new StringReader(jsonString);
            }

            public static object Parse(string jsonString)
            {
                using var instance = new Parser(jsonString);
                return instance.ParseValue();
            }

            public void Dispose()
            {
                _json.Dispose();
                _json = null;
            }

            private char PeekChar => Convert.ToChar(_json.Peek());
            private char NextChar => Convert.ToChar(_json.Read());

            private string NextWord
            {
                get
                {
                    var word = new System.Text.StringBuilder();
                    while (!IsWordBreak(PeekChar))
                    {
                        word.Append(NextChar);
                        if (_json.Peek() == -1) break;
                    }
                    return word.ToString();
                }
            }

            private enum TOKEN
            {
                NONE, CURLY_OPEN, CURLY_CLOSE, SQUARED_OPEN, SQUARED_CLOSE,
                COLON, COMMA, STRING, NUMBER, TRUE, FALSE, NULL
            }

            private bool IsWordBreak(char c) => char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;

            private void EatWhitespace()
            {
                while (char.IsWhiteSpace(PeekChar))
                {
                    _json.Read();
                    if (_json.Peek() == -1) break;
                }
            }

            private TOKEN NextToken
            {
                get
                {
                    EatWhitespace();
                    if (_json.Peek() == -1) return TOKEN.NONE;
                    switch (PeekChar)
                    {
                        case '{': return TOKEN.CURLY_OPEN;
                        case '}': _json.Read(); return TOKEN.CURLY_CLOSE;
                        case '[': return TOKEN.SQUARED_OPEN;
                        case ']': _json.Read(); return TOKEN.SQUARED_CLOSE;
                        case ',': _json.Read(); return TOKEN.COMMA;
                        case '"': return TOKEN.STRING;
                        case ':': return TOKEN.COLON;
                        case '-': case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7': case '8': case '9':
                            return TOKEN.NUMBER;
                    }
                    var word = NextWord;
                    switch (word)
                    {
                        case "false": return TOKEN.FALSE;
                        case "true": return TOKEN.TRUE;
                        case "null": return TOKEN.NULL;
                    }
                    return TOKEN.NONE;
                }
            }

            private object ParseValue()
            {
                var token = NextToken;
                return token switch
                {
                    TOKEN.STRING => ParseString(),
                    TOKEN.NUMBER => ParseNumber(),
                    TOKEN.CURLY_OPEN => ParseObject(),
                    TOKEN.SQUARED_OPEN => ParseArray(),
                    TOKEN.TRUE => true,
                    TOKEN.FALSE => false,
                    TOKEN.NULL => null,
                    _ => null
                };
            }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();
                _json.Read(); // {
                while (true)
                {
                    var token = NextToken;
                    switch (token)
                    {
                        case TOKEN.NONE: return null;
                        case TOKEN.CURLY_CLOSE: return table;
                        case TOKEN.COMMA: continue;
                        default:
                            var name = ParseString();
                            if (name == null) return null;
                            token = NextToken;
                            if (token != TOKEN.COLON) return null;
                            _json.Read(); // :
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();
                _json.Read(); // [
                var parsing = true;
                while (parsing)
                {
                    var token = NextToken;
                    switch (token)
                    {
                        case TOKEN.NONE: return null;
                        case TOKEN.SQUARED_CLOSE: parsing = false; break;
                        case TOKEN.COMMA: break;
                        default:
                            var value = ParseByToken(token);
                            array.Add(value);
                            break;
                    }
                }
                return array;
            }

            private object ParseByToken(TOKEN token)
            {
                return token switch
                {
                    TOKEN.STRING => ParseString(),
                    TOKEN.NUMBER => ParseNumber(),
                    TOKEN.CURLY_OPEN => ParseObject(),
                    TOKEN.SQUARED_OPEN => ParseArray(),
                    TOKEN.TRUE => true,
                    TOKEN.FALSE => false,
                    TOKEN.NULL => null,
                    _ => null
                };
            }

            private string ParseString()
            {
                var s = new System.Text.StringBuilder();
                _json.Read(); // opening "
                bool parsing = true;
                while (parsing)
                {
                    if (_json.Peek() == -1) { parsing = false; break; }
                    char c = NextChar;
                    switch (c)
                    {
                        case '"': parsing = false; break;
                        case '\\':
                            if (_json.Peek() == -1) { parsing = false; break; }
                            c = NextChar;
                            switch (c)
                            {
                                case '"': case '\\': case '/': s.Append(c); break;
                                case 'b': s.Append('\b'); break;
                                case 'f': s.Append('\f'); break;
                                case 'n': s.Append('\n'); break;
                                case 'r': s.Append('\r'); break;
                                case 't': s.Append('\t'); break;
                                case 'u':
                                    var hex = new char[4];
                                    for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                    s.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                            break;
                        default: s.Append(c); break;
                    }
                }
                return s.ToString();
            }

            private object ParseNumber()
            {
                string number = NextWord;
                if (number.Contains(".") || number.Contains("e") || number.Contains("E"))
                {
                    double.TryParse(number, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double result);
                    return result;
                }
                long.TryParse(number, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out long intResult);
                return intResult;
            }
        }

        private sealed class Serializer
        {
            private System.Text.StringBuilder _builder;
            private Serializer() { _builder = new System.Text.StringBuilder(); }

            public static string Serialize(object obj)
            {
                var instance = new Serializer();
                instance.SerializeValue(obj);
                return instance._builder.ToString();
            }

            private void SerializeValue(object value)
            {
                if (value == null) { _builder.Append("null"); return; }
                if (value is string s) { SerializeString(s); return; }
                if (value is bool b) { _builder.Append(b ? "true" : "false"); return; }
                if (value is IList<object> list) { SerializeArray(list); return; }
                if (value is IDictionary<string, object> dict) { SerializeObject(dict); return; }
                if (value is char c) { SerializeString(c.ToString()); return; }
                _builder.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            }

            private void SerializeObject(IDictionary<string, object> obj)
            {
                _builder.Append('{');
                bool first = true;
                foreach (var kvp in obj)
                {
                    if (!first) _builder.Append(',');
                    SerializeString(kvp.Key);
                    _builder.Append(':');
                    SerializeValue(kvp.Value);
                    first = false;
                }
                _builder.Append('}');
            }

            private void SerializeArray(IList<object> array)
            {
                _builder.Append('[');
                bool first = true;
                foreach (var item in array)
                {
                    if (!first) _builder.Append(',');
                    SerializeValue(item);
                    first = false;
                }
                _builder.Append(']');
            }

            private void SerializeString(string str)
            {
                _builder.Append('"');
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '"': _builder.Append("\\\""); break;
                        case '\\': _builder.Append("\\\\"); break;
                        case '\b': _builder.Append("\\b"); break;
                        case '\f': _builder.Append("\\f"); break;
                        case '\n': _builder.Append("\\n"); break;
                        case '\r': _builder.Append("\\r"); break;
                        case '\t': _builder.Append("\\t"); break;
                        default: _builder.Append(c); break;
                    }
                }
                _builder.Append('"');
            }
        }
    }

    /// <summary>
    /// Extension for Dictionary to provide GetValueOrDefault for older C# versions.
    /// </summary>
    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}
