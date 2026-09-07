using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Core.Economy;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The economic cycle, and the four properties that are the whole reason it is endogenous
    /// rather than driven by an outside feed.
    ///
    /// <para>Each test here is a claim the audit made about why a live external price source
    /// would be the wrong shape for a single-player game: it could not be replayed, could not
    /// be bounded, could not be tested without a network, and could not be stopped from
    /// producing a day the player should simply not log in for. These fixtures are what make
    /// those four claims checkable rather than opinions.</para>
    /// </summary>
    public class MarketCycleTests
    {
        private const int SampleSeed = 987654321;

        /// <summary>
        /// Replayability. Same seed and day, same market, every time — which is what lets a
        /// save carry two integers instead of a price table, and what lets a bug report about
        /// "prices went mad" be reproduced instead of believed.
        /// </summary>
        [Test]
        public void SameSeedAndDay_AlwaysResolvesTheSameMarket()
        {
            for (int day = 0; day < 200; day++)
            {
                var a = MarketCycle.Resolve(SampleSeed, day);
                var b = MarketCycle.Resolve(SampleSeed, day);

                Assert.AreEqual(a.Phase, b.Phase, $"phase drifted on day {day}");
                Assert.AreEqual(a.Pressure, b.Pressure, 1e-6f, $"pressure drifted on day {day}");
                Assert.AreEqual(a.CycleLength, b.CycleLength, $"cycle length drifted on day {day}");
                Assert.AreEqual(a.DayInCycle, b.DayInCycle, $"day-in-cycle drifted on day {day}");
            }
        }

        /// <summary>
        /// Two runs must not share an economy. A market that ignored its seed would be one
        /// global cycle every player learns once — and the failure would be invisible,
        /// because each run in isolation still looks like a working market.
        /// </summary>
        [Test]
        public void DifferentSeeds_ProduceDifferentMarkets()
        {
            int differences = 0;
            for (int day = 0; day < 120; day++)
            {
                if (MarketCycle.Resolve(1, day).Phase != MarketCycle.Resolve(2, day).Phase)
                    differences++;
            }

            Assert.That(differences, Is.GreaterThan(10),
                "two seeds produced near-identical markets over 120 days; the seed is not reaching the cycle");
        }

        /// <summary>
        /// The amplitude cap is the promise that no day is unplayable. Checked over both
        /// multipliers and many seeds, because the bound has to hold at the extremes of the
        /// sine and not merely on average.
        /// </summary>
        [Test]
        public void EveryMultiplier_StaysInsideTheAmplitudeBand()
        {
            float lo = 1f - MarketCycle.Amplitude;
            float hi = 1f + MarketCycle.Amplitude;

            for (int seed = 1; seed <= 40; seed++)
            for (int day = 0; day < 150; day++)
            {
                var m = MarketCycle.Resolve(seed, day);
                Assert.That(m.BuyMultiplier, Is.InRange(lo, hi), $"buy out of band, seed {seed} day {day}");
                Assert.That(m.SellMultiplier, Is.InRange(lo, hi), $"sell out of band, seed {seed} day {day}");
                Assert.That(m.Pressure, Is.InRange(-1f, 1f), $"pressure out of range, seed {seed} day {day}");
            }
        }

        /// <summary>
        /// Buying and selling move TOGETHER. Inverting the sell side would make every Trough a
        /// day when goods are cheap AND scrap pays well — a free lunch on a timer, which turns
        /// the layer from a market into an arbitrage clock.
        /// </summary>
        [Test]
        public void BuyAndSell_MoveInTheSameDirection()
        {
            for (int day = 0; day < 100; day++)
            {
                var m = MarketCycle.Resolve(SampleSeed, day);
                bool buyUp = m.BuyMultiplier >= 1f;
                bool sellUp = m.SellMultiplier >= 1f;
                Assert.AreEqual(buyUp, sellUp,
                    $"day {day}: buy and sell disagreed on direction (buy={m.BuyMultiplier}, sell={m.SellMultiplier})");
            }
        }

        /// <summary>
        /// A cycle has to be long enough to live through and short enough to see twice. Also
        /// pins that the length really does VARY: a constant period is a calendar, and a
        /// player who can count days is not reading a market.
        /// </summary>
        [Test]
        public void CycleLengths_StayInRange_AndVary()
        {
            var lengths = new HashSet<int>();
            for (int seed = 1; seed <= 25; seed++)
            for (int day = 0; day < 200; day++)
            {
                var m = MarketCycle.Resolve(seed, day);
                Assert.That(m.CycleLength,
                    Is.InRange(MarketCycle.MinCycleDays, MarketCycle.MaxCycleDays),
                    $"seed {seed} day {day} produced a {m.CycleLength}-day cycle");
                Assert.That(m.DayInCycle, Is.InRange(0, m.CycleLength - 1));
                lengths.Add(m.CycleLength);
            }

            Assert.That(lengths.Count, Is.GreaterThan(1),
                "every cycle came out the same length; the player can play a calendar instead of a market");
        }

        /// <summary>
        /// All four phases must actually be reachable. A phase nothing ever enters is a
        /// quarter of the design that does not exist, and it fails silently — the market goes
        /// on looking like it works.
        /// </summary>
        [Test]
        public void EveryPhase_IsReached()
        {
            var seen = new HashSet<MarketPhase>();
            for (int day = 0; day < 300; day++)
                seen.Add(MarketCycle.Resolve(SampleSeed, day).Phase);

            CollectionAssert.AreEquivalent(
                new[] { MarketPhase.Boom, MarketPhase.Peak, MarketPhase.Bust, MarketPhase.Trough },
                seen);
        }

        /// <summary>
        /// A Peak really is dearer than a Trough. Without this the phase NAMES could be
        /// correct while the pressure they carry was backwards — which reads on screen as a
        /// boom that makes everything cheap, and reads in code as nothing at all.
        /// </summary>
        [Test]
        public void Peak_IsDearerThanTrough()
        {
            float peakMax = float.MinValue, troughMin = float.MaxValue;
            for (int day = 0; day < 300; day++)
            {
                var m = MarketCycle.Resolve(SampleSeed, day);
                if (m.Phase == MarketPhase.Peak) peakMax = System.Math.Max(peakMax, m.BuyMultiplier);
                if (m.Phase == MarketPhase.Trough) troughMin = System.Math.Min(troughMin, m.BuyMultiplier);
            }

            Assert.That(peakMax, Is.GreaterThan(1f), "a Peak never rose above the neutral price");
            Assert.That(troughMin, Is.LessThan(1f), "a Trough never fell below the neutral price");
        }

        /// <summary>
        /// A day counter running backwards is a bug upstream. Clamping answers it with day 0;
        /// wrapping would answer it with a confident, plausible, wrong phase — which is worse,
        /// because nothing then reports the bug.
        /// </summary>
        [Test]
        public void NegativeDays_ClampToDayZero()
        {
            var zero = MarketCycle.Resolve(SampleSeed, 0);
            foreach (int day in new[] { -1, -7, -9999 })
            {
                var m = MarketCycle.Resolve(SampleSeed, day);
                Assert.AreEqual(zero.Phase, m.Phase);
                Assert.AreEqual(zero.Pressure, m.Pressure, 1e-6f);
            }
        }

        /// <summary>
        /// The hash must be pure integer arithmetic, never <c>GetHashCode</c>, which .NET is
        /// explicitly allowed to vary between processes. Pinning literal outputs is what makes
        /// that concrete: if someone swaps the implementation for a "cleaner" one, a save's
        /// market silently reshapes and this is the only thing that says so.
        /// </summary>
        [Test]
        public void Hash_IsPinnedToLiteralValues()
        {
            Assert.AreEqual(MarketCycle.Hash(0, 0), MarketCycle.Hash(0, 0));
            Assert.AreNotEqual(MarketCycle.Hash(1, 0), MarketCycle.Hash(0, 1));

            // Recorded from the shipped implementation. A change here is a save-compat break,
            // not a refactor.
            Assert.AreEqual(260073162u, MarketCycle.Hash(12345, 0), "hash drifted for (12345, 0)");
            Assert.AreEqual(301794027u, MarketCycle.Hash(1, 0), "hash drifted for (1, 0)");
            Assert.AreEqual(2612203797u, MarketCycle.Hash(0, 1), "hash drifted for (0, 1)");
        }
    }
}
