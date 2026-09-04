using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The damage half of a slash. It advances with the drawing rather than resolving in
    /// one frame, so a target is struck when the visible edge reaches it and the reach is
    /// exactly the radius that is drawn — the previous path damaged in a circle one and a
    /// half times longer than anything the player could see.
    /// </summary>
    public sealed partial class SlashAttack
    {
        private void AdvanceDamage(float eased)
        {
            if (_context.Caster == null || _context.Spell == null) return;
            if (_context.TargetLayers.value == 0) return;

            if (_profile.IsRadial) AdvanceRadialDamage(eased);
            else AdvanceAngularDamage(eased);

            AdvanceObstacleDamage(eased);
        }

        /// <summary>
        /// A destructible obstacle is struck ONCE per swing, at the point the drawn edge is
        /// halfway through its arc.
        ///
        /// <para>The per-target dedupe above is keyed on <see cref="Health"/>, and an
        /// obstacle is reached through <see cref="IDestructibleObstacle"/> rather than
        /// through the overlap query — it lives on Building, which no target mask contains.
        /// A flag is the honest dedupe for something the sweep cannot enumerate.</para>
        /// </summary>
        private void AdvanceObstacleDamage(float eased)
        {
            if (_obstaclesStruck || eased < 0.5f) return;

            bool anyObstacles = DestructibleObstacleRegistry.Count > 0;
            bool anySeams = Valkur.Gameplay.World.HarvestSwingRegistry.Count > 0;
            if (!anyObstacles && !anySeams) { _obstaclesStruck = true; return; }

            _obstaclesStruck = true;

            int damage = SpellPower.ScaleToInt(_context.Spell.damage, _context.Caster);
            if (damage <= 0) damage = 1;
            var attacker = _context.Caster != null ? _context.Caster.gameObject : null;
            var element = ProjectileExecutor.ResolveElement(_context.Spell);

            if (anyObstacles)
                DestructibleObstacleRegistry.DamageInArc(
                    transform.position, _profile.Radius, _direction, _profile.ArcDegrees,
                    damage, attacker, element);

            // Harvest seams are reached the same way and for the same reason, but through a
            // registry of their own. They deliberately do NOT implement IDestructibleObstacle:
            // Projectile resolves that interface directly off the collider's parents, so a
            // seam that implemented it could be emptied by any stray fireball that clipped it.
            // See HarvestSwingRegistry.
            if (anySeams)
                Valkur.Gameplay.World.HarvestSwingRegistry.WorkInArc(
                    transform.position, _profile.Radius, _direction, _profile.ArcDegrees,
                    damage, attacker, element);
        }

        /// <summary>Leading edge crosses the arc; each target is hit as it is passed.</summary>
        private void AdvanceAngularDamage(float eased)
        {
            float headAngle = Mathf.Lerp(-_profile.HalfArc, _profile.HalfArc, eased);
            var hits = Physics2D.OverlapCircleAll(transform.position, _profile.Radius,
                                                  _context.TargetLayers);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                Health health = ResolveTarget(hit);
                if (health == null) continue;

                Vector2 point = ResolveBodyPoint(health);
                if (!IsInsideSector(transform.position, _direction, point,
                                    _profile.Radius, _profile.ArcDegrees)) continue;

                float signedAngle = SignedAngleTo(point);
                if (signedAngle < _previousHeadAngle - 0.01f || signedAngle > headAngle + 0.01f)
                    continue;

                Strike(health, point);
            }

            _previousHeadAngle = headAngle;
        }

        /// <summary>Point of the lance travels outward; targets are pierced in depth order.</summary>
        private void AdvanceRadialDamage(float eased)
        {
            float reach = Mathf.Lerp(SlashLanceMesh.RADIAL_START, 1f, eased) * _profile.Radius;
            var hits = Physics2D.OverlapCircleAll(transform.position, reach, _context.TargetLayers);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                Health health = ResolveTarget(hit);
                if (health == null) continue;

                Vector2 point = ResolveBodyPoint(health);
                if (!IsInsideSector(transform.position, _direction, point,
                                    reach, _profile.ArcDegrees)) continue;

                float distance = Vector2.Distance(point, transform.position);
                if (distance < _previousReach - 0.01f) continue;

                Strike(health, point);
            }

            _previousReach = reach;
        }

        /// <summary>
        /// A living, not-yet-struck target that is neither the caster nor part of its
        /// hierarchy. Resolved from the parent chain because damageable entities carry
        /// their collider on a child as often as not.
        /// </summary>
        private Health ResolveTarget(Collider2D hit)
        {
            if (hit == null) return null;

            Health health = hit.GetComponentInParent<Health>();
            if (health == null || health.IsDead || _damaged.Contains(health)) return null;

            Transform caster = _context.Caster;
            if (health.transform == caster ||
                health.transform.IsChildOf(caster) ||
                caster.IsChildOf(health.transform)) return null;

            return health;
        }

        /// <summary>
        /// Where a victim's body actually is.
        ///
        /// The overlap query returns whatever collider of theirs happens to sit on the
        /// target layer, and on an NPC that includes the large, off-centre trigger it uses
        /// to notice the player. Measuring the hit — and drawing the impact — against that
        /// trigger put the contact flash somewhere between the two characters instead of on
        /// the one that was struck. The body box the entity bootstrap builds is centred on
        /// the sprite, so it is the honest answer.
        /// </summary>
        private static Vector2 ResolveBodyPoint(Health health)
        {
            Collider2D body = ResolveBodyCollider(health);
            if (body != null) return body.bounds.center;

            var sr = health.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr.bounds.center;

            return health.transform.position;
        }

        private static Collider2D ResolveBodyCollider(Health health)
            => EntityColliderConfigurator.GetBodyCollider(health.gameObject);

        private float SignedAngleTo(Vector2 point)
        {
            Vector2 toTarget = point - (Vector2)transform.position;
            return toTarget.sqrMagnitude <= 0.0001f
                ? 0f
                : Vector2.SignedAngle(_direction, toTarget.normalized);
        }

        private void Strike(Health health, Vector2 bodyPoint)
        {
            int before = health.CurrentHp;
            int damage = Mathf.Max(1, SpellPower.ScaleToInt(_context.Spell.damage, _context.Caster));
            GameObject casterGo = _context.Caster.gameObject;
            health.TakeDamage(damage, casterGo, ProjectileExecutor.ResolveElement(_context.Spell));
            if (health.CurrentHp == before) return;

            _damaged.Add(health);
            GameEvents.FireHitDealt(casterGo, health.gameObject, damage);
            StatusApplicationFactory.ApplyAll(_context.Spell.statusApplications, health.gameObject, casterGo);

            CombatFeedback feedback = health.GetComponent<CombatFeedback>();
            if (feedback != null) feedback.ApplyKnockback(transform.position);

            // On the face of the body turned towards the swing, so the burst lands where
            // the blade meets them. ClosestPoint returns the query point itself when the
            // origin is inside the collider, which is the one case it cannot answer.
            Collider2D body = ResolveBodyCollider(health);
            Vector2 impactPoint = body != null ? body.ClosestPoint(transform.position) : bodyPoint;
            if ((impactPoint - (Vector2)transform.position).sqrMagnitude < 0.01f)
                impactPoint = bodyPoint;

            SlashImpactBurst.Spawn(impactPoint, _direction, _profile);
            SpawnImpactPreset(impactPoint);

            _hitCount++;
            if (_hitCount > 1) return;

            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_slash_hit");

            // Camera and hit-stop are the director's now: it owns the audience filter, the
            // rate limiting and the trauma budget in one place, so no call site can forget
            // any of the three.
        }

        private void SpawnImpactPreset(Vector2 at)
        {
            string preset = _context.Spell.impactPreset;
            if (string.IsNullOrEmpty(preset)) return;

            var manager = VFX.VFXManager.Instance;
            if (manager != null) manager.SpawnParticlePreset(preset, at);
        }
    }
}
