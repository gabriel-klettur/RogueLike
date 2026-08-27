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
        /// <summary>
        /// The one puddle that is not a puddle. It used to be recognised by its
        /// <c>vfxPreset</c> reading "root_whip" — a preset that has never existed, because
        /// the field was being used as a behaviour switch rather than as a reference. That
        /// left a permanently unresolved preset reference in the catalog, indistinguishable
        /// from a real typo. The spell key is the discriminator that was always meant.
        /// </summary>
        private const string ROOT_WHIP_KEY = "root_whip";

        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 4f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.25f;
            float damagePerTick = ctx.Spell.damagePerTick;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 2f;

            Vector2 spawnPos = ctx.Spell.spawnAtMouse
                ? (Vector2)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell) + ctx.Direction * (ctx.Spell.range > 0 ? ctx.Spell.range / 16f : 5f)
                : (Vector2)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell) + ctx.Direction * dist;

            bool rootWhip = string.Equals(ctx.Spell.spellKey, ROOT_WHIP_KEY,
                                          System.StringComparison.OrdinalIgnoreCase);

            var puddleGo = new GameObject("SpellPuddle");
            puddleGo.transform.position = (Vector3)spawnPos;

            if (!rootWhip)
            {
                // Default puddle visual: round splat decal + orange area indicator.
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
            else
            {
                // Root Whip — tendrils rising from the ground in a circular area.
                Color tendrilColor = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(0.30f, 0.55f, 0.20f, 1f);
                RootWhipFX.AttachTo(puddleGo, radius, tendrilColor);
            }

            var controller = puddleGo.AddComponent<PuddleController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, ctx.Spell.element, ctx.Caster != null ? ctx.Caster.gameObject : null,
                ProjectileExecutor.ResolveElement(ctx.Spell), ctx.Spell.statusApplications);

            // Default puddle gets an orange ground halo for visibility; root whip
            // is already busy enough with rising tendrils — skip the halo there.
            if (!rootWhip && VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 0.47f, 0.24f, 0.6f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos, col, radius, 0.4f);
            }

        
            // Free-standing world object: nothing else can end it. The registry
            // enforces maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(puddleGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
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

    /// <summary>
    /// Particle-based "roots whipping out of the ground" visual for spells that
    /// flag <c>vfxPreset == "root_whip"</c>. Attaches a single ParticleSystem to
    /// the puddle GO that emits short-lived vertical tendrils inside a circle of
    /// the given radius. Tendrils grow fast, sway, then fade — looks like a
    /// patch of writhing roots when emitted continuously.
    /// </summary>
    internal static class RootWhipFX
    {
        public static void AttachTo(GameObject host, float radius, Color color)
        {
            ElementalSprites.EnsureAll();
            var ps = host.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            // Particles don't translate via main.startSpeed — sizeOverLifetime grows
            // them upward via an axis-stretched billboard so they read as rooting.
            main.startSpeed = 0f;
            // X-size is tendril thickness; Y is overridden via a 3D start scale.
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.18f, radius * 0.30f);
            main.startSize3D = false;
            main.startColor = color;
            // Slight side-tilt so each tendril leans differently.
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 16f;     // continuous, dense enough for a "field"

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.05f, radius * 0.85f);
            shape.radiusThickness = 1f;
            shape.randomDirectionAmount = 0f;

            // Size grows fast (root bursting up), holds, then collapses back.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f,    0.10f),
                    new Keyframe(0.20f, 1.20f),
                    new Keyframe(0.65f, 1.00f),
                    new Keyframe(1f,    0.20f)));

            // Rotate slightly during life — the tendril visibly sways.
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            // Fade alpha at the start (sprout) and end (retract).
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            // Earthy colour ramp: dark soil → spell colour → fade out.
            var dark = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.30f, 1f);
            grad.SetKeys(
                new[] {
                    new GradientColorKey(dark,   0f),
                    new GradientColorKey(color,  0.40f),
                    new GradientColorKey(color,  0.85f)
                },
                new[] {
                    new GradientAlphaKey(0f,   0f),
                    new GradientAlphaKey(1f,   0.20f),
                    new GradientAlphaKey(1f,   0.75f),
                    new GradientAlphaKey(0f,   1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
                renderer.sortingOrder = 8;
                // Stretch billboard so each particle is taller than wide — sells the
                // vertical tendril shape regardless of camera angle.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 3.5f;     // taller
                renderer.velocityScale = 0f;     // no velocity-based stretch
                var sprite = ElementalSprites.Wisp != null
                    ? ElementalSprites.Wisp
                    : ElementalSprites.Glow;
                if (sprite != null)
                {
                    // Shared per (texture, blend) instead of one material per puddle,
                    // none of which were ever destroyed.
                    renderer.sharedMaterial =
                        Valkur.Gameplay.VFX.ParticleMaterialCache.Get(sprite.texture, additive: false);
                }
            }

            ps.Play();
        }
    }
}
