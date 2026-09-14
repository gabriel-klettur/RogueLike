using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.General;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.General
{
    /// <summary>
    /// The findings of the 2026-09-05 launcher audit, pinned.
    ///
    /// <para>The one that matters most: since the F-row was retired the launcher is the ONLY
    /// way into any editor, and the chrome close button every <see cref="DraggablePanel"/>
    /// furnishes hid the launcher's panel without deactivating the launcher — measured in
    /// EditMode: after that click ESC then ESC left <c>IsActive=true</c> with the panel
    /// inactive, and the remembered "closed" state hid it again on the next boot. A
    /// soft-lock of the whole editor surface, persisted in PlayerPrefs.</para>
    /// </summary>
    [TestFixture]
    public class GeneralEditorLauncherHardeningTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Scaffolding (mirrors GeneralEditorManagerTests) ─────────────────────

        private static FieldInfo StaticField(System.Type t, string name)
        {
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T Private<T>(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return (T)f.GetValue(obj);
                t = t.BaseType;
            }
            return default;
        }

        private GeneralEditorManager CreateLauncher()
        {
            StaticField(typeof(GeneralEditorManager), "_instance")?.SetValue(null, null);
            StaticField(typeof(GameEditorManager),    "_instance")?.SetValue(null, null);

            var go   = new GameObject("TestGeneralEditor");
            var comp = go.AddComponent<GeneralEditorManager>();
            _sceneObjects.Add(go);

            // EditMode AddComponent does not reliably run Awake: seat the singleton and
            // pump OnSingletonAwake by hand, exactly as GeneralEditorManagerTests does.
            var instField = StaticField(typeof(GeneralEditorManager), "_instance");
            if (instField != null && instField.GetValue(null) == null) instField.SetValue(null, comp);
            typeof(GeneralEditorManager)
                .GetMethod("OnSingletonAwake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(comp, null);

            var mgrGo = GameObject.Find("[GameEditorManager]");
            if (mgrGo != null)
            {
                _sceneObjects.Add(mgrGo);
                var mgrField = StaticField(typeof(GameEditorManager), "_instance");
                if (mgrField != null && mgrField.GetValue(null) == null)
                    mgrField.SetValue(null, mgrGo.GetComponent<GameEditorManager>());
            }

            Track("GeneralEditorCanvas");
            return comp;
        }

        private void Track(string rootName)
        {
            var go = GameObject.Find(rootName);
            if (go != null && !_sceneObjects.Contains(go)) _sceneObjects.Add(go);
        }

        [SetUp]
        public void SetUp()
        {
            EscapeOwnership.ResetForTests();
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Track("GeneralEditorConfirmCanvas");
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            EscapeOwnership.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        private static GameObject PanelOf(GeneralEditorManager ed) => Private<GameObject>(ed, "_panelRoot");

        // ── The close path ───────────────────────────────────────────────────────

        [Test]
        public void ChromeCloseButton_IsSuppressed_OnTheLauncherPanel()
        {
            var ed    = CreateLauncher();
            var panel = PanelOf(ed);
            var drag  = panel.GetComponent<DraggablePanel>();

            Assert.IsFalse(drag.ShowCloseButton,
                "The chrome close button hides the PANEL and remembers that choice. On the " +
                "launcher that is a persisted soft-lock of every editor: it must opt out.");

            drag.EnsureChrome();
            Assert.IsNull(panel.transform.Find("PanelHeader/PanelCloseButton"),
                "Opting out must actually keep the chrome button off the header.");
        }

        [Test]
        public void HeaderCloseButton_DeactivatesTheLauncher_AndLeavesThePanelObjectActive()
        {
            var ed    = CreateLauncher();
            var mgr   = GameEditorManager.Instance;
            var panel = PanelOf(ed);

            mgr.OpenExclusive(ed);
            Assert.IsTrue(ed.IsActive);

            panel.transform.Find("PanelHeader/CloseBtn").GetComponent<Button>().onClick.Invoke();

            Assert.IsFalse(ed.IsActive, "The launcher's own X closes it as an EDITOR.");
            Assert.IsNull(mgr.ActiveEditor, "…and the manager must agree, or player input stays frozen.");
            Assert.IsTrue(panel.activeSelf,
                "The panel object is never hidden — only the canvas is. A hidden panel under a " +
                "shown canvas is exactly the invisible-launcher state the audit measured.");

            mgr.OpenExclusive(ed);
            Assert.IsTrue(Private<Canvas>(ed, "_canvas").gameObject.activeSelf && panel.activeSelf,
                "Reopening after the X must show the panel again.");
        }

        [Test]
        public void ExactlyOneCloseControl_InTheHeader()
        {
            var ed    = CreateLauncher();
            var panel = PanelOf(ed);
            panel.GetComponent<DraggablePanel>().EnsureChrome();

            var buttons = panel.transform.Find("PanelHeader").GetComponentsInChildren<Button>(true);
            Assert.AreEqual(1, buttons.Length,
                "The launcher shipped with TWO X buttons side by side — its own and the chrome's — " +
                "and only one of them closed the launcher.");
        }

        // ── Height ───────────────────────────────────────────────────────────────

        [Test]
        public void PanelHeight_IsDerivedFromTheRegistry_NotAConstant()
        {
            var ed      = CreateLauncher();
            var panel   = PanelOf(ed);
            var entries = GeneralEditorRegistry.BuildEntries();

            var group = panel.transform.Find("Content").GetComponent<VerticalLayoutGroup>();
            float expected = GeneralEditorManager.ComputePanelHeight(
                entries, ed.ActiveTab, group.padding.top + group.padding.bottom, group.spacing);

            Assert.AreEqual(expected, panel.GetComponent<RectTransform>().sizeDelta.y, 0.01f,
                "The panel must be exactly as tall as the OPEN tab wants, read off the built layout.");
        }

        /// <summary>
        /// The panel holds one tab, and a tab that holds more rows is taller.
        ///
        /// <para>This assertion has now been rewritten twice, and both rewrites were correct
        /// rather than accommodating: it first pinned the stacked SUM of three sections, then
        /// the TALLEST of them, and now the OPEN one. Each time the layout genuinely changed
        /// and the old shape would have gone on passing while measuring something the panel no
        /// longer does.</para>
        /// </summary>
        [Test]
        public void EachTab_IsSizedByItsOwnRowCount()
        {
            var entries = GeneralEditorRegistry.BuildEntries();

            float editors = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Editors);
            float tools   = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Tools);
            float game    = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Game);

            Assert.Greater(editors, tools, "Nineteen entries must want more room than five.");
            Assert.Greater(editors, game,  "Nineteen entries must want more room than five.");

            float rows = GeneralEditorManager.GridHeight(entries.Count(e => e.Section == GeneralEditorSection.Editors));
            Assert.Greater(editors, rows, "The tallest tab must clear its own grid plus the chrome.");
        }

        [Test]
        public void OneMoreRow_InATab_GrowsThatTab_ByExactlyOneRow()
        {
            var entries = GeneralEditorRegistry.BuildEntries();

            // How many entries it takes to START a new row is arithmetic on the column count,
            // never a number to hard-code: nineteen entries fill seven rows of three and so do
            // twenty and twenty-one, so a test that added exactly one — or exactly two —
            // measured a panel that correctly did not move. Fill the partial last row, then
            // one more; that one is the eighth row.
            int count  = entries.Count(e => e.Section == GeneralEditorSection.Tools);
            int cols   = GeneralEditorManager.GRID_COLUMNS;
            int filled = ((count + cols - 1) / cols) * cols;
            int toAdd  = filled + 1 - count;

            var more = new List<GeneralEditorEntry>(entries);
            for (int i = 0; i < toAdd; i++)
                more.Add(new GeneralEditorEntry($"Extra{i}", GeneralEditorSection.Tools, () => { }));

            float before = GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Tools);
            float after  = GeneralEditorManager.ComputePanelHeight(more,    GeneralEditorSection.Tools);

            Assert.AreEqual(GeneralEditorManager.BUTTON_HEIGHT + GeneralEditorManager.GRID_SPACING,
                after - before, 0.01f,
                "A registry entry that opens a new row must grow its own tab by one row, not clip.");
        }

        [Test]
        public void OneMoreRow_InOneTab_DoesNotMoveAnother()
        {
            var entries = GeneralEditorRegistry.BuildEntries();

            var more = new List<GeneralEditorEntry>(entries);
            for (int i = 0; i < 4; i++)
                more.Add(new GeneralEditorEntry($"Extra{i}", GeneralEditorSection.Game, () => { }));

            Assert.AreEqual(
                GeneralEditorManager.ComputePanelHeight(entries, GeneralEditorSection.Tools),
                GeneralEditorManager.ComputePanelHeight(more,    GeneralEditorSection.Tools), 0.01f,
                "A tab is sized by its OWN rows; growing another one must not reach it.");
        }

        [Test]
        public void RestoreWorkspace_ReappliesTheDerivedHeight()
        {
            var ed    = CreateLauncher();
            var panel = PanelOf(ed);
            var rt    = panel.GetComponent<RectTransform>();
            float derived = rt.sizeDelta.y;

            // A workspace document restored from a session with fewer entries hands back
            // the size the panel HAD.
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 100f);
            ((IProvidesWorkspaceState)ed).RestoreWorkspace(new EditorWorkspace { editorName = "General" });

            Assert.AreEqual(derived, rt.sizeDelta.y, 0.01f,
                "Position may be remembered; height is always re-derived from today's registry.");
        }

        [Test]
        public void Launcher_ProvidesWorkspaceState_RootedAtItsCanvas()
        {
            var ed = CreateLauncher();
            var ws = ed as IProvidesWorkspaceState;
            Assert.IsNotNull(ws, "The launcher joins the workspace layer so a dragged position survives.");
            Assert.AreSame(Private<Canvas>(ed, "_canvas").transform, ws.WorkspaceRoot);
        }

        // ── Keyboard ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Rewritten when the entries became talent-style tiles. The old form pinned the
        /// ColorBlock's selectedColor to the hover tint, because uGUI's near-white default read as
        /// a missing texture. The tiles carry no colour transition at all now — selection is drawn
        /// by the tile's socket — so the equivalent promise is: no transition that could paint the
        /// default white, and keyboard selection lights the socket exactly as hover does.
        /// </summary>
        [Test]
        public void EntryTiles_KeyboardSelection_LightsTheSocket_WithNoColourTransition()
        {
            var ed    = CreateLauncher();
            var panel = PanelOf(ed);

            var grids = panel.GetComponentsInChildren<GridLayoutGroup>(true);
            var buttons = grids.SelectMany(g => g.GetComponentsInChildren<Button>(true)).ToList();
            Assert.IsNotEmpty(buttons);

            foreach (var b in buttons)
            {
                Assert.AreEqual(Selectable.Transition.None, b.transition,
                    $"'{b.name}': a colour transition would paint uGUI's near-white selectedColor over the tile.");
                var tile = b.GetComponent<GeneralEditorTile>();
                Assert.IsNotNull(tile, $"'{b.name}' is not a launcher tile.");
                tile.OnSelect(null);
                tile.Tick(0.5f);
                Assert.IsTrue(tile.Socket.Brackets, $"'{b.name}': keyboard selection did not light its socket.");
                tile.OnDeselect(null);
            }
        }

        // ── Confirm dialog ───────────────────────────────────────────────────────

        [Test]
        public void Confirm_ClaimsEscape_AndConfirmRunsTheAction_Once()
        {
            var ed = CreateLauncher();
            int fired = 0;

            ed.Confirm("leave?", () => fired++);
            Assert.IsTrue(ed.IsConfirmOpen);
            Assert.IsTrue(EscapeOwnership.IsClaimed,
                "While the dialog is up, Escape belongs to it — not to the launcher toggle, " +
                "not to the character sheet.");

            Private<Button>(ed, "_confirmOk").onClick.Invoke();
            Assert.AreEqual(1, fired);
            Assert.IsFalse(ed.IsConfirmOpen);
            Assert.IsFalse(EscapeOwnership.IsClaimedOn(Time.frameCount + 1),
                "The claim must go with the dialog.");

            Private<Button>(ed, "_confirmOk").onClick.Invoke();
            Assert.AreEqual(1, fired, "A stale click on the hidden dialog must not run the action again.");
        }

        [Test]
        public void Confirm_Cancel_ReopensTheLauncher()
        {
            var ed  = CreateLauncher();
            var mgr = GameEditorManager.Instance;
            bool fired = false;

            mgr.OpenExclusive(ed);
            ed.Deactivate();                       // what a ClosesLauncher entry does first
            ed.Confirm("leave?", () => fired = true);

            Private<Button>(ed, "_confirmCancel").onClick.Invoke();

            Assert.IsFalse(fired);
            Assert.IsFalse(ed.IsConfirmOpen);
            Assert.IsTrue(ed.IsActive, "Cancel brings the launcher back, like the Map Backups browser does.");
            Assert.AreSame(ed, mgr.ActiveEditor);
        }

        [Test]
        public void ExitToMenu_GoesThroughTheConfirmDialog()
        {
            var ed    = CreateLauncher();
            var entry = GeneralEditorRegistry.BuildEntries().First(e => e.Label == "Exit to Menu");

            // With a launcher alive the entry must ASK, not leave. (Without one it would
            // load the main menu, which is why this test needs the launcher to exist.)
            entry.OnClick();

            Assert.IsTrue(ed.IsConfirmOpen,
                "Leaving the session is the one launcher action with no way back; it asks first.");
        }
    }
}
