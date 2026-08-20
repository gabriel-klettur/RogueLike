using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates a persistent arcane flame zone that damages nearby enemies.
    /// Mirrors Python's ArcaneFlameResolver.
    /// </summary>
    public class ArcaneFlameExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 5f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 8f;
            float damagePerTick = ctx.Spell.damagePerTick > 0 ? ctx.Spell.damagePerTick : 5f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;

            Vector2 pos = (Vector2)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell) + ctx.Direction * 2f;

            var flameGo = new GameObject("ArcaneFlame");
            flameGo.transform.position = (Vector3)pos;

            var sr = flameGo.AddComponent<SpriteRenderer>();
            sr.sprite = CreateFlameSprite();
            sr.color = new Color(0.6f, 0.2f, 0.9f, 0.4f);
            sr.sortingLayerName = "VFX";
            sr.sortingOrder = 2;
            flameGo.transform.localScale = Vector3.one * (radius * 0.4f);

            var controller = flameGo.AddComponent<ArcaneFlameController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod, ctx.TargetLayers);

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)pos, duration);

        
            // Free-standing world object: nothing else can end it. The registry
            // enforces maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(flameGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
}

        private static Sprite CreateFlameSprite()
        {
            int size = 48;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float rSq = center * center;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    float dSq = dx * dx + dy * dy;
                    float t = 1f - (dSq / rSq);
                    pixels[y * size + x] = t > 0 ? new Color(1f, 1f, 1f, t * 0.6f) : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
