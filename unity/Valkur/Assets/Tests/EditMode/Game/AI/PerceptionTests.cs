using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// What a monster can and cannot notice.
    ///
    /// <para>Acquisition used to be written out separately in <see cref="IdleState"/> and
    /// <see cref="PatrolState"/>, and the two had already drifted — one asked
    /// <c>IsBlocked</c>, the other <c>IsClear</c>, and only one checked the target was
    /// alive. Both now go through <see cref="FSMPerception.TryAcquire"/>, so this fixture
    /// covers the rule for every hostile state at once.</para>
    ///
    /// <para>The cone is the part with a trap in it, and it is why
    /// <see cref="TheConeIsSuspended_ForAMomentAfterBeingHit"/> exists: a field of view a
    /// monster cannot get out of makes being attacked from behind unanswerable, because the
    /// flinch resumes the state it interrupted and the authored alert edge only fires when
    /// the attacker is BEYOND aggro range. A melee attacker standing behind a monster would
    /// be permanently invisible to it.</para>
    /// </summary>
    public class PerceptionTests
    {
        private const int WorldLayer = 11;

        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();
            _player = new GameObject("Player");
            _player.transform.position = new Vector2(5f, 0f);
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

        /// <summary>
        /// A machine that owns a body but no animator, so <see cref="FSMPerception.FacingOf"/>
        /// resolves the facing from the rigidbody's velocity — which a test can set exactly,
        /// unlike an animator's internal direction.
        /// </summary>
        private StateMachine MakeMonster(Vector2 at, float aggroRange, Vector2 facing)
        {
            var go = new GameObject("Monster");
            go.transform.position = at;
            var rb = go.AddComponent<Rigidbody2D>();
            _scene.Add(go);

            var fsm = new StateMachine(go, new IdleState());
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));
            fsm.SetContext("aggro_range", aggroRange);
            fsm.Begin();

            // AFTER Begin, not before: entering IdleState calls StopMovement, which zeroes
            // the velocity this fixture uses as the facing — so a velocity set first is
            // erased and every cone test silently falls back to the default east facing and
            // passes for the wrong reason. (Measured: it did.)
            rb.velocity = facing;

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

        // ── Range ────────────────────────────────────────────────────────────────

        [Test]
        public void APlayerInsideTheRing_IsAcquired()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);

            Assert.IsTrue(FSMPerception.TryAcquire(fsm, out var target));
            Assert.AreSame(_player, target);
        }

        [Test]
        public void APlayerOutsideTheRing_IsNot()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 3f, facing: Vector2.right);

            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _));
        }

        // ── Geometry ─────────────────────────────────────────────────────────────

        [Test]
        public void AWallBetweenThem_RefusesAcquisition()
        {
            Wall(new Vector2(2.5f, 0f), new Vector2(1f, 6f));
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);

            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _),
                "Before line of sight existed this was a naked distance test, so everything " +
                "behind a wall woke up when the player walked past it.");
        }

        // ── The cone ─────────────────────────────────────────────────────────────

        [Test]
        public void WithNoConeAuthored_EveryDirectionIsVisible()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.left);

            Assert.IsTrue(FSMPerception.TryAcquire(fsm, out _),
                "fov_degrees defaults to 360, which is the historical behaviour — an " +
                "unauthored monster must not silently go half blind.");
        }

        [Test]
        public void ATargetBehindTheMonster_IsOutsideANarrowCone()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.left);
            fsm.SetContext(FSMTuning.KeyFovDegrees, 90f);

            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _));
        }

        [Test]
        public void ATargetInFrontOfTheMonster_IsInsideTheCone()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);
            fsm.SetContext(FSMTuning.KeyFovDegrees, 90f);

            Assert.IsTrue(FSMPerception.TryAcquire(fsm, out _));
        }

        [Test]
        public void TheConeIsSuspended_ForAMomentAfterBeingHit()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.left);
            fsm.SetContext(FSMTuning.KeyFovDegrees, 90f);
            // Stop the flinch from firing, so what is under test is the perception change and
            // not DamageState: the roll is what decides whether the entity staggers, while
            // TimeSinceLastHit records that it noticed regardless.
            fsm.SetContext("damage_stop_probability", 0f);

            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _), "precondition: cannot see behind");

            fsm.QueueEvent(new FSMEvent { Type = FSMEventType.OnHit, Damage = 1 });
            fsm.Update(0.016f);

            Assert.IsTrue(FSMPerception.TryAcquire(fsm, out _),
                "Being shot tells you where the shooter is whether or not you were looking " +
                "at them. Without this a monster stabbed in the back can never turn around.");
        }

        // ── Suppression ──────────────────────────────────────────────────────────

        [Test]
        public void AMonsterThatJustFled_RefusesToReacquire()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);
            FSMPerception.SuppressAggro(fsm, 5f);

            Assert.IsTrue(FSMPerception.AggroSuppressed(fsm));
            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _),
                "FleeState hands over to PatrolState, which re-acquires on the very next " +
                "tick — this window is the only thing that stops the flee oscillating.");
        }

        // ── Viability ────────────────────────────────────────────────────────────

        [Test]
        public void ADeadTarget_IsNotPerceivable()
        {
            _player.GetComponent<Health>().TakeDamage(1000);
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);

            Assert.IsFalse(FSMPerception.TryAcquire(fsm, out _));
        }

        // ── Memory ───────────────────────────────────────────────────────────────

        [Test]
        public void RememberingASighting_SurvivesTheStateThatRecordedIt()
        {
            var fsm = MakeMonster(Vector2.zero, aggroRange: 10f, facing: Vector2.right);

            Assert.IsFalse(FSMTargetMemory.Has(fsm), "nothing has been seen yet");

            FSMTargetMemory.Remember(fsm, new Vector2(7f, 2f));

            Assert.IsTrue(FSMTargetMemory.Has(fsm));
            Assert.AreEqual(new Vector2(7f, 2f), FSMTargetMemory.Position(fsm));
            Assert.Less(FSMTargetMemory.Age(fsm), 1f);
        }

        [Test]
        public void WithNoMemory_TheSearchPositionIsTheMonstersOwn()
        {
            var fsm = MakeMonster(new Vector2(3f, 4f), aggroRange: 10f, facing: Vector2.right);

            Assert.AreEqual(new Vector2(3f, 4f), FSMTargetMemory.Position(fsm),
                "A search with no memory investigates where it stands, not the world origin.");
        }
    }
}
