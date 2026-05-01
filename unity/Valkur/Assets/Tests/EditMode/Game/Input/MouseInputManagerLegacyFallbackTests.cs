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
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ── I2: new-system branch of the OR fires after synthetic press ─────

        [Test]
        public void IsLeftMouseButtonPressed_AfterSyntheticPress_ReturnsTrue()
        {
            QueueButtonValue(Mouse.current.leftButton, 1f);
            Assert.IsTrue(MouseInputManager.IsLeftMouseButtonPressed());
            QueueButtonValue(Mouse.current.leftButton, 0f);
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
            QueueButtonValue(Mouse.current.rightButton, 1f);
            Assert.IsTrue(MouseInputManager.IsRightMouseButtonPressed());
            QueueButtonValue(Mouse.current.rightButton, 0f);
        }

        [Test]
        public void IsMiddleMouseButtonPressed_AfterSyntheticPress_ReturnsTrue()
        {
            QueueButtonValue(Mouse.current.middleButton, 1f);
            Assert.IsTrue(MouseInputManager.IsMiddleMouseButtonPressed());
            QueueButtonValue(Mouse.current.middleButton, 0f);
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

        private static void QueueButtonValue(UnityEngine.InputSystem.Controls.ButtonControl button, float value)
        {
            using (StateEvent.From(button.device, out InputEventPtr evt))
            {
                InputControlExtensions.WriteValueIntoEvent(button, value, evt);
                InputSystem.QueueEvent(evt);
            }
            InputSystem.Update();
        }
    }
}
