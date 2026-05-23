using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Valkur.UIKit;

// Assembly-level registration so every EditMode test in this assembly gets
// the BeforeTest hook for free — no opt-in / no inheritance required from
// individual fixtures. NUnit fans BeforeTest out to each test in turn when
// the attribute targets ActionTargets.Test.
[assembly: Valkur.Tests.EditMode.Infrastructure.SelectableResetTestAction]

namespace Valkur.Tests.EditMode.Infrastructure
{
    /// <summary>
    /// Grows UGUI's <c>Selectable</c> static array (via
    /// <see cref="SelectableArrayPreGrow.EnsureCapacity(int)"/>) to a generous
    /// capacity before every EditMode test in this assembly. Prevents the
    /// recurring <c>IndexOutOfRangeException</c> cascade in
    /// <c>Selectable.OnEnable()</c>.
    ///
    /// ── Why this exists ────────────────────────────────────────────────────
    /// The runtime workaround in <see cref="SelectableArrayPreGrow"/> only
    /// fires at <see cref="UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration"/>
    /// — i.e. Play Mode start. EditMode tests live entirely inside the editor's
    /// scripting domain and never hit that hook, so UGUI's
    /// <c>s_Selectables</c> array starts at Unity's default capacity (10) and
    /// fills up as test fixtures spin up Scrollbars / Buttons / Toggles.
    /// Drift compounds: a failed <c>OnEnable</c> leaves <c>m_EnableCalled</c>
    /// false, so the matching <c>OnDisable</c> skips its decrement when the
    /// test's TearDown destroys the GameObject. The static count drifts
    /// upward, and every subsequent test in the session inherits the broken
    /// state — a cascade that knocks out dozens to hundreds of tests for a
    /// single root cause.
    ///
    /// ── How the fix works ──────────────────────────────────────────────────
    /// <see cref="BeforeTest"/> calls
    /// <see cref="SelectableArrayPreGrow.EnsureCapacity(int)"/> with
    /// <see cref="TestSessionCapacity"/> slots before every test. The capacity
    /// is intentionally far above the realistic drift budget for an entire
    /// 4000-test EditMode session, so UGUI's strict-equality grow-check in
    /// <c>Selectable.OnEnable</c> never has a chance to be tripped.
    ///
    /// We deliberately use <see cref="SelectableArrayPreGrow.EnsureCapacity"/>
    /// (which only grows the array) instead of
    /// <see cref="SelectableArrayPreGrow.Reset"/> (which zeroes the count).
    /// Reset is unsafe between EditMode tests because the editor's permanent
    /// UI (Inspector, Project window, the Test Runner itself) keeps
    /// Selectables registered against the live array. Zeroing the count
    /// orphans them — their next <c>OnDisable</c> underflows, which then
    /// surfaces as IOoR cascade failures in unrelated subsequent tests. The
    /// grow-only EnsureCapacity preserves every existing reference.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = true)]
    public sealed class SelectableResetTestActionAttribute : Attribute, ITestAction
    {
        // Capacity sized for "every EditMode test in the project running back
        // to back without the array ever filling up". The full suite (~4000
        // tests today) typically registers a few thousand Selectables across
        // the session; 16384 gives 4x headroom against drift and against
        // single-test fixtures that build very large UIs (TableColumnsConfig,
        // Items editor with item rows, scrollbar fixtures, etc.).
        private const int TestSessionCapacity = 16384;

        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test)
            => SelectableArrayPreGrow.EnsureCapacity(TestSessionCapacity);

        public void AfterTest(ITest test) { }
    }
}
