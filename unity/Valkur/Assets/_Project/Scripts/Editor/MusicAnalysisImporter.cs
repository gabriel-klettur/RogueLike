using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool: reads <c>python/data/audio/music_analysis.json</c> produced by
    /// <c>python/scripts/analyze_music.py</c> and patches the <see cref="AudioCatalogSO"/>
    /// track entries with <c>bpm</c>, <c>key</c>, <c>keyConfidence</c> and
    /// <c>firstBeatOffsetSec</c>.
    /// <para>Menu: <c>Valkur &gt; Audio &gt; Import BPM/Key Analysis</c>.</para>
    /// </summary>
    public static class MusicAnalysisImporter
    {
        private const string ANALYSIS_JSON = "python/data/audio/music_analysis.json";
        private const string CATALOG_PATH  = "Assets/_Project/Data/AudioCatalog.asset";

        [MenuItem("Valkur/Audio/Import BPM_Key Analysis", priority = 22)]
        public static void ImportFromJson()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string jsonPath    = Path.Combine(projectRoot, ANALYSIS_JSON);

            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog(
                    "Music analysis missing",
                    "music_analysis.json was not found.\n\n" +
                    "Run from the repo root with the venv active:\n" +
                    "  python python/scripts/analyze_music.py",
                    "OK");
                Debug.LogError($"[MusicAnalysisImporter] Not found: {jsonPath}");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalogSO>(CATALOG_PATH);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog(
                    "AudioCatalog missing",
                    "AudioCatalog.asset not found. Run\n" +
                    "Valkur > Audio > Import Catalog from Python JSON first.",
                    "OK");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("tracks", out var tracksObj) &&
                                  tracksObj is Dictionary<string, object> tracksDict))
            {
                Debug.LogError("[MusicAnalysisImporter] Malformed analysis JSON (missing 'tracks').");
                return;
            }

            var byStem = new Dictionary<string, Dictionary<string, object>>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tracksDict)
            {
                if (kv.Value is Dictionary<string, object> entry)
                    byStem[kv.Key] = entry;
            }

            int updated = 0, missing = 0;
            var unmatched = new List<string>();

            var tracks = catalog.Tracks;
            for (int i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                if (t == null || t.clip == null) { missing++; continue; }

                string stem = t.clip.name;
                if (!byStem.TryGetValue(stem, out var data))
                {
                    unmatched.Add($"{t.id} ({stem})");
                    continue;
                }

                t.bpm                = (float)GetDouble(data, "bpm",                t.bpm);
                t.firstBeatOffsetSec = (float)GetDouble(data, "first_beat_offset_sec", t.firstBeatOffsetSec);
                t.key                = GetString(data, "key", t.key);
                t.keyConfidence      = (float)GetDouble(data, "key_confidence",     t.keyConfidence);
                updated++;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary =
                $"Updated {updated} tracks. " +
                (missing > 0 ? $"{missing} entries had no clip. " : string.Empty) +
                (unmatched.Count > 0
                    ? $"\n\nNo analysis match for:\n - {string.Join("\n - ", unmatched)}"
                    : string.Empty);
            Debug.Log("[MusicAnalysisImporter] " + summary);
            EditorUtility.DisplayDialog("Music analysis imported", summary, "OK");
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            return d != null && d.TryGetValue(key, out var v) && v != null ? v.ToString() : fallback;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double fallback)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return fallback;
            switch (v)
            {
                case double dv: return dv;
                case float fv:  return fv;
                case long lv:   return lv;
                case int iv:    return iv;
                default:
                    return double.TryParse(v.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed) ? parsed : fallback;
            }
        }
    }
}
