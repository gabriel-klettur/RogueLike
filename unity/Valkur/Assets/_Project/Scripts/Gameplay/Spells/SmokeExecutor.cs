using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a smoke burst spell: spawns a cloud of particles at caster position.
    /// Mirrors Python's SmokeResolver — purely visual/utility (no damage).
    /// </summary>
    public class SmokeExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            Vector3 pos = ctx.Caster.position;

            if (VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.78f, 0.78f, 0.78f, 0.6f);

                if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                    VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, pos);
                else
                    VFXManager.Instance.SpawnAreaIndicator(pos, col, 1.5f, 0.8f);
            }

            Debug.Log($"[SpellDebug] Smoke cast at {pos}");
        }
    }
}
