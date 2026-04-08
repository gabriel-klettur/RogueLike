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
            var rawList = EditorMiniJson.Deserialize(json) as List<object>;
            if (rawList == null)
            {
                Debug.LogWarning("[SpawnerDataImporter] Failed to parse templates JSON (expected array).");
                return;
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
                    var so = ImportOneTemplate(dict);
                    if (so != null)
                    {
                        catalog.UpsertTemplate(so);
                        count++;
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

        private static SpawnerTemplateData ImportOneTemplate(Dictionary<string, object> dict)
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
            so.spawnRadius = GetInt(dict, "spawn_radius", 20);
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
                so.maxActive = GetInt(pol, "max_active");
                so.persistent = GetBool(pol, "persistent");
                so.restartOnDone = GetBool(pol, "restart_on_done");
            }

            // Waves (inline)
            so.waves = new List<WaveDefinition>();
            if (dict.TryGetValue("waves", out var wavesObj) && wavesObj is List<object> wavesList)
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
            else if (dict.TryGetValue("waves_id", out var wid) && wid is string wavesIdStr)
            {
                so.wavesId = wavesIdStr;
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

    /// <summary>
    /// Minimal JSON runtime deserializer for editor importers.
    /// Separate from Valkur.Gameplay.World.MiniJsonRuntime to avoid assembly coupling.
    /// </summary>
    internal static class EditorMiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;
            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') { index += 4; return null; }
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++;
            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (json[index] == '}') { index++; return dict; }
                if (json[index] == ',') { index++; continue; }
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                index++;
                object value = ParseValue(json, ref index);
                dict[key] = value;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++;
            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (json[index] == ']') { index++; return list; }
                if (json[index] == ',') { index++; continue; }
                list.Add(ParseValue(json, ref index));
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            index++;
            var sb = new System.Text.StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && index < json.Length)
                {
                    char next = json[index++];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                index += 4;
                            }
                            break;
                        default: sb.Append(next); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && "0123456789.eE+-".IndexOf(json[index]) >= 0) index++;
            string num = json.Substring(start, index - start);
            if (num.Contains(".") || num.Contains("e") || num.Contains("E"))
            {
                if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(num, out long l)) return l;
            }
            return 0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json[index] == 't') { index += 4; return true; }
            index += 5;
            return false;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }
    }
}
