using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The main menu's screen-visibility contract.
    ///
    /// <para>It exists to keep one bug dead: "after deleting a save in the Load Game panel, the
    /// main menu reappears ON TOP of the still-open overlay". The invariants are unchanged by the
    /// 2026-09-12 rebuild, which is why this fixture survived it —</para>
    ///
    /// <list type="number">
    ///   <item><c>ShowMenuScreen</c> is the single source of truth: exactly one root is active
    ///   at a time.</item>
    ///   <item>The active root is the LAST sibling under the canvas, so a freshly rebuilt main
    ///   panel can never appear over it.</item>
    ///   <item><c>RebuildMenuPanel</c> respects the current screen.</item>
    /// </list>
    ///
    /// <para><b>What DID change is the mechanism, and the names.</b> The four screens behind one
    /// shared <c>_optOverlay</c> are now five panels with a root each (<c>MenuPanelView</c>), and
    /// "Sounds" and "Inputs" are "Audio" and "Controls". The fixture reads the roots through
    /// <c>PanelFor</c> rather than through a field per screen, so adding a sixth screen extends
    /// the coverage instead of escaping it.</para>
    /// </summary>
    [TestFixture]
    public class MainMenuScreenVisibilityTests
    {
        private GameObject _go;
        private MainMenuUI _menu;

        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Every screen that owns a panel, by name. Read from the enum, never a list.</summary>
        private static IEnumerable<string> PanelScreens()
        {
            foreach (var name in Enum.GetNames(EnumType()))
                if (name != "Main" && name != "LoadGame" && name != "ClassSelector")
                    yield return name;
        }

        private static Type EnumType()
        {
            var t = typeof(MainMenuUI).GetNestedType("MenuScreen", BindingFlags.NonPublic);
            Assert.IsNotNull(t, "Private enum MainMenuUI.MenuScreen must exist");
            return t;
        }

        [SetUp]
        public void SetUp()
        {
            var existing = UnityEngine.Object.FindObjectOfType<MainMenuUI>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            _go = new GameObject("TestMainMenuUI_ScreenVisibility");
            _menu = _go.AddComponent<MainMenuUI>();
            InvokePrivate("Start");
            // Screens are built ON DEMAND in production — the class selector, the four option
            // panels, the load browser and the credits are 790 of 815 transforms and the player
            // may never open any of them. This fixture is about VISIBILITY, so it builds them all
            // up front rather than also being a test about lazy construction.
            _menu.BuildAllScreensForTests();
            DismissPressToStart();
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate, never Destroy: Object.Destroy is an outright ERROR in Edit Mode.
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ───────────────────────────────────────────────

        private void InvokePrivate(string methodName, params object[] args)
        {
            var m = typeof(MainMenuUI).GetMethod(methodName, PrivInst);
            Assert.IsNotNull(m, methodName + " must exist");
            m.Invoke(_menu, args);
        }

        private T GetField<T>(string name) where T : class
        {
            var f = typeof(MainMenuUI).GetField(name, PrivInst);
            return f?.GetValue(_menu) as T;
        }

        private void DismissPressToStart()
        {
            var m = typeof(MainMenuUI).GetMethod("DismissPressToStart", PrivInst);
            Assert.IsNotNull(m, "DismissPressToStart must exist");
            m.Invoke(_menu, new object[] { true });
        }

        private void ShowScreen(string valueName)
        {
            var m = typeof(MainMenuUI).GetMethod("ShowMenuScreen", PrivInst);
            Assert.IsNotNull(m, "ShowMenuScreen private method must exist");
            m.Invoke(_menu, new[] { Enum.Parse(EnumType(), valueName) });
        }

        private string CurrentScreen()
        {
            var f = typeof(MainMenuUI).GetField("_menuScreen", PrivInst);
            return f?.GetValue(_menu)?.ToString() ?? "null";
        }

        /// <summary>The root GameObject of a panel-backed screen, through the production lookup.</summary>
        private GameObject PanelRoot(string valueName)
        {
            var m = typeof(MainMenuUI).GetMethod("PanelFor", PrivInst);
            Assert.IsNotNull(m, "PanelFor must exist — it is how ShowMenuScreen finds a panel");
            var view = m.Invoke(_menu, new[] { Enum.Parse(EnumType(), valueName) });
            if (view == null) return null;
            var rootProp = view.GetType().GetField("Root");
            Assert.IsNotNull(rootProp, "MenuPanelView.Root must exist");
            var rt = rootProp.GetValue(view) as RectTransform;
            return rt != null ? rt.gameObject : null;
        }

        private GameObject MenuPanel => GetField<GameObject>("_menuPanelGo");
        private GameObject LoadOverlay => GetField<GameObject>("_mmLoadOverlay");
        private GameObject ClassPanel => GetField<GameObject>("_classSelectionPanel");

        private List<GameObject> AllRoots()
        {
            var roots = new List<GameObject>();
            if (MenuPanel != null) roots.Add(MenuPanel);
            if (LoadOverlay != null) roots.Add(LoadOverlay);
            if (ClassPanel != null) roots.Add(ClassPanel);
            foreach (var screen in PanelScreens())
            {
                var root = PanelRoot(screen);
                if (root != null) roots.Add(root);
            }
            return roots;
        }

        private int CountActiveRoots()
        {
            int n = 0;
            foreach (var root in AllRoots()) if (root.activeSelf) n++;
            return n;
        }

        // ── Initial state ────────────────────────────────────────────────────

        [Test]
        public void EveryRootCanBeBuilt_AndOnlyTheMainPanelIsActive()
        {
            Assert.IsNotNull(MenuPanel, "_menuPanelGo must be built during Start");
            Assert.IsNotNull(LoadOverlay, "_mmLoadOverlay must be built during Start");
            Assert.IsNotNull(ClassPanel, "_classSelectionPanel must be built during Start");
            foreach (var screen in PanelScreens())
                Assert.IsNotNull(PanelRoot(screen), screen + " has no panel");

            Assert.IsTrue(MenuPanel.activeSelf, "the main panel must be visible at start");
            Assert.AreEqual(1, CountActiveRoots(), "exactly one root may be active");
        }

        // ── One root at a time ───────────────────────────────────────────────

        [Test]
        public void EveryScreen_LeavesExactlyOneRootActive()
        {
            foreach (var name in Enum.GetNames(EnumType()))
            {
                ShowScreen(name);
                Assert.AreEqual(1, CountActiveRoots(),
                    $"after ShowMenuScreen({name}) exactly one root must be active");
                Assert.AreEqual(name, CurrentScreen());
            }
        }

        [Test]
        public void TheActiveRoot_IsTheLastSibling()
        {
            foreach (var name in Enum.GetNames(EnumType()))
            {
                ShowScreen(name);
                GameObject active = null;
                foreach (var root in AllRoots()) if (root.activeSelf) active = root;
                Assert.IsNotNull(active, name + " left nothing on screen");
                var parent = active.transform.parent;
                Assert.AreEqual(parent.childCount - 1, active.transform.GetSiblingIndex(),
                    $"{name}'s root must be the last sibling or a rebuilt panel can cover it");
            }
        }

        [Test]
        public void ShowLoadGame_HidesTheMainPanel()
        {
            ShowScreen("LoadGame");
            Assert.IsFalse(MenuPanel.activeSelf);
            Assert.IsTrue(LoadOverlay.activeSelf);
        }

        [Test]
        public void ShowAudio_LeavesTheOptionsPanelClosed()
        {
            ShowScreen("Audio");
            Assert.IsTrue(PanelRoot("Audio").activeSelf);
            Assert.IsFalse(PanelRoot("Options").activeSelf,
                "each screen owns its own root now; two cannot be up at once");
        }

        // ── RebuildMenuPanel respects the current screen ─────────────────────

        [Test]
        public void RebuildMenuPanel_WhileOnASubScreen_LeavesTheMainPanelHidden()
        {
            ShowScreen("LoadGame");
            InvokePrivate("RebuildMenuPanel");
            Assert.IsFalse(MenuPanel.activeSelf,
                "the rebuilt panel must not pop up over the load overlay");
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void RebuildMenuPanel_OnMain_KeepsTheMainPanelVisible()
        {
            ShowScreen("Main");
            InvokePrivate("RebuildMenuPanel");
            Assert.IsTrue(MenuPanel.activeSelf);
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void OptionsGoBack_FromASubScreen_ReturnsToOptions_ThenToMain()
        {
            ShowScreen("Audio");
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Options", CurrentScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Main", CurrentScreen());
            Assert.AreEqual(1, CountActiveRoots());
        }

        [Test]
        public void ASequenceOfScreens_NeverLeavesTwoRootsUp()
        {
            string[] sequence = { "Main", "Options", "Audio", "Video", "Gameplay", "Controls",
                                  "Options", "LoadGame", "Main", "Credits", "ClassSelector", "Main" };
            foreach (var name in sequence)
            {
                ShowScreen(name);
                Assert.AreEqual(1, CountActiveRoots(), "two roots were up after " + name);
            }
        }
    }
}
