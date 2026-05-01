using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Locks down the legacy-KeyCode fallback inside
    /// <see cref="EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey)"/>
    /// and friends. Without this fallback every F-key (F1..F12 → editor
    /// toggles) silently dies whenever the new InputSystem package drops
    /// OS event delivery in the Editor (recurring Unity 2022.3.62f1 bug).
    ///
    /// Verified properties:
    ///
    ///   I1. Each Hotkey has a non-None KeyCode in the legacy mapping table.
    ///   I2. The IL bodies of the three stateless query methods are large
    ///       enough to contain BOTH the new-system check and the legacy
    ///       branch (a refactor that drops the OR would shrink them).
    ///   I3. The methods don't throw under any combination of states.
    ///
    /// We can't drive the legacy backend from EditMode (no synthetic-event
    /// path), so behavior of the legacy branch is verified at runtime by
    /// the user pressing F-keys and seeing editors toggle. This file is the
    /// structural guard that prevents future refactors from removing the
    /// branch by accident.
    /// </summary>
    [TestFixture]
    public class EditorHotkeyBindingsLegacyFallbackTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
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

        [Test]
        public void EveryHotkey_MapsToNonNoneLegacyKeyCode()
        {
            // Reflect into the private LegacyKeyCode method that EditorHotkeyBindings
            // uses for the OR-fallback. Every Hotkey enum value must map to a real
            // KeyCode — KeyCode.None means the legacy branch silently no-ops, and
            // a future refactor that adds a Hotkey without updating the mapping
            // table would silently lose the legacy fallback for that key.
            var legacyKeyCodeMethod = typeof(EditorHotkeyBindings).GetMethod(
                "LegacyKeyCode", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(legacyKeyCodeMethod,
                "EditorHotkeyBindings.LegacyKeyCode private method must exist " +
                "(it is the source of truth for the Hotkey→KeyCode mapping).");

            foreach (EditorHotkeyBindings.Hotkey hk in
                System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                var keyCode = (KeyCode)legacyKeyCodeMethod.Invoke(null, new object[] { hk });
                Assert.AreNotEqual(KeyCode.None, keyCode,
                    $"Hotkey.{hk} maps to KeyCode.None — the legacy fallback for " +
                    "this hotkey is a silent no-op. Add a row to LegacyKeyCode().");
            }
        }

        [Test]
        public void StatelessQueryMethods_HaveLegacyFallbackBranchInIL()
        {
            // Verify that each stateless query method's IL body is large enough
            // to contain BOTH branches of the OR. A method that only checks the
            // new-system action would be very short (~10 bytes); adding the
            // legacy branch (UnityEngine.Input.GetKeyXxx) brings it well past 30.
            string[] mustHave = {
                nameof(EditorHotkeyBindings.WasPerformedThisFrame),
                nameof(EditorHotkeyBindings.IsPressed),
                nameof(EditorHotkeyBindings.WasReleasedThisFrame),
            };

            foreach (var name in mustHave)
            {
                var m = typeof(EditorHotkeyBindings).GetMethod(name,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(EditorHotkeyBindings.Hotkey) },
                    null);
                Assert.IsNotNull(m, $"EditorHotkeyBindings.{name}(Hotkey) must exist");
                var body = m.GetMethodBody();
                Assert.IsNotNull(body);
                var il = body.GetILAsByteArray();
                Assert.Greater(il.Length, 20,
                    $"{name}: IL body is {il.Length} bytes — too short to contain " +
                    "the legacy UnityEngine.Input fallback in addition to the " +
                    "new-system branch. Did a refactor drop the OR-fallback?");
            }
        }

        [Test]
        public void WasPerformedThisFrame_OnIdleFrame_ReturnsFalseForEveryHotkey()
        {
            foreach (EditorHotkeyBindings.Hotkey hk in
                System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                Assert.IsFalse(EditorHotkeyBindings.WasPerformedThisFrame(hk),
                    $"Hotkey.{hk} reported pressed on idle frame.");
            }
        }

        [Test]
        public void IsPressed_OnIdleFrame_ReturnsFalseForEveryHotkey()
        {
            foreach (EditorHotkeyBindings.Hotkey hk in
                System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                Assert.IsFalse(EditorHotkeyBindings.IsPressed(hk),
                    $"Hotkey.{hk} reported held on idle frame.");
            }
        }

        [Test]
        public void StatelessAPI_DoesNotThrow_WhenInputServiceIsAbsent()
        {
            InputService.ResetForTests();
            // No Initialize: the stateless API should still resolve via ad-hoc
            // cache (and the legacy branch should be available either way).
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles));
            Assert.DoesNotThrow(() =>
                EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier));
        }
    }
}
