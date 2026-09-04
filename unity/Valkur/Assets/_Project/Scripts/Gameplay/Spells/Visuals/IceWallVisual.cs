using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The drawn half of an ice wall: a row of crystals standing along a line, the frost
    /// patch they grow out of, the cold haze above them and the light they throw.
    ///
    /// <para>WHY NOT <see cref="AreaFXRig"/>. That rig is four concentric DISCS, which is
    /// the right vocabulary for a puddle or a vortex and the wrong one for a barrier: a wall
    /// is a line, and stretching a disc onto a line gives an ellipse, not a wall. The old
    /// ice wall did exactly that, on top of a quad the executor had sized to 0.78 x 0.05
    /// world units, and the whole effect came to less than one screen pixel of height.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED OR ROTATED. Two of this project's recorded traps meet
    /// here: a <c>Light2D</c> parented under a scaled transform renders its authored radius
    /// at some other value (which is what <c>WorldLightLoader</c>'s counter-scale exists to
    /// undo), and in a top-down projection a crystal has to stand UP on screen no matter
    /// which way the barrier runs. So the line direction lives in the child POSITIONS, every
    /// child carries an absolute world size, and the only rotated thing in the hierarchy is
    /// the collider box.</para>
    /// </summary>
    internal sealed partial class IceWallVisual : IWallVisual
    {
        /// <summary>Target gap between crystal centres. Sets how many shards a length gets.</summary>
        private const float ShardSpacing = 0.52f;
        private const int MinShards = 5;
        private const int MaxShards = 26;

        /// <summary>
        /// How far up-screen the back row sits. It has to be more than a pixel — the two rows
        /// are separated in the sort by their Y, and the eye needs the parallax to read the
        /// wall as having depth rather than as one flat picket fence.
        /// </summary>
        private const float BackRowOffsetY = 0.20f;

        public struct Config
        {
            /// <summary>Length of the barrier along <see cref="Axis"/>, world units.</summary>
            public float Length;
            /// <summary>How tall the tallest crystal stands, world units.</summary>
            public float Height;
            /// <summary>Unit vector along the barrier.</summary>
            public Vector2 Axis;
            /// <summary>Per-cast seed, so two walls are never the same formation.</summary>
            public int Seed;
        }

        private sealed class Shard
        {
            public Transform Root;
            public SpriteRenderer Body, Rim, Facet, Crack;
            public float T;              // 0..1 along the wall
            public float Along;          // signed distance from the centre, world units
            public float Width, Height;  // world units
            public float Phase;          // shimmer phase
            public float BirthDelay;     // seconds after the cast before it rises
            public float Rise;           // 0..1 eruption progress
            public float Flash;          // additive hit flash, 0..1
            public float CrackAlpha;     // fracture overlay, ramped by accumulated damage
            public bool Broken;
            public bool BackRow;
            public Vector3 BaseLocal;    // resting local position of the base
            public float BaseAlpha;      // row-dependent body alpha
            public float LastVisible = -1f;  // last written opaque alpha, to skip redundant writes
            public float LastCrack = -1f;
        }

        private Transform _root;
        private Config _config;
        private readonly List<Shard> _shards = new List<Shard>();
        private SpriteRenderer _rime;
        private readonly List<SpriteRenderer> _auras = new List<SpriteRenderer>();
        private ParticleSystem _mist;
        private ParticleSystem _sparkle;
        private readonly List<GameObject> _lights = new List<GameObject>();
        private readonly List<Component> _lightComponents = new List<Component>();

        private float _age;
        private float _lightBaseIntensity;
        private System.Random _rng;

        public static IceWallVisual Build(Transform root, Config config)
        {
            IceSprites.EnsureAll();
            ElementalSprites.EnsureAll();

            var visual = new IceWallVisual
            {
                _root = root,
                _config = config,
                _rng = new System.Random(config.Seed),
            };

            visual.BuildRime();
            visual.BuildShards();
            visual.BuildAura();
            visual.BuildParticles();
            visual.BuildLights();
            return visual;
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private Vector3 AlongAxis(float distance) => new Vector3(
            _config.Axis.x * distance, _config.Axis.y * distance, 0f);

        /// <summary>
        /// Sorting order for something standing at <paramref name="localY"/> on the wall.
        /// Derived from the WORLD Y of that piece so the player passing in front of or
        /// behind the barrier sorts correctly against each crystal individually — a single
        /// order for the whole wall gets one of the two wrong at every angle.
        /// </summary>
        private int OrderAt(float localY, int part)
            => SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, _root.position.y + localY) + part;

        private void BuildRime()
        {
            var go = new GameObject("GroundRime");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(_config.Axis.y, _config.Axis.x) * Mathf.Rad2Deg);

            _rime = go.AddComponent<SpriteRenderer>();
            _rime.sprite = IceSprites.Rime;
            _rime.color = new Color(0.72f, 0.92f, 1f, 0f);   // faded in by the eruption
            _rime.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            _rime.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            _rime.sortingOrder = 40;

            // The sprite is 2x1 units; the patch reaches a little past the crystals at each
            // end and about a third of the height in depth, which is what frost spreading
            // out from a formation looks like from above.
            go.transform.localScale = new Vector3(
                (_config.Length * 1.18f) / 2f,
                Mathf.Max(0.5f, _config.Height * 0.42f),
                1f);
        }

        private void BuildShards()
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / ShardSpacing), MinShards, MaxShards);
            float spacing = _config.Length / count;

            // The back row is thinner on purpose: a second full row would read as a hedge.
            int backCount = Mathf.Max(2, count / 2);

            for (int i = 0; i < backCount; i++)
                CreateShard((i + 0.5f) / backCount, spacing * 1.6f, backRow: true);
            for (int i = 0; i < count; i++)
                CreateShard((i + 0.5f) / count, spacing, backRow: false);
        }

        private void CreateShard(float t, float spacing, bool backRow)
        {
            // Tall in the middle, low at the ends. A row of equal spikes is a fence; the
            // arch is what makes it read as one crystal formation that erupted at a point.
            float profile = 0.55f + 0.45f * Mathf.Sin(Mathf.PI * t);
            float height = _config.Height * profile * Range(0.82f, 1.18f) * (backRow ? 0.74f : 1f);
            float width = spacing * Range(0.95f, 1.45f) * (backRow ? 0.85f : 1f);

            var shard = new Shard
            {
                T = t,
                Width = width,
                Height = height,
                Phase = Range(0f, Mathf.PI * 2f),
                BackRow = backRow,
                BaseAlpha = backRow ? 0.62f : 1f,
                // The eruption travels OUTWARD from the middle, which is where the caster
                // aimed. Ends-first would read as the wall closing in on the player.
                BirthDelay = Mathf.Abs(t - 0.5f) * 0.30f + Range(0f, 0.04f) + (backRow ? 0.05f : 0f),
            };

            float alongOffset = (t - 0.5f) * _config.Length + Range(-spacing * 0.14f, spacing * 0.14f);
            shard.Along = alongOffset;
            float depth = (backRow ? BackRowOffsetY : 0f) + Range(-0.06f, 0.06f);
            shard.BaseLocal = AlongAxis(alongOffset) + new Vector3(0f, depth, 0f);

            var go = new GameObject(backRow ? "ShardBack" : "Shard");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = shard.BaseLocal;
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                (t - 0.5f) * 22f + Range(-11f, 11f));
            shard.Root = go.transform;

            int variant = _rng.Next(IceSprites.VariantCount);
            int order = OrderAt(depth, 0);

            shard.Body = MakePart(go.transform, "Body", IceSprites.Body(variant),
                new Color(1f, 1f, 1f, 0f), order, additive: false, width, height);
            shard.Crack = MakePart(go.transform, "Crack", IceSprites.Crack(variant),
                new Color(1f, 1f, 1f, 0f), order + 1, additive: false, width, height);
            shard.Facet = MakePart(go.transform, "Facet", IceSprites.Facet(variant),
                new Color(1f, 1f, 1f, 0f), order + 2, additive: true, width, height);
            shard.Rim = MakePart(go.transform, "Rim", IceSprites.Rim(variant),
                new Color(1f, 1f, 1f, 0f), order + 3, additive: true, width, height);

            _shards.Add(shard);
        }

        private static SpriteRenderer MakePart(Transform parent, string name, Sprite sprite,
            Color color, int order, bool additive, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            IceSprites.ScaleShard(go.transform, width, height);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            // Additive because on the alpha material the brightest pixel a highlight can
            // produce is its own colour — a rim light that cannot blow out is not a rim
            // light. SharedAdditiveMaterial is SrcAlpha/One, so alpha still fades it.
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
