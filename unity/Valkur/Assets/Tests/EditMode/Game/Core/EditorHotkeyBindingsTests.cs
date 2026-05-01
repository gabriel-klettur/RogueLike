using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Core
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

            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out bool owns);

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

        [TestCase(EditorHotkeyBindings.Hotkey.ToggleParticles,    "<Keyboard>/f1")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleCombatRanges, "<Keyboard>/f2")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleSpawner,      "<Keyboard>/f3")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleLighting,     "<Keyboard>/f3")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleSpells,       "<Keyboard>/f4")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleEntities,     "<Keyboard>/f5")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleInventory,    "<Keyboard>/f6")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleItems,        "<Keyboard>/f7")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleTile,         "<Keyboard>/f8")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleDebugHUD,     "<Keyboard>/f9")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleBuildings,    "<Keyboard>/f10")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleMap,          "<Keyboard>/f11")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleFSM,          "<Keyboard>/f12")]
        [TestCase(EditorHotkeyBindings.Hotkey.QuickSave,          "<Keyboard>/f5")]
        [TestCase(EditorHotkeyBindings.Hotkey.QuickLoad,          "<Keyboard>/f9")]
        [TestCase(EditorHotkeyBindings.Hotkey.CtrlModifier,       "<Keyboard>/leftCtrl")]
        [TestCase(EditorHotkeyBindings.Hotkey.AltModifier,        "<Keyboard>/leftAlt")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleDevConsole,   "<Keyboard>/backquote")]
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

        [TestCase(EditorHotkeyBindings.Hotkey.ToggleEntities,     "<Keyboard>/f5")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleTile,         "<Keyboard>/f8")]
        [TestCase(EditorHotkeyBindings.Hotkey.ToggleBuildings,    "<Keyboard>/f10")]
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
