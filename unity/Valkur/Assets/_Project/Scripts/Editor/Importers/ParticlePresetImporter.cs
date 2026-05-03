using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Imports Python's data/particles/particles.json into Unity ParticlePresetDefinition
    /// ScriptableObjects and assembles them into a ParticlePresetCatalog asset.
    ///
    /// Menu: Valkur > Particles > Import Presets from Python JSON
    ///
    /// Unit conversions applied during import (Python → Unity):
    ///   size (pixels)          → world units:    value / PPU
    ///   speed (px/tick)        → world units/s:  value * TICK_RATE / PPU
    ///   gravity (px/tick²)     → world units/s²: value * TICK_RATE² / PPU
    ///   emit_rate (ptcl/tick)  → particles/s:    value * TICK_RATE
    ///   lifespan (ticks)       → seconds:        value / TICK_RATE
    ///   life_ms (milliseconds) → seconds:        value / 1000
    ///   radius (pixels)        → world units:    value / PPU
    ///   RGB [0..255]           → Unity Color:    component / 255
    /// </summary>
    public static partial class ParticlePresetImporter
    {
        private const float PPU = 32f;           // pixels per Unity world unit (matches TILE_PPU in ValkurAssetPostprocessor)
        private const float TICK_RATE = 60f;     // Python ECS ticks per second

        private const string PYTHON_PARTICLES_JSON = "../../../python/data/particles/particles.json";
        private const string SO_OUTPUT_DIR = "Assets/_Project/Data/Catalogs/Particles";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        // ------------------------------------------------------------------ menu items

        [MenuItem("Valkur/Particles/Import Presets from Python JSON")]
        public static void ImportPresetsMenu() => ImportPresets(dryRun: false);

        [MenuItem("Valkur/Particles/Dry-Run Preset Import")]
        public static void DryRunMenu() => ImportPresets(dryRun: true);

        [MenuItem("Valkur/Particles/Backfill loops attribute on existing presets")]
        public static void BackfillLoopsAttribute()
        {
            string[] guids = AssetDatabase.FindAssets("t:ParticlePresetDefinition",
                new[] { "Assets/_Project/Data" });

            int updated = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var preset  = AssetDatabase.LoadAssetAtPath<ParticlePresetDefinition>(path);
                if (preset == null || preset.vfx == null) continue;

                bool expected = !IsFiniteKind(preset.vfx.kind);
                if (preset.vfx.loops != expected)
                {
                    preset.vfx.loops = expected;
                    EditorUtility.SetDirty(preset);
                    updated++;
                    Debug.Log($"[ParticlePresetImporter] Backfilled loops={expected} on '{preset.id}' (kind='{preset.vfx.kind}').");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ParticlePresetImporter] Backfill complete: {updated} preset(s) updated out of {guids.Length} scanned.");
        }

        // ------------------------------------------------------------------ public API

        public static MigrationReport ImportPresets(bool dryRun)
        {
            var report = new MigrationReport();
            const string source = "particles.json";

            string jsonPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, PYTHON_PARTICLES_JSON));

            if (!File.Exists(jsonPath))
            {
                report.AddError(source, "-", $"File not found: {jsonPath}");
                report.PrintToConsole($"Particle Presets ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            string json = File.ReadAllText(jsonPath);

            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                report.AddError(source, "-", "Failed to parse JSON root (expected object).");
                report.PrintToConsole($"Particle Presets ({(dryRun ? "DRY-RUN" : "IMPORT")})");
                return report;
            }

            EnsureOutputDirectory(dryRun);

            var createdPresets = new List<ParticlePresetDefinition>();

            foreach (var kvp in parsed)
            {
                string presetId = kvp.Key;
                var data = kvp.Value as Dictionary<string, object>;
                if (data == null)
                {
                    report.AddWarning(source, presetId, "Entry is not a valid dictionary, skipping.");
                    continue;
                }

                try
                {
                    var def = ConvertPreset(presetId, data, report, source, dryRun);
                    if (def != null)
                        createdPresets.Add(def);
                }
                catch (Exception ex)
                {
                    report.AddError(source, presetId, $"Exception during conversion: {ex.Message}");
                }
            }

            if (!dryRun && createdPresets.Count > 0)
            {
                BuildOrUpdateCatalog(createdPresets, report, source);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            report.PrintToConsole($"Particle Presets ({(dryRun ? "DRY-RUN" : "IMPORT")}) — {createdPresets.Count} entries");
            return report;
        }
    }
}
