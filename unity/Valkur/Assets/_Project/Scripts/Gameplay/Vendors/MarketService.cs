using UnityEngine;
using Valkur.Core;
using Valkur.Core.Economy;
using Valkur.Data;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// The live owner of the economic cycle: which day the market is on, what seed shapes it,
    /// and therefore what <see cref="MarketCycle"/> every price is resolved against.
    ///
    /// <para><b>It keeps its OWN day counter and does not read
    /// <c>DayNightCycle.DayCount</c> as the source of truth.</b> That counter is not
    /// persisted and runs backwards across a Play-mode restart — the chat journal already
    /// documents that, and a market driven by it would rewind to Boom every launch, which
    /// makes the cycle unlearnable and turns "sell at the Peak" into a session trick. What it
    /// DOES read is the day-changed EVENT, as a tick: the world says a day passed, the market
    /// advances one, and the total is saved.</para>
    ///
    /// <para><b>The seed decides the flavour, this class never invents it live.</b> A run
    /// without a stored seed derives one from its run id, so two characters do not share a
    /// market. <see cref="SetSeed"/> is the door for anything else — including a real-world
    /// index, which is the ONLY shape in which such an index is safe here: read once, written
    /// to the save, and never consulted again while playing. A price that re-reads an external
    /// feed is a price the player can change with a proxy or a system clock, cannot be
    /// reproduced from a bug report, and does not exist offline.</para>
    /// </summary>
    public class MarketService : SingletonMonoBehaviour<MarketService>
    {
        /// <summary>Save metadata key holding the seed. See <see cref="Seed"/>.</summary>
        public const string SeedMetaKey = "market.seed";

        /// <summary>Save metadata key holding the day counter. See <see cref="Day"/>.</summary>
        public const string DayMetaKey = "market.day";

        /// <summary>Save metadata key holding where the seed came from. Diagnostic only.</summary>
        public const string SourceMetaKey = "market.seed_source";

        /// <summary>What <see cref="SeedSource"/> reads when nothing has claimed the seed.</summary>
        public const string DefaultSeedSource = "run";

        /// <summary>
        /// Fires when the resolved market changes — a day tick, a seed change, a save
        /// restore. Static so UI can subscribe without ordering against this component's
        /// creation; reset on subsystem registration because Domain Reload is OFF and a
        /// subscriber from the previous session would otherwise still be attached.
        /// </summary>
        public static System.Action<MarketCycle> OnMarketChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvent() => OnMarketChanged = null;

        [SerializeField, Tooltip("Turns the whole cycle off: every multiplier reads 1. Left " +
                                 "on by default — a shipped layer nothing exercises is the " +
                                 "authored-and-inert shape this project keeps paying for.")]
        private bool cycleEnabled = true;

        private int _seed;
        private int _day;
        private string _seedSource = DefaultSeedSource;
        private bool _subscribed;

        /// <summary>The integer that shapes this run's cycle lengths.</summary>
        public int Seed => _seed;

        /// <summary>Days elapsed as far as the market is concerned. Monotonic, persisted.</summary>
        public int Day => _day;

        /// <summary>Where <see cref="Seed"/> came from. Diagnostic; never read by pricing.</summary>
        public string SeedSource => _seedSource;

        /// <summary>Whether the cycle moves prices at all.</summary>
        public bool CycleEnabled => cycleEnabled;

        /// <summary>
        /// The cycle's authored shape, or the shipped defaults when no tuning asset exists.
        ///
        /// <para>Read every time rather than cached: the Economy editor writes the asset live
        /// and a cached shape would leave the world priced against numbers the author has
        /// already changed, which is exactly the "apply button" confusion a live-editing
        /// surface exists to avoid. <c>EconomyTuning.Active</c> does its own caching, so the
        /// cost is a field read.</para>
        /// </summary>
        public MarketShape Shape
        {
            get
            {
                var tuning = EconomyTuning.Active;
                tuning.ResolveCycleDays(out int min, out int max);
                return new MarketShape(tuning.amplitude, min, max);
            }
        }

        /// <summary>The market today.</summary>
        public MarketCycle Current => MarketCycle.Resolve(_seed, _day, Shape);

        /// <summary>
        /// Multiplier on what the player PAYS. Reads exactly 1 while the cycle is off, so a
        /// caller never needs to branch on <see cref="CycleEnabled"/>.
        /// </summary>
        public float BuyMultiplier => cycleEnabled ? Current.BuyMultiplier : 1f;

        /// <summary>Multiplier on what the player RECEIVES. 1 while the cycle is off.</summary>
        public float SellMultiplier => cycleEnabled ? Current.SellMultiplier : 1f;

        protected override void OnSingletonAwake()
        {
            if (_seed == 0) _seed = DeriveSeedFromRun();
            ServiceLocator.Register(this);
            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        protected override void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) ServiceLocator.Unregister<MarketService>();
            base.OnDestroy();
        }

        /// <summary>
        /// Guarded twice, against two different failures.
        ///
        /// <para><c>_subscribed</c> because <c>DayNightCycle.OnDayChanged</c> is a STATIC
        /// delegate on a project with Domain Reload off: a handler added twice fires twice —
        /// two market days per world day — and one never removed keeps a destroyed service
        /// alive for the session. The chat system records the identical hazard.</para>
        ///
        /// <para><c>Instance != this</c> because a DUPLICATE still runs <c>OnEnable</c>.
        /// <c>SingletonMonoBehaviour.Awake</c> calls <c>Destroy(gameObject)</c> on the loser
        /// and returns without reaching <c>OnSingletonAwake</c>, but <c>Destroy</c> is
        /// deferred to end of frame and <c>OnEnable</c> runs right after <c>Awake</c> — so
        /// without this the doomed copy subscribes, and for the rest of that frame a day tick
        /// advances the market TWICE. It heals itself a frame later, which is exactly what
        /// would make it impossible to find.</para>
        /// </summary>
        private void Subscribe()
        {
            if (_subscribed || Instance != this) return;
            World.DayNightCycle.OnDayChanged += HandleDayChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            World.DayNightCycle.OnDayChanged -= HandleDayChanged;
            _subscribed = false;
        }

        private void HandleDayChanged(int worldDay)
        {
            // The argument is deliberately ignored: it is the volatile counter this class
            // exists not to trust. What the event carries that matters is that it fired.
            AdvanceDay(1);
        }

        /// <summary>Moves the market forward. Negative steps are refused, not clamped silently.</summary>
        public void AdvanceDay(int days)
        {
            if (days <= 0) return;
            _day += days;
            OnMarketChanged?.Invoke(Current);
        }

        /// <summary>
        /// Claims the seed and records where it came from.
        ///
        /// <para><paramref name="source"/> is a label, not a mechanism — nothing in pricing
        /// reads it. It exists so a save can say WHY its market looks the way it does, which
        /// is the difference between "the cycle is broken" and "this run was seeded from
        /// something unusual" when a report arrives.</para>
        ///
        /// <para>A seed of 0 is refused: it is what an unset integer deserialises to, so
        /// accepting it would make "nobody set a seed" and "somebody set zero"
        /// indistinguishable — and every run that had never been saved would share one
        /// market.</para>
        /// </summary>
        public void SetSeed(int seed, string source)
        {
            if (seed == 0) return;
            _seed = seed;
            _seedSource = string.IsNullOrEmpty(source) ? DefaultSeedSource : source;
            OnMarketChanged?.Invoke(Current);
        }

        /// <summary>
        /// Re-announces the market after the TUNING changed rather than the seed or the day.
        ///
        /// <para>Nothing needs invalidating — <see cref="Shape"/> is read fresh on every
        /// resolve — but every listener drawn from the cycle (the Economy editor's own
        /// readout, any HUD) is repainted by the event and by nothing else, so retuning the
        /// amplitude would otherwise change the world's prices while every panel showing them
        /// kept the old numbers until something unrelated ticked.</para>
        /// </summary>
        public void NotifyTuningChanged() => OnMarketChanged?.Invoke(Current);

        /// <summary>Turns the cycle on or off at runtime. Used by the console and by options.</summary>
        public void SetCycleEnabled(bool enabled)
        {
            cycleEnabled = enabled;
            OnMarketChanged?.Invoke(Current);
        }

        /// <summary>
        /// Rehydrates seed and day from a save. Both are read leniently: a save written
        /// before this layer existed carries neither, and the right answer there is a fresh
        /// market rather than a refusal.
        /// </summary>
        public void RestoreFrom(int seed, int day, string source)
        {
            if (seed != 0) _seed = seed;
            if (_seed == 0) _seed = DeriveSeedFromRun();
            _day = Mathf.Max(0, day);
            if (!string.IsNullOrEmpty(source)) _seedSource = source;
            OnMarketChanged?.Invoke(Current);
        }

        /// <summary>
        /// A seed for a run that has never had one. Derived from the run id so two characters
        /// played on the same machine do not share a market, and stable within a run so the
        /// cycle does not jump before the first save writes it down.
        ///
        /// <para>Never <c>Random.Range</c>: a market re-seeded on every scene load would move
        /// the phase under a player mid-run. Never <c>string.GetHashCode</c> either — .NET is
        /// allowed to vary it between processes, which is exactly the irreproducibility this
        /// whole layer is built to avoid.</para>
        /// </summary>
        private static int DeriveSeedFromRun()
        {
            string runId = SaveService.HasInstance ? SaveService.Instance.RunId : null;
            if (string.IsNullOrEmpty(runId)) return unchecked((int)MarketCycle.Hash(20260907, 1));

            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < runId.Length; i++)
                {
                    h ^= runId[i];
                    h *= 16777619u;
                }
                int seed = (int)h;
                return seed == 0 ? 1 : seed;
            }
        }
    }
}
