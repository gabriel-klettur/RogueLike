using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spawns a blocking wall perpendicular to the casterâ†’mouse direction.
    /// Mirrors Python's WallResolver: wall_ice with HP, blocks projectiles/units.
    /// </summary>
    public class WallExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            float width = ctx.Spell.wallWidth > 0 ? ctx.Spell.wallWidth / 32f : 6f;
            float height = ctx.Spell.wallHeight > 0 ? ctx.Spell.wallHeight / 32f : 1.5f;
            float hp = ctx.Spell.wallHP > 0 ? ctx.Spell.wallHP : 100f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 6f;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;

            Vector2 spawnPos = (Vector2)ctx.Caster.position + ctx.Direction * dist;
            float angle = Mathf.Atan2(ctx.Direction.y, ctx.Direction.x) * Mathf.Rad2Deg;

            var wallGo = new GameObject("SpellWall");
            wallGo.transform.position = (Vector3)spawnPos;
            // Wall is perpendicular to cast direction
            wallGo.transform.rotation = Quaternion.Euler(0, 0, angle + 90f);

            // Visual
            var sr = wallGo.AddComponent<SpriteRenderer>();
            if (ctx.Spell.sprite != null)
            {
                sr.sprite = ctx.Spell.sprite;
            }
            else
            {
                sr.sprite = CreateWallSprite();
                sr.color = new Color(0.6f, 0.85f, 1f, 0.9f);
            }
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 5;
            wallGo.transform.localScale = new Vector3(width, height, 1f);

            // Collision
            var col = wallGo.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;
            wallGo.layer = LayerMask.NameToLayer("Building") != -1
                ? LayerMask.NameToLayer("Building") : 14;

            // Destroyable health
            var wallHealth = wallGo.AddComponent<Health>();
            wallHealth.Initialize(Mathf.RoundToInt(hp));

            // Auto-destroy
            var controller = wallGo.AddComponent<WallController>();
            controller.Initialize(duration, wallHealth);

            if (VFXManager.Instance != null)
            {
                Color col2 = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.6f, 0.85f, 1f, 0.6f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos, col2, width * 0.5f, 0.3f);
            }

        }

        private static Sprite CreateWallSprite()
        {
            int w = 32, h = 8;
            var tex = new Texture2D(w, h);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
