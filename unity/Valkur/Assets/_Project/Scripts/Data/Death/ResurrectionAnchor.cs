using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// WHERE on a building the player has to stand for it to revive them.
    ///
    /// <para>Four answers because a building is not a point and the right spot depends on what the
    /// art is. A wayside shrine is touched from IN FRONT; an arch is walked THROUGH, so its spot is
    /// the far side of its own footprint; a flat sigil painted on the ground is stood ON. One
    /// hard-coded rule would be right for one of those and wrong for the others.</para>
    /// </summary>
    public enum ResurrectionAnchor
    {
        /// <summary>
        /// Anywhere within <c>resurrectionRadius</c> of the building's bounds, on any side. The
        /// forgiving option and the default — and the only one that is reliably reachable on a
        /// SOLID building, whose own collision grid keeps the player out of the other three.
        /// </summary>
        Proximity = 0,

        /// <summary>The bottom band, plus slack in FRONT of it. Standing at the foot of the thing.</summary>
        Base = 1,

        /// <summary>The middle band. Inside the footprint — needs a walkable gap in the collision grid.</summary>
        Center = 2,

        /// <summary>The top band, plus slack BEHIND it. The far side of an arch you walked under.</summary>
        Top = 3,
    }

    /// <summary>
    /// The one answer to "is this world point inside that altar's zone".
    ///
    /// <para><b>Pure and static on purpose.</b> It takes a <see cref="Rect"/> and returns a bool, so
    /// every one of the four shapes is testable in EditMode with no scene, no building and no
    /// renderer — which matters because the failure mode of an altar zone is silent by nature: a
    /// zone in the wrong place looks exactly like a zone the player has not reached yet. The whole
    /// death subsystem was rebuilt out of one such silence.</para>
    ///
    /// <para><b>The rect is the building's FULL bounds</b>, as <c>BuildingObject.TryGetWorldRect</c>
    /// reports it: <c>yMin</c> is the ground line the sprite stands on and <c>yMax</c> is the top of
    /// the canopy. So "Top" is the back of the building in a top-down projection, not somewhere in
    /// the sky — the canopy is drawn OVER the player, and walking behind a building is walking up.</para>
    /// </summary>
    public static class ResurrectionZoneGeometry
    {
        /// <summary>
        /// How much of the building's height each band claims. A third: two bands never touch, so
        /// Base, Center and Top are three distinguishable places on the same building rather than
        /// three names for the middle of it.
        /// </summary>
        public const float BandFraction = 0.34f;

        /// <summary>
        /// Floor on a band's height, in world units. Roughly half a tile.
        ///
        /// <para>Without it a one-tile prop gives each band a third of a tile, and the player has to
        /// stand on a strip a few pixels tall — which reads as an altar that does not work rather
        /// than as an altar with a precise spot. The floor can make the bands overlap on a very
        /// short building, and that is the correct trade: overlapping zones on a small prop mean
        /// the player is revived, which is the whole point of the subsystem.</para>
        /// </summary>
        public const float MinBandHeight = 0.5f;

        /// <summary>
        /// True when <paramref name="point"/> is inside the zone.
        ///
        /// <para><paramref name="radius"/> is used only by <see cref="ResurrectionAnchor.Proximity"/>;
        /// <paramref name="pad"/> (the tuning's <c>altarActivationPadding</c>) is the slack every
        /// shape gets sideways, plus the OUTWARD slack Base and Top get on their open edge. Center
        /// gets no outward slack because it has no outward side — padding it would grow the band
        /// until it swallowed the other two.</para>
        /// </summary>
        public static bool Contains(Rect rect, Vector2 point, ResurrectionAnchor anchor,
                                    float radius, float pad)
        {
            return ZoneOf(rect, anchor, radius, pad).Contains(point);
        }

        /// <summary>
        /// The zone as a rect, so the editor can draw exactly what the runtime tests. Two
        /// implementations of this shape would be a marker that lies about where the altar is.
        /// </summary>
        public static Rect ZoneOf(Rect rect, ResurrectionAnchor anchor, float radius, float pad)
        {
            pad = Mathf.Max(0f, pad);
            radius = Mathf.Max(0f, radius);

            if (anchor == ResurrectionAnchor.Proximity)
            {
                // The larger of the two, not their sum: an author who raises the padding for the
                // banded shapes must not silently widen every proximity altar in the world with it.
                float reach = Mathf.Max(radius, pad);
                return new Rect(rect.xMin - reach, rect.yMin - reach,
                                rect.width + reach * 2f, rect.height + reach * 2f);
            }

            float band = Mathf.Max(rect.height * BandFraction, MinBandHeight);
            float x = rect.xMin - pad;
            float w = rect.width + pad * 2f;

            switch (anchor)
            {
                case ResurrectionAnchor.Base:
                    return new Rect(x, rect.yMin - pad, w, band + pad);

                case ResurrectionAnchor.Top:
                    return new Rect(x, rect.yMax - band, w, band + pad);

                case ResurrectionAnchor.Center:
                default:
                    return new Rect(x, rect.center.y - band * 0.5f, w, band);
            }
        }

        /// <summary>A short human sentence for a status line. The editor and the console share it.</summary>
        public static string Describe(ResurrectionAnchor anchor)
        {
            switch (anchor)
            {
                case ResurrectionAnchor.Base:   return "al pie del edificio";
                case ResurrectionAnchor.Center: return "en el centro del edificio";
                case ResurrectionAnchor.Top:    return "en la parte de arriba del edificio";
                default:                        return "cerca del edificio, por cualquier lado";
            }
        }
    }
}
