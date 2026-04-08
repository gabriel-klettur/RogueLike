using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates a ground puddle that damages enemies standing in it with DoT.
    /// Mirrors Python's PuddleResolver with tick-based damage and optional burn status.
    /// </summary>
    public class PuddleExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 4f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.25f;
            float damagePerTick = ctx.Spell.damagePerTick;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 2f;

            Vector2 spawnPos = ctx.Spell.spawnAtMouse
                ? (Vector2)ctx.Caster.position + ctx.Direction * (ctx.Spell.range > 0 ? ctx.Spell.range / 16f : 5f)
                : (Vector2)ctx.Caster.position + ctx.Direction * dist;

            var puddleGo = new GameObject("SpellPuddle");
            puddleGo.transform.position = (Vector3)spawnPos;

            var sr = puddleGo.AddComponent<SpriteRenderer>();
            if (ctx.Spell.sprite != null)
            {
                sr.sprite = ctx.Spell.sprite;
            }
            else
            {
                sr.sprite = CreatePuddleSprite();
                Color puddleColor = !string.IsNullOrEmpty(ctx.Spell.element) && ctx.Spell.element == "lava"
                    ? new Color(1f, 0.47f, 0.24f, 0.6f)
                    : new Color(0.4f, 0.8f, 0.3f, 0.6f);
                sr.color = puddleColor;
            }
            sr.sortingLayerName = "FloorDecals";
            sr.sortingOrder = 5;
            puddleGo.transform.localScale = Vector3.one * (radius * 0.5f);

            var controller = puddleGo.AddComponent<PuddleController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, ctx.Spell.element);

            if (VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 0.47f, 0.24f, 0.6f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos, col, radius, 0.4f);
            }

            Debug.Log($"[SpellDebug] Puddle at {spawnPos}, r={radius:F1}, dur={duration:F1}s, dmg={damagePerTick}/tick, element={ctx.Spell.element}");
        }

        private static Sprite CreatePuddleSprite()
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
                    pixels[y * size + x] = dSq <= rSq ? Color.white : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
