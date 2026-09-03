using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The per-knob cast-gather override: a spell pins the pieces of its flourish it wants to
    /// own and keeps tracking its family for everything else.
    ///
    /// <para>The bridge between the authored bag (Valkur.Data) and the profile struct
    /// (Valkur.Gameplay) is reflection over the struct's own field names, which is what stops
    /// a third parallel list existing — so what these tests pin is the CONTRACT that makes
    /// that safe: every knob round-trips, a knob that vanished is survivable, and the one
    /// field that must never be authorable stays out of reach.</para>
    /// </summary>
    [TestFixture]
    public class CastGatherOverrideTests
    {
        private const string FireballPath = "Assets/_Project/Data/Catalogs/Spells/fireball.asset";

        /// <summary>Line break for multi-line assertion messages.</summary>
        private static readonly string NL = Environment.NewLine + "  ";

        private static SpellDefinition NewSpell(SpellType type, float range = 15f)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = "test_spell";
            spell.type = type;
            spell.range = range;
            return spell;
        }

        private static void Destroy(SpellDefinition spell)
        {
            if (spell != null) UnityEngine.Object.DestroyImmediate(spell);
        }

        // ── The default state is "pure family" ────────────────────────────────────

        [Test]
        public void AnUnpinnedSpellResolvesExactlyToItsFamily()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                var family = CastFlourishProfile.BuildFamily(spell);
                var resolved = CastFlourishProfile.Build(spell);

                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                    Assert.AreEqual(knob.GetValue(family), knob.GetValue(resolved),
                        $"'{knob.Name}' drifted with nothing pinned. An empty bag must be a " +
                        "no-op, or every spell in the game silently stops matching its family.");
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void ANullBagIsSurvivable()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                // What a spell asset serialized before the field existed deserializes as.
                spell.gatherOverride = null;
                Assert.AreEqual(CastFlourishProfile.BuildFamily(spell).Gather,
                                CastFlourishProfile.Build(spell).Gather);
            }
            finally { Destroy(spell); }
        }

        // ── Pinning is per knob, and that is the whole point ──────────────────────

        [Test]
        public void PinningOneKnobLeavesEveryOtherKnobOnTheFamily()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                var family = CastFlourishProfile.BuildFamily(spell);
                spell.gatherOverride.SetNumber("Gather", 0.06f);

                var resolved = CastFlourishProfile.Build(spell);
                Assert.AreEqual(0.06f, resolved.Gather, 1e-5f, "The pinned knob must take.");

                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                {
                    if (knob.Name == "Gather") continue;
                    Assert.AreEqual(knob.GetValue(family), knob.GetValue(resolved),
                        $"Pinning 'Gather' also moved '{knob.Name}'. Per-knob pinning exists so " +
                        "retuning a family still reaches every value a spell has not overruled; " +
                        "a pin that freezes the whole struct is the wholesale switch it replaced.");
                }
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void ReleasingAKnobPutsItBackOnTheFamily()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                float familyGather = CastFlourishProfile.BuildFamily(spell).Gather;

                spell.gatherOverride.SetNumber("Gather", 0.06f);
                Assert.AreEqual(0.06f, CastFlourishProfile.Build(spell).Gather, 1e-5f);

                Assert.IsTrue(spell.gatherOverride.Clear("Gather"));
                Assert.AreEqual(familyGather, CastFlourishProfile.Build(spell).Gather, 1e-5f,
                    "Presence in the bag IS the switch, so a released knob must leave no value " +
                    "behind that could come back later.");
            }
            finally { Destroy(spell); }
        }

        // ── Every knob type survives a round trip ─────────────────────────────────

        /// <summary>
        /// The guarantee the reflection bridge rests on: whatever a family declares can be
        /// authored and read back. Without this a knob added to the struct could be offered by
        /// the Gather tab and silently dropped on the way to the profile.
        /// </summary>
        [Test]
        public void EveryAuthorableKnobRoundTrips()
        {
            var spell = NewSpell(SpellType.VortexField);   // the only family that builds a funnel
            try
            {
                var untouched = new List<string>();

                foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                {
                    var family = CastFlourishProfile.BuildFamily(spell);
                    object seed = knob.GetValue(family);
                    object want = DistinctFrom(knob.FieldType, seed);
                    if (want == null) { untouched.Add(knob.Name + " (untestable type)"); continue; }

                    spell.gatherOverride.ClearAll();
                    if (knob.FieldType.IsEnum) spell.gatherOverride.SetText(knob.Name, want.ToString());
                    else                       spell.gatherOverride.SetNumber(knob.Name, ToNumber(want));

                    object got = knob.GetValue(CastFlourishProfile.Build(spell));
                    if (!Equals(got, want)) untouched.Add($"{knob.Name}: wanted {want}, got {got}");
                }

                Assert.IsEmpty(untouched,
                    "These knobs cannot be authored through the bag, so the Gather tab would " +
                    "offer a row that does nothing:\n  " + string.Join("\n  ", untouched));
            }
            finally { Destroy(spell); }
        }

        /// <summary>A value of the knob's type that is guaranteed different from <paramref name="seed"/>.</summary>
        private static object DistinctFrom(Type type, object seed)
        {
            if (type == typeof(float)) return (float)seed + 1.25f;
            if (type == typeof(int)) return (int)seed + 3;
            if (type == typeof(bool)) return !(bool)seed;
            if (type.IsEnum)
            {
                foreach (var value in Enum.GetValues(type))
                    if (!Equals(value, seed)) return value;
            }
            return null;
        }

        private static float ToNumber(object value)
        {
            if (value is bool b) return b ? 1f : 0f;
            if (value is int i) return i;
            return Convert.ToSingle(value);
        }

        [Test]
        public void ABoolKnobRidesTheNumericPayload()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                Assert.IsTrue(CastFlourishProfile.BuildFamily(spell).HandAnchored,
                    "Hurl is hand-anchored; this test is written against that.");

                spell.gatherOverride.SetNumber("HandAnchored", 0f);
                Assert.IsFalse(CastFlourishProfile.Build(spell).HandAnchored);

                spell.gatherOverride.SetNumber("HandAnchored", 1f);
                Assert.IsTrue(CastFlourishProfile.Build(spell).HandAnchored);
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void AnEnumKnobIsStoredByNameNotByIndex()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                spell.gatherOverride.SetText("Departure", "TrailBehind");

                var entry = spell.gatherOverride.Find("Departure");
                Assert.AreEqual("TrailBehind", entry.text,
                    "An index would re-point at a different member the day the enum is " +
                    "reordered, silently changing what every spell that pinned it does.");
                Assert.AreEqual(MoteDeparture.TrailBehind, CastFlourishProfile.Build(spell).Departure);
            }
            finally { Destroy(spell); }
        }

        // ── Bad data must be survivable, never fatal ──────────────────────────────

        [Test]
        public void AKnobThatNoLongerExistsIsIgnoredRatherThanFatal()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                var family = CastFlourishProfile.BuildFamily(spell);
                spell.gatherOverride.SetNumber("KnobFromAnOlderBuild", 9f);
                spell.gatherOverride.SetNumber("Gather", 0.06f);

                LogAssert.ignoreFailingMessages = true;
                var resolved = CastFlourishProfile.Build(spell);
                LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(0.06f, resolved.Gather, 1e-5f,
                    "A stale entry must not cost the entries beside it.");
                Assert.AreEqual(family.MoteCount, resolved.MoteCount);
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void AnUnparseableEnumMemberFallsBackToTheFamily()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                var family = CastFlourishProfile.BuildFamily(spell);
                spell.gatherOverride.SetText("Approach", "NotAMember");

                LogAssert.ignoreFailingMessages = true;
                var resolved = CastFlourishProfile.Build(spell);
                LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(family.Approach, resolved.Approach,
                    "The flourish still has to play: a spell whose gesture cannot be parsed " +
                    "falls back to the one its type dictates rather than drawing nothing.");
            }
            finally { Destroy(spell); }
        }

        // ── The one field that must stay out of reach ─────────────────────────────

        [Test]
        public void FamilyNameIsNotAuthorable()
        {
            foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                Assert.AreNotEqual(CastGatherOverrides.FAMILY_NAME_FIELD, knob.Name,
                    "FamilyName is the answer to 'which gesture is this', written by the family " +
                    "that built the profile. A spell that could rewrite it would make the label " +
                    "disagree with every other value in the struct.");

            Assert.IsNull(CastGatherOverrides.Knob(CastGatherOverrides.FAMILY_NAME_FIELD));

            var spell = NewSpell(SpellType.Projectile);
            try
            {
                spell.gatherOverride.SetText(CastGatherOverrides.FAMILY_NAME_FIELD, "Vortex");
                LogAssert.ignoreFailingMessages = true;
                Assert.AreEqual("Hurl", CastFlourishProfile.Build(spell).FamilyName);
                LogAssert.ignoreFailingMessages = false;
            }
            finally { Destroy(spell); }
        }

        // ── The shipped catalog ───────────────────────────────────────────────────

        /// <summary>
        /// Every knob any shipped spell pins must still exist. A rename in the profile struct
        /// orphans authored data silently — the flourish keeps playing, on the family's value,
        /// and the designer's edit is simply gone.
        /// </summary>
        [Test]
        public void NoShippedSpellPinsAKnobThatDoesNotExist()
        {
            var orphans = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition"))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell?.gatherOverride?.fields == null) continue;

                foreach (var entry in spell.gatherOverride.fields)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.name)) continue;
                    var knob = CastGatherOverrides.Knob(entry.name);
                    if (knob == null)
                    {
                        orphans.Add($"{spell.spellKey}: '{entry.name}'");
                        continue;
                    }
                    if (knob.FieldType.IsEnum
                        && !Array.Exists(Enum.GetNames(knob.FieldType),
                                         n => string.Equals(n, entry.text, StringComparison.OrdinalIgnoreCase)))
                        orphans.Add($"{spell.spellKey}: '{entry.name}' = '{entry.text}' is not a " +
                                    $"{knob.FieldType.Name} member");
                }
            }

            Assert.IsEmpty(orphans,
                "Authored gather overrides that no longer resolve:\n  " + string.Join("\n  ", orphans));
        }

        // ── The editor form's side of the contract ───────────────────────────────

        /// <summary>
        /// Every knob the bag can carry must also be one the Gather tab can render an input
        /// for. Without this a knob of a new type would show a checkbox with nothing under
        /// it: pinned, applied, and uneditable.
        /// </summary>
        [Test]
        public void EveryAuthorableKnobHasAnEditorRowType()
        {
            var unrenderable = new List<string>();
            foreach (var knob in CastGatherOverrides.AuthorableKnobs())
                if (!SpellsRuntimeEditor.CanEditKnob(knob.FieldType))
                    unrenderable.Add($"{knob.Name} : {knob.FieldType.Name}");

            Assert.IsEmpty(unrenderable,
                "The Gather tab builds float / int / bool / enum rows only, so these knobs " +
                "would be offered by the bag and never editable:" + NL +
                string.Join(NL, unrenderable));
        }

        /// <summary>
        /// The funnel knobs are hidden for a family that draws no funnel — but a PINNED one
        /// must survive that filter, or changing a spell's type strands an override that
        /// still applies with no row to release it from.
        /// </summary>
        [Test]
        public void APinnedFunnelKnobStillAppliesAfterTheFamilyStopsUsingIt()
        {
            var spell = NewSpell(SpellType.VortexField);
            try
            {
                Assert.Greater(CastFlourishProfile.BuildFamily(spell).FunnelBands, 0,
                    "Vortex is the family that draws a funnel; this test is written against that.");

                spell.gatherOverride.SetNumber("FunnelHeight", 7.5f);
                Assert.AreEqual(7.5f, CastFlourishProfile.Build(spell).FunnelHeight, 1e-4f);

                // The same spell retyped: the family no longer builds a funnel at all.
                spell.type = SpellType.Projectile;
                Assert.AreEqual(0, CastFlourishProfile.BuildFamily(spell).FunnelBands);
                Assert.AreEqual(7.5f, CastFlourishProfile.Build(spell).FunnelHeight, 1e-4f,
                    "The pin still applies, so the tab must still show it — an override that " +
                    "acts with no row to release it is unreachable authored state.");
            }
            finally { Destroy(spell); }
        }

        /// <summary>
        /// Fireball is what the tab was built for, so its shipped state is worth stating: it
        /// resolves to Hurl, and whatever it pins is a deliberate edit rather than a leftover.
        /// </summary>
        [Test]
        public void FireballResolvesToHurl()
        {
            var fireball = AssetDatabase.LoadAssetAtPath<SpellDefinition>(FireballPath);
            Assert.IsNotNull(fireball, "fireball.asset is missing.");
            Assert.AreEqual("Hurl", CastFlourishProfile.BuildFamily(fireball).FamilyName);
            Assert.AreEqual("Hurl", CastFlourishProfile.Build(fireball).FamilyName,
                "An override must never be able to change which family a spell reports.");
        }
    }
}
