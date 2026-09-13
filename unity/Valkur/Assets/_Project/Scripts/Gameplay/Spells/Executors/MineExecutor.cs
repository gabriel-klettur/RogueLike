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
            Vector2 pos = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);
            float armingTime = ctx.Spell.armingTime > 0 ? ctx.Spell.armingTime : 0.5f;
            // WORLD UNITS, for the reason MeteorExecutor now states at length: the divide by 16
            // is the Python pixel scale and the fallbacks (3.75 and 8.75 WORLD units) were
            // sixteen times what any authored value could reach. Shipped mine_basic authored
            // 3.75 / 8.75, which resolved to a trigger of 0.23 u and a blast of 0.55 u -- a
            // trap the player had to stand almost exactly on top of, under a ring drawn at a
            // constant 0.6 u that said nothing about either.
            float triggerRadius = ctx.Spell.triggerRadius > 0 ? ctx.Spell.triggerRadius : 1.5f;
            float explosionRadius = ctx.Spell.explosionRadius > 0 ? ctx.Spell.explosionRadius : 2.75f;
            float explosionDamage = SpellPower.Scale(
                ctx.Spell.explosionDamage > 0 ? ctx.Spell.explosionDamage : ctx.Spell.damage,
                ctx.Caster);
            // A mine's ttl is a cleanup timer, not a clock it animates against:
            // MineController animates from absolute Time.time and already owns a real
            // exit — proximity detonation. Infinite here just means "the trap waits".
            float ttl = ctx.Spell.infinite
                ? float.PositiveInfinity
                : (ctx.Spell.ttl > 0 ? ctx.Spell.ttl : 14f);

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
                ctx.Spell.impactPreset, ctx.Caster != null ? ctx.Caster.gameObject : null,
                ProjectileExecutor.ResolveElement(ctx.Spell), ctx.Spell.statusApplications);

        
            // Free-standing world object: nothing else can end it. The registry
            // enforces maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(mineGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
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
