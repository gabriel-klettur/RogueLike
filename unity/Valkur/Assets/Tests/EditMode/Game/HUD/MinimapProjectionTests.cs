using NUnit.Framework;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The world-to-map arithmetic. The case that matters most is the corner one: the minimap
    /// this replaced clamped to the edges of its SQUARE texture while showing it through a
    /// circular mask, so a monster to the south-east was clamped into a corner outside the
    /// circle and vanished.
    /// </summary>
    public class MinimapProjectionTests
    {
        [Test]
        public void WorldToLocal_And_Back_RoundTrip()
        {
            var centre = new Vector2(175f, 80f);
            var half = new Vector2(24f, 24f);
            var size = new Vector2(88f, 88f);
            var world = new Vector2(190f, 71f);
            var local = MinimapProjection.WorldToLocal(world, centre, half, size);
            var back = MinimapProjection.LocalToWorld(local, centre, half, size);
            Assert.That(Vector2.Distance(world, back), Is.LessThan(1e-3f));
            Assert.That(local.x, Is.EqualTo(15f / 24f * 88f).Within(1e-3f));
        }

        [Test]
        public void ClampToRim_KeepsACornerPoint_OnTheCircle_NotInTheSquareCorner()
        {
            var p = new Vector2(100f, -100f);
            bool clamped = MinimapProjection.ClampToRim(ref p, 80f, out float bearing);
            Assert.IsTrue(clamped);
            Assert.That(p.magnitude, Is.EqualTo(80f).Within(1e-3f), "clamped onto the rim, not into a square corner");
            Assert.That(bearing, Is.EqualTo(-45f).Within(1e-3f));
            Assert.That(p.x, Is.EqualTo(-p.y).Within(1e-3f), "direction preserved");
        }

        [Test]
        public void ClampToRim_LeavesAnInsidePointAlone()
        {
            var p = new Vector2(10f, 20f);
            Assert.IsFalse(MinimapProjection.ClampToRim(ref p, 80f, out _));
            Assert.AreEqual(new Vector2(10f, 20f), p);
        }

        [Test]
        public void ClampToRect_SitsWhereTheDirectionCrossesTheEdge()
        {
            var p = new Vector2(400f, 100f);
            Assert.IsTrue(MinimapProjection.ClampToRect(ref p, new Vector2(200f, 150f), out _));
            Assert.That(p.x, Is.EqualTo(200f).Within(1e-3f));
            Assert.That(p.y, Is.EqualTo(50f).Within(1e-3f), "scaled along the ray, not clamped per axis");
        }

        [Test]
        public void HeadingRotation_PointsAnUpAuthoredIconAlongTheFacing()
        {
            Assert.That(MinimapProjection.HeadingRotation(Vector2.up), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(MinimapProjection.HeadingRotation(Vector2.right), Is.EqualTo(-90f).Within(1e-3f));
            Assert.That(MinimapProjection.HeadingRotation(Vector2.left), Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Damp_ApproachesTheTarget_FrameRateIndependently()
        {
            float a = 10f, b = 10f;
            for (int i = 0; i < 10; i++) a = MinimapProjection.Damp(a, 30f, 0.07f, 0.01f);
            b = MinimapProjection.Damp(b, 30f, 0.07f, 0.1f);
            Assert.That(a, Is.EqualTo(b).Within(1e-3f), "ten small steps equal one large one");
            Assert.That(a, Is.GreaterThan(10f).And.LessThan(30f));
        }
    }
}
