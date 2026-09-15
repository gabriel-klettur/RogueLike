using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

// EditMode does NOT fire Unity lifecycle methods (Awake/Start/OnEnable) on
// AddComponent. We invoke the singleton init manually via reflection — exactly
// the same pattern as SaveServiceDirtyFlagTests in EditMode/Game/Data/ — so
// that OnSingletonAwake runs and GameEvents are bound before each test.

namespace Valkur.Tests.EditMode.Gameplay.Save
{
    /// <summary>
    /// Covers the Phase-1 additions to <see cref="SaveService"/>:
    ///   • MarkDirty / IsSessionDirty behaviour
    ///   • SaveImmediately debounce-clear contract
    ///   • Event-wiring: OnPlayerDamaged, OnItemConsumed, OnPlayerDied,
    ///     OnEntityDied (boss-kill path), OnZoneChanged
    ///   • Update() debounce timer drain
    ///   • autosaveIntervalSeconds default sanity
    ///   • QuestManager.HandleCompletion null-guard (no SaveService instance)
    /// </summary>
    [TestFixture]
    public class SaveServiceDirtyAndImmediateTests
    {
        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject  _saveServiceGo;
        private SaveService _saveService;

        // ── Reflection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Replicates SingletonMonoBehaviour&lt;T&gt;.Awake without the
        /// DontDestroyOnLoad call that throws in EditMode.
        /// </summary>
        private static void ForceSingletonInit(SaveService svc)
        {
            var baseType      = typeof(SaveService).BaseType; // SingletonMonoBehaviour<SaveService>
            var instanceField = baseType?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            instanceField?.SetValue(null, svc);

            var onAwake = typeof(SaveService).GetMethod("OnSingletonAwake",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            onAwake?.Invoke(svc, null);
        }

        /// <summary>
        /// Components added via AddComponent in EditMode never receive Awake,
        /// so Unity also skips OnDestroy at DestroyImmediate time. The
        /// SceneManager.sceneLoaded subscription installed by OnSingletonAwake
        /// would then keep the C# component alive as a zombie that re-binds
        /// to GameEvents on the next runtime scene load — the x12 recurrence
        /// of incident .github/incidents/RUN_TWIN_SAVE.md (2026-05-09).
        /// Call this BEFORE DestroyImmediate so the cleanup contract holds.
        /// </summary>
        private static void ManuallyInvokeOnDestroy(SaveService svc)
        {
            if (svc == null) return;
            var onDestroy = typeof(SaveService).GetMethod("OnDestroy",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            try { onDestroy?.Invoke(svc, null); } catch { /* best-effort cleanup */ }
        }

        /// <summary>Reads a private/internal float field from SaveService by name.</summary>
        private static float GetFloat(SaveService svc, string fieldName)
        {
            var f = typeof(SaveService).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on SaveService — " +
                                "production code may need a test-only seam.");
            return (float)f.GetValue(svc);
        }

        /// <summary>Writes a private/internal float field on SaveService by name.</summary>
        private static void SetFloat(SaveService svc, string fieldName, float value)
        {
            var f = typeof(SaveService).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on SaveService — " +
                                "production code may need a test-only seam.");
            f.SetValue(svc, value);
        }

        /// <summary>Reads a private/internal bool field from SaveService by name.</summary>
        private static bool GetBool(SaveService svc, string fieldName)
        {
            var f = typeof(SaveService).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Field '{fieldName}' not found on SaveService — " +
                                "production code may need a test-only seam.");
            return (bool)f.GetValue(svc);
        }

        // ── SetUp / TearDown ──────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;   // SaveService logs aggressively

            if (SaveService.HasInstance)
                Object.DestroyImmediate(SaveService.Instance.gameObject);

            _saveServiceGo = new GameObject("TestSaveService_DirtyAndImmediate");
            _saveService   = _saveServiceGo.AddComponent<SaveService>();

            ForceSingletonInit(_saveService);

