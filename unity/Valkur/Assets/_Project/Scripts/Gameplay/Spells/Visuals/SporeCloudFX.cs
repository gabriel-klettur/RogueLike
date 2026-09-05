using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A hanging VOLUME of spores: two depth slices of drifting motes, a sickly wash on the
    /// floor beneath them, grit settling out of the air, and puffs that bloom and are gone.
    ///
    /// <para>A VOLUME IS THE HARDEST THING TO DRAW in a 2D top-down game, because there is no
    /// depth cue to borrow. The answer is the blizzard's and the weather system's: DEPTH
    /// SLICES. The far slice is small, dense and quick and draws on <c>FloorDecals</c> so it
    /// passes behind the fighters; the near slice is large, slow, sparse and pale and draws over
    /// them. One system for both would give every mote the same size and speed, which is
    /// precisely how a cloud comes to read as a decal on the lens.</para>
    ///
    /// <para>IT DRIFTS WITH THE SHARED WIND, and the drift is CLAMPED. <see cref="Valkur.Gameplay.World.Weather.WeatherWind"/>
    /// is ticked once per frame by the weather manager so every reader samples the same gust —
    /// a cloud that ignores the wind while rain slants past it reads as cheapness. But this
    /// cloud is a HAZARD pinned to a damage circle, so an unclamped wind would blow the picture
    /// off the mechanic: the lean is capped at a fraction of the radius per particle lifetime,
    /// which leans the volume downwind without moving it off the ground it poisons.</para>
    ///
    /// <para>ONE OPAQUE LAYER: the grit that settles out. Everything else is additive light, and
    /// a cloud made only of light hangs in front of the world rather than in it.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED. <c>SmokeLifetime.Init</c> — the code this rig replaces
    /// for hazard clouds — wrote <c>localScale = one * radius</c> AFTER <c>AreaFXRig.Attach</c>
    /// had already sized every child by that radius, so a 3-unit cloud rendered a
    /// <c>Light2D</c> at an effective 15.3 units on a 33-unit-wide screen. That is the same
    /// two-line pair that once made a vortex light reach 367.</para>
    /// </summary>
    internal sealed partial class SporeCloudFX : IGroundFieldVisual
    {
        private const int SLICES = 2;
        private const int GRIT_COUNT = 14;

        /// <summary>How many bloom sprites are pooled. Three is enough that an ambient bloom
        /// and a damage bloom can overlap without either being cut short.</summary>
        private const int BLOOMS = 3;

        private const float GROUND_SQUASH = 0.34f;
        private const float RING_BAND = 0.39f;

        /// <summary>Particles per second per square world unit, per slice.</summary>
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_DENSITY = { 0.55f, 0.16f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_SIZE = { 0.55f, 1.10f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_ALPHA = { 0.26f, 0.15f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_LIFE = { 2.6f, 4.2f };
        /// <summary>Above 1 on the near slice, so a gust SHEARS the depth stack instead of
        /// translating it — the parallax cue that makes the near slice read as near.</summary>
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_WIND = { 0.85f, 1.40f };

        /// <summary>
        /// The furthest a mote may be carried downwind, as a fraction of the field radius.
        /// See the class doc: the cloud leans, it does not travel.
        /// </summary>
        private const float MAX_DRIFT_FRAC = 0.42f;

        // ── the event layer ──────────────────────────────────────────────────────────
        private const float BLOOM_SECONDS = 0.50f;
        private const float BLOOM_INTERVAL_MIN = 1.20f;
        private const float BLOOM_INTERVAL_MAX = 2.50f;

        /// <summary>Seconds the grit takes to finish settling out of the air.</summary>
        private const float SETTLE_SECONDS = 2.6f;

        // ── sorting ──────────────────────────────────────────────────────────────────
        private const int ORDER_HAZE = 40;
        private const int ORDER_RING = 42;
        private const int ORDER_GRIT = 43;
        private const int ORDER_FAR_SLICE = 46;
        private const int ORDER_NEAR_SLICE = 2;
        private const int ORDER_BLOOM = 4;

        private Transform _root;
        private float _radius;
        private ElementPalette _palette;

        private readonly ParticleSystem[] _slices = new ParticleSystem[SLICES];
        private readonly float[] _sliceRate = new float[SLICES];
        private readonly float[] _sliceAppliedDrift = new float[SLICES];

        private SpriteRenderer _haze;
        private SpriteRenderer _ring;

        private Transform[] _grit;
        private SpriteRenderer[] _gritRenderers;
        private float[] _gritPhase;

        private Transform[] _blooms;
        private SpriteRenderer[] _bloomRenderers;
        private float[] _bloomAge;
        private float[] _bloomScale;
        private int _bloomCursor;

        private GameObject _lightGo;
        private Component _light;

        private float _age;
        private float _fade = 1f;
        private float _bloomTimer;
        private bool _destroyed;

        /// <summary>The circle the ground ring is drawn on, which is the damage radius.</summary>
        public float GroundRadius => _radius;

        /// <summary>How many depth slices the volume is built from.</summary>
        public int SliceCount => SLICES;

        /// <summary>The scale that puts <see cref="ElementalSprites.Ring"/>'s bright band on a
        /// given world radius, so a test can assert the composition and not either half.</summary>
        public static float RingSpanFor(float worldRadius) => worldRadius / RING_BAND;

        public static SporeCloudFX Attach(Transform parent, float radius, ElementPalette palette)
        {
            ElementalSprites.EnsureAll();
            FieldSprites.EnsureAll();
            KiSprites.EnsureAll();

            var fx = new SporeCloudFX
            {
                _root = parent,
                _radius = Mathf.Max(0.5f, radius),
                _palette = palette,
            };

            // Identity root: every child carries an absolute world size. See the class doc for
            // what happens when it does not.
            parent.localScale = Vector3.one;

            fx.BuildGround();
            fx.BuildGrit();
            fx.BuildSlices();
            fx.BuildBlooms();
            fx.AttachLight();
            fx._bloomTimer = Random.Range(0.35f, BLOOM_INTERVAL_MIN);
            return fx;
        }

        private void BuildGround()
        {
            float ringSpan = RingSpanFor(_radius);
            _ring = MakeSprite("SporeRing", ElementalSprites.Ring, _palette.core, 0f,
                ORDER_RING, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _ring.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);

            // A sickly wash under the volume. Very low alpha: it is a stain the cloud casts on
            // the floor, and anything brighter competes with the cloud itself.
            float hazeSpan = _radius * 2.0f;
            _haze = MakeSprite("FloorHaze", ElementalSprites.Glow, _palette.glow, 0f,
                ORDER_HAZE, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _haze.transform.localScale = new Vector3(hazeSpan, hazeSpan * GROUND_SQUASH, 1f);
        }

        /// <summary>
        /// Spore grit falling out of the volume and staying on the floor. The rig's ONE opaque
        /// layer, and deliberately not folded into the additive stack as a tidy-up: a dull chip
        /// on an additive surface adds almost nothing, so the layer would vanish silently.
        /// </summary>
        private void BuildGrit()
        {
            _grit = new Transform[GRIT_COUNT];
            _gritRenderers = new SpriteRenderer[GRIT_COUNT];
            _gritPhase = new float[GRIT_COUNT];

            for (int i = 0; i < GRIT_COUNT; i++)
            {
                var sr = MakeSprite("Spore" + i, KiSprites.Pebble, _palette.accent, 0f,
                    ORDER_GRIT, SortingConfig.LAYER_FLOOR_DECALS, additive: false);
                float size = Random.Range(0.06f, 0.13f);
                sr.transform.localScale = new Vector3(size, size * 0.8f, 1f);
                sr.transform.localPosition = RandomGroundPoint();
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                _grit[i] = sr.transform;
                _gritRenderers[i] = sr;
                // Each chip settles at its own moment, so the floor fills in gradually rather
                // than all fourteen appearing on one frame.
                _gritPhase[i] = Random.value;
            }
        }

        private Vector3 RandomGroundPoint()
        {
            // sqrt on the radius: uniform in angle and uniform in radius is not uniform in AREA,
            // and without it every chip lands near the rim.
            float r = _radius * Mathf.Sqrt(Random.value) * 0.92f;
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * GROUND_SQUASH, 0f);
        }

        private void BuildSlices()
        {
            float area = Mathf.PI * _radius * _radius;
            for (int i = 0; i < SLICES; i++)
            {
                _sliceRate[i] = SLICE_DENSITY[i] * area;
                _slices[i] = BuildSlice(i);
                _sliceAppliedDrift[i] = float.NaN;   // forces the first write
            }
        }

        private ParticleSystem BuildSlice(int index)
        {
            var go = new GameObject("Volume" + index);
            go.transform.SetParent(_root, false);

            var ps = go.AddComponent<ParticleSystem>();
            // main.duration cannot be written while the system plays, and AddComponent starts
            // it immediately: stop, configure, then play.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = SLICE_LIFE[index];
            main.startSpeed = 0f;                 // a Sphere would emit outward; the drift is ours
            main.startSize = SLICE_SIZE[index];
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            main.startColor = WithAlpha(_palette.core, SLICE_ALPHA[index]);

            var emission = ps.emission;
            emission.rateOverTime = _sliceRate[index];

            // Born throughout the volume rather than on its shell: a hemisphere emitter puts
            // every mote on the rim, which draws a bubble instead of a cloud.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _radius * 0.80f;
            shape.radiusThickness = 1f;
            shape.scale = new Vector3(1f, GROUND_SQUASH + 0.34f, 0.01f);

            // All three axes as plain constants so they share a MinMaxCurveMode. Mixing modes
            // logs "Particle Velocity curves must all be in the same mode" every frame.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.10f);   // spores are lighter than air
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            // Churn. The scalar strength, NOT strengthX/Y/Z: those are ignored unless
            // separateAxes is set first, and the module then silently reads the scalar — which
            // defaults to 1 and would shove every mote a full unit per second.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.09f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.25f;
            noise.damping = true;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.22f),
                    new GradientAlphaKey(0.85f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.45f, 1f), new Keyframe(1f, 1.25f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ParticleMaterialCache.Get(FieldSprites.Puff.texture, additive: true);

            bool far = index == 0;
            string layer = far ? SortingConfig.LAYER_FLOOR_DECALS : SortingConfig.LAYER_VFX;
            renderer.sortingLayerID = SortingLayer.NameToID(layer);
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = far ? ORDER_FAR_SLICE : ORDER_NEAR_SLICE;

            ps.Play(true);
            return ps;
        }

        private void BuildBlooms()
        {
            _blooms = new Transform[BLOOMS];
            _bloomRenderers = new SpriteRenderer[BLOOMS];
            _bloomAge = new float[BLOOMS];
            _bloomScale = new float[BLOOMS];

            for (int i = 0; i < BLOOMS; i++)
            {
                var sr = MakeSprite("Bloom" + i, FieldSprites.Puff, _palette.hotCore, 0f,
                    ORDER_BLOOM, SortingConfig.LAYER_VFX, additive: true);
                _blooms[i] = sr.transform;
                _bloomRenderers[i] = sr;
                _bloomAge[i] = BLOOM_SECONDS;     // starts spent, so nothing draws until fired
                _bloomScale[i] = 1f;
            }
        }

        private void AttachLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("SporeLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));   // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.07f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.20f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.95f);
                SetLightIntensity(0f);
            }
            catch { _light = null; }
        }

        private void SetLightIntensity(float intensity)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity); }
            catch { }
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;

            for (int i = 0; i < SLICES; i++)
                if (_slices[i] != null) _slices[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (_lightGo != null)
            {
                // Destroy is an outright ERROR in Edit Mode, where a test builds this directly.
                if (Application.isPlaying) Object.Destroy(_lightGo);
                else Object.DestroyImmediate(_lightGo);
            }
            _lightGo = null;
            _light = null;
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, float alpha,
            int order, string layer, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, alpha);
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
