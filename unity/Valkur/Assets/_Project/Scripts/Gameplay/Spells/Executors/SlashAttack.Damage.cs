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
            // The SECTOR is what this swing can reach; the circle is only the broad phase that
            // IsInsideSector then narrows. Recording the circle alone would draw an area up to
            // six times the swing on a thrust, which is the legacy slash defect this path was
            // written to remove.
            Debugging.SpellDebugAreas.Sector(transform.position, _direction, _profile.Radius,
                _profile.ArcDegrees, Debugging.SpellDebugRole.Damage,
                "tajo " + _profile.Radius.ToString("0.##") + " u / " + _profile.ArcDegrees.ToString("0") + " grados");
            var hits = Debugging.SpellProbe.OverlapCircleAll(transform.position, _profile.Radius,
                                                  _context.TargetLayers,
                                                  Debugging.SpellDebugRole.Reach, "fase amplia");

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                Health health = ResolveTarget(hit);
                if (health == null) continue;

                // The BODY is sampled, not its centre: a creature wider than the sliver the edge
                // swept this frame is struck when the blade reaches its near side.
                bool struck = SweptBodyTest.TryAngular(health.gameObject, transform.position, _direction,
                    _profile.Radius, _profile.ArcDegrees, _previousHeadAngle, headAngle,
                    out Vector2 point, out Vector2 reported, out bool inside);
                Debugging.EntityCollisionDebug.TestPoint(health.gameObject, reported, inside, "cuerpo (hurtbox)");
                if (!struck) continue;

                Strike(health, point);
            }

            _previousHeadAngle = headAngle;
        }

        /// <summary>Point of the lance travels outward; targets are pierced in depth order.</summary>
        private void AdvanceRadialDamage(float eased)
        {
            float reach = Mathf.Lerp(SlashLanceMesh.RADIAL_START, 1f, eased) * _profile.Radius;
            Debugging.SpellDebugAreas.Sector(transform.position, _direction, reach,
                _profile.ArcDegrees, Debugging.SpellDebugRole.Damage,
                "punta " + reach.ToString("0.##") + " u");
            var hits = Debugging.SpellProbe.OverlapCircleAll(transform.position, reach, _context.TargetLayers,
                Debugging.SpellDebugRole.Reach, null);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                Health health = ResolveTarget(hit);
                if (health == null) continue;

                bool struck = SweptBodyTest.TryRadial(health.gameObject, transform.position, _direction,
                    reach, _profile.ArcDegrees, _previousReach, out Vector2 point, out bool inside);
                Debugging.EntityCollisionDebug.TestPoint(health.gameObject, point, inside, "cuerpo (hurtbox)");
                if (!struck) continue;

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

        private void Strike(Health health, Vector2 bodyPoint)
        {
            int before = health.CurrentHp;
            int damage = Mathf.Max(1, SpellPower.ScaleToInt(_context.Spell.damage, _context.Caster));
            GameObject casterGo = _context.Caster.gameObject;
            // One roll per victim: the player's crit stat reaches every path that deals damage,
            // and the number the victim shows is the one they were dealt.
            damage = CritResolver.Resolve(damage, casterGo, out bool wasCrit);
            health.TakeDamage(damage, casterGo, ProjectileExecutor.ResolveElement(_context.Spell), wasCrit);
            if (health.CurrentHp == before) return;

            _damaged.Add(health);
            GameEvents.FireHitDealt(casterGo, health.gameObject, damage);
            StatusApplicationFactory.ApplyAll(_context.Spell.statusApplications, health.gameObject, casterGo);

            CombatFeedback feedback = health.GetComponent<CombatFeedback>();
            if (feedback != null) feedback.ApplyKnockback(transform.position);

            // On the face of the body turned towards the swing, so the burst lands where
            // the blade meets them. ClosestPoint returns the query point itself when the
            // origin is inside the collider, which is the one case it cannot answer.
            Vector2 impactPoint = EntityBody.ClosestPoint(health.gameObject, transform.position);
            if ((impactPoint - (Vector2)transform.position).sqrMagnitude < 0.01f)
                impactPoint = bodyPoint;

            SlashImpactBurst.Spawn(impactPoint, _direction, _profile);
            SpawnImpactPreset(impactPoint);

            _hitCount++;
            if (_hitCount > 1) return;

            // Gated on HasSfx. AudioCatalog ships no spell_* id at all, so an ungated call here is
            // one console warning per id on the first swing of every session -- and a spell without
            // a sound is missing content, not a data bug. See IAudioService.HasSfx.
            var audioSvc = ServiceLocator.Get<IAudioService>();
            if (audioSvc != null && audioSvc.HasSfx("spell_slash_hit")) audioSvc.PlaySfxById("spell_slash_hit");

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
