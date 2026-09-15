using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Gameplay.Enemies.FSM.States
{
    /// <summary>
    /// The two states that decide what a monster does when it is NOT winning: running away,
    /// and looking for something it lost.
    ///
    /// <para><see cref="FleeState"/> shipped with two defects that made fleeing worse than not
    /// fleeing at all. It ran in a straight line with no geometry test, so a monster cornered
    /// by a building pressed itself into the wall for the whole window; and it exited into
    /// <see cref="PatrolState"/>, which re-acquires on the very next tick, so a monster under
    /// its flee threshold oscillated on the authored transition's cooldown forever.</para>
    /// </summary>
    public class FleeAndSearchTests
    {
        private const int WorldLayer = 11;

        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();
            _player = new GameObject("Player");
            _player.AddComponent<Health>().Initialize(100);
            EntityRegistry.RegisterPlayer(_player);
            _scene.Add(_player);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            EntityRegistry.Clear();
            Physics2D.SyncTransforms();
        }

        private StateMachine Make(IState initial, Vector2 at, float aggroRange = 10f)
        {
            var go = new GameObject("Monster");
            go.transform.position = at;
            go.AddComponent<Rigidbody2D>();
            _scene.Add(go);

            var fsm = new StateMachine(go, initial);
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));
            fsm.SetContext("aggro_range", aggroRange);
            fsm.SetContext("melee_range", 1.5f);
            fsm.SetContext("speed", 2f);
            fsm.SetContext("chasing_speed", 3f);
            fsm.Begin();
            Physics2D.SyncTransforms();
            return fsm;
        }

        private GameObject Wall(Vector2 centre, Vector2 size)
        {
            var go = new GameObject("Wall") { layer = WorldLayer };
            go.transform.position = centre;
            go.AddComponent<BoxCollider2D>().size = size;
            _scene.Add(go);
            Physics2D.SyncTransforms();
            return go;
        }

        private static Vector2 Velocity(StateMachine fsm)
            => fsm.Owner.GetComponent<Rigidbody2D>().velocity;

        // ── Flee ─────────────────────────────────────────────────────────────────

        [Test]
        public void FleeingRunsAwayFromTheThreat()
        {
            _player.transform.position = new Vector2(2f, 0f);
            var fsm = Make(new FleeState(), Vector2.zero);

            fsm.Update(0.016f);

            Assert.Less(Velocity(fsm).x, 0f);
        }

        [Test]
        public void FleeingTurnsAsideRatherThanRunningIntoAWall()
        {
            // Straight away from the player is -X, and that is exactly where the wall is.
            _player.transform.position = new Vector2(3f, 0f);
            Wall(new Vector2(-2f, 0f), new Vector2(1f, 3f));
            var fsm = Make(new FleeState(), Vector2.zero);

            fsm.Update(0.016f);

            var v = Velocity(fsm);
            Assert.Greater(v.sqrMagnitude, 0.01f, "it must still be running");
            Assert.Greater(Mathf.Abs(v.y), 0.1f,
                "The escape fan is what makes the difference between fleeing and vibrating " +
                "against the nearest building for three seconds.");
        }

        [Test]
        public void ACorneredMonster_StandsStillInsteadOfGrindingIntoTheWall()
        {
            _player.transform.position = new Vector2(2f, 0f);
            Wall(new Vector2(-1.2f, 0f), new Vector2(0.4f, 8f));
            Wall(new Vector2(0f, 1.2f), new Vector2(8f, 0.4f));
            Wall(new Vector2(0f, -1.2f), new Vector2(8f, 0.4f));
            var fsm = Make(new FleeState(), Vector2.zero);

            fsm.Update(0.016f);

            Assert.AreEqual(Vector2.zero, Velocity(fsm));
        }

        [Test]
        public void TheFleeEnds_AndSuppressesReacquisition()
        {
            // Far enough away that the "got clear" half of the exit passes as soon as the
            // timer does.
            _player.transform.position = new Vector2(40f, 0f);
            var fsm = Make(new FleeState(), Vector2.zero, aggroRange: 10f);

            for (int i = 0; i < 5; i++) fsm.Update(1f);   // default flee duration is 3 s

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState);
            Assert.IsTrue(FSMPerception.AggroSuppressed(fsm),
                "Without the regroup window PatrolState re-acquires on the next tick and the " +
                "whole flee was a two-frame animation of turning round.");
        }

        [Test]
        public void AMonsterStillBeingChased_KeepsRunningPastItsTimer()
        {
            _player.transform.position = new Vector2(2f, 0f);
            var fsm = Make(new FleeState(), Vector2.zero, aggroRange: 10f);

            // Past the 3 s duration but well inside the 2x hard ceiling, with the threat
            // still inside the aggro ring: the flee is not finished.
            fsm.Update(3.5f);

            Assert.IsInstanceOf<FleeState>(fsm.CurrentState);
        }

        [Test]
        public void AMonsterThatCannotBreakContact_StopsFleeingAtTheCeiling()
        {
            _player.transform.position = new Vector2(2f, 0f);
            var fsm = Make(new FleeState(), Vector2.zero, aggroRange: 10f);

            fsm.Update(7f);   // 3 s duration x the 2x ceiling

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState,
                "Fleeing for the rest of the fight is not a behaviour — the ceiling is what " +
                "makes a cornered monster turn and fight again.");
        }

        // ── Search ───────────────────────────────────────────────────────────────

        [Test]
        public void ASearchWalksTowardTheLastKnownPosition()
        {
            _player.transform.position = new Vector2(50f, 0f);   // far away, unacquirable
            var fsm = Make(new SearchState(), Vector2.zero);
            FSMTargetMemory.Remember(fsm, new Vector2(8f, 0f));
            fsm.ChangeState(new SearchState());                  // re-enter so it reads the memory

            fsm.Update(0.016f);

            Assert.Greater(Velocity(fsm).x, 0f);
        }

        [Test]
        public void ASearchThatFindsNothing_GivesUp()
        {
            _player.transform.position = new Vector2(50f, 0f);
            var fsm = Make(new SearchState(), Vector2.zero);

            for (int i = 0; i < 8; i++) fsm.Update(1f);   // default search duration is 6 s

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState);
        }

        [Test]
        public void ASearchThatFindsTheTarget_ResumesTheChase()
        {
            _player.transform.position = new Vector2(3f, 0f);
            var fsm = Make(new SearchState(), Vector2.zero, aggroRange: 10f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState);
        }

        [Test]
        public void ASearchCannotSeeThroughTheWallItIsWalkingAround()
        {
            _player.transform.position = new Vector2(3f, 0f);
            Wall(new Vector2(1.5f, 0f), new Vector2(0.5f, 8f));
            var fsm = Make(new SearchState(), Vector2.zero, aggroRange: 10f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<SearchState>(fsm.CurrentState,
                "Re-finding the target runs the same acquisition test as spotting it the " +
                "first time, geometry included.");
        }

        [Test]
        public void ASearchPrefersAFreshAlertOverItsOwnMemory()
        {
            _player.transform.position = new Vector2(50f, 0f);
            var fsm = Make(new SearchState(), Vector2.zero);
            FSMTargetMemory.Remember(fsm, new Vector2(-8f, 0f));
            FSMAlert.Raise(fsm, new Vector2(8f, 0f));
            fsm.ChangeState(new SearchState());

            fsm.Update(0.016f);

            Assert.Greater(Velocity(fsm).x, 0f,
                "A neighbour who has just seen the target knows more than this monster's own " +
                "stale sighting.");
            Assert.IsFalse(FSMAlert.IsPending(fsm),
                "and the alert is consumed, or the monster re-takes it forever.");
        }
    }
}
