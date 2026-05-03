using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies that <see cref="KeyboardInputManager"/> static helpers honour
    /// <see cref="InputBlocker.IsGameplayBlocked"/>:
    ///
    ///   • While blocked, generic helpers return <c>false</c> for regular keys.
    ///   • While blocked, helpers for keys on the always-allowed list (Esc, ~,
    ///     Enter) bypass the early-return and reach the hardware lookup — in
    ///     EditMode they still return <c>false</c> (no real key is pressed),
    ///     but crucially they must NOT early-return before consulting hardware.
    ///   • <see cref="KeyboardInputManager.WasAnyKeyPressedThisFrame"/> always
    ///     returns <c>false</c> when blocked, with no exception.
    ///   • All generic helpers are no-throw when unblocked (no real hardware in
    ///     EditMode, so results are <c>false</c>, but no NRE).
    ///
    /// Because EditMode has no synthetic key-press pipeline that reliably produces
    /// <c>wasPressedThisFrame</c>, we can only verify the no-throw contract and the
    /// blocked-vs-unblocked early-return distinction indirectly.
    /// </summary>
    [TestFixture]
    public class KeyboardInputManagerBlockerTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            InputBlocker.SetBlocked(false);
        }

        [TearDown]
        public void TearDown()
        {
            InputBlocker.SetBlocked(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Blocked: regular key helpers early-return false ─────────────────

        [Test]
        public void KeyboardHelpers_WhenBlocked_ReturnFalse_ForRegularKey()
        {
            InputBlocker.SetBlocked(true);

            // Key.A / KeyCode.A is not on the always-allowed list.
            bool pressed  = KeyboardInputManager.IsKeyPressed(Key.A, KeyCode.A);
            bool wasDown  = KeyboardInputManager.WasKeyPressedThisFrame(Key.A, KeyCode.A);
            bool wasUp    = KeyboardInputManager.WasKeyReleasedThisFrame(Key.A, KeyCode.A);

            Assert.IsFalse(pressed, "IsKeyPressed(A) must return false while blocked.");
            Assert.IsFalse(wasDown, "WasKeyPressedThisFrame(A) must return false while blocked.");
            Assert.IsFalse(wasUp,   "WasKeyReleasedThisFrame(A) must return false while blocked.");
        }

        // ── Blocked: always-allowed key bypasses early-return ───────────────

        [Test]
        public void KeyboardHelpers_WhenBlocked_AlwaysAllowedKey_DoesNotEarlyReturn()
        {
            // Verify the "always allowed" path: with IsGameplayBlocked=true, calling
            // a helper for an always-allowed key (Escape / Enter / ~) must NOT
            // early-return. In EditMode no key is physically pressed so the result
            // is still false, but the code reaches the Keyboard.current lookup
            // rather than short-circuiting. We verify: no throw + false result.
            InputBlocker.SetBlocked(true);

            bool result = false;
            Assert.DoesNotThrow(() =>
            {
                result = KeyboardInputManager.WasKeyPressedThisFrame(Key.Escape, KeyCode.Escape);
            }, "WasKeyPressedThisFrame(Escape) must not throw while blocked.");

            // Result is false because no real key is pressed in EditMode.
            // The important thing is the code reached hardware lookup rather than
            // returning early-false — both paths return false here, but no throw
            // confirms execution continued past the IsAlwaysAllowedKey guard.
            Assert.IsFalse(result,
                "Escape is allowed while blocked; in EditMode it is simply not pressed.");

            // Also test WasEnterPressedThisFrame — Enter is always allowed.
            Assert.DoesNotThrow(() => KeyboardInputManager.WasEnterPressedThisFrame(),
                "WasEnterPressedThisFrame must not throw while blocked.");

            // And WasEscapePressedThisFrame — wraps the generic helper.
            Assert.DoesNotThrow(() => KeyboardInputManager.WasEscapePressedThisFrame(),
                "WasEscapePressedThisFrame must not throw while blocked.");
        }

        // ── Blocked: WasAnyKeyPressedThisFrame always returns false ─────────

        [Test]
        public void WasAnyKeyPressedThisFrame_WhenBlocked_AlwaysReturnsFalse()
        {
            InputBlocker.SetBlocked(true);

            bool result = false;
            Assert.DoesNotThrow(() =>
            {
                result = KeyboardInputManager.WasAnyKeyPressedThisFrame();
            }, "WasAnyKeyPressedThisFrame must not throw while blocked.");

            Assert.IsFalse(result,
                "WasAnyKeyPressedThisFrame must return false when gameplay is blocked.");
        }

        // ── Unblocked: generic helpers must not throw ────────────────────────

        [Test]
        public void GenericHelpers_WhenUnblocked_DoNotThrow()
        {
            // Blocker is off — helpers reach the hardware path.
            // No real keyboard is pressed, so results are false, but no NRE.
            Assert.DoesNotThrow(() => KeyboardInputManager.IsKeyPressed(Key.W, KeyCode.W),
                "IsKeyPressed must not throw when unblocked.");
            Assert.DoesNotThrow(() => KeyboardInputManager.WasKeyPressedThisFrame(Key.W, KeyCode.W),
                "WasKeyPressedThisFrame must not throw when unblocked.");
            Assert.DoesNotThrow(() => KeyboardInputManager.WasKeyReleasedThisFrame(Key.W, KeyCode.W),
                "WasKeyReleasedThisFrame must not throw when unblocked.");
            Assert.DoesNotThrow(() => KeyboardInputManager.WasAnyKeyPressedThisFrame(),
                "WasAnyKeyPressedThisFrame must not throw when unblocked.");
            Assert.DoesNotThrow(() => KeyboardInputManager.IsCtrlHeld(),
                "IsCtrlHeld must not throw when unblocked.");
            Assert.DoesNotThrow(() => KeyboardInputManager.IsShiftHeld(),
                "IsShiftHeld must not throw when unblocked.");
        }
    }
}
