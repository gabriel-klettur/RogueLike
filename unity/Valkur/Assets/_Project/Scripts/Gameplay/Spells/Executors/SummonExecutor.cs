using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Summons an allied unit at the target position.
    /// Mirrors Python's SummonResolver: spawns a monster entity with limited duration.
    /// Falls back to spawning a simple combat proxy when monster factory isn't available.
    /// </summary>
    public class SummonExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            int count = ctx.Spell.summonCount > 0 ? ctx.Spell.summonCount : 1;
            float duration = ctx.Spell.summonDuration > 0 ? ctx.Spell.summonDuration : 20f;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;

            Vector2 spawnPos = (Vector2)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell) + ctx.Direction * dist;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = count > 1 ? Random.insideUnitCircle * 1.5f : Vector2.zero;
                SpawnSummon(spawnPos + offset, duration, ctx);
            }

        }

        private void SpawnSummon(Vector2 pos, float duration, SpellContext ctx)
        {
            // Create a simple combat proxy summon
            var summonGo = new GameObject($"Summon_{ctx.Spell.summonTemplate}");
            summonGo.transform.position = (Vector3)pos;
            summonGo.layer = ctx.Caster.gameObject.layer;

            // Visual
            var sr = summonGo.AddComponent<SpriteRenderer>();
            if (ctx.Spell.sprite != null)
                sr.sprite = ctx.Spell.sprite;
            else
            {
                sr.sprite = CreateSummonSprite();
                sr.color = new Color(0.3f, 0.95f, 0.5f, 0.9f);
            }
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 10;
            float scale = ctx.Spell.scale > 0 ? ctx.Spell.scale : 1f;
            summonGo.transform.localScale = Vector3.one * scale;

            // Physics
            var rb = summonGo.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var col = summonGo.AddComponent<CircleCollider2D>();
            col.radius = 0.4f;

            // HP
            var health = summonGo.AddComponent<Health>();
            health.Initialize(50);

            // Auto-destroy controller
            var controller = summonGo.AddComponent<SummonController>();
            controller.Initialize(duration, ctx.Caster);

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnAreaIndicator((Vector3)pos,
                    new Color(0.3f, 0.95f, 0.5f, 0.5f), 1f, 0.4f);
            }
        }

        private static Sprite CreateSummonSprite()
        {
            int size = 24;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float rSq = (size / 2f - 1f) * (size / 2f - 1f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    pixels[y * size + x] = dx * dx + dy * dy <= rSq ? Color.white : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
