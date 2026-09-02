using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A sustained energy charge burning off a character: the aura column, the ki streaming
    /// off it, the ground it tears up, and the lightning that crawls over it once it is
    /// violent enough.
    ///
    /// <para>WHY IT IS NOT AN <see cref="AreaFXRig"/> OR AN <see cref="IceWallVisual"/>. Those
    /// two are a DISC and a LINE. This is a COLUMN — it is tall, it is anchored to a moving
    /// body rather than to a patch of ground, and its silhouette flickers, which is the one
    /// thing that separates fire from a light. Nothing already here draws that.</para>
    ///
    /// <para>SEVEN LAYERS, and each is doing a different job:</para>
    /// <list type="bullet">
    /// <item><b>Column</b> — the smooth mass of the aura, hugging the body. Featureless on
    /// purpose: detail here competes with the tongues.</item>
    /// <item><b>Tongues</b> — flame silhouettes at their own scales and phases. These carry the
    /// flicker, and the flicker is what makes it read as burning rather than as glowing.</item>
    /// <item><b>Haze</b> — a wide faint halo so the column ends in something instead of at a
    /// hard edge.</item>
    /// <item><b>Rising ki</b> — sparks streaming upward off the body. The most recognisable
    /// element of the whole idea.</item>
    /// <item><b>Debris</b> — chips of ground lifting and turning. The only opaque layer, and
    /// the only one that says the world is being affected rather than just lit.</item>
    /// <item><b>Ground rings</b> — pressure leaving in pulses, flat on the floor.</item>
    /// <item><b>Lightning</b> — above <see cref="KiPalette.LightningThreshold"/> only.</item>
    /// </list>
    ///
    /// <para>The root is never scaled or rotated, for the reason recorded across this folder:
    /// a <c>Light2D</c> under a scaled transform renders its authored radius at some other
    /// value. Every child carries an absolute world size instead.</para>
    /// </summary>
    internal sealed partial class KiAuraFX
    {
        /// <summary>How long the ignition flare takes to reach the steady burn.</summary>
        private const float IgnitionSeconds = 0.45f;

        private const int RING_POOL = 3;
        private const int BOLT_POOL = 7;

        // Offsets from the caster's own sorting order. Negative burns behind them, positive
        // in front. The debris sits at +1 because a chip of rock torn off the floor is
        // between the camera and the character it was torn out from under.
        private const int TONGUE_ORDER = -2;
        private const int KI_STREAM_ORDER = 3;
        private const int DEBRIS_ORDER = 1;

        public struct Config
        {
            public KiPalette Palette;
            /// <summary>World size of the caster's silhouette. Everything is measured off it.</summary>
            public Vector2 BodySize;
            /// <summary>Owner-relative centre of that silhouette.</summary>
            public Vector3 BodyOffset;
            /// <summary>Footprint the aura disturbs on the ground, world units.</summary>
            public float GroundRadius;
            public int Seed;
        }

        private sealed class Tongue
        {
            public Transform Root;
            public SpriteRenderer Renderer;
            public Color Tint;
            public float BaseWidth, BaseHeight;
            public float Phase, FlickerSpeed, SwayAmount;
            public float LeanDegrees;
            public Vector3 Anchor;
        }

        private Transform _root;
        private Config _config;
        private System.Random _rng;
        private float _age;

        private SpriteRenderer _column;
        private SpriteRenderer _haze;
        private SpriteRenderer _hot;
        private readonly List<Tongue> _tongues = new List<Tongue>();
        private readonly List<SpriteRenderer> _rings = new List<SpriteRenderer>();
        private readonly float[] _ringStart = new float[RING_POOL];
        private readonly List<SpriteRenderer> _bolts = new List<SpriteRenderer>();
        private readonly float[] _boltUntil = new float[BOLT_POOL];

        private ParticleSystem _kiStream;
        private ParticleSystem _debris;
        private ParticleSystemRenderer _kiStreamRenderer;
        private ParticleSystemRenderer _debrisRenderer;

        // Every Entities-layer piece and how far it sits from the CASTER's own order. Kept as
        // a pair of lists rather than recomputed, because the base moves whenever the caster
        // walks (YSortEntity rewrites their order on every Y change) and the offsets do not.
        private readonly List<SpriteRenderer> _ordered = new List<SpriteRenderer>();
        private readonly List<int> _orderOffsets = new List<int>();
        private GameObject _lightGo;
        private Component _light;

        private float _ringPeriod;
        private float _nextRing;
        private int _nextRingSlot;
        private float _nextBolt;

        /// <summary>Steady-state envelope, 0 while igniting and 1 once the aura is settled.</summary>
        public float Ignition => Mathf.Clamp01(_age / IgnitionSeconds);

        public static KiAuraFX Attach(Transform root, Config config)
        {
            KiSprites.EnsureAll();
            ElementalSprites.EnsureAll();

            var fx = new KiAuraFX
            {
                _root = root,
                _config = config,
                _rng = new System.Random(config.Seed),
            };

            // Faster pulses the harder it burns: at the top of the ladder the ground is being
            // hit roughly three times a second, at the bottom barely once every two.
            fx._ringPeriod = Mathf.Lerp(2.1f, 0.34f, config.Palette.Intensity);
            fx._nextRing = 0f;

            fx.BuildColumn();
            fx.BuildTongues();
            fx.BuildRings();
            fx.BuildBolts();
            fx.BuildEmitters();
            fx.BuildLight();
            return fx;
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private float Intensity => _config.Palette.Intensity;

        /// <summary>
        /// How tall the aura stands, as a multiple of the character's own height.
        ///
        /// <para>Measured, not guessed. The first values tried put the void charge's tongues
        /// at 9.8 world units off a 2.5-unit body — the camera is 10 units tall at ortho 5, so
        /// the aura filled the screen and stopped reading as something coming off a person.
        /// These land the tallest tongue near twice the body, with the ignition flare briefly
        /// pushing past that, which is what a flare is for.</para>
        /// </summary>
        private const float COLUMN_HEIGHT_CALM = 1.30f;
        private const float COLUMN_HEIGHT_FIERCE = 2.10f;
        private const float TONGUE_HEIGHT_CALM = 1.05f;
        private const float TONGUE_HEIGHT_FIERCE = 1.75f;

        private void BuildColumn()
        {
            float width = _config.BodySize.x * Mathf.Lerp(1.45f, 2.15f, Intensity);
            float height = _config.BodySize.y * Mathf.Lerp(COLUMN_HEIGHT_CALM, COLUMN_HEIGHT_FIERCE, Intensity);

            // Behind the character: the column is the mass they are standing IN FRONT of.
            _column = MakeSprite("Column", KiSprites.Column, _config.Palette.Core, -3);
            KiSprites.ScaleTongue(_column.transform, width, height);
            _column.transform.localPosition = Vector3.zero;

            _haze = MakeSprite("Haze", ElementalSprites.Halo, _config.Palette.Edge, -4);
            _haze.transform.localPosition = _config.BodyOffset;
            _haze.transform.localScale = new Vector3(width * 2.6f, height * 1.5f, 1f);

            // The one layer IN FRONT of the character, and it is deliberately small and pale:
            // it is the light of the aura falling on them, not another aura.
            _hot = MakeSprite("BodyHot", ElementalSprites.Glow, _config.Palette.Core, 2);
            _hot.transform.localPosition = _config.BodyOffset;
            _hot.transform.localScale = new Vector3(_config.BodySize.x * 1.5f,
                                                    _config.BodySize.y * 1.15f, 1f);
        }

        private void BuildTongues()
        {
            // A calm charge is a handful of slow flames; a violent one is a wall of them.
            int count = Mathf.RoundToInt(Mathf.Lerp(5f, 15f, Intensity));

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                // Tall in the middle, short at the flanks — the aura has the shape of the
                // body it is coming off, not the shape of the box around it.
                float profile = 0.55f + 0.45f * Mathf.Sin(Mathf.PI * t);

                var tongue = new Tongue
                {
                    Tint = Color.Lerp(_config.Palette.Mid, _config.Palette.Edge, Range(0f, 0.8f)),
                    BaseWidth = _config.BodySize.x * Range(0.34f, 0.72f),
                    BaseHeight = _config.BodySize.y * profile *
                                 Mathf.Lerp(TONGUE_HEIGHT_CALM, TONGUE_HEIGHT_FIERCE, Intensity) *
                                 Range(0.80f, 1.20f),
                    Phase = Range(0f, Mathf.PI * 2f),
                    // Faster flicker the hotter it burns. Below ~6 Hz a flame reads as a
                    // wobbling sprite; above ~16 it reads as noise.
                    FlickerSpeed = Mathf.Lerp(6f, 15f, Intensity) * Range(0.8f, 1.25f),
                    SwayAmount = _config.BodySize.x * Range(0.04f, 0.13f),
                    LeanDegrees = (t - 0.5f) * 26f + Range(-9f, 9f),
                };

                var go = new GameObject("Tongue");
                go.transform.SetParent(_root, false);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, tongue.LeanDegrees);
                tongue.Anchor = new Vector3((t - 0.5f) * _config.BodySize.x * 1.15f,
                                            Range(-0.04f, 0.10f), 0f);
                go.transform.localPosition = tongue.Anchor;
                tongue.Root = go.transform;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = KiSprites.Tongue(_rng.Next(KiSprites.TongueVariants));
                sr.color = new Color(tongue.Tint.r, tongue.Tint.g, tongue.Tint.b, 0f);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = TONGUE_ORDER;
                _ordered.Add(sr);
                _orderOffsets.Add(TONGUE_ORDER);
                tongue.Renderer = sr;

                _tongues.Add(tongue);
            }
        }

        private void BuildRings()
        {
            for (int i = 0; i < RING_POOL; i++)
            {
                var sr = MakeSprite("GroundRing_" + i, ElementalSprites.Ring, _config.Palette.Mid, 0,
                    followsCaster: false);
                sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
                sr.sortingOrder = 45 + i;
                _rings.Add(sr);
                _ringStart[i] = float.NegativeInfinity;
            }
        }

        private void BuildBolts()
        {
            if (!_config.Palette.HasLightning) return;

            for (int i = 0; i < BOLT_POOL; i++)
            {
                var sr = MakeSprite("Bolt_" + i, ElementalSprites.Bolt, _config.Palette.Core, 4);
                _bolts.Add(sr);
                _boltUntil[i] = float.NegativeInfinity;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("KiLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localPosition = _config.BodyOffset;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProperty = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4 — the documented trap.
                if (typeProperty != null)
                    typeProperty.SetValue(_light, System.Enum.ToObject(typeProperty.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _config.Palette.Light);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0f);
                ElementalProjectileVisual.GetLight2DOuterProp()
                    ?.SetValue(_light, Mathf.Lerp(2.6f, 6.5f, Intensity));
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.4f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        /// <summary>
        /// Re-seat every Entities-layer piece around <paramref name="casterOrder"/>, so the
        /// column burns BEHIND the character and the light of it falls in front. Called
        /// whenever the caster's own sorting order changes.
        /// </summary>
        public void RebaseSortingOrder(int casterOrder)
        {
            for (int i = 0; i < _ordered.Count; i++)
                if (_ordered[i] != null) _ordered[i].sortingOrder = casterOrder + _orderOffsets[i];

            if (_kiStreamRenderer != null) _kiStreamRenderer.sortingOrder = casterOrder + KI_STREAM_ORDER;
            if (_debrisRenderer != null) _debrisRenderer.sortingOrder = casterOrder + DEBRIS_ORDER;
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, int orderOffset,
            bool followsCaster = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(color.r, color.g, color.b, 0f);
            // Additive: on the alpha material the brightest pixel a glow can produce is its
            // own colour, and an aura that cannot blow out to white is not on fire.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = orderOffset;
            if (followsCaster) { _ordered.Add(sr); _orderOffsets.Add(orderOffset); }
            return sr;
        }
    }
}
