using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Interaction;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins how the interact key decides WHAT the player meant.
    ///
    /// <para>The registry has two traversals — a flat scan for a handful of interactables and
    /// a spatial hash for a forest — and the whole risk of that design is that they answer
    /// differently. The last test here is the one that matters: the same population must give
    /// the same answer on both sides of the threshold.</para>
    /// </summary>
    [TestFixture]
    public class InteractableRegistryTests
    {
        /// <summary>
        /// A test double. The registry talks only to the interface, so nothing here needs a
        /// building, a profile or a scene.
        /// </summary>
        private sealed class FakeInteractable : IPlayerInteractable
        {
            public Bounds Bounds;
            public float Radius = 2f;
            public bool Available = true;

            /// <summary>
            /// Whether the badge would be drawn at all. Separate from <see cref="Available"/>
            /// on purpose: a spent seam is NOT interactable and still has to win the prompt in
            /// order to say so, which is exactly the case the registry has to get right.
            /// </summary>
            public bool Visible = true;

            public string Prompt = "Use";
            public int Begins;
            public int Cancels;

            public Vector2 InteractionPosition => Bounds.center;
            public Bounds InteractionBounds => Bounds;
            public float InteractionRadius => Radius;
            public bool CanInteract(GameObject player) => Available;
            public bool IsInteracting { get; private set; }

            public InteractionPromptInfo DescribePrompt(GameObject player)
            {
                if (!Visible) return InteractionPromptInfo.None;
                return new InteractionPromptInfo(
                    Available ? InteractionAvailability.Ready : InteractionAvailability.Blocked,
                    Prompt);
            }

            public void BeginInteraction(GameObject player) { Begins++; IsInteracting = true; }
            public void CancelInteraction() { Cancels++; IsInteracting = false; }
        }

        private readonly List<FakeInteractable> _registered = new List<FakeInteractable>();

        private FakeInteractable Add(Vector2 center, Vector2 extents, float radius = 2f)
        {
            var fake = new FakeInteractable
            {
                Bounds = new Bounds(center, extents * 2f),
                Radius = radius,
            };
            InteractableRegistry.Register(fake);
            _registered.Add(fake);
            return fake;
        }

        [TearDown]
        public void TearDown()
        {
            // The registry is static and Domain Reload is OFF, so leftovers would be walked by
            // the next fixture in the run and by the next Play session.
            foreach (var fake in _registered) InteractableRegistry.Unregister(fake);
            _registered.Clear();
            Assert.That(InteractableRegistry.Count, Is.Zero, "Registry leaked between tests.");
        }

        [Test]
        public void FindBest_ReturnsNothingWhenTheRegistryIsEmpty()
        {
            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.Null);
        }

        [Test]
        public void FindBest_IgnoresAnythingOutOfRange()
        {
            Add(new Vector2(50f, 50f), Vector2.one, radius: 1f);
            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.Null);
        }

        [Test]
        public void FindBest_IgnoresAnythingWithNothingToSay()
        {
            var gone = Add(Vector2.zero, Vector2.one);
            gone.Visible = false;

            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.Null,
                "A felled tree's stump has no prompt and must stop competing with the live " +
                "trees standing next to it.");
        }

        [Test]
        public void FindBest_StillOffersATargetThatIsRefusingTheKey()
        {
            // A spent seam is not interactable and still has to win the prompt, because
            // showing nothing is indistinguishable from a decorative rock — the player either
            // concludes the feature is broken or keeps walking into it hoping.
            var spent = Add(Vector2.zero, Vector2.one);
            spent.Available = false;

            var best = InteractableRegistry.FindBest(null, Vector2.zero);
            Assert.That(best, Is.SameAs(spent));
            Assert.That(best.DescribePrompt(null).Availability,
                Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(best.DescribePrompt(null).IsActionable, Is.False);
        }

        [Test]
        public void FindBest_MeasuresToTheSurfaceRatherThanThePivot()
        {
            // A wide mine face the player is standing against, and a narrow sapling whose
            // PIVOT is nearer but whose surface is not. Comparing centres hands the prompt to
            // the sapling, which is the wrong answer from where the player is standing.
            var wide = Add(new Vector2(0f, 6f), new Vector2(6f, 2f));
            Add(new Vector2(3.5f, 0f), new Vector2(0.2f, 0.2f));

            var best = InteractableRegistry.FindBest(null, new Vector2(0f, 3.6f));
            Assert.That(best, Is.SameAs(wide));
        }

        [Test]
        public void FindBest_PrefersTheNearestSurfaceAmongEqualCandidates()
        {
            Add(new Vector2(-1.5f, 0f), new Vector2(0.25f, 0.25f));
            var near = Add(new Vector2(0.6f, 0f), new Vector2(0.25f, 0.25f));

            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.SameAs(near));
        }

        [Test]
        public void FindBest_HonoursEachCandidatesOwnRadius()
        {
            // Same distance, different reach: only the generous one is in range.
            Add(new Vector2(0f, 3f), new Vector2(0.25f, 0.25f), radius: 0.5f);
            var reaches = Add(new Vector2(0f, -3f), new Vector2(0.25f, 0.25f), radius: 4f);

            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.SameAs(reaches));
        }

        [Test]
        public void Register_IsIdempotentAndUnregisterIsSafeTwice()
        {
            var fake = Add(Vector2.zero, Vector2.one);
            InteractableRegistry.Register(fake);
            Assert.That(InteractableRegistry.Count, Is.EqualTo(1));

            InteractableRegistry.Unregister(fake);
            InteractableRegistry.Unregister(fake);
            _registered.Remove(fake);
            Assert.That(InteractableRegistry.Count, Is.Zero);
        }

        [Test]
        public void FindBest_AgreesAcrossTheSpatialHashThreshold()
        {
            // Below the threshold the registry scans flatly; above it, it goes through a
            // spatial hash. Two answers to one question is the entire risk of that design, so
            // this grows the population past the switch and demands the same winner.
            var target = Add(new Vector2(0.8f, 0f), new Vector2(0.3f, 0.3f));
            var query = new Vector2(0.1f, 0f);

            Assert.That(InteractableRegistry.FindBest(null, query), Is.SameAs(target),
                "flat scan");

            // Padding placed far away so it changes the traversal without changing the answer.
            for (int i = 0; i < 40; i++)
                Add(new Vector2(100f + i * 5f, 100f), new Vector2(0.3f, 0.3f));

            Assert.That(InteractableRegistry.Count, Is.GreaterThan(24));
            Assert.That(InteractableRegistry.FindBest(null, query), Is.SameAs(target),
                "spatial hash");
        }

        [Test]
        public void AMovingInteractableIsFoundAfterItMoves_EvenPastTheHashThreshold()
        {
            // The hash indexes an entry by the position it held when the hash was last REBUILT,
            // and it rebuilds only on a membership change. Anything whose bounds move is
            // therefore looked up at a stale point — and only above the threshold, so it works
            // in an empty test scene and fails in the shipped world, which is measured at 88
            // nodes with 87 registered. That is why RegisterDynamic exists.
            var mover = new FakeInteractable
            {
                Bounds = new Bounds(new Vector2(0f, 0f), Vector2.one),
                Radius = 1.5f,
            };
            InteractableRegistry.RegisterDynamic(mover);
            _registered.Add(mover);

            for (int i = 0; i < 40; i++)
                Add(new Vector2(500f + i * 5f, 500f), new Vector2(0.3f, 0.3f));

            Assert.That(InteractableRegistry.Count, Is.GreaterThan(24),
                "The point of this test is the hashed traversal; keep the population above it.");

            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.SameAs(mover));

            // Now move it somewhere the stale index could not answer for, and query there.
            mover.Bounds = new Bounds(new Vector2(80f, -60f), Vector2.one);

            Assert.That(InteractableRegistry.FindBest(null, new Vector2(80f, -60f)), Is.SameAs(mover),
                "A moving interactable stopped being retrieved once it left the position the " +
                "hash cached for it — which reads in game as the prompt vanishing when you " +
                "walk, i.e. as a range bug rather than a registry one.");
            Assert.That(InteractableRegistry.FindBest(null, Vector2.zero), Is.Null,
                "And it must no longer answer for where it used to be.");
        }

        // Pointing ---------------------------------------------------------------------

        [Test]
        public void FindAt_ReturnsWhatIsUnderThePoint()
        {
            Add(new Vector2(-4f, 0f), new Vector2(0.5f, 0.5f), radius: 12f);
            var pointed = Add(new Vector2(3f, 0f), new Vector2(0.5f, 0.5f), radius: 12f);

            Assert.That(InteractableRegistry.FindAt(null, new Vector2(3f, 0f), Vector2.zero),
                Is.SameAs(pointed),
                "Proximity would have answered with the nearer one; pointing must not.");
        }

        [Test]
        public void FindAt_PrefersTheSmallestThingContainingThePoint()
        {
            // Overlap is the normal case: a shoal drawn inside a bay, a crystal on the face of
            // a mine. Answering with the larger box makes the contained thing unreachable and
            // the containing thing impossible to click through.
            Add(new Vector2(0f, 0f), new Vector2(6f, 6f), radius: 12f);
            var small = Add(new Vector2(0.5f, 0.5f), new Vector2(0.4f, 0.4f), radius: 12f);

            Assert.That(InteractableRegistry.FindAt(null, new Vector2(0.5f, 0.5f), Vector2.zero),
                Is.SameAs(small));
        }

        [Test]
        public void FindAt_IsStillRangeGated()
        {
            // A point query that answered across the map would let a gesture claim a click
            // aimed at something the player cannot reach — and whatever that click would
            // otherwise have done is lost for nothing.
            Add(new Vector2(40f, 0f), new Vector2(1f, 1f), radius: 1.5f);

            Assert.That(InteractableRegistry.FindAt(null, new Vector2(40f, 0f), Vector2.zero),
                Is.Null);
        }

        [Test]
        public void FindAt_IgnoresAPointOverNothing()
        {
            Add(new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), radius: 12f);
            Assert.That(InteractableRegistry.FindAt(null, new Vector2(9f, 9f), Vector2.zero),
                Is.Null, "A click on empty ground has to stay a click on empty ground.");
        }

        [Test]
        public void FindAt_SeesMovingInteractablesToo()
        {
            var mover = new FakeInteractable
            {
                Bounds = new Bounds(new Vector2(2f, 0f), Vector2.one),
                Radius = 12f,
            };
            InteractableRegistry.RegisterDynamic(mover);
            _registered.Add(mover);

            Assert.That(InteractableRegistry.FindAt(null, new Vector2(2f, 0f), Vector2.zero),
                Is.SameAs(mover));
        }

        [Test]
        public void Contains_ReportsMembershipForBothKinds()
        {
            var fixedOne = Add(Vector2.zero, Vector2.one);
            var mover = new FakeInteractable { Bounds = new Bounds(Vector2.one * 3f, Vector2.one) };
            InteractableRegistry.RegisterDynamic(mover);
            _registered.Add(mover);

            Assert.That(InteractableRegistry.Contains(fixedOne), Is.True);
            Assert.That(InteractableRegistry.Contains(mover), Is.True);

            // This is the check a sticky target needs and the object itself cannot answer: a
            // felled tree is still a live C# object that replies to everything except whether
            // it is still in the game.
            InteractableRegistry.Unregister(fixedOne);
            _registered.Remove(fixedOne);
            Assert.That(InteractableRegistry.Contains(fixedOne), Is.False);
            Assert.That(InteractableRegistry.Contains(null), Is.False);
        }

        [Test]
        public void UnregisterRemovesAMovingInteractableToo()
        {
            var mover = new FakeInteractable { Bounds = new Bounds(Vector2.zero, Vector2.one) };
            InteractableRegistry.RegisterDynamic(mover);
            Assert.That(InteractableRegistry.DynamicCount, Is.EqualTo(1));

            // One exit for both lists: a caller should not have to remember which it used.
            InteractableRegistry.Unregister(mover);
            Assert.That(InteractableRegistry.DynamicCount, Is.Zero);
            Assert.That(InteractableRegistry.Count, Is.Zero);
        }

        [Test]
        public void FindBest_RetrievesALargeInteractableTheQueryOnlyClips()
        {
            // The hash indexes by a POINT while range is measured against BOUNDS. A building
            // several units across, standing at the edge of the query, is exactly the case a
            // naive hash lookup drops — and it would be unhittable from the places you can
            // actually reach it.
            var huge = Add(new Vector2(30f, 0f), new Vector2(28f, 2f), radius: 1.5f);
            for (int i = 0; i < 40; i++)
                Add(new Vector2(-200f - i * 5f, 300f), new Vector2(0.3f, 0.3f));

            Assert.That(InteractableRegistry.Count, Is.GreaterThan(24));
            Assert.That(InteractableRegistry.FindBest(null, new Vector2(1.0f, 0f)), Is.SameAs(huge));
        }
    }
}
