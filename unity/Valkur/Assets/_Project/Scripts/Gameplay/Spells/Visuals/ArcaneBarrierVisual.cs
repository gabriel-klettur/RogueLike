using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A wall of woven magic: anchor posts pinned into the floor, a translucent hexagonal
    /// membrane knitted between them, a lattice edge along the top, glyphs drifting across
    /// the surface and a seal burnt into the ground under it.
    ///
    /// <para>WHY NOT <see cref="IceWallVisual"/> RECOLOURED. That rig is a row of opaque
    /// crystal spikes, which is the vocabulary of ICE: irregular, faceted, piled on the
    /// ground. Tinting it violet gives a purple ice wall. See <see cref="IWallVisual"/> for
    /// the rule, which this project has now recorded four times.</para>
    ///
    /// <para>THE BARRIER IS SEE-THROUGH ON PURPOSE, and that is not only taste. Every layer
    /// but the floor seal is additive and low-alpha, so the player reads a surface they can
    /// see the fight through — which is the honest picture of a spell that ships
    /// <c>blockProjectiles: 1</c> and <c>blockUnits: 0</c>. An opaque barrier that bodies walk
    /// straight through teaches the player to distrust the art.</para>
    ///
    /// <para>THE ROOT IS NEVER SCALED OR ROTATED, for the two reasons <see cref="IceWallVisual"/>
    /// records: a <c>Light2D</c> under a scaled transform renders its authored radius at some
    /// other value, and in a top-down projection a post has to stand UP on screen whichever way
    /// the barrier runs. The line direction lives in the child POSITIONS.</para>
    /// </summary>
    internal sealed partial class ArcaneBarrierVisual : IWallVisual
    {
        /// <summary>Rows of hexagons over the barrier's height. More rows, finer weave.</summary>
        private const float TargetRowHeight = 0.62f;
        private const int MinRows = 3, MaxRows = 6;

        /// <summary>
        /// Ceiling on total panels. Each is one SpriteRenderer (a second is added lazily only
        /// where damage lands), so this is the rig's real cost and it is bounded here rather
        /// than by whatever length someone authors.
        /// </summary>
        private const int MaxPanels = 132;

        /// <summary>World units of a Panel sprite's half-extent at localScale 1.</summary>
        private const float PanelHalfWidth = 0.497f, PanelHalfHeight = 0.43f;

        /// <summary>Width of an anchor shaft, world units. Its height is the barrier's.</summary>
        private const float PostWidth = 0.42f;

        public struct Config
        {
            /// <summary>Length of the barrier along <see cref="Axis"/>, world units.</summary>
            public float Length;
            /// <summary>How tall the membrane stands, world units.</summary>
            public float Height;
            /// <summary>Unit vector along the barrier.</summary>
            public Vector2 Axis;
            /// <summary>Per-cast seed, so no two barriers weave identically.</summary>
            public int Seed;
            /// <summary>The spell's own <c>particleColor</c>. Drives every colour in the rig.</summary>
            public Color Swatch;
        }

        private sealed class Panel
        {
            public Transform Root;
            public SpriteRenderer Body;
            public SpriteRenderer Crack;     // created lazily, only where damage lands
            public int Variant;              // so the fracture lines match the hexagon
            public float Along;              // signed distance from centre along the wall
            public float Size;               // localScale of the panel sprite
            public float Phase;              // shimmer phase
            public float KnitDelay;          // seconds after the cast before it weaves in
            /// <summary>0 at an anchor, 1 in the middle of a bay. Drives the unravel order.</summary>
            public float KnitRank;
            public float Knit;               // 0..1 weave-in progress
            public float Flash;              // additive hit flash, 0..1
            public float CrackAlpha;
            public bool Broken;
            public float LastBody = -1f, LastCrack = -1f;
        }

        private sealed class Post
        {
            public Transform Root;
            public SpriteRenderer Shaft;
            public SpriteRenderer Sigil;     // the turning disc on the floor
            public float Along;
            public float Delay;
            public float Rise;               // 0..1
        }

        private Transform _root;
        private Config _config;
        private ArcaneBarrierPalette _palette;
        private System.Random _rng;

        private readonly List<Panel> _panels = new List<Panel>();
        private readonly List<Post> _posts = new List<Post>();
        private readonly List<SpriteRenderer> _edges = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _haze = new List<SpriteRenderer>();
        private SpriteRenderer _seal;

        private int _rows;
        private float _age;
        private float _lightBaseIntensity;

        /// <summary>The order every part is offset from. See <see cref="OrderFor"/>.</summary>
        private int _baseOrder;

        public static ArcaneBarrierVisual Build(Transform root, Config config)
        {
            ArcaneSprites.EnsureAll();
            ElementalSprites.EnsureAll();

            var visual = new ArcaneBarrierVisual
            {
                _root = root,
                _config = config,
                _palette = ArcaneBarrierPalette.From(config.Swatch),
                _rng = new System.Random(config.Seed),
            };

            visual._baseOrder = SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, root.position.y);
            // BEFORE any Build*: every sorting order above the weave is derived from the row
            // count, and the posts are built first. Left at zero here the anchors take
            // baseOrder+2 while a four-row weave reaches baseOrder+3, and the posts render
            // BEHIND the membrane they are holding up.
            visual._rows = visual.ResolveRows();

            visual.BuildSeal();
            visual.BuildPosts();
            visual.BuildPanels();
            visual.BuildEdges();
            visual.BuildHaze();
            visual.BuildRunes();
            visual.BuildMotes();
            visual.BuildLights();
            return visual;
        }

        private float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private Vector3 AlongAxis(float distance)
            => new Vector3(_config.Axis.x * distance, _config.Axis.y * distance, 0f);

        /// <summary>
        /// Where a part sits in the sort, as an offset from the barrier's own world Y.
        ///
        /// <para>Every offset above the panels is DERIVED from <see cref="_rows"/> rather than
        /// written down, because the panel stack occupies <c>_rows</c> orders and a hand-picked
        /// constant that clears it at three rows silently sinks the posts behind the weave at
        /// six. <c>VortexFunnelFX</c> lost its near-side debris to exactly that when its band
        /// count doubled.</para>
        /// </summary>
        private int OrderFor(Part part, int row = 0) => part switch
        {
            Part.Haze => _baseOrder - 2,
            Part.Panel => _baseOrder + row,
            Part.Crack => _baseOrder + _rows + 1,
            Part.Post => _baseOrder + _rows + 2,
            Part.Edge => _baseOrder + _rows + 3,
            Part.Rune => _baseOrder + _rows + 4,
            _ => _baseOrder,
        };

        private enum Part { Haze, Panel, Crack, Post, Edge, Rune }

        /// <summary>
        /// The mark burnt into the floor along the barrier's line. The rig's only NON-additive
        /// layer: see <see cref="ArcaneSprites"/> for why deleting it would cost the effect its
        /// claim on the world.
        /// </summary>
        private void BuildSeal()
        {
            var go = new GameObject("GroundSeal");
            go.transform.SetParent(_root, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(_config.Axis.y, _config.Axis.x) * Mathf.Rad2Deg);

            _seal = go.AddComponent<SpriteRenderer>();
            _seal.sprite = ArcaneSprites.Seal;
            _seal.color = WithAlpha(_palette.Seal, 0f);       // faded in by the eruption
            _seal.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            _seal.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            _seal.sortingOrder = 40;

            // The Seal sprite is 2x1 units. The band reaches a little past the end posts and
            // is shallow in depth, which is what an inscription on the floor looks like from
            // above — a deep one reads as a puddle.
            go.transform.localScale = new Vector3(
                (_config.Length * 1.10f) / 2f,
                Mathf.Clamp(_config.Height * 0.30f, 0.34f, 0.85f),
                1f);
        }

        /// <summary>
        /// The anchor posts. Two ends plus interior nodes, spaced so no bay is much wider than
        /// the barrier is tall — a bay far wider than it is high has nothing holding its middle
        /// and the membrane reads as unsupported.
        /// </summary>
        private void BuildPosts()
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / 2.2f) + 1, 2, 6);

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float along = (t - 0.5f) * _config.Length;

                var go = new GameObject("Anchor");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = AlongAxis(along);

                var post = new Post
                {
                    Root = go.transform,
                    Along = along,
                    // The ENDS go first and the middle follows: a ward is staked out and then
                    // filled in. Middle-first would read as the barrier growing outward, which
                    // is what the ice wall does and is the opposite statement.
                    Delay = (1f - Mathf.Abs(t - 0.5f) * 2f) * 0.07f + Range(0f, 0.02f),
                };

                var shaftGo = new GameObject("Shaft");
                shaftGo.transform.SetParent(go.transform, false);
                ArcaneSprites.ScalePost(shaftGo.transform, PostWidth, _config.Height * 1.06f);
                post.Shaft = Paint(shaftGo, ArcaneSprites.Post, _palette.Lattice,
                    additive: true, SortingConfig.LAYER_ENTITIES, OrderFor(Part.Post));

                var sigilGo = new GameObject("Sigil");
                sigilGo.transform.SetParent(go.transform, false);
                // Squashed on Y because it lies on the FLOOR and the camera looks at it at an
                // angle. One squash on this transform, with the spin applied to it directly:
                // the sprite is radially symmetric, so a spin here needs no separate child.
                sigilGo.transform.localScale = new Vector3(0.95f, 0.95f * 0.42f, 1f);
                post.Sigil = Paint(sigilGo, ArcaneSprites.Sigil, _palette.Rune,
                    additive: true, SortingConfig.LAYER_FLOOR_DECALS, 42);

                _posts.Add(post);
            }
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static SpriteRenderer Paint(GameObject go, Sprite sprite, Color color,
            bool additive, string layer, int order)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
