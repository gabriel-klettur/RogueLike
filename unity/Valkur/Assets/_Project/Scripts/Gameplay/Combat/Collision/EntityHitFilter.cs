using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Folds a physics query's results down to ONE collider per entity.
    ///
    /// <para>An entity with a shaped hurtbox answers a query with its footprint AND every
    /// capsule the query touched, and most damage paths here do not dedupe: they walk the array
    /// and call <c>TakeDamage</c> on each <c>Health</c> they find. Without this a fireball's
    /// splash would hit a dragon once per capsule. Folding it here, once, inside
    /// <c>SpellProbe</c>, is what let the hurtbox arrive without editing twenty executors.</para>
    ///
    /// <para><b>Which collider survives matters, and it is the capsule NEAREST the query.</b>
    /// Several executors then measure against the collider they were handed (a cone's
    /// <c>ClosestPoint</c>, an impact flash) — so the representative is the part of the body
    /// that is actually in front of the attacker, never the footprint unless it is all the query
    /// touched. Colliders that belong to no rig (walls, pickups, an entity built before the rig
    /// existed) pass through untouched and keep their order.</para>
    /// </summary>
    public static class EntityHitFilter
    {
        private static readonly Dictionary<int, int> SlotByRig = new Dictionary<int, int>(32);
        private static readonly List<float> Distances = new List<float>(32);

        /// <summary>Collapse a NonAlloc buffer in place. Returns the new count.</summary>
        public static int Collapse(Collider2D[] buffer, int count, Vector2 from)
        {
            if (buffer == null || count <= 1) return count;
            if (!AnyRigOwned(buffer, count)) return count;

            SlotByRig.Clear();
            Distances.Clear();
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                Collider2D c = buffer[read];
                if (c == null) continue;

                if (!EntityColliderRig.TryGetOwner(c, out var rig))
                {
                    buffer[write++] = c;
                    Distances.Add(0f);
                    continue;
                }

                float score = Score(rig, c, from);
                int key = rig.GetInstanceID();
                if (SlotByRig.TryGetValue(key, out int slot))
                {
                    if (score < Distances[slot])
                    {
                        buffer[slot] = c;
                        Distances[slot] = score;
                    }
                    continue;
                }

                SlotByRig[key] = write;
                buffer[write++] = c;
                Distances.Add(score);
            }

            for (int i = write; i < count; i++) buffer[i] = null;
            SlotByRig.Clear();
            Distances.Clear();
            return write;
        }

        /// <summary>Collapse an array the query allocated. Returns the same array when nothing folded.</summary>
        public static Collider2D[] Collapse(Collider2D[] hits, Vector2 from)
        {
            if (hits == null || hits.Length <= 1) return hits;
            int count = Collapse(hits, hits.Length, from);
            if (count == hits.Length) return hits;
            var trimmed = new Collider2D[count];
            System.Array.Copy(hits, trimmed, count);
            return trimmed;
        }

        /// <summary>
        /// Collapse a cast's hits: per entity the one with the smallest DISTANCE along the cast,
        /// which is the part of the body the sweep reaches first.
        /// </summary>
        public static RaycastHit2D[] Collapse(RaycastHit2D[] hits)
        {
            if (hits == null || hits.Length <= 1) return hits;

            bool any = false;
            for (int i = 0; i < hits.Length && !any; i++)
                any = EntityColliderRig.TryGetOwner(hits[i].collider, out _);
            if (!any) return hits;

            SlotByRig.Clear();
            var kept = new List<RaycastHit2D>(hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!EntityColliderRig.TryGetOwner(h.collider, out var rig)) { kept.Add(h); continue; }
                int key = rig.GetInstanceID();
                if (SlotByRig.TryGetValue(key, out int slot))
                {
                    if (h.distance < kept[slot].distance) kept[slot] = h;
                    continue;
                }
                SlotByRig[key] = kept.Count;
                kept.Add(h);
            }
            SlotByRig.Clear();
            return kept.Count == hits.Length ? hits : kept.ToArray();
        }

        /// <summary>
        /// A capsule always beats the footprint; among capsules the nearest to the query wins.
        /// </summary>
        private static float Score(EntityColliderRig rig, Collider2D c, Vector2 from)
        {
            float d = ((Vector2)c.ClosestPoint(from) - from).sqrMagnitude;
            return c == rig.Footprint ? d + 1e6f : d;
        }

        private static bool AnyRigOwned(Collider2D[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
                if (EntityColliderRig.TryGetOwner(buffer[i], out _)) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SlotByRig.Clear();
            Distances.Clear();
        }
    }
}
