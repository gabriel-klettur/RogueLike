using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// Pins the seven energy-charge auras: that they exist, that they are seven DIFFERENT
    /// things, and that the ladder between them is monotonic.
    ///
    /// <para>These are visual-only spells, which is exactly why they need a test. Nothing in
    /// the game reads them yet, so a broken one produces no error and no failing behaviour —
    /// it just quietly looks like the tier below it. The ladder is the content here, and a
    /// ladder with two equal rungs is the failure this guards against.</para>
    /// </summary>
    public class EnergyChargeTests
    {
        /// <summary>Weakest first. The order is the content — see <c>DevConsole</c>'s copy.</summary>
        private static readonly string[] Keys =
        {
            "charge_ki_spirit", "charge_ki_azure", "charge_ki_verdant", "charge_ki_solar",
            "charge_ki_crimson", "charge_ki_violet", "charge_ki_void",
        };

        private const string Folder = "Assets/_Project/Data/Catalogs/Spells/";

        private static SpellDefinition Load(string key)
            => AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + key + ".asset");

        [Test]
        public void AllSevenShip_AsEnergyCharges()
        {
            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.IsNotNull(spell, key + " is missing from " + Folder);
                Assert.AreEqual(SpellType.EnergyCharge, spell.type, key);
                Assert.AreEqual(key, spell.spellKey, key + " carries the wrong spellKey");
                Assert.IsFalse(string.IsNullOrEmpty(spell.displayName), key + " has no display name");
                // Visual only, for now. The moment one of these deals damage it is a spell and
                // needs balancing, which is a different conversation from how it looks.
                Assert.AreEqual(0f, spell.damage, key + " deals damage — it is meant to be visual only");
                Assert.AreEqual(1, spell.maxInstances,
                    key + ": a second aura on the same body would double every layer");
            }
        }

        [Test]
        public void TheLadderIsMonotonic()
        {
            float previousIntensity = -1f;
            float previousDuration = -1f;

            foreach (var key in Keys)
            {
                var spell = Load(key);
                Assert.Greater(spell.scale, previousIntensity,
                    key + ": intensity must strictly increase — two equal rungs is one rung.");
                Assert.Greater(spell.duration, previousDuration, key + ": duration must increase too");
                previousIntensity = spell.scale;
                previousDuration = spell.duration;
            }

            Assert.LessOrEqual(previousIntensity, 1f, "intensity is a 0..1 dial");
        }

        [Test]
        public void EverySwatchIsDistinct()
        {
            var seen = new List<Color>();
            foreach (var key in Keys)
            {
                Color c = Load(key).particleColor;
                foreach (var other in seen)
                {
                    float distance = Mathf.Abs(c.r - other.r) + Mathf.Abs(c.g - other.g) + Mathf.Abs(c.b - other.b);
                    Assert.Greater(distance, 0.35f,
                        key + " is too close to another charge to tell apart in play.");
                }
                seen.Add(c);
            }
        }

        [Test]
        public void OnlyTheFiercestChargesCrackle()
        {
            // The arcs are the loudest element in the effect. Putting them on every tier would
            // erase the difference between the bottom of the ladder and the top, which is the
            // whole reason there is a ladder.
            var withLightning = new List<string>();
            foreach (var key in Keys)
                if (KiPalette.From(Load(key).particleColor, Load(key).scale).HasLightning)
                    withLightning.Add(key);

            Assert.Greater(withLightning.Count, 0, "nothing crackles — the top of the ladder is flat");
            Assert.Less(withLightning.Count, Keys.Length, "everything crackles — the ladder is flat");
            Assert.Contains("charge_ki_void", withLightning, "the fiercest charge must crackle");
            Assert.IsFalse(withLightning.Contains("charge_ki_spirit"), "the calmest charge must not");
        }

        [Test]
        public void ThePaletteIsOrderedFromCoreToEdge()
        {
            foreach (var key in Keys)
            {
                var spell = Load(key);
                var palette = KiPalette.From(spell.particleColor, spell.scale);

                Assert.Greater(Luminance(palette.Core), Luminance(palette.Mid),
                    key + ": the spine of a flame is always brighter than its flanks.");
                Assert.Greater(Luminance(palette.Mid), Luminance(palette.Edge),
                    key + ": the outer tongues are the deepest part of the aura.");
            }
        }

        [Test]
        public void AnUnauthoredSwatchStillProducesAnAura()
        {
            // White with full alpha is what particleColor holds when nobody has touched it.
            var palette = KiPalette.From(Color.white, 0.5f);
            Assert.Greater(Luminance(palette.Mid), 0.1f, "an untouched colour must not render black");
            Assert.Less(palette.Mid.r, 0.99f, "the fallback must not be pure white — nothing would read as colour");
        }

        [Test]
        public void TheChargeHasItsOwnExecutor()
        {
            var executor = SpellCaster.GetExecutor(SpellType.EnergyCharge);
            Assert.IsNotNull(executor,
                "With no executor the caster silently falls back to Projectile and fires a bolt.");
            Assert.IsInstanceOf<EnergyChargeExecutor>(executor);
        }

        [Test]
        public void TheAuthoringSurfaceIsReachableInTheEditor()
        {
            var spell = Load(Keys[0]);
            // These two ARE the spell: one swatch the whole palette is derived from, and the
            // intensity dial. Hidden, a designer cannot reach either.
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "particleColor"));
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "scale"));
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "duration"));
            Assert.IsTrue(SpellFieldRelevance.Applies(spell, "radius"));
        }

        [Test]
        public void TheCastFlourishStandsAside()
        {
            // The aura opens with an ignition flare of its own that runs for twice as long as
            // a flourish. Two systems lighting the same silhouette in the same half-second
            // read as one of them being broken.
            Assert.IsFalse(SpellCastFlourishFX.AppliesTo(Load("charge_ki_void")));
        }

        private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
    }
}
