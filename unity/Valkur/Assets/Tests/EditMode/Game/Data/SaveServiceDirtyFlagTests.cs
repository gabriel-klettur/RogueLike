using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        // Captured Unity log messages — populated by Application.logMessageReceived
        // for the duration of each test so milestone-handler tests can detect
        // the [SaveService] SaveImmediately: ... line that signals the trigger
        // fired (independent of whether the actual disk write succeeded).
        private readonly List<string> _capturedLogs = new List<string>();
        private Application.LogCallback _logHandler;

        [SetUp]
        public void SetUp()
        {
            // Tear down any leftover singleton from prior tests so we own the
            // SaveService.Instance for the duration of this fixture.
            if (SaveService.HasInstance)
                UnityEngine.Object.DestroyImmediate(SaveService.Instance.gameObject);

            _saveServiceGo = new GameObject("TestSaveService");
            _saveService   = _saveServiceGo.AddComponent<SaveService>();

            // EditMode does NOT auto-fire Awake on AddComponent. Worse, the
            // base Awake calls DontDestroyOnLoad, which itself THROWS in
            // EditMode. So invoke the post-DontDestroyOnLoad steps by hand
            // via reflection: assign the singleton instance and run the
            // SaveService-specific OnSingletonAwake (which binds GameEvents).
            ForceSingletonInit(_saveService);

            // Hook log capture for milestone tests. Use an instance handler
            // (not a static one) so each fixture instance captures only its
            // own logs and unsubscribes cleanly in TearDown.
            _capturedLogs.Clear();
            _logHandler = (string condition, string stackTrace, LogType type) =>
            {
                if (type == LogType.Log || type == LogType.Warning)
                    _capturedLogs.Add(condition ?? string.Empty);
            };
            Application.logMessageReceived += _logHandler;
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
            if (_logHandler != null)
            {
                Application.logMessageReceived -= _logHandler;
                _logHandler = null;
            }

            if (_saveServiceGo != null)
                UnityEngine.Object.DestroyImmediate(_saveServiceGo);

            // Also flush the static GameEvents subscriptions our handler may
            // have left behind so other test fixtures see a clean bus.
            GameEvents.Clear();
        }

        /// <summary>
        /// True iff the captured log buffer contains an
        /// <c>[SaveService] SaveImmediately:</c> line whose reason matches
        /// <paramref name="reasonSubstring"/>. Use this to verify that a
        /// milestone handler dispatched an immediate save without depending
        /// on the disk write actually succeeding (which it can't in EditMode
        /// because <see cref="GameStateCollector"/> requires a live
        /// EntityRegistry.Player + Health component).
        /// </summary>
        private bool LogContainsSaveImmediately(string reasonSubstring)
        {
            for (int i = 0; i < _capturedLogs.Count; i++)
            {
                var msg = _capturedLogs[i] ?? string.Empty;
                if (msg.IndexOf("SaveImmediately", StringComparison.Ordinal) < 0) continue;
                if (string.IsNullOrEmpty(reasonSubstring)) return true;
                if (msg.IndexOf(reasonSubstring, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
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
        public void PlayerDamageEvent_MarksSessionDirty_AndFiresImmediateSave()
        {
            // Contract since the position-lag fix: every gameplay trigger
            // (damage, XP, item, zone change, level up, death, boss kill)
            // captures the FULL live player state via SaveImmediately at the
            // moment of the event — no longer the "MarkDirty + 2 s debounce"
            // path that lost the player's position when they walked away
            // during the debounce window.
            _saveService.BeginNewRun();
            Assert.IsFalse(_saveService.IsSessionDirty);

            _capturedLogs.Clear();
            GameEvents.FirePlayerDamaged(amount: 10, currentHp: 90, maxHp: 100);

            Assert.IsTrue(_saveService.IsSessionDirty,
                "Taking damage is meaningful progress and must arm the autosave.");
            Assert.IsTrue(LogContainsSaveImmediately("player damaged"),
                "Damage must also dispatch SaveImmediately so the live position " +
                "is captured at the moment of the hit, not 2 s later.");
        }

        [Test]
        public void PlayerXpGainedEvent_MarksSessionDirty_AndFiresImmediateSave()
        {
            _saveService.BeginNewRun();
            var player = BuildPlayerGameObject();
            try
            {
                _capturedLogs.Clear();
                GameEvents.FireXpGained(player, amount: 25);
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "Gaining XP on the player must arm the autosave.");
                Assert.IsTrue(LogContainsSaveImmediately("gained 25 XP"),
                    "XP gain must also dispatch SaveImmediately so the kill " +
                    "location and post-pickup position both land on disk.");
            }
            finally { UnityEngine.Object.DestroyImmediate(player); }
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
            finally { UnityEngine.Object.DestroyImmediate(npc); }
        }

        [Test]
        public void PlayerLevelUpEvent_TriggersImmediateSaveAndArmsDirtyFallback()
        {
            // Contract since the milestone-save upgrade (commits Phase 1–4):
            //   • Level-up is a critical milestone → SaveImmediately is invoked
            //     so a crash before the next periodic save can't lose the new
            //     level + skill points.
            //   • MarkDirty is also fired BEFORE SaveImmediately so the
            //     autosave timer / debounce still picks the event up if the
            //     immediate save fails (e.g. GameStateCollector.Collect() is
            //     unable to snapshot the player). On a successful immediate
            //     write, WriteAutosaveToDisk re-clears the flag.
            _saveService.BeginNewRun();
            Assert.IsFalse(_saveService.IsSessionDirty,
                "Pre-condition: BeginNewRun leaves the session non-dirty.");

            var player = BuildPlayerGameObject();
            try
            {
                _capturedLogs.Clear();
                GameEvents.FireLevelUp(player, newLevel: 2);

                Assert.IsTrue(LogContainsSaveImmediately("leveled up to 2"),
                    "Leveling up must dispatch SaveImmediately (look for the " +
                    "'[SaveService] SaveImmediately: player leveled up to 2' log).");

                // EditMode-only: GameStateCollector returns null (no Player
                // registered in EntityRegistry), so WriteAutosaveToDisk bails
                // before clearing _sessionDirty. The MarkDirty fallback we
                // injected before SaveImmediately is what keeps the autosave
                // timer armed in this failure mode.
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "MarkDirty fallback must keep the dirty flag armed when " +
                    "the immediate save fails (here: no player registered in EditMode).");
            }
            finally { UnityEngine.Object.DestroyImmediate(player); }
        }

        [Test]
        public void PlayerItemPickupEvent_MarksSessionDirty_AndFiresImmediateSave()
        {
            _saveService.BeginNewRun();
            var player = BuildPlayerGameObject();
            try
            {
                _capturedLogs.Clear();
                GameEvents.FireItemPickedUp(player, "Health Potion", 1);
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "Picking up an item must arm the autosave.");
                Assert.IsTrue(LogContainsSaveImmediately("picked up Health Potion"),
                    "Item pickup must also dispatch SaveImmediately so the " +
                    "exact pickup location is captured immediately.");
            }
            finally { UnityEngine.Object.DestroyImmediate(player); }
        }

        [Test]
        public void ZoneChangedEvent_TriggersImmediateSaveAndArmsDirtyFallback()
        {
            // Same contract as PlayerLevelUpEvent: zone transitions are
            // canonical checkpoints, so SaveImmediately is dispatched +
            // MarkDirty is set as a fallback so the autosave timer catches
            // the event if the immediate save can't actually write (e.g. no
            // player registered in EntityRegistry, like here in EditMode).
            _saveService.BeginNewRun();
            Assert.IsFalse(_saveService.IsSessionDirty);

            _capturedLogs.Clear();
            GameEvents.FireZoneChanged(oldZone: "Lobby", newZone: "Forest");

            Assert.IsTrue(LogContainsSaveImmediately("zone Lobby"),
                "Crossing into a new zone must dispatch SaveImmediately " +
                "(look for the '[SaveService] SaveImmediately: zone Lobby → Forest' log).");

            Assert.IsTrue(_saveService.IsSessionDirty,
                "MarkDirty fallback must keep the dirty flag armed when the " +
                "immediate zone-change save fails in the test environment.");
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
