using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class ParticlePresetImporter
    {
        // ------------------------------------------------------------------ conversion

        private static ParticlePresetDefinition ConvertPreset(
            string presetId,
            Dictionary<string, object> data,
            MigrationReport report,
            string source,
            bool dryRun)
        {
            string name = GetString(data, "name", presetId);
            string type = GetString(data, "type", "");

            var vfxData = data.GetValueOrDefault("vfx") as Dictionary<string, object>;
            var particlesData = vfxData?.GetValueOrDefault("particles") as Dictionary<string, object>;

            if (particlesData == null)
            {
                report.AddWarning(source, presetId, "No 'vfx.particles' block found — using defaults.");
                particlesData = new Dictionary<string, object>();
            }

            var vfxParams = ConvertVfxParams(presetId, particlesData, report, source);

            if (dryRun)
            {
                report.AddOk(source, presetId, $"Validated (dry-run): kind='{vfxParams.kind}', type='{type}'.");
                return null;
            }

            // Create or overwrite the ScriptableObject asset
            string assetPath = Path.Combine(SO_OUTPUT_DIR, $"PP_{presetId}.asset").Replace('\\', '/');

            var existing = AssetDatabase.LoadAssetAtPath<ParticlePresetDefinition>(assetPath);
            ParticlePresetDefinition so;
            if (existing != null)
            {
                so = existing;
            }
            else
            {
                so = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
                AssetDatabase.CreateAsset(so, assetPath);
            }

            so.id          = presetId;
            so.displayName = name;
            so.type        = type;
            so.vfx         = vfxParams;

            EditorUtility.SetDirty(so);
            report.AddOk(source, presetId, $"Imported: kind='{vfxParams.kind}', type='{type}'.");
            return so;
        }

        private static ParticleVfxParams ConvertVfxParams(
            string presetId,
            Dictionary<string, object> p,
            MigrationReport report,
            string source)
        {
            var v = new ParticleVfxParams();

            v.kind = GetString(p, "kind", "explosion");

            // ---- Emission ----
            float rawEmitRate = GetFloat(p, "emit_rate", 0f);
            v.emitRate = rawEmitRate > 0f ? rawEmitRate * TICK_RATE : 10f;
            v.count    = GetInt(p, "count", 12);
            v.burstIntervalSeconds = GetFloat(p, "interval_ms", 0f) / 1000f;

            // ---- Motion ----
            // speed_px (pixels/s in Python dash emitter) or speed (pixels/tick)
            float speedPx = GetFloat(p, "speed_px", 0f);
            float speedTick = GetFloat(p, "speed", 0f);
            if (speedPx > 0f)
                v.speed = speedPx / PPU;                // px/s → world/s (dash emitter uses px/s already)
            else
                v.speed = speedTick * TICK_RATE / PPU; // px/tick → world/s

            v.gravity = GetFloat(p, "gravity", 0f) * (TICK_RATE * TICK_RATE) / PPU;

            // gravity can be a [gx, gy] vector in some presets (e.g. ember_plume, nebulous_smoke)
            var gravityRaw = p.GetValueOrDefault("gravity");
            if (gravityRaw is List<object> gravityList && gravityList.Count >= 2)
            {
                float gx = GetListFloat(gravityList, 0) * (TICK_RATE * TICK_RATE) / PPU;
                float gy = GetListFloat(gravityList, 1) * (TICK_RATE * TICK_RATE) / PPU;
                // Python Y-down → Unity Y-up: flip gy sign
                v.gravityVector = new Vector2(gx, -gy);
                v.useGravityVector = true;
                v.gravity = 0f; // scalar unused when vector is active
            }
            else
            {
                v.useGravityVector = false;
            }

            v.drag    = Mathf.Clamp(GetFloat(p, "drag", 0f), 0f, 0.98f);

            var dirList = p.GetValueOrDefault("direction") as List<object>;
            if (dirList != null && dirList.Count >= 2)
                v.direction = new Vector2(GetListFloat(dirList, 0), GetListFloat(dirList, 1));

            // ---- Lifetime ----
            float lifespanTicks = GetFloat(p, "lifespan", 0f);
            float lifeMs        = GetFloat(p, "life_ms", 0f);
            float lifetimeSec   = GetFloat(p, "lifetime", 0f); // some entries use "lifetime" in ticks
            if (lifespanTicks > 0f)
                v.lifespan = lifespanTicks / TICK_RATE;
            else if (lifeMs > 0f)
                v.lifespan = lifeMs / 1000f;
            else if (lifetimeSec > 0f)
                v.lifespan = lifetimeSec / TICK_RATE;
            else
                v.lifespan = 1f;

            // ---- Size ----
            var sizeRange = p.GetValueOrDefault("size_range") as List<object>;
            if (sizeRange != null && sizeRange.Count >= 2)
            {
                v.sizeMin = GetListFloat(sizeRange, 0) / PPU;
                v.sizeMax = GetListFloat(sizeRange, 1) / PPU;
            }
            else
            {
                // Some presets use a plain "size" field as [w, h] or single int
                var sizeField = p.GetValueOrDefault("size");
                float singleSize = 0f;
                if (sizeField is List<object> sizeList && sizeList.Count >= 1)
                    singleSize = GetListFloat(sizeList, 0);
                else if (sizeField != null)
                    try { singleSize = Convert.ToSingle(sizeField); } catch { }

                v.sizeMin = singleSize > 0f ? singleSize / PPU : 2f / PPU;
                v.sizeMax = singleSize > 0f ? singleSize / PPU * 1.5f : 4f / PPU;
            }

            // ---- Colors ----
            var colorsList = p.GetValueOrDefault("colors") as List<object>;
            var singleColor = p.GetValueOrDefault("color") as List<object>;
            if (colorsList != null && colorsList.Count > 0)
            {
                v.colors = ParseColorList(colorsList);
                v.color  = v.colors[0];
            }
            else if (singleColor != null && singleColor.Count >= 3)
            {
                v.color  = ParseRgb(singleColor);
                v.colors = new[] { v.color };
            }
            else
            {
                v.colors = new[] { Color.white };
                v.color  = Color.white;
            }

            v.additive = string.Equals(GetString(p, "blend_mode", ""), "additive", StringComparison.OrdinalIgnoreCase);

            // ---- Shape ----
            v.radius          = GetFloat(p, "radius", 24f) / PPU;
            v.arcRangeDegrees = GetFloat(p, "arc_range_degrees", 45f);
            v.segments        = GetInt(p, "segments", 10);
            v.lightningOffset = GetFloat(p, "offset", 10f) / PPU;
            v.thickness       = GetFloat(p, "thickness", 2f) / PPU;

            // ---- Water Fountain ----
            var spoutsRaw = p.GetValueOrDefault("spouts") as List<object>;
            if (spoutsRaw != null)
            {
                v.spouts = new float[spoutsRaw.Count];
                for (int i = 0; i < spoutsRaw.Count; i++)
                    try { v.spouts[i] = Convert.ToSingle(spoutsRaw[i]); } catch { v.spouts[i] = 0.5f; }
            }
            v.splashCount = GetInt(p, "splash_count", 2);
            v.dropletSize = GetFloat(p, "droplet_size", 2f) / PPU;

            // ---- Falling Leaf ----
            v.swayAmp   = GetFloat(p, "sway_amp",   0.7f) / PPU;
            v.swaySpeed = GetFloat(p, "sway_speed", 0.12f);

            // ---- Water Flow ----
            v.stripeGap      = GetFloat(p, "stripe_gap", 8f) / PPU;
            v.rippleAmp      = GetFloat(p, "ripple_amp", 0.6f);
            v.alphaBase      = Mathf.Clamp(GetInt(p, "alpha_base", 110), 0, 255);
            v.alphaWave      = Mathf.Clamp(GetInt(p, "alpha_wave", 70), 0, 255);
            var hlColor      = p.GetValueOrDefault("highlight_color") as List<object>;
            if (hlColor != null && hlColor.Count >= 3)
                v.highlightColor = ParseRgb(hlColor);

            // ---- Dispersion (smoke_emitter emission spread) ----
            float rawDispersion = GetFloat(p, "dispersion", 0f);
            v.dispersion = rawDispersion / PPU;

            // ---- Curves ----
            v.sizeOverLife = ParseKeyframeCurve(p.GetValueOrDefault("size_over_life") as List<object>);
            v.alphaOverLife = ParseKeyframeCurve(p.GetValueOrDefault("alpha_over_life") as List<object>);
            v.colorOverLife = ParseColorKeyframeCurve(p.GetValueOrDefault("color_over_life") as List<object>);

            // ---- Portal ----
            v.ellipseRatio = GetFloat(p, "ellipse_ratio", 1f);
            v.outerRadius = GetFloat(p, "outer_radius", 0f) / PPU;

            return v;
        }

        // ------------------------------------------------------------------ curve helpers

        private static Keyframe2D[] ParseKeyframeCurve(List<object> list)
        {
            if (list == null || list.Count == 0) return null;
            var result = new Keyframe2D[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i] as List<object>;
                if (entry == null || entry.Count < 2) continue;
                result[i] = new Keyframe2D(
                    Mathf.Clamp01(GetListFloat(entry, 0)),
                    GetListFloat(entry, 1));
            }
            return result;
        }

        private static ColorKeyframe[] ParseColorKeyframeCurve(List<object> list)
        {
            if (list == null || list.Count == 0) return null;
            var result = new ColorKeyframe[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i] as List<object>;
                if (entry == null || entry.Count < 2) continue;
                float t = Mathf.Clamp01(GetListFloat(entry, 0));
                var rgb = entry[1] as List<object>;
                Color c = (rgb != null && rgb.Count >= 3) ? ParseRgb(rgb) : Color.white;
                result[i] = new ColorKeyframe(t, c);
            }
            return result;
        }
    }
}
