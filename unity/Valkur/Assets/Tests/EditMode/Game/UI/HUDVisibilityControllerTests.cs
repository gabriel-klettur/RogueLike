using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Regression tests for <see cref="HUDVisibilityController"/>.
    ///
    /// Regressions guarded:
    ///   1. Editor open → HUD canvases hidden.
    ///   2. Editor close → only previously-hidden canvases restored (never activates
    ///      objects that were already inactive before the editor opened).
    ///   3. Multiple Hide() calls without Show() do not duplicate the restore list,
    ///      causing double-activation or incorrect state.
    ///   4. OnDisable while HUD is hidden must restore (defensive cleanup).
    ///   5. Race: editor already open at enable time → controller hides immediately.
    /// </summary>
    [TestFixture]
    public class HUDVisibilityControllerTests
    {
        // ── Reflection ───────────────────────────────────────────────────────

        private static readonly BindingFlags PrivInst =
            BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags PrivStatic =
            BindingFlags.NonPublic | BindingFlags.Static;

        // Backing field of the static event (compiler-generated field name matches
        // the event name in C# — confirmed safe to use with reflection in test code).
        private static readonly FieldInfo s_editorStateEventField =
            typeof(GameEditorManager).GetField("OnEditorStateChanged",
                PrivStatic | BindingFlags.Public);

        // Singleton instance field in the generic base class
        private static readonly FieldInfo s_singletonInstanceField =
            typeof(SingletonMonoBehaviour<GameEditorManager>)
                .GetField("_instance", PrivStatic);

        // ── Minimal IGameEditor mock ─────────────────────────────────────────

        private sealed class FakeEditor : GameEditorManager.IGameEditor
        {
            public string EditorName { get; } = "FakeTestEditor";
            public bool IsActive { get; private set; }
            public void Activate()   => IsActive = true;
            public void Deactivate() => IsActive = false;
        }

        // ── Test state ───────────────────────────────────────────────────────

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private GameObject _managerGo;
        private GameEditorManager _manager;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // Clear any leftover singleton from a prior test (Domain Reload is OFF).
            CleanupSingleton();
            // Also clear the static event in case a prior test left subscribers.
            ClearEditorStateEvent();
            // And drop stray HUD containers. The controller finds its targets with
            // GameObject.Find, which returns whichever "[UI]" it meets first — so a
            // container left behind by any earlier test (GameplaySceneSetup builds
            // one via GetSceneContainer, and EditMode objects outlive their test
            // with Domain Reload off) gets hidden instead of the one built here.
            DestroyStrayHudContainers();

            // Create a fresh GameEditorManager singleton.
            // In EditMode, Unity does NOT auto-call Awake/OnEnable — invoke manually.
            _managerGo = new GameObject("[GameEditorManager_Test]");
            _manager   = _managerGo.AddComponent<GameEditorManager>();
            typeof(SingletonMonoBehaviour<GameEditorManager>)
                .GetMethod("Awake", PrivInst)
                ?.Invoke(_manager, null);
            _sceneObjects.Add(_managerGo);
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy all GameObjects (controller + canvases + manager).
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            // Clean singleton ref and event so the next test starts fresh.
            CleanupSingleton();
            ClearEditorStateEvent();
        }

        // Names the controller looks up by GameObject.Find. Any survivor from an
        // earlier test would shadow this fixture's own containers.
        private static readonly string[] HudContainerNames =
        {
            "[UI]", "MusicHUDCanvas", "ToastCanvas",
        };

        private static void DestroyStrayHudContainers()
        {
            foreach (var name in HudContainerNames)
            {
                GameObject stray;
                while ((stray = GameObject.Find(name)) != null)
                    Object.DestroyImmediate(stray);
            }
        }

        // ── Singleton / event helpers ─────────────────────────────────────────

        private static void CleanupSingleton()
        {
            // If the field is instance-based in the generic base, set via reflection.
            s_singletonInstanceField?.SetValue(null, null);
        }

        private static void ClearEditorStateEvent()
        {
            // The backing field for a C# event stores the delegate chain.
            // We zero it so stale subscribers from prior tests can't fire.
            if (s_editorStateEventField != null)
                s_editorStateEventField.SetValue(null, null);
        }

        // ── Scene-building helpers ────────────────────────────────────────────

        /// <summary>Creates a fresh HUDVisibilityController on its own GO.</summary>
        private HUDVisibilityController CreateController()
        {
            var go = new GameObject("[HUDVisibilityController_Test]");
            var ctrl = go.AddComponent<HUDVisibilityController>();
            _sceneObjects.Add(go);
            // In EditMode, Unity does NOT auto-call OnEnable — invoke manually so the
            // controller subscribes to OnEditorStateChanged and checks the race condition.
            typeof(HUDVisibilityController)
                .GetMethod("OnEnable", PrivInst)
                ?.Invoke(ctrl, null);
            return ctrl;
        }

        /// <summary>Creates a named root GO, optionally active or inactive.</summary>
        private GameObject CreateHUDCanvas(string name, bool active = true)
        {
            var go = new GameObject(name);
            go.SetActive(active);
            _sceneObjects.Add(go);
            return go;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// When an editor opens, all active HUD canvases ([UI], MusicHUDCanvas,
        /// ToastCanvas) must be deactivated.
        /// </summary>
        [Test]
        public void EditorOpens_HidesUIContainerAndRootCanvases()
        {
            var ui    = CreateHUDCanvas("[UI]");
            var music = CreateHUDCanvas("MusicHUDCanvas");
            var toast = CreateHUDCanvas("ToastCanvas");

            // Controller subscribes to the event in OnEnable.
            var ctrl = CreateController();

            // Simulate editor open via the real API (fires OnEditorStateChanged(true)).
            var fake = new FakeEditor();
            _manager.Register(fake);
            _manager.OpenExclusive(fake);

            Assert.IsFalse(ui.activeSelf,
                "[UI] must be deactivated when an editor opens");
            Assert.IsFalse(music.activeSelf,
                "MusicHUDCanvas must be deactivated when an editor opens");
            Assert.IsFalse(toast.activeSelf,
                "ToastCanvas must be deactivated when an editor opens");

            _ = ctrl; // used
        }

        /// <summary>
        /// After editor closes, previously hidden objects are restored.
        /// </summary>
        [Test]
        public void EditorCloses_RestoresOnlyHiddenObjects()
        {
            var ui    = CreateHUDCanvas("[UI]");
            var music = CreateHUDCanvas("MusicHUDCanvas");
            var toast = CreateHUDCanvas("ToastCanvas");

            var ctrl = CreateController();
            var fake = new FakeEditor();
            _manager.Register(fake);

            // Open → Hide
            _manager.OpenExclusive(fake);
            Assert.IsFalse(ui.activeSelf,    "Precondition: [UI] hidden");
            Assert.IsFalse(music.activeSelf, "Precondition: MusicHUDCanvas hidden");
            Assert.IsFalse(toast.activeSelf, "Precondition: ToastCanvas hidden");

            // Close → Restore
            _manager.CloseAll();
            Assert.IsTrue(ui.activeSelf,    "[UI] must be restored after editor closes");
            Assert.IsTrue(music.activeSelf, "MusicHUDCanvas must be restored after editor closes");
            Assert.IsTrue(toast.activeSelf, "ToastCanvas must be restored after editor closes");

            _ = ctrl;
        }

        /// <summary>
        /// Objects that were already inactive BEFORE the editor opened must NOT be
        /// reactivated when the editor closes. The controller must only restore what
        /// it itself deactivated.
        /// </summary>
        [Test]
        public void EditorCloses_DoesNotActivateAlreadyInactiveObjects()
        {
            var ui    = CreateHUDCanvas("[UI]");
            // MusicHUDCanvas was already inactive (e.g. DebugHUD off by F1)
            var music = CreateHUDCanvas("MusicHUDCanvas", active: false);
            var toast = CreateHUDCanvas("ToastCanvas");

            var ctrl = CreateController();
            var fake = new FakeEditor();
            _manager.Register(fake);

            _manager.OpenExclusive(fake);
            _manager.CloseAll();

            Assert.IsTrue(ui.activeSelf,
                "[UI] was active before open → must be restored after close");
            Assert.IsFalse(music.activeSelf,
                "MusicHUDCanvas was ALREADY inactive before editor opened → " +
                "controller must NOT activate it on restore. " +
                "Regression: toggling on something the user explicitly hid.");
            Assert.IsTrue(toast.activeSelf,
                "ToastCanvas was active before open → must be restored after close");

            _ = ctrl;
        }

        /// <summary>
        /// Firing the editor-open event twice without an intervening close must NOT
        /// duplicate entries in the internal restore list, which would cause objects
        /// to be activated unexpectedly or list state to be corrupted.
        /// </summary>
        [Test]
        public void MultipleHidesWithoutShow_DoNotDuplicateRestoreList()
        {
            var ui    = CreateHUDCanvas("[UI]");
            var music = CreateHUDCanvas("MusicHUDCanvas");
            var toast = CreateHUDCanvas("ToastCanvas");

            var ctrl  = CreateController();
            var fake1 = new FakeEditor();
            var fake2 = new FakeEditor();
            _manager.Register(fake1);
            _manager.Register(fake2);

            // Open once
            _manager.OpenExclusive(fake1);
            Assert.IsFalse(ui.activeSelf, "Precondition: hidden after first open");

            // Open again (switches active editor, fires open event again)
            _manager.OpenExclusive(fake2);

            // Close
            _manager.CloseAll();

            // All three must be back to active exactly once (no double-toggle side effects)
            Assert.IsTrue(ui.activeSelf,
                "[UI] must be active after close — double-hide must not corrupt restore");
            Assert.IsTrue(music.activeSelf,
                "MusicHUDCanvas must be active after close");
            Assert.IsTrue(toast.activeSelf,
                "ToastCanvas must be active after close");

            _ = ctrl;
        }

        /// <summary>
        /// When the controller's GameObject is destroyed while the HUD is hidden
        /// (OnDisable fires), it must defensively restore all canvases it hid.
        /// </summary>
        [Test]
        public void OnDisable_WhileHidden_RestoresHUD()
        {
            var ui    = CreateHUDCanvas("[UI]");
            var music = CreateHUDCanvas("MusicHUDCanvas");
            var toast = CreateHUDCanvas("ToastCanvas");

            // Create the controller on its own GO so we can destroy it independently.
            var ctrlGo = new GameObject("[HUDVisibilityController_OnDisable_Test]");
            var ctrl   = ctrlGo.AddComponent<HUDVisibilityController>();
            _sceneObjects.Add(ctrlGo); // TearDown will clean up if test fails early
            // In EditMode, Unity does NOT auto-call OnEnable — invoke manually.
            typeof(HUDVisibilityController)
                .GetMethod("OnEnable", PrivInst)
                ?.Invoke(ctrl, null);

            var fake = new FakeEditor();
            _manager.Register(fake);
            _manager.OpenExclusive(fake);

            Assert.IsFalse(ui.activeSelf, "Precondition: HUD is hidden");

            // In EditMode, DestroyImmediate does NOT automatically call OnDisable.
            // Invoke it manually before destruction so the defensive restore runs.
            typeof(HUDVisibilityController)
                .GetMethod("OnDisable", PrivInst)
                ?.Invoke(ctrl, null);
            Object.DestroyImmediate(ctrlGo);
            _sceneObjects.Remove(ctrlGo); // already destroyed, remove from cleanup list

            Assert.IsTrue(ui.activeSelf,
                "[UI] must be restored when the HUDVisibilityController is destroyed " +
                "while the HUD was hidden (defensive OnDisable cleanup).");
            Assert.IsTrue(music.activeSelf,
                "MusicHUDCanvas must be restored on controller destruction");
            Assert.IsTrue(toast.activeSelf,
                "ToastCanvas must be restored on controller destruction");
        }

        /// <summary>
        /// Race condition: if a runtime editor is already open when the controller
        /// is enabled (e.g. scene loaded while an editor was open), the controller
        /// must detect this in OnEnable via HasInstance + AnyEditorActive and hide
        /// the HUD immediately — without waiting for an event.
        /// </summary>
        [Test]
        public void EditorAlreadyOpenAtEnable_HidesImmediately()
        {
            var ui    = CreateHUDCanvas("[UI]");
            var music = CreateHUDCanvas("MusicHUDCanvas");
            var toast = CreateHUDCanvas("ToastCanvas");

            // Open an editor BEFORE the controller is created.
            var fake = new FakeEditor();
            _manager.Register(fake);
            _manager.OpenExclusive(fake);

            // Verify manager state (precondition for the controller's OnEnable check).
            Assert.IsTrue(GameEditorManager.HasInstance,
                "Precondition: GameEditorManager.HasInstance must be true");
            Assert.IsTrue(GameEditorManager.Instance.AnyEditorActive,
                "Precondition: AnyEditorActive must be true before controller is created");

            // Now create the controller — its OnEnable must catch the race.
            var ctrl = CreateController();

            Assert.IsFalse(ui.activeSelf,
                "[UI] must be hidden immediately in OnEnable when an editor is already open. " +
                "Without this fix, the HUD stays visible until the NEXT editor-open event, " +
                "which never fires because the editor opened before the controller existed.");
            Assert.IsFalse(music.activeSelf,
                "MusicHUDCanvas must be hidden immediately on enable (race case)");
            Assert.IsFalse(toast.activeSelf,
                "ToastCanvas must be hidden immediately on enable (race case)");

            _ = ctrl;
        }

        /// <summary>
        /// Source-level guard: verifies the OnDisable defensive restore path exists
        /// and OnEnable subscribes + checks HasInstance + AnyEditorActive.
        /// </summary>
        [Test]
        public void SourceCode_OnEnableChecksRaceAndOnDisableRestoresDefensively()
        {
            string scriptPath = System.IO.Path.Combine(
                Application.dataPath,
                "_Project", "Scripts", "UI", "HUD", "HUDVisibilityController.cs");
            Assert.IsTrue(System.IO.File.Exists(scriptPath),
                $"Production script not found at {scriptPath}");

            string src = System.IO.File.ReadAllText(scriptPath);

            // OnEnable must subscribe to the event
            Assert.IsTrue(src.Contains("OnEditorStateChanged +="),
                "OnEnable must subscribe to GameEditorManager.OnEditorStateChanged");

            // OnEnable must check the race condition
            Assert.IsTrue(src.Contains("HasInstance"),
                "OnEnable must check GameEditorManager.HasInstance to handle the race " +
                "where an editor is already open when the controller is enabled");
            Assert.IsTrue(src.Contains("AnyEditorActive"),
                "OnEnable must check AnyEditorActive to detect already-open editors");

            // OnDisable must unsubscribe
            Assert.IsTrue(src.Contains("OnEditorStateChanged -="),
                "OnDisable must unsubscribe from OnEditorStateChanged");

            // OnDisable must call Show() defensively
            Assert.IsTrue(src.Contains("_hudHidden") && src.Contains("Show()"),
                "OnDisable must call Show() defensively when _hudHidden is true");

            // Hide() must guard against duplicate calls
            Assert.IsTrue(src.Contains("if (_hudHidden) return"),
                "Hide() must be idempotent — guard against duplicate calls with " +
                "'if (_hudHidden) return' to prevent list corruption");
        }
    }
}
