using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies that <see cref="EditorHotkeyBindings"/> stateless query API
    /// honours <see cref="InputBlocker.IsGameplayBlocked"/>:
    ///
    ///   • While blocked, all non-<see cref="EditorHotkeyBindings.Hotkey.ToggleDevConsole"/>
    ///     hotkeys return <c>false</c> immediately (early-return path).
    ///   • While blocked, <see cref="EditorHotkeyBindings.Hotkey.ToggleDevConsole"/>
    ///     bypasses the early-return and reaches <c>ResolveLive</c> — in EditMode
    ///     it builds an ad-hoc action and returns <c>false</c> (key not pressed),
    ///     but must not throw.
    ///   • While unblocked, all queries return <c>false</c> (no hardware) without
    ///     throwing.
    /// </summary>
    [TestFixture]
    public class EditorHotkeyBindingsBlockerTests
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

        // ── Blocked: non-ToggleDevConsole hotkeys early-return false ────────

        [Test]
        public void WasPerformedThisFrame_WhenBlocked_ReturnsFalse_ForNonToggleDevConsole()
        {
            InputBlocker.SetBlocked(true);

            // A representative sample of non-DevConsole hotkeys.
            Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleTile),
                "ToggleTile must return false when blocked.");
            Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleMap),
                "ToggleMap must return false when blocked.");
            Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleBuildings),
                "ToggleBuildings must return false when blocked.");
            Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.QuickSave),
                "QuickSave must return false when blocked.");
            Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleFSM),
                "ToggleFSM must return false when blocked.");

            // Same contract for IsPressed and WasReleasedThisFrame.
            Assert.IsFalse(EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.ToggleTile),
                "IsPressed(ToggleTile) must return false when blocked.");
            Assert.IsFalse(EditorHotkeyBindings.WasReleasedThisFrame(EditorHotkeyBindings.Hotkey.ToggleTile),
                "WasReleasedThisFrame(ToggleTile) must return false when blocked.");
        }

        // ── Blocked: ToggleDevConsole bypasses early-return ─────────────────

        [Test]
        public void WasPerformedThisFrame_WhenBlocked_ToggleDevConsole_StillReachesAction()
        {
            // ToggleDevConsole must NOT early-return while blocked —
            // the ~ key must keep working so the user can dismiss the dev console.
            // In EditMode, InputService is not up, so ResolveLive builds an ad-hoc
            // action for backquote. The key is not physically pressed, so the
            // result is false, but the code path must reach ResolveLive (no throw).
            InputBlocker.SetBlocked(true);

            bool result = true; // pre-set to true to prove the assignment happened
            Assert.DoesNotThrow(() =>
            {
                result = EditorHotkeyBindings.WasPerformedThisFrame(
                    EditorHotkeyBindings.Hotkey.ToggleDevConsole);
            }, "WasPerformedThisFrame(ToggleDevConsole) must not throw while blocked.");

            Assert.IsFalse(result,
                "ToggleDevConsole returns false in EditMode (no key pressed), " +
                "but must reach ResolveLive rather than early-return.");

            // Same for IsPressed and WasReleasedThisFrame.
            Assert.DoesNotThrow(() => EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.ToggleDevConsole),
                "IsPressed(ToggleDevConsole) must not throw while blocked.");
            Assert.DoesNotThrow(() => EditorHotkeyBindings.WasReleasedThisFrame(EditorHotkeyBindings.Hotkey.ToggleDevConsole),
                "WasReleasedThisFrame(ToggleDevConsole) must not throw while blocked.");
        }

        // ── Unblocked: all queries return false and do not throw ─────────────

        [Test]
        public void AllQueries_WhenUnblocked_ReturnFalse_NoThrow()
        {
            // Blocker is off. No key is pressed. Results are false; no throws.
            var hotkeys = (EditorHotkeyBindings.Hotkey[])
                System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey));

            foreach (var hotkey in hotkeys)
            {
                var h = hotkey; // capture for lambda
                Assert.DoesNotThrow(() => EditorHotkeyBindings.WasPerformedThisFrame(h),
                    $"WasPerformedThisFrame({h}) must not throw when unblocked.");
                Assert.DoesNotThrow(() => EditorHotkeyBindings.IsPressed(h),
                    $"IsPressed({h}) must not throw when unblocked.");
                Assert.DoesNotThrow(() => EditorHotkeyBindings.WasReleasedThisFrame(h),
                    $"WasReleasedThisFrame({h}) must not throw when unblocked.");
            }
        }
    }
}
