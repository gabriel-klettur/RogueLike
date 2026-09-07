using System;
using System.Collections.Generic;

namespace Valkur.Core.Economy
{
    /// <summary>The chart intervals the Economy editor offers.</summary>
    public enum BitcoinTimeframe
    {
        /// <summary>One candle per hour.</summary>
        Hour1 = 0,

        /// <summary>One candle per four hours.</summary>
        Hour4 = 1,

        /// <summary>One candle per day.</summary>
        Day1 = 2,
    }

    /// <summary>One OHLC bar. Immutable, and deliberately carries no volume — nothing here reads it.</summary>
    public readonly struct BitcoinCandle
    {
        public long OpenTimeUnixMs { get; }
        public double Open { get; }
        public double High { get; }
        public double Low { get; }
        public double Close { get; }

        public BitcoinCandle(long openTimeUnixMs, double open, double high, double low, double close)
        {
            OpenTimeUnixMs = openTimeUnixMs;
            Open = open;
            High = high;
            Low = low;
            Close = close;
        }
    }

    /// <summary>
    /// A fetched run of candles for one timeframe, plus WHEN it was fetched and from where.
    ///
    /// <para><b>The staleness stamp is not decoration.</b> This data comes off a network that
    /// is allowed to be unavailable, and the failure mode of a cached chart with no timestamp
    /// is the worst kind: it looks live. Every reader is expected to show <see cref="AgeUtc"/>
    /// — an author making a decision off a price needs to know whether it is from a minute ago
    /// or from the last time the machine had a connection.</para>
    ///
    /// <para>Pure <c>Valkur.Core</c>: no <c>UnityWebRequest</c>, no JSON, no coroutine. The
    /// fetching lives in <c>Valkur.Gameplay</c>, so this type can be constructed and asserted
    /// on in an EditMode test with no network at all — which is the only way the chart maths
    /// below is testable.</para>
    /// </summary>
    public sealed class BitcoinSeries
    {
        /// <summary>The interval each candle covers.</summary>
        public BitcoinTimeframe Timeframe { get; }

        /// <summary>Oldest first. Never null; may be empty.</summary>
        public IReadOnlyList<BitcoinCandle> Candles { get; }

        /// <summary>When this run was fetched, UTC.</summary>
        public DateTime FetchedUtc { get; }

        /// <summary>Where it came from, for the status line. Never read by any logic.</summary>
        public string Source { get; }

        public BitcoinSeries(BitcoinTimeframe timeframe, IReadOnlyList<BitcoinCandle> candles,
                             DateTime fetchedUtc, string source)
        {
            Timeframe = timeframe;
            Candles = candles ?? Array.Empty<BitcoinCandle>();
            FetchedUtc = fetchedUtc;
            Source = source ?? "";
        }

        /// <summary>True when there is nothing to draw.</summary>
        public bool IsEmpty => Candles.Count == 0;

        /// <summary>How long ago this was fetched.</summary>
        public TimeSpan AgeUtc => DateTime.UtcNow - FetchedUtc;

        /// <summary>The most recent close, or 0 when empty.</summary>
        public double LastClose => IsEmpty ? 0d : Candles[Candles.Count - 1].Close;

        /// <summary>The first close in the run, or 0 when empty.</summary>
        public double FirstClose => IsEmpty ? 0d : Candles[0].Close;

        /// <summary>
        /// Percent change across the whole run. Zero when empty or when the run opens at zero
        /// — a divide there would produce an infinity that formats as a plausible-looking
        /// number on screen.
        /// </summary>
        public double ChangePercent
        {
            get
            {
                double first = FirstClose;
                if (IsEmpty || first <= 0d) return 0d;
                return (LastClose - first) / first * 100d;
            }
        }

        /// <summary>
        /// Lowest LOW and highest HIGH across the run — the vertical extent a chart has to
        /// cover. Taken from the wicks rather than the closes because a chart scaled to the
        /// closes clips the very spikes an author is looking at.
        /// </summary>
        public void Extent(out double low, out double high)
        {
            if (IsEmpty) { low = 0d; high = 0d; return; }

            low = double.MaxValue;
            high = double.MinValue;
            for (int i = 0; i < Candles.Count; i++)
            {
                if (Candles[i].Low < low) low = Candles[i].Low;
                if (Candles[i].High > high) high = Candles[i].High;
            }

            // A perfectly flat run has zero extent and would divide by zero in every
            // normaliser downstream. Open a symmetric window around the value instead, so a
            // flat line draws through the middle rather than at an edge or not at all.
            if (high - low < double.Epsilon)
            {
                double pad = Math.Abs(high) * 0.01d;
                if (pad <= 0d) pad = 1d;
                low -= pad;
                high += pad;
            }
        }

        /// <summary>
        /// The closes, normalised into 0..1 against <see cref="Extent"/> — what a line chart
        /// actually plots. Returns an empty array for an empty series rather than null, so a
        /// renderer never has to null-check before a loop.
        /// </summary>
        public float[] NormalisedCloses()
        {
            if (IsEmpty) return Array.Empty<float>();

            Extent(out double low, out double high);
            double span = high - low;

            var result = new float[Candles.Count];
            for (int i = 0; i < Candles.Count; i++)
                result[i] = (float)((Candles[i].Close - low) / span);
            return result;
        }

        /// <summary>
        /// A deterministic integer derived from the most recent close, for use as a market
        /// seed.
        ///
        /// <para><b>This is the ONLY sanctioned bridge from a real-world price into gameplay,
        /// and the shape is the entire argument.</b> The number is read ONCE, turned into a
        /// seed, and written to the save; nothing re-reads the feed while playing. That keeps
        /// every property the cycle is built on — a run stays replayable from two integers, a
        /// bug report stays reproducible, the game still works offline, and a player cannot
        /// move their own prices with a proxy or a system clock. A price consulted live would
        /// forfeit all four.</para>
        ///
        /// <para>Quantised to whole units before hashing, so two fetches seconds apart at
        /// 61234.51 and 61234.86 give the SAME seed. Without that an author pressing the
        /// button twice would reshape the world's economy for no reason they could see.</para>
        /// </summary>
        public int SeedFromLastClose()
        {
            long whole = (long)Math.Floor(LastClose);
            if (whole <= 0L) return 0;

            int seed = unchecked((int)MarketCycle.Hash((int)(whole & 0x7FFFFFFF), (int)Timeframe));
            // 0 is the "nobody set a seed" sentinel everywhere in this layer; a hash that
            // lands on it must not be mistaken for "no seed".
            return seed == 0 ? 1 : seed;
        }
    }
}
