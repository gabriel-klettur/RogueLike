using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The seams a SWING can work, so holding the attack button mines a rock the same way it
    /// already chops a tree.
    ///
    /// <para>WHY THIS IS NOT <c>IDestructibleObstacle</c>, having tried that first and been
    /// wrong. Implementing that interface looks like the cheap answer — a tree already reaches
    /// swings through it — but the interface is not swing-only. <c>Projectile</c> resolves it
    /// directly with <c>GetComponentInParent&lt;IDestructibleObstacle&gt;()</c> rather than
    /// through <c>DestructibleObstacleRegistry</c>, precisely because a thing on the Building
    /// layer is unreachable any other way. A mine IS a building, and its collision cells are
    /// children of the object a <see cref="HarvestNode"/> sits on, so implementing the
    /// interface would have let any stray fireball that clipped a seam empty it from across
    /// the room, with no proximity, no arc and no session.</para>
    ///
    /// <para>There was a second hazard behind that one. The interface is on the TYPE, not on
    /// the mode, so a Destroy-mode tree would have carried two implementers —
    /// <see cref="BuildingDurability"/> and the node — and <c>GetComponentInParent</c> returns
    /// the FIRST match. Which one a projectile damaged would have been decided by
    /// <c>AddComponent</c> order in the loader: correct today by luck, and this project has
    /// already recorded what that costs (the boomerang's two components, where "there is no
    /// DefaultExecutionOrder anywhere in the project, so that was luck").</para>
    ///
    /// <para>So the swing gets its own door. Both callers that deal swing damage —
    /// <c>MeleeCombat</c> and <c>SlashAttack</c> — ask this registry alongside the obstacle
    /// one, and nothing else in the game can see it.</para>
    /// </summary>
    public static class HarvestSwingRegistry
    {
        private static List<HarvestNode> _live = new List<HarvestNode>(4);

        /// <summary>How many seams a swing could currently work. Zero is the normal case.</summary>
        public static int Count => _live.Count;

        /// <summary>
        /// Domain Reload is OFF, so a list left holding destroyed nodes would carry into the
        /// next Play session and be walked by the first swing. Assigning a FRESH list is a
        /// plain <c>stsfld</c>, the only reset shape <c>DomainReloadStaticResetTests</c>
        /// recognises — <c>_live.Clear()</c> passes the field as an argument and reads to that
        /// scanner as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _live = new List<HarvestNode>(4);

        public static void Register(HarvestNode node)
        {
            if (node == null || _live.Contains(node)) return;
            _live.Add(node);
        }

        public static void Unregister(HarvestNode node)
        {
            if (node == null) return;
            _live.Remove(node);
        }

        /// <summary>
        /// Work every seam whose surface falls inside the swing's circle and arc. Returns how
        /// many were struck, so a caller can fold them into its own hit count.
        ///
        /// <para>The range test is against the node's BOUNDS, not its centre, and matches
        /// <c>DestructibleObstacleRegistry.DamageInArc</c> deliberately: a mine face is several
        /// units across, and a player who can chop the near edge of a tree expects to be able
        /// to work the near edge of a rock. A swing centred INSIDE the bounds skips the arc
        /// test rather than failing it, for the same reason it does there — the direction to
        /// the contact point is undefined when the contact point is the query point.</para>
        ///
        /// <para>A flat scan with no spatial hash, unlike its sibling: the seams are a handful
        /// where the destructible obstacles are a forest, and building an accelerator to search
        /// four items costs more than searching four items.</para>
        /// </summary>
        public static int WorkInArc(Vector2 origin, float radius, Vector2 direction,
            float arcDegrees, int damage, GameObject attacker, SpellElement? element)
        {
            if (_live.Count == 0 || damage <= 0) return 0;

            int struck = 0;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var node = _live[i];
                if (node == null) { _live.RemoveAt(i); continue; }
                if (!node.AcceptsSwing) continue;

                Vector2 contact = node.InteractionBounds.ClosestPoint(origin);
                Vector2 toContact = contact - origin;
                if (toContact.sqrMagnitude > radius * radius) continue;

                if (toContact.sqrMagnitude > 0.0001f && arcDegrees < 360f)
                {
                    float angle = Vector2.Angle(direction.normalized, toContact.normalized);
                    if (angle > arcDegrees * 0.5f) continue;
                }

                node.ApplySwing(damage, attacker, contact, element);
                struck++;
            }
            return struck;
        }
    }
}
