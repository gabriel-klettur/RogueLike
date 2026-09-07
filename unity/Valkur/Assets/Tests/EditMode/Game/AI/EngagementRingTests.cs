using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Whether a pack surrounds or queues.
    ///
    /// <para>Every chaser used to steer at its target's centre. With one attacker that is
    /// correct and invisible; with six it is a conga line down one bearing, only the front one
    /// can reach, and the player holds a doorway against a horde by standing still. The ring is
    /// a targeting offset rather than a new behaviour, which is what makes it cheap — but it has
    /// two properties that are easy to lose and that nothing else in the project would catch.</para>
    ///
    /// <para>ONE: a LONE attacker must not use it, or a solo monster walks a circle around its
    /// victim before engaging — the exact behaviour the ring exists to prevent, introduced by
    /// the fix for it. TWO: slots must be STABLE, because re-dealing every frame makes the whole
    /// pack cross over each other whenever one dies.</para>
    /// </summary>
    public class EngagementRingTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        private GameObject Make(string name, Vector2 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            _scene.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            // Release before destroying: the registry is keyed by instance id, and Unity reuses
            // ids, so a leaked claim would be inherited by an unrelated object in the next test.
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        [Test]
        public void ALoneAttackerSteersAtTheTargetItself()
        {
            var target = Make("Target", Vector2.zero);
            var seeker = Make("Seeker", new Vector2(5f, 0f));

            var point = EngagementRing.ApproachPoint(seeker, target, 2f);

            Assert.That(Vector2.Distance(point, target.transform.position), Is.LessThan(0.01f),
                "One attacker has no ring to stand on. Sending it to a bearing would make a " +
                "solo monster orbit its victim before closing.");

            EngagementRing.Release(seeker, target);
        }

        [Test]
        public void TwoAttackersTakeOppositeSides()
        {
            var target = Make("Target", Vector2.zero);
            var a = Make("A", new Vector2(5f, 0f));
            var b = Make("B", new Vector2(5.1f, 0.2f));   // arriving from nearly the same bearing

            // Claim first, THEN measure. The first caller of a growing pack legitimately sees
            // an occupancy of one and steers at the centre — it re-asks every frame and joins
            // the ring on the next one. Measuring the claiming call measures that transient.
            EngagementRing.ApproachPoint(a, target, 2f);
            EngagementRing.ApproachPoint(b, target, 2f);

            var pa = EngagementRing.ApproachPoint(a, target, 2f);
            var pb = EngagementRing.ApproachPoint(b, target, 2f);

            Assert.That(Vector2.Distance(pa, pb), Is.GreaterThan(2f),
                "Two attackers converging down one bearing is the queue this exists to break. " +
                $"Got {pa} and {pb}.");

            EngagementRing.Release(a, target);
            EngagementRing.Release(b, target);
        }

        [Test]
        public void EveryAttackerStandsOnTheRing()
        {
            var target = Make("Target", Vector2.zero);
            var seekers = new List<GameObject>();
            for (int i = 0; i < 5; i++)
                seekers.Add(Make("S" + i, new Vector2(4f + i, i)));

            const float standoff = 3f;
            foreach (var s in seekers) EngagementRing.ApproachPoint(s, target, standoff);

            foreach (var s in seekers)
            {
                var p = EngagementRing.ApproachPoint(s, target, standoff);
                Assert.That(Vector2.Distance(p, target.transform.position),
                            Is.EqualTo(standoff).Within(0.01f),
                    "The slot must sit at the standoff, so a melee monster arrives swinging and " +
                    "a caster arrives at its band.");
            }

            Assert.AreEqual(5, EngagementRing.OccupancyOf(target));
            foreach (var s in seekers) EngagementRing.Release(s, target);
        }

        [Test]
        public void SlotsAreStableWhileTheClaimIsRenewed()
        {
            var target = Make("Target", Vector2.zero);
            var a = Make("A", new Vector2(5f, 0f));
            var b = Make("B", new Vector2(0f, 5f));

            EngagementRing.ApproachPoint(b, target, 2f);
            var first = EngagementRing.ApproachPoint(a, target, 2f);
            var second = EngagementRing.ApproachPoint(a, target, 2f);

            Assert.That(Vector2.Distance(first, second), Is.LessThan(0.01f),
                "Re-dealing every frame makes the whole pack cross over each other, which looks " +
                "like a bug and fights the separation system.");

            EngagementRing.Release(a, target);
            EngagementRing.Release(b, target);
        }

        [Test]
        public void ReleasingFreesTheSlot()
        {
            var target = Make("Target", Vector2.zero);
            var a = Make("A", new Vector2(5f, 0f));
            var b = Make("B", new Vector2(0f, 5f));

            EngagementRing.ApproachPoint(a, target, 2f);
            EngagementRing.ApproachPoint(b, target, 2f);
            Assert.AreEqual(2, EngagementRing.OccupancyOf(target));

            EngagementRing.Release(a, target);
            Assert.AreEqual(1, EngagementRing.OccupancyOf(target),
                "A dead attacker's slot must be recycled, or the survivors fan around a gap " +
                "exactly where it used to stand.");

            EngagementRing.Release(b, target);
        }

        [Test]
        public void ZeroStandoffIsAPassThrough()
        {
            // The ring must never be able to make a monster refuse to fight, and a caller with
            // nothing to stand off from is the degenerate case of that.
            var target = Make("Target", new Vector2(3f, 4f));
            var seeker = Make("Seeker", Vector2.zero);

            var point = EngagementRing.ApproachPoint(seeker, target, 0f);
            Assert.That(Vector2.Distance(point, target.transform.position), Is.LessThan(0.01f));
        }

        [Test]
        public void NullsAreSurvivable()
        {
            Assert.AreEqual(Vector2.zero, EngagementRing.ApproachPoint(null, null, 2f));
            Assert.AreEqual(0, EngagementRing.OccupancyOf(null));
            Assert.DoesNotThrow(() => EngagementRing.Release(null, null));
        }
    }
}
