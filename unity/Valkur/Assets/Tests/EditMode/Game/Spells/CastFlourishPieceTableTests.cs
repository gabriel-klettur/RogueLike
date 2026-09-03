using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Keeps <see cref="CastFlourishPieces"/> honest against the struct it describes.
    ///
    /// <para>Deliberately in the shape CLAUDE.md credits for <c>FSMBuiltInTransitionRegistryTests</c>:
    /// it fails when the table and the code disagree <b>in either direction</b>, which is what
    /// stops a declaration table quietly becoming another <c>animation_map.json</c> — a file
    /// everything writes and nothing reads.</para>
    ///
    /// <para>The table is read by three separate consumers — the rig's build guards, the F4
    /// Gather tab's headers and switches, and the override bag's authoring — so a row that
    /// drifts from the struct is wrong in three places at once and visible in none.</para>
    /// </summary>
    [TestFixture]
    public class CastFlourishPieceTableTests
    {
        private static SpellDefinition NewSpell(SpellType type)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = "table_probe";
            spell.type = type;
            spell.range = 15f;
            spell.radius = 2f;
            return spell;
        }

        private static void Destroy(SpellDefinition s)
        {
            if (s != null) UnityEngine.Object.DestroyImmediate(s);
        }

        private static IEnumerable<SpellType> AllTypes()
            => Enum.GetValues(typeof(SpellType)).Cast<SpellType>();

        // ── Coverage, both directions ─────────────────────────────────────────────

        [Test]
        public void EveryAuthorableKnobBelongsToExactlyOneSection()
        {
            var claimed = new Dictionary<string, string>();
            var twice = new List<string>();

            foreach (var piece in CastFlourishPieces.All)
                foreach (var knob in piece.Knobs)
                {
                    if (claimed.TryGetValue(knob, out var already))
                        twice.Add($"{knob} claimed by both '{already}' and '{piece.Section}'");
                    else claimed[knob] = piece.Section;
                }

            Assert.IsEmpty(twice,
                "A knob under two headers renders twice and its pin state is edited from two " +
                "rows:\n  " + string.Join("\n  ", twice));

            var orphans = CastGatherOverrides.AuthorableKnobs()
                .Select(k => k.Name).Where(n => !claimed.ContainsKey(n)).ToList();
            Assert.IsEmpty(orphans,
                "These knobs belong to no section, so the Gather tab renders no row for them at " +
                "all — they became unauthorable the moment the panel started iterating the " +
                "table instead of the knob list:\n  " + string.Join("\n  ", orphans));
        }

        [Test]
        public void NoSectionClaimsAKnobThatNoLongerExists()
        {
            var ghosts = (from piece in CastFlourishPieces.All
                          from knob in piece.Knobs
                          where CastGatherOverrides.Knob(knob) == null
                          select $"{piece.Section}: '{knob}'").ToList();

            Assert.IsEmpty(ghosts,
                "A renamed profile field leaves the table pointing at nothing. That is a red " +
                "test rather than a single runtime warning, because the row it would have " +
                "rendered simply disappears:\n  " + string.Join("\n  ", ghosts));
        }

        // ── The gate ──────────────────────────────────────────────────────────────

        [Test]
        public void EveryGateKnobResolvesAndIsOwnedByItsOwnSection()
        {
            foreach (var piece in CastFlourishPieces.All)
            {
                if (piece.IsLocked) continue;
                Assert.IsNotNull(CastGatherOverrides.Knob(piece.GateKnob),
                    $"'{piece.Section}' gates on '{piece.GateKnob}', which is not a profile knob.");
                Assert.Contains(piece.GateKnob, piece.Knobs,
                    $"'{piece.Section}' gates on a knob it does not own, so its switch would " +
                    "write a value no row under it can show.");
            }
        }

        /// <summary>
        /// The one real hazard of a weakly-typed <c>OffValue</c>: a boxed value of the wrong
        /// type compares unequal under <c>Equals</c> forever, so the switch silently reads
        /// "always on". No compile error, no runtime exception — the only symptom is that OFF
        /// does nothing, which is precisely the bug this feature was built to fix.
        /// </summary>
        [Test]
        public void EveryOffValueIsOfItsGateKnobsExactType()
        {
            var mismatched = new List<string>();
            foreach (var piece in CastFlourishPieces.All)
            {
                if (piece.IsLocked) continue;
                var knob = CastGatherOverrides.Knob(piece.GateKnob);
                if (knob == null) continue;
                if (piece.OffValue == null || piece.OffValue.GetType() != knob.FieldType)
                    mismatched.Add($"{piece.Section}: gate '{piece.GateKnob}' is {knob.FieldType.Name} " +
                                   $"but OffValue is {piece.OffValue?.GetType().Name ?? "null"}");
            }
            Assert.IsEmpty(mismatched,
                "These switches can never turn their piece off:\n  " + string.Join("\n  ", mismatched));
        }

        // ── Locked sections ───────────────────────────────────────────────────────

        [Test]
        public void ExactlyTimingAndAnchorAreLocked()
        {
            var locked = CastFlourishPieces.All.Where(p => p.IsLocked).Select(p => p.Section).ToList();
            CollectionAssert.AreEquivalent(new[] { "Timing", "Anchor" }, locked,
                "Timing is the CLOCK — Duration = 0 builds the whole rig and destroys it on " +
                "frame one, so a switch there would suppress nothing. Anchor is a CHOICE — " +
                "HandAnchored picks the point the lance and every mote ride, so switching it " +
                "'off' relocates the gather rather than removing it. Any other locked section " +
                "is a piece someone gave up on rather than one that cannot have a switch.");

            foreach (var piece in CastFlourishPieces.All.Where(p => p.IsLocked))
                Assert.IsTrue(CastFlourishPieces.IsOn(default, piece),
                    $"'{piece.Section}' is locked, so it must read ON for every profile.");
        }

        // ── The switch actually switches ──────────────────────────────────────────

        [Test]
        public void EverySwitchablePieceCanBeTurnedOffThroughTheBag()
        {
            var stuck = new List<string>();
            var spell = NewSpell(SpellType.VortexField);   // the one family that draws every piece
            try
            {
                foreach (var piece in CastFlourishPieces.All)
                {
                    if (piece.IsLocked) continue;

                    spell.gatherOverride.ClearAll();
                    CastFlourishPieces.WriteOff(spell.gatherOverride, piece);

                    if (CastFlourishPieces.IsOn(CastFlourishProfile.Build(spell), piece))
                        stuck.Add($"{piece.Section} (gate '{piece.GateKnob}')");
                }
            }
            finally { Destroy(spell); }

            Assert.IsEmpty(stuck,
                "The UI offers a switch for these and the authored value does not turn them " +
                "off — a control that cannot do what it says:\n  " + string.Join("\n  ", stuck));
        }

        /// <summary>
        /// A piece no family ever draws is dead weight: its switch could only ever be turned
        /// off. Guards against a row surviving a piece's removal from the rig.
        /// </summary>
        [Test]
        public void EverySwitchablePieceIsDrawnByAtLeastOneFamily()
        {
            var never = new List<string>();
            foreach (var piece in CastFlourishPieces.All)
            {
                if (piece.IsLocked) continue;
                bool drawnSomewhere = false;

                foreach (var type in AllTypes())
                {
                    var spell = NewSpell(type);
                    try
                    {
                        if (CastFlourishPieces.IsOn(CastFlourishProfile.BuildFamily(spell), piece))
                        { drawnSomewhere = true; break; }
                    }
                    finally { Destroy(spell); }
                }
                if (!drawnSomewhere) never.Add(piece.Section);
            }

            Assert.IsEmpty(never,
                "No family draws these, so their switch has only one reachable state:\n  " +
                string.Join("\n  ", never));
        }

        /// <summary>
        /// Turning a piece ON must produce something VISIBLE. Releasing the gate alone is not
        /// enough where the family zeroed the piece's companions — Edge ships
        /// <c>Sigil = None</c> beside radius, spin and alpha all at zero, so a gate-only ON
        /// would build two rings at radius 0.05 and alpha 0.
        /// </summary>
        [Test]
        public void SeedingAFamilyOffPieceProducesADrawableOne()
        {
            var spell = NewSpell(SpellType.Slash);   // Edge: draws no sigil, and zeroes its knobs
            try
            {
                var family = CastFlourishProfile.BuildFamily(spell);
                Assert.IsFalse(CastFlourishPieces.IsOn(family, CastFlourishPieces.Sigil),
                    "Edge draws no sigil; this test is written against that.");
                Assert.AreEqual(0f, family.SigilAlpha, "Edge also zeroes the companions.");

                CastFlourishPieces.SeedFrom(spell.gatherOverride, CastFlourishPieces.Sigil,
                    CastFlourishPieces.Sigil.Donor(spell));

                var resolved = CastFlourishProfile.Build(spell);
                Assert.IsTrue(CastFlourishPieces.IsOn(resolved, CastFlourishPieces.Sigil));
                Assert.Greater(resolved.SigilAlpha, 0f,
                    "A switch that reads ON and draws nothing is the bug this replaced.");
                Assert.Greater(resolved.SigilRadius, 0.05f);
            }
            finally { Destroy(spell); }
        }

        [Test]
        public void EveryDonorDrawsThePieceItIsTheDonorFor()
        {
            var spell = NewSpell(SpellType.Projectile);
            try
            {
                foreach (var piece in CastFlourishPieces.All)
                {
                    if (piece.IsLocked) continue;
                    Assert.IsNotNull(piece.Donor, $"'{piece.Section}' has no donor family.");
                    Assert.IsTrue(CastFlourishPieces.IsOn(piece.Donor(spell), piece),
                        $"'{piece.Section}' donates from a family that does not draw it, so " +
                        "seeding would turn the switch on and leave the piece invisible.");
                }
            }
            finally { Destroy(spell); }
        }

        // ── Section lookup ────────────────────────────────────────────────────────

        [Test]
        public void EveryKnobResolvesToTheSectionThatClaimsIt()
        {
            foreach (var piece in CastFlourishPieces.All)
                foreach (var knob in piece.Knobs)
                {
                    Assert.IsTrue(CastFlourishPieces.TryGetSection(knob, out var found),
                        $"'{knob}' is claimed by '{piece.Section}' but resolves to no section.");
                    Assert.AreEqual(piece.Section, found.Section);
                }

            Assert.IsFalse(CastFlourishPieces.TryGetSection("NotAKnob", out _));
            Assert.IsFalse(CastFlourishPieces.TryGetSection(null, out _));
        }

        // ── The shipped catalog ───────────────────────────────────────────────────

        /// <summary>
        /// Five knobs changed meaning in this pass: <c>AuraDrive</c>, <c>HandScale</c>,
        /// <c>BodyDrive</c>, <c>MoteCount</c> and <c>LightMul</c> at zero went from "built but
        /// weak" to "not built at all". This lists every shipped spell that turns a piece off,
        /// so a deliberate OFF is a recorded fact rather than something noticed on screen six
        /// weeks later — and so the day one of those zeroes appears by accident, it is a diff.
        /// </summary>
        [Test]
        public void ShippedSpellsThatSwitchAPieceOffAreAccountedFor()
        {
            var expected = new HashSet<string> { "fireball: Sigil" };
            var actual = new HashSet<string>();

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:SpellDefinition"))
            {
                var spell = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (spell?.gatherOverride == null || spell.gatherOverride.Count == 0) continue;
                if (!SpellCastFlourishFX.AppliesTo(spell)) continue;

                var resolved = CastFlourishProfile.Build(spell);
                foreach (var piece in CastFlourishPieces.All)
                {
                    if (piece.IsLocked) continue;
                    if (!spell.gatherOverride.Has(piece.GateKnob)) continue;
                    if (!CastFlourishPieces.IsOn(resolved, piece))
                        actual.Add($"{spell.spellKey}: {piece.Section}");
                }
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "The set of shipped spells that switch a flourish piece OFF changed. If that " +
                "was deliberate, update `expected` in this test — that edit is the record.");
        }
    }
}
