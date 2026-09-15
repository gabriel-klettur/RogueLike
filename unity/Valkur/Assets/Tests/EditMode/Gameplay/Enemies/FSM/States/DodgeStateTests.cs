using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Gameplay.Enemies.FSM.States
{
    /// <summary>
    /// The first behaviour in this machine that answers a PROJECTILE rather than a position.
    ///
    /// <para>What these pin, in order of how easily each would be lost: that dodging is OFF
    /// unless a monster asks for it twice over (a tuned chance AND a set that declares
    /// <see cref="DodgeState"/>), that the sidestep leaves the shot's corridor rather than
    /// backing along it, that a blocked flank is answered by using the other one and two
    /// blocked flanks by standing still, and that the roll is SPENT whether or not it passes —
    /// which is the only thing standing between a 35 % chance and a certainty, because the
    /// sense runs every frame.</para>
    ///
    /// <para>The heading maths and the whitelist gate are tested directly on
    /// <see cref="FSMDodge"/>; the physics sweep behind <c>FSMThreatSense</c> is not, because
    /// an EditMode <c>OverlapCircle</c> against a pooled projectile with a live
    /// <c>Rigidbody2D</c> would be measuring the harness. What IS pinned is that the two
    /// callers ask through the same helper, so a future change to sensing cannot leave one of
    /// them behind.</para>
    /// </summary>
    public class DodgeStateTests
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

        private StateMachine Make(IState initial, Vector2 at, params string[] allowed)
        {
            var go = new GameObject("Monster");
            go.transform.position = at;
            go.AddComponent<Rigidbody2D>();
            _scene.Add(go);

            var fsm = new StateMachine(go, initial);
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));
            fsm.SetContext("aggro_range", 10f);
            fsm.SetContext("melee_range", 1.5f);
            fsm.SetContext("speed", 2f);
            fsm.SetContext("chasing_speed", 3f);
            if (allowed != null && allowed.Length > 0)
                fsm.SetAllowedStates(new HashSet<string>(allowed));
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

        // ── The heading ──────────────────────────────────────────────────────────

        [Test]
        public void Sidestep_LeavesTheCorridor_RatherThanBackingDownIt()
        {
            // A bolt travelling east. Retreating west would be the obvious answer and it is
            // the wrong one: a projectile outruns a monster, so backing along the line of fire
            // keeps the body inside the shot's own corridor and only delays the hit.
            var heading = FSMDodge.SidestepHeading(Vector2.zero, Vector2.right, 2f);

            Assert.AreNotEqual(Vector2.zero, heading, "An open field must offer a sidestep.");
            Assert.That(Mathf.Abs(Vector2.Dot(heading.normalized, Vector2.right)), Is.LessThan(0.01f),
                $"The sidestep must be perpendicular to the shot; got {heading}.");
        }

        [Test]
        public void Sidestep_TakesTheOpenFlank_WhenOneIsWalled()
        {
            // Bolt travelling east, so the flanks are north and south. Wall off the north one.
            Wall(new Vector2(0f, 2f), new Vector2(8f, 1f));

            var heading = FSMDodge.SidestepHeading(Vector2.zero, Vector2.right, 2.5f);

            Assert.AreNotEqual(Vector2.zero, heading, "One blocked flank still leaves the other.");
            Assert.That(heading.y, Is.LessThan(0f),
                $"Must dive to the open (south) flank, not into the wall; got {heading}.");
        }

        [Test]
        public void Sidestep_ReturnsZero_WhenBothFlanksAreBlocked()
        {
            Wall(new Vector2(0f, 2f), new Vector2(8f, 1f));
            Wall(new Vector2(0f, -2f), new Vector2(8f, 1f));

            Assert.AreEqual(Vector2.zero, FSMDodge.SidestepHeading(Vector2.zero, Vector2.right, 2.5f),
                "Zero is the caller's signal that there is nowhere to go — the monster stands " +
                "and takes it rather than grinding along a wall, exactly as FSMRetreat does.");
        }

        // ── The two gates ────────────────────────────────────────────────────────

        [Test]
        public void NoDodge_WhenTheMonsterHasNoDodgeChanceAuthored()
        {
            var fsm = Make(new IdleState(), Vector2.zero, "IdleState", "DodgeState");

            Assert.IsFalse(FSMDodge.ShouldDodgeProjectile(fsm, out _),
                "dodge_chance defaults to 0, which is every monster shipped before this " +
                "behaviour existed. They must not silently start dodging.");
        }

        [Test]
        public void NoDodge_WhenTheSetDoesNotDeclareDodgeState()
        {
            var fsm = Make(new IdleState(), Vector2.zero, "IdleState", "ChaseState");
            fsm.SetContext(FSMTuning.KeyDodgeChance, 1f);

            Assert.IsFalse(FSMDodge.ShouldDodgeProjectile(fsm, out _),
                "The allowed-state whitelist is the structural half of the gate: a set that " +
                "cannot enter DodgeState must not be able to reach it however it is tuned.");
        }

        // ── The state ────────────────────────────────────────────────────────────

        [Test]
        public void TheBurstMoves_AlongTheHeadingItWasGiven()
        {
            var fsm = Make(new DodgeState(Vector2.up), Vector2.zero, "DodgeState", "ChaseState");

            var v = Velocity(fsm);
            Assert.That(v.magnitude, Is.GreaterThan(0.1f), "Entering the state starts the burst.");
            Assert.That(Vector2.Dot(v.normalized, Vector2.up), Is.GreaterThan(0.99f),
                $"The burst must run along the requested heading; got {v}.");
        }

        [Test]
        public void TheBurstIsFasterThanTheChase()
        {
            var fsm = Make(new DodgeState(Vector2.up), Vector2.zero, "DodgeState", "ChaseState");

            // chasing_speed is 3 and the default multiplier is 2.2. A dodge that moved at walk
            // pace would be a monster stepping politely out of the way.
            Assert.That(Velocity(fsm).magnitude, Is.GreaterThan(3f * 1.5f));
        }

        [Test]
        public void TheBurstEnds_AndHandsBackToChase()
        {
            _player.transform.position = new Vector2(20f, 0f);   // well outside melee reach
            var fsm = Make(new DodgeState(Vector2.up), Vector2.zero, "DodgeState", "ChaseState");

            // Well past any legal duration: the state is capped at 0.6 s.
            fsm.Update(1f);

            Assert.IsInstanceOf<ChaseState>(fsm.CurrentState,
                "A dodge is part of fighting — it returns to the fight rather than to patrol, " +
                "which is the whole difference between it and a flee.");
        }

        [Test]
        public void TheBurstEndsInASwing_WhenItLandedInsideReach()
        {
            // The sidestep can end closer than it started — a dodge across a charging target
            // ends beside it. Routing that through Chase would spend a frame walking nowhere
            // before ChaseState decided the same thing.
            _player.transform.position = new Vector2(0.5f, 0f);
            var fsm = Make(new DodgeState(Vector2.up), Vector2.zero,
                           "DodgeState", "ChaseState", "AttackState");

            fsm.Update(1f);

            Assert.IsInstanceOf<AttackState>(fsm.CurrentState);
        }

        [Test]
        public void TheBurstStandsStill_WhenThereIsNowhereToGo()
        {
            // Authored entry (no heading given) with the target due west and both flanks walled.
            _player.transform.position = new Vector2(-3f, 0f);
            Wall(new Vector2(0f, 2.2f), new Vector2(10f, 1f));
            Wall(new Vector2(0f, -2.2f), new Vector2(10f, 1f));
            Physics2D.SyncTransforms();

            var fsm = Make(new DodgeState(), Vector2.zero, "DodgeState", "ChaseState");

            Assert.AreEqual(Vector2.zero, Velocity(fsm),
                "Cornered means standing, not grinding into scenery.");
        }

        [Test]
        public void TheCooldownIsSpent_EvenByADodgeThatCouldNotMove()
        {
            _player.transform.position = new Vector2(-3f, 0f);
            Wall(new Vector2(0f, 2.2f), new Vector2(10f, 1f));
            Wall(new Vector2(0f, -2.2f), new Vector2(10f, 1f));
            Physics2D.SyncTransforms();

            var fsm = Make(new DodgeState(), Vector2.zero, "DodgeState", "ChaseState");

            Assert.That(fsm.GetContextFloat(FSMDodge.KeyLastDodgeTime, float.NegativeInfinity),
                        Is.Not.EqualTo(float.NegativeInfinity),
                "Without this the authored transition re-fires on the very next tick and the " +
                "monster is pinned in a dodge it cannot perform.");
        }

        [Test]
        public void MarkDecided_HoldsOffTheNextDodge()
        {
            var fsm = Make(new IdleState(), Vector2.zero, "IdleState", "DodgeState");
            fsm.SetContext(FSMTuning.KeyDodgeChance, 1f);
            FSMDodge.MarkDecided(fsm);

            Assert.IsFalse(FSMDodge.ShouldDodgeProjectile(fsm, out _),
                "The roll is spent on the DECISION, not on the success. Re-rolling every frame " +
                "would turn any non-zero chance into certainty within two frames and the dial " +
                "would only control how long that took.");
        }

        // ── The callers ──────────────────────────────────────────────────────────

        [Test]
        public void BothChaseAndAttack_AskThroughTheSameHelper()
        {
            // A source check, because the two callers are the whole reachable surface of this
            // feature and a sensing change that reached only one of them would be invisible:
            // the monster would dodge while chasing and stand still mid-windup, which reads as
            // a bug in the dodge rather than as a missing call site.
            string dir = TestSourceRoot + "/_Project/Scripts/Gameplay/Enemies/FSM/States/";

            foreach (var file in new[] { "ChaseState.cs", "AttackState.cs" })
            {
                string code = System.IO.File.ReadAllText(dir + file);
                Assert.That(code, Does.Contain("FSMDodge.ShouldDodgeProjectile"),
                    $"{file} must ask the shared helper.");
                Assert.That(code, Does.Contain("new DodgeState("),
                    $"{file} must hold its own ChangeState — FSMBuiltInTransitionRegistryTests " +
                    "takes its census by reading these files, so a transition moved one level " +
                    "down is a coded edge no test can see.");
            }
        }

        [Test]
        public void AttackState_RefusesToDodgeAfterItsBlowHasLanded()
        {
            string code = System.IO.File.ReadAllText(
                TestSourceRoot + "/_Project/Scripts/Gameplay/Enemies/FSM/States/AttackState.cs");

            Assert.That(code, Does.Contain("!_attacked && FSMDodge.ShouldDodgeProjectile"),
                "A monster that could cancel any frame of any attack would be untouchable at " +
                "range, and the windup would stop being a tell worth reading.");
        }

        private static string TestSourceRoot =>
            System.IO.Path.GetDirectoryName(Application.dataPath) + "/Assets";
    }
}
