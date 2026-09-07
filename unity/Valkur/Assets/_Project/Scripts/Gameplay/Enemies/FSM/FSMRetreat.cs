using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// "Get away from that, without running into a wall."
    ///
    /// <para><see cref="FleeState"/> used to normalise the vector away from its threat and
    /// drive straight down it, with no pathfinding and no geometry test at all — so a
    /// panicking monster ran into the nearest building and stood there vibrating against it
    /// for the whole flee window, which is the single most visible AI failure a player can
    /// be shown. The same job is now also wanted by <see cref="ChaseState"/>, which has to
    /// back OUT of a standoff band when the player closes on a caster.</para>
    ///
    /// <para>This is deliberately steering, not pathfinding. A retreat is a decision about
    /// the next second, re-taken continuously; A* to a destination chosen while panicking
    /// commits the body to a route that was solved for a threat position two seconds stale,
    /// and it spends a search from the frame budget on it. Probing a fan of candidate
    /// headings with the same <see cref="LineOfSight"/> everything else uses costs a handful
    /// of raycasts and cannot commit to a corner.</para>
    /// </summary>
    public static class FSMRetreat
    {
        /// <summary>
        /// Headings tried, fanned symmetrically either side of straight-away. Nine covers
        /// the whole forward half at 22.5-degree steps: enough to find the gap beside a
        /// building, coarse enough that the choice is stable frame to frame.
        /// </summary>
        [SelfHealingStatic("Immutable literal table of angles. Nothing writes to it after " +
                           "init and it holds no Unity object, so it cannot carry a stale " +
                           "reference into the next Play session.")]
        private static readonly float[] FanDegrees = { 0f, 22.5f, -22.5f, 45f, -45f, 67.5f, -67.5f, 90f, -90f };

        /// <summary>
        /// The best direction to move away from <paramref name="threat"/>, or
        /// <see cref="Vector2.zero"/> when every candidate is blocked — which is the caller's
        /// signal that it is CORNERED and should do something else (a cornered caster turns
        /// and swings rather than pressing itself into the wall).
        ///
        /// The fan is walked in order, so the straightest escape that is actually open wins
        /// and the result does not flicker between two equally good headings.
        /// </summary>
        public static Vector2 Heading(Vector2 from, Vector2 threat, float probeDistance)
        {
            Vector2 away = from - threat;
            if (away.sqrMagnitude < 0.0001f) away = Vector2.right;
            away.Normalize();

            if (probeDistance <= 0f) probeDistance = 1f;

            for (int i = 0; i < FanDegrees.Length; i++)
            {
                Vector2 candidate = Rotate(away, FanDegrees[i]);
                if (LineOfSight.IsClear(from, from + candidate * probeDistance))
                    return candidate;
            }

            return Vector2.zero;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
