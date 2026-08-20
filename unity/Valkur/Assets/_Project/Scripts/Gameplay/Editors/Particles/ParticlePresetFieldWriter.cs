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
