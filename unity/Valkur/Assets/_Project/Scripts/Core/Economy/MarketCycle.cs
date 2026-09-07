using System;
using UnityEngine;

namespace Valkur.Core.Economy
{
    /// <summary>The four phases a market walks through, in order.</summary>
    public enum MarketPhase
    {
        /// <summary>Prices climbing. Selling is good, restocking is not.</summary>
        Boom = 0,

        /// <summary>The top. The best day of the cycle to unload a bag.</summary>
        Peak = 1,

        /// <summary>Falling. The moment to hold and to spend.</summary>
        Bust = 2,

        /// <summary>The bottom. Everything is cheap and nobody pays for anything.</summary>
        Trough = 3,
    }

    /// <summary>
    /// The three dials that decide a cycle's SHAPE, passed in rather than read.
    ///
    /// <para><b>Passed in because <c>Valkur.Core</c> may not reference <c>Valkur.Data</c>,</b>
    /// and the authored values live on an <c>EconomyTuning</c> ScriptableObject over there.
    /// Inverting the dependency to read it would break the assembly rule; handing the numbers
    /// down keeps <see cref="MarketCycle.Resolve"/> a pure function of its arguments, which is
    /// the property the whole layer is built on — a resolve that reached out to a live asset
    /// could not be replayed from a save's two integers.</para>
    ///
    /// <para>The constructor CLAMPS and SWAPS rather than refusing. These are authored one
    /// field at a time in a live editor, so "minimum currently above maximum" is a state every
    /// author passes through on the way to a valid pair — a refusal there makes the second
    /// field unreachable, and a pure struct has nowhere to report one from anyway.</para>
    /// </summary>
    public readonly struct MarketShape
    {
        /// <summary>How far a phase may move a price, as a fraction. See <see cref="MarketCycle.Amplitude"/>.</summary>
        public float Amplitude { get; }

        /// <summary>Shortest a full four-phase cycle may run, in in-game days.</summary>
        public int MinCycleDays { get; }

        /// <summary>Longest a full four-phase cycle may run, in in-game days.</summary>
        public int MaxCycleDays { get; }

        public MarketShape(float amplitude, int minCycleDays, int maxCycleDays)
        {
            Amplitude = Mathf.Clamp(amplitude, 0f, MarketCycle.AmplitudeCeiling);

            int min = Mathf.Max(1, minCycleDays);
            int max = Mathf.Max(1, maxCycleDays);
            if (min > max) { int t = min; min = max; max = t; }
            MinCycleDays = min;
            MaxCycleDays = max;
        }

        /// <summary>
        /// The shape the constants shipped with. What <see cref="MarketCycle.Resolve(int,int)"/>
        /// uses, so every caller written before tuning existed keeps its exact behaviour.
        /// </summary>
        public static MarketShape Default =>
            new MarketShape(MarketCycle.Amplitude, MarketCycle.MinCycleDays, MarketCycle.MaxCycleDays);
    }

