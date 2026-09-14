using UnityEngine;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// The physics queries a spell uses to find what it hit, with the drawing of that query
    /// folded INTO the call.
    ///
    /// <para><b>Why a wrapper rather than a second pass that re-reads the asset.</b> The
    /// overlay's whole job is to show where a spell really reaches, and the defects it is meant
    /// to expose are exactly the ones where the authored number and the swept geometry
    /// disagree - a radius divided by 16, a ring drawn at a constant scale, a centre pushed
    /// forward by a hard-coded offset. An overlay that re-derived the shape from
    /// <c>SpellDefinition</c> would agree with the asset and disagree with the game, which is
    /// the bug wearing the instrument's clothes. Here the shape handed to the recorder is the
    /// same set of arguments handed to <c>Physics2D</c>, one expression apart, so they cannot
    /// drift.</para>
    ///
    /// <para>Free when the overlay is off: <see cref="SpellDebugAreas"/> returns on its first
    /// line, so what remains is the <c>Physics2D</c> call the site was already making.</para>
    ///
    /// <para><b>Every query result is folded to one collider per entity</b>
    /// (<see cref="Combat.EntityHitFilter"/>) before it is returned. An entity's hurtbox is
    /// several capsules, and the executors that call this walk the result and damage each
    /// <c>Health</c> they find; folding here is what keeps a splash from hitting a dragon once
    /// per capsule without every executor having to learn about capsules.</para>
    /// </summary>
    public static class SpellProbe
    {
        public static Collider2D[] OverlapCircleAll(Vector2 centre, float radius, LayerMask mask,
                                                    SpellDebugRole role, string label = null)
        {
            SpellDebugAreas.Circle(centre, radius, role, label);
            var hits = Physics2D.OverlapCircleAll(centre, radius, mask);
            RecordContacts(hits, hits.Length);
            return Combat.EntityHitFilter.Collapse(hits, centre);
        }

        public static int OverlapCircleNonAlloc(Vector2 centre, float radius, Collider2D[] results,
                                                LayerMask mask, SpellDebugRole role, string label = null)
        {
            SpellDebugAreas.Circle(centre, radius, role, label);
            int count = Physics2D.OverlapCircleNonAlloc(centre, radius, results, mask);
            RecordContacts(results, count);
            return Combat.EntityHitFilter.Collapse(results, count, centre);
        }

        /// <summary>
        /// Every collider a spell query returned, handed to the entity-collision overlay BEFORE
        /// the executor narrows it. That ordering is the point: a target the executor then
        /// rejects is exactly the one an author cannot otherwise see. Free when that overlay is off.
        /// </summary>
        private static void RecordContacts(Collider2D[] colliders, int count)
        {
            if (!EntityCollisionDebug.Enabled || colliders == null) return;
            for (int i = 0; i < count && i < colliders.Length; i++)
                EntityCollisionDebug.Touched(colliders[i]);
        }

        private static void RecordContacts(RaycastHit2D[] hits)
        {
            if (!EntityCollisionDebug.Enabled || hits == null) return;
            for (int i = 0; i < hits.Length; i++)
                EntityCollisionDebug.Touched(hits[i].collider);
        }

        /// <summary>
        /// A capsule sweep, recorded as the SEGMENT it really is. Unity takes a centre, a size
        /// and an angle; an author looking at the screen needs the two ends and the width, and
        /// converting here rather than at the call site keeps the conversion in one place.
        /// </summary>
        public static Collider2D[] OverlapCapsuleAll(Vector2 centre, Vector2 size,
                                                     CapsuleDirection2D capsuleDirection, float angle,
                                                     LayerMask mask, SpellDebugRole role,
                                                     string label = null)
        {
            if (SpellDebugAreas.Enabled)
            {
                Vector2 axis = Quaternion.Euler(0f, 0f, angle) *
                               (capsuleDirection == CapsuleDirection2D.Horizontal
                                   ? Vector2.right : Vector2.up);
                float length = capsuleDirection == CapsuleDirection2D.Horizontal ? size.x : size.y;
                float half = capsuleDirection == CapsuleDirection2D.Horizontal ? size.y : size.x;
                SpellDebugAreas.Segment(centre - axis * (length * 0.5f),
                                        centre + axis * (length * 0.5f),
                                        half * 0.5f, role, label);
            }
            var hits = Physics2D.OverlapCapsuleAll(centre, size, capsuleDirection, angle, mask);
            RecordContacts(hits, hits.Length);
            return Combat.EntityHitFilter.Collapse(hits, centre);
        }

        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction,
                                                   float distance, LayerMask mask,
                                                   SpellDebugRole role, string label = null)
        {
            SpellDebugAreas.Segment(origin, origin + direction.normalized * distance, radius, role, label);
            var hits = Physics2D.CircleCastAll(origin, radius, direction, distance, mask);
            RecordContacts(hits);
            return Combat.EntityHitFilter.Collapse(hits);
        }

        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction,
                                              float distance, LayerMask mask,
                                              SpellDebugRole role, string label = null)
        {
            SpellDebugAreas.Segment(origin, origin + direction.normalized * distance, radius, role, label);
            var hit = Physics2D.CircleCast(origin, radius, direction, distance, mask);
            if (EntityCollisionDebug.Enabled) EntityCollisionDebug.Touched(hit.collider);
            return hit;
        }
    }
}
