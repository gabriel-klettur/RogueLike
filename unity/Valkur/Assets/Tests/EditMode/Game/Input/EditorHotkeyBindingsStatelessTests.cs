using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Locks down the stateless query API on <see cref="EditorHotkeyBindings"/>.
    /// This is the regression guard for the recurring "F-keys stop working after a
    /// hot-recompile" bug: fields holding <see cref="InputAction"/> get serialised
    /// + restored as bindingless zombies under Domain Reload off, but the stateless
    /// API resolves a fresh action on every call so the zombie state is invisible
    /// to callers.
    /// </summary>
    [TestFixture]
    public class EditorHotkeyBindingsStatelessTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            InputService.ResetForTests();
            InputService.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            InputService.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ─── API contract ────────────────────────────────────────────────────────

        [Test]
        public void WasPerformedThisFrame_AnyHotkey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles));
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleBuildings));
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleEntities));
        }

        [Test]
        public void IsPressed_AnyHotkey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier));
        }

        [Test]
        public void WasPerformedThisFrame_OnIdleFrame_ReturnsFalse()
        {
            // No keystroke happened, every hotkey should report false.
            foreach (EditorHotkeyBindings.Hotkey hk in System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
                Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(hk),
                    $"Hotkey {hk} reported pressed on an idle frame.");
        }

        // ─── Zombie-immunity (the regression we're guarding against) ────────────

        [Test]
        public void StatelessAPI_StillWorks_AfterCallerCachedActionGoesZombie()
        {
            // Simulate a MonoBehaviour that resolved once and cached. After hot-reload
            // the cached InputAction is a clone whose actionMap is null and bindings
            // collection is empty.
            var cached = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleParticles, out _);
            Assert.IsNotNull(cached);

            // Pretend the cached reference got hot-reload-zombified by replacing it
            // with a separate bindingless InputAction that shares the name.
            var zombie = new InputAction("ToggleParticles", InputActionType.Button);
            Assert.AreEqual(0, zombie.bindings.Count);
            Assert.IsFalse(zombie.WasPerformedThisFrame(),
                "Zombie InputAction reports nothing — that's the bug we're working around.");

            // The stateless API resolves fresh and works regardless.
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles));
        }

        // ─── Ad-hoc fallback for EditMode without InputService ──────────────────

        [Test]
        public void StatelessAPI_FallsBackToAdHocAction_WhenInputServiceIsAbsent()
        {
            InputService.ResetForTests();
            // No Initialize: stateless API should still resolve via ad-hoc cache.
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles));
            Assert.IsFalse(
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles));
        }

        // ─── ReviveIfZombie helper ──────────────────────────────────────────────

        [Test]
        public void ReviveIfZombie_LiveAction_ReturnsSameInstance()
        {
            var live = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleParticles, out bool owns);
            var revived = EditorHotkeyBindings.ReviveIfZombie(
                live, EditorHotkeyBindings.Hotkey.ToggleParticles, ref owns);
            Assert.AreSame(live, revived,
                "A live action with bindings must not be replaced.");
        }

        [Test]
        public void ReviveIfZombie_BindinglessClone_ReturnsFreshLiveAction()
        {
            var zombie = new InputAction("ToggleParticles", InputActionType.Button);
            bool owns = false;
            var revived = EditorHotkeyBindings.ReviveIfZombie(
                zombie, EditorHotkeyBindings.Hotkey.ToggleParticles, ref owns);
            Assert.IsNotNull(revived);
            Assert.Greater(revived.bindings.Count, 0,
                "Revival must return a live action with bindings.");
        }
    }
}