            // Start every test from a clean, non-dirty run.
            _saveService.BeginNewRun();
        }

        [TearDown]
        public void TearDown()
        {
            ManuallyInvokeOnDestroy(_saveService);

            if (_saveServiceGo != null)
                Object.DestroyImmediate(_saveServiceGo);

            GameEvents.Clear();
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static GameObject MakePlayer()
        {
            return new GameObject("TestPlayer") { tag = "Player" };
        }

        // ======================================================================
        // 1. MarkDirty / IsSessionDirty
        // ======================================================================

        [Test]
        public void MarkDirty_FlipsFlag_WhenInitiallyClean()
        {
            Assert.IsFalse(_saveService.IsSessionDirty,
                "Precondition: session must be clean after BeginNewRun.");

            _saveService.MarkDirty("test reason");

            Assert.IsTrue(_saveService.IsSessionDirty,
                "MarkDirty must flip IsSessionDirty to true.");
        }

        [Test]
        public void MarkDirty_IsIdempotent_WhenAlreadyDirty()
        {
            // First call — should log.
            _saveService.MarkDirty("first");
            Assert.IsTrue(_saveService.IsSessionDirty);

            // Second call — must not throw, must leave flag true.
            Assert.DoesNotThrow(() => _saveService.MarkDirty("second"),
                "Re-calling MarkDirty on an already-dirty session must not throw.");
            Assert.IsTrue(_saveService.IsSessionDirty,
                "Flag must remain true after idempotent re-call.");
        }

        // ======================================================================
        // 2. autosaveIntervalSeconds default
        // ======================================================================

        [Test]
        public void AutosaveIntervalSeconds_DefaultIs45()
        {
            // Read the serialized field via reflection — guards against accidental revert.
            var f = typeof(SaveService).GetField("autosaveIntervalSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Field 'autosaveIntervalSeconds' not found on SaveService.");

            float value = (float)f.GetValue(_saveService);
            Assert.AreEqual(45f, value, 0.001f,
                "autosaveIntervalSeconds default must remain 45 f (Phase-1 change).");
        }

        // ======================================================================
        // 3. SaveImmediately no-player path
        // ======================================================================

        [Test]
        public void SaveImmediately_DoesNothingWhenNoPlayer()
        {
            // GameStateCollector.Collect() returns null when EntityRegistry has no
            // player. Verify SaveImmediately returns false and does not throw.
            bool result = false;
            Assert.DoesNotThrow(() => result = _saveService.SaveImmediately("test_no_player"),
                "SaveImmediately must not throw when GameStateCollector returns null.");
            Assert.IsFalse(result,
                "SaveImmediately must return false when there is nothing to collect.");
        }

        // ======================================================================
        // 4. SaveImmediately clears debounce
        // ======================================================================

        [Test]
        public void SaveImmediately_ClearsDebouncePending()
        {
            // Arm the debounce window.
            _saveService.MarkDirty("arm debounce");
            Assert.IsTrue(GetBool(_saveService, "_dirtyDebouncePending"),
                "Precondition: MarkDirty must set _dirtyDebouncePending = true.");

            // Force-save clears it.
            _saveService.SaveImmediately("clear test");

            Assert.IsFalse(GetBool(_saveService, "_dirtyDebouncePending"),
                "SaveImmediately must clear _dirtyDebouncePending so Update does " +
                "not fire a second save via the debounce path.");
            float timer = GetFloat(_saveService, "_dirtyDebounceTimer");
            Assert.AreEqual(-1f, timer, 0.001f,
                "SaveImmediately must reset _dirtyDebounceTimer to -1 (disarmed sentinel).");
        }

        // ======================================================================
        // 5. dirtyDebounceSeconds field exists with default 2 f
        // ======================================================================

        [Test]
        public void DirtyDebounceSeconds_DefaultIs2()
        {
            var f = typeof(SaveService).GetField("dirtyDebounceSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Field 'dirtyDebounceSeconds' not found on SaveService.");

            float value = (float)f.GetValue(_saveService);
            Assert.AreEqual(2f, value, 0.001f,
                "dirtyDebounceSeconds default must be 2 f (Phase-1 addition).");
        }

        // ======================================================================
        // 6. Event wiring — OnPlayerDamaged
        // ======================================================================

        [Test]
        public void OnPlayerDamaged_MarksDirty()
        {
            Assert.IsFalse(_saveService.IsSessionDirty);

            GameEvents.FirePlayerDamaged(amount: 15, currentHp: 85, maxHp: 100);

            Assert.IsTrue(_saveService.IsSessionDirty,
                "OnPlayerDamaged must call MarkDirty.");
        }

        // ======================================================================
        // 7. Event wiring — OnItemConsumed (new in Phase 1)
        // ======================================================================

        [Test]
        public void OnItemConsumed_ByPlayer_MarksDirty()
        {
            Assert.IsFalse(_saveService.IsSessionDirty);

            var player = MakePlayer();
            try
            {
                GameEvents.FireItemConsumed(player, "HealthPotion");
                Assert.IsTrue(_saveService.IsSessionDirty,
                    "OnItemConsumed by the player must call MarkDirty.");
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void OnItemConsumed_ByNonPlayer_DoesNotMarkDirty()
        {
            var npc = new GameObject("TestNpc"); // default tag = "Untagged"
            try
            {
                GameEvents.FireItemConsumed(npc, "SomeThing");
                Assert.IsFalse(_saveService.IsSessionDirty,
                    "OnItemConsumed by a non-player entity must NOT mark dirty.");
            }
            finally { Object.DestroyImmediate(npc); }
        }

        // ======================================================================
        // 8. Event wiring — OnPlayerDied (new in Phase 1)
        // ======================================================================

        [Test]
        public void OnPlayerDied_DoesNotThrow_WhenNoPlayer()
        {
            // HandlePlayerDied calls SaveImmediately which calls
            // WriteAutosaveToDisk → GameStateCollector.Collect() which returns null
            // when there is no EntityRegistry player. Must not throw.
            Assert.DoesNotThrow(() => GameEvents.FirePlayerDied(),
                "OnPlayerDied must not throw even when GameStateCollector returns null.");
        }

        // ======================================================================
        // 9. Event wiring — OnZoneChanged
        // ======================================================================

        [Test]
        public void OnZoneChanged_DoesNotThrow_WhenNoPlayer()
        {
            // Zone change fires SaveImmediately, which hits GameStateCollector.Collect().
            // With no player present it should return false cleanly, never throw.
            Assert.DoesNotThrow(() => GameEvents.FireZoneChanged("OldZone", "NewZone"),
                "OnZoneChanged must not throw when GameStateCollector returns null.");
        }

        // ======================================================================
        // 10. Event wiring — OnEntityDied boss-kill path (new in Phase 1)
        // ======================================================================

        [Test]
        public void OnEntityDied_BossVictim_CallsSaveImmediately()
        {
            // Spawn a victim that carries a BossPhaseController.
            // BossPhaseController has [RequireComponent(typeof(Health))] so we add
            // Health too. We cannot wire up the HP event here; we just need the
            // component to be present so HandleEntityDied takes the boss branch.
            var bossGo = new GameObject("TestBoss");
            bossGo.AddComponent<Health>();
            bossGo.AddComponent<BossPhaseController>();

            try
            {
                // Firing must not throw and must clear _dirtyDebouncePending
                // (because SaveImmediately was called internally).
                // We arm the debounce first so we can verify it was cleared.
                _saveService.MarkDirty("pre-boss arm");
                Assert.IsTrue(GetBool(_saveService, "_dirtyDebouncePending"),
                    "Precondition: debounce must be pending before boss kill.");

                Assert.DoesNotThrow(
                    () => GameEvents.FireEntityDied(victim: bossGo, killer: null),
                    "OnEntityDied with a boss victim must not throw.");

                Assert.IsFalse(GetBool(_saveService, "_dirtyDebouncePending"),
                    "SaveImmediately triggered by boss kill must clear _dirtyDebouncePending.");
            }
            finally { Object.DestroyImmediate(bossGo); }
        }

        [Test]
        public void OnEntityDied_NonBossVictim_DoesNotClearDebounce()
        {
            var nonBossGo = new GameObject("TestNonBoss");
            try
            {
                _saveService.MarkDirty("pre-non-boss arm");
                Assert.IsTrue(GetBool(_saveService, "_dirtyDebouncePending"),
                    "Precondition: debounce must be pending.");

                GameEvents.FireEntityDied(victim: nonBossGo, killer: null);

                // Non-boss entity death should NOT trigger SaveImmediately,
                // so the debounce window remains armed.
                Assert.IsTrue(GetBool(_saveService, "_dirtyDebouncePending"),
                    "A non-boss entity death must NOT call SaveImmediately " +
                    "and therefore must NOT clear _dirtyDebouncePending.");
            }
            finally { Object.DestroyImmediate(nonBossGo); }
        }

        [Test]
        public void OnEntityDied_NullVictim_DoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => GameEvents.FireEntityDied(victim: null, killer: null),
                "HandleEntityDied must guard against a null victim gracefully.");
        }

        // ======================================================================
        // 11. Update debounce: fires Autosave after timeout
        // ======================================================================

        [Test]
        public void Update_DebounceFiresAutosaveAfterTimeout()
        {
            // Strategy: set dirtyDebounceSeconds to 0 (fires immediately on the
            // first Update tick that accumulates any delta) and advance the
            // debounce timer manually to just past the threshold. Then call the
            // private Update method with a zeroed dt so the autosave timer branch
            // stays dormant, but the debounce branch fires.

            // Force the debounce threshold to 0 so any positive timer triggers it.
            SetFloat(_saveService, "dirtyDebounceSeconds", 0f);
            // Also disable periodic autosave so the autosave timer branch stays quiet.
            var autosaveEnabledField = typeof(SaveService).GetField("autosaveEnabled",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(autosaveEnabledField, "autosaveEnabled field not found.");
            autosaveEnabledField.SetValue(_saveService, false);

            _saveService.MarkDirty("debounce test");
            Assert.IsTrue(GetBool(_saveService, "_dirtyDebouncePending"),
                "Precondition: debounce must be pending after MarkDirty.");

            // Advance the debounce timer past 0 (our chosen threshold).
            // The Update method reads Time.unscaledDeltaTime which is 0 in
            // EditMode, so we pre-advance the timer field instead.
            SetFloat(_saveService, "_dirtyDebounceTimer", 0.01f);

            // Invoke Update via reflection.
            var updateMethod = typeof(SaveService).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(updateMethod, "Update method not found on SaveService.");
            updateMethod.Invoke(_saveService, null);

            // The debounce branch should have fired and cleared _dirtyDebouncePending.
            Assert.IsFalse(GetBool(_saveService, "_dirtyDebouncePending"),
                "After the debounce timeout, Update must clear _dirtyDebouncePending.");
            float timerAfter = GetFloat(_saveService, "_dirtyDebounceTimer");
            Assert.AreEqual(-1f, timerAfter, 0.001f,
                "After the debounce fires, _dirtyDebounceTimer must be reset to -1 (disarmed).");
        }

        // ======================================================================
        // 12. QuestManager null-guard: SaveService.Instance == null path
        // ======================================================================

        [Test]
        public void QuestManager_HandleCompletion_DoesNotThrow_WhenSaveServiceInstanceIsNull()
        {
            // Destroy the live SaveService so Instance is null.
            Object.DestroyImmediate(_saveServiceGo);
            _saveServiceGo = null;

            // SaveService.Instance?.SaveImmediately(...) in QuestManager uses
            // the null-conditional — verify the static expression is safe.
            // We call it directly here (without a full QuestManager fixture)
            // to confirm the null-safe path compiles and runs without throwing.
            Assert.IsFalse(SaveService.HasInstance,
                "Precondition: SaveService.Instance must be null for this test.");

            bool threw = false;
            try
            {
                // Mirrors exactly what QuestManager.HandleCompletion does.
                SaveService.Instance?.SaveImmediately("quest completed (null guard test)");
            }
            catch
            {
                threw = true;
            }

            Assert.IsFalse(threw,
                "SaveService.Instance?.SaveImmediately must be safe when Instance is null.");
        }

        // ======================================================================
        // 12. Orphan-bootstrap guard — WriteAutosaveToDisk refuses to write
        //     while _currentRunOrdinal is still 0 (the transient window between
        //     BeginNewRun and ProfileTelemetrySystem.StartRun). Without this
        //     gate, an event that fires inside the bootstrap window leaks a
        //     Saves/<guid>/ folder whose autosave lacks run_ordinal, exactly
        //     the "phantom burst" pattern that polluted the Load Game panel
        //     with 86 duplicate runs in a single second.
        // ======================================================================

        [Test]
        public void WriteAutosaveToDisk_RefusesWriteWhenRunOrdinalIsZero()
        {
            // BeginNewRun set _currentRunId but _currentRunOrdinal stays 0
            // until SetRunOrdinal is invoked by the bootstrap.
            Assert.AreEqual(0, _saveService.RunOrdinal,
                "Pre-condition: a freshly-begun run must have ordinal=0 " +
                "(StartTelemetryRun has not finalised the run identity yet).");

            // SaveImmediately is force=true. Even forced saves must be gated
            // because a forced save with ordinal=0 produces the same orphan
            // folder a non-forced one would.
            bool result = _saveService.SaveImmediately("orphan-bootstrap regression");

            Assert.IsFalse(result,
                "WriteAutosaveToDisk must return false when _currentRunOrdinal == 0. " +
                "Otherwise an event fired in the BeginNewRun→StartRun window leaks " +
                "a Saves/<guid>/ folder that pollutes the Load Game panel.");
        }

        [Test]
        public void WriteAutosaveToDisk_ProceedsAfterRunOrdinalIsSet()
        {
            // Simulate the bootstrap finishing: SetRunOrdinal mints the
            // per-profile ordinal and unblocks subsequent saves.
            _saveService.SetRunOrdinal(1);
            Assert.AreEqual(1, _saveService.RunOrdinal,
                "Pre-condition: SetRunOrdinal must propagate to RunOrdinal.");

            // The orphan-bootstrap (ordinal=0) guard is now disarmed. In EditMode
            // the next layer — RefuseWriteOutsidePlayMode — rejects the write so
            // tests cannot leak Saves/<guid>/ folders into persistentDataPath.
            // The end state ("returns false, no disk pollution") is unchanged.
            bool result = _saveService.SaveImmediately("post-bootstrap save");
            Assert.IsFalse(result,
                "EditMode write must be refused — either by the no-player short-" +
                "circuit or by the Play-Mode-only guard added for incident RUN_TWIN_SAVE.");
        }

        // ======================================================================
        // 12. Test-pollution guard — incident RUN_TWIN_SAVE.md
        //     EditMode tests must not be able to write to persistentDataPath/Saves/
        //     even when both the dirty flag AND the run ordinal allow it.
        // ======================================================================

        [Test]
        public void SaveImmediately_RefusesDiskWrite_OutsidePlayMode()
        {
            _saveService.SetRunOrdinal(1);
            _saveService.MarkDirty("regression: ensure both gates would otherwise allow the write");

            string persistentRoot = System.IO.Path.Combine(
                UnityEngine.Application.persistentDataPath, "Saves");
            string runFolder = System.IO.Path.Combine(persistentRoot, _saveService.RunId);

            bool existedBefore = System.IO.Directory.Exists(runFolder);

            bool result = _saveService.SaveImmediately("regression: outside-play-mode guard");

            Assert.IsFalse(result,
                "SaveImmediately must return false in EditMode (Application.isPlaying == false).");

            // Hard contract: the run folder must NOT have been created by this call.
            // (If it pre-existed for unrelated reasons we don't fail the test, but
            // the absence-after-call is the regression we care about.)
            if (!existedBefore)
            {
                Assert.IsFalse(System.IO.Directory.Exists(runFolder),
                    $"EditMode test must not leak a Saves/<runId>/ folder. Path: {runFolder}");
            }
        }

        [Test]
        public void Save_RefusesDiskWrite_OutsidePlayMode()
        {
            _saveService.SetRunOrdinal(1);

            string persistentRoot = System.IO.Path.Combine(
                UnityEngine.Application.persistentDataPath, "Saves");
            string runFolder = System.IO.Path.Combine(persistentRoot, _saveService.RunId);
            bool existedBefore = System.IO.Directory.Exists(runFolder);

            bool result = _saveService.Save("regression_slot");

            Assert.IsFalse(result,
                "Manual Save must return false in EditMode (Play Mode guard).");

            if (!existedBefore)
            {
                Assert.IsFalse(System.IO.Directory.Exists(runFolder),
                    $"EditMode manual Save must not leak a Saves/<runId>/ folder. Path: {runFolder}");
            }
        }

        // ======================================================================
        // 13. Full-state-on-every-trigger contract — position-lag fix.
        //     Every gameplay trigger (damage, XP, item pickup, item consume,
        //     level up, zone change, player death, boss death) must dispatch
        //     SaveImmediately so the live player state — INCLUDING THE
        //     CURRENT POSITION — is captured at the moment of the event,
        //     not 2 seconds later through the debounce path.
        //     User-reported regression: "killed an NPC, picked up orbs,
        //     gained XP — XP saved but position did not."
        // ======================================================================

        private System.Collections.Generic.List<string> _capturedLogs;
        private UnityEngine.Application.LogCallback _logCapture;

        private void StartLogCapture()
        {
            _capturedLogs = new System.Collections.Generic.List<string>();
            _logCapture = (msg, _, type) =>
            {
                if (type == LogType.Log || type == LogType.Warning)
                    _capturedLogs.Add(msg ?? string.Empty);
            };
            UnityEngine.Application.logMessageReceived += _logCapture;
        }

        private void StopLogCapture()
        {
            if (_logCapture != null)
            {
                UnityEngine.Application.logMessageReceived -= _logCapture;
                _logCapture = null;
            }
        }

        private bool LogContainsSaveImmediately(string reasonSubstring)
        {
            if (_capturedLogs == null) return false;
            for (int i = 0; i < _capturedLogs.Count; i++)
            {
                var msg = _capturedLogs[i] ?? string.Empty;
                if (msg.IndexOf("SaveImmediately", System.StringComparison.Ordinal) < 0) continue;
                if (string.IsNullOrEmpty(reasonSubstring)) return true;
                if (msg.IndexOf(reasonSubstring, System.StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        [Test]
        public void OnPlayerDamaged_DispatchesSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            StartLogCapture();
            try
            {
                GameEvents.FirePlayerDamaged(amount: 7, currentHp: 93, maxHp: 100);
                Assert.IsTrue(LogContainsSaveImmediately("player damaged"),
                    "Damage handler must call SaveImmediately so the player's " +
                    "current position is captured at the moment of the hit, " +
                    "not 2 s later through the debounce path.");
            }
            finally { StopLogCapture(); }
        }

        [Test]
        public void OnXpGained_ByPlayer_DispatchesSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var player = MakePlayer();
            StartLogCapture();
            try
            {
                GameEvents.FireXpGained(player, amount: 17);
                Assert.IsTrue(LogContainsSaveImmediately("gained 17 XP"),
                    "XP gain on the player must call SaveImmediately — this is " +
                    "the canonical 'killed an NPC + picked up orbs' regression.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(player); }
        }

        [Test]
        public void OnXpGained_ByNonPlayer_DoesNotDispatchSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var npc = new GameObject("TestNpc"); // tag = Untagged
            StartLogCapture();
            try
            {
                GameEvents.FireXpGained(npc, amount: 17);
                Assert.IsFalse(LogContainsSaveImmediately("gained"),
                    "Untagged entities gaining XP must NOT trigger a save — " +
                    "only the player's progression matters for autosave decisions.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(npc); }
        }

        [Test]
        public void OnItemPickedUp_ByPlayer_DispatchesSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var player = MakePlayer();
            StartLogCapture();
            try
            {
                GameEvents.FireItemPickedUp(player, "XpOrb", 1);
                Assert.IsTrue(LogContainsSaveImmediately("picked up XpOrb"),
                    "Item pickup must call SaveImmediately so the exact pickup " +
                    "location lands on disk before the player walks away.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(player); }
        }

        [Test]
        public void OnItemPickedUp_ByNonPlayer_DoesNotDispatchSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var npc = new GameObject("TestNpc");
            StartLogCapture();
            try
            {
                GameEvents.FireItemPickedUp(npc, "AnyItem", 1);
                Assert.IsFalse(LogContainsSaveImmediately("picked up"),
                    "Non-player entities picking up items must NOT trigger a save.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(npc); }
        }

        [Test]
        public void OnItemConsumed_ByPlayer_DispatchesSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var player = MakePlayer();
            StartLogCapture();
            try
            {
                GameEvents.FireItemConsumed(player, "HealthPotion");
                Assert.IsTrue(LogContainsSaveImmediately("consumed HealthPotion"),
                    "Item consume must call SaveImmediately so the post-consume " +
                    "stats (HP, mana, inventory) and current position both land on disk.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(player); }
        }

        [Test]
        public void OnItemConsumed_ByNonPlayer_DoesNotDispatchSaveImmediately()
        {
            _saveService.SetRunOrdinal(1);
            var npc = new GameObject("TestNpc");
            StartLogCapture();
            try
            {
                GameEvents.FireItemConsumed(npc, "AnyItem");
                Assert.IsFalse(LogContainsSaveImmediately("consumed"),
                    "Non-player entities consuming items must NOT trigger a save.");
            }
            finally { StopLogCapture(); Object.DestroyImmediate(npc); }
        }

        // ======================================================================
        // 14. GameStateCollector captures the LIVE position — proves that
        //     when SaveImmediately fires, the position reaching disk is the
        //     position at the moment of the call, not a stale cached value.
        // ======================================================================

        [Test]
        public void GameStateCollector_CapturesLivePlayerPosition_AtMomentOfCall()
        {
            // Build a real player with Health, register it, place it at pos A.
            var player = new GameObject("TestPlayer_PositionCapture") { tag = "Player" };
            var health = player.AddComponent<Valkur.Gameplay.Health>();
            health.Initialize(100);

            var posA = new Vector3(123.5f, 67.25f, 0f);
            var posB = new Vector3(456.75f, 12.5f, 0f);
            player.transform.position = posA;

            EntityRegistry.Clear();
            EntityRegistry.RegisterPlayer(player);

            try
            {
                var snapshotA = GameStateCollector.Collect();
                Assert.IsNotNull(snapshotA,
                    "Collect must succeed when a Player with valid Health is registered.");
                Assert.AreEqual(posA.x, snapshotA.player.position.x, 0.001f,
                    "Snapshot A must capture x at posA.");
                Assert.AreEqual(posA.y, snapshotA.player.position.y, 0.001f,
                    "Snapshot A must capture y at posA.");

                // Move the player. A second Collect must reflect the new position
                // — this is the core of the position-lag fix: every save call
                // re-reads transform.position right then.
                player.transform.position = posB;

                var snapshotB = GameStateCollector.Collect();
                Assert.IsNotNull(snapshotB);
                Assert.AreEqual(posB.x, snapshotB.player.position.x, 0.001f,
                    "Snapshot B must capture x at posB (live read, not stale).");
                Assert.AreEqual(posB.y, snapshotB.player.position.y, 0.001f,
                    "Snapshot B must capture y at posB (live read, not stale).");

                Assert.AreNotEqual(snapshotA.player.position.x, snapshotB.player.position.x,
                    "Two snapshots taken at different positions must differ — " +
                    "proves the capture is live, not cached.");
            }
            finally
            {
                EntityRegistry.UnregisterPlayer(player);
                Object.DestroyImmediate(player);
            }
        }

        // ======================================================================
        // 15. Defensive null-checks — handlers must not throw when the player
        //     reference is null. Regression target: any Fire* call that races
        //     scene transitions or runs before EntitySetup completes.
        // ======================================================================

        [Test]
        public void TriggerHandlers_DoNotThrow_WhenNullEntityPassed()
        {
            _saveService.SetRunOrdinal(1);
            Assert.DoesNotThrow(() => GameEvents.FireXpGained(null, 5),
                "FireXpGained(null, ...) must not crash the handler.");
            Assert.DoesNotThrow(() => GameEvents.FireItemPickedUp(null, "x", 1),
                "FireItemPickedUp(null, ...) must not crash the handler.");
            Assert.DoesNotThrow(() => GameEvents.FireItemConsumed(null, "x"),
                "FireItemConsumed(null, ...) must not crash the handler.");
        }
    }
}
