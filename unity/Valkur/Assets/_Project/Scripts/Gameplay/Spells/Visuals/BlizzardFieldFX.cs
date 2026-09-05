using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A standing storm: a COLUMN of falling ice over a hard floor boundary, ground frost that
    /// creeps out and thaws, grit skittering across the floor, and a ring that says exactly how
    /// far the cold reaches.
    ///
    /// <para>WHY NOT <see cref="AreaFXRig"/>. That rig is four concentric discs and a circle
    /// emitter — right for a pool, and what this spell used for its whole life, with the
    /// <c>LavaPuddle</c> palette on top because <c>PuddleController</c> had no other. So an ICE
    /// spell authoring <c>(0.72, 0.90, 1.00)</c> drew ORANGE, pixel-identical to
    /// <c>cinder_trail</c>. Recolouring the discs would have fixed the hue and left the deeper
    /// problem: a stack of coplanar discs cannot draw something FALLING, and a blizzard that is
    /// not falling is a puddle.</para>
    ///
    /// <para>THREE DEPTH SLICES, NEVER ONE. This is the weather system's lesson applied
    /// directly: a single-system downpour gives every flake the same size, brightness and
    /// speed, so the eye has nothing to resolve depth from and reads the whole thing as a decal
    /// on the lens. The far slice is small, fast, faint and dense and draws on
    /// <c>FloorDecals</c> so it passes BEHIND the fighters; the near slice is large, slow,
    /// sparse and blurred and draws over them. That split is the entire depth cue.</para>
    ///
    /// <para>THE FLOOR IS HARD. Each slice's lifetime is exactly its fall height over its own
    /// speed, so every flake dies on the ground plane rather than sinking through it, and the
    /// spawn slab is the damage ellipse translated straight up — so what lands, lands inside
    /// the circle the ring promises.</para>
    ///
    /// <para>ONE OPAQUE LAYER: the grit. Everything else is additive light. A field made only
    /// of light reads as something shining on the floor rather than as weather happening to it,
    /// which is the split <c>VortexFunnelFX</c> and <c>KiAuraFX</c> make for their debris.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED. Every child carries an absolute world size, which is
    /// what keeps the <c>Light2D</c> rendering at its authored radius — the pair of lines that
    /// once made a vortex light reach an effective 367 units.</para>
    /// </summary>
    internal sealed partial class BlizzardFieldFX : IGroundFieldVisual
    {
        // ── geometry ─────────────────────────────────────────────────────────────────
        /// <summary>How high above the floor the column starts, world units. The camera is
        /// ~10 units tall at the shipped zoom, so this fills the frame above the circle
        /// without spawning flakes the player never sees fall.</summary>
        private const float FALL_HEIGHT = 6.0f;

        /// <summary>How flat a horizontal circle is drawn — the camera looks down at a shallow
        /// angle. The same constant the vortex, the cast flourish and the root field use.</summary>
        private const float GROUND_SQUASH = 0.34f;

        /// <summary><c>ElementalSprites.Ring</c>'s bright band peaks at this normalized radius,
        /// so <c>radius / RING_BAND</c> is the scale that puts the drawn circle exactly on the
        /// circle <c>Physics2D.OverlapCircleAll</c> queries.</summary>
        private const float RING_BAND = 0.39f;

        // ── population ───────────────────────────────────────────────────────────────
        private const int GRIT_COUNT = 12;

        /// <summary>
        /// Particles per second per square world unit, per slice. Multiplied by the field's
        /// own area, so a 4-unit blizzard is denser in total than a 2-unit one WITHOUT being
        /// denser on screen — which is what makes the radius readable.
        /// </summary>
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_DENSITY = { 1.10f, 0.60f, 0.16f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_SPEED = { 8.5f, 5.5f, 3.4f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_SIZE = { 0.11f, 0.19f, 0.36f };
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_ALPHA = { 0.50f, 0.85f, 0.34f };

        /// <summary>How hard a gust shears each slice. Above 1 on the near slice so a gust
        /// visibly SHEARS the depth stack instead of translating it — the parallax cue that
        /// makes the near slice read as near.</summary>
        [SelfHealingStatic("Immutable slice table built once from float literals. Holds no "
            + "Unity objects and is never mutated after init, so it cannot go stale across a "
            + "Play session.")]
        private static readonly float[] SLICE_WIND = { 0.75f, 1.00f, 1.45f };

        private const int SLICES = 3;

        // ── the event layer ──────────────────────────────────────────────────────────
        /// <summary>Seconds a gust holds. With the interval below this is ~26 % duty, which is
        /// the band an event layer has to sit in: below ~15 % nothing is happening, above ~40 %
        /// it is a steady state again and the eye stops reporting it.</summary>
        private const float GUST_SECONDS = 0.42f;
        private const float GUST_INTERVAL_MIN = 0.90f;
        private const float GUST_INTERVAL_MAX = 2.20f;
        private const float GUST_LEAN_MIN_DEG = 15f;
        private const float GUST_LEAN_MAX_DEG = 25f;
        /// <summary>How much the near slice thickens during a gust. It is the slice the player
        /// is standing in, so it is the one a gust has to be felt on.</summary>
        private const float GUST_NEAR_RATE_GAIN = 2.0f;

        /// <summary>Seconds the ground frost takes to reach full cover.</summary>
        private const float FROST_RISE_SECONDS = 2.0f;

        // ── sorting ──────────────────────────────────────────────────────────────────
        private const int ORDER_FROST = 40;
        private const int ORDER_RING = 42;
        private const int ORDER_GRIT = 43;
        private const int ORDER_FAR_SLICE = 44;
        private const int ORDER_MID_SLICE = 1;
        private const int ORDER_NEAR_SLICE = 3;

        private Transform _root;
        private float _radius;
        private ElementPalette _palette;

        private readonly ParticleSystem[] _slices = new ParticleSystem[SLICES];
        private readonly float[] _sliceRate = new float[SLICES];
        private readonly float[] _sliceAppliedLean = new float[SLICES];

        private SpriteRenderer _ring;
        private SpriteRenderer _frost;
        private Transform[] _grit;
        private SpriteRenderer[] _gritRenderers;
        private float[] _gritBearing;
        private float[] _gritSpeed;
        private float[] _gritPhase;

        private GameObject _lightGo;
        private Component _light;

        private float _age;
        private float _fade = 1f;
        private float _tickFlash;
        private float _gustTimer;
        private float _gustRemaining;
        private float _gustLeanDeg;
        private bool _destroyed;

        /// <summary>The circle the ground ring is drawn on, which is the damage radius.</summary>
        public float GroundRadius => _radius;

        /// <summary>How many depth slices the column is built from. A test cannot name one
        /// without it, and naming one by index is how an assertion silently starts measuring a
        /// different slice when the count changes.</summary>
        public int SliceCount => SLICES;

        /// <summary>The scale that puts <see cref="ElementalSprites.Ring"/>'s bright band on a
        /// given world radius. Exposed so a test can assert the composition, not either half.</summary>
        public static float RingSpanFor(float worldRadius) => worldRadius / RING_BAND;

        public static BlizzardFieldFX Attach(Transform parent, float radius, ElementPalette palette)
        {
            ElementalSprites.EnsureAll();
            FieldSprites.EnsureAll();
            KiSprites.EnsureAll();

            var fx = new BlizzardFieldFX
            {
                _root = parent,
                _radius = Mathf.Max(0.5f, radius),
                _palette = palette,
            };

            // Identity root. Anything that wants a world size takes it as an absolute child
            // scale — the only thing that keeps the light at its authored radius.
            parent.localScale = Vector3.one;

            fx.BuildGround();
            fx.BuildGrit();
            fx.BuildSlices();
            fx.AttachLight();
            fx.ScheduleGust();
            return fx;
        }

        // ── construction ─────────────────────────────────────────────────────────────

        private void BuildGround()
        {
            // Pinned to the damage radius. The falling flakes are scattered and moving, so
            // their silhouette states nothing exact; the ring is the contract.
            float ringSpan = RingSpanFor(_radius);
            _ring = MakeSprite("ChillRing", ElementalSprites.Ring, _palette.core, 0f,
                ORDER_RING, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _ring.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);

            // Settled frost. Wide and very dim: it says the ground has been changed, and a
            // bright wash here would flatten everything standing on it.
            float frostSpan = _radius * 2.05f;
            _frost = MakeSprite("Frost", ElementalSprites.Glow, _palette.halo, 0f,
                ORDER_FROST, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _frost.transform.localScale = new Vector3(frostSpan, frostSpan * GROUND_SQUASH, 1f);
        }

        /// <summary>
        /// The one OPAQUE layer: chips of ice driven across the floor. Deliberately NOT folded
        /// into the additive stack as a tidy-up — a dark chip on an additive surface adds almost
        /// nothing, so the layer would vanish with nothing failing.
        /// </summary>
        private void BuildGrit()
        {
            _grit = new Transform[GRIT_COUNT];
            _gritRenderers = new SpriteRenderer[GRIT_COUNT];
            _gritBearing = new float[GRIT_COUNT];
            _gritSpeed = new float[GRIT_COUNT];
            _gritPhase = new float[GRIT_COUNT];

            for (int i = 0; i < GRIT_COUNT; i++)
            {
                var sr = MakeSprite("Grit" + i, KiSprites.Pebble, _palette.accent, 0f,
                    ORDER_GRIT, SortingConfig.LAYER_FLOOR_DECALS, additive: false);
                float size = Random.Range(0.07f, 0.15f);
                sr.transform.localScale = new Vector3(size, size * 0.75f, 1f);

                _grit[i] = sr.transform;
                _gritRenderers[i] = sr;
                _gritBearing[i] = Random.Range(0f, 360f);
                _gritSpeed[i] = Random.Range(0.55f, 1.45f);
                _gritPhase[i] = Random.Range(0f, 1f);
                _grit[i].localPosition = RandomGroundPoint();
            }
        }

        private Vector3 RandomGroundPoint()
        {
            // sqrt on the radius, or every point lands near the rim: uniform in ANGLE and
            // uniform in RADIUS is not uniform in AREA.
            float r = _radius * Mathf.Sqrt(Random.value) * 0.94f;
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
                _sliceAppliedLean[i] = float.NaN;   // forces the first lean write
            }
        }

        private ParticleSystem BuildSlice(int index)
        {
            var go = new GameObject("Fall" + index);
            go.transform.SetParent(_root, false);

            var ps = go.AddComponent<ParticleSystem>();
            // AddComponent starts it immediately, and main.duration cannot be written while a
            // system is playing — it fires "Setting the duration while system is still playing
            // is not supported" and silently keeps the old value.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float speed = SLICE_SPEED[index];
            float life = FALL_HEIGHT / speed;   // dies exactly on the floor

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = life;
            main.startSpeed = 0f;               // a Box emits along its own +Z, into the screen
            main.startSize = SLICE_SIZE[index];
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.startColor = WithAlpha(_palette.core, SLICE_ALPHA[index]);

            var emission = ps.emission;
            emission.rateOverTime = _sliceRate[index];

            // The spawn slab IS the damage ellipse, translated straight up. Falling the same
            // height at a constant speed then lands every flake back inside it.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_radius * 2f, _radius * 2f * GROUND_SQUASH, 0.01f);
            shape.position = new Vector3(0f, FALL_HEIGHT, 0f);
            shape.rotation = Vector3.zero;

            // All three axes as plain constants, so they share a MinMaxCurveMode. Mixing modes
            // logs "Particle Velocity curves must all be in the same mode" once per frame, per
            // system, for as long as the effect lives.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-speed);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            // In fast, out slow. The fade-out is what turns "the flake was deleted" into "the
            // flake settled"; without it a hard floor is a row of things blinking off.
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.07f),
                    new GradientAlphaKey(1f, 0.86f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            bool far = index == 0;
            // The far slice is a STREAK because it is moving fastest; the streak texture is
            // wider than it is tall because a stretched billboard aligns its U axis with
            // VELOCITY, and a vertical strip is smeared across its own fall.
            Sprite sprite = far ? FieldSprites.Streak
                          : index == 1 ? ElementalSprites.Snowflake
                          : FieldSprites.Puff;
            renderer.sharedMaterial = ParticleMaterialCache.Get(sprite.texture, additive: true);
            renderer.renderMode = far ? ParticleSystemRenderMode.Stretch
                                      : ParticleSystemRenderMode.Billboard;
            if (far) renderer.lengthScale = 2.6f;

            // The far slice draws UNDER the fighters and the near slice over them. That single
            // split is what stops the column reading as one flat sheet.
            string layer = far ? SortingConfig.LAYER_FLOOR_DECALS : SortingConfig.LAYER_VFX;
            renderer.sortingLayerID = SortingLayer.NameToID(layer);
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = far ? ORDER_FAR_SLICE
                                  : index == 1 ? ORDER_MID_SLICE : ORDER_NEAR_SLICE;

            ps.Play(true);
            return ps;
        }

        private void AttachLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("BlizzardLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4.
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.05f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.15f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
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
                // Destroy is an outright ERROR in Edit Mode, where a test builds this rig and
                // tears it down directly.
                if (Application.isPlaying) Object.Destroy(_lightGo);
                else Object.DestroyImmediate(_lightGo);
            }
            _lightGo = null;
            _light = null;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────

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
