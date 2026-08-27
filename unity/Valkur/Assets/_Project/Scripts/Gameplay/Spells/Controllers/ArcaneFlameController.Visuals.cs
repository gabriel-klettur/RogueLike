using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The drawn half of the arcane flame: the ground decals, the additive volume,
    /// the two particle systems, the travelling boundary ring, and the envelope that
    /// opens and closes all of them.
    /// </summary>
    public partial class ArcaneFlameController
    {
        internal const float MoteLifetime = 1.2f;
        private const float HazeLifetime  = 2.2f;
        private const float MoteRate      = 38f;
        private const float HazeRate      = 6f;
        // Budget (vfx-authoring SKILL.md: the number to hold is emitRate x lifespan):
        // 38 x 1.2 = 45.6 live motes + 6 x 2.2 = 13.2 live haze = 58.8, inside the
        // "player aura / trail <= 60" band this spell falls in.
        private const float BoundaryRingLife = 0.34f;
        private const int   MaxBoundaryRings = 4;

        /// <summary>
        /// The boundary's hue. A saturated magenta-violet rather than the palette's near-white
        /// `accent`, so the whole silhouette reads arcane and the only near-white pixel in the
        /// effect is the hot core. Slightly hotter than <c>palette.glow</c> so the rim still
        /// separates from the volume behind it.
        /// </summary>
        private static readonly Color RingColor = new Color(0.86f, 0.42f, 1.00f);

        private SpriteRenderer _scorch, _runeSpin, _runeStatic, _haze, _halo, _glow, _core, _hotCore, _accent;
        private ParticleSystem _motes, _hazePs;

        private readonly List<BoundaryRing> _rings = new List<BoundaryRing>(MaxBoundaryRings);

        private struct BoundaryRing
        {
            public SpriteRenderer Sr;
            public float Age;
        }

        // ── Build ───────────────────────────────────────────────────────────────

        private void BuildVisual()
        {
            ElementalSprites.EnsureAll();

            float d = _radius * 2f;   // a child's localScale is its world DIAMETER

            // Ground. FloorDecals is inside the ambient light mask, but these use the
            // unlit material like the rest of the elemental family — a rune burning in
            // the floor should not dim with the daylight.
            _scorch = MakeChild("Scorch", ElementalSprites.Glow,
                new Color(0.10f, 0.03f, 0.16f, 0.34f), d * ScorchRadiusMul,
                SortingConfig.LAYER_FLOOR_DECALS, 48, additive: false);

            // THE BOUNDARY. Crest on _radius exactly — see invariant 1 in the main file.
            //
            // ON LAYER_VFX, NOT ON THE FLOOR, and this is a gameplay decision rather than an
            // art one. The scorch above is a ground mark and being occluded by a wall is
            // correct for it. This ring is the only thing that tells the player where the
            // damage stops — and measured in the shipped town, tree `Canopy` renderers sit on
            // WallsTop (sorting value 8) and building `Footprint` on WallsBottom (5), both
            // far above FloorDecals (3). On the floor the ring came out as a CRESCENT, its
            // right half swallowed by a building, which recreates dynamically the exact
            // failure the crest fix removed: a hazard edge with no readable pixel.
            //
            // Tinted with RingColor, NOT the palette's `accent`. Accent is a pale lilac-white
            // (0.95, 0.85, 1.00) and TWO ring layers stack, so on screen the boundary
            // composited to near-white and became the loudest thing in the effect — the spell
            // read as "white circle" rather than as arcane fire, and the near-white value that
            // belongs to the HOT CORE alone was spent on the rim. White only at the centre.
            float ringDiameter = d / RingCrestNormalized;
            _runeStatic = MakeChild("RuneStatic", ElementalSprites.Ring,
                WithAlpha(RingColor, 0.20f), ringDiameter,
                SortingConfig.LAYER_VFX, 4, additive: false);
            _runeSpin = MakeChild("Rune", ElementalSprites.Ring,
                WithAlpha(RingColor, 0.62f), ringDiameter,
                SortingConfig.LAYER_VFX, 5, additive: false);

            // Volume, on VFX where an entity standing in the fire no longer occludes it.
            // The haze is the only alpha-blended layer up here: it is the MASS the
            // additive layers sit on. Additive alone has no body, it only ever brightens.
            _haze = MakeChild("Haze", ElementalSprites.Halo,
                new Color(0.30f, 0.12f, 0.55f, 0.26f), d * HazeRadiusMul,
                SortingConfig.LAYER_VFX, 8, additive: false);

            // Alphas below the palette's authored values on purpose: additive layers
            // STACK toward white, so the authored alpha-blend numbers (halo 0.30,
            // glow 0.65) blow the centre out to flat white once they are summed.
            // These must stay in step with the per-frame values in AnimateVisuals, which
            // overwrite them every frame — two different numbers for one layer is exactly
            // the trap the old executor's overwritten localScale was.
            _halo = MakeChild("Halo", ElementalSprites.Halo,
                WithAlpha(_palette.halo, 0.24f), d * HaloRadiusMul,
                SortingConfig.LAYER_VFX, 10, additive: true);
            _glow = MakeChild("Glow", ElementalSprites.Glow,
                WithAlpha(_palette.glow, 0.42f), d * GlowRadiusMul,
                SortingConfig.LAYER_VFX, 11, additive: true);
            _core = MakeChild("Core", ElementalSprites.Core,
                WithAlpha(_palette.core, 0.62f), d * CoreRadiusMul,
                SortingConfig.LAYER_VFX, 12, additive: true);
            _hotCore = MakeChild("HotCore", ElementalSprites.HotCore,
                WithAlpha(_palette.hotCore, 0.80f), d * HotCoreRadiusMul,
                SortingConfig.LAYER_VFX, 13, additive: true);
            _accent = MakeChild("Accent", ElementalSprites.SparkleStar,
                WithAlpha(_palette.accent, 0.34f), d * AccentRadiusMul,
                SortingConfig.LAYER_VFX, 14, additive: true);

            BuildMotes();
            BuildHaze();
        }

        private SpriteRenderer MakeChild(string name, Sprite sprite, Color color, float scale,
            string layer, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            // sharedMaterial, never material: `.material` clones per renderer and leaks
            // an instance that EditMode tests report. Neither material is ever mutated
            // through this reference — that is the AuraController.cs:262 landmine.
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            return sr;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private void BuildMotes()
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _motes = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately (playOnAwake defaults true) and
            // Unity REFUSES main.duration on a playing system — it logs "Setting the
            // duration while system is still playing is not supported" and silently keeps
            // the old value. Stop -> configure -> Play, in that order, always.
            _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _motes.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = MoteLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.040f, _radius * 0.104f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(_palette.accent, _palette.core);
            main.gravityModifier = -0.35f;   // motes rise out of the disc
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90;

            var emission = _motes.emission;
            emission.rateOverTime = MoteRate;

            var shape = _motes.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            // 0.72 rather than the full radius: CLAUDE.md's measured noise rule is that
            // displacement reaches ~3.67 x strength x lifetime, so the noise below adds
            // ~0.55 u at radius 2.5. 0.72 x r + 0.55 keeps every mote inside the circle
            // that damages, which is the point of invariant 1.
            shape.radius = _radius * 0.72f;
            shape.radiusThickness = 1f;

            var col = _motes.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(BuildMoteGradient());

            var size = _motes.sizeOverLifetime;
            size.enabled = true;
            // Grow-then-fade. The old curve was EaseInOut(0, 0.4, 1, 0) — shrink-only,
            // and its 0.4 peak capped every mote at 40 % of an already sub-pixel size.
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f), new Keyframe(0.30f, 1f), new Keyframe(1f, 0.15f)));

            var rot = _motes.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f);

            var noise = _motes.noise;
            noise.enabled = true;
            noise.strength = _radius * 0.05f;
            noise.frequency = 0.4f;
            noise.damping = true;
            noise.scrollSpeed = 0.25f;

            var psr = _motes.GetComponent<ParticleSystemRenderer>();
            // ParticleMaterialCache, NOT ElementalSprites.SharedUnlitMaterial. That
            // material carries no texture, so a ParticleSystemRenderer pointed at it
            // draws hard white SQUARES — and AuraController writes through the same
            // static, so an earlier healing aura silently retextured these motes.
            psr.sharedMaterial = ParticleMaterialCache.Get(
                ParticleTextureLibrary.Get(ParticleTextureShape.Glow, 0.85f), additive: true);
            psr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            psr.sortingLayerName = SortingConfig.LAYER_VFX;
            psr.sortingOrder = 15;

            _motes.Play();
        }

        private Gradient BuildMoteGradient()
        {
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(_palette.accent, 0f),
                    new GradientColorKey(_palette.core, 0.45f),
                    new GradientColorKey(_palette.glow, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.90f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                });
            return grad;
        }

        private void BuildHaze()
        {
            var go = new GameObject("HazeParticles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _hazePs = go.AddComponent<ParticleSystem>();
            _hazePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _hazePs.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = HazeLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.24f, _radius * 0.44f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.34f, 0.14f, 0.60f, 0.16f), new Color(0.55f, 0.24f, 0.85f, 0.10f));
            main.gravityModifier = -0.10f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 30;

            var emission = _hazePs.emission;
            emission.rateOverTime = HazeRate;

            var shape = _hazePs.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _radius * 0.55f;
            shape.radiusThickness = 1f;

            var col = _hazePs.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.30f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = grad;

            var size = _hazePs.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(1f, 1.25f)));

            var rot = _hazePs.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            var psr = _hazePs.GetComponent<ParticleSystemRenderer>();
            // Alpha, not additive: this layer is the mass the additive volume sits on.
            psr.sharedMaterial = ParticleMaterialCache.Get(
                ParticleTextureLibrary.Get(ParticleTextureShape.Smoke, 0.9f), additive: false);
            psr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            psr.sortingLayerName = SortingConfig.LAYER_VFX;
            psr.sortingOrder = 9;

            _hazePs.Play();
        }
    }
}
