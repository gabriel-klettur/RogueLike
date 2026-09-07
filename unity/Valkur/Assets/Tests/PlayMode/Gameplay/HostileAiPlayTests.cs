using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// The AI, running.
    ///
    /// <para><b>WHY THIS FILE HAD TO EXIST.</b> The AI layer is around 8,000 lines and until now
    /// NOT ONE PlayMode test touched it — every fixture drove a <c>StateMachine</c> by hand,
    /// tick by tick, with a synthetic target. That is the right shape for a decision rule and
    /// it is blind to the whole class of defect this project actually ships: the ones that only
    /// exist in the composition, over real frames, against real physics. The cast freeze is the
    /// example. Every unit test of <c>NPCCastState</c> passed for the entire life of the bug,
    /// because each one asserted the transition it was written for and none of them asked how
    /// LONG the monster stood there.</para>
    ///
    /// <para>So these assert over ELAPSED TIME with the real update loop running: that a monster
    /// left alone with a target actually closes on it, that a caster does not spend the fight
    /// rooted, that a pack does not converge into a single stack, and that a neutral standing in
    /// the middle of all of it is never touched.</para>
    ///
    /// <para>Everything is built from bare GameObjects rather than from the shipped bootstrap:
    /// a scene-loading test would be measuring the scene. What is under test here is the brain
    /// and the states, given components and frames.</para>
    /// </summary>
    public class HostileAiPlayTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();

            // A CAMERA, and it is not decoration. FSMMonsterBrain adds EntityCulling, which
            // ticks an OFF-SCREEN entity on a slow interval — by design, so a distant monster's
            // timers do not run at full cost. With no Camera.main in the scene every monster is
            // permanently off screen, and the first run of these tests measured that: monsters
            // correctly in ChaseState moving 1.36 units in four seconds against a published
            // chase speed of 4 u/s. The numbers were real and were about the harness, which is
            // the trap CLAUDE.md names.
            var cameraGo = new GameObject("TestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 30f;                 // wide enough to hold every fixture
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.tag = "MainCamera";
            _scene.Add(cameraGo);

            _player = new GameObject("Player");
            _player.AddComponent<Health>().Initialize(1000);
            EntityRegistry.RegisterPlayer(_player);
            _scene.Add(_player);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.Destroy(go);
            _scene.Clear();
            EntityRegistry.Clear();
        }

        /// <summary>
        /// A monster with the parts a brain needs and nothing else. Deliberately not the shipped
        /// prefab: a prefab drags in visuals, audio, culling and a dozen other components, and a
        /// failure there would not tell you which layer broke.
        /// </summary>
        private FSMMonsterBrain Monster(string name, Vector2 at, string faction = "EVIL")
        {
            var go = new GameObject(name);
            go.transform.position = at;
            go.layer = 9;                                   // NPC

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;

            go.AddComponent<Health>().Initialize(200);
            go.AddComponent<EntityFaction>().SetAuthoredFaction(faction);
            go.AddComponent<ThreatMemory>();

            var brain = go.AddComponent<FSMMonsterBrain>();
            EntityRegistry.RegisterMonster(go);
            _scene.Add(go);

            // Awake has run by the time the next frame ticks; the FSM is built there.
            brain.enabled = true;
            return brain;
        }

        /// <summary>
        /// Waits <paramref name="seconds"/> of GAME time.
        ///
        /// <para>Not a frame count, and the difference is the whole reason this helper exists.
        /// `yield return null` advances one FRAME, and the test runner is not capped to 60 —
        /// the first version of these tests waited 120 frames "for about two seconds", got
        /// roughly half a second of simulated time, and reported a monster correctly chasing at
        /// 4 u/s as having moved 0.96 units. Every number in that failure was real and none of
        /// them was about the AI.</para>
        /// </summary>
        private static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        private static void Publish(FSMMonsterBrain brain, float aggro, float melee)
        {
            var fsm = brain.FSM;
            if (fsm == null) return;
            fsm.SetContext("aggro_range", aggro);
            fsm.SetContext("melee_range", melee);
            fsm.SetContext("speed", 2f);
            fsm.SetContext("chasing_speed", 4f);
        }

        // ── It fights ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AHostileClosesOnThePlayer_OverRealFrames()
        {
            _player.transform.position = new Vector2(10f, 0f);
            var brain = Monster("Hunter", Vector2.zero);
            yield return null;
            Publish(brain, aggro: 25f, melee: 1.5f);

            float startDistance = Vector2.Distance(brain.transform.position, _player.transform.position);

            yield return Seconds(2f);

            float endDistance = Vector2.Distance(brain.transform.position, _player.transform.position);
            Assert.That(endDistance, Is.LessThan(startDistance - 1f),
                $"A hostile handed a target must actually close on it. {startDistance:0.0} -> " +
                $"{endDistance:0.0} after two seconds, in state {brain.CurrentStateName}.");
        }

        [UnityTest]
        public IEnumerator ANeutralIsNeverTouchedByTheFight()
        {
            // The property vendors depend on, asserted where it can actually fail. It used to be
            // true only because their aggroRange was 0 — one edit away from a shopkeeper hunting
            // the player, and nothing in the suite would have noticed.
            _player.transform.position = new Vector2(6f, 0f);
            var vendor = Monster("Vendor", new Vector2(3f, 0f), faction: "NEUTRAL");
            var hostile = Monster("Hostile", Vector2.zero);
            yield return null;
            Publish(vendor, 25f, 1.5f);
            Publish(hostile, 25f, 1.5f);

            var vendorHealth = vendor.GetComponent<Health>();
            int hp = vendorHealth.CurrentHp;

            yield return Seconds(2.5f);

            Assert.AreEqual(hp, vendorHealth.CurrentHp,
                "A hostile walked past a neutral and hit it. Neutrality has to be structural.");
            Assert.IsNull(FactionTargeting.EnemyOf(vendor.gameObject),
                "and the neutral must be hunting nobody itself.");
        }

        [UnityTest]
        public IEnumerator APackSurrounds_RatherThanStacking()
        {
            // The failure this replaces is invisible to any unit test: six chasers all steer at
            // the target's centre, arrive down one bearing, and the separation system spends the
            // fight pushing them off each other.
            _player.transform.position = Vector2.zero;

            var pack = new List<FSMMonsterBrain>();
            for (int i = 0; i < 5; i++)
            {
                float a = i * 0.12f;                      // all approaching from nearly one side
                var b = Monster("Pack" + i, new Vector2(9f + i * 0.3f, a * 9f));
                pack.Add(b);
            }
            yield return null;
            foreach (var b in pack) Publish(b, 40f, 1.5f);

            yield return Seconds(5f);

            // The spread of bearings around the target is what "surrounded" means. Five
            // attackers stacked on one side occupy a narrow arc; five on a ring do not.
            float minAngle = float.MaxValue, maxAngle = float.MinValue;
            int alive = 0;
            foreach (var b in pack)
            {
                if (b == null) continue;
                Vector2 d = (Vector2)b.transform.position - (Vector2)_player.transform.position;
                if (d.sqrMagnitude < 0.0001f) continue;
                alive++;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                minAngle = Mathf.Min(minAngle, ang);
                maxAngle = Mathf.Max(maxAngle, ang);
            }

            Assert.That(alive, Is.GreaterThanOrEqualTo(4), "Most of the pack must still be alive.");
            Assert.That(maxAngle - minAngle, Is.GreaterThan(40f),
                $"The pack occupies only {maxAngle - minAngle:0}deg of arc around its target — " +
                "that is a queue, not an encirclement.");
        }

        [UnityTest]
        public IEnumerator DamageRedirectsAMonsterOntoWhoeverDealtIt()
        {
            // The composition: Health.OnDamagedBy -> FSMMonsterBrain -> ThreatMemory ->
            // FactionTargeting. Each half is unit-tested; only this asserts they are connected.
            _player.transform.position = new Vector2(12f, 0f);

            var ally = Monster("Ally", new Vector2(1f, 0f));
            // SetLifetime rather than a bare AddComponent: it is the documented path that
            // registers with AlliedUnit.Live, and PlayMode does run Awake — but every other
            // fixture in the project uses this call, and matching them keeps the two modes
            // reading the same way.
            ally.gameObject.AddComponent<AlliedUnit>().SetLifetime(-1f);

            var monster = Monster("Monster", Vector2.zero);
            yield return null;
            Publish(monster, 40f, 1.5f);

            Assert.AreSame(ally.gameObject, FactionTargeting.EnemyOf(monster.gameObject),
                "Precondition: with nothing recorded, the nearest player-side entity wins.");

            monster.GetComponent<Health>().TakeDamage(80, _player);
            yield return null;

            Assert.AreSame(_player, FactionTargeting.EnemyOf(monster.gameObject),
                "Damage must be able to pull a monster off the thing standing next to it.");
        }

        // ── It does not freeze ───────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ACasterDoesNotSpendTheFightRooted()
        {
            // THE REGRESSION GUARD. Measured before the fix, on the shipped roster: 9 of 31
            // monsters in NPCCastState at velocity 0.00, worst 10.3 s and still counting. Every
            // unit test of that state passed throughout, because each asserted the transition it
            // was written for and none asked how long the monster stood there.
            _player.transform.position = new Vector2(8f, 0f);
            var brain = Monster("Caster", Vector2.zero);
            yield return null;
            Publish(brain, 40f, 1.5f);

            int framesInCast = 0;
            float longestUnbrokenCast = 0f;
            float current = 0f;

            float castUntil = Time.time + 5f;
            while (Time.time < castUntil)
            {
                yield return null;
                bool casting = brain.CurrentStateName == "NPCCast";
                if (casting)
                {
                    framesInCast++;
                    current += Time.deltaTime;
                    longestUnbrokenCast = Mathf.Max(longestUnbrokenCast, current);
                }
                else current = 0f;
            }

            Assert.That(longestUnbrokenCast, Is.LessThan(2f),
                $"A single cast held the monster for {longestUnbrokenCast:0.00}s. The cast pose " +
                "is a tell, not a rate limit — holding for the spell's cooldown is the defect " +
                "this guard exists for.");
        }

        [UnityTest]
        public IEnumerator AMonsterNeverStopsMovingForeverWhileItHasATarget()
        {
            // Deliberately broad: it does not care WHICH state a monster is in, only that a
            // hostile with a live target somewhere out of reach is not a statue. That is the
            // question the cast freeze failed, and the same question would catch a deadlocked
            // path follower, a stuck flinch or a refused transition loop.
            _player.transform.position = new Vector2(14f, 0f);
            var brain = Monster("Mover", Vector2.zero);
            yield return null;
            Publish(brain, 40f, 1.5f);

            float travelled = 0f;
            Vector2 previous = brain.transform.position;

            float moveUntil = Time.time + 4f;
            while (Time.time < moveUntil)
            {
                yield return null;
                Vector2 now = brain.transform.position;
                travelled += Vector2.Distance(previous, now);
                previous = now;
            }

            Assert.That(travelled, Is.GreaterThan(2f),
                $"Moved {travelled:0.00} units in four seconds with a target 14 units away, " +
                $"ending in state {brain.CurrentStateName}.");
        }
    }
}
