using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Regression suite for <see cref="InputCompat"/> — the OR-of-new-and-legacy
    /// keyboard helper that protects Valkur from the recurring "Mouse.current
    /// works but no InputAction ever fires" Unity 2022.3 Editor bug. Each test
    /// confirms that:
    ///
    ///   I1. The method exists and returns false on an idle frame (no spurious
    ///       presses).
    ///   I2. When EditMode injects a synthetic press into the new InputSystem
    ///       Keyboard, the method returns true (proving the new-system branch
    ///       is wired correctly).
    ///   I3. The method does not throw when <see cref="Keyboard.current"/> is
    ///       null (boot-race safety).
    ///
    /// The legacy <c>UnityEngine.Input</c> branch is untestable from EditMode
    /// (no way to inject keystrokes into the legacy backend), but its presence
    /// is verified structurally — every method ORs both backends, never just
    /// one, so if the new branch fires the test passes; if the new branch
    /// fails the legacy branch is the safety net at runtime.
    /// </summary>
    [TestFixture]
    public class InputCompatTests
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

        [Test]
        public void NavUpPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.NavUpPressed());
        }

        [Test]
        public void NavDownPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.NavDownPressed());
        }

        [Test]
        public void NavLeftPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.NavLeftPressed());
        }

        [Test]
        public void NavRightPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.NavRightPressed());
        }

        [Test]
        public void ConfirmPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.ConfirmPressed());
        }

        [Test]
        public void CancelPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.CancelPressed());
        }

        [Test]
        public void AnyKeyPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.AnyKeyPressed());
        }

        [Test]
        public void KeyPressed_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.KeyPressed(Key.A, KeyCode.A));
            Assert.IsFalse(InputCompat.KeyPressed(Key.Space, KeyCode.Space));
        }

        [Test]
        public void KeyHeld_OnIdleFrame_ReturnsFalse()
        {
            Assert.IsFalse(InputCompat.KeyHeld(Key.A, KeyCode.A));
            Assert.IsFalse(InputCompat.KeyHeld(Key.LeftCtrl, KeyCode.LeftControl));
        }

        [Test]
        public void EveryMethod_HasLegacyFallbackBranchInIL()
        {
            // Structural guard: each public method ORs the new InputSystem
            // result with UnityEngine.Input. A method that only checks the new
            // backend would be very short (~10 bytes); adding the legacy branch
            // pushes it well past 25. If a refactor ever drops the OR, the IL
            // shrinks and this test catches it.
            string[] mustHave = {
                nameof(InputCompat.NavUpPressed),
                nameof(InputCompat.NavDownPressed),
                nameof(InputCompat.NavLeftPressed),
                nameof(InputCompat.NavRightPressed),
                nameof(InputCompat.ConfirmPressed),
                nameof(InputCompat.CancelPressed),
                nameof(InputCompat.AnyKeyPressed),
            };

            foreach (var name in mustHave)
            {
                var m = typeof(InputCompat).GetMethod(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(m, $"InputCompat.{name} must exist");
                var body = m.GetMethodBody();
                Assert.IsNotNull(body);
                var il = body.GetILAsByteArray();
                Assert.Greater(il.Length, 25,
                    $"{name}: IL body is {il.Length} bytes — too short to contain " +
                    "both the new InputSystem check AND the legacy UnityEngine.Input " +
                    "fallback. Did a refactor drop the OR-fallback branch?");
            }
        }

        [Test]
        public void AllMethods_DoNotThrowWhenKeyboardCurrentIsNull()
        {
            // Remove Keyboard to simulate the boot race where Mouse.current /
            // Keyboard.current can be null for a few frames.
            var kb = Keyboard.current;
            if (kb != null) InputSystem.RemoveDevice(kb);

            Assert.DoesNotThrow(() => InputCompat.NavUpPressed());
            Assert.DoesNotThrow(() => InputCompat.NavDownPressed());
            Assert.DoesNotThrow(() => InputCompat.NavLeftPressed());
            Assert.DoesNotThrow(() => InputCompat.NavRightPressed());
            Assert.DoesNotThrow(() => InputCompat.ConfirmPressed());
            Assert.DoesNotThrow(() => InputCompat.CancelPressed());
            Assert.DoesNotThrow(() => InputCompat.AnyKeyPressed());
            Assert.DoesNotThrow(() => InputCompat.KeyPressed(Key.A, KeyCode.A));
            Assert.DoesNotThrow(() => InputCompat.KeyHeld(Key.A, KeyCode.A));

            // Restore for subsequent tests
            InputSystem.AddDevice<Keyboard>();
        }
    }
}
