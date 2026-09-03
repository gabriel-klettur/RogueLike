using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A patch of ground that has come alive: cracked earth, a ring that says how far it
    /// reaches, barbed stems that break the surface, sway, lash at whatever is standing in
    /// them, and sink back.
    ///
    /// <para>WHY NOT <see cref="AreaFXRig"/>. That rig is four concentric discs, a circle
    /// emitter and a light — right for a puddle, and what this spell used for as long as it
    /// existed, with the <c>LavaPuddle</c> palette on top of it because
    /// <c>PuddleController</c> had no other. Measured, that put four ORANGE sprites, an
    /// orange <c>Light2D</c> and 25 lava particles a second under 16 green ones: a nature
    /// spell wearing a lava pool, 61% orange by particle rate. The deeper problem is the one
    /// <c>IceWallVisual</c> records for a line and <c>VortexFunnelFX</c> for a column — a
    /// stack of coplanar discs cannot draw a thing that STANDS UP, and a root that does not
    /// rise out of the ground is not a root.</para>
    ///
    /// <para>THE ROOT TRANSFORM IS NEVER SCALED. Every child carries an absolute world size.
    /// <c>PuddleController</c> used to write <c>localScale = one * radius</c> over this GO,
    /// which scaled the old particle emitter's shape with it — measured, a 1.5-unit damage
    /// circle emitting over 1.91 units — and would scale a <c>Light2D</c> parented under it
    /// the same way, the failure that once lit 367 world units off a 21-unit light.</para>
    ///
    /// <para>THE GROUND RING IS THE CONTRACT. The stems are scattered and moving, so their
    /// silhouette says nothing exact; the ring is pinned to the damage radius through
    /// <see cref="RING_BAND"/> and every stem is seeded strictly inside it
    /// (<see cref="SEED_FRAC"/>). Three different radii — a ring at 57% of the damage
    /// circle, damage at 100%, tendrils at 127% — is what the old rig actually drew.</para>
    ///
    /// <para>ONE LAYER IS OPAQUE ON PURPOSE. The stems and the thrown clods are on the alpha
    /// material; only the ring, the glow and the sprout pops are additive. An all-additive
    /// field reads as light shining on the floor rather than as the floor being torn open,
    /// the same split <c>VortexFunnelFX</c> and <c>KiAuraFX</c> make for their debris.</para>
    /// </summary>
    internal sealed partial class RootWhipFX : IGroundFieldVisual
    {
        // ── population ───────────────────────────────────────────────────────────────
        private const int TENDRILS = 15;
        private const int CLODS = 20;
        private const int CRACKS = 9;

        /// <summary>
        /// How far out stems are allowed to be seeded, as a fraction of the damage radius.
        /// Strictly inside 1 so nothing is ever drawn outside the ring the spell promises —
        /// a stem past the ring is an enemy standing next to a root that cannot touch them.
        /// </summary>
        private const float SEED_FRAC = 0.88f;

        // ── silhouette ───────────────────────────────────────────────────────────────
        /// <summary>Stem height in world units, before the per-stem random. Roughly knee to
        /// waist on a 1.86-unit character: tall enough to read, short enough that a field of
        /// fifteen does not hide the fight happening inside it.</summary>
        private const float STEM_HEIGHT_MIN = 0.70f;
        private const float STEM_HEIGHT_MAX = 1.30f;

        /// <summary>
        /// How flat a horizontal circle is drawn. The camera looks down at a shallow angle,
        /// so anything lying on the ground plane is a wide thin ellipse — the same constant
        /// the vortex funnel, the cast flourish and the ki aura ground pulses use.
        /// </summary>
        private const float GROUND_SQUASH = 0.34f;

        /// <summary><c>ElementalSprites.Ring</c>'s bright band peaks at this normalized
        /// radius, so a wanted world radius divided by it is the scale that puts the drawn
        /// circle exactly there.</summary>
        private const float RING_BAND = 0.39f;

        // ── motion ───────────────────────────────────────────────────────────────────
        private const float SPROUT_SECONDS = 0.20f;
        private const float RETRACT_SECONDS = 0.32f;
        private const float LIFE_MIN = 1.10f;
        private const float LIFE_MAX = 2.40f;

        /// <summary>How far past full height a stem overshoots as it breaks the surface.
        /// Without it the sprout is a linear stretch and reads as a growing rectangle.</summary>
        private const float SPROUT_OVERSHOOT = 0.22f;

        private const float SWAY_AMPLITUDE_DEG = 7.5f;
        private const float SWAY_HZ_MIN = 0.35f;
        private const float SWAY_HZ_MAX = 0.80f;

        /// <summary>Seconds one lash takes from crack to recovery.</summary>
        private const float LASH_SECONDS = 0.28f;
        /// <summary>How far a lashing stem stretches at the peak of the crack.</summary>
        private const float LASH_STRETCH = 0.40f;
        /// <summary>How many stems answer one damage tick. Every stem lashing at once is a
        /// field pulsing, which reads as one object breathing rather than as individual
        /// roots striking at something.</summary>
        private const int LASH_STEMS = 4;

        // ── sorting ──────────────────────────────────────────────────────────────────
        private const int ORDER_CRACK = 40;
        private const int ORDER_GROUND_GLOW = 41;
        private const int ORDER_GROUND_RING = 42;
        private const int ORDER_TENDRIL = 60;

        /// <summary>
        /// Derived, never hand-written. Stem <c>i</c> takes <c>ORDER_TENDRIL + i</c> so the
        /// ones further back draw behind, and a thrown clod has to clear the whole stack.
        /// A literal here is the bug that sank the vortex debris behind its own funnel when
        /// the band count changed.
        /// </summary>
        private const int ORDER_CLOD = ORDER_TENDRIL + TENDRILS + 2;
        private const int ORDER_BURST = ORDER_CLOD + 1;

        private Transform _root;
        private float _radius;
        private RootPalette _palette;
        private float _age;
        private float _fade = 1f;

        private SpriteRenderer _groundRing;
        private SpriteRenderer _groundGlow;
        private SpriteRenderer[] _cracks;

        private Transform[] _stemPivots;
        private SpriteRenderer[] _stemRenderers;
        private float[] _stemHeight;
        private float[] _stemLean;
        private float[] _stemSwayHz;
        private float[] _stemSwayPhase;
        private float[] _stemAge;
        private float[] _stemLife;
        private float[] _stemMirror;
        private float[] _stemLash;
        private float[] _stemLashLean;

        private Transform[] _clods;
        private SpriteRenderer[] _clodRenderers;
        private Vector2[] _clodVelocity;
        private float[] _clodAge;
        private float[] _clodLife;
        private float[] _clodSpin;

        private SpriteRenderer[] _bursts;
        private float[] _burstAge;
        private int _burstCursor;

        private GameObject _lightGo;
        private Component _light;

        /// <summary>How many stems the field is built from. Read by tests, which cannot name
        /// one without it.</summary>
        public int StemCount { get { return TENDRILS; } }

        /// <summary>The circle the ground ring is drawn on, which is the damage radius.</summary>
        public float GroundRadius { get { return _radius; } }

        /// <summary>How flat a ground-plane circle is drawn. Exposed because a test that
        /// measures where a stem was seeded has to un-squash the offset first, or every stem
        /// looks closer to the centre than it is.</summary>
        public static float GroundSquash { get { return GROUND_SQUASH; } }

        /// <summary>The scale factor that puts <see cref="ElementalSprites.Ring"/>'s bright
        /// band on a given world radius. Exposed so a test can assert the composition rather
        /// than either half of it.</summary>
        public static float RingSpanFor(float worldRadius) { return worldRadius / RING_BAND; }

        /// <summary>
        /// Build the field under <paramref name="parent"/>.
        /// </summary>
        /// <param name="radius">The damage radius in WORLD UNITS. The ground ring is drawn
        /// on it exactly and no stem is seeded outside it.</param>
        /// <param name="swatch">The spell's own <c>particleColor</c>. One colour in, four
        /// out — see <see cref="RootPalette"/>.</param>
        public static RootWhipFX Attach(Transform parent, float radius, Color swatch)
        {
            var fx = new RootWhipFX
            {
                _root = parent,
                _radius = Mathf.Max(0.4f, radius),
                _palette = RootPalette.From(swatch),
            };

            ElementalSprites.EnsureAll();
            RootSprites.EnsureAll();

            // The root stays at identity. Anything that wants a world size takes it as an
            // absolute child scale, which is what makes the light render at its authored
            // radius and the ring land on the damage circle.
            parent.localScale = Vector3.one;

            fx.BuildGround();
            fx.BuildCracks();
            fx.BuildStems();
            fx.BuildClods();
            fx.BuildBursts();
            fx.AttachLight();
            return fx;
        }

        private void BuildGround()
        {
            // Pinned to the damage radius: the ring's bright band is what the player reads
            // as "this far and no further", so it has to sit on the circle
            // Physics2D.OverlapCircleAll actually queries.
            float ringSpan = RingSpanFor(_radius);
            _groundRing = MakeSprite("GroundRing", ElementalSprites.Ring,
                WithAlpha(_palette.Leaf, 0f), ORDER_GROUND_RING,
                SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _groundRing.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);

            float glowSpan = _radius * 1.9f;
            _groundGlow = MakeSprite("GroundGlow", ElementalSprites.Glow,
                WithAlpha(_palette.Bark, 0f), ORDER_GROUND_GLOW,
                SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _groundGlow.transform.localScale = new Vector3(glowSpan, glowSpan * GROUND_SQUASH, 1f);
        }

        private void BuildCracks()
        {
            _cracks = new SpriteRenderer[CRACKS];
            for (int i = 0; i < CRACKS; i++)
            {
                // Jittered off an even fan: a perfectly even star reads as a decal stamped
                // on the floor, which is the one thing the cracks exist to deny.
                float bearing = (i / (float)CRACKS) * 360f + Random.Range(-14f, 14f);
                float reach = _radius * Random.Range(0.45f, SEED_FRAC);

                // Opaque, like the clods: a crack is absence of ground, not light on it.
                var sr = MakeSprite("Crack" + i, RootSprites.Crack,
                    WithAlpha(_palette.Soil, 0f), ORDER_CRACK,
                    SortingConfig.LAYER_FLOOR_DECALS, additive: false);

                // The crack sprite runs along +X from its pivot, so the bearing is a plain
                // Z rotation and the reach is localScale.x. The ground squash is applied on
                // Y of the same transform because a fissure lying on the floor is
                // foreshortened exactly as the ring is.
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, bearing);
                sr.transform.localScale = new Vector3(reach, reach * GROUND_SQUASH * 1.4f, 1f);
                _cracks[i] = sr;
            }
        }

        private void AttachLight()
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;

            // An IDENTITY child of an unscaled root, so the authored radius is the rendered
            // radius. The whole reason the rig refuses to scale itself.
            _lightGo = new GameObject("RootLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lightType = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lightType != null)
                    lightType.SetValue(_light, System.Enum.ToObject(lightType.PropertyType, 3));  // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.Sap);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.25f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.20f);
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

        /// <summary>Tears the rig down. Safe to call twice.</summary>
        public void Destroy()
        {
            // Destroy is an outright ERROR in Edit Mode, where a test or an editor probe
            // builds this rig and then tears it down.
            if (_lightGo != null)
            {
                if (Application.isPlaying) Object.Destroy(_lightGo);
                else Object.DestroyImmediate(_lightGo);
            }
            _lightGo = null;
            _light = null;
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, int order,
                                          string layer, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            // Additive for anything that is LIGHT (the ring, the glow, a sprout pop);
            // plain alpha for anything that is MATTER. A dark chip on an additive surface
            // adds almost nothing, so folding the clods in as a tidy-up would delete them
            // with nothing failing.
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }
    }
}
