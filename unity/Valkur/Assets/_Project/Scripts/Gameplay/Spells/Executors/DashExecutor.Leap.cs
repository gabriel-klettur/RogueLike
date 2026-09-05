using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The half of a Dash that makes <c>leap_slam</c> a different spell from <c>dash</c>:
    /// aiming at a POINT, and an area application where the body lands.
    ///
    /// <para>The roadmap describes this spell as "a Dash composed with an Area on landing".
    /// That composition did not exist — the Dash executor read only <c>collisionDamage</c>
    /// (a shoulder-check along the path), and a leap authors 0 there because it does not
    /// barge through anything, it comes DOWN on one spot. The result was a spell with four
    /// authored combat fields none of which reached any code. Same shape as the spawner
    /// coordinate drift: every half internally consistent, the composition missing.</para>
    /// </summary>
    public partial class DashExecutor
    {
        /// <summary>Wall clearance for an AIMED leap, in world units.</summary>
        private const float WALL_CLEARANCE = 0.4f;

        /// <summary>
        /// A spell that authors an area to land in. Gated rather than always-on so the two
        /// shipped dashes — which author <c>radius: 0</c> — behave exactly as they always have.
        /// </summary>
        internal static bool HasLandingSlam(Data.SpellDefinition spell)
            => spell != null
               && spell.radius > 0f
               && (spell.damage > 0f
                   || (spell.statusApplications != null && spell.statusApplications.Length > 0));

        /// <summary>
        /// How far, and in which direction, the body actually goes.
        ///
        /// <para>The cursor is read through <see cref="SpellTargeting"/> — the single owner of
        /// what <c>spawnAtMouse</c> means — and then REBASED. That helper measures from
        /// <c>ResolveCastStart</c>, i.e. hand height plus forward clearance, which is where a
        /// projectile leaves from; turned into a body displacement unrebased it would carry
        /// the character upward by the height of their own hands on every leap.</para>
        ///
        /// <para>A cursor can point through a wall, so an aimed leap is swept and stopped
        /// short. The facing path is deliberately NOT swept: that is what <c>dash</c> and
        /// <c>hostile_dash</c> have always done and nothing here is a reason to change it.</para>
        /// </summary>
        private static Vector2 ResolveTravel(SpellContext ctx, Vector2 startPos, float dist)
        {
            if (!ctx.Spell.spawnAtMouse) return ctx.Direction * dist;

            Vector2 aimed = SpellTargeting.ResolveGroundTarget(ctx, dist, dist);
            Vector2 lift = (Vector2)ProjectileExecutor.ResolveCastStart(
                               ctx.Caster, ctx.Direction, ctx.Spell) - startPos;

            Vector2 travel = aimed - lift - startPos;
            if (travel.sqrMagnitude > dist * dist) travel = travel.normalized * dist;

            // A click on the character's own feet is not a cast the player meant to waste.
            if (travel.sqrMagnitude < 0.0025f) return ctx.Direction * dist;

            return ClampToOpenGround(startPos, travel);
        }

        private static Vector2 ClampToOpenGround(Vector2 startPos, Vector2 travel)
        {
            float length = travel.magnitude;
            Vector2 heading = travel / length;

            var hit = Physics2D.CircleCast(startPos, SWEEP_RADIUS, heading, length,
                                           World.Layering.WorldCollisionLayers.BlockingMask());
            if (hit.collider == null) return travel;

            return heading * Mathf.Max(0f, hit.distance - WALL_CLEARANCE);
        }

        /// <summary>
        /// The landing itself: everything inside <c>radius</c> takes <c>damage</c>, is thrown
        /// outward by <c>knockback</c> and picks up whatever <c>statusApplications</c> the
        /// spell authors.
        ///
        /// <para>Knockback is RADIAL, away from the point of impact — a leap lands in the
        /// middle of a group and scatters it, which is a different verb from a dash pushing
        /// everything the same way. Deferred to the moment the flight rig says the body has
        /// arrived, so the damage and the picture happen on the same frame.</para>
        /// </summary>
        private static void ApplyLandingSlam(SpellContext ctx, Vector2 landing)
        {
            if (ctx.Caster == null || ctx.Spell == null) return;

            LeapImpactFX.Play(landing, ctx.Spell);

            // The weight of the blow. No zoom punch, ever: CameraPixelSnap derives its lattice
            // from the live ortho size and a few-percent punch lands between rungs, which makes
            // every tile on screen crawl. Weight is kick, shake and hit-stop instead.
            Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.ImpactHeavy, Vector2.down);
            Feel.CameraFeel.Freeze(HIT_STOP_SECONDS);

            if (ctx.TargetLayers.value == 0) return;

            float radius = ctx.Spell.radius;
            var element = ProjectileExecutor.ResolveElement(ctx.Spell);
            var hits = Physics2D.OverlapCircleAll(landing, radius, ctx.TargetLayers);
            var struck = new HashSet<Health>();

            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i];
                if (collider == null) continue;
                if (collider.transform.IsChildOf(ctx.Caster)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null || health.IsDead || !struck.Add(health)) continue;

                int dealt = SpellPower.ScaleToInt(ctx.Spell.damage, ctx.Caster);
                if (dealt > 0)
                {
                    health.TakeDamage(dealt, ctx.Caster.gameObject, element);
                    // ComboCounter listens on this; a player damage path that does not raise
                    // it is a combo that can never start.
                    GameEvents.FireHitDealt(ctx.Caster.gameObject, health.gameObject, dealt);
                }

                StatusApplicationFactory.ApplyAll(ctx.Spell.statusApplications,
                                                  health.gameObject, ctx.Caster.gameObject);

                if (ctx.Spell.knockback <= 0f) continue;
                var hitRb = health.GetComponent<Rigidbody2D>();
                if (hitRb == null) continue;

                Vector2 away = (Vector2)health.transform.position - landing;
                // A body standing exactly on the impact point has no direction to be thrown
                // in; give it one rather than multiplying by a zero vector.
                if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitCircle.normalized;
                hitRb.AddForce(away.normalized * ctx.Spell.knockback, ForceMode2D.Impulse);
            }
        }

        /// <summary>
        /// Hit-stop on the landing. Short on purpose: long enough that the frame is felt to
        /// stop, short enough that it never reads as a dropped frame.
        /// </summary>
        private const float HIT_STOP_SECONDS = 0.06f;
    }
}
