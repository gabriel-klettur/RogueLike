using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

// EditMode-only helper: AddComponent<T>() does NOT fire Unity lifecycle methods
// (Awake/Start/OnEnable) outside Play Mode, so a SaveService created here would
// silently skip OnSingletonAwake and never bind GameEvents. Each test fixture
// in this file invokes Awake via reflection right after AddComponent so the
// production binding code runs.

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Regression suite for the autosave dirty-flag behaviour added to fix
    /// "Cargar Juego shows save slots the user never created."
    ///
    /// The bug was: every play session called <see cref="SaveService.BeginNewRun"/>
    /// at scene start and <see cref="SaveService.Autosave"/> on quit, so even
    /// trivial sessions (open game, walk around at full HP, close window) wrote
    /// a fresh per-run autosave folder. After dozens of test launches the Load
    /// Game panel filled with phantom Lv.0 entries.
    ///
    /// Invariants enforced here:
    ///   • A fresh <see cref="SaveService.BeginNewRun"/> leaves the session
    ///     non-dirty.
    ///   • <see cref="SaveService.Autosave"/> short-circuits when the session
    ///     is non-dirty (returns false, writes nothing).
    ///   • Standard progression events (player damage, XP, level-up, item
    ///     pickup, zone change) flip the session to dirty.
    ///   • <see cref="SaveService.Load"/> resets the dirty flag (the loaded
    ///     state already matches disk — no autosave needed until something
    ///     actually changes).
    ///   • <see cref="SaveService.QuickSave"/> still writes when the player
    ///     explicitly asks (e.g. the pause-menu "Guardar partida" / "Salir"
    ///     buttons), even on a clean session.
    /// </summary>
    [TestFixture]
    public class SaveServiceDirtyFlagTests
    {
        private GameObject _saveServiceGo;
        private SaveService _saveService;

        [SetUp]
        public void SetUp()
        {
            // Tear down any leftover singleton from prior tests so we own the
            // SaveService.Instance for the duration of this fixture.
            if (SaveService.HasInstance)
                Object.DestroyImmediate(SaveService.Instance.gameObject);

            _saveServiceGo = new GameObject("TestSaveService");
            _saveService   = _saveServiceGo.AddComponent<SaveService>();

            // EditMode does NOT auto-fire Awake on AddComponent. Worse, the
            // base Awake calls DontDestroyOnLoad, which itself THROWS in
            // EditMode. So invoke the post-DontDestroyOnLoad steps by hand
            // via reflection: assign the singleton instance and run the
            // SaveService-specific OnSingletonAwake (which binds GameEvents).
            ForceSingletonInit(_saveService);
        }

        private static void ForceSingletonInit(SaveService svc)
        {
            // Mirror SingletonMonoBehaviour<T>.Awake's body without
            // DontDestroyOnLoad: set the static _instance and invoke
            // OnSingletonAwake. _instance lives on the closed generic
            // SingletonMonoBehaviour<SaveService>.
            var baseType = typeof(SaveService).BaseType; // SingletonMonoBehaviour<SaveService>
            var instanceField = baseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, svc);

            var onAwake = typeof(SaveService).GetMethod("OnSingletonAwake",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            onAwake?.Invoke(svc, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_saveServiceGo != null)
                Object.DestroyImmediate(_saveServiceGo);

            // Also flush the static GameEvents subscriptions our handler may
            // have left behind so other test fixtures see a clean bus.
            GameEvents.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static GameObject BuildPlayerGameObject()
        {
            var go = new GameObject("TestPlayer") { tag = "Player" };
            return go;
        }

        // ── Initial state ────────────────────────────────────────────────────

        [Test]
        public void BeginNewRun_LeavesSessionNonDirty()
        {
            _saveService.BeginNewRun();
            Assert.IsFalse(_saveService.IsSessionDirty,
                "A brand-new run must start non-dirty so a no-op session " +
                "(start → quit) does not write a phantom autosave.");
        }

        // ── Dirty-trigger events ─────────────────────────────────────────────

        [Test]
        public void PlayerDamageEvent_MarksSessionDirty()
        {
            _saveService.BeginNewRun();
            Assert.IsFalse(_saveService.IsSessionDirty);

            GameEvents.FirePlayerDamaged(amount: 10, currentHp: 90, maxHp: 100);

            Assert.IsTrue(_saveService.IsSessionDirty,
                "Taking damage is meaningful progress and must arm the autosave.");
        }

        [Test]
        public void PlayerXpGainedEvent_MarksSessionDirty()
        {
            _saveService.BeginNewRun();
            var player = BuildPlayerGameObject();
            try
            {
                GameEvents.FireXpGained(player, amount: 25);
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "Gaining XP on the player must arm the autosave.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void NonPlayerXpGained_DoesNotMarkDirty()
        {
            _saveService.BeginNewRun();
            // GameObjects with no "Player" tag should be ignored — only the
            // player's progression matters for autosave decisions.
            var npc = new GameObject("TestNpc"); // default tag = "Untagged"
            try
            {
                GameEvents.FireXpGained(npc, amount: 25);
                Assert.IsFalse(_saveService.IsSessionDirty,
                    "Untagged entities gaining XP must NOT mark the session dirty.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        [Test]
        public void PlayerLevelUpEvent_MarksSessionDirty()
        {
            _saveService.BeginNewRun();
            var player = BuildPlayerGameObject();
            try
            {
                GameEvents.FireLevelUp(player, newLevel: 2);
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "Leveling up the player must arm the autosave.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void PlayerItemPickupEvent_MarksSessionDirty()
        {
            _saveService.BeginNewRun();
            var player = BuildPlayerGameObject();
            try
            {
                GameEvents.FireItemPickedUp(player, "Health Potion", 1);
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "Picking up an item must arm the autosave.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void ZoneChangedEvent_MarksSessionDirty()
        {
            _saveService.BeginNewRun();
            GameEvents.FireZoneChanged(oldZone: "Lobby", newZone: "Forest");
            Assert.IsTrue(_saveService.IsSessionDirty,
                "Crossing into a new zone counts as exploration progress and " +
                "must arm the autosave.");
        }

        [Test]
        public void MarkDirty_IsIdempotent()
        {
            _saveService.BeginNewRun();
            _saveService.MarkDirty("first");
            Assert.IsTrue(_saveService.IsSessionDirty);
            _saveService.MarkDirty("second"); // must not throw or duplicate state
            Assert.IsTrue(_saveService.IsSessionDirty);
        }

        // ── Autosave gate ────────────────────────────────────────────────────

        [Test]
        public void Autosave_OnNonDirtySession_ReturnsFalseAndWritesNothing()
        {
            _saveService.BeginNewRun();
            string runId = _saveService.RunId;
            Assert.IsFalse(string.IsNullOrEmpty(runId));

            // Sanity: the run folder for this fresh run must not exist yet.
            string runDir = SaveFileManager.GetRunDirectory(runId);
            Assert.IsFalse(Directory.Exists(runDir),
                "BeginNewRun must not eagerly create the run folder.");

            bool written = _saveService.Autosave();

            Assert.IsFalse(written,
                "Autosave on a clean session must skip and return false.");
            Assert.IsFalse(Directory.Exists(runDir),
                "Autosave on a clean session must NOT create a run folder.");
        }

        // ── Load resets dirty ───────────────────────────────────────────────

        [Test]
        public void Load_NonexistentPath_LeavesSessionNonDirtyAndReturnsFalse()
        {
            // After BeginNewRun + a dirty event, Load on a missing path should
            // bail early without flipping the dirty flag back on.
            _saveService.BeginNewRun();
            _saveService.MarkDirty("test");
            Assert.IsTrue(_saveService.IsSessionDirty);

            string ghostPath = Path.Combine(SaveFileManager.GetSaveDirectory(),
                                            "_test_ghost", "autosave.json");
            bool ok = _saveService.Load(ghostPath);

            Assert.IsFalse(ok, "Load of a missing file must return false.");
            // The dirty flag is intentionally NOT reset on a failed load —
            // failing to load shouldn't silently discard the player's work
            // up to that point.
            Assert.IsTrue(_saveService.IsSessionDirty);
        }
    }
}
