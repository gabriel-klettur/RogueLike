using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes an area-of-effect spell: one <c>Physics2D</c> overlap at the resolved centre,
    /// then the burst rig <see cref="AreaBurstProfile"/> chose for it.
    /// </summary>
    public class AreaExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : 2f;
            Vector2 center = ResolveCenter(ctx, radius);
            SpellElement? element = ProjectileExecutor.ResolveElement(ctx.Spell);

            var profile = AreaBurstProfile.Resolve(ctx.Spell, radius);
            // Only the snare needs to know WHO it caught — its rig is the sole feedback that
            // anyone was held, because the spell deals no damage. Building the list for the
            // other four would allocate on every cast to hand it to something that ignores it.
            var caught = profile.Silhouette == AreaSilhouette.Snare ? new List<GameObject>() : null;

            var hits = Physics2D.OverlapCircleAll(center, radius, ctx.TargetLayers);
            foreach (var hit in hits)
            {
                if (hit.gameObject == ctx.Caster.gameObject) continue;
                var health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                int dealt = SpellPower.ScaleToInt(ctx.Spell.damage, ctx.Caster);
                health.TakeDamage(dealt, ctx.Caster.gameObject, element);
                Valkur.Core.GameEvents.FireHitDealt(ctx.Caster.gameObject, hit.gameObject, dealt);
                StatusApplicationFactory.ApplyAll(ctx.Spell.statusApplications,
                                                  health.gameObject, ctx.Caster.gameObject);

                if (caught == null) continue;
                // Asked AFTER the roll rather than inferred from the authored chance: the
                // target may be immune, may already have been held, and the rig must draw
                // what actually happened rather than what the asset hoped for.
                var status = health.GetComponent<StatusEffectManager>();
                if (status != null && status.IsRooted) caught.Add(health.gameObject);
            }

            AreaBurstFX.Play(center, profile, ctx.Caster, caught);
        }

        /// <summary>
        /// Where the circle actually goes.
        ///
        /// <para>THIS USED TO BE <c>castStart + direction * radius</c>, UNCONDITIONALLY. So
        /// <c>thunderclap</c> — a clap AROUND the caster, authored <c>castAnchor: Center</c> —
        /// detonated 3.6 units in FRONT of the player, and <c>frost_nova</c>'s ring left the
        /// caster standing outside their own nova. The two fields that say where the spell
        /// starts were being read and then overruled by a constant offset, which is the same
        /// shape as <c>spawnAtMouse</c> not reading the mouse: internally consistent, and
        /// disagreeing only with the screen.</para>
        ///
        /// <para>A spell anchored at the FEET or the CENTRE is anchored on the BODY, and a
        /// burst on the body is centred on the body. Hands and Head keep the historical
        /// forward push, because those anchors describe something leaving the caster.</para>
        /// </summary>
        private static Vector2 ResolveCenter(SpellContext ctx, float radius)
        {
            var anchor = ctx.Spell != null ? ctx.Spell.castAnchor : SpellCastAnchor.Hands;

            // A cursor-placed area is aimed, and SpellTargeting is the single owner of where an
            // aimed spell lands — including the clamp to the spell's own range, so the reach
            // stays something a player can learn.
            if (ctx.Spell != null && ctx.Spell.spawnAtMouse)
                return SpellTargeting.ResolveGroundTarget(ctx, radius * 2.5f, radius);

            if (anchor == SpellCastAnchor.Feet || anchor == SpellCastAnchor.Center)
            {
                // Forward clearance zeroed as well as the radius offset: that clearance is
                // muzzle spacing for something LEAVING the caster, and a nova is not leaving.
                return ProjectileExecutor.ResolveCastStart(
                    ctx.Caster, ctx.Direction, ctx.Spell.castAnchor, forwardOffset: 0f);
            }

            return (Vector2)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell)
                 + ctx.Direction * radius;
        }
    }
}
