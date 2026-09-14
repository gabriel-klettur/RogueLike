using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Does a sweeping blade reach this BODY — not this body's centre.
    ///
    /// <para>Both slashes used to test one point per victim: the centre of its single body
    /// collider. A creature larger than the arc's sliver was then immune whenever that one
    /// point sat a degree outside a sweep that visibly cut through half of it, and a dragon's
    /// head could never be struck because its centre is its back. With a shaped hurtbox the
    /// test samples the body (<see cref="EntityBody.ProbePoints"/>): the capsule point nearest
    /// the blade, each capsule's centre and both ends of its spine — and the victim is struck
    /// by the first sample the edge has just passed.</para>
    ///
    /// <para>The ORDER a sample is accepted in matters as much as the samples: the angular sweep
    /// still only strikes a point once the leading edge has crossed it this frame, so a wide
    /// body is hit when the blade reaches its near side, not when it reaches its middle.</para>
    /// </summary>
    internal static class SweptBodyTest
    {
        private static readonly List<Vector2> Points = new List<Vector2>(24);

        /// <summary>
        /// The sample of <paramref name="victim"/> inside the sector whose angle the edge
        /// crossed between <paramref name="previousAngle"/> and <paramref name="headAngle"/>.
        /// <paramref name="reported"/> is the point an overlay should mark either way, and
        /// <paramref name="anyInside"/> whether any sample was inside the sector at all.
        /// </summary>
        public static bool TryAngular(GameObject victim, Vector2 origin, Vector2 forward,
                                      float radius, float arcDegrees,
                                      float previousAngle, float headAngle,
                                      out Vector2 point, out Vector2 reported, out bool anyInside)
        {
            Collect(victim, origin);
            point = reported = Points.Count > 0 ? Points[0] : origin;
            anyInside = false;

            for (int i = 0; i < Points.Count; i++)
            {
                Vector2 p = Points[i];
                if (!SlashAttack.IsInsideSector(origin, forward, p, radius, arcDegrees)) continue;
                if (!anyInside) { anyInside = true; reported = p; }

                Vector2 to = p - origin;
                float signed = to.sqrMagnitude <= 0.0001f ? 0f : Vector2.SignedAngle(forward, to.normalized);
                if (signed < previousAngle - 0.01f || signed > headAngle + 0.01f) continue;

                point = reported = p;
                Points.Clear();
                return true;
            }

            Points.Clear();
            return false;
        }

        /// <summary>
        /// The sample inside a lance's sector at or beyond <paramref name="previousReach"/> — the
        /// nearest one, since a thrust meets the near face first.
        /// </summary>
        public static bool TryRadial(GameObject victim, Vector2 origin, Vector2 forward,
                                     float reach, float arcDegrees, float previousReach,
                                     out Vector2 point, out bool anyInside)
        {
            Collect(victim, origin);
            point = Points.Count > 0 ? Points[0] : origin;
            anyInside = false;
            float best = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < Points.Count; i++)
            {
                Vector2 p = Points[i];
                if (!SlashAttack.IsInsideSector(origin, forward, p, reach, arcDegrees)) continue;
                if (!anyInside) { anyInside = true; point = p; }

                float d = Vector2.Distance(p, origin);
                if (d < previousReach - 0.01f || d >= best) continue;
                best = d;
                point = p;
                found = true;
            }

            Points.Clear();
            return found;
        }

        private static void Collect(GameObject victim, Vector2 origin)
        {
            Points.Clear();
            EntityBody.ProbePoints(victim, origin, Points);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Points.Clear();
    }
}
