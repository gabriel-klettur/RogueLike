using UnityEngine;
using Valkur.Core.Economy;

namespace Valkur.Data
{
    /// <summary>
    /// The economy's design-time dials, in one asset an editor can reach.
    ///
    /// <para><b>Why this exists.</b> Every number here was a <c>const</c> — the cycle's
    /// amplitude and period inside <c>MarketCycle</c>, the coin heuristic's divisors and
    /// variance inside <c>DeathDropSystem</c>. Constants are the right home for a rule and the
    /// wrong home for a JUDGEMENT, and each of these is a judgement about play that can only be
    /// settled by playing: "is a 25 % swing something the player notices?" is not answerable in
    /// a code review, and the stop-edit-play loop is what destroys the answer. Same argument
    /// the Camera editor makes about feel and the Skills editor about recipe levels.</para>
    ///
    /// <para><b>It is DESIGN-TIME state, deliberately not saved per run.</b> A save carries the
    /// market's seed and day; it does not carry these, exactly as it does not carry item
    /// prices. Retuning the amplitude changes every existing save's prices, and that is the
    /// correct behaviour for a balance change rather than a bug — the alternative, stamping the
    /// tuning into every save, would freeze old runs against a balance pass and make the two
    /// impossible to reconcile.</para>
    ///
    /// <para><b>Every consumer must be null-safe and fall back to these defaults.</b> The asset
    /// is optional: EditMode tests, a scene that predates it and a build with a stripped
    /// <c>Resources/</c> must all behave exactly as the constants did. A tuning layer that
    /// changes behaviour by being absent is worse than no tuning layer.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyTuning", menuName = "Valkur/Economy/Economy Tuning")]
    public sealed class EconomyTuning : ScriptableObject
    {
        /// <summary>
        /// Path under <c>Resources/</c>. Under Resources because the systems that read it —
        /// <c>MarketService</c> and <c>DeathDropSystem</c> — are both <c>AddComponent</c>-ed
        /// onto bare GameObjects and have no inspector slot to be wired from. Same reason
        /// <c>ProgressionCatalog</c> and <c>ChatAssignmentCatalog</c> live there.
        /// </summary>
        public const string ResourcePath = "Economy/EconomyTuning";

        // ── The cycle ────────────────────────────────────────────────────────

        [Header("Economic cycle")]
        [Tooltip("Hard ceiling on how far a phase may move a price, as a fraction. This is the " +
                 "load-bearing number of the whole layer: below ~0.10 nobody notices the cycle " +
                 "exists and it is decoration, above ~0.35 a Trough stops meaning 'a bad week " +
                 "to sell' and starts meaning 'do not play today', which punishes the player " +
                 "for logging in. The range is clamped for that reason rather than left open.")]
        // The ceiling is MarketCycle's own constant rather than a literal, so the slider and
        // the clamp MarketShape applies can never disagree — a slider that stops at 0.50 over a
        // clamp that allows 0.60 is a control that silently under-reports what the value can be.
        [Range(0.02f, MarketCycle.AmplitudeCeiling)] public float amplitude = 0.25f;

        [Tooltip("Shortest a full four-phase cycle may run, in in-game days. A cycle shorter " +
                 "than about four days gives each phase less than a day and the player cannot " +
                 "act on one before it has gone.")]
        [Range(4, 40)] public int minCycleDays = 6;

        [Tooltip("Longest a full four-phase cycle may run. The RANGE is what matters more than " +
                 "either end: a fixed period is a calendar, and a player who can count days is " +
                 "not reading a market. Values below minCycleDays are swapped, not refused.")]
        [Range(4, 60)] public int maxCycleDays = 14;

        // ── The coin faucet ──────────────────────────────────────────────────

        [Header("Coin faucet")]
        [Tooltip("HP per coin in the fallback reward heuristic (hp/this + power/next). Larger " +
                 "means a poorer world. Measured against the shipped roster at 40: a trash " +
                 "barbol pays 4, a knight_red 6, a dark_vampire 12, barbol_boss 52.")]
        [Min(1)] public int coinPerHp = 40;

        [Tooltip("Power per coin in the same heuristic. Separate from the HP divisor because a " +
                 "glass-cannon caster and a sack of hit points should not be worth the same.")]
        [Min(1)] public int coinPerPower = 4;

        [Tooltip("Lowest multiplier a kill's coin roll can take. Variance exists so a payout " +
                 "reads as loot rather than as a wage; at 1.0 both ends it is a wage.")]
        [Range(0.1f, 1f)] public float coinVarianceMin = 0.65f;

        [Tooltip("Highest multiplier a kill's coin roll can take. Below coinVarianceMin the " +
                 "two are swapped rather than refused, so a half-typed pair cannot invert the roll.")]
        [Range(1f, 3f)] public float coinVarianceMax = 1.35f;

        // ── The purse ────────────────────────────────────────────────────────

        [Header("Player")]
        [Tooltip("Coins a NEW character carries, when the PlayerDefinition does not say. The " +
                 "definition wins when it is positive — this is the floor for a class authored " +
                 "before the field existed, not an override.")]
        [Min(0)] public int fallbackStartingCoins = 25;

        // ── Resolution ───────────────────────────────────────────────────────

        /// <summary>
        /// The live tuning, or null when no asset ships. Cached because
        /// <c>Resources.Load</c> on a miss is not free and both callers are per-kill or
        /// per-price paths.
        ///
        /// <para>Domain Reload is OFF, so the cache is cleared on subsystem registration with
        /// a plain <c>stsfld</c> — the only reset shape <c>DomainReloadStaticResetTests</c>
        /// recognises. It also has to be cleared because a stale reference survives a
        /// recompile pointing at an asset the editor may have reimported.</para>
        /// </summary>
        private static EconomyTuning s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The shipped tuning, or a throwaway instance carrying the defaults. Never null, so a
        /// caller never has to branch — the defaults ARE the constants these fields replaced.
        /// </summary>
        public static EconomyTuning Active
        {
            get
            {
                if (!s_looked)
                {
                    s_cached = Resources.Load<EconomyTuning>(ResourcePath);
                    s_looked = true;
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<EconomyTuning>();
                    // HideAndDontSave, because this instance is never an asset: without it an
                    // EditMode run that touches the coin heuristic leaves a ScriptableObject
                    // Unity reports as leaked, on a fixture that has nothing to do with it.
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }

        /// <summary>
        /// Drops the cache so the next read re-resolves. Called by the editor after it writes
        /// the asset, and by anything that reimports it — without it a live session keeps a
        /// reference the asset database has already replaced.
        /// </summary>
        public static void InvalidateCache()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The cycle bounds in the order <c>MarketCycle</c> needs them, with an inverted pair
        /// SWAPPED rather than refused.
        ///
        /// <para>Swapping matters because these are authored in a live editor, one field at a
        /// time: dragging the minimum above the current maximum is a state every author passes
        /// THROUGH on the way to a valid pair, and a refusal there would make the second field
        /// impossible to reach. A refusal also has nowhere to report itself from inside a
        /// pure resolve.</para>
        /// </summary>
        public void ResolveCycleDays(out int min, out int max)
        {
            min = Mathf.Max(1, minCycleDays);
            max = Mathf.Max(1, maxCycleDays);
            if (min > max) { int t = min; min = max; max = t; }
        }

        /// <summary>The variance pair, with an inverted one swapped for the same reason.</summary>
        public void ResolveCoinVariance(out double min, out double max)
        {
            min = coinVarianceMin;
            max = coinVarianceMax;
            if (min > max) { double t = min; min = max; max = t; }
        }
    }
}
