using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Places a healing totem at the target position that periodically heals nearby allies.
    /// Mirrors Python's TotemResolver (healing_totem: kind=heal).
    /// </summary>
    public class TotemExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 10f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 13.75f;
            float healPerTick = ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;

            Vector2 spawnPos = ctx.Spell.spawnAtMouse
                ? (Vector2)ctx.Caster.position + ctx.Direction * (ctx.Spell.range > 0 ? ctx.Spell.range / 16f : 5f)
                : (Vector2)ctx.Caster.position + ctx.Direction * dist;

            var totemGo = new GameObject("SpellTotem");
            totemGo.transform.position = (Vector3)spawnPos;

            // Totem visual: triangle/pillar shape
            var sr = totemGo.AddComponent<SpriteRenderer>();
            sr.sprite = CreateTotemSprite();
            sr.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 8;
            totemGo.transform.localScale = Vector3.one * 0.8f;

            // Collision
            var col = totemGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.7f);

            var controller = totemGo.AddComponent<TotemController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(healPerTick), tickPeriod, ctx.Caster);

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos,
                    new Color(1f, 0.9f, 0.3f, 0.4f), radius, 0.5f);
            }

            Debug.Log($"[SpellDebug] Totem placed at {spawnPos}, dur={duration:F1}s, r={radius:F1}, heal={healPerTick}/tick");
        }

        private static Sprite CreateTotemSprite()
        {
            int w = 24, h = 32;
            var tex = new Texture2D(w, h);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[w * h];
            // Triangle shape
            for (int y = 0; y < h; y++)
            {
                float halfWidth = (float)(h - y) / h * (w / 2f);
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - w / 2f);
                    pixels[y * w + x] = dx <= halfWidth ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f);
        }
    }
}
