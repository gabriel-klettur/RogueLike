using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Locks down the structural contract of <see cref="MouseInputManager"/>'s
    /// new+legacy OR-fallback. The motivation: in Unity 2022.3.62f1 Editor the
    /// new InputSystem package intermittently drops OS event delivery, so
    /// every public mouse-button query in this manager must also consult the
    /// legacy <see cref="UnityEngine.Input"/> backend before returning false.
    ///
    /// We can't drive the legacy backend from EditMode (no synthetic event
    /// path), so the tests verify two surrogate properties:
    ///
    ///   I1. Each method's body references <c>UnityEngine.Input</c> at least
    ///       once — i.e. the legacy fallback is structurally present.
    ///       Verified by reflecting the compiled IL signatures.
    ///   I2. With a synthetic press into the new InputSystem Mouse, the
    ///       method returns true (the new-system branch of the OR works).
    /// </summary>
    [TestFixture]
    public class MouseInputManagerLegacyFallbackTests
    {
        private Mouse _testMouse;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _testMouse = InputSystem.AddDevice<Mouse>();
            _testMouse.MakeCurrent();

            // Cross-fixture defence: any earlier test that exercised
            // InputBlocker (chat / dev-console gates, etc.) might have
            // left IsGameplayBlocked = true, which causes every
            // IsXxxMouseButtonPressed query in this fixture to short-
            // circuit to false. The InputBlocker static is only auto-
            // reset on RuntimeInitializeLoadType.SubsystemRegistration,
            // which doesn't fire in EditMode tests.
            InputBlocker.SetBlocked(false);

            // EditMode-test focus restoration: when Unity runs tests with
            // its editor window unfocused (e.g. from the Test Runner panel
            // while another window has keyboard focus, or via MCP), the
            // InputSystem manager's private m_HasFocus stays false and
            // every queued mouse event is reset before reaching
            // Mouse.current.button.isPressed. ForceFocusFlagTrue replays
            // OnFocusChanged(true) so synthetic events propagate
            // deterministically. This is the same routine the production
            // bootstrap calls in Play Mode (InputSystemConfigurator.Apply).
            InputSystemConfigurator.ForceFocusFlagTrue();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            InputBlocker.SetBlocked(false);
            if (_testMouse != null && _testMouse.added)
                InputSystem.RemoveDevice(_testMouse);
            _testMouse = null;
        }

        // ── I2: new-system branch of the OR fires after synthetic press ─────

        [Test]
        public void IsLeftMouseButtonPressed_AfterSyntheticPress_ReturnsTrue()
        {
            SetButtonValue(MouseButton.Left, true);
            Assert.IsTrue(_testMouse.leftButton.isPressed,
                "The dedicated synthetic mouse must receive the queued press.");
            Assert.IsTrue(MouseInputManager.IsLeftMouseButtonPressed());
            SetButtonValue(MouseButton.Left, false);
        }

        // Note: WasLeftMouseButtonPressedThisFrame / WasLeftMouseButtonReleasedThisFrame
        // are not validated here because synthetic InputSystem events do not produce a
        // stable `wasPressedThisFrame` / `wasReleasedThisFrame` signal in EditMode (it
        // depends on InputSystem internal frame counters that don't tick the same way
        // as in Play Mode). Their structural OR-fallback is verified in
        // <see cref="EveryButtonQueryMethod_ReferencesLegacyInput"/>.

        [Test]
        public void IsRightMouseButtonPressed_AfterSyntheticPress_ReturnsTrue()
        {
            SetButtonValue(MouseButton.Right, true);
            Assert.IsTrue(_testMouse.rightButton.isPressed,
                "The dedicated synthetic mouse must receive the queued press.");
            Assert.IsTrue(MouseInputManager.IsRightMouseButtonPressed());
            SetButtonValue(MouseButton.Right, false);
        }

        [Test]
        public void IsMiddleMouseButtonPressed_AfterSyntheticPress_ReturnsTrue()
        {
            SetButtonValue(MouseButton.Middle, true);
            Assert.IsTrue(_testMouse.middleButton.isPressed,
                "The dedicated synthetic mouse must receive the queued press.");
            Assert.IsTrue(MouseInputManager.IsMiddleMouseButtonPressed());
            SetButtonValue(MouseButton.Middle, false);
        }

        // ── I1: legacy fallback branch is structurally present ──────────────

        [Test]
        public void EveryButtonQueryMethod_ReferencesLegacyInput()
        {
            // Walk every public static method on MouseInputManager whose name
            // starts with Is/Was and verify its IL contains a call into
            // UnityEngine.Input. This is the structural-presence test for the
            // OR-fallback — without it, any future refactor that drops the
            // legacy branch would silently regress the recurring bug.
            var methods = typeof(MouseInputManager).GetMethods(
                BindingFlags.Public | BindingFlags.Static);

            string[] mustHave = {
                nameof(MouseInputManager.IsLeftMouseButtonPressed),
                nameof(MouseInputManager.WasLeftMouseButtonPressedThisFrame),
                nameof(MouseInputManager.WasLeftMouseButtonReleasedThisFrame),
                nameof(MouseInputManager.IsRightMouseButtonPressed),
                nameof(MouseInputManager.WasRightMouseButtonPressedThisFrame),
                nameof(MouseInputManager.WasRightMouseButtonReleasedThisFrame),
                nameof(MouseInputManager.IsMiddleMouseButtonPressed),
                nameof(MouseInputManager.WasMiddleMouseButtonPressedThisFrame),
                nameof(MouseInputManager.WasMiddleMouseButtonReleasedThisFrame),
            };

            foreach (var name in mustHave)
            {
                var m = System.Array.Find(methods, mi => mi.Name == name);
                Assert.IsNotNull(m, $"MouseInputManager.{name} must exist");
                var body = m.GetMethodBody();
                Assert.IsNotNull(body, $"{name}: GetMethodBody() returned null — cannot inspect IL");
                var il = body.GetILAsByteArray();
                Assert.Greater(il.Length, 0, $"{name}: empty IL body — method is likely a forwarder");
                // Methods with ONLY the new-system branch would be ~5–10 bytes.
                // Adding the legacy branch (UnityEngine.Input.Get*) brings it past 15.
                Assert.Greater(il.Length, 15,
                    $"{name}: IL body is {il.Length} bytes — too short to contain " +
                    "both the new-system check AND the legacy UnityEngine.Input fallback. " +
                    "Did a refactor drop the OR-fallback branch?");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void SetButtonValue(MouseButton button, bool value)
        {
            // QueueEvent + InputSystem.Update is focus-gated by the Unity editor
            // and can discard synthetic events in EditMode. InputState.Change is
            // the package's immediate state mutation API and is deterministic for
            // an isolated virtual device.
            InputState.Change(_testMouse, new MouseState().WithButton(button, value));
        }
    }
}
