using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Navigation
{
    /// <summary>
    /// The A* every chasing monster depends on, which had no test of any kind.
    ///
    /// <para>What is pinned here is the CONTRACT rather than the algorithm: that a path is
    /// returned, that it does not begin by walking backwards into the caller's own tile
    /// centre, that it goes AROUND geometry instead of through it, and that the frame budget
    /// distinguishes "no path exists" from "not this frame". That last distinction is the
    /// whole point of the budget — a follower told "no path" falls back to walking straight
    /// at the target, which for a refused-because-busy search would mean walking into the
    /// wall the path was routing around.</para>
    /// </summary>
    public class PathFinderTests
    {
        private const int WorldLayer = 11;

        private readonly List<GameObject> _scene = new List<GameObject>();
        private PathFinder _finder;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("PathFinder");
            _scene.Add(go);
            _finder = go.AddComponent<PathFinder>();

            // EditMode does not fire Awake, and SingletonMonoBehaviour.Awake is what installs
            // Instance — without it HasInstance stays false and the follower silently skips
            // every repath.
            typeof(PathFinder)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_finder, null);

            PathFinder.InvalidateWalkability();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            PathFinder.InvalidateWalkability();
            Physics2D.SyncTransforms();
        }

        private void Wall(Vector2 centre, Vector2 size)
        {
            var go = new GameObject("Wall") { layer = WorldLayer };
            go.transform.position = centre;
            go.AddComponent<BoxCollider2D>().size = size;
            _scene.Add(go);
            Physics2D.SyncTransforms();

            // Walkability is memoised, and the baker calls this after every rebake for the
            // same reason: without it the solver keeps routing through walls that were
            // painted after the first probe.
            PathFinder.InvalidateWalkability();
        }

        private static float Length(List<Vector2> path, Vector2 start)
        {
            float total = 0f;
            Vector2 prev = start;
            for (int i = 0; i < path.Count; i++) { total += Vector2.Distance(prev, path[i]); prev = path[i]; }
            return total;
        }

        // ── The basics ───────────────────────────────────────────────────────────

        [Test]
        public void TheSingletonIsInstalled()
        {
            Assert.IsTrue(PathFinder.HasInstance,
                "Every path follower checks HasInstance first; without it they all fall back " +
                "to straight-line movement and this fixture would prove nothing.");
        }

        [Test]
        public void AnOpenPath_EndsExactlyAtTheGoal()
        {
            var path = new List<Vector2>();
            Assert.IsTrue(_finder.TryFindPath(new Vector2(0.5f, 0.5f), new Vector2(6.5f, 0.5f), path));

            Assert.IsNotEmpty(path);
            Assert.AreEqual(new Vector2(6.5f, 0.5f), path[path.Count - 1],
                "The real goal replaces the last tile CENTRE, or every follower stops a " +
                "fraction of a tile short.");
        }

        [Test]
        public void TheFirstWaypoint_IsNotBehindTheCaller()
        {
            // Standing near the right edge of its own tile: the tile centre is BEHIND, and
            // returning it made the follower's first move go backwards once per repath.
            var start = new Vector2(0.9f, 0.5f);
            var path = new List<Vector2>();
            _finder.TryFindPath(start, new Vector2(6.5f, 0.5f), path);

            Assert.IsNotEmpty(path);
            Assert.Greater(path[0].x, start.x - 0.4f);
        }

        [Test]
        public void AGoalInTheSameTile_ReturnsTheGoal()
        {
            var path = new List<Vector2>();
            Assert.IsTrue(_finder.TryFindPath(new Vector2(0.2f, 0.2f), new Vector2(0.7f, 0.7f), path));

            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(new Vector2(0.7f, 0.7f), path[0]);
        }

        // ── Geometry ─────────────────────────────────────────────────────────────

        [Test]
        public void APathAroundAWall_IsLongerThanTheStraightLine()
        {
            Wall(new Vector2(3.5f, 0.5f), new Vector2(1f, 6f));

            var start = new Vector2(0.5f, 0.5f);
            var goal = new Vector2(6.5f, 0.5f);
            var path = new List<Vector2>();
            Assert.IsTrue(_finder.TryFindPath(start, goal, path));

            Assert.IsNotEmpty(path, "there is a way round — the wall does not span the map");
            Assert.Greater(Length(path, start), Vector2.Distance(start, goal) + 0.5f,
                "Routing straight through a wall is the failure this whole class exists to " +
                "prevent, and it is invisible in the returned data unless the length is checked.");
        }

        // ── The frame budget ─────────────────────────────────────────────────────

        [Test]
        public void TheBudgetRefusesFurtherSearchesInTheSameFrame()
        {
            var path = new List<Vector2>();
            var start = new Vector2(0.5f, 0.5f);
            var goal = new Vector2(6.5f, 0.5f);

            // The serialized ceiling is 4 per frame. A pack of chasers repaths on independent
            // timers that drift into alignment, which is what this bounds.
            int granted = 0;
            for (int i = 0; i < 12; i++)
                if (_finder.TryFindPath(start, goal, path)) granted++;

            Assert.AreEqual(4, granted,
                "Twenty monsters each spending maxNodes expansions in one frame is the spike " +
                "the budget exists for.");
            Assert.IsFalse(PathFinder.HasInstance && _finder.HasSearchBudget);
        }

        [Test]
        public void ARefusedSearch_LeavesTheCallersPathUntouched()
        {
            var path = new List<Vector2>();
            var start = new Vector2(0.5f, 0.5f);
            var goal = new Vector2(6.5f, 0.5f);

            _finder.TryFindPath(start, goal, path);
            int before = path.Count;
            Assert.Greater(before, 0);

            while (_finder.TryFindPath(start, goal, path)) { }   // exhaust the budget

            Assert.AreEqual(before, path.Count,
                "A refusal must not clear the path: the follower keeps walking the one it " +
                "has. Clearing it would degrade a busy frame into a straight line at the " +
                "target — into whatever the path was routing around.");
        }
    }
}
