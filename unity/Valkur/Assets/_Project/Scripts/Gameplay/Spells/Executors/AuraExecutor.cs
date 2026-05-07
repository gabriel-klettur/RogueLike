using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a healing aura: creates a lingering area that heals allies within radius.
    /// Mirrors Python's AuraResolver with heal_per_second buff. All visuals are built
    /// procedurally inside <see cref="AuraController"/> for an "epic" holy/nature look
    /// (ground rune, light pillar, rising sparkles, pulsing rings, Light2D glow).
    /// </summary>
    public class AuraExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration    = ctx.Spell.duration    > 0 ? ctx.Spell.duration              : 6f;
            float radius      = ctx.Spell.radius      > 0 ? ctx.Spell.radius / 16f          : 1.5f;
            float healPerTick = ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick           : 20f;
            float tickPeriod  = ctx.Spell.tickPeriod  > 0 ? ctx.Spell.tickPeriod            : 0.5f;

            // Ensure a minimum on-screen footprint so the rune is always readable.
            float visualRadius = Mathf.Max(radius, 1.25f);

            var auraGo = new GameObject("SpellAura_Healing");
            auraGo.transform.SetParent(ctx.Caster, false);
            auraGo.transform.localPosition = Vector3.zero;

            var controller = auraGo.AddComponent<AuraController>();
            controller.InitializeHealing(
                duration:     duration,
                gameRadius:   radius,
                visualRadius: visualRadius,
                healPerTick:  Mathf.RoundToInt(healPerTick),
                tickPeriod:   tickPeriod,
                caster:       ctx.Caster);

            // Keep data-driven preset support (extra particles on top of procedural FX).
            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, ctx.Caster.position, duration);

        }
    }
}
