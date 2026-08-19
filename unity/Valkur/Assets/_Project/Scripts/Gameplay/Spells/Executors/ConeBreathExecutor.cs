using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a cone breath attack: directional cone AoE with damage ticks over duration.
    /// Mirrors Python's ConeBreathResolver (flame_breath: hold-to-channel cone).
    /// </summary>
    public class ConeBreathExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.coneArc > 0 ? ctx.Spell.coneArc : 60f;
            float length = ctx.Spell.coneLength > 0 ? ctx.Spell.coneLength / 16f : 16.25f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 2f;
            float damagePerTick = ctx.Spell.damagePerTick > 0 ? ctx.Spell.damagePerTick : 4f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.2f;

            Vector3 castStart = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            var coneGo = new GameObject("ConeBreath");
            coneGo.transform.position = castStart;

            var controller = coneGo.AddComponent<ConeBreathController>();
            controller.SetCastOrigin(ctx.Spell);
            controller.Initialize(duration, arc, length, Mathf.RoundToInt(damagePerTick),
                tickPeriod, ctx.Direction, ctx.Caster, ctx.TargetLayers, ctx.Spell.element);

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, castStart, duration);

        }
    }
}
