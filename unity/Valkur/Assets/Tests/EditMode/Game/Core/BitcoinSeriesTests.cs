using System;
using System.Collections.Generic;
using NUnit.Framework;
using Valkur.Core.Economy;

namespace Valkur.Tests.EditMode.Game.Core
{
    /// <summary>
    /// The Bitcoin chart's maths, tested with no network at all.
    ///
    /// <para>That separation is the reason <see cref="BitcoinSeries"/> lives in
    /// <c>Valkur.Core</c> with the fetching in <c>Valkur.Gameplay</c>: every decision this type
    /// makes — how a chart is scaled, what a flat run does, how a price becomes a market seed —
    /// is checkable here, while the half that can fail for reasons nobody controls stays out of
    /// the suite entirely.</para>
    /// </summary>
    public class BitcoinSeriesTests
    {
        private static BitcoinSeries Make(params double[] closes)
        {
            var candles = new List<BitcoinCandle>();
            for (int i = 0; i < closes.Length; i++)
            {
                // A wick either side of the close, so the extent tests are measuring highs and
                // lows rather than closes by accident.
                candles.Add(new BitcoinCandle(i * 3600000L, closes[i], closes[i] + 10d,
                                              closes[i] - 10d, closes[i]));
            }
            return new BitcoinSeries(BitcoinTimeframe.Day1, candles, DateTime.UtcNow, "test");
        }

        /// <summary>
        /// An empty series must answer every question without throwing. It is the state the
        /// panel is in before the first fetch lands and every time the machine is offline —
        /// i.e. the common case, not the edge case.
        /// </summary>
        [Test]
        public void AnEmptySeries_AnswersEverythingWithoutThrowing()
        {
            var empty = new BitcoinSeries(BitcoinTimeframe.Hour1, null, DateTime.UtcNow, null);

            Assert.IsTrue(empty.IsEmpty);
            Assert.AreEqual(0d, empty.LastClose);
            Assert.AreEqual(0d, empty.ChangePercent);
            Assert.AreEqual(0, empty.NormalisedCloses().Length);
            Assert.AreEqual(0, empty.SeedFromLastClose());
            Assert.DoesNotThrow(() => empty.Extent(out _, out _));
        }

        /// <summary>
        /// The chart is scaled to the WICKS, not the closes. Scaling to closes clips the very
        /// spikes an author opened the chart to look at — and it is invisible, because the
        /// clipped chart is still a plausible chart.
        /// </summary>
        [Test]
        public void Extent_CoversTheWicks_NotJustTheCloses()
        {
            var series = Make(100d, 200d, 150d);
            series.Extent(out double low, out double high);

            Assert.AreEqual(90d, low, 1e-6, "extent ignored the low wick");
            Assert.AreEqual(210d, high, 1e-6, "extent ignored the high wick");
        }

        /// <summary>
        /// A perfectly flat run has zero extent and would divide by zero in every normaliser
        /// downstream. It must open a window around the value instead — a flat line drawn
        /// through the middle, not a NaN and not nothing.
        /// </summary>
        [Test]
        public void AFlatRun_DoesNotDivideByZero()
        {
            var flat = new BitcoinSeries(BitcoinTimeframe.Hour1, new List<BitcoinCandle>
            {
                new BitcoinCandle(0L, 50d, 50d, 50d, 50d),
                new BitcoinCandle(1L, 50d, 50d, 50d, 50d),
            }, DateTime.UtcNow, "test");

            flat.Extent(out double low, out double high);
            Assert.That(high, Is.GreaterThan(low), "a flat run produced a zero-width window");

            foreach (float v in flat.NormalisedCloses())
            {
                Assert.IsFalse(float.IsNaN(v), "normalisation produced NaN on a flat run");
                Assert.That(v, Is.InRange(0f, 1f));
            }
        }

