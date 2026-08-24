using System;
using System.Reflection;
using UnityEngine;
using Valkur.Core;
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

        /// <summary>Field name behind the Sorting Layer dropdown, without the vfx prefix.</summary>
        private const string SORTING_LAYER_FIELD = "sortingLayer";

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

            // Definition-level keys belong to the asset and have no counterpart on a block.
            if (!key.StartsWith(VFX_PREFIX, StringComparison.Ordinal))
            {
                var defField = typeof(ParticlePresetDefinition).GetField(key,
                    BindingFlags.Public | BindingFlags.Instance);
                if (defField == null)
                {
                    error = $"Field '{key}' not found on {nameof(ParticlePresetDefinition)}.";
                    return false;
                }
                if (!TryConvert(value, defField.FieldType, out object defValue, out error)) return false;
                defField.SetValue(def, defValue);
                return true;
            }

            return TrySetField(def.vfx, key, value, out error);
        }

        /// <summary>
        /// The same, writing into a bare <see cref="ParticleVfxParams"/> — a placed instance's
        /// OWN configuration rather than a preset asset.
        ///
        /// This is what makes copy-on-place editable: the F1 properties form drives the block
        /// the selected emitter is actually running, and the preset it came from is left alone.
        /// Only <c>vfx.*</c> keys are accepted; <c>displayName</c> and the other
        /// definition-level fields name the asset, and an instance has no business renaming it.
        /// </summary>
        public static bool TrySetField(ParticleVfxParams vfx, string key, object value,
                                       out string error)
        {
            error = null;
            if (vfx == null) { error = "No configuration to edit."; return false; }
            if (string.IsNullOrEmpty(key)) { error = "Empty field key."; return false; }

            if (!key.StartsWith(VFX_PREFIX, StringComparison.Ordinal))
            {
                error = $"'{key}' belongs to the preset asset, not to one placement.";
                return false;
            }

            // Virtual keys first: the per-particle variation pair and the over-life
            // gradient live in arrays, which the reflection path refuses on purpose.
            // These give them a scalar-shaped surface the form's colour rows can drive.
            if (key == "vfx.colors.a" || key == "vfx.colors.b")
                return TrySetVariationColor(vfx, key.EndsWith(".a"), value, out error);
            if (key.StartsWith("vfx.colorOverLife.", StringComparison.Ordinal))
                return TrySetGradientStop(vfx, key.Substring("vfx.colorOverLife.".Length),
                                          value, out error);

            // sortingLayer stores a NAME but is edited through a dropdown, and a dropdown
            // reports the selected INDEX. Left to the reflection path below, TryConvert
            // would see an int landing on a string field and store the literal "4" — a name
            // no sorting layer has, which ParticleEmitter would quietly resolve back to VFX
            // while the panel showed the author their pick. Resolve the index against the
            // same list the row was built from instead. A string still falls through to
            // reflection, so setting the name directly (tests, a future text widget, a
            // paste) keeps working.
            if (key == VFX_PREFIX + SORTING_LAYER_FIELD && value is int layerIndex)
                return TrySetSortingLayer(vfx, layerIndex, out error);

            object target = vfx;
            string fieldName = key.Substring(VFX_PREFIX.Length);

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
        private static bool TrySetVariationColor(ParticleVfxParams v, bool endA,
                                                 object value, out string error)
        {
            if (!TryParseColor(value, out var c, out error)) return false;
            if (v == null) { error = "No configuration to edit."; return false; }
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
        /// One stop of the over-life gradient, addressed as start / mid / end.
        ///
        /// THE RULE — Birth and Death are the outermost keys, Middle is the interior key
        /// nearest t = 0.5, and nothing else in the array is touched:
        ///
        ///   Birth  → key[0]                          (earliest key)
        ///   Death  → key[last]                       (latest key)
        ///   Middle → nearest t = 0.5 over 1..last-1  (see <see cref="MidStopIndex"/>)
        ///
        /// Every other key keeps its time AND its colour, so a five- or eight-stop gradient
        /// survives an edit intact; the stops the three rows do not address stay
        /// Inspector-only, which the panel footer states.
        ///
        /// This replaces a normalisation to exactly three keys at t = 0 / 0.5 / 1. That
        /// rewrite was silently destructive: touching ANY of the three rows deleted every
        /// intermediate key and moved the surviving ones onto 0 / 0.5 / 1. It went unnoticed
        /// only because every preset authored so far happens to hold exactly three keys —
        /// the first richer gradient anyone tuned would have died on its first colour edit.
        ///
        /// Restricting Middle to the INTERIOR is what keeps the three rows independent: on a
        /// two-key gradient "nearest 0.5" would land on an endpoint, and editing Middle would
        /// silently overwrite Birth or Death. Below three keys the array is therefore GROWN
        /// rather than rewritten (<see cref="GrowToThreeStops"/>) — the inserted key carries
        /// the colour the gradient already evaluates at that time, so the render is identical
        /// before and after apart from the one colour actually edited.
        ///
        /// An EMPTY array is the one case with nothing to preserve, so it still seeds the
        /// canonical three keys at 0 / 0.5 / 1 — there the three rows genuinely ARE the
        /// gradient.
        ///
        /// If the preset has no alphaOverLife, one is seeded with the exact fade the engine
        /// hard-codes when the field is empty (1 → 0.5 at 0.6 → 0): colourOverLife is
        /// IGNORED unless alphaOverLife is authored, so without this the user would edit a
        /// gradient, see nothing change, and reasonably file it as a bug.
        /// </summary>
        private static bool TrySetGradientStop(ParticleVfxParams v, string stop,
                                               object value, out string error)
        {
            if (!TryParseColor(value, out var c, out error)) return false;
            if (stop != "start" && stop != "mid" && stop != "end")
            {
                error = $"Unknown gradient stop '{stop}' — use start, mid or end.";
                return false;
            }

            if (v == null) { error = "No configuration to edit."; return false; }

            var keys = v.colorOverLife;
            if (keys == null || keys.Length == 0)
            {
                keys = new[]
                {
                    new ColorKeyframe(0f,   Color.white),
                    new ColorKeyframe(0.5f, Color.white),
                    new ColorKeyframe(1f,   Color.white),
                };
            }
            else if (keys.Length < 3)
            {
                keys = GrowToThreeStops(keys);
            }

            int idx = stop == "start" ? 0
                    : stop == "end"   ? keys.Length - 1
                    :                   MidStopIndex(keys);
            if (idx < 0 || idx >= keys.Length)
            {
                // Unreachable: all three branches above leave at least three keys. Kept
                // because this class's contract is to report, never to throw at a UI handler.
                error = $"Gradient stop '{stop}' has no key to write ({keys.Length} keys).";
                return false;
            }

            // ColorKeyframe is a struct, but an array element is a variable — this mutates
            // the slot in place rather than a copy, and leaves .time alone on purpose.
            keys[idx].color = c;
            v.colorOverLife = keys;

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

        /// <summary>
        /// Index of the key the form's "Middle" row addresses: the INTERIOR key — never the
        /// first, never the last — whose time sits closest to 0.5. Ties go to the lower
        /// index so the answer is stable across calls. Returns -1 when the gradient holds
        /// fewer than three keys and therefore has no interior key yet; the form shows the
        /// interpolated midpoint in that case and <see cref="TrySetGradientStop"/> only
        /// materialises a real key if the row is actually edited.
        ///
        /// Public because the form and the writer MUST resolve "Middle" to the same key. A
        /// row that displays key 2 and writes key 3 is a lie the author cannot see.
        /// </summary>
        public static int MidStopIndex(ColorKeyframe[] keys)
        {
            if (keys == null || keys.Length < 3) return -1;

            int best = 1;
            float bestDist = Mathf.Abs(keys[1].time - 0.5f);
            for (int i = 2; i < keys.Length - 1; i++)
            {
                float d = Mathf.Abs(keys[i].time - 0.5f);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Grows a one- or two-key gradient to three WITHOUT changing what it renders, so
        /// the start / mid / end rows address three distinct keys. Existing keys keep their
        /// exact times and colours; the added ones carry the colour the gradient already
        /// evaluated at that time.
        ///
        /// Two keys → insert the midpoint between them (time averaged, colour lerped).
        /// One key  → a one-key gradient is a constant, so two more keys of the same colour
        ///            change nothing; the original is placed as Birth / Middle / Death
        ///            according to where its own time already sits.
        /// </summary>
        private static ColorKeyframe[] GrowToThreeStops(ColorKeyframe[] keys)
        {
            if (keys.Length == 2)
            {
                var a = keys[0];
                var b = keys[1];
                return new[]
                {
                    a,
                    new ColorKeyframe((a.time + b.time) * 0.5f, Color.Lerp(a.color, b.color, 0.5f)),
                    b,
                };
            }

            var k = keys[0];
            if (k.time <= 0f)
                return new[] { k, new ColorKeyframe((k.time + 1f) * 0.5f, k.color), new ColorKeyframe(1f, k.color) };
            if (k.time >= 1f)
                return new[] { new ColorKeyframe(0f, k.color), new ColorKeyframe(k.time * 0.5f, k.color), k };
            return new[] { new ColorKeyframe(0f, k.color), k, new ColorKeyframe(1f, k.color) };
        }

        // ------------------------------------------------------------------ sorting layer

        /// <summary>
        /// The Sorting Layer option list, in the order Unity draws them — exactly
        /// <see cref="SortingLayer.layers"/>, which is already ordered back-to-front and is
        /// the only authority on what the project actually defines.
        /// <see cref="SortingConfig"/>'s constants are a convenience copy that cannot notice
        /// a layer added, renamed or reordered in ProjectSettings > Tags and Layers, so they
        /// are deliberately NOT the source here.
        ///
        /// Public because the dropdown and <see cref="TrySetField"/> MUST derive the list the
        /// same way: the widget hands back an index, so any drift between the two turns
        /// "pick WallsBottom" into "store whatever sits at slot 4 today". One method, called
        /// by both, is what makes the index meaningful.
        ///
        /// <paramref name="authored"/> is appended when it names a layer the project no
        /// longer has — a preset authored before someone renamed or deleted a layer. Without
        /// it the row would preselect a layer the preset does not hold, and the author would
        /// be editing a value they cannot see. It is listed undecorated: ParticleEmitter
        /// already logs one warning per unknown name per session saying it renders on VFX
        /// instead, and a decorated label would have to be stripped again on the way back
        /// into the field.
        /// </summary>
        public static string[] SortingLayerNames(string authored)
        {
            var layers = SortingLayer.layers;

            bool orphan = !string.IsNullOrEmpty(authored);
            for (int i = 0; orphan && i < layers.Length; i++)
                if (layers[i].name == authored) orphan = false;

            var names = new string[layers.Length + (orphan ? 1 : 0)];
            for (int i = 0; i < layers.Length; i++) names[i] = layers[i].name;
            if (orphan) names[layers.Length] = authored;
            return names;
        }

        /// <summary>
        /// Which entry of <see cref="SortingLayerNames"/> the row must show for
        /// <paramref name="authored"/>.
        ///
        /// An EMPTY authored value preselects VFX rather than getting an entry of its own.
        /// Empty and "VFX" are the same picture — <c>ResolveSortingLayerName</c> in
        /// ParticleEmitter.Colors.cs maps "" onto <see cref="SortingConfig.LAYER_VFX"/>
        /// before the renderer is touched — so an explicit "(default: VFX)" entry would put
        /// two visibly different choices in the list that render identically, and leave the
        /// author to work out that they do. Preselecting VFX makes the row state the layer
        /// the emitter really draws in, always. The cost is that picking VFX by hand turns ""
        /// into "VFX" on the .asset; that is a no-op for rendering, strictly more legible on
        /// disk, and nothing in the codebase treats "" as distinct from "VFX".
        ///
        /// Falling back to the first entry is unreachable in a sane project (VFX exists, and
        /// an unknown authored name was appended by <see cref="SortingLayerNames"/>) but not
        /// in one where VFX itself was deleted — better the wrong layer preselected than an
        /// out-of-range index handed to the dropdown.
        /// </summary>
        public static int SortingLayerIndex(string authored)
        {
            string want = string.IsNullOrEmpty(authored) ? SortingConfig.LAYER_VFX : authored;
            var names = SortingLayerNames(authored);
            for (int i = 0; i < names.Length; i++)
                if (names[i] == want) return i;
            return 0;
        }

        /// <summary>
        /// Dropdown index → layer name. The index addresses
        /// <see cref="SortingLayerNames"/> built from the preset's CURRENT authored value,
        /// which is precisely the list the form built its options from: the panel rebuilds
        /// itself after every accepted edit, so the options cannot have shifted underneath
        /// the row between build and pick — unless someone edits Tags and Layers with the
        /// panel open, which is what the range check is for.
        /// </summary>
        private static bool TrySetSortingLayer(ParticleVfxParams v, int index, out string error)
        {
            error = null;
            if (v == null) { error = "No configuration to edit."; return false; }

            var names = SortingLayerNames(v.sortingLayer);
            if (index < 0 || index >= names.Length)
            {
                error = $"Sorting layer index {index} is outside the {names.Length} layers " +
                        "this project defines — reopen the panel if Tags and Layers changed.";
                return false;
            }

            v.sortingLayer = names[index];
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