    /// <summary>
    /// What the market is doing on one in-game day, and what that does to a price.
    ///
    /// <para><b>This is deterministic and endogenous, and both halves are the design.</b> The
    /// state of the economy on day N is a pure function of <c>(seed, N)</c> — no clock, no
    /// network, no <c>UnityEngine.Random</c>. That is what makes it saveable (one integer),
    /// testable (assert on a day, not on a mood), reproducible (a bug report about "prices
    /// were insane" can be replayed), and unexploitable (there is nothing outside the save to
    /// tamper with). An economy driven live by an external feed has none of those four, and
    /// in a single-player game the feed is client-authoritative anyway — a mechanic the
    /// player can change by editing a clock is not a constraint.</para>
    ///
    /// <para><b>Where a real-world index CAN legitimately go is the SEED</b> — see
    /// <see cref="MarketSeed"/>. Read once, cached in the save, it decides the FLAVOUR of a
    /// stretch of days while this class keeps deciding the RULES. That split is the whole
    /// argument: the shape of the cycle stays authored and bounded, while what particular
    /// shape you got this week can come from outside if that is wanted.</para>
    ///
    /// <para><b>Why a shaped cycle rather than noise.</b> A price multiplier resampled per day
    /// is a random walk, and a random walk teaches the player nothing: they cannot read it,
    /// anticipate it, or act on it, so it is a tax with a theme. Phases give it the one
    /// property that makes an economy playable — a Boom TELLS you a Peak is coming, and
    /// holding an ore stack for two more days becomes a decision instead of a coin flip.</para>
    /// </summary>
    public readonly struct MarketCycle
    {
        /// <summary>Default shortest full four-phase cycle, in in-game days.</summary>
        public const int MinCycleDays = 6;

        /// <summary>Default longest full four-phase cycle, in in-game days.</summary>
        public const int MaxCycleDays = 14;

        /// <summary>
        /// Default ceiling on how far a phase may move a price, as a fraction.
        ///
        /// <para>0.25 is a judgement and it is the load-bearing number in the file. Below
        /// ~0.10 nobody notices the cycle exists and the whole layer is decoration; above
        /// ~0.35 a Trough stops meaning "a bad week to sell" and starts meaning "do not play
        /// today", which is a mechanic that punishes the player for logging in. Every
        /// multiplier this struct produces lands inside [1 - amplitude, 1 + amplitude].</para>
        ///
        /// <para>Authorable through <see cref="MarketShape"/>, but only up to
        /// <see cref="AmplitudeCeiling"/> — the dial is a judgement, the ceiling is a rule.</para>
        /// </summary>
        public const float Amplitude = 0.25f;

        /// <summary>
        /// The most any authored amplitude may reach.
        ///
        /// <para>A hard clamp rather than a slider range, because a range on an inspector
        /// field is a suggestion: the asset can be hand-edited, and a value arriving from a
        /// migrated or corrupted file must still land somewhere playable. At 0.60 a Trough
        /// already halves what a sale pays, which is the far edge of "a bad week"; beyond it
        /// the cycle stops being a market and becomes a lockout.</para>
        /// </summary>
        public const float AmplitudeCeiling = 0.60f;

        /// <summary>The phase this day sits in.</summary>
        public MarketPhase Phase { get; }

        /// <summary>
        /// Signed market pressure for the day, in [-1, +1]. +1 is the top of a Peak, -1 the
        /// floor of a Trough. This is the raw quantity; the multipliers below are what
        /// callers should use.
        /// </summary>
        public float Pressure { get; }

        /// <summary>Which day of the current cycle this is, zero-based.</summary>
        public int DayInCycle { get; }

        /// <summary>How many days this particular cycle runs for.</summary>
        public int CycleLength { get; }

        /// <summary>
        /// The amplitude this result was resolved with. Carried on the struct rather than read
        /// back from the tuning, so a multiplier can never disagree with the pressure it was
        /// computed beside — a retune mid-frame would otherwise scale a cached cycle by a
        /// number it was never resolved against.
        /// </summary>
        public float ResolvedAmplitude { get; }

        private MarketCycle(MarketPhase phase, float pressure, int dayInCycle, int cycleLength,
                            float amplitude)
        {
            Phase = phase;
            Pressure = pressure;
            DayInCycle = dayInCycle;
            CycleLength = cycleLength;
            ResolvedAmplitude = amplitude;
        }

        /// <summary>
        /// What the player PAYS, as a multiplier on the resolved buy price. High pressure
        /// makes goods expensive.
        /// </summary>
        public float BuyMultiplier => 1f + Pressure * ResolvedAmplitude;

        /// <summary>
        /// What the player RECEIVES, as a multiplier on the resolved sell price. Moves with
        /// buying, not against it: a boom lifts both sides of the counter.
        ///
        /// <para>It is deliberately NOT the inverse. Inverting it would mean a Trough where
        /// goods are cheap AND scrap pays well, which is a free lunch every cycle and turns
        /// the layer into an arbitrage timer. Moving together is also what a market does, and
        /// it keeps the interesting decision — WHEN to sell versus WHEN to buy — pointing at
        /// two different days instead of the same one.</para>
        /// </summary>
        public float SellMultiplier => 1f + Pressure * ResolvedAmplitude;

        /// <summary>
        /// Resolve the market on <paramref name="day"/> for a run seeded with
        /// <paramref name="seed"/>, using the shipped default shape.
        ///
        /// <para>Kept so every caller written before the shape was authorable reads exactly as
        /// it did — and so a test can pin the constants without reaching for an asset.</para>
        /// </summary>
        public static MarketCycle Resolve(int seed, int day) =>
            Resolve(seed, day, MarketShape.Default);

        /// <summary>
        /// Resolve the market on <paramref name="day"/> for a run seeded with
        /// <paramref name="seed"/>, under an authored <paramref name="shape"/>.
        ///
        /// <para>Negative days are clamped to 0 rather than wrapping: a day counter running
        /// backwards is a bug upstream, and wrapping would answer it with a confident,
        /// plausible, wrong phase.</para>
        /// </summary>
        public static MarketCycle Resolve(int seed, int day, MarketShape shape)
        {
            if (day < 0) day = 0;

            // Walk cycle by cycle rather than dividing: each cycle's LENGTH is itself derived
            // from the seed, so there is no constant period to divide by. The loop is bounded
            // by MinCycleDays per step, so even a very old save costs a few hundred
            // iterations of integer work — cheaper than caching it would be to invalidate.
            int cycleIndex = 0;
            int cursor = day;
            int length = LengthOf(seed, cycleIndex, shape);
            while (cursor >= length)
            {
                cursor -= length;
                cycleIndex++;
                length = LengthOf(seed, cycleIndex, shape);
            }

            float t = length <= 1 ? 0f : cursor / (float)length;

            // One full sine over the cycle, starting at zero and rising: day 0 is neutral
            // and climbing, a quarter in is the Peak, three quarters in the Trough. A sine
            // rather than a sawtooth because the two ENDS of a phase should feel different
            // from its middle — a price that steps between four plateaus reads as four
            // settings, not as a cycle.
            float pressure = (float)Math.Sin(t * 2d * Math.PI);

            return new MarketCycle(PhaseOf(t), pressure, cursor, length, shape.Amplitude);
        }

        /// <summary>
        /// Which phase the fraction <paramref name="t"/> through a cycle falls in.
        /// Quarters, so each phase is a readable stretch of days rather than a single
        /// day the player can miss by sleeping.
        /// </summary>
        private static MarketPhase PhaseOf(float t)
        {
            if (t < 0.25f) return MarketPhase.Boom;
            if (t < 0.50f) return MarketPhase.Peak;
            if (t < 0.75f) return MarketPhase.Bust;
            return MarketPhase.Trough;
        }

        /// <summary>
        /// Length in days of cycle number <paramref name="cycleIndex"/> under
        /// <paramref name="seed"/>. Varying it is what stops the player learning "sell every
        /// tenth day" and playing a calendar instead of a market.
        /// </summary>
        private static int LengthOf(int seed, int cycleIndex, MarketShape shape)
        {
            int span = shape.MaxCycleDays - shape.MinCycleDays + 1;
            return shape.MinCycleDays + (int)(Hash(seed, cycleIndex) % (uint)span);
        }

        /// <summary>
        /// A small integer avalanche (the finaliser from MurmurHash3). Deterministic across
        /// platforms because every step is unsigned integer arithmetic — no floating point,
        /// no <c>GetHashCode</c> (which .NET is explicitly allowed to vary between runs and
        /// which would make a save's market irreproducible on the same machine).
        ///
        /// <para>Public, not internal: <c>MarketService</c> lives in <c>Valkur.Gameplay</c>
        /// and derives a run's default seed with it, and <c>internal</c> does not cross an
        /// assembly boundary. Anything that needs a reproducible integer from a pair of
        /// integers is welcome to it — that is cheaper than a second avalanche that could
        /// disagree with this one.</para>
        /// </summary>
        public static uint Hash(int seed, int counter)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)counter * 2246822519u;
                h ^= h >> 16;
                h *= 2246822507u;
                h ^= h >> 13;
                h *= 3266489909u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
