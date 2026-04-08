using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes a healing aura: creates a lingering area that heals allies within radius.
    /// Mirrors Python's AuraResolver with heal_per_second buff.
    /// </summary>
    public class AuraExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 1f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 0.625f;
            float healPerTick = ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick : 20f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;

            var auraGo = new GameObject("SpellAura");
            auraGo.transform.SetParent(ctx.Caster, false);
            auraGo.transform.localPosition = Vector3.zero;

            // Visual ring
            var sr = auraGo.AddComponent<SpriteRenderer>();
            sr.sprite = CreateAuraSprite();
            sr.color = new Color(0.2f, 0.9f, 0.3f, 0.3f);
            sr.sortingLayerName = "VFX";
            sr.sortingOrder = 1;
            auraGo.transform.localScale = Vector3.one * radius;

            var controller = auraGo.AddComponent<AuraController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(healPerTick), tickPeriod, ctx.Caster);

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, ctx.Caster.position, duration);

            Debug.Log($"[SpellDebug] HealingAura on {ctx.Caster.name}, dur={duration:F1}s, r={radius:F1}, heal={healPerTick}/tick");
        }

        private static Sprite CreateAuraSprite()
        {
            int size = 64;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float outerSq = center * center;
            float innerSq = (center - 3f) * (center - 3f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    float dSq = dx * dx + dy * dy;
                    if (dSq <= outerSq)
                    {
                        float alpha = dSq >= innerSq ? 0.8f : 0.2f;
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                        pixels[y * size + x] = Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
