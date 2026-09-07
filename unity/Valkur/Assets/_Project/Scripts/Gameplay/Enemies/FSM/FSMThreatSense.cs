using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// "Is something about to hit me, and from where?"
    ///
    /// <para>Every reactive behaviour this project has is driven by a DISTANCE — aggro,
    /// leash, standoff, reswing. That is enough for a monster that walks at you and swings,
    /// and it is exactly why a ranged player has never had to move: a bolt in flight is
    /// invisible to the FSM, so nothing an enemy does can ever be a reaction to being SHOT
    /// AT. <see cref="DodgeState"/> is the first behaviour that answers a projectile rather
    /// than a position, and this is the only thing that can tell it one is coming.</para>
    ///
    /// <para>THREE FILTERS, AND EACH REMOVES A DIFFERENT WAY OF LOOKING BROKEN. A projectile
    /// this entity FIRED is inside its own threat ring and closing on it for the frame it
    /// leaves the hand, so without the caster test every caster dives sideways the instant it
    /// shoots. A projectile that cannot damage this entity is not a threat, and dodging an
    /// ally's shot reads as the pack panicking at itself — the mask is the only honest
    /// allegiance test there is, since nothing in the damage path reads a faction. And a
    /// projectile whose velocity points AWAY is a shot that already missed; reacting to it is
    /// reacting to the past.</para>
    ///
    /// <para>It is a query and nothing else: it writes no state, starts no cooldown and takes
    /// no decision. The roll, the cooldown and the transition live in
    /// <see cref="FSMDodge"/> and in the state classes, so the census that keeps
    /// <c>FSMBuiltInTransitions</c> honest can still see every coded edge in the file that
    /// owns it.</para>
    /// </summary>
    public static class FSMThreatSense
    {
        /// <summary>
        /// How closely a projectile's heading must agree with "towards me" before it counts
        /// as inbound. cos(45 degrees): a shot passing at a wider angle than that is going
        /// to miss on its own, and diving out of the way of it is the AI showing the player
        /// that it cannot tell.
        /// </summary>
        private const float ClosingDot = 0.707f;

        /// <summary>
        /// Ignore anything already this close. Below it the dodge cannot finish before the
        /// impact, so the monster would be hit anyway AND be standing in the wrong place
        /// afterwards — the worst of both, and it reads as flinching at nothing.
        /// </summary>
        private const float MinReactDistance = 0.6f;

        /// <summary>
        /// The nearest projectile that is aimed at <paramref name="owner"/> and closing,
        /// expressed as its TRAVEL direction — which is what a sidestep needs, because a
        /// dodge is perpendicular to the shot, not away from the shooter.
        /// </summary>
        /// <returns>False when nothing qualifies, which is the overwhelmingly common case.</returns>
        public static bool TryFindIncomingProjectile(GameObject owner, float radius,
                                                     out Vector2 travelDirection)
        {
            travelDirection = Vector2.zero;
            if (owner == null || radius <= 0f) return false;

            int mask = ProjectileLayerMask();
            if (mask == 0) return false;

            Vector2 myPos = owner.transform.position;
            var buffer = PhysicsScratch.DodgeThreats;
            int count = Physics2D.OverlapCircleNonAlloc(myPos, radius, buffer, mask);
            if (count <= 0) return false;

            float bestSqr = float.MaxValue;
            for (int i = 0; i < count && i < buffer.Length; i++)
            {
                var col = buffer[i];
                if (col == null) continue;

                var projectile = col.GetComponentInParent<Projectile>();
                if (projectile == null) continue;

                // Mine. Never dodge your own shot.
                if (projectile.Caster != null &&
                    (projectile.Caster == owner.transform ||
                     projectile.Caster.IsChildOf(owner.transform) ||
                     owner.transform.IsChildOf(projectile.Caster)))
                    continue;

                // Not aimed at anything I am.
                if ((projectile.TargetLayers.value & (1 << owner.layer)) == 0) continue;

                Vector2 projPos = projectile.transform.position;
                Vector2 toMe = myPos - projPos;
                float sqr = toMe.sqrMagnitude;
                if (sqr < MinReactDistance * MinReactDistance) continue;
                if (sqr >= bestSqr) continue;

                Vector2 travel = ProjectileTravel(projectile);
                if (travel == Vector2.zero) continue;

                if (Vector2.Dot(travel, toMe.normalized) < ClosingDot) continue;

                bestSqr = sqr;
                travelDirection = travel;
            }

            return travelDirection != Vector2.zero;
        }

        /// <summary>
        /// Where the projectile is going. Its <c>Rigidbody2D.velocity</c> is the truth —
        /// <c>Projectile</c> rewrites it every FixedUpdate and a homing shot turns, so the
        /// direction it was fired in goes stale the moment it starts to curve. The transform's
        /// own right vector is the fallback for the frame before physics has run once.
        /// </summary>
        private static Vector2 ProjectileTravel(Projectile projectile)
        {
            var rb = projectile.GetComponent<Rigidbody2D>();
            if (rb != null && rb.velocity.sqrMagnitude > 0.0001f) return rb.velocity.normalized;

            Vector2 forward = projectile.transform.right;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector2.zero;
        }

        /// <summary>
        /// Resolved once per call rather than cached in a static: Domain Reload is OFF, and a
        /// cached layer index is exactly the kind of static that survives a Play-mode restart
        /// holding a value from a project state that no longer exists.
        /// <c>LayerMask.NameToLayer</c> is a dictionary lookup, not a scene query.
        /// </summary>
        private static int ProjectileLayerMask()
        {
            int layer = LayerMask.NameToLayer("Projectile");
            return layer >= 0 ? 1 << layer : 0;
        }
    }
}
