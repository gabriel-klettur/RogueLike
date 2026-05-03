using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class PythonDataMigrator
    {
        // ──────────────────────────────────────────────────────────────────────
        // Neutrals (vendors / NPCs) import
        // ──────────────────────────────────────────────────────────────────────

        [MenuItem("Valkur/Migration/Import Neutrals (Vendors) from Python JSON")]
        public static void ImportNeutrals() => ImportNeutrals(dryRun: false);

        public static MigrationReport ImportNeutrals(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "new_neutrals.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_DATA_ROOT, "entities/new_neutrals.json"));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-", $"File not found: {jsonPath}");
                report.PrintToConsole($"Neutrals ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);
            ImportNeutralsManual(json, dryRun, report);
            report.PrintToConsole($"Neutrals ({(dryRun ? "DRY-RUN" : "IMPORT")})");
            return report;
        }

        private static void ImportNeutralsManual(string json, bool dryRun, MigrationReport report)
        {
            const string source = "new_neutrals.json";

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

            var neutrals = parsed.GetValueOrDefault("neutrals") as Dictionary<string, object>;
            if (neutrals == null) { report.AddError(source, "-", "Missing 'neutrals' key."); return; }

            var classes = neutrals.GetValueOrDefault("classes") as Dictionary<string, object>;
            if (classes == null) { report.AddError(source, "-", "Missing 'neutrals.classes' key."); return; }

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
                    report.AddError(source, "(empty)", "Neutral key is empty.");
                    continue;
                }

                if (dryRun)
                {
                    report.AddOk(source, className, "Validated (dry-run).");
                    count++;
                    continue;
                }

                var stats = classCfg.GetValueOrDefault("stats") as Dictionary<string, object>;

                var so = ScriptableObject.CreateInstance<MonsterDefinition>();
                so.monsterKey = className;
                so.displayName = classCfg.GetValueOrDefault("default_name") as string ?? className;
                so.fsmSet = classCfg.GetValueOrDefault("fsm_set") as string ?? "";
                so.useAttackTelegraph = false;

                if (stats != null)
                {
                    so.stats = new EntityStats
                    {
                        hp = GetInt(stats, "hp"),
                        speed = GetFloat(stats, "speed"),
                        faction = stats.GetValueOrDefault("faction") as string ?? "NEUTRAL",
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
                        chatRange = GetFloat(stats, "chat_range")
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
                                    tint = ParseTint(scaleData)
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

        // ──────────────────────────────────────────────────────────────────────
        // MonsterCatalog builder
        // ──────────────────────────────────────────────────────────────────────

        private const string MONSTER_CATALOG_PATH = "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";

        [MenuItem("Valkur/Migration/Build Monster Catalog")]
        public static void BuildMonsterCatalog()
        {
            string monstersDir = Path.Combine(SO_OUTPUT_ROOT, "Monsters");
            if (!AssetDatabase.IsValidFolder(monstersDir))
            {
                Debug.LogWarning("[PythonDataMigrator] Monsters folder not found. Import monsters first.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(MONSTER_CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
                AssetDatabase.CreateAsset(catalog, MONSTER_CATALOG_PATH);
            }

            string[] guids = AssetDatabase.FindAssets("t:MonsterDefinition", new[] { monstersDir });
            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def != null)
                {
                    catalog.UpsertDefinition(def);
                    count++;
                }
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PythonDataMigrator] MonsterCatalog rebuilt with {count} definitions.");
        }
    }
}
