using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Severity level for migration report entries.
    /// </summary>
    public enum MigrationSeverity { Ok, Warning, Error }

    /// <summary>
    /// Single entry in a migration report.
    /// </summary>
    public struct MigrationEntry
    {
        public MigrationSeverity Severity;
        public string Source;
        public string EntityKey;
        public string Message;

        public MigrationEntry(MigrationSeverity severity, string source, string entityKey, string message)
        {
            Severity = severity;
            Source = source;
            EntityKey = entityKey;
            Message = message;
        }

        public override string ToString()
        {
            string tag = Severity switch
            {
                MigrationSeverity.Ok => "OK",
                MigrationSeverity.Warning => "WARN",
                MigrationSeverity.Error => "ERROR",
                _ => "?"
            };
            return $"[{tag}] {Source} / {EntityKey}: {Message}";
        }
    }

    /// <summary>
    /// Accumulates migration results per file/entity and prints a summary report.
    /// Used by PythonDataMigrator for both live imports and dry-run validation.
    /// </summary>
    public class MigrationReport
    {
        private readonly List<MigrationEntry> _entries = new List<MigrationEntry>();

        public int OkCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }
        public int TotalCount => _entries.Count;
        public IReadOnlyList<MigrationEntry> Entries => _entries;

        public void AddOk(string source, string entityKey, string message = "Imported successfully")
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Ok, source, entityKey, message));
            OkCount++;
        }

        public void AddWarning(string source, string entityKey, string message)
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Warning, source, entityKey, message));
            WarningCount++;
        }

        public void AddError(string source, string entityKey, string message)
        {
            _entries.Add(new MigrationEntry(MigrationSeverity.Error, source, entityKey, message));
            ErrorCount++;
        }

        /// <summary>
        /// Prints full report to Unity console with summary header.
        /// </summary>
        public void PrintToConsole(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Migration Report: {title} ===");
            sb.AppendLine($"Total: {TotalCount} | OK: {OkCount} | Warnings: {WarningCount} | Errors: {ErrorCount}");
            sb.AppendLine("---");

            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.ToString());
            }

            sb.AppendLine("=== End Report ===");

            if (ErrorCount > 0)
                Debug.LogError(sb.ToString());
            else if (WarningCount > 0)
                Debug.LogWarning(sb.ToString());
            else
                Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Merge another report into this one.
        /// </summary>
        public void Merge(MigrationReport other)
        {
            _entries.AddRange(other._entries);
            OkCount += other.OkCount;
            WarningCount += other.WarningCount;
            ErrorCount += other.ErrorCount;
        }
    }

    /// <summary>
    /// Editor tool that imports Python JSON data files into Unity ScriptableObjects.
    /// Menu: Valkur > Migration > Import Python Data
    /// Supports dry-run mode (validate without writing) and conversion reports.
    /// </summary>
    public static class PythonDataMigrator
    {
        private const string PYTHON_DATA_ROOT = "../../../python/data";
        private const string SO_OUTPUT_ROOT = "Assets/_Project/Data/Catalogs";

        [MenuItem("Valkur/Migration/Import Monsters from Python JSON")]
        public static void ImportMonsters() => ImportMonsters(dryRun: false);

        public static MigrationReport ImportMonsters(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "new_hostiles.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "entities/new_hostiles.json"));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-", $"File not found: {jsonPath}");
                report.PrintToConsole($"Monsters ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);
            ImportMonstersManual(json, dryRun, report);
            report.PrintToConsole($"Monsters ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return report;
        }

        private static void ImportMonstersManual(string json, bool dryRun, MigrationReport report)
        {
            const string source = "new_hostiles.json";

            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                report.AddError(source, "-", "Failed to parse JSON root.");
                return;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Monsters");
            if (!dryRun && !AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Monsters");
            }

            var hostiles = parsed.GetValueOrDefault("hostiles") as Dictionary<string, object>;
            if (hostiles == null) { report.AddError(source, "-", "Missing 'hostiles' key."); return; }

            var classes = hostiles.GetValueOrDefault("classes") as Dictionary<string, object>;
            if (classes == null) { report.AddError(source, "-", "Missing 'hostiles.classes' key."); return; }

            float defaultDeathTime = Convert.ToSingle(parsed.GetValueOrDefault("DEFAULT_DEATH_DISSAPEAR_TIME") ?? 10f);
            float defaultDmgStopProb = Convert.ToSingle(parsed.GetValueOrDefault("DEFAULT_DAMAGE_STOP_PROBABILITY") ?? 0.25f);

            int count = 0;
            foreach (var kvp in classes)
            {
                string className = kvp.Key;
                var classCfg = kvp.Value as Dictionary<string, object>;
                if (classCfg == null)
                {
                    report.AddError(source, className, "Entry is not a valid dictionary.");
                    continue;
                }

                // Validate required fields
                var stats = classCfg.GetValueOrDefault("stats") as Dictionary<string, object>;
                if (stats == null)
                {
                    report.AddWarning(source, className, "Missing 'stats' block — will use zero defaults.");
                }
                else
                {
                    int hp = GetInt(stats, "hp");
                    float speed = GetFloat(stats, "speed");
                    if (hp <= 0)
                        report.AddWarning(source, className, $"HP is {hp} (expected > 0).");
                    if (speed <= 0f)
                        report.AddWarning(source, className, $"Speed is {speed} (expected > 0).");
                }

                if (string.IsNullOrEmpty(className))
                {
                    report.AddError(source, "(empty)", "Monster key is empty.");
                    continue;
                }

                if (dryRun)
                {
                    if (report.ErrorCount == 0 || !HasErrorForKey(report, className))
                        report.AddOk(source, className, "Validated (dry-run).");
                    count++;
                    continue;
                }

                var so = ScriptableObject.CreateInstance<MonsterDefinition>();
                so.monsterKey = className;
                so.displayName = classCfg.GetValueOrDefault("default_name") as string ?? className;
                so.fsmSet = classCfg.GetValueOrDefault("fsm_set") as string ?? "";
                so.useAttackTelegraph = Convert.ToBoolean(classCfg.GetValueOrDefault("use_attack_telegraph") ?? false);

                var patrol = classCfg.GetValueOrDefault("patrol") as Dictionary<string, object>;
                if (patrol != null)
                    so.patrolType = patrol.GetValueOrDefault("id") as string ?? "";

                so.nextPhase = classCfg.GetValueOrDefault("next_phase") as string ?? "";
                so.phaseIndex = Convert.ToInt32(classCfg.GetValueOrDefault("phase_index") ?? 0);
                so.autoCast = Convert.ToBoolean(classCfg.GetValueOrDefault("auto_cast") ?? false);

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
                report.AddOk(source, className);
                count++;
            }

            if (!dryRun && count > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Valkur/Migration/Import Spells from Python JSON")]
        public static void ImportSpells() => ImportSpells(dryRun: false);

        public static MigrationReport ImportSpells(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "spells.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "spells/spells.json"));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-", $"File not found: {jsonPath}");
                report.PrintToConsole($"Spells ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                report.AddError(source, "-", "Failed to parse JSON root.");
                report.PrintToConsole($"Spells ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Spells");
            if (!dryRun && !AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Spells");
            }

            int count = 0;
            foreach (var kvp in parsed)
            {
                string spellKey = kvp.Key;
                var spellData = kvp.Value as Dictionary<string, object>;
                if (spellData == null)
                {
                    report.AddError(source, spellKey, "Entry is not a valid dictionary.");
                    continue;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(spellKey))
                {
                    report.AddError(source, "(empty)", "Spell key is empty.");
                    continue;
                }

                string typeStr = spellData.GetValueOrDefault("type") as string ?? "projectile";
                SpellType parsedType = ParseSpellType(typeStr);

                var effect = spellData.GetValueOrDefault("effect") as Dictionary<string, object>;
                if (parsedType == SpellType.Projectile)
                {
                    float speed = effect != null ? GetFloat(effect, "speed") : 0f;
                    if (speed <= 0f)
                        report.AddWarning(source, spellKey, $"Projectile spell has speed={speed} (expected > 0).");
                }

                var timings = spellData.GetValueOrDefault("timings") as Dictionary<string, object>;
                if (timings != null)
                {
                    float cd = GetFloat(timings, "cooldown");
                    if (cd <= 0f)
                        report.AddWarning(source, spellKey, $"Cooldown is {cd} (expected > 0).");
                }
                else
                {
                    report.AddWarning(source, spellKey, "Missing 'timings' block.");
                }

                if (dryRun)
                {
                    if (!HasErrorForKey(report, spellKey))
                        report.AddOk(source, spellKey, "Validated (dry-run).");
                    count++;
                    continue;
                }

                var so = ScriptableObject.CreateInstance<SpellDefinition>();
                so.spellKey = spellKey;
                so.displayName = spellData.GetValueOrDefault("name") as string ?? spellKey;
                so.type = parsedType;
                so.manaCost = GetFloat(spellData, "mana_cost");

                if (timings != null)
                {
                    so.prepareDuration = GetFloat(timings, "prepare");
                    so.channelDuration = GetFloat(timings, "channel");
                    so.cooldownDuration = GetFloat(timings, "cooldown");
                }

                var rules = spellData.GetValueOrDefault("rules") as Dictionary<string, object>;
                if (rules != null)
                {
                    so.lockCastDirection = GetBool(rules, "lock_cast_direction");
                    so.interruptible = GetBool(rules, "interruptible");
                    so.automaticCastPunish = GetFloat(rules, "automatic_cast_punish", 1f);
                    so.allowMovement = GetBool(rules, "allow_movement");
                    so.automatic = GetBool(rules, "automatic");
                }

                var constraints = spellData.GetValueOrDefault("constraints") as Dictionary<string, object>;
                if (constraints != null)
                {
                    so.maxInstances = GetInt(constraints, "max_instances");
                    so.allowOverlap = GetBool(constraints, "allow_overlap", true);
                }

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

                var meta = spellData.GetValueOrDefault("meta") as Dictionary<string, object>;
                if (meta != null)
                {
                    so.speedMultiplier = GetFloat(meta, "speed_multiplier", 1f);
                    so.offset = GetFloat(meta, "offset");
                }

                string assetPath = $"{outputDir}/{spellKey}.asset";
                AssetDatabase.CreateAsset(so, assetPath);
                report.AddOk(source, spellKey);
                count++;
            }

            if (!dryRun && count > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            report.PrintToConsole($"Spells ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return report;
        }

        [MenuItem("Valkur/Migration/Import Players from Python JSON")]
        public static void ImportPlayers() => ImportPlayers(dryRun: false);

        public static MigrationReport ImportPlayers(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "new_players.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "entities/new_players.json"));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-", $"File not found: {jsonPath}");
                report.PrintToConsole($"Players ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                report.AddError(source, "-", "Failed to parse JSON root.");
                report.PrintToConsole($"Players ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string outputDir = Path.Combine(SO_OUTPUT_ROOT, "Players");
            if (!dryRun && !AssetDatabase.IsValidFolder(outputDir))
            {
                AssetDatabase.CreateFolder(SO_OUTPUT_ROOT, "Players");
            }

            var players = parsed.GetValueOrDefault("players") as Dictionary<string, object>;
            if (players == null) { report.AddError(source, "-", "Missing 'players' key."); report.PrintToConsole($"Players ({(dryRun ? "DRY-RUN" : "IMPORT")})"); return report; }

            var classes = players.GetValueOrDefault("classes") as Dictionary<string, object>;
            if (classes == null) { report.AddError(source, "-", "Missing 'players.classes' key."); report.PrintToConsole($"Players ({(dryRun ? "DRY-RUN" : "IMPORT")})"); return report; }

            int count = 0;
            foreach (var kvp in classes)
            {
                string className = kvp.Key;
                var classCfg = kvp.Value as Dictionary<string, object>;
                if (classCfg == null)
                {
                    report.AddError(source, className, "Entry is not a valid dictionary.");
                    continue;
                }

                if (string.IsNullOrEmpty(className))
                {
                    report.AddError(source, "(empty)", "Player key is empty.");
                    continue;
                }

                // Validate required fields
                var stats = classCfg.GetValueOrDefault("stats") as Dictionary<string, object>;
                if (stats == null)
                {
                    report.AddWarning(source, className, "Missing 'stats' block — will use zero defaults.");
                }
                else
                {
                    float speed = GetFloat(stats, "basic_speed");
                    int hp = GetInt(stats, "initial_strength");
                    if (speed <= 0f)
                        report.AddWarning(source, className, $"basic_speed is {speed} (expected > 0).");
                    if (hp <= 0)
                        report.AddWarning(source, className, $"initial_strength is {hp} (expected > 0).");
                }

                if (dryRun)
                {
                    if (!HasErrorForKey(report, className))
                        report.AddOk(source, className, "Validated (dry-run).");
                    count++;
                    continue;
                }

                var so = ScriptableObject.CreateInstance<PlayerDefinition>();
                so.playerKey = className;
                so.displayName = className;

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
                report.AddOk(source, className);
                count++;
            }

            if (!dryRun && count > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            report.PrintToConsole($"Players ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return report;
        }

        [MenuItem("Valkur/Migration/Import All Python Data")]
        public static void ImportAll() => ImportAll(dryRun: false);

        [MenuItem("Valkur/Migration/Dry-Run All (Validate Only)")]
        public static void DryRunAll() => ImportAll(dryRun: true);

        public static MigrationReport ImportAll(bool dryRun)
        {
            var combined = new MigrationReport();
            combined.Merge(ImportMonsters(dryRun));
            combined.Merge(ImportSpells(dryRun));
            combined.Merge(ImportPlayers(dryRun));
            combined.PrintToConsole($"ALL DATA ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return combined;
        }

        /// <summary>
        /// Check if the report already has an Error-level entry for the given entity key.
        /// </summary>
        private static bool HasErrorForKey(MigrationReport report, string entityKey)
        {
            foreach (var e in report.Entries)
            {
                if (e.Severity == MigrationSeverity.Error && e.EntityKey == entityKey)
                    return true;
            }
            return false;
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
