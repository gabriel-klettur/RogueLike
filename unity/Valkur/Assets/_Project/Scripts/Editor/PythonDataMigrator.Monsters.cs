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
                    report.AddWarning(source, className, "Missing 'stats' block â€” will use zero defaults.");
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

                // Parse assets block (no-sets directional sprites)
                var assetsBlock = classCfg.GetValueOrDefault("assets") as Dictionary<string, object>;
                if (assetsBlock != null)
                {
                    string activeSet = assetsBlock.GetValueOrDefault("active_set") as string ?? "no-sets";
                    if (activeSet == "no-sets")
                    {
                        var noSets = assetsBlock.GetValueOrDefault("no-sets") as Dictionary<string, object>;
                        if (noSets != null)
                        {
                            var assetConfig = new EntityAssetConfig();
                            assetConfig.idle = ResolveDirectionalSprites(noSets, "idle", report, source, className);
                            assetConfig.walk = ResolveDirectionalSprites(noSets, "walk", report, source, className);
                            assetConfig.chase = ResolveDirectionalSprites(noSets, "chase", report, source, className);
                            assetConfig.cast = ResolveDirectionalSprites(noSets, "casting", report, source, className);
                            assetConfig.attack = ResolveDirectionalSprites(noSets, "attack", report, source, className);
                            assetConfig.damage = ResolveDirectionalSprites(noSets, "damage", report, source, className);
                            assetConfig.death = ResolveDirectionalSprites(noSets, "death", report, source, className);

                            var scaleData = noSets.GetValueOrDefault("sprites_data_no-set") as Dictionary<string, object>;
                            if (scaleData != null)
                            {
                                assetConfig.scaleConfig = new AnimationScaleConfig
                                {
                                    scaleIdle = GetFloat(scaleData, "scale_idle", 1f),
                                    scaleWalk = GetFloat(scaleData, "scale_walk", 1f),
                                    scaleChase = GetFloat(scaleData, "scale_chase", 1f),
                                    scaleCast = GetFloat(scaleData, "scale_cast", 1f),
                                    scaleAttack = GetFloat(scaleData, "scale_attack", 1f),
                                    scaleDamage = GetFloat(scaleData, "scale_damage", 1f),
                                    scaleDeath = GetFloat(scaleData, "scale_death", 1f),
                                    tint = Color.white
                                };
                            }

                            so.assetConfig = assetConfig;
                        }
                    }
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
    }
}
