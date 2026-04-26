using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Regression tests for the main-menu screen visibility contract.
    ///
    /// These tests guarantee that the bug "after deleting a save in the Load
    /// Game panel, the Main Menu reappears on top of the still-open Load Game
    /// overlay" never returns. The contract enforced here:
    ///
    ///   1. <c>ShowMenuScreen</c> is the single source of truth for which root
    ///      container is visible. Exactly one of {Main, Options/Sounds/Inputs,
    ///      LoadGame} is ever active at a time.
    ///   2. The active sub-screen overlay is always the last sibling under the
    ///      canvas, so a freshly rebuilt MenuPanel can never appear on top.
    ///   3. <c>RebuildMenuPanel</c> respects the current <c>_menuScreen</c>:
    ///      the rebuilt MenuPanel stays hidden if a sub-screen is open, and
    ///      the active sub-screen overlay is re-promoted to last sibling.
    ///
    /// Together these invariants make the menus open/close as siblings without
    /// ever overlapping or stealing each other's mouse input.
    /// </summary>
    [TestFixture]
    public class MainMenuScreenVisibilityTests
    {
        private GameObject _go;
        private MainMenuUI _menu;

        // Cached reflection handles
        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            var existing = UnityEngine.Object.FindObjectOfType<MainMenuUI>();
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            _go   = new GameObject("TestMainMenuUI_ScreenVisibility");
            _menu = _go.AddComponent<MainMenuUI>();
            InvokePrivate("Start");

            // Press-to-Start overlay (added later) hides the main panel until
            // the player acknowledges. This fixture targets the post-acknowledge
            // menu-screen visibility contract, so we dismiss it programmatically
            // here so every test runs from the canonical "Main panel visible" state.
            DismissPressToStart();
        }

        /// <summary>Force-dismiss the Press-to-Start overlay so MenuPanel is shown.</summary>
        private void DismissPressToStart()
        {
            var activeField = typeof(MainMenuUI).GetField("_pressToStartActive", PrivInst);
            if (activeField != null) activeField.SetValue(_menu, false);

            var overlayField = typeof(MainMenuUI).GetField("_pressToStartOverlay", PrivInst);
            if (overlayField?.GetValue(_menu) is GameObject overlay)
                overlay.SetActive(false);

            // Re-show the main menu panel that PressToStart hid during BuildUI.
            var panelField = typeof(MainMenuUI).GetField("_menuPanelGo", PrivInst);
            if (panelField?.GetValue(_menu) is GameObject panel)
                panel.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private void InvokePrivate(string methodName, params object[] args)
        {
            var m = typeof(MainMenuUI).GetMethod(methodName, PrivInst);
            m?.Invoke(_menu, args);
        }

        private T GetField<T>(string name)
        {
            var f = typeof(MainMenuUI).GetField(name, PrivInst);
            return f != null ? (T)f.GetValue(_menu) : default;
        }

        /// <summary>Resolves the private nested <c>MenuScreen</c> enum value by name.</summary>
        private static object MenuScreen(string valueName)
        {
            var enumType = typeof(MainMenuUI).GetNestedType("MenuScreen", PrivInst);
            Assert.IsNotNull(enumType, "Private enum MainMenuUI.MenuScreen must exist");
            return Enum.Parse(enumType, valueName);
        }

        private void ShowScreen(string valueName)
        {
            var m = typeof(MainMenuUI).GetMethod("ShowMenuScreen", PrivInst);
            Assert.IsNotNull(m, "ShowMenuScreen private method must exist");
            m.Invoke(_menu, new[] { MenuScreen(valueName) });
        }

        private GameObject MenuPanel    => GetField<GameObject>("_menuPanelGo");
        private GameObject OptOverlay   => GetField<GameObject>("_optOverlay");
        private GameObject LoadOverlay  => GetField<GameObject>("_mmLoadOverlay");
        private GameObject ClassPanel   => GetField<GameObject>("_classSelectionPanel");

        /// <summary>
        /// Counts how many of the four menu roots are currently active. The
        /// invariant is "exactly one" for any single-screen state.
        /// </summary>
        private int CountActiveRoots()
        {
            int n = 0;
            if (MenuPanel   != null && MenuPanel.activeSelf)   n++;
            if (OptOverlay  != null && OptOverlay.activeSelf)  n++;
            if (LoadOverlay != null && LoadOverlay.activeSelf) n++;
            if (ClassPanel  != null && ClassPanel.activeSelf)  n++;
            return n;
        }

        // ── Initial state ─────────────────────────────────────────────────────

        [Test]
        public void Start_LeavesOnlyMainPanelActive()
        {
            Assert.IsNotNull(MenuPanel,   "_menuPanelGo must be built during Start");
            Assert.IsNotNull(OptOverlay,  "_optOverlay must be built during Start");
            Assert.IsNotNull(LoadOverlay, "_mmLoadOverlay must be built during Start");

            Assert.IsTrue(MenuPanel.activeSelf,    "Main menu panel must be visible at start");
            Assert.IsFalse(OptOverlay.activeSelf,  "Options overlay must be hidden at start");
            Assert.IsFalse(LoadOverlay.activeSelf, "Load overlay must be hidden at start");
            Assert.IsNotNull(ClassPanel,           "_classSelectionPanel must be built during Start");
            Assert.IsFalse(ClassPanel.activeSelf,  "Class selector overlay must be hidden at start");
        }

        // ── ShowMenuScreen single-source-of-truth ────────────────────────────

        [Test]
        public void ShowLoadGame_HidesMainPanel()
        {
            ShowScreen("LoadGame");
            Assert.IsFalse(MenuPanel.activeSelf,
                "Main menu panel must be hidden while the Load Game overlay is open");
            Assert.IsTrue(LoadOverlay.activeSelf,
                "Load Game overlay must be active");
            Assert.AreEqual(1, CountActiveRoots(),
                "Exactly one root container must be active at a time");
        }

        [Test]
        public void ShowOptions_HidesMainPanel()
        {
            ShowScreen("Options");
            Assert.IsFalse(MenuPanel.activeSelf,
                "Main menu panel must be hidden while the Options overlay is open");
            Assert.IsTrue(OptOverlay.activeSelf, "Options overlay must be active");
            Assert.AreEqual(1, CountActiveRoots(),
                "Exactly one root container must be active at a time");
        }

        [Test]
        public void ShowSounds_HidesMainPanel_AndKeepsOptionsOverlay()
        {
            ShowScreen("Sounds");
            Assert.IsFalse(MenuPanel.activeSelf,
                "Main menu panel must be hidden in any sub-screen");
            Assert.IsTrue(OptOverlay.activeSelf,
                "Sounds is rendered inside the Options overlay container");
            Assert.AreEqual(1, CountActiveRoots(),
                "Exactly one root container must be active at a time");
        }

        [Test]
        public void ShowMain_AfterSubscreen_RestoresMainPanel()
        {
            ShowScreen("LoadGame");
            ShowScreen("Main");
            Assert.IsTrue(MenuPanel.activeSelf,
                "Returning to Main must re-show the menu panel");
            Assert.IsFalse(LoadOverlay.activeSelf,
                "Load overlay must be hidden when back on Main");
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void EveryScreenTransition_KeepsExactlyOneRootActive()
        {
            string[] sequence = { "Main", "Options", "Sounds", "Inputs", "Options",
                                  "Main", "LoadGame", "Main" };
            foreach (var s in sequence)
            {
                ShowScreen(s);
                Assert.AreEqual(1, CountActiveRoots(),
                    $"After ShowMenuScreen({s}) exactly one root must be active");
            }
        }

        // ── Z-order: active overlay is always last sibling ───────────────────

        [Test]
        public void ShowLoadGame_PromotesLoadOverlayToLastSibling()
        {
            ShowScreen("LoadGame");
            int last = LoadOverlay.transform.parent.childCount - 1;
            Assert.AreEqual(last, LoadOverlay.transform.GetSiblingIndex(),
                "Load overlay must be the last canvas sibling so it draws on top");
        }

        [Test]
        public void ShowOptions_PromotesOptionsOverlayToLastSibling()
        {
            ShowScreen("Options");
            int last = OptOverlay.transform.parent.childCount - 1;
            Assert.AreEqual(last, OptOverlay.transform.GetSiblingIndex(),
                "Options overlay must be the last canvas sibling so it draws on top");
        }

        // ── RebuildMenuPanel must respect the current screen ─────────────────

        [Test]
        public void RebuildMenuPanel_WhileOnLoadGame_KeepsMainPanelHidden()
        {
            // Reproduces the exact bug: open Load Game, "delete a save" (which
            // internally calls RebuildMenuPanel), and verify the main menu does
            // NOT pop back on top of the still-open load overlay.
            ShowScreen("LoadGame");
            InvokePrivate("RebuildMenuPanel");

            Assert.IsNotNull(MenuPanel, "RebuildMenuPanel must produce a fresh _menuPanelGo");
            Assert.IsFalse(MenuPanel.activeSelf,
                "After rebuilding while in LoadGame, the new MenuPanel must remain hidden");
            Assert.IsTrue(LoadOverlay.activeSelf,
                "The Load overlay must stay open during/after the rebuild");
            Assert.AreEqual(1, CountActiveRoots(),
                "Rebuilding must not break the single-active-root invariant");
        }

        [Test]
        public void RebuildMenuPanel_WhileOnLoadGame_LoadOverlayStaysOnTop()
        {
            ShowScreen("LoadGame");
            InvokePrivate("RebuildMenuPanel");
            int last = LoadOverlay.transform.parent.childCount - 1;
            Assert.AreEqual(last, LoadOverlay.transform.GetSiblingIndex(),
                "After rebuild, the Load overlay must remain the last sibling " +
                "so the freshly created MenuPanel cannot be drawn on top of it");
        }

        [Test]
        public void RebuildMenuPanel_WhileOnOptions_KeepsMainPanelHidden()
        {
            ShowScreen("Options");
            InvokePrivate("RebuildMenuPanel");
            Assert.IsFalse(MenuPanel.activeSelf,
                "After rebuilding while in any sub-screen, MenuPanel must stay hidden");
            Assert.IsTrue(OptOverlay.activeSelf, "Options overlay must remain visible");
            int last = OptOverlay.transform.parent.childCount - 1;
            Assert.AreEqual(last, OptOverlay.transform.GetSiblingIndex(),
                "After rebuild while in Options, Options overlay must remain on top");
        }

        [Test]
        public void RebuildMenuPanel_WhileOnMain_KeepsMainPanelVisible()
        {
            // Sanity check: rebuilding while on Main (the normal case after a
            // back-from-LoadGame) must keep the panel visible.
            ShowScreen("Main");
            InvokePrivate("RebuildMenuPanel");
            Assert.IsTrue(MenuPanel.activeSelf,
                "Rebuilding while on Main must leave the new MenuPanel visible");
            Assert.AreEqual(1, CountActiveRoots());
        }

        // ── Back navigation from LoadGame after a deletion ───────────────────

        [Test]
        public void OptionsGoBack_FromLoadGame_RestoresMainPanelOnTop()
        {
            ShowScreen("LoadGame");
            // Simulate having mutated the save list while in LoadGame: rebuild
            // must happen before the screen flip, otherwise the visibility
            // contract still holds because ShowMenuScreen is the final step.
            InvokePrivate("OptionsGoBack");

            Assert.IsTrue(MenuPanel.activeSelf,
                "After OptionsGoBack from LoadGame, Main panel must be visible again");
            Assert.IsFalse(LoadOverlay.activeSelf,
                "Load overlay must be hidden after returning to Main");
            Assert.AreEqual(1, CountActiveRoots());
            int last = MenuPanel.transform.parent.childCount - 1;
            Assert.AreEqual(last, MenuPanel.transform.GetSiblingIndex(),
                "Main panel must end up as the last sibling so it's interactable");
        }

        // ── ClassSelector screen (regression: "Nuevo juego" overlap bug) ─────

        [Test]
        public void ShowClassSelector_HidesMainPanel()
        {
            // Bug repro: clicking "Nuevo juego" used to leave the main menu
            // drawn on top of the class selector because OpenClassSelector did
            // not toggle _menuPanelGo. The fix routes through ShowMenuScreen,
            // which is the single source of truth for visibility + z-order.
            ShowScreen("ClassSelector");
            Assert.IsTrue(ClassPanel.activeSelf,
                "Class selector must be active after ShowMenuScreen(ClassSelector)");
            Assert.IsFalse(MenuPanel.activeSelf,
                "Main menu panel must be hidden while the class selector is open");
            Assert.AreEqual(1, CountActiveRoots(),
                "Exactly one root container must be active at a time");
        }

        [Test]
        public void ShowClassSelector_PromotesPanelToLastSibling()
        {
            ShowScreen("ClassSelector");
            int last = ClassPanel.transform.parent.childCount - 1;
            Assert.AreEqual(last, ClassPanel.transform.GetSiblingIndex(),
                "Class selector must be the last canvas sibling so it draws on top " +
                "(otherwise the main menu pill rows would intercept the mouse)");
        }

        [Test]
        public void OpenClassSelector_HidesMainPanel()
        {
            // Direct invocation of the high-level OpenClassSelector entry point
            // mirrors what "Nuevo juego" does. The Main panel must disappear.
            InvokePrivate("OpenClassSelector");
            Assert.IsTrue(ClassPanel.activeSelf,
                "OpenClassSelector must show the class selector overlay");
            Assert.IsFalse(MenuPanel.activeSelf,
                "OpenClassSelector must hide the main menu panel");
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void CloseClassSelector_RestoresMainPanel()
        {
            InvokePrivate("OpenClassSelector");
            InvokePrivate("CloseClassSelector");
            Assert.IsTrue(MenuPanel.activeSelf,
                "Closing the class selector must re-show the main menu panel");
            Assert.IsFalse(ClassPanel.activeSelf,
                "Class selector overlay must be hidden after CloseClassSelector");
            Assert.AreEqual(1, CountActiveRoots());
            int last = MenuPanel.transform.parent.childCount - 1;
            Assert.AreEqual(last, MenuPanel.transform.GetSiblingIndex(),
                "Main panel must be on top after closing the class selector");
        }

        [Test]
        public void OpenClassSelector_SyncsShowingClassSelectorFlag()
        {
            // _showingClassSelector is what Update() inspects to route input
            // (HandleClassSelectorInput vs. HandleKeyboardNavigation). It must
            // stay in sync with the screen state so keyboard works.
            InvokePrivate("OpenClassSelector");
            Assert.IsTrue(GetField<bool>("_showingClassSelector"),
                "Opening the class selector must set _showingClassSelector=true");

            InvokePrivate("CloseClassSelector");
            Assert.IsFalse(GetField<bool>("_showingClassSelector"),
                "Closing the class selector must clear _showingClassSelector");
        }

        [Test]
        public void RebuildMenuPanel_WhileOnClassSelector_KeepsMainPanelHidden()
        {
            // If "Nuevo juego" is followed by any rebuild (e.g. a save was
            // deleted asynchronously), the main menu must not pop on top of
            // the class selector.
            InvokePrivate("OpenClassSelector");
            InvokePrivate("RebuildMenuPanel");
            Assert.IsFalse(MenuPanel.activeSelf,
                "After rebuild, main panel must remain hidden while the class selector is open");
            Assert.IsTrue(ClassPanel.activeSelf,
                "Class selector must remain visible across rebuilds");
            int last = ClassPanel.transform.parent.childCount - 1;
            Assert.AreEqual(last, ClassPanel.transform.GetSiblingIndex(),
                "Class selector must be re-promoted to last sibling after rebuild");
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void EveryScreenTransition_IncludingClassSelector_KeepsExactlyOneRootActive()
        {
            string[] sequence = { "Main", "ClassSelector", "Main", "Options",
                                  "ClassSelector", "LoadGame", "ClassSelector", "Main" };
            foreach (var s in sequence)
            {
                ShowScreen(s);
                Assert.AreEqual(1, CountActiveRoots(),
                    $"After ShowMenuScreen({s}) exactly one root must be active");
            }
        }
    }
}
