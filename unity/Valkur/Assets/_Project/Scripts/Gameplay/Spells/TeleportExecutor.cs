using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Teleports the caster in the cast direction by spell.distance world units.
    /// Spawns a VFX indicator at both the origin and destination.
    /// Mirrors Python's TeleportResolver: direction * distance, lifespan VFX.
    /// </summary>
    public class TeleportExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 4f;
            Vector2 origin = ctx.Caster.position;
            Vector2 destination = origin + ctx.Direction * dist;

            // Snap destination away from walls using Physics2D sweep
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                // Cast from mid-point toward destination to find blocking geometry
                const int blockingMask = (1 << 11) | (1 << 14); // World + Building
                var hit = Physics2D.CircleCast(origin, 0.3f, ctx.Direction, dist, blockingMask);
                if (hit.collider != null)
                    destination = origin + ctx.Direction * Mathf.Max(0f, hit.distance - 0.4f);

                rb.MovePosition(destination);
            }
            else
            {
                ctx.Caster.position = (Vector3)destination;
            }

            if (VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.4f, 0.2f, 1f, 0.7f);
                // Flash at departure and arrival
                VFXManager.Instance.SpawnAreaIndicator((Vector3)origin,      col, 0.6f, 0.25f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)destination, col, 0.6f, 0.35f);

                if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                    VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)destination);
            }
        }
    }
}
