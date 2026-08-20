using System;
using System.Reflection;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Applies one edited value from the F1 Properties form onto a
    /// <see cref="ParticlePresetDefinition"/>, by key.
    ///
    /// Kept as a pure static class — no UI, no scene — so the EditMode tests can drive
    /// every conversion and failure path without building the panel.
    ///
    /// Keys are two-level: <c>"displayName"</c> resolves against the definition itself,
    /// <c>"vfx.emitRate"</c> against the nested <see cref="ParticleVfxParams"/>. The
    /// Spells editor's flat <c>GetField</c> lookup cannot be reused here because
    /// reflection does not traverse into member objects — <c>GetField("emitRate")</c> on
    /// the definition returns null, and every vfx field would report "not found".
    /// </summary>
    public static class ParticlePresetFieldWriter
    {
        private const string VFX_PREFIX = "vfx.";

        /// <summary>
        /// Sets <paramref name="key"/> on <paramref name="def"/> to <paramref name="value"/>.
        /// Returns false with a human-readable <paramref name="error"/> instead of throwing:
        /// the caller is a UI handler, and a typo in a row key must surface in the status
        /// line, not as an exception swallowed by the event system.
        /// </summary>
        public static bool TrySetField(ParticlePresetDefinition def, string key, object value,
                                       out string error)
        {
            error = null;
            if (def == null) { error = "No preset selected."; return false; }
            if (string.IsNullOrEmpty(key)) { error = "Empty field key."; return false; }

            // Virtual keys first: the per-particle variation pair and the over-life
            // gradient live in arrays, which the reflection path refuses on purpose.
            // These give them a scalar-shaped surface the form's colour rows can drive.
            if (key == "vfx.colors.a" || key == "vfx.colors.b")
                return TrySetVariationColor(def, key.EndsWith(".a"), value, out error);
            if (key.StartsWith("vfx.colorOverLife.", StringComparison.Ordinal))
                return TrySetGradientStop(def, key.Substring("vfx.colorOverLife.".Length),
                                          value, out error);

            object target;
            string fieldName;
            if (key.StartsWith(VFX_PREFIX, StringComparison.Ordinal))
            {
                if (def.vfx == null) { error = "Preset has no vfx block."; return false; }
                target = def.vfx;
                fieldName = key.Substring(VFX_PREFIX.Length);
            }
            else
            {
                target = def;
                fieldName = key;
            }

            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                error = $"Field '{fieldName}' not found on {target.GetType().Name}.";
                return false;
            }

            // Arrays and object references need dedicated widgets (colour lists, curve
            // editors, sprite pickers). Refusing here keeps the failure explicit if a row
            // for one is ever added before its widget exists.
            if (field.FieldType.IsArray || typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
            {
                error = $"Field '{fieldName}' ({field.FieldType.Name}) needs a dedicated widget.";
                return false;
            }

            if (!TryConvert(value, field.FieldType, out object converted, out error))
                return false;

            // Honour the [Range] the Inspector would have enforced. AddFloat happily
            // accepts 9999 into a 0..1 field; the clamp is the difference between a
            // designer typo and a preset that silently breaks its own invariants.
            if (converted is float f)
            {
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null) converted = Mathf.Clamp(f, range.min, range.max);
            }
            else if (converted is int i)
            {
                var range = field.GetCustomAttribute<RangeAttribute>();
                if (range != null) converted = Mathf.Clamp(i, (int)range.min, (int)range.max);
            }

            field.SetValue(target, converted);
            return true;
        }

        /// <summary>
        /// One end of the per-particle variation pair. BuildColorParameter randomises
        /// between cols[0] and cols[last] and ignores everything between, so the pair IS
        /// the authorable surface; a shorter array is grown to two, seeded from the base
        /// colour so the untouched end keeps looking like the preset did.
        /// </summary>
        private static bool TrySetVariationColor(ParticlePresetDefinition def, bool endA,
                                                 object value, out string error)
        {
            if (!TryParseColor(value, out var c, out error)) return false;
            var v = def.vfx;
            if (v.colors == null || v.colors.Length < 2)
            {
                var seed = (v.colors != null && v.colors.Length == 1) ? v.colors[0] : v.color;
                v.colors = new[] { seed, seed };
            }
            if (endA) v.colors[0] = c;
            else      v.colors[v.colors.Length - 1] = c;
            return true;
        }

        /// <summary>
        /// One stop of the over-life gradient, addressed as start / mid / end. The array is
        /// normalised to exactly three keys at t = 0, 0.5 and 1 — richer gradients exist
        /// only in a handful of hand-tuned presets, and three stops is what this panel can
        /// honestly present as three rows.
        ///
        /// If the preset has no alphaOverLife, one is seeded with the exact fade the engine
        /// hard-codes when the field is empty (1 → 0.5 at 0.6 → 0): colourOverLife is
        /// IGNORED unless alphaOverLife is authored, so without this the user would edit a
        /// gradient, see nothing change, and reasonably file it as a bug.
        /// </summary>
        private static bool TrySetGradientStop(ParticlePresetDefinition def, string stop,
                                               object value, out string error)
        {
            if (!TryParseColor(value, out var c, out error)) return false;

            int idx;
            switch (stop)
            {
                case "start": idx = 0; break;
                case "mid":   idx = 1; break;
                case "end":   idx = 2; break;
                default:
                    error = $"Unknown gradient stop '{stop}' — use start, mid or end.";
                    return false;
            }

            var v = def.vfx;
            Color s0 = Color.white, s1 = Color.white, s2 = Color.white;
            if (v.colorOverLife != null && v.colorOverLife.Length > 0)
            {
                s0 = v.colorOverLife[0].color;
                s2 = v.colorOverLife[v.colorOverLife.Length - 1].color;
                s1 = v.colorOverLife.Length >= 3
                    ? v.colorOverLife[v.colorOverLife.Length / 2].color
                    : Color.Lerp(s0, s2, 0.5f);
            }
            if (idx == 0) s0 = c; else if (idx == 1) s1 = c; else s2 = c;

            v.colorOverLife = new[]
            {
                new ColorKeyframe(0f, s0),
                new ColorKeyframe(0.5f, s1),
                new ColorKeyframe(1f, s2),
            };

            if (v.alphaOverLife == null || v.alphaOverLife.Length == 0)
            {
                v.alphaOverLife = new[]
                {
                    new Keyframe2D(0f, 1f),
                    new Keyframe2D(0.6f, 0.5f),
                    new Keyframe2D(1f, 0f),
                };
            }
            return true;
        }

        private static bool TryParseColor(object value, out Color color, out string error)
        {
            error = null;
            if (value is Color c) { color = c; return true; }
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                string hex = s.StartsWith("#") ? s : "#" + s;
                if (ColorUtility.TryParseHtmlString(hex, out color)) return true;
            }
            color = default;
            error = $"'{value}' is not a colour — use #RRGGBB or #RRGGBBAA.";
            return false;
        }

        private static bool TryConvert(object value, Type targetType, out object converted,
                                       out string error)
        {
            converted = null;
            error = null;

            if (value != null && targetType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            // The form's rows emit string (AddText), int (AddInt and dropdown index),
            // float (AddFloat) and bool (AddBool). Everything below maps those onto the
            // field types they may legitimately land on.
            if (targetType == typeof(float))
            {
                switch (value)
                {
                    case int i: converted = (float)i; return true;
                    case string s when float.TryParse(s, out var f): converted = f; return true;
                }
            }
            else if (targetType == typeof(int))
            {
                switch (value)
                {
                    case float f: converted = Mathf.RoundToInt(f); return true;
                    case string s when int.TryParse(s, out var i): converted = i; return true;
                }
            }
            else if (targetType.IsEnum)
            {
                switch (value)
                {
                    // A dropdown reports the selected index.
                    case int idx:
                        var values = Enum.GetValues(targetType);
                        if (idx >= 0 && idx < values.Length)
                        {
                            converted = values.GetValue(idx);
                            return true;
                        }
                        error = $"Index {idx} is outside {targetType.Name}.";
                        return false;
                    case string s when Enum.IsDefined(targetType, s):
                        converted = Enum.Parse(targetType, s);
                        return true;
                }
            }
            else if (targetType == typeof(Color))
            {
                if (TryParseColor(value, out var col, out error)) { converted = col; return true; }
                return false;
            }
            else if (targetType == typeof(string) && value != null)
            {
                converted = value.ToString();
                return true;
            }

            error = $"Cannot convert {(value == null ? "null" : value.GetType().Name)} " +
                    $"to {targetType.Name}.";
            return false;
        }
    }
}
