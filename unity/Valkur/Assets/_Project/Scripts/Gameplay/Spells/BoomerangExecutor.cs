using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a boomerang projectile that travels out then returns to the caster.
    /// Uses ProjectilePrefab (should have BoomerangProjectile component).
    /// Mirrors Python's BoomerangResolver: pass-through, range, returnSpeed in extras.
    /// </summary>
    public class BoomerangExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            if (ctx.ProjectilePrefab == null)
            {
                Debug.LogWarning("[BoomerangExecutor] ProjectilePrefab is null; cannot spawn boomerang.");
                return;
            }

            Vector3 spawnPos = ctx.Caster.position + (Vector3)(ctx.Direction * 0.4f);
            var go = Object.Instantiate(ctx.ProjectilePrefab, spawnPos, Quaternion.identity);
            go.SetActive(true);

            // Ensure BoomerangProjectile component is present
            var boom = go.GetComponent<BoomerangProjectile>();
            if (boom == null)
                boom = go.AddComponent<BoomerangProjectile>();

            // Defaults matching Python config fields
            float speed      = ctx.Spell.speed > 0 ? ctx.Spell.speed : 8f;
            float range      = ctx.Spell.range > 0 ? ctx.Spell.range : 6f;
            float hitRadius  = ctx.Spell.hitRadius > 0 ? ctx.Spell.hitRadius : 0.25f;
            float returnSpd  = speed; // same speed back unless overridden
            bool passes      = false; // conservative default

            Color col = ctx.Spell.particleColor != Color.clear
                ? ctx.Spell.particleColor
                : new Color(0.3f, 0.7f, 1f, 1f);

            boom.Initialize(ctx.Caster, ctx.Direction, speed, returnSpd,
                            ctx.Spell.damage, range, hitRadius, passes,
                            ctx.TargetLayers, col);

            if (ctx.Spell.sprite != null)
            {
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr != null) sr.sprite = ctx.Spell.sprite;
            }

            // Procedural epic visual: spinning blade halo + green ember trail.
            if (go.GetComponent<IProjectileVisual>() == null)
            {
                var v = go.AddComponent<ElementalProjectileVisual>();
                v.SetElement(SpellElement.Boomerang);
            }

            // Audio cue
            var audio = Valkur.Core.ServiceLocator.Get<Valkur.Core.IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_boomerang_throw");

            if (!string.IsNullOrEmpty(ctx.Spell.vfxPreset) && VFXManager.Instance != null)
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, spawnPos);
        }
    }
}
