using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the string-pulling pass over A*'s output.
    ///
    /// A* returns one waypoint per TILE and the follower steers straight at each one in
    /// turn, so an open diagonal run came out as a visible zig-zag between tile centres and
    /// a straight corridor cost one course correction per tile. Keeping a waypoint is only
    /// worth it when the geometry actually requires the turn.
    ///
    /// The pass is driven by <see cref="LineOfSight"/>, the same helper the aggro and melee
    /// checks use — so "can I walk straight there" means the same thing everywhere. With no
    /// colliders in an EditMode scene every line is clear, which is exactly the open-ground
    /// case worth pinning: an unobstructed path must collapse to its endpoint.
    /// </summary>
    [TestFixture]
    public class PathSmoothingTests
    {
        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        private static void Smooth(Vector2 start, List<Vector2> waypoints)
        {
            var m = typeof(PathFinder).GetMethod("SmoothPath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "PathFinder.SmoothPath must exist");
            m.Invoke(null, new object[] { start, waypoints });
        }

        [Test]
        public void OpenGround_CollapsesToTheDestination()
        {
            // The staircase A* produces across open ground: one waypoint per tile.
            var path = new List<Vector2>
            {
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(2f, 1f), new Vector2(2f, 2f),
                new Vector2(3f, 2f), new Vector2(3f, 3f),
            };
            var destination = path[path.Count - 1];

            Smooth(Vector2.zero, path);

            Assert.AreEqual(1, path.Count,
                "With nothing in the way every intermediate corner is a turn the follower " +
                "never needed to make.");
            Assert.AreEqual(destination, path[0], "the destination must survive");
        }

        [Test]
        public void TheFinalWaypoint_IsNeverDropped()
        {
            // The last element is the true goal position, not a tile centre — losing it
            // would leave a chasing monster walking to the wrong place.
            var path = new List<Vector2> { new Vector2(5f, 5f) };

            Smooth(Vector2.zero, path);

            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(new Vector2(5f, 5f), path[0]);
        }

        [Test]
        public void EmptyPath_IsLeftAlone()
        {
            var path = new List<Vector2>();

            Assert.DoesNotThrow(() => Smooth(Vector2.zero, path));
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void SmoothingIsIdempotent()
        {
            var path = new List<Vector2>
            {
                new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(3f, 0f),
            };

            Smooth(Vector2.zero, path);
            int afterFirst = path.Count;
            Smooth(Vector2.zero, path);

            Assert.AreEqual(afterFirst, path.Count,
                "A second pass must find nothing left to remove — otherwise the repath " +
                "cadence would keep shortening a path that is already minimal.");
        }
    }
}
