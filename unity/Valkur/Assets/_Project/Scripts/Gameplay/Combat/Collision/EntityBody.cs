using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// "Where is this body" for damage paths that test a SHAPE rather than trusting the query —
    /// a sword's arc, a breath's cone, the point an impact flash is drawn at.
    ///
    /// <para>Answers from the entity's hurtbox when it has one, and falls back to the old single
    /// body collider (or the sprite) when it does not, so an entity built by a path that never
    /// installed a rig — a test fixture, a hand-made prop with <c>Health</c> — behaves exactly as
    /// it did.</para>
    /// </summary>
    public static class EntityBody
    {
        /// <summary>The rig on an entity root, or null.</summary>
        public static EntityColliderRig RigOf(GameObject entity)
            => entity != null ? entity.GetComponent<EntityColliderRig>() : null;

        /// <summary>The point of the body nearest <paramref name="from"/>.</summary>
        public static Vector2 ClosestPoint(GameObject entity, Vector2 from)
        {
            if (entity == null) return from;
            var rig = RigOf(entity);
            if (rig != null && (rig.HurtboxCount > 0 || rig.Footprint != null)) return rig.ClosestPoint(from);

            var body = EntityColliderConfigurator.GetBodyCollider(entity);
            return body != null ? body.ClosestPoint(from) : Center(entity);
        }

        /// <summary>The middle of what can be hit: the hurtbox's bounds, else the old body, else the sprite.</summary>
        public static Vector2 Center(GameObject entity)
        {
            if (entity == null) return Vector2.zero;
            var rig = RigOf(entity);
            if (rig != null && rig.HurtboxCount > 0) return rig.HurtBounds.center;

            var body = EntityColliderConfigurator.GetBodyCollider(entity);
            if (body != null) return body.bounds.center;

            var sr = entity.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr.bounds.center;
            return entity.transform.position;
        }

        /// <summary>
        /// Sample points of the body for a shaped test, nearest-first is NOT guaranteed. Always
        /// appends at least one point.
        /// </summary>
        public static void ProbePoints(GameObject entity, Vector2 from, List<Vector2> points)
        {
            if (entity == null) return;
            int before = points.Count;
            var rig = RigOf(entity);
            if (rig != null) rig.CollectProbePoints(from, points);

            if (points.Count > before) return;

            var body = EntityColliderConfigurator.GetBodyCollider(entity);
            if (body != null)
            {
                points.Add(body.bounds.center);
                points.Add(body.ClosestPoint(from));
            }
            else
            {
                points.Add(Center(entity));
            }
        }
    }
}
