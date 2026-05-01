using UnityEngine;

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
            sr.material = ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private void BuildParticles(Transform parent, float radius, string layer)
        {
            var go = new GameObject("Particles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            Particles = go.AddComponent<ParticleSystem>();

            var main = Particles.main;
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

            var psr = Particles.GetComponent<ParticleSystemRenderer>();
            psr.material = ElementalSprites.SharedUnlitMaterial;
            psr.sortingLayerID = SortingLayer.NameToID(layer);
            psr.sortingLayerName = layer;
            psr.sortingOrder = 60;
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
                if (lt != null) lt.SetValue(Light, System.Enum.ToObject(lt.PropertyType, 2));
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

        public static AreaPalette Smoke()
        {
            ElementalSprites.EnsureAll();
            return new AreaPalette
            {
                runeSprite = null,
                haloSprite = ElementalSprites.Halo, haloColor = new Color(0.55f, 0.55f, 0.60f, 0.45f), haloScale = 2.10f,
                glowSprite = ElementalSprites.Glow, glowColor = new Color(0.40f, 0.40f, 0.45f, 0.40f), glowScale = 1.50f,
                coreSprite = null,
                useFloor = false,
                particleEnabled = true,
                particleColorA = new Color(0.78f, 0.78f, 0.82f, 1f),
                particleColorB = new Color(0.40f, 0.40f, 0.45f, 1f),
                particleLife = 1.6f, particleSpeedMin = 0.4f, particleSpeedMax = 1.0f,
                particleSizeMin = 0.20f, particleSizeMax = 0.45f,
                particleRate = 30f, particleGravity = -0.4f,
                lightEnabled = false, lightColor = Color.white, lightIntensity = 0f, lightOuterMul = 1f,
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
