using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Reads and writes a <see cref="ParticleVfxParams"/> as compact JSON, for the per-instance
    /// configurations stored in <c>StreamingAssets/Particles/particles_instances.json</c>.
    ///
    /// WHY NOT JsonUtility. It writes all sixty-odd fields for every block, and a world with
    /// 185 placed emitters — several of them composites with three layers each — turns into
    /// half a megabyte of mostly-default noise in a file this project reads by eye. This writer
    /// emits only what DIFFERS FROM THE TYPE'S OWN DEFAULTS, which for a typical preset is
    /// around twenty keys.
    ///
    /// Compared against the DEFAULTS, never against the preset. An instance's configuration has
    /// to be independent of later preset edits — that is the whole point of copy-on-place — and
    /// a diff against the preset would quietly re-link them: every field the instance happened
    /// to share would follow the asset the next time someone retuned it.
    ///
    /// Object references (<c>customSprite</c>, <c>flipbookFrames</c>) are skipped: a Sprite has
    /// no representation here. They keep coming from the preset — see
    /// <see cref="ParticleInstanceConfig"/>.
    ///
    /// Round-trip contract: <c>Read(Write(x))</c> equals x for every field this writer emits.
    /// Pinned by ParticleCopyOnPlaceTests over the whole shipped catalog.
    /// </summary>
    public static class ParticleVfxParamsJson
    {
        /// <summary>Fields are compared against a pristine instance, built once.</summary>
        [Valkur.Core.SelfHealingStatic("A pristine ParticleVfxParams used only as the right-hand " +
            "side of comparisons. Nothing in this file writes to it, and it holds no Unity " +
            "object, so carrying it across a domain reload cannot strand a destroyed reference.")]
        private static readonly ParticleVfxParams Defaults = new ParticleVfxParams();

        [Valkur.Core.SelfHealingStatic("Reflection metadata for a type in this same assembly, " +
            "read once and never mutated. A domain reload rebuilds the assembly and this table " +
            "with it; between reloads the type cannot change.")]
        private static readonly FieldInfo[] Fields = typeof(ParticleVfxParams)
            .GetFields(BindingFlags.Public | BindingFlags.Instance);

        // ── Write ────────────────────────────────────────────────────────────────

        /// <summary>Compact JSON object for one block, or <c>null</c> for a null block.</summary>
        public static string Write(ParticleVfxParams v)
        {
            if (v == null) return "null";

            var sb = new StringBuilder("{");
            bool first = true;

            foreach (var field in Fields)
            {
                if (!IsSupported(field.FieldType)) continue;

                object value = field.GetValue(v);
                object fallback = field.GetValue(Defaults);
                if (AreEqual(field.FieldType, value, fallback)) continue;

                if (!first) sb.Append(',');
                first = false;

                sb.Append('"').Append(field.Name).Append("\":");
                WriteValue(sb, field.FieldType, value);
            }

            return sb.Append('}').ToString();
        }

        // ── Read ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds a block from a parsed JSON object. Absent keys keep the TYPE's default,
        /// which is what makes the encoding lossless in both directions: the writer omits
        /// exactly the fields that already hold their default.
        /// </summary>
        public static ParticleVfxParams Read(Dictionary<string, object> obj)
        {
            var result = new ParticleVfxParams();
            if (obj == null) return result;

            foreach (var field in Fields)
            {
                if (!IsSupported(field.FieldType)) continue;
                if (!obj.TryGetValue(field.Name, out object raw) || raw == null) continue;

                object parsed = ReadValue(field.FieldType, raw);
                if (parsed != null) field.SetValue(result, parsed);
            }

            return result;
        }

        // ── Type support ─────────────────────────────────────────────────────────

        private static bool IsSupported(Type t)
        {
            if (t == typeof(float) || t == typeof(int) || t == typeof(bool) ||
                t == typeof(string) || t == typeof(Color) || t == typeof(Vector2) ||
                t.IsEnum) return true;

            return t == typeof(Color[]) || t == typeof(Keyframe2D[]) || t == typeof(ColorKeyframe[]);
        }

        private static bool AreEqual(Type t, object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;

            if (t == typeof(float)) return Mathf.Abs((float)a - (float)b) < 1e-6f;
            if (t == typeof(Color)) return (Color)a == (Color)b;
            if (t == typeof(Vector2)) return (Vector2)a == (Vector2)b;

            if (t == typeof(Color[])) return SameColors((Color[])a, (Color[])b);
            if (t == typeof(Keyframe2D[])) return SameKeys((Keyframe2D[])a, (Keyframe2D[])b);
            if (t == typeof(ColorKeyframe[])) return SameColorKeys((ColorKeyframe[])a, (ColorKeyframe[])b);

            return a.Equals(b);
        }

        private static bool SameColors(Color[] a, Color[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameKeys(Keyframe2D[] a, Keyframe2D[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].time - b[i].time) > 1e-6f ||
                    Mathf.Abs(a[i].value - b[i].value) > 1e-6f) return false;
            return true;
        }

        private static bool SameColorKeys(ColorKeyframe[] a, ColorKeyframe[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].time - b[i].time) > 1e-6f || a[i].color != b[i].color) return false;
            return true;
        }

        // ── Value writers ────────────────────────────────────────────────────────

        private static void WriteValue(StringBuilder sb, Type t, object value)
        {
            if (t == typeof(float)) { sb.Append(F((float)value)); return; }
            if (t == typeof(int)) { sb.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (t == typeof(bool)) { sb.Append((bool)value ? "true" : "false"); return; }
            if (t == typeof(string)) { sb.Append('"').Append(Escape((string)value)).Append('"'); return; }
            if (t.IsEnum) { sb.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }

            if (t == typeof(Color)) { WriteColor(sb, (Color)value); return; }
            if (t == typeof(Vector2))
            {
                var v = (Vector2)value;
                sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(']');
                return;
            }

            if (t == typeof(Color[]))
            {
                var arr = (Color[])value;
                sb.Append('[');
                for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(','); WriteColor(sb, arr[i]); }
                sb.Append(']');
                return;
            }

            if (t == typeof(Keyframe2D[]))
            {
                var arr = (Keyframe2D[])value;
                sb.Append('[');
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(F(arr[i].time)).Append(',').Append(F(arr[i].value)).Append(']');
                }
                sb.Append(']');
                return;
            }

            if (t == typeof(ColorKeyframe[]))
            {
                var arr = (ColorKeyframe[])value;
                sb.Append('[');
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(F(arr[i].time)).Append(',');
                    WriteColor(sb, arr[i].color);
                    sb.Append(']');
                }
                sb.Append(']');
                return;
            }
        }

        /// <summary>Colours are RGBA arrays: four numbers read better than four keys, and this
        /// file is reviewed by reading it.</summary>
        private static void WriteColor(StringBuilder sb, Color c)
            => sb.Append('[').Append(F(c.r)).Append(',').Append(F(c.g)).Append(',')
                 .Append(F(c.b)).Append(',').Append(F(c.a)).Append(']');

        /// <summary>Four decimals: enough for a colour channel and for a world-unit size at
        /// 16 PPU, short enough that a record stays one readable line.</summary>
        private static string F(float value)
            => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Escape(string s)
            => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── Value readers ────────────────────────────────────────────────────────

        private static object ReadValue(Type t, object raw)
        {
            if (t == typeof(float)) return ToFloat(raw);
            if (t == typeof(int)) return Mathf.RoundToInt(ToFloat(raw));
            if (t == typeof(bool)) return raw is bool b ? b : ToFloat(raw) != 0f;
            if (t == typeof(string)) return raw as string ?? "";
            if (t.IsEnum) return Enum.ToObject(t, Mathf.RoundToInt(ToFloat(raw)));

            if (t == typeof(Color)) return ToColor(raw as List<object>);
            if (t == typeof(Vector2))
            {
                var list = raw as List<object>;
                if (list == null || list.Count < 2) return null;
                return new Vector2(ToFloat(list[0]), ToFloat(list[1]));
            }

            if (t == typeof(Color[]))
            {
                if (!(raw is List<object> list)) return null;
                var arr = new Color[list.Count];
                for (int i = 0; i < list.Count; i++) arr[i] = ToColor(list[i] as List<object>);
                return arr;
            }

            if (t == typeof(Keyframe2D[]))
            {
                if (!(raw is List<object> list)) return null;
                var arr = new Keyframe2D[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    var pair = list[i] as List<object>;
                    if (pair == null || pair.Count < 2) continue;
                    arr[i] = new Keyframe2D(ToFloat(pair[0]), ToFloat(pair[1]));
                }
                return arr;
            }

            if (t == typeof(ColorKeyframe[]))
            {
                if (!(raw is List<object> list)) return null;
                var arr = new ColorKeyframe[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    var pair = list[i] as List<object>;
                    if (pair == null || pair.Count < 2) continue;
                    arr[i] = new ColorKeyframe(ToFloat(pair[0]), ToColor(pair[1] as List<object>));
                }
                return arr;
            }

            return null;
        }

        private static Color ToColor(List<object> list)
        {
            if (list == null || list.Count < 3) return Color.white;
            return new Color(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]),
                             list.Count > 3 ? ToFloat(list[3]) : 1f);
        }

        private static float ToFloat(object raw)
        {
            switch (raw)
            {
                case double d: return (float)d;
                case float f: return f;
                case long l: return l;
                case int i: return i;
                case bool b: return b ? 1f : 0f;
                case string s:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                        ? parsed : 0f;
                default: return 0f;
            }
        }
    }
}
