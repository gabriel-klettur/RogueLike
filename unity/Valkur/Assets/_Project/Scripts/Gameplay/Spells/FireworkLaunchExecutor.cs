using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Launches a firework projectile toward the cast direction.
    /// Mirrors Python's FireworkLaunchResolver.
    /// Reuses ProjectileExecutor internally with firework VFX on impact.
    /// </summary>
    public class FireworkLaunchExecutor : ISpellExecutor
    {
        private static readonly ProjectileExecutor _projExecutor = new ProjectileExecutor();

        public void Execute(SpellContext ctx)
        {
            // Use projectile executor for the launch physics
            _projExecutor.Execute(ctx);

            // Firework burst VFX at caster (launch VFX)
            if (VFXManager.Instance != null)
            {
                Color launchColor = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 0.8f, 0.2f, 0.7f);
                VFXManager.Instance.SpawnImpact(ctx.Caster.position, launchColor, 0.3f, 0.5f);
            }

            Debug.Log($"[SpellDebug] FireworkLaunch from {ctx.Caster.position} dir={ctx.Direction}");
        }
    }
}
