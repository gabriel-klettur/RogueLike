using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates an invulnerability sphere around the caster for a duration.
    /// Mirrors Python's SphereMagicShieldResolver.
    /// </summary>
    public class ShieldExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 5f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 5f;

            // Create shield visual child object
            var shieldGo = new GameObject("SpellShield");
            shieldGo.transform.SetParent(ctx.Caster, false);
            shieldGo.transform.localPosition = Vector3.zero;

            var sr = shieldGo.AddComponent<SpriteRenderer>();
            sr.sprite = CreateShieldSprite();
            sr.color = new Color(0.3f, 0.5f, 1f, 0.35f);
            sr.sortingLayerName = "VFX";
            sr.sortingOrder = 3;
            shieldGo.transform.localScale = Vector3.one * radius * 0.5f;

            var controller = shieldGo.AddComponent<ShieldController>();
            controller.Initialize(duration, ctx.Caster);

            if (VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.3f, 0.5f, 1f, 0.5f);
                VFXManager.Instance.SpawnAreaIndicator(ctx.Caster.position, col, radius * 0.5f, 0.4f);
            }

        }

        private static Sprite CreateShieldSprite()
        {
            int size = 64;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float outerSq = (size / 2f) * (size / 2f);
            float innerSq = (size / 2f - 4f) * (size / 2f - 4f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    float dSq = dx * dx + dy * dy;
                    if (dSq <= outerSq && dSq >= innerSq)
                        pixels[y * size + x] = new Color(1f, 1f, 1f, 0.8f);
                    else if (dSq < innerSq)
                        pixels[y * size + x] = new Color(1f, 1f, 1f, 0.15f);
                    else
                        pixels[y * size + x] = Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
