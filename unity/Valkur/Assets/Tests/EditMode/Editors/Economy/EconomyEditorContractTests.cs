using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Economy;
using Valkur.Data;
using Valkur.Gameplay.Editors.General;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Editors.Economy
{
    /// <summary>
    /// The Economy editor's contract with the rest of the project, and the tuning layer it
    /// authors.
    ///
    /// <para><c>EditorReachabilityTests</c> already walks every shipped <c>EditorName</c>
    /// against the General Editor's list, so this fixture does NOT re-derive that — it asserts
    /// the things specific to this editor that would otherwise fail silently: that its entry is
    /// really there, and that the tuning it writes cannot change behaviour by being absent.</para>
    /// </summary>
    public class EconomyEditorContractTests
    {
        /// <summary>
        /// With the F-row retired the launcher is the ONLY way into any editor, so a missing
        /// entry is an editor nobody can open and nothing throws to say so. Named explicitly
        /// here as well as covered generically, because this one was added last and a new
        /// editor is exactly when the generic test's stem-matching could be argued with.
        /// </summary>
        [Test]
        public void TheEconomyEditor_IsInTheGeneralEditorLauncher()
        {
            var labels = GeneralEditorRegistry.BuildEntries()
                .Where(e => e.Section == GeneralEditorSection.Editors)
                .Select(e => e.Label)
                .ToList();

            CollectionAssert.Contains(labels, "Economy",
                "the Economy editor has no launcher entry, so nothing can open it");
        }

        /// <summary>
        /// THE property that makes an optional tuning asset safe: absent, it must behave
        /// exactly as the constants it replaced. A tuning layer that changes behaviour by not
        /// being there is worse than no tuning layer, because the difference only shows up in
        /// a project that has not run the seeder — which is every fresh clone.
        /// </summary>
        [Test]
        public void DefaultTuning_ReproducesTheShippedConstants()
        {
            var fresh = ScriptableObject.CreateInstance<EconomyTuning>();
            try
            {
                Assert.AreEqual(MarketCycle.Amplitude, fresh.amplitude, 1e-6f);
                Assert.AreEqual(MarketCycle.MinCycleDays, fresh.minCycleDays);
                Assert.AreEqual(MarketCycle.MaxCycleDays, fresh.maxCycleDays);

                fresh.ResolveCycleDays(out int min, out int max);
                var shape = new MarketShape(fresh.amplitude, min, max);

                // The composition, not either half: a shape built from the defaults must
                // resolve every day identically to the parameterless overload.
                for (int day = 0; day < 120; day++)
                {
                    var viaShape = MarketCycle.Resolve(4242, day, shape);
                    var viaDefaults = MarketCycle.Resolve(4242, day);
                    Assert.AreEqual(viaDefaults.Phase, viaShape.Phase, $"phase differs on day {day}");
                    Assert.AreEqual(viaDefaults.BuyMultiplier, viaShape.BuyMultiplier, 1e-6f,
                        $"buy multiplier differs on day {day}");
                }
            }
            finally { Object.DestroyImmediate(fresh); }
        }

        /// <summary>
        /// An inverted pair is SWAPPED, never refused. These are authored one field at a time
        /// in a live editor, so "minimum currently above maximum" is a state every author
        /// passes through on the way to a valid pair — refusing there makes the second field
        /// unreachable, and a pure struct has nowhere to report a refusal from anyway.
        /// </summary>
        [Test]
        public void AnInvertedPair_IsSwappedRatherThanRefused()
        {
            var tuning = ScriptableObject.CreateInstance<EconomyTuning>();
            try
            {
                tuning.minCycleDays = 20;
                tuning.maxCycleDays = 8;
                tuning.ResolveCycleDays(out int min, out int max);
                Assert.AreEqual(8, min);
                Assert.AreEqual(20, max);

                tuning.coinVarianceMin = 1.4f;
                tuning.coinVarianceMax = 0.7f;
                tuning.ResolveCoinVariance(out double lo, out double hi);
                Assert.That(lo, Is.LessThanOrEqualTo(hi));

                // And the swapped pair must still produce a legal shape rather than an
                // exception two frames later inside a price resolve.
                var shape = new MarketShape(tuning.amplitude, min, max);
                Assert.That(shape.MinCycleDays, Is.LessThanOrEqualTo(shape.MaxCycleDays));
            }
            finally { Object.DestroyImmediate(tuning); }
        }

        /// <summary>
        /// The amplitude ceiling is a RULE, not a slider hint. An inspector Range is a
        /// suggestion — the asset can be hand-edited and a value can arrive from a migrated or
        /// corrupted file — so the clamp has to live where the value is consumed. Beyond the
        /// ceiling the cycle stops being a market and becomes a lockout.
        /// </summary>
        [Test]
        public void AmplitudeIsClampedAtTheCeiling_HoweverItWasAuthored()
        {
            var wild = new MarketShape(999f, 6, 14);
            Assert.AreEqual(MarketCycle.AmplitudeCeiling, wild.Amplitude, 1e-6f);

            var negative = new MarketShape(-5f, 6, 14);
            Assert.AreEqual(0f, negative.Amplitude, 1e-6f);

            for (int day = 0; day < 60; day++)
            {
                var c = MarketCycle.Resolve(7, day, wild);
                Assert.That(c.BuyMultiplier,
                    Is.InRange(1f - MarketCycle.AmplitudeCeiling, 1f + MarketCycle.AmplitudeCeiling));
            }
        }

        /// <summary>
        /// A shape with zero amplitude must leave prices exactly alone — that is what the
        /// editor's "cycle off" and a fully damped tuning both rely on, and an off switch that
        /// still moves prices by a rounding error is an off switch nobody can trust.
        /// </summary>
        [Test]
        public void ZeroAmplitude_LeavesEveryMultiplierAtOne()
        {
            var flat = new MarketShape(0f, 6, 14);
            for (int day = 0; day < 60; day++)
            {
                var c = MarketCycle.Resolve(31337, day, flat);
                Assert.AreEqual(1f, c.BuyMultiplier, 1e-6f, $"day {day}");
                Assert.AreEqual(1f, c.SellMultiplier, 1e-6f, $"day {day}");
            }
        }

        /// <summary>
        /// The Bitcoin feed ships OFF. This is a consent property, not a preference: a game
        /// that reaches the network without being asked is a game phoning home, and the default
        /// is the only part of that nobody has to notice to be protected by.
        /// </summary>
        [Test]
        public void TheBitcoinFeed_IsOffUnlessTheAuthorTurnsItOn()
        {
            // PlayerPrefs is MACHINE state and survives the run, the Editor and the reboot, so
            // the key is cleared on both sides — otherwise enabling the feed by hand once would
            // leave this assertion failing forever, on that machine only, for a reason nothing
            // in the test name mentions.
            bool had = PlayerPrefs.HasKey(BitcoinPriceService.EnabledPrefKey);
            int previous = PlayerPrefs.GetInt(BitcoinPriceService.EnabledPrefKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(BitcoinPriceService.EnabledPrefKey);
                Assert.IsFalse(BitcoinPriceService.Enabled,
                    "the Bitcoin feed defaulted to ON; nothing may reach the network unasked");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(BitcoinPriceService.EnabledPrefKey, previous);
                else PlayerPrefs.DeleteKey(BitcoinPriceService.EnabledPrefKey);
                PlayerPrefs.Save();
            }
        }
    }
}
