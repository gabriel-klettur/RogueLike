using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies <see cref="InputService"/> bootstraps correctly from the canonical
    /// <c>Resources/Input/ValkurInputActions</c> asset and that every action map
    /// promised by the typed accessors actually exists, is non-null, and is
    /// enabled.
    ///
    /// These tests guard against the regression where editor F-key actions stop
    /// firing because either:
    ///   • the asset on disk drifted from the typed accessors (a rename or
    ///     deletion of an action in the asset would silently throw at runtime),
    ///   • <c>FindActionMap</c> / <c>FindAction</c> returned <c>null</c>, or
    ///   • the maps were instantiated but not enabled, leaving every consumer
    ///     polling a permanently-disabled action.
    /// </summary>
    [TestFixture]
    public class InputServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            InputService.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            InputService.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // ─── Bootstrap contract ─────────────────────────────────────────────────

        [Test]
        public void Initialize_FromCanonicalAsset_CreatesSingleton()
        {
            var svc = InputService.Initialize();
            Assert.IsNotNull(svc, "InputService.Initialize must return a service when the canonical asset exists in Resources/Input.");
            Assert.IsTrue(InputService.HasInstance);
            Assert.AreSame(svc, InputService.Instance);
        }

        [Test]
        public void Initialize_CalledTwice_ReturnsSameSingleton()
        {
            var first  = InputService.Initialize();
            var second = InputService.Initialize();
            Assert.AreSame(first, second, "Re-initialization must be idempotent — repeated callers (RuntimeInputBootstrap.OnSceneLoaded, MainMenuUI.Start, etc.) all converge on the same singleton.");
        }

        [Test]
        public void Initialize_LoadsCanonicalAsset_NotARuntimeClone()
        {
            var svc = InputService.Initialize();
            Assert.IsNotNull(svc.Asset);
            // We deliberately do NOT clone the asset — Object.Instantiate of an
            // InputActionAsset created subtle event-tracking issues on Unity 2022.3.
            // The asset name therefore matches whatever is on disk (typically
            // "ValkurInputActions") rather than a "(Runtime)" suffix.
            StringAssert.DoesNotContain("(Runtime)", svc.Asset.name,
                "InputService must use the canonical asset directly; a clone would inject '(Runtime)' into the name.");
        }

        // ─── UI map ─────────────────────────────────────────────────────────────

        [Test]
        public void UIMap_IsEnabledAfterInitialize()
        {
            var svc = InputService.Initialize();
            Assert.IsTrue(svc.UI.Map.enabled, "UI map must be enabled so the EventSystem receives pointer events from the moment InputService boots.");
        }

        [Test]
        public void UIMap_HasAllRequiredActions_AndEachIsEnabled()
        {
            var svc = InputService.Initialize();
            var ui = svc.UI;

            Assert.IsNotNull(ui.Point,        "UI.Point");
            Assert.IsNotNull(ui.Click,        "UI.Click");
            Assert.IsNotNull(ui.RightClick,   "UI.RightClick");
            Assert.IsNotNull(ui.MiddleClick,  "UI.MiddleClick");
            Assert.IsNotNull(ui.ScrollWheel,  "UI.ScrollWheel");
            Assert.IsNotNull(ui.Navigate,     "UI.Navigate");
            Assert.IsNotNull(ui.Submit,       "UI.Submit");
            Assert.IsNotNull(ui.Cancel,       "UI.Cancel");

            Assert.IsTrue(ui.Point.enabled,   "UI.Point.enabled");
            Assert.IsTrue(ui.Click.enabled,   "UI.Click.enabled");
            Assert.IsTrue(ui.Submit.enabled,  "UI.Submit.enabled");
            Assert.IsTrue(ui.Cancel.enabled,  "UI.Cancel.enabled");
        }

        [Test]
        public void UIMap_PointAction_BindsToMousePosition()
        {
            var svc = InputService.Initialize();
            AssertActionHasBinding(svc.UI.Point, "<Mouse>/position");
        }

        [Test]
        public void UIMap_ClickAction_BindsToMouseLeftButton()
        {
            var svc = InputService.Initialize();
            AssertActionHasBinding(svc.UI.Click, "<Mouse>/leftButton");
        }

        // ─── Editors map ────────────────────────────────────────────────────────

        [Test]
        public void EditorsMap_IsEnabledAfterInitialize()
        {
            var svc = InputService.Initialize();
            Assert.IsTrue(svc.Editors.Map.enabled,
                "Editors map MUST be enabled at boot — every editor (TileEditor, EntitiesEditor, ...) polls its toggle action assuming it is live.");
        }

        [Test]
        public void EditorsMap_AllToggleActions_AreNonNull_AndEnabled()
        {
            var svc = InputService.Initialize();
            var e = svc.Editors;

            void Check(InputAction a, string name)
            {
                Assert.IsNotNull(a, $"Editors.{name} must exist");
                Assert.IsTrue(a.enabled, $"Editors.{name} must be enabled");
            }

            Check(e.ToggleParticles,    nameof(e.ToggleParticles));
            Check(e.ToggleCombatRanges, nameof(e.ToggleCombatRanges));
            Check(e.ToggleSpawner,      nameof(e.ToggleSpawner));
            Check(e.ToggleLighting,     nameof(e.ToggleLighting));
            Check(e.ToggleSpells,       nameof(e.ToggleSpells));
            Check(e.ToggleEntities,     nameof(e.ToggleEntities));
            Check(e.ToggleInventory,    nameof(e.ToggleInventory));
            Check(e.ToggleItems,        nameof(e.ToggleItems));
            Check(e.ToggleTile,         nameof(e.ToggleTile));
            Check(e.ToggleDebugHUD,     nameof(e.ToggleDebugHUD));
            Check(e.ToggleBuildings,    nameof(e.ToggleBuildings));
            Check(e.ToggleMap,          nameof(e.ToggleMap));
            Check(e.ToggleFSM,          nameof(e.ToggleFSM));
            Check(e.QuickSave,          nameof(e.QuickSave));
            Check(e.QuickLoad,          nameof(e.QuickLoad));
            Check(e.CtrlModifier,       nameof(e.CtrlModifier));
            Check(e.AltModifier,        nameof(e.AltModifier));
            Check(e.ToggleDevConsole,   nameof(e.ToggleDevConsole));
        }

        // ─── F-key parity (binding paths in the canonical asset) ────────────────

        // Only the Editors-map actions that still carry a key. The fourteen editor toggles
        // ship UNBOUND — every runtime editor is reached from the General Editor on Escape,
        // and the F-row was the source of every same-map collision in the project.
        // EditorEntryPointTests asserts the other side: that they carry no binding, and that
        // each still has a menu entry so nothing became unreachable.
        [TestCase("QuickSave",          "<Keyboard>/f5")]
        [TestCase("QuickLoad",          "<Keyboard>/f9")]
        [TestCase("CtrlModifier",       "<Keyboard>/leftCtrl")]
        [TestCase("AltModifier",        "<Keyboard>/leftAlt")]
        [TestCase("ToggleDevConsole",   "<Keyboard>/backquote")]
        [TestCase("OpenGeneralEditor",  "<Keyboard>/escape")]
        public void EditorsMap_ActionHasExpectedBinding(string actionName, string expectedPath)
        {
            var svc = InputService.Initialize();
            var action = svc.Editors.Map.FindAction(actionName);
            Assert.IsNotNull(action, $"Editors.{actionName} not found in canonical asset");
            AssertActionHasBinding(action, expectedPath);
        }

        // ─── Gameplay map ───────────────────────────────────────────────────────

        [Test]
        public void GameplayMap_HasAllRequiredActions()
        {
            var svc = InputService.Initialize();
            var g = svc.Gameplay;

            Assert.IsNotNull(g.Move);
            Assert.IsNotNull(g.Look);
            Assert.IsNotNull(g.PrimaryAttack);
            Assert.IsNotNull(g.SecondaryAttack);
            Assert.IsNotNull(g.MiddleClick);
            Assert.IsNotNull(g.Dash);
            Assert.IsNotNull(g.Interact);
            Assert.IsNotNull(g.Inventory);
            Assert.IsNotNull(g.Pause);
            Assert.IsNotNull(g.ToggleStance);
            // DropItem is in the asset because it was not: InventoryUI built its own
            // InputActions in code, so nothing could audit them — and one of them was still
            // bound to `tab`, which belongs to the stance toggle.
            Assert.IsNotNull(g.DropItem);
        }

        /// <summary>
        /// Every spell slot the catalog declares resolves to a live action.
        ///
        /// <para>This replaced twenty-four <c>Assert.IsNotNull(g.SpellX)</c> lines against
        /// twenty-four properties. The properties are gone with the hardcoded
        /// <c>(action, spellKey, KeyCode)</c> table that sat beside them: the KeyCode column
        /// fed the legacy OR-gate and did not move when a slot was rebound, so an override
        /// applied half of itself and the old key went on casting. The slot list is
        /// <see cref="InputActionCatalog"/>'s now, which is also what makes this test a loop
        /// rather than a list somebody has to remember to extend.</para>
        /// </summary>
        [Test]
        public void EveryCatalogSpellSlot_ResolvesToALiveAction()
        {
            var svc = InputService.Initialize();
            var g = svc.Gameplay;

            var missing = new List<string>();
            foreach (var descriptor in InputActionCatalog.Spells())
                if (g.Spell(descriptor.Action) == null)
                    missing.Add(descriptor.Id);

            Assert.IsEmpty(missing,
                "Spell slots in InputActionCatalog with no action in ValkurInputActions:\n" +
                string.Join("\n", missing));
        }

        /// <summary>
        /// Every action the catalog names exists in the asset, in the map the catalog claims.
        ///
        /// <para>The catalog is a CLOSED table on purpose — an action present in the asset and
        /// absent from it is a real gap, because nobody decided whether firing it can hurt
        /// somebody. This is the half that catches the reverse: a catalog entry naming an
        /// action the asset does not have, which reads as a control the Controls editor draws
        /// and can never bind.</para>
        /// </summary>
        [Test]
        public void EveryCatalogAction_ExistsInTheAsset()
        {
            var asset = InputService.Initialize().Asset;

            var missing = new List<string>();
            foreach (var descriptor in InputActionCatalog.All)
            {
                var map = asset.FindActionMap(descriptor.Map, throwIfNotFound: false);
                if (map == null) { missing.Add(descriptor.Id + " (no such map)"); continue; }
                if (map.FindAction(descriptor.Action, throwIfNotFound: false) == null)
                    missing.Add(descriptor.Id);
            }

            Assert.IsEmpty(missing,
                "InputActionCatalog names actions ValkurInputActions does not have:\n" +
                string.Join("\n", missing));
        }

        /// <summary>
        /// Every action in the asset has a catalog descriptor.
        ///
        /// <para>The direction that matters most. Without it, adding an action to the asset
        /// gives it no category, no stance mask and — the load-bearing one — no answer to
        /// "does firing this reach the damage path", which is what
        /// <see cref="InputContextPolicy"/> refuses a Peace binding on. A missing descriptor
        /// would let a new damage verb be bound in Peace, silently.</para>
        /// </summary>
        [Test]
        public void EveryAssetAction_HasACatalogDescriptor()
        {
            var asset = InputService.Initialize().Asset;

            var undeclared = new List<string>();
            foreach (var map in asset.actionMaps)
                foreach (var action in map.actions)
                    if (InputActionCatalog.Find(map.name, action.name) == null)
                        undeclared.Add(map.name + "/" + action.name);

            Assert.IsEmpty(undeclared,
                "Actions in ValkurInputActions with no InputActionCatalog descriptor. Each " +
                "needs a category, a stance mask and — the one that cannot be guessed — a " +
                "decision about whether firing it can damage something:\n" +
                string.Join("\n", undeclared));
        }

        /// <summary>
        /// No two bindings and no two actions share an id.
        ///
        /// <para>Not a tidiness check. <c>ApplyBindingOverride</c> and
        /// <c>SaveBindingOverridesAsJson</c> key overrides BY BINDING ID, so two bindings
        /// sharing one means a rebind of either moves BOTH, silently — which is what a
        /// Controls editor would have done on its first use. The shipped asset really had two
        /// such pairs (Inventory/MiddleClick and SpellTeleport/ToggleStance) plus a duplicate
        /// ACTION id (SpellBoomerang/ToggleStance), each of them invisible until something
        /// tried to write an override.</para>
        /// </summary>
        [Test]
        public void AssetIds_AreUnique()
        {
            var asset = InputService.Initialize().Asset;

            var actionIds  = new Dictionary<string, string>();
            var bindingIds = new Dictionary<string, string>();
            var clashes    = new List<string>();

            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    string id = action.id.ToString();
                    if (actionIds.TryGetValue(id, out var prior))
                        clashes.Add($"action id {id}: {prior} and {map.name}/{action.name}");
                    else actionIds[id] = map.name + "/" + action.name;
                }

                foreach (var b in map.bindings)
                {
                    string id = b.id.ToString();
                    string who = $"{map.name}/{b.action} → {b.path}";
                    if (bindingIds.TryGetValue(id, out var prior))
                        clashes.Add($"binding id {id}: {prior} and {who}");
                    else bindingIds[id] = who;
                }
            }

            Assert.IsEmpty(clashes,
                "Duplicate ids in ValkurInputActions. Binding overrides are keyed by id, so a " +
                "rebind of one moves every binding that shares it:\n" + string.Join("\n", clashes));
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private static void AssertActionHasBinding(InputAction action, string expectedPath)
        {
            bool found = false;
            foreach (var b in action.bindings)
            {
                if (b.path == expectedPath) { found = true; break; }
            }
            Assert.IsTrue(found,
                $"Action '{action.name}' must include a binding to {expectedPath}.");
        }
    }
}
