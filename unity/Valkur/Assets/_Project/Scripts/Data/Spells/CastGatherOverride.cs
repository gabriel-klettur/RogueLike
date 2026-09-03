using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-spell overrides for the cast flourish — the gather, the release and every
    /// shape the caster's gesture is made of.
    ///
    /// <para>WHY A BAG AND NOT FIELDS. The gesture lives in
    /// <c>CastFlourishProfile</c> (Valkur.Gameplay), a struct of thirty tunables built by
    /// one of nine hard-coded family functions. Mirroring it here as thirty
    /// <c>bool has… / float value…</c> pairs would create a THIRD parallel list beside the
    /// struct and the editor form, and the three would drift the first time a family grew
    /// a knob — the positional tax <c>EntityAssetConfig.attackVariants</c> exists to
    /// avoid. Entries are keyed by the profile's own field NAME instead, so a new tunable
    /// is authorable the moment it is declared and no list has to be maintained by
    /// hand.</para>
    ///
    /// <para>PRESENCE IS THE SWITCH. An entry in <see cref="fields"/> means "this spell
    /// pins this knob"; its absence means "keep taking it from the family". There is no
    /// separate enabled flag, so the two can never disagree — a checkbox in the Gather tab
    /// adds or removes the entry outright.</para>
    ///
    /// <para>Enums travel as <see cref="Field.text"/> rather than as an index, for the
    /// reason <c>SpellDefinition.animState</c> is a string: the enums are declared in
    /// Valkur.Gameplay and this assembly may not reference it. A name also survives a
    /// reordering of the enum, which an index does not.</para>
    /// </summary>
    [Serializable]
    public class CastGatherOverride
    {
        /// <summary>
        /// One pinned knob. <see cref="name"/> matches a <c>CastFlourishProfile</c> field
        /// exactly; which of <see cref="number"/> / <see cref="text"/> is read follows
        /// from that field's TYPE, so a value authored against a float knob is simply
        /// ignored if the knob later becomes an enum rather than being silently coerced.
        /// </summary>
        [Serializable]
        public class Field
        {
            [Tooltip("Name of the CastFlourishProfile field this pins. Case-sensitive.")]
            public string name;

            [Tooltip("Value for a float / int / bool knob. A bool reads any non-zero as true.")]
            public float number;

            [Tooltip("Value for an enum knob, as the member's name.")]
            public string text;
        }

        [Tooltip("Knobs this spell pins. Anything not listed keeps coming from the family.")]
        public List<Field> fields = new List<Field>();

        /// <summary>Number of knobs pinned. Zero means the spell is pure family.</summary>
        public int Count => fields != null ? fields.Count : 0;

        public bool Has(string name) => Find(name) != null;

        public Field Find(string name)
        {
            if (fields == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f != null && string.Equals(f.name, name, StringComparison.Ordinal)) return f;
            }
            return null;
        }

        /// <summary>Pin a numeric knob, replacing any existing entry for it.</summary>
        public void SetNumber(string name, float value)
        {
            var f = Ensure(name);
            if (f != null) f.number = value;
        }

        /// <summary>Pin an enum knob by member name, replacing any existing entry for it.</summary>
        public void SetText(string name, string value)
        {
            var f = Ensure(name);
            if (f != null) f.text = value;
        }

        /// <summary>Unpin a knob. Returns true when something was actually removed.</summary>
        public bool Clear(string name)
        {
            var f = Find(name);
            if (f == null) return false;
            fields.Remove(f);
            return true;
        }

        /// <summary>Unpin everything — the spell goes back to its family wholesale.</summary>
        public void ClearAll() => fields?.Clear();

        private Field Ensure(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (fields == null) fields = new List<Field>();

            var existing = Find(name);
            if (existing != null) return existing;

            var created = new Field { name = name };
            fields.Add(created);
            return created;
        }
    }
}
