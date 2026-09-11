using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// World-to-map arithmetic, pure and static so every rule is provable in EditMode.
    ///
    /// <para><b>The disc is a CIRCLE and the clamp must be one too.</b> The minimap this
    /// replaced clamped out-of-range dots to the edges of its square texture while showing
    /// that texture through a circular mask, so anything clamped into a corner was drawn
    /// OUTSIDE the circle and vanished: a monster 88 units to the south-east of the player was
    /// simply not on the map. <see cref="ClampToRim"/> clamps radially.</para>
    /// </summary>
    public static class MinimapProjection
    {
        /// <summary>
        /// Map-local position (canvas units, origin at the view centre) of a world point, for
        /// a view that shows <paramref name="halfExtent"/> world units from its centre to each
        /// edge across <paramref name="halfSizeUnits"/> canvas units.
        /// </summary>
        public static Vector2 WorldToLocal(Vector2 world, Vector2 viewCentre, Vector2 halfExtent, Vector2 halfSizeUnits)
        {
            Vector2 rel = world - viewCentre;
            return new Vector2(
                rel.x / Mathf.Max(1e-4f, halfExtent.x) * halfSizeUnits.x,
                rel.y / Mathf.Max(1e-4f, halfExtent.y) * halfSizeUnits.y);
        }

        /// <summary>Inverse of <see cref="WorldToLocal"/>.</summary>
        public static Vector2 LocalToWorld(Vector2 local, Vector2 viewCentre, Vector2 halfExtent, Vector2 halfSizeUnits)
        {
            return viewCentre + new Vector2(
                local.x / Mathf.Max(1e-4f, halfSizeUnits.x) * halfExtent.x,
                local.y / Mathf.Max(1e-4f, halfSizeUnits.y) * halfExtent.y);
        }

        /// <summary>True when a local position lies inside a circle of radius <paramref name="radius"/>.</summary>
        public static bool InsideCircle(Vector2 local, float radius) => local.sqrMagnitude <= radius * radius;

        /// <summary>
        /// Clamp a local position to a circle. Returns true when it had to be clamped, and the
        /// bearing of the point (degrees, counter-clockwise from +X) for an edge chevron.
        /// </summary>
        public static bool ClampToRim(ref Vector2 local, float radius, out float bearingDeg)
        {
            bearingDeg = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            float m = local.magnitude;
            if (m <= radius) return false;
            local = m > 1e-5f ? local / m * radius : Vector2.zero;
            return true;
        }

        /// <summary>
        /// Clamp a local position to a rectangle of half size <paramref name="half"/>. Returns true
        /// when it had to be clamped, and the bearing of the ORIGINAL point.
        /// </summary>
        public static bool ClampToRect(ref Vector2 local, Vector2 half, out float bearingDeg)
        {
            bearingDeg = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y) return false;
            // Scale along the ray from the centre so the pin sits where the direction crosses the edge.
            float sx = Mathf.Abs(local.x) > 1e-5f ? half.x / Mathf.Abs(local.x) : float.MaxValue;
            float sy = Mathf.Abs(local.y) > 1e-5f ? half.y / Mathf.Abs(local.y) : float.MaxValue;
            local *= Mathf.Min(sx, sy);
            return true;
        }

        /// <summary>Canvas units per world unit for a view.</summary>
        public static float UnitsPerWorld(float halfExtent, float halfSizeUnits)
            => halfSizeUnits / Mathf.Max(1e-4f, halfExtent);

        /// <summary>
        /// Heading of a facing vector as a rotation for an icon authored pointing UP (+Y),
        /// counter-clockwise degrees.
        /// </summary>
        public static float HeadingRotation(Vector2 facing)
        {
            if (facing.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
        }

        /// <summary>Frame-rate independent exponential approach of <paramref name="current"/> to <paramref name="target"/>.</summary>
        public static float Damp(float current, float target, float halfLifeSeconds, float dt)
        {
            if (halfLifeSeconds <= 0f) return target;
            return Mathf.Lerp(target, current, Mathf.Pow(0.5f, dt / halfLifeSeconds));
        }
    }
}
