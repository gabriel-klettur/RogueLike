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

        [TestCase("ToggleParticles",    "<Keyboard>/f1")]
        [TestCase("ToggleCombatRanges", "<Keyboard>/f2")]
        [TestCase("ToggleSpawner",      "<Keyboard>/f3")]
        [TestCase("ToggleLighting",     "<Keyboard>/f3")]
        [TestCase("ToggleSpells",       "<Keyboard>/f4")]
        [TestCase("ToggleEntities",     "<Keyboard>/f5")]
        [TestCase("ToggleInventory",    "<Keyboard>/f6")]
        [TestCase("ToggleItems",        "<Keyboard>/f7")]
        [TestCase("ToggleTile",         "<Keyboard>/f8")]
        [TestCase("ToggleDebugHUD",     "<Keyboard>/f9")]
        [TestCase("ToggleBuildings",    "<Keyboard>/f10")]
        [TestCase("ToggleMap",          "<Keyboard>/f11")]
        [TestCase("ToggleFSM",          "<Keyboard>/f12")]
        [TestCase("QuickSave",          "<Keyboard>/f5")]
        [TestCase("QuickLoad",          "<Keyboard>/f9")]
        [TestCase("CtrlModifier",       "<Keyboard>/leftCtrl")]
        [TestCase("AltModifier",        "<Keyboard>/leftAlt")]
        [TestCase("ToggleDevConsole",   "<Keyboard>/backquote")]
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
            Assert.IsNotNull(g.Dash);
            Assert.IsNotNull(g.Interact);
            Assert.IsNotNull(g.Inventory);
            Assert.IsNotNull(g.Spell1);
            Assert.IsNotNull(g.Spell2);
            Assert.IsNotNull(g.Spell3);
            Assert.IsNotNull(g.Spell4);
            Assert.IsNotNull(g.Pause);
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
