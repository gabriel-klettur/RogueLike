using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The live <see cref="IDestructibleObstacle"/> instances, so a melee swing can reach
    /// them without widening its <c>LayerMask</c> onto Building.
    ///
    /// <para>A registry rather than a physics query because it holds only the things that
    /// can actually be broken, where an <c>OverlapCircleAll</c> against Building would walk
    /// every painted collision cell and every building box in range, on every attack of
    /// every entity.</para>
    ///
    /// <para>SCALE. This was written when the only implementer was <c>wall_ice</c>
    /// (<c>maxInstances: 1</c>), and a flat scan of one item is free. A world where
    /// buildings carry durability registers a whole forest instead, so above
    /// <see cref="HASH_THRESHOLD"/> the search goes through a <see cref="SpatialHash{T}"/>.
    /// Below it the flat scan is kept verbatim: building a hash to search four items costs
    /// more than searching four items, and the ice wall must not start paying for the
    /// forest. Obstacles never move, so the hash is built once and rebuilt only when
    /// membership changes — a building being destroyed, not a per-frame event.</para>
    /// </summary>
    public static class DestructibleObstacleRegistry
    {
        /// <summary>
        /// Membership count at which the spatial hash starts paying for itself. Chosen so
        /// the single-wall case and any handful of live spell effects keep the original flat
        /// scan; a populated forest is orders of magnitude above it either way.
        /// </summary>
        private const int HASH_THRESHOLD = 24;

        private static List<IDestructibleObstacle> _live = new List<IDestructibleObstacle>(4);

        // The accelerator and its scratch buffers, hoisted rather than allocated per query:
        // DamageInArc runs on every connecting swing of every entity.
        private static SpatialHash<IDestructibleObstacle> _hash =
            new SpatialHash<IDestructibleObstacle>(4f);
        private static List<(IDestructibleObstacle item, Vector2 pos)> _queryBuffer =
            new List<(IDestructibleObstacle, Vector2)>(32);
        private static bool _hashDirty = true;

        /// <summary>
        /// Half-diagonal of the largest registered obstacle. The hash indexes each obstacle
        /// by a POINT while the range test is against its BOUNDS, so a query has to be
        /// widened by this much or a swing clipping the corner of a large building would
        /// never retrieve it as a candidate. A house is several units across; without the
        /// widening it would be unhittable from exactly the places you can reach it.
        /// </summary>
        private static float _maxObstacleExtent;

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
        private static void ResetStatics()
        {
            _live = new List<IDestructibleObstacle>(4);
            _hash = new SpatialHash<IDestructibleObstacle>(4f);
            _queryBuffer = new List<(IDestructibleObstacle, Vector2)>(32);
            _hashDirty = true;
            _maxObstacleExtent = 0f;
        }

        public static void Register(IDestructibleObstacle obstacle)
        {
            if (obstacle == null || _live.Contains(obstacle)) return;
            _live.Add(obstacle);
            _hashDirty = true;
        }

        public static void Unregister(IDestructibleObstacle obstacle)
        {
            if (obstacle == null) return;
            if (_live.Remove(obstacle)) _hashDirty = true;
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

            return _live.Count < HASH_THRESHOLD
                ? DamageInArcLinear(origin, radius, direction, arcDegrees, damage, attacker, element)
                : DamageInArcHashed(origin, radius, direction, arcDegrees, damage, attacker, element);
        }

        private static int DamageInArcLinear(Vector2 origin, float radius, Vector2 direction,
            float arcDegrees, int damage, GameObject attacker, SpellElement? element)
        {
            int struck = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var obstacle = _live[i];
                if (obstacle == null) { _live.RemoveAt(i); _hashDirty = true; continue; }
                if (TryStrike(obstacle, origin, radius, direction, arcDegrees, damage, attacker, element))
                    struck++;
            }
            return struck;
        }

        private static int DamageInArcHashed(Vector2 origin, float radius, Vector2 direction,
            float arcDegrees, int damage, GameObject attacker, SpellElement? element)
        {
            RebuildHashIfDirty();
            _hash.QueryRadius(origin, radius + _maxObstacleExtent, _queryBuffer);

            int struck = 0;
            for (int i = 0; i < _queryBuffer.Count; i++)
            {
                var obstacle = _queryBuffer[i].item;
                if (obstacle == null) { _hashDirty = true; continue; }
                if (TryStrike(obstacle, origin, radius, direction, arcDegrees, damage, attacker, element))
                    struck++;
            }
            return struck;
        }

        /// <summary>
        /// The range and arc test, shared by both traversals so the two can never disagree
        /// about what counts as a hit. Returns whether the blow landed.
        /// </summary>
        private static bool TryStrike(IDestructibleObstacle obstacle, Vector2 origin, float radius,
            Vector2 direction, float arcDegrees, int damage, GameObject attacker, SpellElement? element)
        {
            if (!obstacle.AcceptsDamage) return false;

            Vector2 contact = obstacle.ObstacleBounds.ClosestPoint(origin);
            Vector2 toContact = contact - origin;
            if (toContact.sqrMagnitude > radius * radius) return false;

            // A swing centred on the attacker can be standing INSIDE the bounds, where
            // ClosestPoint returns the query point itself and the direction is undefined.
            // That is a hit by any reading, so the arc test is skipped rather than failed.
            if (toContact.sqrMagnitude > 0.0001f && arcDegrees < 360f)
            {
                float angle = Vector2.Angle(direction.normalized, toContact.normalized);
                if (angle > arcDegrees * 0.5f) return false;
            }

            obstacle.ApplyObstacleDamage(damage, attacker, contact, element);
            return true;
        }

        private static void RebuildHashIfDirty()
        {
            if (!_hashDirty) return;

            _hash.Clear();
            _maxObstacleExtent = 0f;

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var obstacle = _live[i];
                if (obstacle == null) { _live.RemoveAt(i); continue; }

                _hash.Insert(obstacle, obstacle.ObstaclePosition);

                Vector3 extents = obstacle.ObstacleBounds.extents;
                float half = new Vector2(extents.x, extents.y).magnitude;
                if (half > _maxObstacleExtent) _maxObstacleExtent = half;
            }

            _hashDirty = false;
        }
    }
}
