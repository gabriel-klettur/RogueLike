using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A breath weapon standing in front of its caster: a filled wedge of flame with a
    /// white-hot throat, embers riding it out, a scorch on the ground under it and a light
    /// that reaches as far as the fire does.
    ///
    /// <para>WHY NOT A <c>LineRenderer</c>. The cone used to be twelve points — origin, an
    /// arc, back to origin — which is a WIRE OUTLINE, not a flame. A LineRenderer can draw the
    /// boundary of a shape and can never fill one, so the spell's whole silhouette was two
    /// thin radial strokes and a curve between them. The same argument <c>IceWallVisual</c>
    /// makes about a line and <c>VortexFunnelFX</c> makes about a column: the rig has to be
    /// shaped like the thing it draws.</para>
    ///
    /// <para>THE DRAWN WEDGE IS THE QUERIED WEDGE. Every slice's cross extent comes from
    /// <see cref="HalfWidthAt"/>, which is the same half-width <see cref="ConeBreathController"/>
    /// tests a target against — so the edge a player reads is the edge that hurts. A rig whose
    /// reach is decorative is the failure the vortex's ground ring exists to prevent.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED and neither is either of its oriented children, so the
    /// <c>Light2D</c> renders at its authored radius. Sizes are absolute, per child.</para>
    ///
    /// <para>TWO ORIENTED CHILDREN, NOT ONE, and the reason is not tidiness. A sprite's quad
    /// lies in its own XY plane, so a sprite parent may only ever be turned ABOUT Z or it goes
    /// edge-on to the camera and disappears. A <c>ParticleSystem</c>'s Cone shape emits along
    /// its own +Z, which needs a full <c>LookRotation</c>. One transform cannot be both. The
    /// old controller tried to be, with a hand-derived <c>Euler(deg - 90, 90, 0)</c> that is a
    /// MIRROR about the 45 degree diagonal — measured across the eight facings, aiming east
    /// emitted north, aiming north emitted east, and 135 and 315 came out exactly reversed.
    /// Six of eight directions sprayed fire somewhere other than the damage.</para>
    /// </summary>
    internal sealed partial class FlameConeFX
    {
        // Silhouette -----------------------------------------------------------------
        private const int SLICES = 14;
        private const int CORE_SLICES = 9;

        /// <summary>How far down the cone the white-hot throat reaches, as a fraction.</summary>
        private const float CORE_REACH = 0.55f;

        /// <summary>Throat width as a fraction of the body's at the same distance.</summary>
        private const float CORE_WIDTH = 0.38f;

        /// <summary>
        /// How much longer than its own spacing a slice is drawn, along the axis. Below about
        /// 1.8 the stack reads as separate puffs; the wedge only closes once neighbours
        /// genuinely overlap.
        /// </summary>
        private const float SLICE_OVERLAP = 2.2f;

        /// <summary>
        /// The mouth is a point, so the first slice would be infinitely thin. A cone breath
        /// leaves a real opening — this is that opening, as a fraction of the full reach.
        /// </summary>
        private const float MOUTH_WIDTH = 0.10f;

        /// <summary>
        /// Summed alpha through the body, NOT per slice. On an additive stack a pixel receives
        /// the sum of everything over it, so raising <see cref="SLICES"/> would otherwise be a
        /// brightness dial rather than a resolution one — the arithmetic <c>VortexFunnelFX</c>
        /// records for its bands, and the reason a red vortex washed out to white when its
        /// count doubled. Dividing by the count buys detail and no light.
        /// </summary>
        private const float BODY_ALPHA_BUDGET = 3.1f;
        private const float CORE_ALPHA_BUDGET = 2.0f;

        /// <summary>How flat a circle drawn on the ground plane is. Shared with the vortex.</summary>
        private const float GROUND_SQUASH = 0.34f;

        // Motion ---------------------------------------------------------------------

        /// <summary>
        /// A flame's whole identity is that it FLICKERS. A cone at a steady brightness is read
        /// once and then filed as a texture — the lesson the vortex's discharges exist for —
        /// so the body's alpha and width run a noise wave that TRAVELS outward from the mouth.
        /// A per-slice noise sampled independently vibrates in place instead, which reads as
        /// static rather than as fire.
        /// </summary>
        private const float FLICKER_RATE = 7.5f;
        private const float FLICKER_TRAVEL = 2.6f;
        private const float FLICKER_DEPTH = 0.42f;

        /// <summary>How far a slice may lick sideways off the axis, as a fraction of its own width.</summary>
        private const float LICK_DEPTH = 0.16f;

        /// <summary>Ignition and extinction ramps, in seconds. Independent of the spell's duration.</summary>
        public const float IGNITE_SECONDS = 0.14f;
        public const float EXTINGUISH_SECONDS = 0.30f;

        // Colour ---------------------------------------------------------------------

        /// <summary>
        /// THE INTENSITY DIAL, and it is a COLOUR dial rather than an alpha one on purpose.
        /// On an additive surface alpha is COVERAGE and colour is BRIGHTNESS — the rule
        /// <c>WeaponSwapFlashFX</c> records — so reaching for the alpha budget to make fire
        /// fiercer widens it instead of hardening it, and past a point it stops being fire and
        /// becomes fog. Values above 1 are a real overdrive here, not a rounding error:
        /// measured, <c>SpriteRenderer.color</c> reads back an authored 2.400 unchanged, and
        /// both the camera and the URP asset have HDR on, so the excess survives to the
        /// framebuffer and blows the throat out to white while the flanks keep their hue.
        /// </summary>
        private const float BODY_GAIN = 2.65f;

        /// <summary>Falloff of the gain along the cone. The tip is deeper, never darker.</summary>
        private const float BODY_GAIN_TIP = 0.72f;

        /// <summary>The throat is the hottest thing on screen and is allowed to clip.</summary>
        private const float THROAT_GAIN = 2.9f;
        private const float MUZZLE_GAIN = 2.4f;

        /// <summary>
        /// Peak Light2D intensity. The light is the only part of the effect that touches the
        /// WORLD rather than the screen, so an intense fire that lights nothing reads as a
        /// sticker over the scene however hot the sprites are.
        /// </summary>
        private const float LIGHT_INTENSITY = 3.8f;

        /// <summary>
        /// Saturation floor for the body. The aura palette's <c>Core</c> is near-colourless by
        /// design — measured at saturation 0.25 for the shipped orange — which is right for a
        /// ki spine and wrong for the mouth of a flame: it made the brightest half of the cone
        /// a pale cream and left the coloured half to the DARK end of the ramp. The body now
        /// never touches that white; the throat layer owns it.
        /// </summary>
        private const float BODY_SATURATION_FLOOR = 0.88f;

        /// <summary>
        /// How far the hue cools along the cone, and it only applies to a WARM swatch. A real
        /// flame cools orange to red because that is what a black body does, so shifting the
        /// hue down is a physical statement — and a blue or violet breath has no such physics,
        /// where the same shift would swing it through cyan. Hue is left alone for those.
        /// </summary>
        private const float HUE_COOL = 0.052f;
        private const float WARM_HUE_MAX = 0.13f;
        private const float WARM_HUE_MIN = 0.92f;

        // Sorting --------------------------------------------------------------------
        private const int ORDER_SCORCH = 34;
        private const int ORDER_BODY = 50;

        /// <summary>
        /// Derived, never hand-written. The body takes <c>ORDER_BODY + i</c>, so anything that
        /// must sit over the whole stack has to clear its top — a literal that was right at one
        /// slice count is silently wrong at the next, which is how the vortex sank its own
        /// near-side debris behind the funnel.
        /// </summary>
        private const int ORDER_CORE = ORDER_BODY + SLICES + 1;
        private const int ORDER_MUZZLE = ORDER_CORE + CORE_SLICES + 1;
        private const int ORDER_PARTICLES = ORDER_MUZZLE + 2;

        private Transform _root;

        /// <summary>Sprites. Turned about Z only, so every quad still faces the camera.</summary>
        private Transform _spriteRoot;

        /// <summary>Emitters. Full <c>LookRotation</c>, so a Cone shape's +Z is the aim.</summary>
        private Transform _emitterRoot;

        private Transform _groundPlane;

        private float _length;
        private float _halfArcRad;
        private KiPalette _palette;
        private float _seed;

        private Transform[] _bodySlices;
        private SpriteRenderer[] _bodyRenderers;
        private float[] _bodyT;

        private Transform[] _coreSlices;
        private SpriteRenderer[] _coreRenderers;
        private float[] _coreT;

        private SpriteRenderer _muzzleHot;
        private SpriteRenderer _muzzleHalo;
        private SpriteRenderer _scorch;

        private ParticleSystem _fire;
        private ParticleSystem _embers;

        private GameObject _lightGo;
        private Component _light;

        private float _age;

        /// <summary>How many body slices the wedge is built from. Tests cannot name the far one without it.</summary>
        public int SliceCount { get { return SLICES; } }

        /// <summary>How far the fire reaches, in world units — the distance the damage query sweeps.</summary>
        public float Length { get { return _length; } }

        /// <summary>Half the cone's opening, in radians.</summary>
        public float HalfArcRadians { get { return _halfArcRad; } }

        /// <summary>The transform the sprite wedge hangs from. Read by tests.</summary>
        public Transform SpriteRoot { get { return _spriteRoot; } }

        /// <summary>The transform the emitters hang from. Its forward IS the aim.</summary>
        public Transform EmitterRoot { get { return _emitterRoot; } }

        /// <summary>The fire emitter, so the controller can stop it before the tail fade.</summary>
        public ParticleSystem Fire { get { return _fire; } }

        /// <summary>
        /// The cone's half-width at <paramref name="distance"/> along the axis. The single
        /// owner of the wedge's geometry: the slices are drawn from it and
        /// <see cref="ConeBreathController"/> tests targets against it, so the two cannot drift.
        /// </summary>
        public float HalfWidthAt(float distance)
        {
            return Mathf.Tan(_halfArcRad) * Mathf.Max(0f, distance) + _length * MOUTH_WIDTH * 0.5f;
        }

        /// <summary>
        /// Build the cone under <paramref name="parent"/>, aimed along <paramref name="direction"/>.
        /// </summary>
        /// <param name="length">Reach in WORLD UNITS — the distance the damage query sweeps.</param>
        /// <param name="arcDegrees">Full opening angle.</param>
        /// <param name="swatch">The resolved colour for this breath.</param>
        public static FlameConeFX Attach(Transform parent, Vector2 direction, float length,
                                         float arcDegrees, Color swatch)
        {
            var fx = new FlameConeFX
            {
                _root = parent,
                _length = Mathf.Max(0.5f, length),
                _halfArcRad = Mathf.Clamp(arcDegrees, 8f, 170f) * 0.5f * Mathf.Deg2Rad,
                _palette = KiPalette.From(swatch, 1f),
                _seed = Random.Range(0f, 128f),
            };

            ElementalSprites.EnsureAll();

            fx.BuildRoots(direction);
            fx.BuildScorch();
            fx.BuildBody();
            fx.BuildCore();
            fx.BuildMuzzle();
            fx.BuildEmitters();
            fx.AttachLight();

            // A rig built here renders one frame before Update first runs, so the envelope has
            // to be seated at zero now or the breath pops at full brightness for one frame
            // before starting to ignite — the seam every persistent spell effect records.
            fx.Tick(0f, 1f);
            return fx;
        }

        private void BuildRoots(Vector2 direction)
        {
            Vector3 dir = new Vector3(direction.x, direction.y, 0f);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            dir.Normalize();
            float deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            _spriteRoot = new GameObject("Wedge").transform;
            _spriteRoot.SetParent(_root, false);
            _spriteRoot.localRotation = Quaternion.Euler(0f, 0f, deg);

            _emitterRoot = new GameObject("Emitters").transform;
            _emitterRoot.SetParent(_root, false);
            // World +Z is perpendicular to every 2D aim, so this is exact for all facings —
            // verified against the eight compass directions to within a float epsilon.
            _emitterRoot.localRotation = Quaternion.LookRotation(dir, Vector3.forward);

            // Rotation on the CHILD, squash on the PARENT: turning an already-flattened
            // transform swings its corners up and down instead of laying the shape on the
            // floor. The split the vortex's ground layer already makes.
            _groundPlane = new GameObject("GroundPlane").transform;
            _groundPlane.SetParent(_root, false);
            _groundPlane.localScale = new Vector3(1f, GROUND_SQUASH, 1f);
        }

        private void BuildScorch()
        {
            var rot = new GameObject("ScorchAim").transform;
            rot.SetParent(_groundPlane, false);
            rot.localRotation = _spriteRoot.localRotation;

            var go = new GameObject("Scorch");
            go.transform.SetParent(rot, false);
            go.transform.localPosition = new Vector3(_length * 0.5f, 0f, 0f);
            go.transform.localScale = new Vector3(_length * 1.15f, HalfWidthAt(_length) * 2.2f, 1f);

            _scorch = go.AddComponent<SpriteRenderer>();
            _scorch.sprite = ElementalSprites.Glow;
            // The ONE non-additive layer, and it must stay that way. Everything else here is
            // light being added; this is ground being blackened, and a dark chip on an additive
            // surface adds almost nothing — the layer would vanish with nothing failing, which
            // is the note KiAuraFX and VortexFunnelFX both carry about their own ground debris.
            _scorch.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            _scorch.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            _scorch.sortingOrder = ORDER_SCORCH;
            _scorch.color = new Color(0.10f, 0.05f, 0.03f, 0f);
        }

        private void BuildBody()
        {
            _bodySlices = new Transform[SLICES];
            _bodyRenderers = new SpriteRenderer[SLICES];
            _bodyT = new float[SLICES];

            for (int i = 0; i < SLICES; i++)
            {
                float t = (i + 0.5f) / SLICES;
                _bodyT[i] = t;

                var sr = MakeSprite(_spriteRoot, "Body" + i.ToString("00"), ElementalSprites.Glow,
                                    ORDER_BODY + i, SortingConfig.LAYER_VFX);
                sr.color = WithAlpha(BodyColour(t), 0f);

                _bodySlices[i] = sr.transform;
                _bodyRenderers[i] = sr;
            }
        }

        private void BuildCore()
        {
            _coreSlices = new Transform[CORE_SLICES];
            _coreRenderers = new SpriteRenderer[CORE_SLICES];
            _coreT = new float[CORE_SLICES];

            for (int i = 0; i < CORE_SLICES; i++)
            {
                float t = (i + 0.5f) / CORE_SLICES;
                _coreT[i] = t;

                var sr = MakeSprite(_spriteRoot, "Core" + i.ToString("00"), ElementalSprites.HotCore,
                                    ORDER_CORE + i, SortingConfig.LAYER_VFX);
                sr.color = WithAlpha(Color.Lerp(Color.white, _palette.Core, t) * THROAT_GAIN, 0f);

                _coreSlices[i] = sr.transform;
                _coreRenderers[i] = sr;
            }
        }

        private void BuildMuzzle()
        {
            float mouth = _length * MOUTH_WIDTH;

            _muzzleHalo = MakeSprite(_spriteRoot, "MuzzleHalo", ElementalSprites.Halo,
                                     ORDER_MUZZLE, SortingConfig.LAYER_VFX);
            _muzzleHalo.transform.localScale = Vector3.one * (mouth * 5.5f);
            _muzzleHalo.color = WithAlpha(BodyColour(0f), 0f);

            _muzzleHot = MakeSprite(_spriteRoot, "MuzzleHot", ElementalSprites.HotCore,
                                    ORDER_MUZZLE + 1, SortingConfig.LAYER_VFX);
            _muzzleHot.transform.localScale = Vector3.one * (mouth * 2.4f);
            _muzzleHot.color = WithAlpha(Color.Lerp(Color.white, _palette.Core, 0.35f) * MUZZLE_GAIN, 0f);
        }

        private void AttachLight()
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;

            _lightGo = new GameObject("BreathLight");
            // A child of the UNSCALED sprite root, so the authored radius is the rendered one.
            _lightGo.transform.SetParent(_spriteRoot, false);
            _lightGo.transform.localPosition = new Vector3(_length * 0.45f, 0f, 0f);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(l2dType);
                var lightType = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lightType != null)
                    lightType.SetValue(_light, System.Enum.ToObject(lightType.PropertyType, 3));  // Point
                // The palette's Light is deliberately pale — right for a ki aura washing a
                // room, wrong for a torrent of fire, which throws a saturated orange.
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(
                    _light, Color.Lerp(FireHue(0.2f), Color.white, 0.18f));
                // The fire lights as far as it reaches. The old rig hung a 0.71-unit lamp on the
                // caster's hands for a spell that is supposed to be a torrent.
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _length * 0.95f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _length * 0.14f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                SetLightIntensity(0f);
            }
            catch { _light = null; }
        }

        private static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite,
                                                 int order, string layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// The body's colour at <paramref name="t01"/> along the cone: a fully saturated,
        /// FULL-VALUE flame that reddens as it goes, overdriven into HDR.
        ///
        /// <para>Holding the value at 1 is the load-bearing half. The old ramp dimmed the
        /// colour toward <c>KiPalette.Edge</c> — value 0.62 for the shipped orange — at the
        /// same time as the alpha taper was already fading it, so the far end of the cone was
        /// darkened TWICE and added almost nothing to an additive surface. Fading is the
        /// alpha's job alone; the colour only ever says what the fire IS.</para>
        /// </summary>
        private Color BodyColour(float t01)
        {
            Color c = FireHue(t01);
            float gain = Mathf.Lerp(BODY_GAIN, BODY_GAIN * BODY_GAIN_TIP, t01);
            return new Color(c.r * gain, c.g * gain, c.b * gain, 1f);
        }

        /// <summary>
        /// The flame's colour at <paramref name="t01"/> with NO overdrive — saturated, at full
        /// value, inside 0..1. Split out from <see cref="BodyColour"/> because a particle's
        /// vertex colour is packed to <c>Color32</c> and CLAMPS, so the gain that reaches the
        /// sprite wedge would be silently thrown away here. The emitters take the hue; only the
        /// sprites take the overdrive.
        /// </summary>
        public Color FireHue(float t01)
        {
            Color.RGBToHSV(_palette.Mid, out float h, out float s, out float _);

            bool warm = h <= WARM_HUE_MAX || h >= WARM_HUE_MIN;
            float hue = warm ? Mathf.Repeat(h - t01 * HUE_COOL, 1f) : h;
            float sat = Mathf.Clamp01(Mathf.Max(s, BODY_SATURATION_FLOOR) + t01 * 0.12f);
            return Color.HSVToRGB(hue, sat, 1f);
        }

        /// <summary>Preserves an HDR component above 1 — a <c>Color</c> is not clamped.</summary>
        private static Color WithAlpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }
    }
}
