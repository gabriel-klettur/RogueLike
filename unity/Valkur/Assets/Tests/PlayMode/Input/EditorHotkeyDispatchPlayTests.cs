using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.PlayMode.Input
{
    /// <summary>
    /// Real key-press dispatch tests using the Input System's <see cref="InputTestFixture"/>
    /// pattern. Each test queues a synthetic key event and verifies that the
    /// canonical action in <see cref="InputService.Editors"/> reports
    /// <c>WasPerformedThisFrame</c> on the next Update.
    ///
    /// This is the layer that EditMode tests cannot exercise — they only verify
    /// binding paths and enabled flags. Here we prove the keyboard-event →
    /// InputAction → polling pipeline actually fires for every editor F-key,
    /// which is the user-visible regression ("F8 doesn't open Tile Editor").
    ///
    /// <para>
    /// <b>Why every test is [Ignore]'d:</b> the dispatch path depends on the
    /// Unity Editor window having keyboard focus AND on the InputSystem action
    /// evaluator running on a Player update. Under MCP / batch-mode neither
    /// holds — the InputSystem manager's private <c>m_HasFocus</c> stays false
    /// and queued events are reset before reaching action handlers. The
    /// structural counterparts in
    /// <c>Valkur.Tests.EditMode.Input.EditorHotkeyBindingTests</c> cover the
    /// wiring that, if intact, guarantees these tests would pass with focus.
    /// </para>
    /// <para>
    /// <b>To run locally:</b> open Test Runner, ensure the Unity Editor window
    /// has focus, remove (or comment) the <c>[Ignore]</c> on the test(s) you
    /// want to validate, and run them manually. Do NOT remove the attributes
    /// in committed code — MCP / CI will go red.
    /// </para>
    /// </summary>
    [TestFixture]
    public class EditorHotkeyDispatchPlayTests
    {
        // Single source of truth for the [Ignore] reason on every test method.
        // Bumping this also bumps the message in every failure report.
        private const string IgnoreReason =
            "Requires Editor focus + Player-update action evaluator. " +
            "Fails deterministically under MCP / batch. Structural wiring is " +
            "covered by EditMode EditorHotkeyBindingTests; run this fixture " +
            "manually from Test Runner with the Editor focused if you need " +
            "to validate the live dispatch path.";


        private Keyboard _keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Reset to a clean InputService instance bound to the canonical asset.
            InputService.ResetForTests();
            InputService.Initialize();

            _keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputService.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        // ─── Per-key dispatch ──────────────────────────────────────────────────
        //
        // The eleven F-key dispatch tests are GONE with the keys they pressed. Every runtime
        // editor is reached from the General Editor on Escape now; the F-row is free, and the
        // fourteen toggles ship unbound. What remains is the hotkeys that still carry a key.

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Escape_Press_FiresOpenGeneralEditor()
            => AssertKeyFires(Key.Escape, () => InputService.Instance.Editors.OpenGeneralEditor);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F5_Press_FiresQuickSave()
            => AssertKeyFires(Key.F5, () => InputService.Instance.Editors.QuickSave);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F9_Press_FiresQuickLoad()
            => AssertKeyFires(Key.F9, () => InputService.Instance.Editors.QuickLoad);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Backquote_Press_FiresToggleDevConsole()
            => AssertKeyFires(Key.Backquote, () => InputService.Instance.Editors.ToggleDevConsole);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator LeftCtrl_Hold_ReportsCtrlModifierPressed()
        {
            yield return PressKey(Key.LeftCtrl);
            Assert.IsTrue(InputService.Instance.Editors.CtrlModifier.IsPressed(),
                "Ctrl modifier action must report IsPressed while leftCtrl is held — SaveLoadInputHandler gates QuickSave on this.");
            yield return ReleaseKey(Key.LeftCtrl);
        }

        /// <summary>
        /// The retired toggles must NOT fire on the keys they used to own. This is the half
        /// that would catch a stray binding or a resurrected legacy fallback putting them
        /// back — the failure mode there is silent, because pressing F8 and getting the Tile
        /// editor looks like it is working.
        /// </summary>
        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator RetiredToggles_DoNotFireOnTheirOldKeys()
        {
            var cases = new (Key key, System.Func<InputAction> get)[]
            {
                (Key.F1,  () => InputService.Instance.Editors.ToggleParticles),
                (Key.F4,  () => InputService.Instance.Editors.ToggleSpells),
                (Key.F8,  () => InputService.Instance.Editors.ToggleTile),
                (Key.F10, () => InputService.Instance.Editors.ToggleBuildings),
                (Key.F12, () => InputService.Instance.Editors.ToggleFSM),
            };

            foreach (var (key, get) in cases)
            {
                yield return PressAndRelease(key);
                var action = get();
                Assert.IsFalse(action.WasPerformedThisFrame() || action.triggered,
                    $"{key} must no longer fire {action.name} — editors are opened from the " +
                    "General Editor on Escape.");
            }
        }

        // ─── End-to-end via EditorHotkeyBindings.Resolve ───────────────────────

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_OpenGeneralEditor_FiresOnEscape_ViaService()
        {
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.OpenGeneralEditor, out bool owns);
            Assert.IsFalse(owns, "Service-backed resolve must not transfer ownership.");

            yield return PressAndRelease(Key.Escape);
            Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered,
                "Escape is the only way into any editor now — if this stops dispatching, all " +
                "sixteen are unreachable.");
        }

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_QuickSave_FiresOnF5Press_ViaService()
        {
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.QuickSave, out _);

            yield return PressAndRelease(Key.F5);
            Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered);
        }

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_FallbackPath_FiresOnBackquotePress()
        {
            // No InputService → fallback returns ad-hoc action. ToggleDevConsole rather than
            // ToggleTile: the editor toggles ship unbound, so their fallback path is null by
            // design and there is no ad-hoc action to dispatch.
            InputService.ResetForTests();
            Assert.IsFalse(InputService.HasInstance);

            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleDevConsole, out bool owns);
            Assert.IsTrue(owns, "Fallback must transfer ownership.");

            try
            {
                yield return PressAndRelease(Key.Backquote);
                Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered,
                    "Even without InputService, the ad-hoc fallback must dispatch — editor " +
                    "fixtures that build one editor in isolation depend on it.");
            }
            finally
            {
                action?.Dispose();
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private IEnumerator AssertKeyFires(Key key, System.Func<InputAction> getAction)
        {
            yield return PressAndRelease(key);
            var action = getAction();
            Assert.IsNotNull(action);
            Assert.IsTrue(
                action.WasPerformedThisFrame() || action.triggered,
                $"Action '{action.name}' must report a fired event for key {key}.");
        }

        private IEnumerator PressAndRelease(Key key)
        {
            yield return PressKey(key);
            yield return ReleaseKey(key);
            yield return null; // give the action one more frame to settle
        }

        private IEnumerator PressKey(Key key)
        {
            using (StateEvent.From(_keyboard, out var ev))
            {
                _keyboard[key].WriteValueIntoEvent(1f, ev);
                InputSystem.QueueEvent(ev);
            }
            InputSystem.Update();
            yield return null;
        }

        private IEnumerator ReleaseKey(Key key)
        {
            using (StateEvent.From(_keyboard, out var ev))
            {
                _keyboard[key].WriteValueIntoEvent(0f, ev);
                InputSystem.QueueEvent(ev);
            }
            InputSystem.Update();
            yield return null;
        }
    }
}
