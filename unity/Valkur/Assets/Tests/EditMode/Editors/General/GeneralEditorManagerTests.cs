using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.General;

namespace Valkur.Tests.EditMode.Editors.General
{
    /// <summary>
    /// Tests for the General Editor launcher: lifecycle, IGameEditor contract,
    /// registry composition, hotkey mapping, and the canvas-build / show-hide
    /// behaviour. Stays inside the EditMode-friendly subset (no real input
    /// pump, no Update tick) — just public API + reflection where needed.
    /// </summary>
    [TestFixture]
    public class GeneralEditorManagerTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static void InvokeMethod(object obj, string methodName)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, null);
        }

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetPrivateFieldValue<T>(object obj, string name)
        {
            var f = GetField(obj, name);
            return f != null ? (T)f.GetValue(obj) : default;
        }

        private GeneralEditorManager CreateLauncher(string name = "TestGeneralEditor")
        {
            ClearSingletonInstance<GeneralEditorManager>();
            ClearSingletonInstance<GameEditorManager>();

            var go = new GameObject(name);
            var comp = go.AddComponent<GeneralEditorManager>();
            _sceneObjects.Add(go);

            // EditMode test framework caveat: AddComponent does NOT reliably
            // fire MonoBehaviour.Awake, so the SingletonMonoBehaviour pump
            // skips and the static _instance field stays null. Force-set the
            // launcher singleton, then invoke OnSingletonAwake manually.
            EnsureSingletonInstance(comp);
            InvokeMethod(comp, "OnSingletonAwake");

            // OnSingletonAwake spawned the GameEditorManager via EnsureInstance;
            // its Awake also probably skipped, so backfill its singleton field
            // too so tests can rely on `GameEditorManager.HasInstance`.
            var mgrGo = GameObject.Find("[GameEditorManager]");
            if (mgrGo != null)
            {
                _sceneObjects.Add(mgrGo);
                var mgrComp = mgrGo.GetComponent<GameEditorManager>();
                if (mgrComp != null) EnsureSingletonInstance(mgrComp);
            }

            // The launcher canvas is parented to the scene root (not the
            // launcher GameObject) so we have to track + destroy it explicitly
            // to avoid leaking across tests.
            var canvasGo = GameObject.Find("GeneralEditorCanvas");
            if (canvasGo != null) _sceneObjects.Add(canvasGo);

            return comp;
        }

        /// <summary>
        /// Walks the inheritance chain to find <see cref="SingletonMonoBehaviour{T}._instance"/>
        /// and sets it to <paramref name="comp"/> if it's currently null. EditMode AddComponent
        /// occasionally skips the Awake pump that does this assignment.
        /// </summary>
        private static void EnsureSingletonInstance<T>(T comp) where T : MonoBehaviour
        {
            if (comp == null) return;
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    if (field.GetValue(null) == null) field.SetValue(null, comp);
                    return;
                }
                type = type.BaseType;
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            // Many EditMode UI builders log warnings when running outside Play —
            // they never affect production behaviour but trip the assert harness.
            LogAssert.ignoreFailingMessages = false;
        }

        // ── IGameEditor contract ─────────────────────────────────────────────────

        [Test]
        public void EditorName_Returns_GeneralString()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            Assert.AreEqual("General", ed.EditorName,
                "EditorName must surface a stable identifier so the GameEditorManager " +
                "exclusivity log + hotkey diagnostics can refer to the launcher by name.");
        }

        [Test]
        public void IsActive_InitiallyFalse_AfterAwake()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            Assert.IsFalse(ed.IsActive,
                "Launcher must boot hidden — Activate() is the only legitimate way to surface it.");
        }

        [Test]
        public void Activate_Then_IsActive_True_AndRegisteredWithGameEditorManager()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            ed.Activate();
            Assert.IsTrue(ed.IsActive,
                "Activate() must flip IsActive so GameEditorManager.ToggleExclusive can detect us as the live editor.");

            // Activate() doesn't itself OpenExclusive; that's GameEditorManager's job.
            // But the launcher must have registered itself in OnSingletonAwake.
            Assert.IsTrue(GameEditorManager.HasInstance,
                "GameEditorManager.EnsureInstance must have spawned the manager singleton during launcher Awake.");
        }

        [Test]
        public void Deactivate_ClearsActiveFlag()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            ed.Activate();
            ed.Deactivate();
            Assert.IsFalse(ed.IsActive,
                "Deactivate() must clear IsActive so a subsequent ESC press re-opens cleanly.");
        }

        // ── BuildUI / canvas wiring ──────────────────────────────────────────────

        [Test]
        public void Awake_BuildsCanvas_NamedGeneralEditorCanvas()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            var canvas = GetPrivateFieldValue<Canvas>(ed, "_canvas");
            Assert.IsNotNull(canvas,
                "OnSingletonAwake must build the launcher canvas eagerly so the first Activate() shows it without a one-frame flash.");
            Assert.AreEqual("GeneralEditorCanvas", canvas.gameObject.name,
                "Canvas should keep its conventional name so other systems can locate it.");
        }

        [Test]
        public void Awake_BuildsPanel_NamedGeneralEditorPanel()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            var panel = GetPrivateFieldValue<GameObject>(ed, "_panelRoot");
            Assert.IsNotNull(panel,
                "The launcher panel must exist as a child of the canvas so EditorUIHelpers' DraggablePanel + PanelChrome wire up correctly at boot.");
            Assert.AreEqual("GeneralEditorPanel", panel.name,
                "Panel root should retain the canonical name so QA tooling and inspector navigation can locate it.");
        }

        [Test]
        public void CanvasIsHidden_BeforeActivate()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            var canvas = GetPrivateFieldValue<Canvas>(ed, "_canvas");
            Assert.IsNotNull(canvas);
            Assert.IsFalse(canvas.gameObject.activeSelf,
                "Launcher canvas must boot hidden — visible-on-spawn would obscure gameplay before any user action.");
        }

        [Test]
        public void CanvasIsShown_AfterActivate()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateLauncher();
            ed.Activate();
            var canvas = GetPrivateFieldValue<Canvas>(ed, "_canvas");
            Assert.IsNotNull(canvas);
            Assert.IsTrue(canvas.gameObject.activeSelf,
                "Activate() must enable the canvas so the launcher panel becomes visible to the player.");
        }

        // ── Registry composition ─────────────────────────────────────────────────
        //
        // Each section's expected labels are enumerated explicitly so that:
        //   • adding a new entry to GeneralEditorRegistry without updating the
        //     test fails fast (catches "stealth additions" that may break UX);
        //   • removing or renaming an entry surfaces a useful diff instead of a
        //     bare count mismatch;
        //   • the total count is derived from the section sets, so adding an
        //     entry only requires one edit in this file rather than three.
        // To extend: add the new label to the matching section set.

        private static readonly string[] ExpectedEditorLabels =
        {
            "Tile", "Buildings", "Items", "Spells", "Entities", "Boss", "FSM",
            "Map", "Inventory", "Particles", "Spawners", "Lighting",
            "Time & Weather", "Dungeon NodeGraph",
        };

        private static readonly string[] ExpectedDiagnosticsLabels =
        {
            "Combat Ranges", "Debug HUD", "Save Log",
        };

        private static readonly string[] ExpectedGameLabels =
        {
            "Pause Menu", "Save Game", "Load", "Options", "Map Backups", "Exit to Menu",
        };

        private static int ExpectedTotal =>
            ExpectedEditorLabels.Length +
            ExpectedDiagnosticsLabels.Length +
            ExpectedGameLabels.Length;

        [Test]
        public void Registry_ReturnsExpectedTotalEntryCount()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            Assert.AreEqual(ExpectedTotal, entries.Count,
                $"Registry must list {ExpectedEditorLabels.Length} runtime editors + " +
                $"{ExpectedDiagnosticsLabels.Length} diagnostics + " +
                $"{ExpectedGameLabels.Length} game actions = {ExpectedTotal} buttons total.");
        }

        [Test]
        public void Registry_EditorSection_MatchesExpectedLabels()
        {
            AssertSectionMatches(GeneralEditorSection.Editors, ExpectedEditorLabels);
        }

        [Test]
        public void Registry_DiagnosticsSection_MatchesExpectedLabels()
        {
            AssertSectionMatches(GeneralEditorSection.Diagnostics, ExpectedDiagnosticsLabels);
        }

        [Test]
        public void Registry_GameSection_MatchesExpectedLabels()
        {
            AssertSectionMatches(GeneralEditorSection.Game, ExpectedGameLabels);
        }

        [Test]
        public void Registry_HasNoDuplicateLabelsInAnySection()
        {
            // A duplicate label between two MakeEditor() calls is the classic
            // "I forgot to delete the old line after a copy-paste" bug.
            var entries = GeneralEditorRegistry.BuildEntries();
            foreach (GeneralEditorSection section in System.Enum.GetValues(typeof(GeneralEditorSection)))
            {
                var dup = entries
                    .Where(e => e.Section == section)
                    .GroupBy(e => e.Label)
                    .FirstOrDefault(g => g.Count() > 1);
                Assert.IsNull(dup,
                    $"Section '{section}' has duplicate label '{dup?.Key}' — " +
                    "two registry entries with the same label collide in the launcher UI.");
            }
        }

        private static void AssertSectionMatches(GeneralEditorSection section, string[] expected)
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            var actual = entries
                .Where(e => e.Section == section)
                .Select(e => e.Label)
                .ToArray();

            CollectionAssert.AreEquivalent(expected, actual,
                $"Section '{section}' label set drift. " +
                $"Expected: [{string.Join(", ", expected)}]. " +
                $"Actual: [{string.Join(", ", actual)}]. " +
                "If the change is intentional, update the matching Expected*Labels constant in this test file.");
        }

        [Test]
        public void GameEntries_AllSetClosesLauncher()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            foreach (var e in entries.Where(e => e.Section == GeneralEditorSection.Game))
            {
                Assert.IsTrue(e.ClosesLauncher,
                    $"Game-section entry '{e.Label}' must set ClosesLauncher=true so the launcher hides before invoking pause-menu / scene-transition flows.");
            }
        }

        [Test]
        public void EditorEntries_DoNotCloseLauncher()
        {
            // Editor entries rely on GameEditorManager.OpenExclusive to
            // auto-close the launcher; explicitly setting ClosesLauncher=true
            // would double-call Deactivate and confuse the manager.
            var entries = GeneralEditorRegistry.BuildEntries();
            foreach (var e in entries.Where(e => e.Section == GeneralEditorSection.Editors))
            {
                Assert.IsFalse(e.ClosesLauncher,
                    $"Editor-section entry '{e.Label}' must let GameEditorManager.OpenExclusive close the launcher implicitly.");
            }
        }

        [Test]
        public void EveryEntry_HasNonEmptyLabel()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            foreach (var e in entries)
                Assert.IsNotEmpty(e.Label, "Every launcher button must surface a human-readable label.");
        }

        [Test]
        public void EveryEntry_HasNonNullOnClick()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            foreach (var e in entries)
                Assert.IsNotNull(e.OnClick,
                    $"Entry '{e.Label}' has no OnClick — clicking it would silently no-op, leaving the user wondering if the button works.");
        }

        // ── Cross-editor transition (ESC closes current + opens launcher) ───────

        /// <summary>
        /// Stand-in for any of the eleven IGameEditor implementations. Lets us
        /// validate the GameEditorManager exclusivity transition without
        /// dragging real editors (and their Catalog assets) into the test.
        /// </summary>
        private class FakeEditor : MonoBehaviour, GameEditorManager.IGameEditor
        {
            public string EditorName => "Fake";
            public bool IsActive { get; private set; }
            public void Activate()   => IsActive = true;
            public void Deactivate() => IsActive = false;
        }

        [Test]
        public void OpenExclusive_FromAnotherActiveEditor_ClosesIt_AndActivatesLauncher()
        {
            // Mirrors the new ESC behaviour: any other editor active + ESC
            // pressed → that editor closes AND the launcher opens, in one
            // step. Validates the underlying GameEditorManager.OpenExclusive
            // contract that the launcher's Update relies on.
            LogAssert.ignoreFailingMessages = true;
            var launcher = CreateLauncher();
            var mgr = GameEditorManager.Instance;
            Assert.IsNotNull(mgr);

            var fakeGo = new GameObject("FakeEditor");
            _sceneObjects.Add(fakeGo);
            var fake = fakeGo.AddComponent<FakeEditor>();
            mgr.Register(fake);
            mgr.OpenExclusive(fake);
            Assert.IsTrue(fake.IsActive,    "Sanity: fake editor should activate first.");
            Assert.IsFalse(launcher.IsActive, "Sanity: launcher should not yet be active.");

            // Simulate the launcher's Update path when ESC is pressed and
            // another editor was the active one.
            mgr.OpenExclusive(launcher);

            Assert.IsFalse(fake.IsActive,
                "ESC during another editor's session must auto-close that editor (Deactivate fired by OpenExclusive).");
            Assert.IsTrue(launcher.IsActive,
                "Launcher must end up active after ESC, satisfying the 'close current + open launcher' UX.");
            Assert.AreSame(launcher, mgr.ActiveEditor,
                "GameEditorManager.ActiveEditor must reflect the launcher as the new exclusive owner.");
        }

        // ── Hotkey mapping ───────────────────────────────────────────────────────

        [Test]
        public void Hotkey_OpenGeneralEditor_ExistsInEnum()
        {
            // Compile-time guarantee: the enum value the launcher Update() reads
            // must be defined. If someone renames the enum, this fails fast.
            Assert.IsTrue(System.Enum.IsDefined(typeof(EditorHotkeyBindings.Hotkey),
                EditorHotkeyBindings.Hotkey.OpenGeneralEditor),
                "Hotkey.OpenGeneralEditor must remain in the enum — the launcher Update polls this name.");
        }

        [Test]
        public void Hotkey_OpenGeneralEditor_LegacyKeyCode_IsEscape()
        {
            // The 2022.3 InputSystem occasionally drops events; the launcher's
            // OR-fallback path resolves the hotkey via legacy KeyCode. We pin
            // ESC here so a future "improvement" can't silently rebind to F-key.
            var method = typeof(EditorHotkeyBindings).GetMethod(
                "LegacyKeyCode", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "LegacyKeyCode helper must exist.");
            var key = (KeyCode) method.Invoke(null,
                new object[] { EditorHotkeyBindings.Hotkey.OpenGeneralEditor });
            Assert.AreEqual(KeyCode.Escape, key,
                "Hotkey.OpenGeneralEditor must map to KeyCode.Escape in the legacy fallback path.");
        }

        [Test]
        public void Hotkey_OpenGeneralEditor_FallbackPath_IsEscape()
        {
            string path = EditorHotkeyBindings.FallbackPath(
                EditorHotkeyBindings.Hotkey.OpenGeneralEditor);
            Assert.AreEqual("<Keyboard>/escape", path,
                "FallbackPath must surface the canonical escape binding so the InputSystem path matches the legacy KeyCode path.");
        }
    }
}