        /// <summary>
        /// Normalised values must land in 0..1, with the extremes actually reaching the ends —
        /// a chart whose maximum sits at 0.8 wastes a fifth of its height and reads as a run
        /// that never got anywhere.
        /// </summary>
        [Test]
        public void NormalisedCloses_SpanTheFullRange()
        {
            var series = Make(100d, 300d, 200d);
            var values = series.NormalisedCloses();

            Assert.AreEqual(3, values.Length);
            foreach (float v in values) Assert.That(v, Is.InRange(0f, 1f));

            Assert.That(values[0], Is.LessThan(values[2]), "the lowest close did not plot lowest");
            Assert.That(values[1], Is.GreaterThan(values[2]), "the highest close did not plot highest");
        }

        /// <summary>
        /// Change is measured first-to-last and signed. Both directions, because a sign error
        /// here paints a crash green.
        /// </summary>
        [Test]
        public void ChangePercent_IsSignedAndMeasuredAcrossTheRun()
        {
            Assert.AreEqual(50d, Make(100d, 150d).ChangePercent, 1e-6);
            Assert.AreEqual(-50d, Make(100d, 50d).ChangePercent, 1e-6);
            Assert.AreEqual(0d, Make(100d, 100d).ChangePercent, 1e-6);
        }

        /// <summary>
        /// A run opening at zero has no percentage to report, and dividing anyway produces an
        /// infinity that formats on screen as a plausible-looking number.
        /// </summary>
        [Test]
        public void ChangePercent_IsZero_WhenTheRunOpensAtZero()
        {
            var series = new BitcoinSeries(BitcoinTimeframe.Hour1, new List<BitcoinCandle>
            {
                new BitcoinCandle(0L, 0d, 0d, 0d, 0d),
                new BitcoinCandle(1L, 10d, 10d, 10d, 10d),
            }, DateTime.UtcNow, "test");

            Assert.AreEqual(0d, series.ChangePercent);
        }

        /// <summary>
        /// The seed is QUANTISED to whole units, so two fetches seconds apart give the same
        /// answer. Without it an author pressing the button twice would reshape the world's
        /// economy for no reason they could see.
        /// </summary>
        [Test]
        public void SeedFromLastClose_IsStableWithinAWholeUnit()
        {
            int a = Make(61234.51d).SeedFromLastClose();
            int b = Make(61234.86d).SeedFromLastClose();
            int c = Make(61235.02d).SeedFromLastClose();

            Assert.AreEqual(a, b, "two prices inside the same whole unit gave different seeds");
            Assert.AreNotEqual(a, c, "a whole unit of movement did not change the seed");
        }

        /// <summary>
        /// Never 0. That value is the "nobody set a seed" sentinel everywhere in this layer,
        /// and a hash landing on it would be read as "no seed" rather than as the seed it is.
        /// </summary>
        [Test]
        public void SeedFromLastClose_NeverReturnsTheNoSeedSentinel_ForARealPrice()
        {
            for (double price = 1d; price < 200000d; price *= 1.37d)
                Assert.AreNotEqual(0, Make(price).SeedFromLastClose(), $"price {price} produced seed 0");
        }

        /// <summary>
        /// A price of zero or below is not a price. It yields 0 — which the caller reads as
        /// "nothing to seed with" and refuses, rather than seeding the world from a hash of
        /// nothing.
        /// </summary>
        [Test]
        public void SeedFromLastClose_RefusesANonPrice()
        {
            Assert.AreEqual(0, Make(0d).SeedFromLastClose());
            Assert.AreEqual(0, Make(-5d).SeedFromLastClose());
        }

        /// <summary>
        /// The same price on two timeframes gives two different markets. Otherwise switching
        /// the chart from 1H to Daily and reseeding would look like it did something and
        /// change nothing.
        /// </summary>
        [Test]
        public void SeedFromLastClose_DiffersByTimeframe()
        {
            var candles = new List<BitcoinCandle> { new BitcoinCandle(0L, 60000d, 60000d, 60000d, 60000d) };
            int hourly = new BitcoinSeries(BitcoinTimeframe.Hour1, candles, DateTime.UtcNow, "t").SeedFromLastClose();
            int daily = new BitcoinSeries(BitcoinTimeframe.Day1, candles, DateTime.UtcNow, "t").SeedFromLastClose();

            Assert.AreNotEqual(hourly, daily);
        }
    }
}
