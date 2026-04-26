using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class PythonDataMigrator
    {

        // Spells import is exposed via Valkur/Spells/Import Spells from Python JSON (SpellDataImporter.cs)
        // Kept as internal method for use by Import All Python Data
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
                    report.AddWarning(source, className, "Missing 'stats' block â€” will use zero defaults.");
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
    }
}
