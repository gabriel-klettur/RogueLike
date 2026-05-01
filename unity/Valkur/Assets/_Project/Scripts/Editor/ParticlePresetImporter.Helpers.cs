using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class ParticlePresetImporter
    {
        // ------------------------------------------------------------------ catalog

        private static void BuildOrUpdateCatalog(
            List<ParticlePresetDefinition> defs,
            MigrationReport report,
            string source)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }
            catalog.SetPresets(defs);
            EditorUtility.SetDirty(catalog);
            report.AddOk(source, "ParticlePresetCatalog", $"Catalog updated with {defs.Count} presets at {CATALOG_PATH}.");
        }

        // ------------------------------------------------------------------ directory

        /// <summary>
        /// Returns true for one-shot burst kinds that should NOT loop.
        /// Rule: explosion, smoke_burst, slash, firework → finite (loops=false).
        /// All other kinds are continuous (loops=true).
        /// </summary>
        internal static bool IsFiniteKind(string kind)
        {
            return kind is "explosion" or "smoke_burst" or "slash" or "firework";
        }

        private static void EnsureOutputDirectory(bool dryRun)
        {
            if (dryRun) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Catalogs"))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Catalogs");
            if (!AssetDatabase.IsValidFolder(SO_OUTPUT_DIR))
                AssetDatabase.CreateFolder("Assets/_Project/Data/Catalogs", "Particles");
        }

        // ------------------------------------------------------------------ JSON helpers

        private static Color[] ParseColorList(List<object> list)
        {
            var result = new Color[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i] as List<object>;
                result[i] = entry != null && entry.Count >= 3 ? ParseRgb(entry) : Color.white;
            }
            return result;
        }

        private static Color ParseRgb(List<object> rgb)
        {
            float r = GetListFloat(rgb, 0) / 255f;
            float g = GetListFloat(rgb, 1) / 255f;
            float b = GetListFloat(rgb, 2) / 255f;
            return new Color(r, g, b, 1f);
        }

        private static float GetListFloat(List<object> list, int index)
        {
            if (index < 0 || index >= list.Count) return 0f;
            try { return Convert.ToSingle(list[index]); } catch { return 0f; }
        }

        private static string GetString(Dictionary<string, object> d, string key, string def)
        {
            if (d.TryGetValue(key, out var v) && v != null) return v.ToString();
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                try { return Convert.ToSingle(v); } catch { }
            return def;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                try { return Convert.ToInt32(v); } catch { }
            return def;
        }
    }
}
