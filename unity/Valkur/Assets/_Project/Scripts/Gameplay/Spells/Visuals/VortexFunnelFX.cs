using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A tornado standing in the world: a ground circle, a spinning funnel rising out of it,
    /// and debris riding the wall.
    ///
    /// <para>WHY NOT <see cref="AreaFXRig"/>. That rig is four concentric discs and a circle
    /// emitter — right for a puddle, and what the vortex used for years. A vortex is not a
    /// disc: it is a COLUMN, narrow where it touches down and flared where it opens, and the
    /// one thing a stack of coplanar discs can never show is the rotation the whole spell is
    /// named after. The same argument <c>IceWallVisual</c> makes about a line.</para>
    ///
    /// <para>THE GROUND RING IS THE CONTRACT. The funnel is narrow at the floor, so its
    /// silhouette says nothing about how far the force reaches — the ring does, and it is
    /// pinned to the force radius through <see cref="RING_BAND"/>. Everything else is drama
    /// that stays inside it.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED. Every child carries an absolute world size instead,
    /// because a <c>Light2D</c> parented under a scaled transform renders at its authored
    /// radius times that scale — which is how the old rig lit 367 world units off a 21-unit
    /// light, on a screen 33 units wide.</para>
    /// </summary>
    internal sealed partial class VortexFunnelFX
    {
        // ── silhouette ───────────────────────────────────────────────────────────────
        private const int BANDS = 18;
        private const int DUST_COUNT = 22;

        /// <summary>
        /// Funnel height as a multiple of the force radius. Taller than wide, or it reads as a
        /// puddle — but only just. Measured at 2.3 a pull stood 8.3 units on a screen 16.7 units
        /// tall and put its widest part four body-heights above the ground the force actually
        /// acts on, which is a tornado with a game behind it.
        /// </summary>
        private const float HEIGHT_PER_RADIUS = 1.55f;

        /// <summary>Where it touches down, and where it opens, as fractions of the force radius.</summary>
        private const float NECK_FRAC = 0.16f;
        private const float FLARE_FRAC = 0.72f;

        /// <summary>
        /// How flat a horizontal circle is drawn. The camera looks down at a shallow angle, so
        /// anything lying on the ground plane is a wide thin ellipse — the same constant the
        /// cast flourish's funnel and the ki aura's ground pulses use.
        /// </summary>
        private const float GROUND_SQUASH = 0.34f;

        /// <summary>
        /// World radius of one band sprite at scale 1. <c>TornadoSprites.BandRadius</c> is
        /// measured in the sprite's own -1..1 space and the sprite is 1 world unit across, so
        /// the band's line sits at half that. Dividing a wanted world radius by it is what pins
        /// the drawn circle to a real distance.
        /// </summary>
        private const float BAND_UNIT_RADIUS = TornadoSprites.BandRadius * 0.5f;

        /// <summary><c>ElementalSprites.Ring</c>'s bright band peaks here in the same space.</summary>
        private const float RING_BAND = 0.39f;

        // ── motion ───────────────────────────────────────────────────────────────────
        private const float SPIN_DEGREES = 300f;

        /// <summary>
        /// Extra fraction of the spin the flared top leads the neck by. A funnel whose every
        /// height shares one angle is a rigid cone being turned, not air being dragged round.
        /// </summary>
        private const float SPIN_TWIST = 0.50f;

        private const float DUST_CLIMB_SPEED = 0.55f;
        private const float DUST_SWEEP = 4.2f;

        // ── sorting ──────────────────────────────────────────────────────────────────
        private const int ORDER_GROUND_GLOW = 40;
        private const int ORDER_GROUND_RING = 41;
        private const int ORDER_BAND = 58;

        /// <summary>
        /// Derived, never hand-written. Each band takes <c>ORDER_BAND + i</c> so the higher ones
        /// draw over the lower, and the near debris has to clear the whole stack — a literal 72
        /// was correct at 9 bands and silently wrong at 18, which sinks the front-side scraps
        /// behind the funnel and costs the rig the one statement it makes about depth.
        /// </summary>
        private const int ORDER_DUST = ORDER_BAND + BANDS + 2;

        private Transform _root;
        private float _radius;
        private float _spinSign;
        private KiPalette _palette;

        private SpriteRenderer _groundRing;
        private SpriteRenderer _groundGlow;

        private Transform[] _bandPivots;      // position + ground squash
        private Transform[] _bandSpinners;    // rotation only
        private SpriteRenderer[] _bandRenderers;
        private float[] _bandHeight01;
        private float[] _bandPhase;

        private Transform[] _dust;
        private SpriteRenderer[] _dustRenderers;
        private float[] _dustAngle;
        private float[] _dustClimb;
        private float[] _dustSize;

        private GameObject _lightGo;
        private Component _light;

        private float _age;

        /// <summary>How many bands the stack is built from. Read by tests, which cannot name
        /// the top one without it.</summary>
        public int BandCount { get { return BANDS; } }

        /// <summary>How tall the funnel stands, in world units. Read by tests and by the controller.</summary>
        public float Height { get { return _radius * HEIGHT_PER_RADIUS; } }

        /// <summary>The circle the ground ring is drawn on, which is the force radius.</summary>
        public float GroundRadius { get { return _radius; } }

        /// <summary>
        /// Build the funnel under <paramref name="parent"/>.
        /// </summary>
        /// <param name="radius">The force radius, in world units. The ground ring is drawn on it exactly.</param>
        /// <param name="pull">Decides which way everything turns; push is the mirror of pull.</param>
        /// <param name="swatch">The spell's own <c>particleColor</c>.</param>
        public static VortexFunnelFX Attach(Transform parent, float radius, bool pull, Color swatch)
        {
            var fx = new VortexFunnelFX
            {
                _root = parent,
                _radius = Mathf.Max(0.4f, radius),
                // Pull turns one way and push the other. Once both are the same shape that sign
                // is the only thing separating them on screen, and it agrees with the sign the
                // cast flourish's funnel already spins in.
                _spinSign = pull ? 1f : -1f,
                _palette = KiPalette.From(swatch, 1f),
            };

            ElementalSprites.EnsureAll();
            TornadoSprites.EnsureAll();

            fx.BuildGround();
            fx.BuildGroundLayers();
            fx.BuildBands();
            fx.BuildDust();
            fx.BuildArcs();
            fx.AttachLight();
            fx.FireShockwave();     // the vortex BITES; it does not fade in
            return fx;
        }

        private void BuildGround()
        {
            // Pinned to the force radius: the ring's own bright band is what the player reads
            // as "this far and no further", so it has to be the circle Physics2D queries.
            float ringSpan = _radius / RING_BAND;
            _groundRing = MakeSprite("GroundRing", ElementalSprites.Ring,
                WithAlpha(_palette.Core, 0f), ORDER_GROUND_RING, SortingConfig.LAYER_FLOOR_DECALS);
            _groundRing.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);

            float glowSpan = _radius * 2.1f;
            _groundGlow = MakeSprite("GroundGlow", ElementalSprites.Glow,
                WithAlpha(_palette.Edge, 0f), ORDER_GROUND_GLOW, SortingConfig.LAYER_FLOOR_DECALS);
            _groundGlow.transform.localScale = new Vector3(glowSpan, glowSpan * GROUND_SQUASH, 1f);
        }

        private void BuildBands()
        {
            _bandPivots = new Transform[BANDS];
            _bandSpinners = new Transform[BANDS];
            _bandRenderers = new SpriteRenderer[BANDS];
            _bandHeight01 = new float[BANDS];
            _bandPhase = new float[BANDS];

            for (int i = 0; i < BANDS; i++)
            {
                float t = i / (BANDS - 1f);
                _bandHeight01[i] = t;
                // Spread the starting angles so the stack never resolves into one seam, which
                // is what a funnel built from one repeated band looks like: a spring. The climb
                // with height is what makes it a helix; the second term has period 3 because
                // there are only 4 sprite variants, so bands i and i+4 are drawn from the SAME
                // arc and a period that divides 4 would leave them a bare 13 degrees apart.
                _bandPhase[i] = t * 230f + (i % 3) * 83f;

                var pivot = new GameObject("Band" + i).transform;
                pivot.SetParent(_root, false);

                // Position and squash on the PARENT, rotation on the CHILD. Rotating an
                // already-squashed transform turns the ellipse like a wheel — corners rising
                // and falling — instead of running the arc around its rim, and the arc running
                // round the rim is the one motion that reads as spin from this camera.
                var spinner = new GameObject("Spin").transform;
                spinner.SetParent(pivot, false);

                var sr = spinner.gameObject.AddComponent<SpriteRenderer>();
                sr.sprite = TornadoSprites.Band(i);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_BAND + i;
                sr.color = WithAlpha(Color.Lerp(_palette.Core, _palette.Mid, t), 0f);

                _bandPivots[i] = pivot;
                _bandSpinners[i] = spinner;
                _bandRenderers[i] = sr;
            }
        }

        private void BuildDust()
        {
            _dust = new Transform[DUST_COUNT];
            _dustRenderers = new SpriteRenderer[DUST_COUNT];
            _dustAngle = new float[DUST_COUNT];
            _dustClimb = new float[DUST_COUNT];
            _dustSize = new float[DUST_COUNT];

            for (int i = 0; i < DUST_COUNT; i++)
            {
                _dustAngle[i] = Random.Range(0f, Mathf.PI * 2f);
                _dustClimb[i] = Random.value;
                _dustSize[i] = _radius * Random.Range(0.055f, 0.115f);

                var sr = MakeSprite("Dust" + i.ToString("00"), TornadoSprites.Dust,
                    WithAlpha(Color.Lerp(_palette.Mid, _palette.Edge, Random.value), 0f),
                    ORDER_DUST, SortingConfig.LAYER_VFX);
                _dust[i] = sr.transform;
                _dustRenderers[i] = sr;
            }
        }

        private void AttachLight()
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;

            // An IDENTITY child of an unscaled root, so the authored radius is the rendered
            // radius. This is the whole reason the rig refuses to scale itself.
            _lightGo = new GameObject("VortexLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lightType = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lightType != null)
                    lightType.SetValue(_light, System.Enum.ToObject(lightType.PropertyType, 3));  // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.Light);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.5f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.25f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
                SetLightIntensity(0f);
            }
            catch { _light = null; }
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, int order, string layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
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
