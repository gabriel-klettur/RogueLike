using UnityEngine;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// "Have I held this frame long enough?" — the one answer shared by every loader
    /// that streams its work across frames.
    ///
    /// <para><b>A yield is not free, and on a boot frame it is expensive.</b> Measured
    /// live: a steady-state frame in this game costs 9.8 ms (102 fps), while a frame
    /// during the arranque costs <b>29 ms warm and about 106 ms cold</b> — the scene has
    /// just activated, so Unity is still paging atlases, compiling shader variants and
    /// syncing thousands of colliders in those frames. Every extra yield buys one of
    /// those.</para>
    ///
    /// <para><b>Which is why a COUNT-based cadence rots.</b> The building loader yielded
    /// once per 60 instances, tuned when an instance cost ~22 ms — a sensible 1.3 s of
    /// work per frame. After the <c>SpriteMeshType.FullRect</c> fix an instance costs
    /// 0.4 ms, so the same rule spent 8 frames (over 200 ms warm) spreading 118 ms of
    /// work. The number did not change; what it was protecting against disappeared, and
    /// nothing said so. A time budget cannot rot that way: it asks the question the
    /// cadence was always standing in for.</para>
    ///
    /// <para>Usage is deliberately a struct with no allocation:
    /// <code>
    /// var budget = LoadFrameBudget.Start();
    /// foreach (var item in items)
    /// {
    ///     DoWork(item);
    ///     if (budget.Spent) { yield return null; budget.Restart(); }
    /// }
    /// </code></para>
    /// </summary>
    public struct LoadFrameBudget
    {
        /// <summary>
        /// How long a loader may hold a frame before giving it up.
        ///
        /// <para>250 ms is deliberately generous, and the number came out of measuring
        /// rather than taste. A first pass used 12 ms — a normal frame-budget figure — and
        /// made things WORSE than the count it replaced, because with the work now cheap
        /// a small budget simply yields more often, and a yield during the arranque buys
        /// a frame that costs far more than the work it interleaves. Raising it to 250 ms
        /// took the boot from 61 frames to 45.</para>
        ///
        /// <para>What that experiment also settled is that this is NOT a big lever: the
        /// same 16 frames were worth only 122 ms, and the time spent outside the steps
        /// did not move at all (1 838 ms against 1 832 ms). The cost of a boot frame is
        /// dominated by work Unity does because the scene was just activated, not by the
        /// yield itself. The budget is kept because it is measurably better and cannot
        /// rot the way a count does — not because it is where the remaining time is.</para>
        ///
        /// <para>The screen still repaints about five times during the world load and
        /// three times during the building spawn, which is what the yields are for.</para>
        /// </summary>
        public const float DefaultBudgetMs = 250f;

        private float _startedAt;
        private float _budgetMs;

        public static LoadFrameBudget Start(float budgetMs = DefaultBudgetMs)
        {
            return new LoadFrameBudget
            {
                _startedAt = Time.realtimeSinceStartup,
                _budgetMs = budgetMs > 0f ? budgetMs : DefaultBudgetMs,
            };
        }

        /// <summary>True once this frame has been held long enough to be worth ending.</summary>
        public bool Spent => (Time.realtimeSinceStartup - _startedAt) * 1000f >= _budgetMs;

        /// <summary>Call immediately after the <c>yield return null</c>.</summary>
        public void Restart() => _startedAt = Time.realtimeSinceStartup;
    }
}
