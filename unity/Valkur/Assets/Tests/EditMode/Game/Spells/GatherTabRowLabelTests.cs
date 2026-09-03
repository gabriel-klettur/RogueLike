using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// What the Gather tab's rows SAY, as opposed to what its data does.
    ///
    /// <para>The two are separate failures and only the second was covered.
    /// <c>CastGatherOverrideTests</c> proves a pinned knob reaches the profile; these prove
    /// the panel then reports the value the spell is actually using. The first version failed
    /// exactly there: an unpinned row read the FAMILY profile and a pinned row dropped its
    /// value from the label altogether, so ticking a checkbox made the panel show strictly
    /// less than before and there was no way to see what the pin had done — reported as "the
    /// checkboxes don't show what is really being used".</para>
    ///
    /// <para>The labels are built by pure statics for this reason: asserting on them needs no
    /// canvas, no catalog and no Play Mode, so the wording is pinned by the same suite that
    /// pins the behaviour.</para>
    /// </summary>
    [TestFixture]
    public class GatherTabRowLabelTests
    {
        private static SpellDefinition NewSpell(SpellType type = SpellType.Projectile)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = "test_spell";
            spell.type = type;
            spell.range = 15f;
            return spell;
        }

        private static void Destroy(SpellDefinition s)
        {
            if (s != null) UnityEngine.Object.DestroyImmediate(s);
        }

        private static FieldInfo Knob(string name)
        {
            var knob = CastGatherOverrides.Knob(name);
            Assert.IsNotNull(knob, $"'{name}' is no longer a CastFlourishProfile knob.");
            return knob;
        }

        // ── The label states the value in USE, in both states ─────────────────────

        [Test]
        public void AnUnpinnedRowShowsTheFamilyValueAndSaysSo()
        {
            var spell = NewSpell();
            try
            {
                var resolved = CastFlourishProfile.Build(spell);
                string label = SpellsRuntimeEditor.PinRowLabel(Knob("Sigil"), resolved, pinned: false);

                Assert.That(label, Does.Contain("Sigil"));
                Assert.That(label, Does.Contain("Contract"),
                    "Hurl's sigil contracts; an unpinned row must report that value.");
                Assert.That(label, Does.Contain("(family)"),
                    "Without the tag, a freshly pinned knob and an unpinned one read identically " +
                    "— pinning seeds from the family, so the NUMBER cannot tell them apart.");
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void APinnedRowShowsThePinnedValueNotTheFamilysAndSaysSo()
        {
            var spell = NewSpell();
            try
            {
                Assert.AreEqual(SigilMotion.Contract, CastFlourishProfile.BuildFamily(spell).Sigil);

                spell.gatherOverride.SetText("Sigil", "None");
                var resolved = CastFlourishProfile.Build(spell);
                string label = SpellsRuntimeEditor.PinRowLabel(Knob("Sigil"), resolved, pinned: true);

                Assert.That(label, Does.Contain("None"),
                    "This is the whole complaint: a pinned row must report what the spell USES.");
                Assert.That(label, Does.Not.Contain("Contract"),
                    "The family's value is precisely the one the spell is no longer using.");
                Assert.That(label, Does.Contain("[pinned]"));
            }
            finally { Destroy(spell); }
        }

        /// <summary>
        /// The regression that produced the report: the label must never get SHORTER on
        /// pinning. Ticking a box is an act of taking ownership, not of hiding information.
        /// </summary>
        [Test]
        public void PinningNeverRemovesTheValueFromTheLabel()
        {
            var spell = NewSpell();
            var lost = new List<string>();
            try
            {
                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                {
                    if (!SpellsRuntimeEditor.CanEditKnob(knob.FieldType)) continue;

                    spell.gatherOverride.ClearAll();
                    var family = CastFlourishProfile.BuildFamily(spell);
                    object value = knob.GetValue(family);

                    // Pin it to exactly what it already was — the seeding the checkbox does.
                    if (knob.FieldType.IsEnum) spell.gatherOverride.SetText(knob.Name, value.ToString());
                    else if (knob.FieldType == typeof(bool))
                        spell.gatherOverride.SetNumber(knob.Name, (bool)value ? 1f : 0f);
                    else spell.gatherOverride.SetNumber(knob.Name, Convert.ToSingle(value));

                    var resolved = CastFlourishProfile.Build(spell);
                    string off = SpellsRuntimeEditor.PinRowLabel(knob, resolved, pinned: false);
                    string on  = SpellsRuntimeEditor.PinRowLabel(knob, resolved, pinned: true);

                    string rendered = SpellsRuntimeEditor.Describe(knob, resolved);
                    if (!off.Contains(rendered) || !on.Contains(rendered))
                        lost.Add($"{knob.Name}: off=\"{off}\" on=\"{on}\" (expected \"{rendered}\")");
                }
            }
            finally { Destroy(spell); }

            Assert.IsEmpty(lost,
                "These rows drop their value in one of the two states, which is what made the " +
                "checkbox look like it did nothing:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", lost));
        }

        [Test]
        public void EveryKnobProducesALabelCarryingItsOwnName()
        {
            var spell = NewSpell(SpellType.VortexField);   // the family that also builds a funnel
            try
            {
                var resolved = CastFlourishProfile.Build(spell);
                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                {
                    if (!SpellsRuntimeEditor.CanEditKnob(knob.FieldType)) continue;

                    string label = SpellsRuntimeEditor.PinRowLabel(knob, resolved, false);
                    Assert.That(label, Does.Contain(SpellsRuntimeEditor.Prettify(knob.Name)),
                        $"'{knob.Name}' must be identifiable in its own row.");
                    Assert.IsNotEmpty(SpellsRuntimeEditor.Describe(knob, resolved),
                        $"'{knob.Name}' renders as an empty string, so its row shows a bare name.");
                }
            }
            finally { Destroy(spell); }
        }

        // ── The value row is identifiable ─────────────────────────────────────────

        [Test]
        public void TheValueRowIsNamedAfterItsKnob()
        {
            foreach (var knob in CastGatherOverrides.AuthorableKnobs())
            {
                if (!SpellsRuntimeEditor.CanEditKnob(knob.FieldType)) continue;
                string label = SpellsRuntimeEditor.ValueRowLabel(knob);
                Assert.That(label, Does.Contain(SpellsRuntimeEditor.Prettify(knob.Name)),
                    "A column of rows all labelled \"value\" says nothing about which knob each " +
                    "one edits — twenty-nine of them sit in the same scroll view.");
            }
        }

        /// <summary>
        /// Every glyph a row prints must exist in the shipped TMP atlas. It carries Latin-1
        /// (the middle dot separator renders) and not much beyond it: a U+21B3 arrow used to
        /// mark the editable row drew as a missing-glyph box in front of every one of them.
        /// A label is only useful if it can be read.
        /// </summary>
        [Test]
        public void RowLabelsUseOnlyGlyphsTheFontActuallyHas()
        {
            var spell = NewSpell(SpellType.VortexField);
            var offenders = new List<string>();
            try
            {
                var resolved = CastFlourishProfile.Build(spell);
                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                {
                    if (!SpellsRuntimeEditor.CanEditKnob(knob.FieldType)) continue;
                    foreach (var label in new[]
                    {
                        SpellsRuntimeEditor.PinRowLabel(knob, resolved, false),
                        SpellsRuntimeEditor.PinRowLabel(knob, resolved, true),
                        SpellsRuntimeEditor.ValueRowLabel(knob),
                    })
                        foreach (char c in label)
                            // Latin-1 ends at U+00FF; anything past it is not in the atlas.
                            if (c > 'ÿ') offenders.Add($"{knob.Name}: U+{(int)c:X4} in \"{label}\"");
                }
            }
            finally { Destroy(spell); }

            Assert.IsEmpty(offenders,
                "These labels print glyphs outside Latin-1, which the atlas draws as empty " +
                "boxes:" + Environment.NewLine + "  " +
                string.Join(Environment.NewLine + "  ", offenders));
        }

        [Test]
        public void TheValueRowReadsAsSubordinateToItsCheckbox()
        {
            string label = SpellsRuntimeEditor.ValueRowLabel(Knob("Gather"));
            Assert.AreNotEqual(label.TrimStart(), label,
                "The editable row must be indented under the checkbox it belongs to, or the two " +
                "read as unrelated siblings.");
        }

        // ── Formatting ────────────────────────────────────────────────────────────

        [Test]
        public void ValuesRenderReadablyForEveryKnobType()
        {
            var spell = NewSpell();
            try
            {
                var resolved = CastFlourishProfile.Build(spell);

                Assert.AreEqual("0.2", SpellsRuntimeEditor.Describe(Knob("Gather"), resolved),
                    "A float must not render with trailing noise; 0.2 is Hurl's gather.");
                Assert.AreEqual("16", SpellsRuntimeEditor.Describe(Knob("MoteCount"), resolved),
                    "An int must render as an int.");
                Assert.AreEqual("SpiralIn", SpellsRuntimeEditor.Describe(Knob("Approach"), resolved),
                    "An enum must render by NAME — an index would be meaningless to a designer " +
                    "and would silently change meaning if the enum were reordered.");
                Assert.AreEqual("True", SpellsRuntimeEditor.Describe(Knob("HandAnchored"), resolved),
                    "A bool must render as a word.");
            }
            finally { Destroy(spell); }
        }

        /// <summary>
        /// The shipped state of the spell this tab was built for, asserted end to end: the
        /// asset pins the sigil off, so the row must say None and say it is pinned.
        /// </summary>
        [Test]
        public void FireballsShippedSigilPinIsReportedByItsRow()
        {
            var fireball = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                "Assets/_Project/Data/Catalogs/Spells/fireball.asset");
            Assert.IsNotNull(fireball, "fireball.asset is missing.");

            bool pinned = fireball.gatherOverride != null && fireball.gatherOverride.Has("Sigil");
            var resolved = CastFlourishProfile.Build(fireball);
            string label = SpellsRuntimeEditor.PinRowLabel(Knob("Sigil"), resolved, pinned);

            Assert.That(label, Does.Contain(resolved.Sigil.ToString()),
                "Whatever the shipped asset resolves to, the row must state it.");
            Assert.That(label, Does.Contain(pinned ? "[pinned]" : "(family)"),
                "The tag must agree with whether the asset actually pins the knob.");
        }
    }
}
