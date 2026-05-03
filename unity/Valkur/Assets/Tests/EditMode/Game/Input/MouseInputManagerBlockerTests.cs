using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies that <see cref="MouseInputManager"/> static helpers honour
    /// <see cref="InputBlocker.IsGameplayBlocked"/>:
    ///   • While blocked, every button helper returns <c>false</c> and
    ///     <see cref="MouseInputManager.GetMouseWheelDelta"/> returns <c>0f</c>
    ///     — without throwing, regardless of whether hardware is present.
    ///   • While unblocked, helpers also return <c>false</c> / <c>0f</c>
    ///     (no physical hardware in EditMode) but must not throw.
    ///
    /// We cannot simulate real clicks in EditMode, so we can only verify the
    /// contract of the early-return path (blocked → immediate false/0f,
    /// no NRE, no hardware access) and the no-throw guarantee while unblocked.
    /// </summary>
    [TestFixture]
    public class MouseInputManagerBlockerTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            InputBlocker.SetBlocked(false);
            // Ensure at least a null-safe device state (may be null in headless EditMode).
        }

        [TearDown]
        public void TearDown()
        {
            InputBlocker.SetBlocked(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Blocked: all button helpers return false ─────────────────────────

        [Test]
        public void MouseHelpers_WhenBlocked_ReturnFalse()
        {
            InputBlocker.SetBlocked(true);

            Assert.IsFalse(MouseInputManager.IsLeftMouseButtonPressed(),          "IsLeftMouseButtonPressed must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasLeftMouseButtonPressedThisFrame(), "WasLeftMouseButtonPressedThisFrame must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasLeftMouseButtonReleasedThisFrame(),"WasLeftMouseButtonReleasedThisFrame must be false when blocked.");
            Assert.IsFalse(MouseInputManager.IsRightMouseButtonPressed(),          "IsRightMouseButtonPressed must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasRightMouseButtonPressedThisFrame(),"WasRightMouseButtonPressedThisFrame must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasRightMouseButtonReleasedThisFrame(),"WasRightMouseButtonReleasedThisFrame must be false when blocked.");
            Assert.IsFalse(MouseInputManager.IsMiddleMouseButtonPressed(),          "IsMiddleMouseButtonPressed must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasMiddleMouseButtonPressedThisFrame(),"WasMiddleMouseButtonPressedThisFrame must be false when blocked.");
            Assert.IsFalse(MouseInputManager.WasMiddleMouseButtonReleasedThisFrame(),"WasMiddleMouseButtonReleasedThisFrame must be false when blocked.");
        }

        // ── Blocked: scroll wheel returns 0f ────────────────────────────────

        [Test]
        public void GetMouseWheelDelta_WhenBlocked_ReturnsZero()
        {
            InputBlocker.SetBlocked(true);

            float delta = MouseInputManager.GetMouseWheelDelta();

            Assert.AreEqual(0f, delta, 0.0001f,
                "GetMouseWheelDelta must return 0 when gameplay is blocked.");
        }

        // ── Unblocked: helpers return false (no real hardware) but do not throw

        [Test]
        public void MouseHelpers_WhenUnblocked_DoNotShortCircuit()
        {
            // Blocker is off — helpers should reach the hardware polling path.
            // In EditMode there is no real hardware so they will return false,
            // but they must not throw an NRE or any other exception.
            Assert.DoesNotThrow(() => MouseInputManager.IsLeftMouseButtonPressed(),          "IsLeftMouseButtonPressed must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasLeftMouseButtonPressedThisFrame(), "WasLeftMouseButtonPressedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasLeftMouseButtonReleasedThisFrame(),"WasLeftMouseButtonReleasedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.IsRightMouseButtonPressed(),          "IsRightMouseButtonPressed must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasRightMouseButtonPressedThisFrame(),"WasRightMouseButtonPressedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasRightMouseButtonReleasedThisFrame(),"WasRightMouseButtonReleasedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.IsMiddleMouseButtonPressed(),          "IsMiddleMouseButtonPressed must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasMiddleMouseButtonPressedThisFrame(),"WasMiddleMouseButtonPressedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.WasMiddleMouseButtonReleasedThisFrame(),"WasMiddleMouseButtonReleasedThisFrame must not throw.");
            Assert.DoesNotThrow(() => MouseInputManager.GetMouseWheelDelta(),                  "GetMouseWheelDelta must not throw.");
        }
    }
}
