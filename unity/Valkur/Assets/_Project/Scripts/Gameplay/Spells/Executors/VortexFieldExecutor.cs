using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates a vortex force field that pulls or pushes enemies.
    /// Mirrors Python's VortexFieldResolver with force modes: pull, push.
    /// </summary>
    public class VortexFieldExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            // Values are already in world units (converted by SpellDataImporter)
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : 17.5f;
            float force = ctx.Spell.force > 0 ? ctx.Spell.force : 87.5f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 2f;
            bool isPull = string.IsNullOrEmpty(ctx.Spell.forceMode) || ctx.Spell.forceMode == "pull";
            bool followCaster = ctx.Spell.followCaster;

            // Spawn position: pull at mouse (distance), push at caster
            Vector2 spawnPos;
            if (ctx.Spell.spawnAtMouse || isPull)
            {
                float dist = ctx.Spell.range > 0 ? ctx.Spell.range : 6f;
                spawnPos = (Vector2)ctx.Caster.position + ctx.Direction * dist;
            }
            else
            {
                spawnPos = ctx.Caster.position;
            }

            var vortexGo = new GameObject(isPull ? "VortexPull" : "VortexPush");
            vortexGo.transform.position = (Vector3)spawnPos;

            // Visual
            var sr = vortexGo.AddComponent<SpriteRenderer>();
            sr.sprite = CreateVortexSprite();
            sr.color = isPull
                ? new Color(0.3f, 0.4f, 1f, 0.3f)
                : new Color(1f, 0.4f, 0.3f, 0.3f);
            sr.sortingLayerName = "VFX";
            sr.sortingOrder = 1;
            vortexGo.transform.localScale = Vector3.one * (radius * 0.4f);

            var controller = vortexGo.AddComponent<VortexFieldController>();
            controller.Initialize(duration, radius, force, isPull, followCaster ? ctx.Caster : null, ctx.TargetLayers);

            // VFX: spawn vortex particle preset
            var vfxService = VFXManager.Instance as IVFXService;
            if (vfxService != null)
            {
                string preset = !string.IsNullOrEmpty(ctx.Spell.vfxPreset) ? ctx.Spell.vfxPreset : "vortex_dark";
                vfxService.SpawnParticlePreset(preset, spawnPos, duration);
            }

            Debug.Log($"[SpellDebug] VortexField ({(isPull ? "pull" : "push")}) at {spawnPos}, r={radius:F1}, force={force:F0}, dur={duration:F1}s, follow={followCaster}");
        }

        private static Sprite CreateVortexSprite()
        {
            int size = 64;
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
                    // Spiral pattern
                    float angle = Mathf.Atan2(dy, dx);
                    float spiral = Mathf.Sin(angle * 3f + Mathf.Sqrt(dSq) * 0.5f) * 0.3f + 0.5f;
                    pixels[y * size + x] = t > 0 ? new Color(1f, 1f, 1f, t * spiral) : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
