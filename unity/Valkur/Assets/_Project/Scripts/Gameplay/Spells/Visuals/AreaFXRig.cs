using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Shared visual rig for persistent ground/area spells (puddle, smoke, vortex,
    /// totem, wall, aura). Builds: ground rune (rotating), halo, glow, optional core,
    /// dynamic Light2D, and a ParticleSystem. Tuned via <see cref="AreaPalette"/>.
    /// </summary>
    public class AreaFXRig
    {
        public SpriteRenderer Rune;
        public SpriteRenderer Halo;
        public SpriteRenderer Glow;
        public SpriteRenderer Core;
        public ParticleSystem Particles;
        public GameObject LightGo;
        public Component Light;

        public AreaPalette Palette;

        public static AreaFXRig Attach(Transform parent, AreaPalette palette, float radius)
        {
            ElementalSprites.EnsureAll();
            var rig = new AreaFXRig { Palette = palette };

            string layer = palette.useFloor ? Valkur.Core.SortingConfig.LAYER_FLOOR_DECALS : Valkur.Core.SortingConfig.LAYER_VFX;

            if (palette.haloSprite != null)
                rig.Halo = MakeChild(parent, "Halo", palette.haloSprite, palette.haloColor, palette.haloScale, layer, 50);
            if (palette.runeSprite != null)
                rig.Rune = MakeChild(parent, "Rune", palette.runeSprite, palette.runeColor, palette.runeScale, layer, 51);
            if (palette.glowSprite != null)
                rig.Glow = MakeChild(parent, "Glow", palette.glowSprite, palette.glowColor, palette.glowScale, layer, 52);
            if (palette.coreSprite != null)
                rig.Core = MakeChild(parent, "Core", palette.coreSprite, palette.coreColor, palette.coreScale, layer, 53);

            if (palette.particleEnabled)
                rig.BuildParticles(parent, radius, layer);

            if (palette.lightEnabled)
                rig.AttachLight(parent, radius);

            return rig;
        }

        private static SpriteRenderer MakeChild(Transform parent, string name, Sprite sprite, Color color,
            float scale, string layer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void BuildParticles(Transform parent, float radius, string layer)
        {
            var go = new GameObject("Particles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            Particles = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately (playOnAwake defaults to true),
            // and `main.duration` is one of the fields Unity refuses to change on a
            // playing system: it fires "Setting the duration while system is still
            // playing is not supported" and keeps the old value. Stop first, configure,
            // Play at the end — the same order every other emitter builder here uses.
            Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = Particles.main;
            main.playOnAwake = false;
            main.duration = 999f;
            main.loop = true;
            main.startLifetime = Palette.particleLife;
            main.startSpeed = new ParticleSystem.MinMaxCurve(Palette.particleSpeedMin, Palette.particleSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(Palette.particleSizeMin, Palette.particleSizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(Palette.particleColorA, Palette.particleColorB);
            main.gravityModifier = Palette.particleGravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            var emission = Particles.emission;
            emission.rateOverTime = Palette.particleRate;

            var shape = Particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.2f, radius * 0.4f);
            shape.radiusThickness = 1f;

            var col = Particles.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Palette.particleColorA, 0f),
                    new GradientColorKey(Palette.particleColorB, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.85f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            // ── Growth ──────────────────────────────────────────────────────────
            // A cloud that never changes size reads as a stamp, not as something
            // expanding into the air.
            var sol = Particles.sizeOverLifetime;
            if (Palette.particleGrowEnabled)
            {
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, Palette.particleGrowFrom),
                    new Keyframe(0.35f, Palette.particleGrowPeak),
                    new Keyframe(1f, Palette.particleGrowTo)));
            }
            else
            {
                sol.enabled = false;
            }

            // ── Turbulence ──────────────────────────────────────────────────────
            var noise = Particles.noise;
            if (Palette.particleNoiseStrength > 0f)
            {
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(Palette.particleNoiseStrength);
                noise.frequency = Mathf.Max(0.0001f, Palette.particleNoiseFrequency);
                noise.damping = true;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.25f);
            }
            else
            {
                noise.enabled = false;
            }

            // ── Flipbook ────────────────────────────────────────────────────────
            var tsa = Particles.textureSheetAnimation;
            Texture flipTex = null;
            if (Palette.particleFlipbook != null && Palette.particleFlipbook.Length > 0)
            {
                for (int i = tsa.spriteCount - 1; i >= 0; i--) tsa.RemoveSprite(i);
                int added = 0;
                for (int i = 0; i < Palette.particleFlipbook.Length; i++)
                {
                    var f = Palette.particleFlipbook[i];
                    if (f == null) continue;
                    tsa.AddSprite(f);
                    if (flipTex == null) flipTex = f.texture;
                    added++;
                }
                if (added > 0)
                {
                    tsa.enabled = true;
                    tsa.mode = ParticleSystemAnimationMode.Sprites;
                    tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
                    tsa.cycleCount = Mathf.Max(1, Palette.particleFlipbookCycles);
                    tsa.startFrame = new ParticleSystem.MinMaxCurve(0f);
                }
                else { tsa.enabled = false; }
            }
            else
            {
                tsa.enabled = false;
            }

            var psr = Particles.GetComponent<ParticleSystemRenderer>();
            // A flipbook needs the atlas page its frames were packed onto; the shared unlit
            // material samples a different texture and would draw the frames as blank quads.
            psr.sharedMaterial = flipTex != null
                ? ParticleMaterialCache.Get(flipTex, false)
                : ElementalSprites.SharedUnlitMaterial;
            psr.sortingLayerID = SortingLayer.NameToID(layer);
            psr.sortingLayerName = layer;
            psr.sortingOrder = 60;

            Particles.Play();
        }

        private void AttachLight(Transform parent, float radius)
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;
            LightGo = new GameObject("AreaLight");
            LightGo.transform.SetParent(parent, false);
            LightGo.transform.localPosition = Vector3.zero;
            try
            {
                Light = LightGo.AddComponent(l2dType);
                var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lt != null) lt.SetValue(Light, System.Enum.ToObject(lt.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(Light, Palette.lightColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(Light, Palette.lightIntensity);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(Light, Mathf.Max(0.5f, radius * Palette.lightOuterMul));
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(Light, Mathf.Max(0.1f, radius * 0.2f));
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(Light, 0.85f);
            }
            catch { Light = null; }
        }

        public void SetIntensity(float intensity)
        {
            if (Light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(Light, intensity); }
            catch { }
        }

        public void SetGlobalAlpha(float a)
        {
            if (Rune != null) { var c = Rune.color; c.a = Palette.runeColor.a * a; Rune.color = c; }
            if (Halo != null) { var c = Halo.color; c.a = Palette.haloColor.a * a; Halo.color = c; }
            if (Glow != null) { var c = Glow.color; c.a = Palette.glowColor.a * a; Glow.color = c; }
            if (Core != null) { var c = Core.color; c.a = Palette.coreColor.a * a; Core.color = c; }
        }

        /// <summary>
        /// Stop emitting while letting every particle already in the air finish its life.
        /// Returns how long the caller must wait before destroying the rig's GameObject.
        ///
        /// Destroying the object outright kills the live particles mid-flight, which is why
        /// smoke used to vanish on a frame boundary instead of drifting apart.
        /// </summary>
        public float StopEmitting()
        {
            if (Particles != null)
                Particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            return Palette.particleLife;
        }

        public void Destroy()
        {
            if (LightGo != null) Object.Destroy(LightGo);
            LightGo = null;
            Light = null;
        }
    }

    /// <summary>Visual palette/preset for an <see cref="AreaFXRig"/>.</summary>
    public struct AreaPalette
    {
        public Sprite runeSprite, haloSprite, glowSprite, coreSprite;
        public Color  runeColor, haloColor, glowColor, coreColor;
        public float  runeScale, haloScale, glowScale, coreScale;
        public float  runeSpinSpeed;
        public bool   useFloor;            // sort under entities (puddle/totem/aura) vs over (smoke/vortex)
        public bool   particleEnabled;
        public Color  particleColorA, particleColorB;
        public float  particleLife, particleSpeedMin, particleSpeedMax;
        public float  particleSizeMin, particleSizeMax;
        public float  particleRate, particleGravity;

        // ── Optional particle detail ────────────────────────────────────────────
        // All default to off so the palettes written before these existed (LavaPuddle,
        // HealingTotem, VortexPull, …) keep rendering exactly as they did.

        /// <summary>Drive sizeOverLifetime. Off = particles keep their birth size forever.</summary>
        public bool   particleGrowEnabled;
        /// <summary>Size multipliers at t=0, t=0.35 and t=1 when growth is enabled.</summary>
        public float  particleGrowFrom, particleGrowPeak, particleGrowTo;

        /// <summary>World-unit turbulence. 0 = no noise module.</summary>
        public float  particleNoiseStrength;
        /// <summary>Low = slow broad billowing, high = fast fine jitter.</summary>
        public float  particleNoiseFrequency;

        /// <summary>Animation frames played over each particle's life. Null = static texture.</summary>
        public Sprite[] particleFlipbook;
        /// <summary>How many times the flipbook repeats per particle lifetime.</summary>
        public int    particleFlipbookCycles;

        public bool   lightEnabled;
        public Color  lightColor;
        public float  lightIntensity, lightOuterMul;

        // ── Presets ───────────────────────────────────────────────────

        public static AreaPalette LavaPuddle()
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = ElementalSprites.Ring,   runeColor = new Color(1f, 0.55f, 0.10f, 0.85f), runeScale = 1.45f, runeSpinSpeed = 25f,
                haloSprite = ElementalSprites.Halo,   haloColor = new Color(0.85f, 0.20f, 0.05f, 0.30f), haloScale = 1.80f,
                glowSprite = ElementalSprites.Glow,   glowColor = new Color(1f, 0.40f, 0.05f, 0.55f), glowScale = 1.05f,
                coreSprite = ElementalSprites.Core,   coreColor = new Color(1f, 0.85f, 0.30f, 0.85f), coreScale = 0.50f,
                useFloor = true,
                particleEnabled = true,
                particleColorA = new Color(1f, 0.85f, 0.30f, 1f),
                particleColorB = new Color(1f, 0.30f, 0.05f, 1f),
                particleLife = 0.9f, particleSpeedMin = 0.5f, particleSpeedMax = 1.4f,
                particleSizeMin = 0.05f, particleSizeMax = 0.12f,
                particleRate = 25f, particleGravity = -0.3f,
                lightEnabled = true, lightColor = new Color(1f, 0.55f, 0.20f, 1f),
                lightIntensity = 1.6f, lightOuterMul = 1.4f,
            };
        }

        /// <summary>
        /// Smoke: a mass that occludes rather than emits. Built from two Wisp silhouettes
        /// (anisotropic, so the cloud reads as billowing upward instead of as a disc) over
        /// a soft halo, plus large slow particles that keep expanding as they thin out.
        ///
        /// The light is deliberately dim and cold: smoke does not glow, but a cloud that
        /// receives no light at all reads as a hole punched in the scene. This is the amount
        /// that makes it look like haze catching the ambient, and no more.
        /// </summary>
        /// <param name="flipbook">
        /// Optional animation frames for the particles, supplied by the caster from the
        /// particle preset asset so the frames stay designer-authored data rather than a
        /// hardcoded Resources path. Null falls back to the procedural smoke texture.
        /// </param>
        public static AreaPalette Smoke(Sprite[] flipbook = null)
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = null,
                haloSprite = ElementalSprites.Halo, haloColor = new Color(0.52f, 0.53f, 0.58f, 0.34f), haloScale = 1.85f,
                glowSprite = ElementalSprites.Wisp, glowColor = new Color(0.66f, 0.67f, 0.72f, 0.42f), glowScale = 1.55f,
                coreSprite = null,
                useFloor = false,
                particleEnabled = true,
                particleColorA = new Color(0.82f, 0.82f, 0.86f, 1f),
                particleColorB = new Color(0.34f, 0.34f, 0.40f, 1f),
                particleLife = 1.9f, particleSpeedMin = 0.25f, particleSpeedMax = 0.75f,
                particleSizeMin = 0.42f, particleSizeMax = 0.95f,
                particleRate = 16f, particleGravity = -0.35f,
                // Smoke thins, it never shrinks — the tail multiplier stays above the peak
                // so a dying puff is the widest and faintest thing on screen.
                particleGrowEnabled = true,
                particleGrowFrom = 0.45f, particleGrowPeak = 1.0f, particleGrowTo = 1.35f,
                particleNoiseStrength = 0.38f, particleNoiseFrequency = 0.32f,
                particleFlipbook = flipbook, particleFlipbookCycles = 1,
                lightEnabled = true, lightColor = new Color(0.62f, 0.66f, 0.78f, 1f),
                lightIntensity = 0.38f, lightOuterMul = 1.7f,
            };
        }

        public static AreaPalette VortexPull()
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = ElementalSprites.Ring, runeColor = new Color(0.45f, 0.20f, 0.85f, 0.90f), runeScale = 1.55f, runeSpinSpeed = 200f,
                haloSprite = ElementalSprites.Halo, haloColor = new Color(0.30f, 0.10f, 0.65f, 0.35f), haloScale = 1.85f,
                glowSprite = ElementalSprites.Glow, glowColor = new Color(0.55f, 0.25f, 1.00f, 0.55f), glowScale = 1.10f,
                coreSprite = ElementalSprites.HotCore, coreColor = new Color(0.85f, 0.55f, 1f, 1f), coreScale = 0.30f,
                useFloor = false,
                particleEnabled = true,
                particleColorA = new Color(0.85f, 0.55f, 1f, 1f),
                particleColorB = new Color(0.35f, 0.15f, 0.85f, 1f),
                particleLife = 0.6f, particleSpeedMin = 1.5f, particleSpeedMax = 2.5f,
                particleSizeMin = 0.05f, particleSizeMax = 0.10f,
                particleRate = 40f, particleGravity = 0f,
                lightEnabled = true, lightColor = new Color(0.65f, 0.30f, 1f, 1f),
                lightIntensity = 1.4f, lightOuterMul = 1.2f,
            };
        }

        public static AreaPalette VortexPush()
        {
            var p = VortexPull();
            p.runeColor = new Color(1f, 0.55f, 0.20f, 0.90f);
            p.haloColor = new Color(0.85f, 0.30f, 0.10f, 0.35f);
            p.glowColor = new Color(1f, 0.45f, 0.10f, 0.55f);
            p.coreColor = new Color(1f, 0.85f, 0.40f, 1f);
            p.runeSpinSpeed = -200f;
            p.particleColorA = new Color(1f, 0.85f, 0.40f, 1f);
            p.particleColorB = new Color(1f, 0.40f, 0.10f, 1f);
            p.lightColor = new Color(1f, 0.55f, 0.20f, 1f);
            return p;
        }

        public static AreaPalette HealingTotem()
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = ElementalSprites.Ring, runeColor = new Color(0.30f, 1f, 0.45f, 0.85f), runeScale = 1.50f, runeSpinSpeed = 30f,
                haloSprite = ElementalSprites.Halo, haloColor = new Color(0.20f, 0.85f, 0.45f, 0.30f), haloScale = 1.80f,
                glowSprite = ElementalSprites.Glow, glowColor = new Color(0.40f, 1f, 0.55f, 0.55f), glowScale = 1.10f,
                coreSprite = ElementalSprites.HotCore, coreColor = new Color(0.85f, 1f, 0.90f, 1f), coreScale = 0.35f,
                useFloor = true,
                particleEnabled = true,
                particleColorA = new Color(0.65f, 1f, 0.75f, 1f),
                particleColorB = new Color(0.30f, 0.85f, 0.45f, 1f),
                particleLife = 1.0f, particleSpeedMin = 0.5f, particleSpeedMax = 1.2f,
                particleSizeMin = 0.05f, particleSizeMax = 0.10f,
                particleRate = 20f, particleGravity = -0.5f,
                lightEnabled = true, lightColor = new Color(0.55f, 1f, 0.65f, 1f),
                lightIntensity = 1.5f, lightOuterMul = 1.3f,
            };
        }

        public static AreaPalette IceWall()
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = ElementalSprites.Ring, runeColor = new Color(0.55f, 0.85f, 1f, 0.65f), runeScale = 1.20f, runeSpinSpeed = 0f,
                haloSprite = ElementalSprites.Halo, haloColor = new Color(0.30f, 0.65f, 1f, 0.35f), haloScale = 1.70f,
                glowSprite = ElementalSprites.Glow, glowColor = new Color(0.65f, 0.92f, 1f, 0.55f), glowScale = 1.10f,
                coreSprite = ElementalSprites.Core, coreColor = new Color(0.95f, 1f, 1f, 0.9f), coreScale = 0.45f,
                useFloor = false,
                particleEnabled = true,
                particleColorA = new Color(0.85f, 0.98f, 1f, 1f),
                particleColorB = new Color(0.40f, 0.75f, 1f, 1f),
                particleLife = 1.4f, particleSpeedMin = 0.2f, particleSpeedMax = 0.6f,
                particleSizeMin = 0.04f, particleSizeMax = 0.10f,
                particleRate = 18f, particleGravity = 0.3f,
                lightEnabled = true, lightColor = new Color(0.55f, 0.85f, 1f, 1f),
                lightIntensity = 1.3f, lightOuterMul = 1.2f,
            };
        }
    }
}
