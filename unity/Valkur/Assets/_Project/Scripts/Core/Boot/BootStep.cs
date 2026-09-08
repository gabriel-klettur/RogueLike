using System;
using System.Collections;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// One entry of the gameplay boot sequence, as DATA.
    ///
    /// The sequence used to be a 250-line coroutine of
    /// <c>EnsureX(); Report("…"); yield return null;</c> triplets, and three
    /// separate things went wrong because of it:
    ///
    /// <list type="bullet">
    ///   <item>the step COUNT lived in a hand-maintained constant
    ///   (<c>SetupStepTotal = 53</c>) that drifted to 53-against-70, so the bar
    ///   reached 100 % at 75.7 % of the work;</item>
    ///   <item>50 of the 55 calls carried no <c>try/catch</c>, so one throw killed
    ///   the coroutine and the player landed in a half-built world in silence;</item>
    ///   <item>the ORDER — which carries half a dozen documented, load-bearing
    ///   constraints — was invisible to every test but one, which greps the source
    ///   text for two method names.</item>
    /// </list>
    ///
    /// A list of these fixes all three at once: the total is <c>list.Count</c>,
    /// the runner owns the exception boundary, and the order is a value a test
    /// can walk.
    /// </summary>
    public sealed class BootStep
    {
        /// <summary>
        /// Player-visible label. Empty means a SILENT step — it still runs and is
        /// still timed, it just does not advance the bar or change the text. Used
        /// for the two manager installs that must precede everything and for the
        /// pure-data blocks that were never reported before either.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Relative cost. Only meaningful as a ratio against the other steps'
        /// weights; <see cref="BootTimeline"/> overwrites it from the previous
        /// boot's measured milliseconds when a profile exists, so a fresh install
        /// runs on these estimates once and on measurements after that.
        /// </summary>
        public float Weight { get; }

        /// <summary>
        /// When true the runner yields a frame after this step, unconditionally.
        /// When false it yields only if the frame budget is already spent.
        ///
        /// The distinction is not cosmetic. A step that creates a GameObject the
        /// NEXT step queries needs the frame, because Unity runs <c>Start</c> on
        /// the frame after the object is enabled — the old code's frame-per-step
        /// was accidentally load-bearing. Steps that only <c>AddComponent</c> a
        /// self-contained manager and register it are independent of one another
        /// and are the ones marked false.
        /// </summary>
        public bool Barrier { get; }

        /// <summary>Synchronous body. Mutually exclusive with <see cref="Progressive"/>.</summary>
        public Action Run { get; }

        /// <summary>Coroutine body, pumped by the runner with the same exception boundary.</summary>
        public Func<IEnumerator> Progressive { get; }

        /// <summary>
        /// How many sub-stages this step reports from inside its own body. The world
        /// load, the player spawn and the building load each narrate themselves, and
        /// without this their labels would change while the bar stood still for
        /// several seconds — which is precisely the freeze those coroutines were
        /// written to remove. Declared rather than counted because the bar has to
        /// know the denominator before the first sub-stage arrives; over-reporting is
        /// clamped to the step's own share, so the number can only ever be an
        /// under-estimate of smoothness, never a source of overshoot.
        /// </summary>
        public int SubStages { get; }

        private BootStep(string label, float weight, bool barrier, Action run,
                         Func<IEnumerator> progressive, int subStages)
        {
            Label = label ?? string.Empty;
            Weight = weight > 0f ? weight : 1f;
            Barrier = barrier;
            Run = run;
            Progressive = progressive;
            SubStages = subStages > 0 ? subStages : 0;
        }

        /// <summary>A reported step that runs synchronously.</summary>
        public static BootStep Of(string label, Action run, float weight = 1f, bool barrier = true)
            => new BootStep(label, weight, barrier, run, null, 0);

        /// <summary>A reported step whose body is a coroutine reporting its own sub-stages.</summary>
        public static BootStep Coroutine(string label, Func<IEnumerator> body, float weight = 1f, int subStages = 0)
            => new BootStep(label, weight, true, null, body, subStages);

        /// <summary>
        /// A step that does work but says nothing. Still timed, still guarded,
        /// still ordered — it simply contributes no label to the bar.
        /// </summary>
        public static BootStep Silent(Action run, float weight = 0.2f, bool barrier = false)
            => new BootStep(string.Empty, weight, barrier, run, null, 0);

        public bool IsReported => !string.IsNullOrEmpty(Label);
    }
}
