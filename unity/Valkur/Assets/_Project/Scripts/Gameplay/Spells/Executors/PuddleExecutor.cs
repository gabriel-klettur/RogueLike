using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates a persistent ground field that damages whatever stands in it and applies the
    /// spell's authored status effects.
    ///
    /// <para><b>radius is WORLD UNITS.</b> It used to be divided by 16 here — the Python
    /// pixel scale this game was ported from, the same one that shipped <c>wallWidth</c>,
    /// the totem radius, the vortex radius, <c>range</c> on three executors and
    /// <c>coneLength</c>. This was the last executor still carrying it. The tell was always
    /// the same: the fallback for an unauthored field (4 WORLD units) was sixteen times
    /// anything the asset could produce. Both shipped puddles were re-authored in world
    /// units in the same change; there is no compatibility shim, because a silent factor of
    /// sixteen is worse than a value that is obviously wrong.</para>
    /// </summary>
    public class PuddleExecutor : ISpellExecutor
    {
        /// <summary>
        /// The one field that is not a puddle. It used to be recognised by its
        /// <c>vfxPreset</c> reading "root_whip" — a preset that has never existed, because
        /// the field was being used as a behaviour switch rather than as a reference. The
        /// spell key is the discriminator that was always meant.
        /// </summary>
        private const string ROOT_WHIP_KEY = "root_whip";

        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : 4f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.25f;
            float damagePerTick = ctx.Spell.damagePerTick;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 2f;

            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(ctx, 5f, dist);

            bool rootWhip = string.Equals(ctx.Spell.spellKey, ROOT_WHIP_KEY,
                                          System.StringComparison.OrdinalIgnoreCase);

            var puddleGo = new GameObject(rootWhip ? "SpellRootField" : "SpellPuddle");
            puddleGo.transform.position = (Vector3)spawnPos;

            IGroundFieldVisual ownVisual = null;

            if (rootWhip)
            {
                // Its own rig, in Spells/Visuals/ with every other one. It used to be a
                // nested class in THIS file emitting a stretched-billboard particle at zero
                // velocity — measured, maxVelocity 0 across every live particle, which
                // makes the stretch axis undefined and Unity ignores particle rotation in
                // that render mode entirely. Three authored parameters (lengthScale,
                // startRotation, rotationOverLifetime) were inert, and nothing rose out of
                // the ground.
                ownVisual = RootWhipFX.Attach(puddleGo.transform, radius,
                                              ResolveSwatch(ctx.Spell));
                RootWhipAudio.PlaySproutAt(spawnPos);
            }
            else
            {
                // Default puddle visual: round splat decal under the shared disc rig.
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
            }

            var controller = puddleGo.AddComponent<PuddleController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, ctx.Spell.element, ctx.Caster != null ? ctx.Caster.gameObject : null,
                ProjectileExecutor.ResolveElement(ctx.Spell), ctx.Spell.statusApplications,
                ownVisual);

            // The root field draws its own ground ring, pinned to the damage radius; a
            // second orange halo over it would be a second, contradictory promise.
            if (!rootWhip && VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 0.47f, 0.24f, 0.6f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos, col, radius, 0.4f);
            }

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(puddleGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// The spell's own colour, with the project-wide "nobody authored this" sentinel
        /// handled. <see cref="RootPalette"/> tests opaque white, matching
        /// <c>KiPalette.IsUnauthored</c> and <c>SpellCastFlourishFX</c>; the older
        /// <c>Color.clear</c> test that used to stand here was unreachable, because no
        /// shipped spell has an alpha-zero swatch.
        /// </summary>
        private static Color ResolveSwatch(SpellDefinition spell)
        {
            return spell.particleColor;
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
