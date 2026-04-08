using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool to import spawner data from Python JSON files.
    /// Sources:
    ///   - python/data/spawners/spawners_templates.json → SpawnerTemplateData SOs + SpawnerTemplateCatalog
    ///   - python/data/worlds/base/spawners/spawners_instances.json → StreamingAssets copy
    ///
    /// Menu: Valkur > Spawners > Import Templates / Copy Instances to StreamingAssets
    /// </summary>
    public static class SpawnerDataImporter
    {
        private const string TEMPLATES_JSON = "python/data/spawners/spawners_templates.json";
        private const string WAVES_JSON = "python/data/spawners/spawners_waves.json";
        private const string INSTANCES_JSON = "python/data/worlds/base/spawners/spawners_instances.json";
        private const string SO_OUTPUT = "Assets/_Project/Data/Catalogs/Spawners";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset";

        [MenuItem("Valkur/Spawners/Import Templates from Python JSON")]
        public static void ImportTemplates()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string templatesFile = Path.Combine(projectRoot, TEMPLATES_JSON);

            if (!File.Exists(templatesFile))
            {
                Debug.LogWarning($"[SpawnerDataImporter] Templates file not found: {templatesFile}");
                return;
            }

            EnsureDirectory(SO_OUTPUT);

            string json = File.ReadAllText(templatesFile);
            var rawList = MiniJson.Deserialize(json) as List<object>;
            if (rawList == null)
            {
                Debug.LogWarning("[SpawnerDataImporter] Failed to parse templates JSON (expected array).");
                return;
            }

            Debug.Log($"[SpawnerDataImporter] Parsed {rawList.Count} template entries from JSON.");

            // Load external waves catalog
            string wavesFile = Path.Combine(projectRoot, WAVES_JSON);
            Dictionary<string, List<object>> externalWaves = null;
            if (File.Exists(wavesFile))
            {
                string wavesJson = File.ReadAllText(wavesFile);
                var wavesRoot = MiniJson.Deserialize(wavesJson) as Dictionary<string, object>;
                if (wavesRoot != null)
                {
                    externalWaves = new Dictionary<string, List<object>>();
                    foreach (var kv in wavesRoot)
                    {
                        if (kv.Value is List<object> wList)
                            externalWaves[kv.Key] = wList;
                        else if (kv.Value is Dictionary<string, object> envelope
                                 && envelope.TryGetValue("waves", out var inner)
                                 && inner is List<object> innerList)
                            externalWaves[kv.Key] = innerList;
                    }
                    Debug.Log($"[SpawnerDataImporter] Loaded {externalWaves.Count} external wave definitions.");
                }
            }

            // Load or create catalog
            var catalog = AssetDatabase.LoadAssetAtPath<SpawnerTemplateCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SpawnerTemplateCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }

            int count = 0;
            foreach (var item in rawList)
            {
                if (item is Dictionary<string, object> dict)
                {
                    try
                    {
                        var so = ImportOneTemplate(dict, externalWaves);
                        if (so != null)
                        {
                            catalog.UpsertTemplate(so);
                            count++;
                        }
                        else
                        {
                            string failId = dict.TryGetValue("id", out var idv) ? idv?.ToString() : "?";
                            Debug.LogWarning($"[SpawnerDataImporter] ImportOneTemplate returned null for '{failId}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        string failId = dict.TryGetValue("id", out var idv) ? idv?.ToString() : "?";
                        Debug.LogError($"[SpawnerDataImporter] Failed to import template '{failId}': {ex}");
                    }
                }
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SpawnerDataImporter] Imported {count} spawner templates.");
        }

        [MenuItem("Valkur/Spawners/Copy Instances to StreamingAssets")]
        public static void CopyInstances()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string srcFile = Path.Combine(projectRoot, INSTANCES_JSON);

            if (!File.Exists(srcFile))
            {
                Debug.LogWarning($"[SpawnerDataImporter] Instances file not found: {srcFile}");
                return;
            }

            string destDir = Path.Combine(Application.streamingAssetsPath, "Spawners");
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            string destFile = Path.Combine(destDir, "spawners_instances.json");
            File.Copy(srcFile, destFile, true);
            AssetDatabase.Refresh();
            Debug.Log("[SpawnerDataImporter] Copied spawner instances to StreamingAssets/Spawners/.");
        }

        private static SpawnerTemplateData ImportOneTemplate(Dictionary<string, object> dict,
            Dictionary<string, List<object>> externalWaves)
        {
            string id = GetString(dict, "id");
            if (string.IsNullOrEmpty(id)) return null;

            string assetPath = $"{SO_OUTPUT}/SpawnerTemplate_{id}.asset";
            var so = AssetDatabase.LoadAssetAtPath<SpawnerTemplateData>(assetPath);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<SpawnerTemplateData>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.templateId = id;
            so.spawnerType = GetString(dict, "spawner_type") == "visual" ? SpawnerType.Visual : SpawnerType.Invisible;
            so.spawnerShape = GetString(dict, "spawner_shape") == "circle" ? SpawnerShape.Circle : SpawnerShape.Square;

            // spawn_radius can be an integer or the string "random"
            if (dict.TryGetValue("spawn_radius", out var srVal) && srVal is string srStr && srStr == "random")
            {
                so.randomSpawnRadius = true;
                so.spawnRadius = 20; // default fallback
            }
            else
            {
                so.randomSpawnRadius = false;
                so.spawnRadius = GetInt(dict, "spawn_radius", 20);
            }

            so.defendSpawn = GetBool(dict, "defend_spawn", true);
            so.defendLeash = GetBool(dict, "defend_leash", true);
            so.visibleInGame = GetBool(dict, "visible_in_game");

            // Trigger
            if (dict.TryGetValue("trigger", out var trigObj) && trigObj is Dictionary<string, object> trig)
            {
                so.triggerType = GetString(trig, "type") == "auto" ? TriggerType.Auto : TriggerType.Proximity;
                so.triggerRadius = GetFloat(trig, "radius", 10f);
                so.autoStart = GetBool(trig, "auto_start", true);
            }

            // Policy
            if (dict.TryGetValue("policy", out var polObj) && polObj is Dictionary<string, object> pol)
            {
                so.spawnMode = GetString(pol, "mode") == "burst" ? SpawnMode.Burst : SpawnMode.Periodic;
                so.cooldownSeconds = GetFloat(pol, "cooldown_s", 1f);
                so.proximityInitialOnly = GetBool(pol, "proximity_initial_only", true);
                so.betweenWavesCooldownSeconds = GetFloat(pol, "between_waves_cooldown_s", 5f);
                so.advanceOn = GetString(pol, "advance_on", "clear") == "cooldown" ? AdvanceOn.Cooldown : AdvanceOn.Clear;
                so.maxActive = GetInt(pol, "max_active");
                so.persistent = GetBool(pol, "persistent");
                so.restartOnDone = GetBool(pol, "restart_on_done");
                so.restartCooldownSeconds = GetFloat(pol, "restart_cooldown_s", 0f);
            }

            // Waves — resolve waves_id first, then fall back to inline
            so.waves = new List<WaveDefinition>();
            string wavesIdStr = GetString(dict, "waves_id");
            if (!string.IsNullOrEmpty(wavesIdStr))
            {
                so.wavesId = wavesIdStr;
                if (externalWaves != null && externalWaves.TryGetValue(wavesIdStr, out var extWavesList))
                {
                    foreach (var waveItem in extWavesList)
                    {
                        if (waveItem is Dictionary<string, object> waveDict)
                        {
                            var waveDef = ParseWave(waveDict);
                            if (waveDef != null)
                                so.waves.Add(waveDef);
                        }
                    }
                    Debug.Log($"[SpawnerDataImporter] Resolved waves_id '{wavesIdStr}' → {so.waves.Count} waves for '{so.templateId}'.");
                }
                else
                {
                    Debug.LogWarning($"[SpawnerDataImporter] waves_id '{wavesIdStr}' not found in external catalog for '{so.templateId}'.");
                }
            }
            else if (dict.TryGetValue("waves", out var wavesObj) && wavesObj is List<object> wavesList)
            {
                foreach (var waveItem in wavesList)
                {
                    if (waveItem is Dictionary<string, object> waveDict)
                    {
                        var waveDef = ParseWave(waveDict);
                        if (waveDef != null)
                            so.waves.Add(waveDef);
                    }
                }
            }

            EditorUtility.SetDirty(so);
            return so;
        }

        private static WaveDefinition ParseWave(Dictionary<string, object> dict)
        {
            var wave = new WaveDefinition();
            if (dict.TryGetValue("spawns", out var spawnsObj) && spawnsObj is List<object> spawnsList)
            {
                foreach (var spawnItem in spawnsList)
                {
                    if (spawnItem is Dictionary<string, object> spawnDict)
                    {
                        var entry = new WaveSpawnEntry
                        {
                            kind = GetString(spawnDict, "kind", "monster"),
                            entityId = GetString(spawnDict, "id"),
                            count = GetInt(spawnDict, "count", 1),
                            spreadRadius = GetFloat(spawnDict, "spread_radius", 3f),
                            spreadFallbackMax = GetFloat(spawnDict, "spread_fallback_max", 12f),
                            minDistance = GetFloat(spawnDict, "min_px_distance", 24f)
                        };
                        wave.spawns.Add(entry);
                    }
                }
            }
            return wave;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void EnsureDirectory(string assetPath)
        {
            string fullPath = Path.Combine(Application.dataPath, assetPath.Replace("Assets/", ""));
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s) return s;
            return fallback;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null) return Convert.ToInt32(v);
            return fallback;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float fallback = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null) return Convert.ToSingle(v);
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d.TryGetValue(key, out var v) && v is bool b) return b;
            return fallback;
        }
    }
}
