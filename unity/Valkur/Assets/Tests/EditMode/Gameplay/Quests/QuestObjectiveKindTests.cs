using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Gameplay.Quests
{
    /// <summary>
    /// Pins the objective vocabulary itself: every <see cref="ObjectiveKind"/> the enum
    /// declares must produce a real objective, every objective must report through the
    /// one event the aggregator listens to, and the generated turn-in must be gated.
    ///
    /// <para><b>Why the coverage test earns its place.</b> An unmapped kind does not
    /// throw and does not fail a quest — <c>BuildObjective</c> logs a warning and returns
    /// null, and the objective is silently DROPPED from the list. So authoring a quest
    /// against a kind nobody implemented makes that quest EASIER rather than broken, and
    /// the only symptom is a log line in a console nobody is watching at the time. The
    /// enum is walked here so adding a value without a case is a red test.</para>
    /// </summary>
    [TestFixture]
    public class QuestObjectiveKindTests
    {
        private GameObject _host;
        private QuestManager _manager;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("QuestManagerHost");
            _manager = _host.AddComponent<QuestManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            GameEvents.Clear();
        }

        private static QuestDefinition QuestWith(params ObjectiveEntry[] objectives)
        {
            var def = ScriptableObject.CreateInstance<QuestDefinition>();
            def.questId = "q_test";
            def.displayName = "Test";
            def.objectives = objectives;
            return def;
        }

        private static ObjectiveEntry Entry(ObjectiveKind kind, string target = "x", int count = 1)
            => new ObjectiveEntry { kind = kind, targetId = target, count = count };

        // ── Coverage ───────────────────────────────────────────────────────

        [Test]
        public void EveryObjectiveKind_BuildsARealObjective()
        {
            var unmapped = new List<ObjectiveKind>();

            foreach (ObjectiveKind kind in Enum.GetValues(typeof(ObjectiveKind)))
            {
                var def = QuestWith(Entry(kind));
                Assert.IsTrue(_manager.StartQuest(def), $"StartQuest refused kind {kind}");

                var quest = _manager.GetActiveQuest(def.questId);

                // A dropped objective leaves an EMPTY quest, which Quest.Begin reads as
                // "already complete" — so the tell is that the quest is not active at
                // all rather than that it holds a null.
                if (quest == null || quest.Objectives.Count == 0) unmapped.Add(kind);

                _manager.AbandonQuest(def.questId);
                UnityEngine.Object.DestroyImmediate(def);

                // A fresh manager per kind: StartQuest refuses an id it has already
                // completed, and a degenerate quest completes on the spot.
                UnityEngine.Object.DestroyImmediate(_host);
                _host = new GameObject("QuestManagerHost");
                _manager = _host.AddComponent<QuestManager>();
            }

            CollectionAssert.IsEmpty(unmapped,
                "ObjectiveKind values with no case in QuestManager.BuildObjective: " +
                string.Join(", ", unmapped));
        }

        [Test]
        public void EveryObjectiveKind_ProducesAnObjectiveBase()
        {
            // The aggregator only hears from ObjectiveBase. A concrete objective that
            // implemented IObjective directly would tick, complete, and never tell the
            // quest — which is exactly the hole that existed while the aggregator
            // duck-typed KillCountObjective alone.
            foreach (ObjectiveKind kind in Enum.GetValues(typeof(ObjectiveKind)))
            {
                var def = QuestWith(Entry(kind));
                _manager.StartQuest(def);
                var quest = _manager.GetActiveQuest(def.questId);
                Assert.IsNotNull(quest, $"kind {kind} produced no active quest");
                Assert.IsInstanceOf<ObjectiveBase>(quest.Objectives[0],
                    $"kind {kind} produced an objective the Quest aggregator cannot hear");

                _manager.AbandonQuest(def.questId);
                UnityEngine.Object.DestroyImmediate(def);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = new GameObject("QuestManagerHost");
                _manager = _host.AddComponent<QuestManager>();
            }
        }

        // ── The aggregator hears every kind ────────────────────────────────

        [Test]
        public void ANonKillObjective_CompletingAlone_CompletesTheQuest()
        {
            // The defect this pins: Quest.Begin used to subscribe only to
            // KillCountObjective, so a quest whose only objective was anything else
            // went complete without OnCompleted ever firing — it stayed in the active
            // list forever and never paid out.
            var def = QuestWith(Entry(ObjectiveKind.Talk, "someone"));
            _manager.StartQuest(def);

            Assert.IsTrue(_manager.IsActive(def.questId));

            GameEvents.FireNpcConversed("someone");

            Assert.IsFalse(_manager.IsActive(def.questId), "quest stayed active after its only objective completed");
            Assert.IsTrue(_manager.IsCompleted(def.questId), "quest never reached the completed set");

            UnityEngine.Object.DestroyImmediate(def);
        }

        [Test]
        public void CraftObjective_TicksOnTheCraftEvent_AndIgnoresOtherRecipes()
        {
            var def = QuestWith(Entry(ObjectiveKind.Craft, "locro", 2));
            _manager.StartQuest(def);
            var obj = _manager.GetActiveQuest(def.questId).Objectives[0];

            GameEvents.FireItemCrafted("paella", "paella", 1);
            Assert.AreEqual(0, obj.Current, "a different recipe should not count");

            GameEvents.FireItemCrafted("locro", "locro", 1);
            Assert.AreEqual(1, obj.Current);

            UnityEngine.Object.DestroyImmediate(def);
        }

        [Test]
        public void ReachZoneObjective_TicksOnTheZoneItNames()
        {
            var def = QuestWith(Entry(ObjectiveKind.Reach, "Forest"));
            _manager.StartQuest(def);
            var obj = _manager.GetActiveQuest(def.questId).Objectives[0];

            GameEvents.FireZoneChanged("Lobby", "dungeon");
            Assert.AreEqual(0, obj.Current);

            // Case-insensitive, the way ZoneManager itself compares zone names.
            GameEvents.FireZoneChanged("dungeon", "forest");
            Assert.AreEqual(1, obj.Current);

            UnityEngine.Object.DestroyImmediate(def);
        }

        // ── The generated turn-in ──────────────────────────────────────────

        [Test]
        public void ATurnInPersona_AppendsOneExtraObjective()
        {
            var def = QuestWith(Entry(ObjectiveKind.Talk, "someone"));
            def.turnInPersonaId = "the_giver";
            _manager.StartQuest(def);

            var quest = _manager.GetActiveQuest(def.questId);
            Assert.AreEqual(2, quest.Objectives.Count,
                "the turn-in step should be appended, never authored by hand");
            Assert.IsInstanceOf<TalkObjective>(quest.Objectives[1]);

            UnityEngine.Object.DestroyImmediate(def);
        }

        [Test]
        public void TheTurnIn_DoesNotTickWhileAuthoredWorkRemains()
        {
            // The failure this exists for: the giver and the turn-in are usually the
            // SAME character, so a hand-authored Talk objective would be satisfied by
            // the very conversation that handed the quest over.
            var def = QuestWith(Entry(ObjectiveKind.Craft, "locro", 1));
            def.turnInPersonaId = "smith";
            _manager.StartQuest(def);

            var quest = _manager.GetActiveQuest(def.questId);
            var turnIn = quest.Objectives[1];

            GameEvents.FireNpcConversed("smith");
            Assert.AreEqual(0, turnIn.Current, "the turn-in ticked before the work was done");
            Assert.IsTrue(_manager.IsActive(def.questId));

            GameEvents.FireItemCrafted("locro", "locro", 1);
            GameEvents.FireNpcConversed("smith");

            Assert.IsTrue(_manager.IsCompleted(def.questId),
                "the turn-in should close the quest once the authored work is done");

            UnityEngine.Object.DestroyImmediate(def);
        }

        // ── Eligibility ────────────────────────────────────────────────────

        [Test]
        public void IsEligible_GatesOnLevelAndPrerequisites_ButStartQuestDoesNot()
        {
            var def = QuestWith(Entry(ObjectiveKind.Talk, "someone"));
            def.requiredLevel = 10;
            def.prerequisiteQuestIds = new[] { "q_missing" };

            Assert.IsFalse(_manager.IsEligible(def, playerLevel: 3), "level gate did not hold");
            Assert.IsFalse(_manager.IsEligible(def, playerLevel: 99), "prerequisite gate did not hold");

            // The console and the fixtures need to force a quest on; folding the gate
            // into StartQuest would make "give me that quest" untestable.
            Assert.IsTrue(_manager.StartQuest(def), "StartQuest is deliberately permissive");

            UnityEngine.Object.DestroyImmediate(def);
        }

        // ── Persistence ────────────────────────────────────────────────────

        [Test]
        public void SaveDocument_RoundTripsPerObjectiveProgress()
        {
            var def = QuestWith(
                Entry(ObjectiveKind.KillCount, "barbol", 5),
                Entry(ObjectiveKind.Craft, "locro", 3));
            _manager.StartQuest(def);

            GameEvents.FireItemCrafted("locro", "locro", 1);
            GameEvents.FireItemCrafted("locro", "locro", 1);

            var data = new QuestSaveData();
            _manager.WriteTo(data);

            Assert.AreEqual(1, data.activeQuestIds.Count);
            CollectionAssert.AreEqual(new[] { 2 }, data.activeObjectiveCounts,
                "the run length must say how many counters this quest contributed");

            var restored = new GameObject("Restored").AddComponent<QuestManager>();
            try
            {
                restored.ReadFrom(data, new List<QuestDefinition> { def });
                var quest = restored.GetActiveQuest(def.questId);
                Assert.IsNotNull(quest, "the quest did not come back");
                Assert.AreEqual(2, quest.Objectives[1].Current, "craft progress was lost across the round trip");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restored.gameObject);
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void AnEmptySaveDocument_RestoresAnEmptyLog_WithoutWarning()
        {
            // Every save written before this layer existed is exactly this, and it
            // describes a character who has accepted nothing.
            _manager.ReadFrom(new QuestSaveData(), Array.Empty<QuestDefinition>());
            Assert.AreEqual(0, _manager.ActiveIds.Count);
            Assert.AreEqual(0, _manager.CompletedIds.Count);
        }

        [Test]
        public void ATruncatedSaveDocument_CostsOneQuestsProgress_NotTheWholeLoad()
        {
            // A run length that overruns the flattened counters is what a half-written
            // or hand-edited save looks like. It must not throw.
            var def = QuestWith(Entry(ObjectiveKind.KillCount, "barbol", 5));
            var data = new QuestSaveData();
            data.activeQuestIds.Add(def.questId);
            data.activeObjectiveCounts.Add(4);   // claims four counters…
            data.activeProgress.Add(2);          // …and supplies one

            Assert.DoesNotThrow(() => _manager.ReadFrom(data, new List<QuestDefinition> { def }));
            Assert.IsTrue(_manager.IsActive(def.questId));

            UnityEngine.Object.DestroyImmediate(def);
        }
    }
}
