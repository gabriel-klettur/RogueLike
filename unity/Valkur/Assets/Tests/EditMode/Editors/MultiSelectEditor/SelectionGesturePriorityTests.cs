using System;
using System.Reflection;
using NUnit.Framework;

namespace Valkur.Tests.EditMode.Editors.MultiSelectEditor
{
    /// <summary>
    /// The gesture-priority rule of the cross-domain Selection tool: WHAT a press arms, before
    /// the author has said which gesture they meant.
    ///
    /// <para><b>The defect this pins.</b> The tool used to decide at the PRESS — pressing
    /// anything unselected cleared the selection, selected that one thing and started moving
    /// it. A drag-select could therefore only ever BEGIN on empty ground, and empty ground is
    /// exactly what a dense scene has none of. Measured on the shipped world, on the cluster
    /// the tool exists for: <b>1.6 % of starting corners could begin a box</b> in an 8x8 area,
    /// 4.2 % in 16x16, 6.4 % in 24x24. After the change, 100 % in all three.</para>
    ///
    /// <para>Reached by reflection: the rule is <c>internal</c> to <c>Valkur.Gameplay</c>, and
    /// an <c>InternalsVisibleTo</c> would open the whole assembly to the test one for a single
    /// static method.</para>
    /// </summary>
    [TestFixture]
    public class SelectionGesturePriorityTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static Type EditorType =>
            Type.GetType("Valkur.Gameplay.Editors.MultiSelect.MultiSelectRuntimeEditor, Valkur.Gameplay");

        private static string Resolve(bool hit, bool selected, bool ctrl)
        {
            var m = EditorType.GetMethod("ResolvePress", Any);
            Assert.NotNull(m, "ResolvePress not found — the gesture rule must stay one testable function.");
            return m.Invoke(null, new object[] { hit, selected, ctrl }).ToString();
        }

        [SetUp]
        public void SetUp() => Assert.NotNull(EditorType, "MultiSelectRuntimeEditor not found.");

        // ── The reported defect ──────────────────────────────────────────────

        [Test]
        public void PressingAnUnselectedObject_ArmsAMarquee_NotAMove()
        {
            Assert.AreEqual("PendingMarquee", Resolve(hit: true, selected: false, ctrl: false),
                "THE defect. A drag has to be able to START over a building, an emitter or a " +
                "light — otherwise the box is unreachable in exactly the dense places it is " +
                "for. Measured before the fix: 1.6 % of an 8x8 cluster could start one.");
        }

        [Test]
        public void PressingEmptyGround_ArmsAMarquee()
        {
            Assert.AreEqual("PendingMarquee", Resolve(hit: false, selected: false, ctrl: false));
        }

        // ── What still claims the drag ───────────────────────────────────────

        [Test]
        public void PressingSomethingAlreadySelected_ArmsAMove()
        {
            Assert.AreEqual("PendingMove", Resolve(hit: true, selected: true, ctrl: false),
                "The one case where the author has already said which objects they mean. " +
                "Without it there would be no way to move a group at all.");
        }

        // ── Ctrl ─────────────────────────────────────────────────────────────

        [Test]
        public void CtrlOnAnObject_ArmsNothing_BecauseTheClickIsAlreadyComplete()
        {
            Assert.AreEqual("None", Resolve(hit: true, selected: false, ctrl: true));
            Assert.AreEqual("None", Resolve(hit: true, selected: true,  ctrl: true),
                "Ctrl on a MEMBER removes it; it must not arm a move of the group it just left.");
        }

        [Test]
        public void CtrlOnEmptyGround_StillArmsAMarquee_SoAnAdditiveBoxIsReachable()
        {
            Assert.AreEqual("PendingMarquee", Resolve(hit: false, selected: false, ctrl: true),
                "Ctrl means ADD for both a click and a box. Arming nothing here would leave " +
                "the additive marquee with no gesture that reaches it.");
        }

        // ── The rule as a whole ──────────────────────────────────────────────

        [Test]
        public void OnlyAnAlreadySelectedObject_EverClaimsTheDrag()
        {
            foreach (bool hit in new[] { false, true })
            foreach (bool sel in new[] { false, true })
            foreach (bool ctrl in new[] { false, true })
            {
                string g = Resolve(hit, sel, ctrl);
                bool isMove = g == "PendingMove";
                bool shouldBeMove = hit && sel && !ctrl;
                Assert.AreEqual(shouldBeMove, isMove,
                    $"hit={hit} selected={sel} ctrl={ctrl} resolved to {g}. A move may be armed " +
                    "ONLY by pressing something already in the group without Ctrl; every other " +
                    "press has to leave the box reachable.");
            }
        }

        [Test]
        public void NoPress_IsEverIgnored_UnlessItIsACtrlClick()
        {
            // "None" means the press did its whole job immediately. Any other combination must
            // arm something, or the button-down would be swallowed with nothing to release.
            foreach (bool hit in new[] { false, true })
            foreach (bool sel in new[] { false, true })
            foreach (bool ctrl in new[] { false, true })
            {
                string g = Resolve(hit, sel, ctrl);
                if (g != "None") continue;
                Assert.IsTrue(hit && ctrl,
                    $"hit={hit} selected={sel} ctrl={ctrl} armed nothing, but it is not a " +
                    "Ctrl+click — the press would be silently dropped.");
            }
        }
    }
}
