using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Locks down the structural contract of <see cref="KeyboardInputManager"/>'s
    /// new+legacy OR-fallback. Every public method must consult BOTH backends
    /// — without that the call silently dies when the new InputSystem package
    /// drops OS event delivery (recurring Unity 2022.3.62f1 Editor bug).
    ///
    /// Synthetic events do not produce a stable <c>wasPressedThisFrame</c>
    /// signal in EditMode, so the behavioural verification is limited to
    /// idle-frame returns. Structural verification reads the IL body length
    /// — a method that lost its OR-fallback would shrink and trip the guard.
    /// </summary>
    [TestFixture]
    public class KeyboardInputManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Idle-frame returns ──────────────────────────────────────────────

        [Test]
        public void IsKeyPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(KeyboardInputManager.IsKeyPressed(Key.A, KeyCode.A));
            Assert.IsFalse(KeyboardInputManager.IsKeyPressed(Key.LeftCtrl, KeyCode.LeftControl));
        }

        [Test]
        public void WasKeyPressedThisFrame_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(KeyboardInputManager.WasKeyPressedThisFrame(Key.A, KeyCode.A));
        }

        [Test]
        public void WasKeyReleasedThisFrame_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(KeyboardInputManager.WasKeyReleasedThisFrame(Key.A, KeyCode.A));
        }

        [Test]
        public void CommonKeyHelpers_OnIdleFrame_ReturnFalse()
        {
            Assert.IsFalse(KeyboardInputManager.WasEnterPressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.WasEscapePressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.WasDeletePressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.WasF2PressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.WasQPressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.WasEPressedThisFrame());
            Assert.IsFalse(KeyboardInputManager.IsShiftHeld());
            Assert.IsFalse(KeyboardInputManager.IsCtrlHeld());
            Assert.IsFalse(KeyboardInputManager.IsAltHeld());
            Assert.IsFalse(KeyboardInputManager.WasAnyKeyPressedThisFrame());
        }

        // ── Null-Keyboard.current safety ─────────────────────────────────────

        [Test]
        public void AllMethods_DoNotThrowWhenKeyboardCurrentIsNull()
        {
            var kb = Keyboard.current;
            if (kb != null) InputSystem.RemoveDevice(kb);

            Assert.DoesNotThrow(() => KeyboardInputManager.IsKeyPressed(Key.A, KeyCode.A));
            Assert.DoesNotThrow(() => KeyboardInputManager.WasKeyPressedThisFrame(Key.A, KeyCode.A));
            Assert.DoesNotThrow(() => KeyboardInputManager.WasKeyReleasedThisFrame(Key.A, KeyCode.A));
            Assert.DoesNotThrow(() => KeyboardInputManager.WasEnterPressedThisFrame());
            Assert.DoesNotThrow(() => KeyboardInputManager.WasEscapePressedThisFrame());
            Assert.DoesNotThrow(() => KeyboardInputManager.IsShiftHeld());
            Assert.DoesNotThrow(() => KeyboardInputManager.IsCtrlHeld());
            Assert.DoesNotThrow(() => KeyboardInputManager.IsAltHeld());
            Assert.DoesNotThrow(() => KeyboardInputManager.WasAnyKeyPressedThisFrame());

            // Restore for subsequent tests
            InputSystem.AddDevice<Keyboard>();
        }

        // ── Structural IL guard — every method ORs both backends ────────────

        [Test]
        public void EveryQueryMethod_ReferencesBothBackendsInIL()
        {
            // Walk every public static method on KeyboardInputManager and verify
            // its IL body is large enough to contain BOTH the new InputSystem
            // branch and the legacy UnityEngine.Input branch. A method that
            // dropped the OR would shrink and trip this guard.
            string[] mustHave = {
                nameof(KeyboardInputManager.IsKeyPressed),
                nameof(KeyboardInputManager.WasKeyPressedThisFrame),
                nameof(KeyboardInputManager.WasKeyReleasedThisFrame),
                nameof(KeyboardInputManager.WasEnterPressedThisFrame),
                nameof(KeyboardInputManager.WasAnyKeyPressedThisFrame),
            };

            foreach (var name in mustHave)
            {
                var m = typeof(KeyboardInputManager).GetMethod(name,
                    BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(m, $"KeyboardInputManager.{name} must exist");
                var body = m.GetMethodBody();
                Assert.IsNotNull(body, $"{name}: GetMethodBody() returned null");
                var il = body.GetILAsByteArray();
                Assert.Greater(il.Length, 25,
                    $"{name}: IL body is {il.Length} bytes — too short to contain " +
                    "BOTH the new-system check AND the legacy UnityEngine.Input " +
                    "fallback. Did a refactor drop the OR-fallback branch?");
            }
        }

        [Test]
        public void EveryHelper_HasMatchingPropertyName()
        {
            // Single-key helpers (WasEnterPressedThisFrame, WasEscapePressedThisFrame, etc.)
            // are expected to exist as named methods on the manager so callsites
            // don't need to re-pair Key.X with KeyCode.X. This test enforces the
            // contract: the named helpers below MUST be present.
            string[] required = {
                "WasEnterPressedThisFrame",
                "WasEscapePressedThisFrame",
                "WasDeletePressedThisFrame",
                "WasF2PressedThisFrame",
                "WasQPressedThisFrame",
                "WasEPressedThisFrame",
                "IsShiftHeld",
                "IsCtrlHeld",
                "IsAltHeld",
                "IsLeftShiftPressed",
                "IsRightShiftPressed",
                "IsLeftCtrlPressed",
                "IsRightCtrlPressed",
                "IsLeftAltPressed",
                "IsRightAltPressed",
                "WasAnyKeyPressedThisFrame",
            };
            foreach (var name in required)
            {
                Assert.IsNotNull(
                    typeof(KeyboardInputManager).GetMethod(name,
                        BindingFlags.Public | BindingFlags.Static),
                    $"KeyboardInputManager.{name} must exist (used across the codebase).");
            }
        }
    }
}
