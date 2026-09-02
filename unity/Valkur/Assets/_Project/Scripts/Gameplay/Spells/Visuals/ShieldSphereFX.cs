using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A sphere of light and particles enclosing a character: a faceted shell, motes orbiting
    /// it on great circles, a Fresnel rim, and ripples that cross the surface when something
    /// is turned away.
    ///
    /// <para>WHY IT IS NOT A STACK OF CONCENTRIC SPRITES, which is what the shield was for as
    /// long as it existed. Four centred discs on <c>LAYER_VFX</c> all draw IN FRONT of the
    /// character, so nothing is ever enclosed by anything — it read as a decal on the lens,
    /// the same failure the single-system weather effects had. A sphere has a FRONT AND A
    /// BACK, and the character is between them. Splitting every layer against the caster's own
    /// sorting order is the single thing that sells enclosure in a 2D scene; no amount of
    /// shading on a flat disc substitutes for it.</para>
    ///
    /// <para>FOUR LAYERS, each answering a different half of "is this a sphere":</para>
    /// <list type="bullet">
    /// <item><b>Rim</b> — <c>ElementalSprites.Ring</c>, whose bright band peaks at normalized
    /// radius 0.78, pinned so the band lands exactly on the authored radius. A sphere seen from
    /// outside is bright at its silhouette and transparent through its middle, and this is that
    /// edge. It draws in FRONT of everything because a silhouette wraps around.</item>
    /// <item><b>Facets</b> — a Fibonacci lattice of hexagons rotating as one rigid shell,
    /// foreshortened against the curve and lit by a Fresnel term. These say the sphere has a
    /// SURFACE. They also carry the impact ripple.</item>
    /// <item><b>Motes</b> — each on its own great circle at its own tilt. These say the sphere
    /// has a VOLUME: a mote crossing behind the character shrinks, dims and sorts behind them,
    /// then swells and comes back in front. Nothing else in the rig can state that.</item>
    /// <item><b>Interior fill + sheen</b> — a faint tint inside the shell so the character
    /// reads as being within something, and a highlight sliding across the front like light on
    /// curved glass.</item>
    /// </list>
    ///
    /// <para>THE SPHERE PROJECTS TO A CIRCLE, and is deliberately NOT squashed. Every other
    /// round thing in this project — the ki aura's ground pulses, an area telegraph, a puddle —
    /// lies on the FLOOR and is flattened on Y because the camera looks at the floor at an
    /// angle. This sphere is in VIEW space, centred on the body rather than resting on the
    /// ground, so flattening it would make it read as a disc lying under the character's feet,
    /// which is the exact opposite of the effect.</para>
    ///
    /// <para>The root is never scaled or rotated, for the reason recorded across this folder:
    /// a <c>Light2D</c> under a scaled transform renders its authored radius at some other
    /// value. Every child carries an absolute world size instead.</para>
    /// </summary>
    internal sealed partial class ShieldSphereFX
    {
        /// <summary>How long the shell takes to assemble out of its incoming motes.</summary>
        private const float AssembleSeconds = 0.55f;

        private const int FACET_COUNT = 30;
        private const int MOTE_COUNT = 28;
        private const int RIPPLE_POOL = 4;

        // Offsets from the caster's own sorting order. The character sits at 0, so anything
        // negative is behind them and inside the far wall of the sphere.
        private const int FILL_ORDER = -4;
        private const int BACK_ORDER = -3;
        private const int FRONT_ORDER = 3;
        private const int RIM_ORDER = 4;
        private const int SHEEN_ORDER = 5;
        private const int FLASH_ORDER = 6;

        /// <summary>
        /// <c>ElementalSprites.Ring</c>'s bright band peaks at normalized radius 0.78, so this
        /// converts a world radius into the scale that puts the band exactly there. Getting it
        /// wrong is invisible in code and brutal on screen — it is what left the arcane flame's
        /// only hard contour 40 % inside the circle that actually hurt.
        /// </summary>
        private const float RING_BAND_RADIUS = 0.39f;

        public struct Config
        {
            /// <summary>Derived from the spell's one authored swatch. See <see cref="KiPalette"/>.</summary>
            public KiPalette Palette;
            /// <summary>Sphere radius in WORLD UNITS. Not pixels — see <c>ShieldExecutor</c>.</summary>
            public float Radius;
            /// <summary>Owner-relative centre of the caster's silhouette; the sphere centres here.</summary>
            public Vector3 BodyOffset;
            public int Seed;
        }

        /// <summary>One hexagonal cell of the shell, fixed in the shell's own frame.</summary>
        private sealed class Facet
        {
            public Transform Root;
            public SpriteRenderer Renderer;
            /// <summary>Unit direction in the SHELL's frame. The live direction is this rotated.</summary>
            public Vector3 LatticeDirection;
            public float RestAlpha;
            public float Size;
            /// <summary>Outward velocity while the shell is coming apart.</summary>
            public float BreakSpeed;
            public float BreakSpin;
            public bool InFront;
        }

        /// <summary>One mote running a great circle at its own tilt and rate.</summary>
        private sealed class Mote
        {
            public Transform Root;
            public SpriteRenderer Renderer;
            /// <summary>Orthonormal basis of the orbit plane: <c>p(t) = U cos t + V sin t</c>.</summary>
            public Vector3 U, V;
            public float Speed;
            public float Phase;
            public float Size;
            /// <summary>Fraction of the radius this mote rides above the shell, for thickness.</summary>
            public float Shell;
            /// <summary>Live outward displacement from an impact, springs back to 0.</summary>
            public float Push;
            public bool InFront;
        }

        /// <summary>An expanding band of light crossing the shell from a point of contact.</summary>
        private struct Ripple
        {
            public Vector3 Contact;
            public float Age;
            public float Strength;
            public bool Active;
        }

        private Transform _root;
        private Config _config;
        private System.Random _rng;
        private float _age;
        private int _baseOrder;

        private SpriteRenderer _rim;
        private SpriteRenderer _fill;
        private SpriteRenderer _sheen;
        private SpriteRenderer _flash;
        private Transform _flashRoot;

        private readonly List<Facet> _facets = new List<Facet>();
        private readonly List<Mote> _motes = new List<Mote>();
        private readonly Ripple[] _ripples = new Ripple[RIPPLE_POOL];
        private int _nextRipple;

        private Quaternion _shellRotation = Quaternion.identity;
        private Vector3 _shellAxis = Vector3.up;
        private float _shellSpeed = 16f;

        private GameObject _lightGo;
        private Component _light;

        /// <summary>Where the last blocked hit landed on the shell, in world space.</summary>
        public Vector3 LastContactPoint { get; private set; }

        public static ShieldSphereFX Attach(Transform root, Config config)
        {
            var fx = new ShieldSphereFX();
            fx.Build(root, config);
            return fx;
        }

        private void Build(Transform root, Config config)
        {
            _root = root;
            _config = config;
            _rng = new System.Random(config.Seed);

            ElementalSprites.EnsureAll();
            ShieldSprites.EnsureAll();

            // A tilted axis rather than a screen-aligned one: a shell turning about the screen
            // Y axis has facets sliding strictly sideways, which the eye reads as a cylinder.
            _shellAxis = new Vector3(Range(-0.45f, 0.45f), 1f, Range(-0.45f, 0.45f)).normalized;
            _shellSpeed = Range(13f, 19f);

            BuildShell();
            BuildMotes();
            BuildLight();
        }

        /// <summary>
        /// Re-seat every layer around the caster's live sorting order.
        ///
        /// <para>Their order moves with their Y — <c>YSortEntity</c> rewrites it whenever they
        /// walk — so a value captured once at build time is correct only while they stand
        /// still. Here the failure is worse than for an aura: the whole illusion is that half
        /// the sphere is behind the character, so a stale base pops the back hemisphere in
        /// front and the sphere flattens into a disc for as long as they are moving.</para>
        /// </summary>
        public void RebaseSortingOrder(int casterOrder)
        {
            _baseOrder = casterOrder;

            if (_fill != null) _fill.sortingOrder = casterOrder + FILL_ORDER;
            if (_rim != null) _rim.sortingOrder = casterOrder + RIM_ORDER;
            if (_sheen != null) _sheen.sortingOrder = casterOrder + SHEEN_ORDER;
            if (_flash != null) _flash.sortingOrder = casterOrder + FLASH_ORDER;

            // Force the per-element halves to be rewritten on the next tick even if their
            // front/back answer has not changed.
            for (int i = 0; i < _facets.Count; i++)
                _facets[i].Renderer.sortingOrder = casterOrder + (_facets[i].InFront ? FRONT_ORDER : BACK_ORDER);
            for (int i = 0; i < _motes.Count; i++)
                _motes[i].Renderer.sortingOrder = casterOrder + (_motes[i].InFront ? FRONT_ORDER : BACK_ORDER);
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("ShieldLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localPosition = _config.BodyOffset;

            try
            {
                _light = _lightGo.AddComponent(lightType);
                var lightTypeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. Anything else here is the
                // day/night bug that left every torch a cookie-less Sprite light.
                if (lightTypeProp != null)
                    lightTypeProp.SetValue(_light, System.Enum.ToObject(lightTypeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _config.Palette.Light);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.4f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _config.Radius * 2.1f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _config.Radius * 0.5f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
            }
            catch { }
        }

        // ── shared helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every piece is additive (<c>SrcAlpha/One</c>). A shield is LIGHT, and on the alpha
        /// material the brightest pixel a glow can produce is its own colour — a pale blue
        /// shell over pale ground would be a net luminance LOSS, which is how the old rig
        /// managed to look dimmer than the world it was protecting.
        /// </summary>
        private SpriteRenderer MakeSprite(string name, Sprite sprite, Transform parent, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            // ENTITIES, not VFX. On the VFX layer every piece draws in front of the character
            // and the back hemisphere cannot exist.
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = order;
            return sr;
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        private static void SetColor(SpriteRenderer renderer, Color rgb, float alpha)
        {
            if (renderer == null) return;
            rgb.a = Mathf.Clamp01(alpha);
            renderer.color = rgb;
        }
    }
}
