using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;        // RunGroupInfo, SaveSlotInfo
using Valkur.Gameplay.Save;   // SaveFileManager

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Regression coverage for the "phantom runs" leak: every Exit through
    /// the pause menu used to force a QuickSave even when the player had
    /// done literally nothing, leaving a Saves/&lt;runId&gt;/autosave.json with
    /// a Lv.0 / Lobby player. Over time the Load Game panel filled with
    /// junk runs.
    ///
    /// PauseMenu now gates that QuickSave on IsSessionDirty (preventing new
    /// phantoms), and SaveFileManager.PrunePhantomRuns drops legacy phantoms
    /// from disk on the next MainMenu visit. These tests pin both halves of
    /// the contract:
    ///
    ///   1. A run with a single Lv.0 / 0-XP autosave is detected as phantom
    ///      and pruned.
    ///   2. A run with any progression (level &gt; 1, XP &gt; 0, or a manual
    ///      save in addition to the autosave) is preserved.
    ///   3. The "active" runId can be passed in as a guard so the in-flight
    ///      session is never wiped from under the live SaveService.
    ///   4. Legacy saves (no runId, runId == "") are left alone.
    ///
    /// Tests hit the real <see cref="Application.persistentDataPath"/> so the
    /// fixture is careful to namespace every artifact under <c>_test_phantom_</c>
    /// and clean it up in TearDown — a failure leaves obvious garbage rather
    /// than stomping on real player saves.
    /// </summary>
    [TestFixture]
    public class SaveFileManagerPrunePhantomRunsTests
    {
        private const string TestPrefix = "_test_phantom_";
        private readonly List<string> _createdRunIds = new List<string>();

        [SetUp]
        public void SetUp()
        {
            SaveFileManager.EnsureSaveDirectory();
            _createdRunIds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var runId in _createdRunIds)
            {
                string runDir = SaveFileManager.GetRunDirectory(runId);
                if (Directory.Exists(runDir))
                {
                    try { Directory.Delete(runDir, recursive: true); } catch { }
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string NewRunId(string label)
        {
            string id = TestPrefix + label + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            _createdRunIds.Add(id);
            return id;
        }

        private static GameSaveData BuildSave(string runId, int level, int xp,
                                              string zone, string playerClass,
                                              int runOrdinal = 0)
        {
            var data = new GameSaveData
            {
                schemaVersion = "1.0",
                timestamp     = "2026-05-07T00:00:00",
                player = new PlayerSaveData
                {
                    playerClass = playerClass,
                    hp          = 100,
                    maxHp       = 100,
                    level       = level,
                    experience  = xp,
                    currentZone = zone,
                    position    = new Vector2(0f, 0f),
                },
            };
            data.SetMeta("run_id", runId);
            if (runOrdinal > 0) data.SetMeta("run_ordinal", runOrdinal.ToString());
            return data;
        }

        private string WriteAutosave(string runId, int level, int xp, string zone, int runOrdinal = 1)
        {
            // Default ordinal=1 so end-to-end pruning tests reflect a real
            // post-bootstrap save — only tests that intentionally exercise
            // the orphan-bootstrap pattern pass ordinal=0 explicitly.
            var data = BuildSave(runId, level, xp, zone, playerClass: "mague", runOrdinal: runOrdinal);
            string path = SaveFileManager.GetAutosavePath(runId);
            SaveFileManager.WriteSaveFile(path, data, "1.0");
            return path;
        }

        private string WriteManualSave(string runId, string slot, int level, int xp)
        {
            var data = BuildSave(runId, level, xp, zone: "Lobby", playerClass: "mague");
            string path = SaveFileManager.GetManualSavePath(runId, TestPrefix + slot);
            SaveFileManager.WriteSaveFile(path, data, "1.0");
            return path;
        }

        // ── Pure predicate ───────────────────────────────────────────────────

        [Test]
        public void IsPhantomRun_NullGroup_ReturnsFalse()
        {
            Assert.IsFalse(SaveFileManager.IsPhantomRun(null));
        }

        [Test]
        public void IsPhantomRun_EmptyGroup_ReturnsFalse()
        {
            var group = new RunGroupInfo { runId = "x", saves = new List<SaveSlotInfo>() };
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group));
        }

        [Test]
        public void IsPhantomRun_SingleAutosaveLv0NoXp_ReturnsTrue()
        {
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo
            {
                isAutoSave  = true,
                isCorrupted = false,
                level       = 0,
                experience  = 0,
                currentZone = "Lobby",
            });
            Assert.IsTrue(SaveFileManager.IsPhantomRun(group),
                "A single Lv.0 / 0-XP autosave with no manual saves is the canonical phantom.");
        }

        [Test]
        public void IsPhantomRun_LevelAboveOne_ReturnsFalse()
        {
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo
            {
                isAutoSave = true, level = 2, experience = 0, currentZone = "Lobby",
                // runOrdinal must be > 0 to assert the level-only-vs-phantom contract;
                // ordinal=0 is now an independent phantom criterion (orphan-bootstrap).
                runOrdinal = 1,
            });
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group),
                "A run that reached Lv.2 represents real progress and must not be pruned.");
        }

        [Test]
        public void IsPhantomRun_AnyXp_ReturnsFalse()
        {
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo
            {
                isAutoSave = true, level = 1, experience = 5, currentZone = "Lobby",
                runOrdinal = 1,
            });
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group),
                "Any XP gain proves the player took at least one action — not a phantom.");
        }

        [Test]
        public void IsPhantomRun_ManualSavePresent_ReturnsFalse()
        {
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo { isAutoSave = true,  level = 0, experience = 0, runOrdinal = 1 });
            group.saves.Add(new SaveSlotInfo { isAutoSave = false, level = 0, experience = 0, runOrdinal = 1 });
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group),
                "An explicit manual save is deliberate — never auto-prune even if Lv.0.");
        }

        [Test]
        public void IsPhantomRun_CorruptedSave_ReturnsFalse()
        {
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo { isAutoSave = true, isCorrupted = true, runOrdinal = 1 });
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group),
                "Corrupted saves stay on disk so the user can decide what to do.");
        }

        // ── New contract: ordinal=0 is itself a phantom (orphan-bootstrap) ────

        [Test]
        public void IsPhantomRun_OrdinalZero_WithProgress_StillPhantom()
        {
            // The "burst phantom" pattern: a save written between SaveService.BeginNewRun
            // and ProfileTelemetrySystem.StartRun, before the per-profile run_ordinal lands.
            // These saves can carry any level/xp/zone (the live player snapshot fired by
            // an event in that bootstrap window) but always lack a positive runOrdinal.
            // They must be treated as phantoms regardless of progression metrics.
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo
            {
                isAutoSave = true, level = 5, experience = 800, currentZone = "Forest",
                runOrdinal = 0,
            });
            Assert.IsTrue(SaveFileManager.IsPhantomRun(group),
                "A save with runOrdinal=0 is always an orphan-bootstrap artefact, even if " +
                "level/XP look real — those values come from the live player snapshot the " +
                "premature save captured before the run identity was finalised.");
        }

        [Test]
        public void IsPhantomRun_OrdinalZero_WithManualSave_NotPhantom()
        {
            // A manual save guards the run regardless of ordinal — the user explicitly
            // opted in to keep this slot, so we never auto-delete the parent folder.
            var group = new RunGroupInfo { runId = "x" };
            group.saves.Add(new SaveSlotInfo { isAutoSave = true,  level = 5, experience = 800, runOrdinal = 0 });
            group.saves.Add(new SaveSlotInfo { isAutoSave = false, level = 5, experience = 800, runOrdinal = 0 });
            Assert.IsFalse(SaveFileManager.IsPhantomRun(group),
                "Even with ordinal=0, an explicit manual save in the same folder " +
                "must keep the group from being pruned.");
        }

        // ── End-to-end pruning ───────────────────────────────────────────────

        [Test]
        public void PrunePhantomRuns_DeletesPhantomFolder()
        {
            string runId = NewRunId("p1");
            WriteAutosave(runId, level: 0, xp: 0, zone: "Lobby");

            string runDir = SaveFileManager.GetRunDirectory(runId);
            Assert.IsTrue(Directory.Exists(runDir), "Pre-condition: run folder exists.");

            int pruned = SaveFileManager.PrunePhantomRuns();

            Assert.GreaterOrEqual(pruned, 1,
                "PrunePhantomRuns must report at least one deletion when a phantom exists.");
            Assert.IsFalse(Directory.Exists(runDir),
                "The phantom run folder must be removed from disk.");
        }

        [Test]
        public void PrunePhantomRuns_PreservesRunWithProgression()
        {
            string runId = NewRunId("real");
            WriteAutosave(runId, level: 3, xp: 250, zone: "Forest");

            string runDir = SaveFileManager.GetRunDirectory(runId);
            int prunedBefore = SaveFileManager.PrunePhantomRuns();

            Assert.IsTrue(Directory.Exists(runDir),
                "A run with real progression must NOT be pruned.");
            // Sanity: re-running the pruner is idempotent and still preserves it.
            int prunedAfter = SaveFileManager.PrunePhantomRuns();
            Assert.AreEqual(0, prunedAfter,
                "Idempotency: a second prune pass with no new phantoms must delete nothing.");
            Assert.IsTrue(Directory.Exists(runDir),
                "The real run folder must survive a second prune call.");
        }

        [Test]
        public void PrunePhantomRuns_PreservesRunWithManualSave()
        {
            string runId = NewRunId("manual");
            WriteAutosave(runId, level: 0, xp: 0, zone: "Lobby");
            WriteManualSave(runId, "explicit", level: 0, xp: 0);

            string runDir = SaveFileManager.GetRunDirectory(runId);
            SaveFileManager.PrunePhantomRuns();

            Assert.IsTrue(Directory.Exists(runDir),
                "Even at Lv.0/0-XP, a folder containing a manual save is NOT a phantom.");
        }

        [Test]
        public void PrunePhantomRuns_RespectsActiveRunIdGuard()
        {
            string runId = NewRunId("active");
            WriteAutosave(runId, level: 0, xp: 0, zone: "Lobby");

            string runDir = SaveFileManager.GetRunDirectory(runId);
            int pruned = SaveFileManager.PrunePhantomRuns(activeRunIdToPreserve: runId);

            Assert.AreEqual(0, pruned,
                "The active runId must be exempted from pruning so the live SaveService " +
                "never gets its folder yanked out from under it.");
            Assert.IsTrue(Directory.Exists(runDir),
                "Active run folder must survive a prune pass that protected it.");
        }

        // ── Run ordinal round-trip ───────────────────────────────────────────

        [Test]
        public void ListSavesByRun_ReadsRunOrdinalFromMeta()
        {
            string runId = NewRunId("ord");
            // Manually write a save that includes meta.run_ordinal = 7.
            var data = BuildSave(runId, level: 5, xp: 300, zone: "Forest",
                                 playerClass: "elven", runOrdinal: 7);
            string path = SaveFileManager.GetAutosavePath(runId);
            SaveFileManager.WriteSaveFile(path, data, "1.0");

            var groups = SaveFileManager.ListSavesByRun();
            RunGroupInfo group = null;
            foreach (var g in groups) if (g.runId == runId) { group = g; break; }
            Assert.IsNotNull(group, "Newly written save must appear in the run grouping.");
            Assert.AreEqual(7, group.runOrdinal,
                "RunGroupInfo.runOrdinal must reflect meta.run_ordinal from disk.");
            Assert.AreEqual(7, group.saves[0].runOrdinal,
                "SaveSlotInfo.runOrdinal must match the meta value parsed during read.");
            Assert.IsTrue(group.displayName.StartsWith("Run #7 ·",
                System.StringComparison.Ordinal),
                $"Display name should lead with 'Run #N · ...' but was: '{group.displayName}'.");
        }

        [Test]
        public void ListSavesByRun_PreOrdinalSave_FallsBackToZero()
        {
            string runId = NewRunId("preord");
            // Save without run_ordinal meta — represents data written by an
            // older build before the ordinal feature shipped.
            var data = BuildSave(runId, level: 2, xp: 50, zone: "Lobby",
                                 playerClass: "mague", runOrdinal: 0);
            string path = SaveFileManager.GetAutosavePath(runId);
            SaveFileManager.WriteSaveFile(path, data, "1.0");

            var groups = SaveFileManager.ListSavesByRun();
            RunGroupInfo group = null;
            foreach (var g in groups) if (g.runId == runId) { group = g; break; }
            Assert.IsNotNull(group);
            Assert.AreEqual(0, group.runOrdinal,
                "Pre-ordinal save groups must report runOrdinal=0 (the missing-meta sentinel).");
            Assert.IsFalse(group.displayName.Contains("Run #"),
                "Without an ordinal the displayName must NOT show a 'Run #N' prefix " +
                "(falls back to the legacy 'class · zone · Lv.M' form).");
        }

        [Test]
        public void PrunePhantomRuns_SkipsLegacyGroup()
        {
            // Legacy bucket: write a save directly into Saves/legacy/ with no runId.
            string legacyDir = SaveFileManager.GetLegacyRunDirectory();
            Directory.CreateDirectory(legacyDir);
            string legacyName = TestPrefix + "legacy_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            string legacyFile = Path.Combine(legacyDir, legacyName + ".json");
            try
            {
                var data = BuildSave(runId: "", level: 0, xp: 0, zone: "Lobby", playerClass: "mague");
                SaveFileManager.WriteSaveFile(legacyFile, data, "1.0");

                SaveFileManager.PrunePhantomRuns();

                Assert.IsTrue(File.Exists(legacyFile),
                    "Legacy saves (runId empty) must never be auto-pruned regardless of level.");
            }
            finally
            {
                if (File.Exists(legacyFile)) File.Delete(legacyFile);
                string checksum = legacyFile.Replace(".json", ".sha256");
                if (File.Exists(checksum)) File.Delete(checksum);
            }
        }
    }
}
