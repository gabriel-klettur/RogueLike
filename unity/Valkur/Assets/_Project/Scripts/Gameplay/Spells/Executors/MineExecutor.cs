using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Places an armed mine that detonates on enemy proximity.
    /// Mirrors Python's MineResolver: arming time â†’ proximity trigger â†’ explosion.
    /// </summary>
    public class MineExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            Vector2 pos = ctx.Caster.position;
            float armingTime = ctx.Spell.armingTime > 0 ? ctx.Spell.armingTime : 0.5f;
            float triggerRadius = ctx.Spell.triggerRadius > 0 ? ctx.Spell.triggerRadius / 16f : 3.75f;
            float explosionRadius = ctx.Spell.explosionRadius > 0 ? ctx.Spell.explosionRadius / 16f : 8.75f;
            float explosionDamage = ctx.Spell.explosionDamage > 0 ? ctx.Spell.explosionDamage : ctx.Spell.damage;
            float ttl = ctx.Spell.ttl > 0 ? ctx.Spell.ttl : 14f;

            var mineGo = new GameObject("SpellMine");
            mineGo.transform.position = (Vector3)pos;

            // Visual
            var sr = mineGo.AddComponent<SpriteRenderer>();
            if (ctx.Spell.sprite != null)
                sr.sprite = ctx.Spell.sprite;
            else
            {
                sr.sprite = CreateMineSprite();
                sr.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
            }
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 2;
            float visualScale = ctx.Spell.scale > 0 ? ctx.Spell.scale : 0.5f;
            mineGo.transform.localScale = Vector3.one * visualScale;

            var controller = mineGo.AddComponent<MineController>();
            controller.Initialize(armingTime, triggerRadius, explosionRadius,
                Mathf.RoundToInt(explosionDamage), ttl, ctx.TargetLayers,
                ctx.Spell.impactPreset);

        }

        private static Sprite CreateMineSprite()
        {
            int size = 16;
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
