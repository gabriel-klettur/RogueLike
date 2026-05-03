using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Game.Quests
{
    /// <summary>
    /// Pins <see cref="QuestManager"/>: StartQuest builds objectives from
    /// the SO, completion fires events + grants rewards, idempotent
    /// double-start, abandon path, and snapshot persistence (active
    /// progress, completed ids, unknown-id pruning on load).
    /// </summary>
    [TestFixture]
    public class QuestManagerTests
    {
        private GameObject _go;
        private QuestManager _manager;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _go = new GameObject("QuestManager");
            _manager = _go.AddComponent<QuestManager>();
        }

        [TearDown]
        public void TearDown()
        {
            EntityRegistry.UnregisterPlayer(EntityRegistry.Player);
            if (_go != null) Object.DestroyImmediate(_go);
            GameEvents.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static QuestDefinition MakeQuest(string id, int killCount, string monsterKey = null,
                                                  int xpReward = 0, int skillPointReward = 0)
        {
            var d = ScriptableObject.CreateInstance<QuestDefinition>();
            d.questId = id;
            d.displayName = id;
            d.objectives = new[]
            {
                new ObjectiveEntry
                {
                    kind = ObjectiveKind.KillCount,
                    targetId = monsterKey,
                    count = killCount,
                    description = "",
                }
            };
            d.xpReward = xpReward;
            d.skillPointReward = skillPointReward;
            return d;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void StartQuest_AddsToActive_FiresEvent()
        {
            var def = MakeQuest("q1", killCount: 3);
            string startedId = null;
            _manager.OnQuestStarted += (id) => startedId = id;
            try
            {
                bool ok = _manager.StartQuest(def);
                Assert.IsTrue(ok);
                Assert.IsTrue(_manager.IsActive("q1"));
                Assert.AreEqual("q1", startedId);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void StartQuest_AlreadyActive_IsNoop()
        {
            var def = MakeQuest("q", killCount: 1);
            try
            {
                Assert.IsTrue(_manager.StartQuest(def));
                Assert.IsFalse(_manager.StartQuest(def),
                    "Re-starting an active quest must be a no-op so UI doesn't have " +
                    "to track button state.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void StartQuest_AlreadyCompleted_IsNoop()
        {
            var def = MakeQuest("q", killCount: 1);
            try
            {
                _manager.StartQuest(def);
                // Force completion by killing a monster.
                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);
                Assert.IsTrue(_manager.IsCompleted("q"));

                Assert.IsFalse(_manager.StartQuest(def),
                    "Re-starting a completed quest must be a no-op — quests are one-shot.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void Completion_FiresEvent_AndMovesToCompleted()
        {
            var def = MakeQuest("kill_one", killCount: 1);
            string completedId = null;
            _manager.OnQuestCompleted += (id) => completedId = id;
            try
            {
                _manager.StartQuest(def);
                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);

                Assert.AreEqual("kill_one", completedId);
                Assert.IsFalse(_manager.IsActive("kill_one"));
                Assert.IsTrue(_manager.IsCompleted("kill_one"));
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void Completion_GrantsXpAndSkillPointsToPlayer()
        {
            var def = MakeQuest("rewards", killCount: 1, xpReward: 50, skillPointReward: 2);

            var player = new GameObject("Player");
            var xp = player.AddComponent<Experience>();
            xp.Initialize(0, 1);
            var tree = ScriptableObject.CreateInstance<SkillTree>();
            var skills = player.AddComponent<LearnedSkills>();
            skills.SetTree(tree);
            EntityRegistry.RegisterPlayer(player);

            try
            {
                _manager.StartQuest(def);
                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);

                Assert.AreEqual(50, xp.TotalXp,
                    "XP reward must flow to Experience component on completion.");
                Assert.AreEqual(2, skills.AvailablePoints,
                    "Skill-point reward must flow to LearnedSkills.");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void AbandonQuest_RemovesFromActive()
        {
            var def = MakeQuest("q", killCount: 5);
            try
            {
                _manager.StartQuest(def);
                _manager.AbandonQuest("q");
                Assert.IsFalse(_manager.IsActive("q"),
                    "Abandoned quests must drop out of the active set.");
                Assert.IsFalse(_manager.IsCompleted("q"),
                    "Abandoned ≠ completed — the quest is just gone.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void Snapshot_RoundTrip_PreservesStateAndProgress()
        {
            var def = MakeQuest("multi", killCount: 5);
            var done = MakeQuest("done", killCount: 1);
            try
            {
                _manager.StartQuest(def);
                _manager.StartQuest(done);

                // Tick the multi-objective once.
                var v = new GameObject("M");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);

                // 'done' completed (count=1), 'multi' has progress 1/5.
                Assert.IsTrue(_manager.IsCompleted("done"));

                var snap = _manager.ToSnapshot();
                Assert.AreEqual(1, snap.activeQuestIds.Count);
                Assert.AreEqual(1, snap.completedQuestIds.Count);
                Assert.AreEqual(1, snap.activeProgress[0][0],
                    "Snapshot must capture the per-objective Current counter.");

                // Fresh manager, restore.
                Object.DestroyImmediate(_go);
                _go = new GameObject("QuestManager2");
                _manager = _go.AddComponent<QuestManager>();
                _manager.FromSnapshot(snap, new List<QuestDefinition> { def, done });

                Assert.IsTrue(_manager.IsActive("multi"));
                Assert.IsTrue(_manager.IsCompleted("done"));
                var multiQuest = _manager.GetActiveQuest("multi");
                Assert.IsNotNull(multiQuest);
                Assert.AreEqual(1, multiQuest.Objectives[0].Current,
                    "Restored quest must resume with the saved progress so the player " +
                    "doesn't lose their grind.");
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(done);
            }
        }

        [Test]
        public void FromSnapshot_DropsUnknownActiveQuest_DoesNotCrash()
        {
            // Save mentions an active quest id that no longer exists in the
            // catalog (designer pruned it). FromSnapshot must skip it with
            // a warning instead of crashing the load.
            var snap = new QuestManager.Snapshot
            {
                activeQuestIds = new List<string> { "ghost_quest" },
                activeProgress = new List<int[]> { new[] { 0 } },
                completedQuestIds = new List<string>(),
            };
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ghost_quest"));
            _manager.FromSnapshot(snap, new List<QuestDefinition>());

            Assert.IsFalse(_manager.IsActive("ghost_quest"),
                "Pruned-id active quests must drop, not crash, on load.");
        }
    }
}
