using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The live <see cref="IDestructibleObstacle"/> instances, so a melee swing can reach
    /// them without widening its <c>LayerMask</c> onto Building.
    ///
    /// <para>A registry rather than a physics query because the list is normally EMPTY and
    /// almost never longer than one — <c>wall_ice</c> ships <c>maxInstances: 1</c>. Every
    /// swing in the game pays a <c>Count == 0</c> check for this; an extra
    /// <c>OverlapCircleAll</c> against Building would instead walk every painted collision
    /// cell and every building box in range, on every attack of every entity.</para>
    /// </summary>
    public static class DestructibleObstacleRegistry
    {
        private static List<IDestructibleObstacle> _live = new List<IDestructibleObstacle>(4);

        /// <summary>How many obstacles are currently destructible. Zero is the normal case.</summary>
        public static int Count => _live.Count;

        /// <summary>
        /// Domain Reload is OFF, so a list left holding destroyed obstacles would carry into
        /// the next Play session and be walked by the first swing. Assigning a FRESH list is
        /// a plain <c>stsfld</c>, which is the only shape <c>DomainReloadStaticResetTests</c>
        /// recognises as a reset — <c>_live.Clear()</c> passes the field as an argument and
        /// reads to that scanner as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _live = new List<IDestructibleObstacle>(4);

        public static void Register(IDestructibleObstacle obstacle)
        {
            if (obstacle == null || _live.Contains(obstacle)) return;
            _live.Add(obstacle);
        }

        public static void Unregister(IDestructibleObstacle obstacle)
        {
            if (obstacle == null) return;
            _live.Remove(obstacle);
        }

        /// <summary>
        /// Damage every obstacle whose surface falls inside the swing's circle and arc.
        /// Returns how many were struck, so a caller can fold them into its own hit count.
        ///
        /// <para>The range test is against the obstacle's BOUNDS, not its centre: a barrier
        /// is several units long, and a swing that clips its end has hit it.</para>
        /// </summary>
        public static int DamageInArc(Vector2 origin, float radius, Vector2 direction,
            float arcDegrees, int damage, GameObject attacker, SpellElement? element)
        {
            if (_live.Count == 0 || damage <= 0) return 0;

            int struck = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var obstacle = _live[i];
                if (obstacle == null) { _live.RemoveAt(i); continue; }
                if (!obstacle.AcceptsDamage) continue;

                Vector2 contact = obstacle.ObstacleBounds.ClosestPoint(origin);
                Vector2 toContact = contact - origin;
                if (toContact.sqrMagnitude > radius * radius) continue;

                // A swing centred on the attacker can be standing INSIDE the bounds, where
                // ClosestPoint returns the query point itself and the direction is undefined.
                // That is a hit by any reading, so the arc test is skipped rather than failed.
                if (toContact.sqrMagnitude > 0.0001f && arcDegrees < 360f)
                {
                    float angle = Vector2.Angle(direction.normalized, toContact.normalized);
                    if (angle > arcDegrees * 0.5f) continue;
                }

                obstacle.ApplyObstacleDamage(damage, attacker, contact, element);
                struck++;
            }
            return struck;
        }
    }
}
