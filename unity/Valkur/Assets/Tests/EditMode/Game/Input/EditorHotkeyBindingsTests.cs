using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies <see cref="EditorHotkeyBindings.Resolve"/> hands editors the
    /// correct, enabled <see cref="InputAction"/> in both code paths:
    ///   • Play mode / bootstrapped: returns the canonical action from
    ///     <see cref="InputService.Editors"/> with <c>ownsAction = false</c>.
    ///   • EditMode / un-bootstrapped: builds an ad-hoc action with
    ///     <c>ownsAction = true</c> so the caller knows to dispose it.
    ///
    /// This is the contract every editor (TileEditor, EntitiesEditor, …) and
    /// handler (SaveLoadInputHandler, DebugHUD, …) depends on. If it ever drifts,
    /// F-keys silently stop dispatching and the user is left with editors that
    /// cannot be opened.
    /// </summary>
    [TestFixture]
    public class EditorHotkeyBindingsTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void TearDown()
        {
            InputService.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ─── Service-backed path ────────────────────────────────────────────────

        [Test]
        public void Resolve_WithService_ReturnsCanonicalAction_AndCallerDoesNotOwn()
        {
            InputService.Initialize();
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out bool owns);

            Assert.IsNotNull(action);
            Assert.AreSame(InputService.Instance.Editors.ToggleTile, action,
                "Resolve must hand back the SHARED canonical action so all editors observe the same state.");
            Assert.IsFalse(owns,
                "When the action comes from InputService, the caller must NOT dispose it on teardown.");
            Assert.IsTrue(action.enabled,
                "The shared canonical action must already be enabled (Editors map is enabled by InputService.ctor).");
        }

        [Test]
        public void Resolve_WithService_AllHotkeys_AreNonNullAndEnabled()
        {
            InputService.Initialize();
            foreach (EditorHotkeyBindings.Hotkey hk in System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                var a = EditorHotkeyBindings.Resolve(hk, out bool owns);
                // Still non-null with the service up, INCLUDING the fourteen retired editor
                // toggles: they ship unbound, not deleted, so the action exists and simply has
                // no bindings. That is what lets the Controls editor offer them.
                Assert.IsNotNull(a, $"{hk} resolved to null");
                Assert.IsTrue(a.enabled, $"{hk} must be enabled when resolved via InputService");
                Assert.IsFalse(owns, $"{hk} must report ownsAction=false when it came from InputService");
            }
        }

        // ─── Fallback path ──────────────────────────────────────────────────────

        [Test]
        public void Resolve_WithoutService_BuildsAdHocAction_AndCallerOwns()
        {
            // Force ResetForTests; service is null.
            InputService.ResetForTests();
            Assert.IsFalse(InputService.HasInstance);

            // ToggleDevConsole, not ToggleTile: the editor toggles ship unbound, so their
            // fallback path is null and Resolve correctly hands back no action at all. This
            // test is about the ad-hoc CONSTRUCTION path, which still exists for the hotkeys
            // that do have a key.
            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleDevConsole, out bool owns);

            try
            {
                Assert.IsNotNull(action);
                Assert.IsTrue(owns,
                    "Fallback path must transfer ownership to the caller so it disposes the ad-hoc action on teardown.");
                Assert.IsTrue(action.enabled,
                    "Fallback action must be Enable()-d before being returned (editor's polling assumes it is live).");
            }
            finally
            {
                if (action != null) action.Dispose();
            }
        }

        // Only the hotkeys that still carry a key. The fourteen editor toggles ship unbound —
        // editors are reached from the General Editor on Escape — and their fallback answers
        // null on purpose, which EditorEntryPointTests pins from the other direction.
        [TestCase(EditorHotkeyBindings.Hotkey.QuickSave,          "<Keyboard>/f5")]
        [TestCase(EditorHotkeyBindings.Hotkey.QuickLoad,          "<Keyboard>/f9")]
        [TestCase(EditorHotkeyBindings.Hotkey.CtrlModifier,       "<Keyboard>/leftCtrl")]
        [TestCase(EditorHotkeyBindings.Hotkey.AltModifier,        "<Keyboard>/leftAlt")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleDevConsole,   "<Keyboard>/backquote")]
        [TestCase(EditorHotkeyBindings.Hotkey.OpenGeneralEditor,  "<Keyboard>/escape")]
        public void Resolve_WithoutService_BindingPath_MatchesFallbackTable(
            EditorHotkeyBindings.Hotkey hotkey, string expectedPath)
        {
            InputService.ResetForTests();

            Assert.AreEqual(expectedPath, EditorHotkeyBindings.FallbackPath(hotkey),
                "Fallback paths must mirror the canonical asset so EditMode tests reflecting on the editor's _toggleAction see the right key.");

            var action = EditorHotkeyBindings.Resolve(hotkey, out bool _);
            try
            {
                Assert.AreEqual(1, action.bindings.Count);
                Assert.AreEqual(expectedPath, action.bindings[0].path);
            }
            finally
            {
                action.Dispose();
            }
        }

        // ─── Service-backed binding paths still match (single source of truth) ─

        [TestCase(EditorHotkeyBindings.Hotkey.OpenGeneralEditor,  "<Keyboard>/escape")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleDevConsole,   "<Keyboard>/backquote")]
        [TestCase(EditorHotkeyBindings.Hotkey.QuickSave,          "<Keyboard>/f5")]
        public void Resolve_WithService_HasExpectedBindingPath(
            EditorHotkeyBindings.Hotkey hotkey, string expectedPath)
        {
            InputService.Initialize();
            var action = EditorHotkeyBindings.Resolve(hotkey, out _);

            bool found = false;
            foreach (var b in action.bindings)
                if (b.path == expectedPath) { found = true; break; }
            Assert.IsTrue(found, $"{hotkey} must bind to {expectedPath} in the canonical asset.");
        }
    }
}
