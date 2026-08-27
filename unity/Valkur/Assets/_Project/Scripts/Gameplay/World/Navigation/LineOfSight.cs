using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// "Can A see B through the world?" — the one geometry query that separates a
    /// monster reacting to what it can perceive from a monster reacting to a number.
    ///
    /// Before this existed, aggro and NPC melee were naked distance tests: walking
    /// past a wall woke everything on the far side (barbol_gigante has an aggro
    /// radius of 30), and a monster on the other side of a building could still
    /// land its swing. Cover, kiting and breaking line of sight were mechanically
    /// absent because nothing in the project ever cast a ray at world geometry.
    ///
    /// Uses <see cref="WorldCollisionLayers.BlockingMask"/> — the legacy
    /// <c>World</c>/<c>Building</c> layers PLUS every painted <c>WorldL{N}</c> /
    /// <c>WorldAll</c> cell. Entities are not in that mask, so neither the caster,
    /// the target, nor a monster standing between them ever blocks the line.
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>
        /// A hit this close to the origin is the caster standing ON a painted cell,
        /// not a wall between it and its target. Without this, any entity that
        /// spawned on top of collision geometry would be permanently blind —
        /// <c>Physics2D.queriesStartInColliders</c> defaults to true.
        /// </summary>
        private const float StartEpsilon = 0.05f;

        // Hoisted so a per-frame perception check on a pack of monsters allocates
        // nothing. Four hits is far more than any straight line needs to decide.
        [SelfHealingStatic("Scratch buffer. LinecastNonAlloc rewrites slots 0..count-1 " +
                           "before any read, and nothing above count is ever read, so a " +
                           "stale collider from the previous Play session is unreachable.")]
        private static readonly RaycastHit2D[] _hits = new RaycastHit2D[4];

        /// <summary>
        /// True when no world geometry stands between the two points. A wall the
        /// target is pressed against DOES block: that is the case this exists for.
        /// </summary>
        public static bool IsClear(Vector2 from, Vector2 to)
        {
            int count = Physics2D.LinecastNonAlloc(from, to, _hits, WorldCollisionLayers.BlockingMask());
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].distance > StartEpsilon) return false;
            }
            return true;
        }

        /// <summary>Convenience inverse of <see cref="IsClear"/>.</summary>
        public static bool IsBlocked(Vector2 from, Vector2 to) => !IsClear(from, to);
    }
}
