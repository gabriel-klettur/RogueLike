using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A charged DOME the caster carries: a shell of charge nodes turning around them, a faint
    /// silhouette, a ground ring pinned to the real reach, and arcs that crawl the shell.
    ///
    /// <para>THE CASTER STANDS INSIDE IT. Every node carries a DEPTH — the z of its point on the
    /// sphere — and three things read off that depth and must agree: a node further away is
    /// SMALLER, DIMMER and sorted BEHIND the caster's own live order. Getting one of the three
    /// wrong makes the motion read as sliding rather than as turning, and getting all three
    /// wrong is a flat disc, which is what <c>sphere_magic_shield</c> was before
    /// <see cref="ShieldSphereFX"/> and what the previous static field was: <c>AuraController</c>
    /// drew a flat rune on the floor in hardcoded GOLD AND GREEN, so a Lightning spell authoring
    /// <c>(0.95, 0.92, 0.50)</c> rendered as a holy healing circle.</para>
    ///
    /// <para>THE ORDER IS REBASED EVERY FRAME. <c>YSortEntity</c> rewrites the caster's sorting
    /// order whenever they walk, so a base captured once at build time pops the far hemisphere
    /// in front of the character the first time they take a step.</para>
    ///
    /// <para>IT FOLLOWS, IT DOES NOT PARENT. <c>AuraExecutor</c> used to <c>SetParent</c> the
    /// whole rig onto the caster, which inherits the entity's scale — and a scaled parent renders
    /// a <c>Light2D</c> at <c>authored x lossyScale</c>, the failure that once put a spell light
    /// at an effective 367 world units.</para>
    ///
    /// <para>NOT SQUASHED ON Y. Every other round thing in this project lies on the FLOOR and is
    /// flattened because the camera looks at it at an angle. This one is in VIEW space, so
    /// flattening it turns it back into a disc under the character's feet — the exact reading it
    /// exists to deny. The GROUND RING is the layer that is squashed, because that one really is
    /// on the floor.</para>
    /// </summary>
    internal sealed partial class StaticDomeFX
    {
        /// <summary>Points of charge on the shell. Enough that the sphere reads as a surface,
        /// few enough that an arc between two of them is a legible event.</summary>
        private const int NODE_COUNT = 22;

        /// <summary>How many arcs can be in the air at once.</summary>
        private const int ARCS = 4;

        private const float GROUND_SQUASH = 0.34f;
        private const float RING_BAND = 0.39f;

        /// <summary>Where the shell's centre sits above the caster's feet, world units. Roughly
        /// chest height on a 1.86-unit character.</summary>
        private const float CENTRE_HEIGHT = 0.85f;

        /// <summary>Degrees per second the shell turns. Slow: the arcs are the event, and a fast
        /// spin competes with them for the same attention.</summary>
        private const float SPIN_DEG_PER_SECOND = 24f;

        private const float NODE_SIZE = 0.155f;

        /// <summary>Sorting offsets from the caster's OWN live order. The only statement in the
        /// rig that anything is inside anything.</summary>
        private const int FRONT_ORDER = 3;
        private const int BACK_ORDER = -3;
        private const int SILHOUETTE_ORDER = 4;
        private const int FILL_ORDER = -4;

        private const int ORDER_GROUND_RING = 42;

        private Transform _root;
        private Transform _shell;
        private float _radius;
        private ElementPalette _palette;

        private Transform[] _nodes;
        private SpriteRenderer[] _nodeRenderers;
        private float[] _nodeAzimuth;      // radians around the vertical axis
        private float[] _nodeElevation;    // radians, -pi/2 .. pi/2
        private float[] _nodePhase;
        private readonly Vector3[] _nodeLocal = new Vector3[NODE_COUNT];
        private readonly float[] _nodeDepth = new float[NODE_COUNT];

        private Transform[] _arcs;
        private SpriteRenderer[] _arcRenderers;
        private float[] _arcAge;
        private int _arcCursor;

        private SpriteRenderer _silhouette;
        private SpriteRenderer _fill;
        private SpriteRenderer _groundRing;

        private GameObject _lightGo;
        private Component _light;

        private int _baseOrder;
        private float _age;
        private float _fade = 1f;
        private float _spin;
        private float _arcTimer;
        private float _arcFlash;

        /// <summary>Where the field last hurt something, in world space, and when. An arc fired
        /// while this is fresh terminates ON that body, which turns a decorative layer into a
        /// damage indicator for free.</summary>
        private Vector3 _targetWorld;
        private float _targetAge = float.MaxValue;

        private bool _destroyed;

        /// <summary>The circle the ground ring is drawn on, which is the damage radius.</summary>
        public float GroundRadius => _radius;

        /// <summary>How many charge nodes the shell is built from. Naming one by index in a
        /// test is how an assertion silently starts measuring a different node.</summary>
        public int NodeCount => NODE_COUNT;

        /// <summary>The scale that puts <see cref="ElementalSprites.Ring"/>'s bright band on a
        /// given world radius, so a test can assert the composition and not either half.</summary>
        public static float RingSpanFor(float worldRadius) => worldRadius / RING_BAND;

        public static StaticDomeFX Attach(Transform parent, float radius, ElementPalette palette)
        {
            ElementalSprites.EnsureAll();
            FieldSprites.EnsureAll();

            var fx = new StaticDomeFX
            {
                _root = parent,
                _radius = Mathf.Max(0.5f, radius),
                _palette = palette,
            };

            // Identity root — see the class doc.
            parent.localScale = Vector3.one;

            fx.BuildShell();
            fx.BuildNodes();
            fx.BuildArcs();
            fx.BuildGround();
            fx.AttachLight();
            fx._arcTimer = Random.Range(0.10f, ARC_INTERVAL_MIN);
            return fx;
        }

        private void BuildShell()
        {
            // A child transform for everything in VIEW space, so the ground ring below can be
            // squashed without squashing the sphere with it.
            var go = new GameObject("Shell");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, CENTRE_HEIGHT, 0f);
            _shell = go.transform;

            float span = _radius * 2f;

            // The silhouette of a sphere IS a circle, and it is the cheapest statement that the
            // shell has an edge. Kept very faint: this spell has to be fought inside.
            _silhouette = MakeSprite(_shell, "Silhouette", ElementalSprites.Ring, _palette.core,
                SILHOUETTE_ORDER);
            _silhouette.transform.localScale = Vector3.one * RingSpanFor(_radius);

            _fill = MakeSprite(_shell, "Fill", ElementalSprites.Glow, _palette.halo, FILL_ORDER);
            _fill.transform.localScale = Vector3.one * span;
        }

        private void BuildNodes()
        {
            _nodes = new Transform[NODE_COUNT];
            _nodeRenderers = new SpriteRenderer[NODE_COUNT];
            _nodeAzimuth = new float[NODE_COUNT];
            _nodeElevation = new float[NODE_COUNT];
            _nodePhase = new float[NODE_COUNT];

            for (int i = 0; i < NODE_COUNT; i++)
            {
                var sr = MakeSprite(_shell, "Node" + i, ElementalSprites.Sparkle,
                    _palette.hotCore, FRONT_ORDER);
                _nodes[i] = sr.transform;
                _nodeRenderers[i] = sr;

                // Evenly in elevation by ARC SINE, not by angle: spacing elevations linearly
                // crowds the poles, and on a 22-point shell that reads as two clumps with a gap.
                float t = (i + 0.5f) / NODE_COUNT;
                _nodeElevation[i] = Mathf.Asin(Mathf.Clamp(t * 2f - 1f, -1f, 1f));
                // The golden angle, so successive nodes never line up into a visible seam.
                _nodeAzimuth[i] = i * 2.39996f;
                _nodePhase[i] = Random.Range(0f, 6.28f);
            }
        }

        private void BuildArcs()
        {
            _arcs = new Transform[ARCS];
            _arcRenderers = new SpriteRenderer[ARCS];
            _arcAge = new float[ARCS];

            for (int i = 0; i < ARCS; i++)
            {
                var sr = MakeSprite(_shell, "Arc" + i, FieldSprites.Arc, _palette.hotCore,
                    FRONT_ORDER + 1);
                _arcs[i] = sr.transform;
                _arcRenderers[i] = sr;
                _arcAge[i] = ARC_SECONDS;    // starts spent, so nothing draws until fired
            }
        }

        private void BuildGround()
        {
            // The one layer that IS on the floor, and therefore the one that is squashed. It is
            // also the honest statement of reach: the shell is a silhouette in view space and
            // says nothing about how far the sweep goes.
            float ringSpan = RingSpanFor(_radius);
            _groundRing = MakeSprite(_root, "FieldRing", ElementalSprites.Ring, _palette.core,
                ORDER_GROUND_RING, SortingConfig.LAYER_FLOOR_DECALS);
            _groundRing.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);
        }

        private void AttachLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("StaticLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localPosition = new Vector3(0f, CENTRE_HEIGHT, 0f);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));   // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.15f);
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

        /// <summary>
        /// Re-hang every sorted piece off the caster's CURRENT order. Must run every frame:
        /// <c>YSortEntity</c> rewrites that order whenever the caster walks, and a stale base
        /// flips the far hemisphere in front of them.
        /// </summary>
        public void RebaseSortingOrder(int casterOrder)
        {
            if (_destroyed || _baseOrder == casterOrder) return;
            _baseOrder = casterOrder;

            if (_silhouette != null) _silhouette.sortingOrder = casterOrder + SILHOUETTE_ORDER;
            if (_fill != null) _fill.sortingOrder = casterOrder + FILL_ORDER;

            if (_nodeFront == null) _nodeFront = new bool[NODE_COUNT];
            for (int i = 0; i < NODE_COUNT; i++)
            {
                if (_nodeRenderers[i] == null) continue;
                // The cached side has to be refreshed here too. AdvanceNodes only rewrites a
                // node whose side CHANGED, so leaving the cache alone after a rebase would
                // strand every node that happens to stay on the same side of the shell — which
                // is most of them — holding the previous frame's order.
                bool front = _nodeDepth[i] >= 0f;
                _nodeFront[i] = front;
                _nodeRenderers[i].sortingOrder = casterOrder + (front ? FRONT_ORDER : BACK_ORDER);
            }

            for (int i = 0; i < ARCS; i++)
                if (_arcRenderers[i] != null)
                    _arcRenderers[i].sortingOrder = casterOrder + FRONT_ORDER + 1;
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;

            if (_lightGo != null)
            {
                // Destroy is an outright ERROR in Edit Mode, where a test builds this directly.
                if (Application.isPlaying) Object.Destroy(_lightGo);
                else Object.DestroyImmediate(_lightGo);
            }
            _lightGo = null;
            _light = null;
        }

        private SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite, Color color,
            int order, string layer = SortingConfig.LAYER_ENTITIES)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            // Every layer here is LIGHT. This rig deliberately has NO opaque layer: a static
            // field displaces nothing and tears nothing off the ground, and inventing a debris
            // layer for the sake of the rule would be a lie about what the spell does.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
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
