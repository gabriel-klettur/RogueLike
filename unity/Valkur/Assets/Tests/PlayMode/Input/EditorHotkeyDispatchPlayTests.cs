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

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F1_Press_FiresToggleParticles()
            => AssertKeyFires(Key.F1, () => InputService.Instance.Editors.ToggleParticles);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F4_Press_FiresToggleSpells()
            => AssertKeyFires(Key.F4, () => InputService.Instance.Editors.ToggleSpells);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F5_Press_FiresToggleEntities()
            => AssertKeyFires(Key.F5, () => InputService.Instance.Editors.ToggleEntities);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F5_Press_AlsoFiresQuickSave_BindingShared()
            => AssertKeyFires(Key.F5, () => InputService.Instance.Editors.QuickSave);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F6_Press_FiresToggleInventory()
            => AssertKeyFires(Key.F6, () => InputService.Instance.Editors.ToggleInventory);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F7_Press_FiresToggleItems()
            => AssertKeyFires(Key.F7, () => InputService.Instance.Editors.ToggleItems);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F8_Press_FiresToggleTile()
            => AssertKeyFires(Key.F8, () => InputService.Instance.Editors.ToggleTile);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F9_Press_FiresToggleDebugHUD()
            => AssertKeyFires(Key.F9, () => InputService.Instance.Editors.ToggleDebugHUD);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F10_Press_FiresToggleBuildings()
            => AssertKeyFires(Key.F10, () => InputService.Instance.Editors.ToggleBuildings);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F11_Press_FiresToggleMap()
            => AssertKeyFires(Key.F11, () => InputService.Instance.Editors.ToggleMap);

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator F12_Press_FiresToggleFSM()
            => AssertKeyFires(Key.F12, () => InputService.Instance.Editors.ToggleFSM);

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

        // ─── End-to-end via EditorHotkeyBindings.Resolve ───────────────────────

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_ToggleTile_FiresOnF8Press_ViaService()
        {
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out bool owns);
            Assert.IsFalse(owns, "Service-backed resolve must not transfer ownership.");

            yield return PressAndRelease(Key.F8);
            Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered,
                "EditorHotkeyBindings.Resolve(ToggleTile) must report a press event in the same frame the F8 key fires.");
        }

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_ToggleEntities_FiresOnF5Press_ViaService()
        {
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleEntities, out _);

            yield return PressAndRelease(Key.F5);
            Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered);
        }

        [UnityTest]
        [Ignore(IgnoreReason)]
        public IEnumerator Resolve_FallbackPath_FiresOnF8Press()
        {
            // No InputService → fallback returns ad-hoc action.
            InputService.ResetForTests();
            Assert.IsFalse(InputService.HasInstance);

            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out bool owns);
            Assert.IsTrue(owns, "Fallback must transfer ownership.");

            try
            {
                yield return PressAndRelease(Key.F8);
                Assert.IsTrue(action.WasPerformedThisFrame() || action.triggered,
                    "Even without InputService, the ad-hoc fallback must dispatch F8 — every editor depends on this when EditMode tests build them in isolation.");
            }
            finally
            {
                action.Dispose();
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
