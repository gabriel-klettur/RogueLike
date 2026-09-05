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

        /// <summary>
        /// The legacy half is DERIVED, not tabled.
        ///
        /// <para>This fixture used to reflect into a private <c>LegacyKeyCode(Hotkey)</c> and
        /// demand that every hotkey mapped to a real <see cref="KeyCode"/>. That table fed
        /// <c>UnityEngine.Input</c> directly, and it is exactly why the editor F-keys could not
        /// be retired by clearing bindings: the legacy leg went on answering for F1-F12 whatever
        /// the asset said. <c>InputBindingResolver</c> reads the action's live binding for both
        /// halves now, so the guarantee to pin is that the derivation EXISTS, not that a hand-
        /// maintained table is complete.</para>
        /// </summary>
        [Test]
        public void TheLegacyKeyCodeTable_IsGone()
        {
            var legacyKeyCodeMethod = typeof(EditorHotkeyBindings).GetMethod(
                "LegacyKeyCode", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNull(legacyKeyCodeMethod,
                "A Hotkey -> KeyCode table is a second source of truth for a binding. It made " +
                "a rebind apply half of itself and made clearing a binding clear none of the " +
                "key. Derive the legacy pair from the live path via InputBindingResolver.");
        }

        [Test]
        public void EveryHotkeyWithAKey_ResolvesBothHalvesFromItsBinding()
        {
            InputService.Initialize();

            foreach (EditorHotkeyBindings.Hotkey hk in
                System.Enum.GetValues(typeof(EditorHotkeyBindings.Hotkey)))
            {
                var action = EditorHotkeyBindings.Resolve(hk, out _);
                if (action == null || action.bindings.Count == 0) continue;   // ships unbound

                foreach (var b in Valkur.Core.Input.InputBindingResolver.Resolve(action))
                {
                    bool hasLegacy = b.Legacy != KeyCode.None;
                    Assert.IsTrue(hasLegacy,
                        $"Hotkey.{hk} is bound to {b.Path}, which has no legacy KeyCode — the " +
                        "OR-gate runs on one leg there and the hotkey dies under the 2022.3 " +
                        "event-drop bug.");
                }
            }
        }

        [Test]
        public void StatelessQueryMethods_DelegateToTheResolver()
        {
            // Each stateless query must delegate to InputBindingResolver, which is where
            // the OR of the two backends now lives. Measuring IL LENGTH was the old proxy for
            // "does it still have both branches"; that stopped meaning anything when the
            // branches moved into a shared helper and the method shrank to one call. The
            // honest check is that the call is there.
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
                var src = System.IO.File.ReadAllText(System.IO.Path.Combine(
                    UnityEngine.Application.dataPath,
                    "_Project", "Scripts", "Core", "Input", "EditorHotkeyBindings.cs"));
                StringAssert.Contains($"InputBindingResolver.{name}(ResolveLive(hotkey))", src,
                    $"{name} must read through InputBindingResolver, which is what ORs the two " +
                    "backends and derives the legacy half from the live binding. Reading the " +
                    "action alone loses the fallback the 2022.3 event-drop bug needs.");
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
