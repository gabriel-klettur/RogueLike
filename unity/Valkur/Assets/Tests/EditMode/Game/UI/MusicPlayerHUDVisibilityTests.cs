using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for the <see cref="MusicPlayerHUD"/> visibility contract.
    ///
    /// Locks in three regressions we hit during the HUD bar refactor:
    ///   1. Player must default to HIDDEN on a fresh launch (no PlayerPref yet) —
    ///      the user opens it via the HUDIconBar music icon.
    ///   2. <c>Update()</c> must NOT bring CanvasGroup alpha back to 1 while
    ///      <c>_panelHidden</c> is true. Forgetting this leaves the panel visible
    ///      while <c>blocksRaycasts</c> is false → looks like "buttons don't work".
    ///   3. Toggling visibility must update alpha + blocksRaycasts + interactable
    ///      together — never just one of them.
    /// </summary>
    public class MusicPlayerHUDVisibilityTests
    {
        // ── Reflection ───────────────────────────────────────────────────────
        private static readonly FieldInfo s_panelHiddenField =
            typeof(MusicPlayerHUD).GetField("_panelHidden",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_cgField =
            typeof(MusicPlayerHUD).GetField("_cg",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_togglePanel =
            typeof(MusicPlayerHUD).GetMethod("TogglePanel",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_onCloseClicked =
            typeof(MusicPlayerHUD).GetMethod("OnCloseClicked",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_applyPanelVisibility =
            typeof(MusicPlayerHUD).GetMethod("ApplyPanelVisibility",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_update =
            typeof(MusicPlayerHUD).GetMethod("Update",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_awake =
            typeof(MusicPlayerHUD).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Test state ───────────────────────────────────────────────────────
        private GameObject     _go;
        private MusicPlayerHUD _hud;
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // PrefKey we wipe between tests to ensure fresh-launch behavior.
        private const string PrefKeyHidden = "valkur.musichud.hidden";

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // CRITICAL: wipe persisted state so each test starts as "fresh launch".
            PlayerPrefs.DeleteKey(PrefKeyHidden);

            CreateHud();
        }

        // Creates a fresh HUD instance with Awake invoked. Tests that need to
        // simulate a different persisted-state launch can DestroyImmediate the
        // existing GO, set the relevant prefs, then call CreateHud() again.
        //
        // We use Reflection.Invoke for Awake (not SendMessage) — SendMessage
        // emits an internal `[Assert] ShouldRunBehaviour()` engine assertion
        // when the next Behaviour created in the same EditMode tick is also
        // invoked via SendMessage (observed in PersistedHiddenFalse_OverridesDefault
        // after DestroyImmediate of the prior GO). LogAssert.ignoreFailingMessages
        // does NOT cover LogType.Assert, so the runner records it as an unhandled
        // log and fails the test. Direct reflection sidesteps that path.
        private void CreateHud()
        {
            _go = new GameObject("TestMusicPlayerHUD", typeof(RectTransform));
            _sceneObjects.Add(_go);
            _hud = _go.AddComponent<MusicPlayerHUD>();
            s_awake.Invoke(_hud, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            PlayerPrefs.DeleteKey(PrefKeyHidden);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private bool GetPanelHidden() => (bool)s_panelHiddenField.GetValue(_hud);
        private CanvasGroup GetCg()   => (CanvasGroup)s_cgField.GetValue(_hud);

        private void TogglePanel()      => s_togglePanel.Invoke(_hud, null);
        private void OnCloseClicked()   => s_onCloseClicked.Invoke(_hud, null);
        private void ApplyVisibility()  => s_applyPanelVisibility.Invoke(_hud, null);
        private void InvokeUpdate()     => s_update.Invoke(_hud, null);

        // ── Default state on fresh launch ───────────────────────────────────

        [Test]
        public void FreshLaunch_PanelHidden_IsTrue()
        {
            // No PlayerPref → default must be hidden (user opens via bar icon).
            Assert.IsTrue(GetPanelHidden(),
                "On a fresh launch the music player must START closed. " +
                "Default _panelHidden must be true.");
        }

        [Test]
        public void FreshLaunch_CanvasGroup_AlphaIsZero()
        {
            var cg = GetCg();
            Assert.IsTrue(cg != null, "BuildUI must add a CanvasGroup");
            Assert.AreEqual(0f, cg.alpha, 0.01f,
                "Hidden panel must have alpha=0 right after BuildUI");
        }

        [Test]
        public void FreshLaunch_CanvasGroup_BlocksRaycasts_IsFalse()
        {
            var cg = GetCg();
            Assert.IsFalse(cg.blocksRaycasts,
                "Hidden panel must NOT block raycasts — clicks must fall through to scene below");
        }

        [Test]
        public void FreshLaunch_CanvasGroup_Interactable_IsFalse()
        {
            var cg = GetCg();
            Assert.IsFalse(cg.interactable,
                "Hidden panel must be non-interactable");
        }

        // ── Persisted state overrides default ───────────────────────────────

        [Test]
        public void PersistedHiddenFalse_OverridesDefault()
        {
            // Simulate a saved "user previously opened the panel" state.
            // Re-create the component so Awake reads the new value.
            Object.DestroyImmediate(_go);
            PlayerPrefs.SetInt(PrefKeyHidden, 0); // 0 = visible
            CreateHud();

            Assert.IsFalse(GetPanelHidden(),
                "Persisted PrefKeyHidden=0 must override the default-true initialization");
        }

        // ── Update() must respect _panelHidden ──────────────────────────────

        [Test]
        public void Update_WhilePanelHidden_DoesNotRestoreAlpha()
        {
            // Regression: previously Update() set alpha = (hideWhenIdle && !active) ? 0 : 1
            // — ignoring _panelHidden. The panel became visually visible while
            // blocksRaycasts stayed false, looking like "buttons don't work".
            var cg = GetCg();
            cg.alpha = 0f; // simulated state right after ApplyPanelVisibility(hidden)

            // Run Update enough frames to let MoveTowards fully drive alpha.
            for (int i = 0; i < 60; i++) InvokeUpdate();

            Assert.AreEqual(0f, cg.alpha, 0.05f,
                "Update() must keep alpha=0 while _panelHidden=true. " +
                "If this fails, the alpha-MoveTowards logic in Update() is " +
                "ignoring _panelHidden again.");
        }

        // ── TogglePanel ─────────────────────────────────────────────────────

        [Test]
        public void TogglePanel_FromHidden_OpensPanel()
        {
            // SetUp leaves the panel hidden. Toggle → should open.
            TogglePanel();
            Assert.IsFalse(GetPanelHidden());

            var cg = GetCg();
            Assert.IsTrue(cg.blocksRaycasts,
                "Open panel must block raycasts so its inner buttons receive clicks");
            Assert.IsTrue(cg.interactable,
                "Open panel must be interactable");
        }

        [Test]
        public void TogglePanel_FromVisible_ClosesPanel()
        {
            TogglePanel();           // hidden → visible
            TogglePanel();           // visible → hidden again
            Assert.IsTrue(GetPanelHidden());

            var cg = GetCg();
            Assert.IsFalse(cg.blocksRaycasts);
            Assert.IsFalse(cg.interactable);
        }

        [Test]
        public void TogglePanel_PersistsState()
        {
            TogglePanel(); // → visible (persisted as 0)
            Assert.AreEqual(0, PlayerPrefs.GetInt(PrefKeyHidden));
            TogglePanel(); // → hidden (persisted as 1)
            Assert.AreEqual(1, PlayerPrefs.GetInt(PrefKeyHidden));
        }

        // ── OnCloseClicked (close button on the panel header) ───────────────

        [Test]
        public void OnCloseClicked_HidesPanel_EvenIfAlreadyHidden()
        {
            // Idempotent: clicking close should always result in panelHidden=true,
            // regardless of prior state.
            OnCloseClicked();
            Assert.IsTrue(GetPanelHidden());

            // Open it via toggle, then close via close-click.
            TogglePanel();
            Assert.IsFalse(GetPanelHidden());
            OnCloseClicked();
            Assert.IsTrue(GetPanelHidden());

            var cg = GetCg();
            Assert.IsFalse(cg.blocksRaycasts);
            Assert.IsFalse(cg.interactable);
        }

        // ── ApplyPanelVisibility consistency ────────────────────────────────

        [Test]
        public void ApplyPanelVisibility_AlwaysSetsAllThreeCgFlagsTogether()
        {
            // Contract: alpha, blocksRaycasts, interactable MUST move as a triple.
            // If any one of them drifts independently, the panel ends up in a
            // half-state (visible-but-unclickable, or invisible-but-blocking).
            var cg = GetCg();

            s_panelHiddenField.SetValue(_hud, false);
            ApplyVisibility();
            Assert.AreEqual(1f, cg.alpha, 0.01f);
            Assert.IsTrue(cg.blocksRaycasts);
            Assert.IsTrue(cg.interactable);

            s_panelHiddenField.SetValue(_hud, true);
            ApplyVisibility();
            Assert.AreEqual(0f, cg.alpha, 0.01f);
            Assert.IsFalse(cg.blocksRaycasts);
            Assert.IsFalse(cg.interactable);
        }

        // ── Bar registration is wired ───────────────────────────────────────

        [Test]
        public void BarButtonId_IsMusic()
        {
            // Make sure the constant we register with HUDIconBar stays "music".
            // Renaming it without updating the bar's expectation would orphan
            // the icon.
            var field = typeof(MusicPlayerHUD).GetField("BarButtonId",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsTrue(field != null);
            Assert.AreEqual("music", (string)field.GetValue(null));
        }
    }
}
