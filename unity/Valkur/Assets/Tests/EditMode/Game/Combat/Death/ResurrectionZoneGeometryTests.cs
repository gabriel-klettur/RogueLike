using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// The four places a building can revive you from.
    ///
    /// <para><b>Why this is worth pinning hard.</b> A resurrection zone in the wrong place is
    /// indistinguishable, from inside the game, from a zone the player simply has not reached — the
    /// exact silence the whole death subsystem was rebuilt out of. The geometry is therefore pure
    /// and static so it can be PROVEN rather than observed, and these tests assert relationships
    /// (this band is above that one, this one reaches in front) rather than coordinates, so
    /// retuning <c>BandFraction</c> does not turn them red for no reason.</para>
    ///
    /// <para>The rect convention throughout: <c>yMin</c> is the ground line the sprite stands on
    /// and <c>yMax</c> the top of the canopy, which is what <c>BuildingObject.TryGetWorldRect</c>
    /// reports.</para>
    /// </summary>
    public class ResurrectionZoneGeometryTests
    {
        // A four-wide, six-tall building standing on y = 0, centred on x = 0.
        private static Rect Building() => new Rect(-2f, 0f, 4f, 6f);

        private const float Radius = 1.5f;
        private const float Pad = 0.75f;

        private static Rect Zone(ResurrectionAnchor anchor) =>
            ResurrectionZoneGeometry.ZoneOf(Building(), anchor, Radius, Pad);

        private static bool In(ResurrectionAnchor anchor, float x, float y) =>
            ResurrectionZoneGeometry.Contains(Building(), new Vector2(x, y), anchor, Radius, Pad);

        // ── Proximity ───────────────────────────────────────────────────────

        /// <summary>
        /// Proximity reaches out on ALL FOUR sides. It is the forgiving option and the default, and
        /// the only one that works on a solid building the player cannot walk into.
        /// </summary>
        [Test]
        public void Proximity_ReachesOutOnEverySide()
        {
            var b = Building();

            Assert.That(In(ResurrectionAnchor.Proximity, 0f, b.yMin - Radius * 0.5f), Is.True, "in front");
            Assert.That(In(ResurrectionAnchor.Proximity, 0f, b.yMax + Radius * 0.5f), Is.True, "behind");
            Assert.That(In(ResurrectionAnchor.Proximity, b.xMin - Radius * 0.5f, 3f), Is.True, "left");
            Assert.That(In(ResurrectionAnchor.Proximity, b.xMax + Radius * 0.5f, 3f), Is.True, "right");
        }

        [Test]
        public void Proximity_StopsAtItsRadius()
        {
            var b = Building();
            Assert.That(In(ResurrectionAnchor.Proximity, 0f, b.yMax + Radius * 2f), Is.False);
            Assert.That(In(ResurrectionAnchor.Proximity, b.xMax + Radius * 2f, 3f), Is.False);
        }

        /// <summary>
        /// The reach is the LARGER of the radius and the padding, never their sum.
        ///
        /// <para>Padding is the sideways slack every shape gets; the radius is Proximity's own
        /// reach. Adding them would mean an author who nudged the padding to make the BANDS easier
        /// to hit had silently widened every proximity altar in the world with it.</para>
        /// </summary>
        [Test]
        public void Proximity_TakesTheLargerOfRadiusAndPad_NotTheirSum()
        {
            var b = Building();
            var zone = ResurrectionZoneGeometry.ZoneOf(b, ResurrectionAnchor.Proximity, 1.5f, 0.75f);

            Assert.That(zone.xMin, Is.EqualTo(b.xMin - 1.5f).Within(0.001f));
            Assert.That(zone.width, Is.EqualTo(b.width + 3f).Within(0.001f));
        }

        [Test]
        public void Proximity_UsesThePad_WhenItIsTheLargerOfTheTwo()
        {
            var b = Building();
            var zone = ResurrectionZoneGeometry.ZoneOf(b, ResurrectionAnchor.Proximity, 0.25f, 2f);

            Assert.That(zone.yMin, Is.EqualTo(b.yMin - 2f).Within(0.001f));
        }

        // ── The three bands ─────────────────────────────────────────────────

        /// <summary>
        /// Base, Center and Top are three DISTINGUISHABLE places, in that vertical order.
        ///
        /// <para>Asserted as an ordering rather than as coordinates: that is the property an author
        /// picking between four buttons is relying on, and it survives any retune of the band
        /// fraction.</para>
        /// </summary>
        [Test]
        public void TheThreeBands_StackInOrder_BaseThenCenterThenTop()
        {
            float baseY   = Zone(ResurrectionAnchor.Base).center.y;
            float centerY = Zone(ResurrectionAnchor.Center).center.y;
            float topY    = Zone(ResurrectionAnchor.Top).center.y;

            Assert.That(baseY, Is.LessThan(centerY), "the base sits below the middle");
            Assert.That(centerY, Is.LessThan(topY), "the middle sits below the top");
        }

        /// <summary>
        /// Base reaches IN FRONT of the building — the ground the player is standing on when they
        /// walk up to a shrine — and does not reach the top.
        /// </summary>
        [Test]
        public void Base_ReachesInFrontOfTheGroundLine_AndNotTheRoof()
        {
            var b = Building();

            Assert.That(In(ResurrectionAnchor.Base, 0f, b.yMin - Pad * 0.5f), Is.True,
                "standing just in front of the base counts");
            Assert.That(In(ResurrectionAnchor.Base, 0f, b.yMax - 0.1f), Is.False,
                "the far side of the building is not its base");
        }

        /// <summary>
        /// Top reaches BEHIND the building. In a top-down projection the canopy is drawn over the
        /// player, so "the top" is the far side of an arch you have walked under — not the sky.
        /// </summary>
        [Test]
        public void Top_ReachesBehindTheBuilding_AndNotTheGroundLine()
        {
            var b = Building();

            Assert.That(In(ResurrectionAnchor.Top, 0f, b.yMax + Pad * 0.5f), Is.True,
                "standing just behind the building counts");
            Assert.That(In(ResurrectionAnchor.Top, 0f, b.yMin + 0.1f), Is.False,
                "the ground line is not the top");
        }

        [Test]
        public void Center_IsTheMiddle_AndReachesNeitherEnd()
        {
            var b = Building();

            Assert.That(In(ResurrectionAnchor.Center, 0f, b.center.y), Is.True);
            Assert.That(In(ResurrectionAnchor.Center, 0f, b.yMin - Pad * 0.5f), Is.False);
            Assert.That(In(ResurrectionAnchor.Center, 0f, b.yMax + Pad * 0.5f), Is.False);
        }

        /// <summary>
        /// The bands get SIDEWAYS slack too. Without it the player has to be inside the building's
        /// exact width, which on a narrow arch is about a tile.
        /// </summary>
        [Test]
        public void EveryBand_GetsSidewaysSlack()
        {
            var b = Building();

            Assert.That(In(ResurrectionAnchor.Base,   b.xMax + Pad * 0.5f, b.yMin + 0.1f), Is.True);
            Assert.That(In(ResurrectionAnchor.Center, b.xMin - Pad * 0.5f, b.center.y),     Is.True);
            Assert.That(In(ResurrectionAnchor.Top,    b.xMax + Pad * 0.5f, b.yMax - 0.1f),  Is.True);
        }

        /// <summary>
        /// A band never collapses to a sliver on a small prop.
        ///
        /// <para>A one-unit-tall prop would otherwise give each band a third of a unit, and a
        /// player would have to stand on a strip a few pixels tall — which reads as an altar that
        /// does not work rather than as one with a precise spot. Overlapping bands on a tiny prop
        /// are the correct trade: the player gets revived, which is the point.</para>
        /// </summary>
        [Test]
        public void ABand_NeverCollapses_OnATinyProp()
        {
            var tiny = new Rect(-0.5f, 0f, 1f, 1f);

            foreach (var anchor in new[] { ResurrectionAnchor.Base, ResurrectionAnchor.Center, ResurrectionAnchor.Top })
            {
                var zone = ResurrectionZoneGeometry.ZoneOf(tiny, anchor, Radius, 0f);
                Assert.That(zone.height, Is.GreaterThanOrEqualTo(ResurrectionZoneGeometry.MinBandHeight - 0.001f),
                    anchor + " collapsed to " + zone.height);
            }
        }

        /// <summary>
        /// Zero padding and zero radius must still produce a usable zone rather than an empty rect —
        /// an author can type 0 into either field, and a zone of no area is an altar that can never
        /// fire with nothing on screen saying why.
        /// </summary>
        [Test]
        public void ZeroPadAndZeroRadius_StillLeaveTheBuildingItself()
        {
            var b = Building();

            foreach (ResurrectionAnchor anchor in System.Enum.GetValues(typeof(ResurrectionAnchor)))
            {
                var zone = ResurrectionZoneGeometry.ZoneOf(b, anchor, 0f, 0f);
                Assert.That(zone.width, Is.GreaterThan(0f), anchor + " has no width");
                Assert.That(zone.height, Is.GreaterThan(0f), anchor + " has no height");
            }
        }

        /// <summary>
        /// Every anchor has its own sentence. The console and the editor both print it, and an
        /// unnamed option is one an author has to guess at from a four-letter button.
        /// </summary>
        [Test]
        public void EveryAnchor_DescribesItself()
        {
            foreach (ResurrectionAnchor anchor in System.Enum.GetValues(typeof(ResurrectionAnchor)))
                Assert.That(ResurrectionZoneGeometry.Describe(anchor), Is.Not.Null.And.Not.Empty,
                    anchor + " has no description");
        }
    }
}
