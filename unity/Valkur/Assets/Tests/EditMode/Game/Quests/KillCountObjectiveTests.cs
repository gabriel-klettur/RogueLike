using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Game.Quests
{
    /// <summary>
    /// Pins <see cref="KillCountObjective"/>: counts monster deaths,
    /// filters by monsterKey when specified, ignores player deaths,
    /// stops counting at Target, and fires OnProgressChanged for UI.
    /// </summary>
    [TestFixture]
    public class KillCountObjectiveTests
    {
        [SetUp]
        public void SetUp() { GameEvents.Clear(); }

        [TearDown]
        public void TearDown() { GameEvents.Clear(); }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static GameObject MakeMonster(string monsterKey)
        {
            var go = new GameObject("Monster_" + monsterKey);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();
            var brain = go.AddComponent<FSMMonsterBrain>();
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = monsterKey;
            def.displayName = monsterKey;
            var f = typeof(FSMMonsterBrain).GetField("definition",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(brain, def);
            return go;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void GenericKillCount_CountsAnyNonPlayerDeath()
        {
            var obj = new KillCountObjective("kill5", "Kill any 5", target: 5);
            obj.Begin();

            for (int i = 0; i < 3; i++)
            {
                var victim = new GameObject("Monster");
                GameEvents.FireEntityDied(victim, killer: null);
                Object.DestroyImmediate(victim);
            }

            Assert.AreEqual(3, obj.Current);
            Assert.IsFalse(obj.IsComplete);
            obj.End();
        }

        [Test]
        public void PlayerDeath_DoesNotCount()
        {
            var obj = new KillCountObjective("kill1", "Kill 1", target: 1);
            obj.Begin();

            var player = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireEntityDied(player, killer: null);
                Assert.AreEqual(0, obj.Current,
                    "Player death must never tick a kill objective.");
            }
            finally { Object.DestroyImmediate(player); }
            obj.End();
        }

        [Test]
        public void MonsterKeyFilter_OnlyCountsMatching()
        {
            var obj = new KillCountObjective("kill_wolves", "Kill 3 wolves",
                target: 3, monsterKey: "wolf");
            obj.Begin();

            var wolf = MakeMonster("wolf");
            var bear = MakeMonster("bear");
            try
            {
                GameEvents.FireEntityDied(wolf, null);
                GameEvents.FireEntityDied(bear, null);
                GameEvents.FireEntityDied(wolf, null);

                Assert.AreEqual(2, obj.Current,
                    "Bear death must not increment a wolf-only objective.");
            }
            finally
            {
                Object.DestroyImmediate(wolf);
                Object.DestroyImmediate(bear);
            }
            obj.End();
        }

        [Test]
        public void Completion_StopsCountingPastTarget()
        {
            var obj = new KillCountObjective("kill2", "Kill 2", target: 2);
            obj.Begin();

            for (int i = 0; i < 5; i++)
            {
                var v = new GameObject("Monster");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);
            }

            Assert.AreEqual(2, obj.Current,
                "Objective must stop incrementing past Target so the quest log " +
                "doesn't show 7/2 after over-killing.");
            Assert.IsTrue(obj.IsComplete);
            obj.End();
        }

        [Test]
        public void OnProgressChanged_FiresPerIncrement()
        {
            int events = 0;
            int lastCurrent = 0;
            int lastTarget = 0;
            var obj = new KillCountObjective("count", "Count 3", target: 3);
            obj.OnProgressChanged += (c, t) => { events++; lastCurrent = c; lastTarget = t; };
            obj.Begin();

            for (int i = 0; i < 3; i++)
            {
                var v = new GameObject("Monster");
                GameEvents.FireEntityDied(v, null);
                Object.DestroyImmediate(v);
            }

            Assert.AreEqual(3, events, "Three increments → three events.");
            Assert.AreEqual(3, lastCurrent);
            Assert.AreEqual(3, lastTarget);
            obj.End();
        }

        [Test]
        public void DoubleBegin_DoesNotDoubleSubscribe()
        {
            var obj = new KillCountObjective("k", "Kill 1", target: 1);
            obj.Begin();
            obj.Begin(); // idempotent

            var v = new GameObject("Monster");
            GameEvents.FireEntityDied(v, null);
            Object.DestroyImmediate(v);

            Assert.AreEqual(1, obj.Current,
                "A second Begin() must not double-count the next death.");
            obj.End();
        }

        [Test]
        public void EndBeforeBegin_IsSilentNoop()
        {
            var obj = new KillCountObjective("k", "Kill 1", target: 1);
            Assert.DoesNotThrow(() => obj.End(),
                "Calling End before Begin must not throw — quests can be " +
                "cleaned up defensively without checking subscription state.");
        }
    }
}
