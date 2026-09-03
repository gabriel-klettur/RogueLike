using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a cone breath: a directional wedge that damages everything inside it on a
    /// tick, for <c>duration</c> seconds.
    ///
    /// <para><c>coneLength</c> IS IN WORLD UNITS. It used to be divided by 16 — the pixel
    /// scale of the Python build this game was ported from, the fifth sighting of it after
    /// <c>wallWidth</c>, the totem's radius, the vortex's radius and <c>range</c> on three
    /// executors. The tell was the same every time: the fallback this method reaches for when
    /// the field is unauthored (<see cref="DEFAULT_LENGTH"/>) was SIXTEEN TIMES larger than
    /// anything the shipped asset could produce. Measured, <c>flame_breath</c>'s authored
    /// 16.25 resolved to a cone 1.02 units long against a camera 33.33 units wide — a breath
    /// weapon that reached three per cent of the screen and stopped short of the caster's own
    /// sprite. Nothing failed; every number was internally consistent and disagreed only with
    /// the display.</para>
    /// </summary>
    public class ConeBreathExecutor : ISpellExecutor
    {
        /// <summary>Reach for a spell that authors none, in world units.</summary>
        public const float DEFAULT_LENGTH = 5.5f;

        public const float DEFAULT_ARC = 60f;
        public const float DEFAULT_DURATION = 1.5f;
        public const float DEFAULT_DAMAGE_PER_TICK = 4f;
        public const float DEFAULT_TICK_PERIOD = 0.2f;

        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.coneArc > 0 ? ctx.Spell.coneArc : DEFAULT_ARC;
            float length = ctx.Spell.coneLength > 0 ? ctx.Spell.coneLength : DEFAULT_LENGTH;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : DEFAULT_DURATION;
            float damagePerTick = ctx.Spell.damagePerTick > 0 ? ctx.Spell.damagePerTick : DEFAULT_DAMAGE_PER_TICK;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : DEFAULT_TICK_PERIOD;

            Vector3 castStart = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            var coneGo = new GameObject("ConeBreath");
            coneGo.transform.position = castStart;

            var controller = coneGo.AddComponent<ConeBreathController>();
            controller.SetCastOrigin(ctx.Spell);
            controller.SetSwatch(ctx.Spell);
            controller.Initialize(duration, arc, length, Mathf.RoundToInt(damagePerTick),
                tickPeriod, ctx.Direction, ctx.Caster, ctx.TargetLayers, ctx.Spell.element,
                ProjectileExecutor.ResolveElement(ctx.Spell));

            // Tracked like every other free-standing spell effect. Without this the spell's
            // own maxInstances was dead data — the cone spawned a loose GameObject nothing
            // owned, so a cooldown shorter than the duration stacked breaths on top of each
            // other and a zone change left one burning in a world that no longer existed.
            SpellEffectRegistry.Track(coneGo, ctx.Spell,
                ctx.Caster != null ? ctx.Caster.gameObject : null);
        }
    }
}
