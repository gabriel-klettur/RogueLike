using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a continuous smoke emitter: attaches a particle emitter to the caster.
    /// Mirrors Python's SmokeEmitterResolver.
    /// </summary>
    public class SmokeEmitterExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 3f;
            Vector3 pos = ctx.Caster.position;

            if (VFXManager.Instance != null)
            {
                if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                    VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, pos, duration);
                else
                {
                    Color col = ctx.Spell.particleColor != Color.clear
                        ? ctx.Spell.particleColor
                        : new Color(0.78f, 0.78f, 0.78f, 0.5f);
                    VFXManager.Instance.SpawnAreaIndicator(pos, col, 2f, duration);
                }
            }

            Debug.Log($"[SpellDebug] SmokeEmitter cast at {pos}, duration={duration:F2}s");
        }
    }
}
