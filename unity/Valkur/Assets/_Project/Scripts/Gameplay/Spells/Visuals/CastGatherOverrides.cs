using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Applies a spell's authored <see cref="CastGatherOverride"/> on top of the family
    /// profile its type resolved to, and describes the knobs so the Spells Editor can build
    /// a form without keeping its own copy of the list.
    ///
    /// <para>WHY REFLECTION. <see cref="CastFlourishProfile"/> is thirty tunables. Bridging
    /// them by hand would mean three parallel lists — the struct, the apply switch and the
    /// editor rows — and CLAUDE.md already records what that costs: an eighth
    /// <c>AnimState</c> pays the positional tax four times over, which is why
    /// <c>attackVariants</c> is a list. Here the struct is the single declaration and both
    /// the apply and the form are derived from it, so a knob added to a family is authorable
    /// the moment it exists and no list can fall behind.</para>
    ///
    /// <para>The cost is paid once per cast, in <see cref="CastFlourishProfile.Build"/>, not
    /// per frame — and the <see cref="FieldInfo"/> lookup itself is cached, so applying an
    /// override is a dictionary hit and a boxed set per pinned knob.</para>
    /// </summary>
    internal static class CastGatherOverrides
    {
        /// <summary>
        /// The one knob that is NOT authorable. <c>FamilyName</c> is the answer to "which
        /// gesture is this", written by the family that built the profile and read by the
        /// editor to label the tab and by tests to assert dispatch. Letting a spell rewrite
        /// it would let the label disagree with every other value in the struct.
        /// </summary>
        internal const string FAMILY_NAME_FIELD = "FamilyName";

        [SelfHealingStatic("Reflection map over CastFlourishProfile's own fields, built once " +
                           "from a compile-time type. Holds no Unity objects and is never " +
                           "mutated after construction, so it cannot go stale across a Play " +
                           "session or a domain reload.")]
        private static readonly Dictionary<string, FieldInfo> Knobs = BuildKnobMap();

        /// <summary>Warned-once per unknown or unparseable knob, so a bad entry says so exactly once.</summary>
        private static HashSet<string> _warned = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _warned = new HashSet<string>();
        }

        private static Dictionary<string, FieldInfo> BuildKnobMap()
        {
            var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (var field in typeof(CastFlourishProfile)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == FAMILY_NAME_FIELD) continue;
                map[field.Name] = field;
            }
            return map;
        }

        /// <summary>Every authorable knob, in declaration order. Used by the editor form and by tests.</summary>
        internal static IEnumerable<FieldInfo> AuthorableKnobs()
        {
            foreach (var field in typeof(CastFlourishProfile)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == FAMILY_NAME_FIELD) continue;
                yield return field;
            }
        }

        internal static FieldInfo Knob(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Knobs.TryGetValue(name, out var field) ? field : null;
        }

        /// <summary>
        /// The value the family alone would give for one knob — what an unpinned row shows.
        /// Boxed, because the caller renders it as text either way.
        /// </summary>
        internal static object Read(CastFlourishProfile profile, string knobName)
        {
            var field = Knob(knobName);
            return field == null ? null : field.GetValue(profile);
        }

        /// <summary>
        /// Overlay <paramref name="overrides"/> onto <paramref name="profile"/>.
        ///
        /// <para>Only knobs actually present in the bag are touched, so a spell that pins the
        /// gather length alone still tracks its family for everything else — which is the
        /// point of pinning per knob rather than wholesale. A pinned knob naming a field that
        /// no longer exists, or an enum member that does not parse, is skipped with one
        /// warning rather than throwing: the flourish must still play.</para>
        /// </summary>
        internal static CastFlourishProfile Apply(CastFlourishProfile profile,
                                                  CastGatherOverride overrides)
        {
            if (overrides == null || overrides.Count == 0) return profile;

            // Boxed once: FieldInfo.SetValue on a struct writes to the box, so setting each
            // knob against the struct directly would discard every write.
            object boxed = profile;

            var fields = overrides.fields;
            for (int i = 0; i < fields.Count; i++)
            {
                var entry = fields[i];
                if (entry == null || string.IsNullOrEmpty(entry.name)) continue;

                var field = Knob(entry.name);
                if (field == null)
                {
                    WarnOnce(entry.name, $"[CastGather] '{entry.name}' is not a CastFlourishProfile " +
                                         "knob — the override is ignored.");
                    continue;
                }

                if (TryCoerce(entry, field.FieldType, out object value))
                    field.SetValue(boxed, value);
            }

            return (CastFlourishProfile)boxed;
        }

        /// <summary>
        /// Turn one authored entry into a value of the knob's type. Which of the entry's two
        /// payloads is read follows from that TYPE rather than from what the entry happens to
        /// carry, so a number left behind by a knob that has since become an enum is ignored
        /// instead of being coerced into whichever member sits at that index.
        /// </summary>
        private static bool TryCoerce(CastGatherOverride.Field entry, Type knobType, out object value)
        {
            value = null;

            if (knobType == typeof(float))   { value = entry.number; return true; }
            if (knobType == typeof(int))     { value = Mathf.RoundToInt(entry.number); return true; }
            // Any non-zero is true, so a checkbox round-trips through the same numeric payload
            // every other knob uses and the bag needs no third field.
            if (knobType == typeof(bool))    { value = !Mathf.Approximately(entry.number, 0f); return true; }

            if (knobType.IsEnum)
            {
                if (string.IsNullOrEmpty(entry.text))
                {
                    WarnOnce(entry.name, $"[CastGather] '{entry.name}' is a {knobType.Name} but " +
                                         "carries no member name — the override is ignored.");
                    return false;
                }
                try
                {
                    value = Enum.Parse(knobType, entry.text, ignoreCase: true);
                    return true;
                }
                catch (ArgumentException)
                {
                    WarnOnce(entry.name + ":" + entry.text,
                        $"[CastGather] '{entry.text}' is not a {knobType.Name} member — " +
                        $"the override on '{entry.name}' is ignored.");
                    return false;
                }
            }

            WarnOnce(entry.name, $"[CastGather] '{entry.name}' is a {knobType.Name}, which the " +
                                 "override bag cannot carry — the override is ignored.");
            return false;
        }

        private static void WarnOnce(string token, string message)
        {
            if (_warned.Contains(token)) return;
            _warned.Add(token);
            Debug.LogWarning(message);
        }
    }
}
