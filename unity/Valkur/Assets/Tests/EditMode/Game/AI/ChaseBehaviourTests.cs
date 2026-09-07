using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// The pursuit rules: when a chase ends, how close it gets, and what happens when the
    /// target goes out of sight.
    ///
    /// <para>None of the three had a test. The leash in particular had been DOCUMENTED in
    /// <see cref="ChaseState"/>'s own summary since the class was written and was not
    /// implemented at all — the only exit was player distance, so a monster followed across
    /// the whole map as long as the player stayed inside its aggro ring, and
    /// <c>barbol_gigante</c>'s ring is 30 units wide. A promise in a doc comment with nothing
    /// asserting it is exactly how that survives.</para>
    /// </summary>
    public class ChaseBehaviourTests
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

        private StateMachine MakeChaser(Vector2 at, float aggroRange = 10f, float meleeRange = 1.5f)
        {
            var go = new GameObject("Monster");
            go.transform.position = at;
            go.AddComponent<Rigidbody2D>();
            _scene.Add(go);

            var fsm = new StateMachine(go, new ChaseState());
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));
            fsm.SetContext("aggro_range", aggroRange);
            fsm.SetContext("melee_range", meleeRange);
            fsm.SetContext("chasing_speed", 3f);
            fsm.SetContext("speed", 1.5f);
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

        // ── Reach ────────────────────────────────────────────────────────────────

        [Test]
        public void AMeleeMonsterInReach_Attacks()
        {
            _player.transform.position = new Vector2(1f, 0f);
            var fsm = MakeChaser(Vector2.zero, meleeRange: 1.5f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<AttackState>(fsm.CurrentState);
        }

        [Test]
        public void APlayerPastTheHysteresis_EndsTheChase()
        {
            // 10 x the default 1.15 hysteresis = 11.5, so 12 is outside and 11 would not be.
            _player.transform.position = new Vector2(12f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 10f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState);
        }

        // ── The leash ────────────────────────────────────────────────────────────

        [Test]
        public void AMonsterDraggedPastItsLeash_GoesHome()
        {
            _player.transform.position = new Vector2(25f, 0f);
            var fsm = MakeChaser(new Vector2(24f, 0f), aggroRange: 10f, meleeRange: 0.5f);
            // Spawned at the origin, currently 24 units from it. The default leash is three
            // times the aggro range = 30, so authoring a short one is what puts it past.
            fsm.SetContext(FSMHomeAnchor.KeyX, 0f);
            fsm.SetContext(FSMHomeAnchor.KeyY, 0f);
            fsm.SetContext(FSMTuning.KeyLeashRange, 8f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<PatrolState>(fsm.CurrentState,
                "The leash is the only exit that measures the monster's own displacement " +
                "rather than the player's distance.");
        }

        [Test]
        public void InsideItsLeash_TheChaseContinues()
        {
            _player.transform.position = new Vector2(6f, 0f);
            var fsm = MakeChaser(new Vector2(4f, 0f), aggroRange: 10f, meleeRange: 0.5f);
            fsm.SetContext(FSMHomeAnchor.KeyX, 0f);
            fsm.SetContext(FSMHomeAnchor.KeyY, 0f);
            fsm.SetContext(FSMTuning.KeyLeashRange, 8f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState);
        }

        // ── The standoff ─────────────────────────────────────────────────────────

        [Test]
        public void ACasterInsideItsStandoff_BacksAway()
        {
            _player.transform.position = new Vector2(2f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 12f, meleeRange: 1.5f);
            fsm.SetContext(FSMTuning.KeyDesiredRange, 7f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState);
            Assert.Less(Velocity(fsm).x, 0f,
                "A caster that keeps closing is a melee monster in a robe — its own " +
                "NPCAutoCast minDistance gate then refuses the spells the standoff is for.");
        }

        [Test]
        public void ACasterInsideItsBand_HoldsStill()
        {
            _player.transform.position = new Vector2(7f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 12f, meleeRange: 1.5f);
            fsm.SetContext(FSMTuning.KeyDesiredRange, 7f);

            fsm.Update(0.016f);

            Assert.AreEqual(Vector2.zero, Velocity(fsm),
                "Standing is what lets the distance-gated spells fire, and a band is what " +
                "stops the monster alternating advance/retreat every frame around the exact " +
                "distance.");
        }

        [Test]
        public void ACasterTooFarOut_Advances()
        {
            _player.transform.position = new Vector2(11f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 12f, meleeRange: 1.5f);
            fsm.SetContext(FSMTuning.KeyDesiredRange, 7f);

            fsm.Update(0.016f);

            Assert.Greater(Velocity(fsm).x, 0f);
        }

        [Test]
        public void ACorneredCaster_SwingsInsteadOfGrindingIntoTheWall()
        {
            // Boxed in on every side except the one the player is standing in.
            _player.transform.position = new Vector2(1f, 0f);
            Wall(new Vector2(-1.2f, 0f), new Vector2(0.4f, 8f));
            Wall(new Vector2(0f, 1.2f), new Vector2(8f, 0.4f));
            Wall(new Vector2(0f, -1.2f), new Vector2(8f, 0.4f));

            var fsm = MakeChaser(Vector2.zero, aggroRange: 12f, meleeRange: 1.5f);
            fsm.SetContext(FSMTuning.KeyDesiredRange, 7f);

            fsm.Update(0.016f);

            Assert.IsInstanceOf<AttackState>(fsm.CurrentState,
                "A caster that refuses to fight when it has nowhere to give ground is a free " +
                "kill, and it reads as the AI having hung.");
        }

        // ── Sight ────────────────────────────────────────────────────────────────

        [Test]
        public void LosingSightForLongEnough_StartsASearch()
        {
            _player.transform.position = new Vector2(6f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 10f, meleeRange: 1.5f);
            Wall(new Vector2(3f, 0f), new Vector2(1f, 8f));

            // Default sight memory is 3 s.
            for (int i = 0; i < 5; i++) fsm.Update(1f);

            Assert.IsInstanceOf<SearchState>(fsm.CurrentState,
                "Sight used to be checked on ACQUISITION only, so a committed monster " +
                "tracked the player through a building forever and breaking line of sight " +
                "was not a mechanic the player had.");
        }

        [Test]
        public void ABrieflyBlockedLine_DoesNotAbandonTheChase()
        {
            _player.transform.position = new Vector2(6f, 0f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 10f, meleeRange: 1.5f);
            Wall(new Vector2(3f, 0f), new Vector2(1f, 8f));

            fsm.Update(0.5f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState,
                "A pillar, a corner or another monster crossing the line must not reset the " +
                "pursuit — that is what the memory window is for.");
        }

        [Test]
        public void SeeingTheTarget_RecordsWhereItWas()
        {
            _player.transform.position = new Vector2(6f, 1f);
            var fsm = MakeChaser(Vector2.zero, aggroRange: 10f, meleeRange: 1.5f);

            fsm.Update(0.016f);

            Assert.IsTrue(FSMTargetMemory.Has(fsm));
            Assert.AreEqual(new Vector2(6f, 1f), FSMTargetMemory.Position(fsm),
                "The last SEEN position is the only thing a search has to go on.");
        }
    }
}
