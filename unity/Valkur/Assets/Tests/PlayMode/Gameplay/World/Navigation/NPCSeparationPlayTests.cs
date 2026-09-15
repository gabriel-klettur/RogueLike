using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Tests.PlayMode.Gameplay.World.Navigation
{
    /// <summary>
    /// The thing that stops a pack becoming one monster.
    ///
    /// <para><b>`NPCSeparationSystem` had no fixture in either mode</b>, which is the same gap
    /// as the AI itself: it runs in <c>FixedUpdate</c>, it reads collider radii off live
    /// entities, and it writes through the same <c>Rigidbody2D</c> the FSM states write — so
    /// there is no honest way to test it without physics. An EditMode fixture would have had to
    /// call its private step directly, which measures the arithmetic and not the thing that can
    /// actually break: whether the push it applies survives contact with everything else
    /// writing velocity in the same frame.</para>
    ///
    /// <para>What is asserted is deliberately weak on MAGNITUDE and strong on DIRECTION and on
    /// what must NOT happen. The exact push is tuning and will move; that overlapping bodies
    /// separate, that separated ones are left alone, and that the system never teleports
    /// anybody, are the properties a pack depends on.</para>
    /// </summary>
    public class NPCSeparationPlayTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _systemGo;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();

            // See HostileAiPlayTests: without a Camera.main every entity reads as off-screen and
            // the systems that throttle on visibility measure the harness rather than themselves.
            var cameraGo = new GameObject("TestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 30f;
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.tag = "MainCamera";
            _scene.Add(cameraGo);

            _systemGo = new GameObject("Separation");
            _systemGo.AddComponent<NPCSeparationSystem>();
            _scene.Add(_systemGo);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.Destroy(go);
            _scene.Clear();
            EntityRegistry.Clear();
        }

        private GameObject Npc(string name, Vector2 at)
        {
            var go = new GameObject(name) { layer = 9 };     // NPC
            go.transform.position = at;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.5f;

            go.AddComponent<Valkur.Gameplay.Health>().Initialize(100);
            EntityRegistry.RegisterMonster(go);
            _scene.Add(go);
            return go;
        }

        /// <summary>
        /// Waits a number of PHYSICS steps, which for this system is the honest unit —
        /// `NPCSeparationSystem` runs in FixedUpdate, so its work is counted in fixed steps
        /// however fast the render loop happens to be going. (Its sibling fixture,
        /// HostileAiPlayTests, waits on SECONDS instead, because the FSM runs in Update and a
        /// frame count there measures the test runner rather than the game.)
        /// </summary>
        private static IEnumerator Settle(int fixedSteps)
        {
            for (int i = 0; i < fixedSteps; i++) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator TwoOverlappingBodiesPushApart()
        {
            var a = Npc("A", Vector2.zero);
            var b = Npc("B", new Vector2(0.15f, 0f));       // well inside each other's radius

            float before = Vector2.Distance(a.transform.position, b.transform.position);
            yield return Settle(60);
            float after = Vector2.Distance(a.transform.position, b.transform.position);

            Assert.That(after, Is.GreaterThan(before),
                $"Two bodies at {before:0.00} units did not separate ({after:0.00}). Without " +
                "this a pack converging on one target becomes a single monster with one " +
                "hitbox, and only the front of it can be fought.");
        }

        [UnityTest]
        public IEnumerator SeparatedBodiesAreLeftAlone()
        {
            // The other half, and the one that would break a formation: a system that pushes
            // whether or not anybody is crowded turns the engagement ring into a slow explosion.
            var a = Npc("A", Vector2.zero);
            var b = Npc("B", new Vector2(6f, 0f));

            Vector2 startA = a.transform.position;
            Vector2 startB = b.transform.position;

            yield return Settle(60);

            Assert.That(Vector2.Distance(startA, a.transform.position), Is.LessThan(0.25f),
                "A body with nobody near it must not be pushed.");
            Assert.That(Vector2.Distance(startB, b.transform.position), Is.LessThan(0.25f));
        }

        [UnityTest]
        public IEnumerator SeparationNeverTeleports()
        {
            // A push applied as a POSITION rather than through the body is the classic version
            // of this bug, and it is invisible until something is standing next to a wall.
            var bodies = new List<GameObject>();
            for (int i = 0; i < 6; i++)
                bodies.Add(Npc("Crowd" + i, new Vector2(i * 0.05f, i * 0.03f)));   // all on one spot

            var previous = new List<Vector2>();
            foreach (var go in bodies) previous.Add(go.transform.position);

            for (int step = 0; step < 60; step++)
            {
                yield return new WaitForFixedUpdate();
                for (int i = 0; i < bodies.Count; i++)
                {
                    Vector2 now = bodies[i].transform.position;
                    Assert.That(Vector2.Distance(previous[i], now), Is.LessThan(1f),
                        $"{bodies[i].name} moved more than a unit in one physics step — that is " +
                        "a position write, not a force.");
                    previous[i] = now;
                }
            }
        }

        [UnityTest]
        public IEnumerator ACrowdOnOneTileResolvesIntoASpread()
        {
            // The realistic case: a wave spawner drops six bodies at nearly the same point.
            var bodies = new List<GameObject>();
            for (int i = 0; i < 6; i++)
                bodies.Add(Npc("Wave" + i, new Vector2(i * 0.04f, 0f)));

            yield return Settle(120);

            float minGap = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                    minGap = Mathf.Min(minGap, Vector2.Distance(
                        bodies[i].transform.position, bodies[j].transform.position));

            Assert.That(minGap, Is.GreaterThan(0.2f),
                $"Closest pair is still {minGap:0.00} units apart after two seconds. A wave that " +
                "arrives stacked has to come apart, or the whole spawn reads as one enemy.");
        }
    }
}
