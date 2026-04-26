using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    public class PatrolWaypointTests
    {
        [Test]
        public void Generate_NullType_ReturnsDefaultLine()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, null);
            Assert.IsNotNull(wp);
            Assert.AreEqual(2, wp.Length);
            Assert.AreEqual(Vector2.zero, wp[0]);
            Assert.AreEqual(new Vector2(5f, 0f), wp[1]);
        }

        [Test]
        public void Generate_EmptyString_ReturnsDefaultLine()
        {
            var wp = PatrolWaypointGenerator.Generate(new Vector2(10, 10), "");
            Assert.IsNotNull(wp);
            Assert.AreEqual(2, wp.Length);
        }

        [Test]
        public void Generate_Line_ReturnsTwoPoints()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "line");
            Assert.AreEqual(2, wp.Length);
            Assert.AreEqual(Vector2.zero, wp[0]);
            Assert.AreEqual(new Vector2(5f, 0f), wp[1]);
        }

        [Test]
        public void Generate_PingPong_ReturnsTwoPoints()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "ping_pong");
            Assert.AreEqual(2, wp.Length);
        }

        [Test]
        public void Generate_Circle_Returns16Points()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "circle");
            Assert.AreEqual(16, wp.Length);
            // First and last should be near each other (closed loop)
            float dist = Vector2.Distance(wp[0], wp[wp.Length - 1]);
            Assert.Less(dist, 3f); // within circumference
        }

        [Test]
        public void Generate_Square_ReturnsMultiplePoints()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "square");
            Assert.IsNotNull(wp);
            Assert.Greater(wp.Length, 4);
        }

        [Test]
        public void Generate_Zigzag_Returns7Points()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "zigzag");
            Assert.AreEqual(7, wp.Length); // 6 segments + 1
        }

        [Test]
        public void Generate_FigureEight_Returns24Points()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "figure_eight");
            Assert.AreEqual(24, wp.Length); // 12 per loop * 2
        }

        [Test]
        public void Generate_UnknownType_ReturnsDefaultLine()
        {
            var wp = PatrolWaypointGenerator.Generate(Vector2.zero, "spiral_nonsense");
            Assert.AreEqual(2, wp.Length);
        }

        [Test]
        public void Generate_PreservesOriginOffset()
        {
            var origin = new Vector2(100, 200);
            var wp = PatrolWaypointGenerator.Generate(origin, "line");
            Assert.AreEqual(origin, wp[0]);
            Assert.AreEqual(origin + new Vector2(5f, 0f), wp[1]);
        }

        [Test]
        public void Generate_Circle_PointsAreAtCorrectRadius()
        {
            var origin = new Vector2(5, 5);
            var wp = PatrolWaypointGenerator.Generate(origin, "circle");
            // All points should be ~4 units from origin (radius_tiles=4)
            foreach (var p in wp)
            {
                float dist = Vector2.Distance(p, origin);
                Assert.AreEqual(4f, dist, 0.1f);
            }
        }
    }
}
