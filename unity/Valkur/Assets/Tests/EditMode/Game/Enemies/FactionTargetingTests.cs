using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.Enemies
{
    /// <summary>
    /// Who each side hunts once an ally is on the field.
    ///
    /// <para>WHY THIS TEST EXISTS AND WHY THE GREEN SUITE DID NOT COVER IT. Twenty FSM call
    /// sites moved from <c>EntityRegistry.Player</c> to
    /// <see cref="FactionTargeting.EnemyOf"/>, and the whole monster suite stayed green — but
    /// that only proves the ALLY-ABSENT path is unchanged, because no existing test puts an
    /// <see cref="AlliedUnit"/> on the field. Both halves being individually correct while the
    /// COMPOSITION is wrong is the exact shape of the spawner coordinate drift, which saved
    /// perfectly and loaded 150 tiles away for months. A test that exercises only one half
    /// proves nothing; these assert the composition.</para>
    /// </summary>
    public class FactionTargetingTests
    {
        private GameObject _player;
        private GameObject _monster;
        private GameObject _ally;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();

            _player = new GameObject("Player");
            _player.AddComponent<Health>().Initialize(100);
            EntityRegistry.RegisterPlayer(_player);
        }

        [TearDown]
        public void TearDown()
        {
            // The registry and AlliedUnit's live list are STATIC and Domain Reload is off, so
            // a test that leaked either would change the answer for every test that runs
            // after it -- and would do so only in a full-suite run, never on its own.
            if (_ally != null) Object.DestroyImmediate(_ally);
            if (_monster != null) Object.DestroyImmediate(_monster);
            if (_player != null) Object.DestroyImmediate(_player);
            EntityRegistry.Clear();
        }

        private GameObject MakeMonster(string name, Vector2 at, bool allied)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            go.AddComponent<Health>().Initialize(50);
            EntityRegistry.RegisterMonster(go);
            if (allied)
            {
                // Added directly rather than through AlliedSummonService: the service needs a
                // MonsterSpawner and a monster prefab, and what is under test here is the
                // TARGETING, not the spawn path.
                var unit = go.AddComponent<AlliedUnit>();
                unit.SetLifetime(30f);
            }
            return go;
        }

        // ── The ally-absent path: nothing may have changed ───────────────────

        [Test]
        public void WithNoAllyOnTheField_AMonsterStillHuntsThePlayer()
        {
            _monster = MakeMonster("Monster", new Vector2(5f, 0f), allied: false);

            Assert.AreSame(_player, FactionTargeting.EnemyOf(_monster),
                "With no ally alive the substitution must resolve to exactly what the twenty " +
                "call sites resolved to before it existed.");
        }

        [Test]
        public void WithNoSeeker_TheAnswerIsStillThePlayer()
        {
            Assert.AreSame(_player, FactionTargeting.EnemyOf(null),
                "A null seeker is a boot-race, not a faction question.");
        }

        // ── The ally-present composition: the part the suite could not see ───

        [Test]
        public void AnAlly_HuntsTheNearestHostileAndNeverThePlayer()
        {
            _monster = MakeMonster("Monster", new Vector2(3f, 0f), allied: false);
            _ally = MakeMonster("Ally", Vector2.zero, allied: true);

            var target = FactionTargeting.EnemyOf(_ally);

            Assert.AreSame(_monster, target, "An ally must hunt the hostile monster.");
            Assert.AreNotSame(_player, target,
                "This is the whole defect the helper exists to prevent: a summon spawned " +
                "through the monster pipeline used to immediately hunt the person who cast it.");
        }

        [Test]
        public void AnAlly_PicksTheNEARESTHostile()
        {
            _monster = MakeMonster("Far", new Vector2(12f, 0f), allied: false);
            var near = MakeMonster("Near", new Vector2(2f, 0f), allied: false);
            _ally = MakeMonster("Ally", Vector2.zero, allied: true);

            try
            {
                Assert.AreSame(near, FactionTargeting.EnemyOf(_ally));
            }
            finally { Object.DestroyImmediate(near); }
        }

        [Test]
        public void AnAlly_NeverTargetsAnotherAlly()
        {
            _ally = MakeMonster("Ally", Vector2.zero, allied: true);
            var second = MakeMonster("Ally2", new Vector2(1f, 0f), allied: true);

            try
            {
                // No hostile exists, and the only other candidate is on our own side.
                Assert.IsNull(FactionTargeting.EnemyOf(_ally),
                    "An ally with nothing hostile in the world must find NOTHING rather than " +
                    "turning on its own side.");
            }
            finally { Object.DestroyImmediate(second); }
        }

        [Test]
        public void AMonster_RetargetsToAnAllyThatIsCloserThanThePlayer()
        {
            _player.transform.position = new Vector2(20f, 0f);
            _monster = MakeMonster("Monster", Vector2.zero, allied: false);
            _ally = MakeMonster("Ally", new Vector2(2f, 0f), allied: true);

            Assert.AreSame(_ally, FactionTargeting.EnemyOf(_monster),
                "A summon that could not be attacked would be an invulnerable turret. The " +
                "spell is a companion, so it has to be reachable.");
        }

        [Test]
        public void AMonster_KeepsHuntingThePlayerWhenTheAllyIsFurther()
        {
            _player.transform.position = new Vector2(1f, 0f);
            _monster = MakeMonster("Monster", Vector2.zero, allied: false);
            _ally = MakeMonster("Ally", new Vector2(15f, 0f), allied: true);

            Assert.AreSame(_player, FactionTargeting.EnemyOf(_monster),
                "An ally on the field must not pull aggro it has not earned by being close.");
        }

        // ── A corpse is not a target ─────────────────────────────────────────

        [Test]
        public void ADeadHostile_IsNotChosen()
        {
            _monster = MakeMonster("Dead", new Vector2(1f, 0f), allied: false);
            _monster.GetComponent<Health>().TakeDamage(9999);

            var far = MakeMonster("Alive", new Vector2(9f, 0f), allied: false);
            _ally = MakeMonster("Ally", Vector2.zero, allied: true);

            try
            {
                Assert.AreSame(far, FactionTargeting.EnemyOf(_ally),
                    "Without this an ally walks to whatever it killed last and stands there " +
                    "swinging until the body despawns, which reads as the summon being broken " +
                    "rather than as it having won.");
            }
            finally { Object.DestroyImmediate(far); }
        }

        // ── The registry itself ──────────────────────────────────────────────

        [Test]
        public void AnAllysRegistration_FollowsItsEnabledState()
        {
            _ally = MakeMonster("Ally", Vector2.zero, allied: true);
            Assert.IsTrue(AlliedUnit.AnyLive, "OnEnable should have registered it.");

            _ally.SetActive(false);
            Assert.IsFalse(AlliedUnit.AnyLive,
                "A disabled ally must leave the list, or the hostile fast path keeps paying " +
                "for a scan over something that is not on the field.");

            _ally.SetActive(true);
            Assert.IsTrue(AlliedUnit.AnyLive);
        }
    }
}
