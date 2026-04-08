using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Imports light presets from python/data/light/presets.json → LightPresetDefinition SOs.
    /// Copies light_instances.json → StreamingAssets/Lights/.
    /// Menu: Valkur > Lighting > Import Presets from Python JSON
    /// Menu: Valkur > Lighting > Copy Light Instances to StreamingAssets
    /// </summary>
    public static class LightPresetImporter
    {
        private const string PRESET_OUTPUT_DIR = "Assets/_Project/Data/LightPresets";
        private const string CATALOG_PATH = "Assets/_Project/Data/LightPresetCatalog.asset";
        private const float PX_TO_WORLD_UNIT = 1f / 16f;

        [MenuItem("Valkur/Lighting/Import Presets from Python JSON")]
        public static void ImportPresets()
        {
            string jsonPath = FindPythonDataFile("light/presets.json");
            if (jsonPath == null)
            {
                Debug.LogError("[LightPresetImporter] Could not find python/data/light/presets.json");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var wrapper = JsonUtility.FromJson<PresetsWrapper>(json);
            if (wrapper?.presets == null || wrapper.presets.Count == 0)
            {
                // JsonUtility doesn't handle dict → try MiniJson
                var dict = ParsePresetsManually(json);
                if (dict == null || dict.Count == 0)
                {
                    Debug.LogError("[LightPresetImporter] Failed to parse presets.json.");
                    return;
                }
                ImportFromDict(dict);
                return;
            }
        }

        private static void ImportFromDict(Dictionary<string, PresetData> dict)
        {
            if (!AssetDatabase.IsValidFolder(PRESET_OUTPUT_DIR))
            {
                string parent = Path.GetDirectoryName(PRESET_OUTPUT_DIR).Replace('\\', '/');
                string folder = Path.GetFileName(PRESET_OUTPUT_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<LightPresetCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LightPresetCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }
            catalog.presets.Clear();

            int count = 0;
            foreach (var kvp in dict)
            {
                string key = kvp.Key;
                var data = kvp.Value;
                string assetPath = $"{PRESET_OUTPUT_DIR}/LightPreset_{key}.asset";

                var preset = AssetDatabase.LoadAssetAtPath<LightPresetDefinition>(assetPath);
                if (preset == null)
                {
                    preset = ScriptableObject.CreateInstance<LightPresetDefinition>();
                    AssetDatabase.CreateAsset(preset, assetPath);
                }

                preset.presetKey = key;
                preset.radius = data.radius;
                preset.intensity = data.intensity;
                preset.falloff = data.falloff;
                preset.color = new Color(data.color[0] / 255f, data.color[1] / 255f, data.color[2] / 255f, 1f);
                preset.flickerAmplitude = data.flicker_amp;
                preset.flickerSpeed = data.flicker_speed;
                preset.centerScale = data.center_scale;

                EditorUtility.SetDirty(preset);
                catalog.presets.Add(preset);
                count++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LightPresetImporter] Imported {count} light presets. Catalog at {CATALOG_PATH}");
        }

        [MenuItem("Valkur/Lighting/Copy Light Instances to StreamingAssets")]
        public static void CopyInstancesToStreamingAssets()
        {
            string srcPath = FindPythonDataFile("light/light_instances.json");
            if (srcPath == null)
            {
                Debug.LogError("[LightPresetImporter] Could not find python/data/light/light_instances.json");
                return;
            }

            string destDir = Path.Combine(Application.streamingAssetsPath, "Lights");
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, "light_instances.json");
            File.Copy(srcPath, destPath, true);
            AssetDatabase.Refresh();
            Debug.Log($"[LightPresetImporter] Copied light_instances.json → {destPath}");
        }

        private static Dictionary<string, PresetData> ParsePresetsManually(string json)
        {
            var result = new Dictionary<string, PresetData>();

            // Parse top-level "presets" object manually
            // Expected format: {"presets": {"Torch": {...}, "Lamp": {...}, ...}}
            int presetsIdx = json.IndexOf("\"presets\"", StringComparison.Ordinal);
            if (presetsIdx < 0) return result;

            // Find opening brace of presets object
            int braceStart = json.IndexOf('{', presetsIdx + 10);
            if (braceStart < 0) return result;

            // Find each preset key
            int pos = braceStart + 1;
            while (pos < json.Length)
            {
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find the object for this key
                int objStart = json.IndexOf('{', keyEnd);
                if (objStart < 0) break;

                int depth = 1;
                int objEnd = objStart + 1;
                while (objEnd < json.Length && depth > 0)
                {
                    if (json[objEnd] == '{') depth++;
                    else if (json[objEnd] == '}') depth--;
                    objEnd++;
                }

                string objJson = json.Substring(objStart, objEnd - objStart);
                var data = JsonUtility.FromJson<PresetData>(objJson);
                if (data != null)
                    result[key] = data;

                pos = objEnd;
            }

            return result;
        }

        private static string FindPythonDataFile(string relativePath)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string candidate = Path.Combine(dir, "python", "data", relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            return null;
        }

        [Serializable]
        private class PresetsWrapper
        {
            public Dictionary<string, PresetData> presets;
        }

        [Serializable]
        private class PresetData
        {
            public float radius = 500f;
            public float intensity = 1f;
            public float falloff = 2f;
            public float[] color = { 255, 200, 140 };
            public float flicker_amp = 0.15f;
            public float flicker_speed = 0.75f;
            public float center_scale = 0.25f;
        }
    }
}
