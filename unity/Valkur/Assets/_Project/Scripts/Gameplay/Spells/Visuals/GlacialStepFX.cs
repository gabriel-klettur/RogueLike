using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A blink that costs the caster their own shape: the silhouette SHATTERS where they left
    /// and the same shards converge and resolve where they arrive, with a patch of rime frozen
    /// onto the floor at each end.
    ///
    /// <para>WHY NOT THE TRANSPORTER RIG. That one is a column of motes — a body being taken
    /// apart into light, which is a Star Trek fiction and reads identically whatever colour it
    /// is tinted. Three Mobility spells shared it and therefore had one visual. Ice does not
    /// dissolve, it BREAKS, and the thing that says so is a flat plate with an edge: the rig
    /// has to be shaped like what it draws (L1), which is why the shards are
    /// <c>IceSprites.Facet</c> and not a radial disc.</para>
    ///
    /// <para>THE PATCH THAWS FROM THE EDGE INWARD, and that is not a detail. A patch whose
    /// alpha ramps down uniformly reads as an alpha ramp — a graphical fade, information about
    /// the renderer. One that SHRINKS reads as ice melting, which is information about the
    /// world. Same distinction the snow accumulation buffer exists for.</para>
    /// </summary>
    internal sealed partial class GlacialStepFX : MonoBehaviour
    {
        internal enum Mode { Shatter, Resolve }

        /// <summary>How long the shards take to fly apart, or to come together.</summary>
        private const float SHARD_SECONDS = 0.42f;

        /// <summary>Fraction of the patch's life spent thawing.</summary>
        private const float THAW_FRACTION = 0.45f;

        private const int SHARD_COUNT = 20;
        private const float DEFAULT_PATCH_RADIUS = 1.2f;

        private const int ORDER_PATCH = 40;
        private const int ORDER_RING  = 41;
        private const int ORDER_GHOST = 46;
        private const int ORDER_SHARD = 50;
        private const int ORDER_POP   = 52;

        private Mode _mode;
        private ElementPalette _palette;
        private Vector2 _silhouette;
        private float _patchRadius;
        private float _patchHold;
        private float _delay;
        private float _age;
        private float _life;

        private SpriteRenderer _ghost;
        private SpriteTintStack _bodyTint;
        private bool _bodyReleased;

        private Transform _groundPlane;
        private SpriteRenderer _patch;
        private SpriteRenderer _ring;
        private SpriteRenderer _pop;
        private Transform[] _shardTransforms;
        private SpriteRenderer[] _shardRenderers;
        private Vector3[] _shardSlot;
        private Vector3[] _shardScatter;
        private float[] _shardSpin;
        private Component _light;

        /// <summary>
        /// The departure: the pose that was standing here comes apart. The caster has already
        /// moved by the time this runs, so the silhouette is passed in rather than read back.
        /// </summary>
        public static void Shatter(Vector3 center, Vector2 silhouette, Sprite sprite, bool flipX,
                                   int sortingLayerId, int sortingOrder, ElementPalette palette,
                                   SpellDefinition spell)
        {
            var fx = Create(Mode.Shatter, center, silhouette, palette, spell, 0f);
            if (fx == null) return;
            fx.BuildGhost(sprite, flipX, sortingLayerId, sortingOrder);
            fx.BuildRig();
        }

        /// <summary>
        /// The arrival. <paramref name="delay"/> is the lead the departure has on it: the body
        /// is held out of sight for that long, so the eye is pulled from one end of the trip to
        /// the other instead of being shown both at once.
        /// </summary>
        public static void Resolve(Transform owner, Vector3 center, Vector2 silhouette,
                                   ElementPalette palette, SpellDefinition spell, float delay)
        {
            var fx = Create(Mode.Resolve, center, silhouette, palette, spell, delay);
            if (fx == null) return;
            if (owner != null) fx._bodyTint = SpriteTintStack.Attach(owner.gameObject);
            fx._bodyTint?.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, 0f));
            fx.BuildRig();
        }

        private static GlacialStepFX Create(Mode mode, Vector3 center, Vector2 silhouette,
                                            ElementPalette palette, SpellDefinition spell,
                                            float delay)
        {
            // Refused outside Play Mode for the reason every timed rig here is: the sequence
            // advances from Update, so an Edit-Mode call leaves a permanent cluster of sprites
            // and, on the arrival, an invisible character.
            if (!Application.isPlaying) return null;

            var go = new GameObject("GlacialStepFX");
            go.transform.position = center;

            var fx = go.AddComponent<GlacialStepFX>();
            fx._mode = mode;
            fx._palette = palette;
            fx._silhouette = new Vector2(Mathf.Max(0.2f, silhouette.x), Mathf.Max(0.3f, silhouette.y));
            fx._patchRadius = spell != null && spell.radius > 0f ? spell.radius : DEFAULT_PATCH_RADIUS;
            fx._patchHold = spell != null && spell.duration > 0f ? spell.duration : 3f;
            fx._delay = delay;
            fx._life = delay + Mathf.Max(SHARD_SECONDS, fx._patchHold);
            return fx;
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildGhost(Sprite sprite, bool flipX, int sortingLayerId, int sortingOrder)
        {
            if (sprite == null) return;

            var go = new GameObject("FrozenPose");
            go.transform.SetParent(transform, false);
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sprite = sprite;
            _ghost.flipX = flipX;
            _ghost.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            _ghost.sortingLayerID = sortingLayerId;
            _ghost.sortingOrder = sortingOrder;
            _ghost.color = new Color(1f, 1f, 1f, 0f);

            // Match the size the body actually occupied rather than copying a transform chain,
            // so a scaled or nested character still leaves a silhouette that fits.
            Vector3 local = sprite.bounds.size;
            go.transform.localScale = new Vector3(
                local.x > 0.0001f ? _silhouette.x / local.x : 1f,
                local.y > 0.0001f ? _silhouette.y / local.y : 1f,
                1f);
        }

        private void BuildRig()
        {
            ElementalSprites.EnsureAll();
            IceSprites.EnsureAll();

            BuildPatch();
            BuildShards();

            _pop = MakeAdditive("Pop", ElementalSprites.HotCore, _palette.hotCore, ORDER_POP, transform);
            _pop.transform.localScale = Vector3.one * (_silhouette.x * 1.6f);

            BuildLight();
        }

        private void BuildPatch()
        {
            // One squash parent for the floor, rotation on the children. Squashing each item
            // separately foreshortens its length without turning its direction, which slides
            // it across the ground instead of laying it on the ground.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localPosition = new Vector3(0f, -_silhouette.y * 0.5f, 0f);
            plane.transform.localScale = new Vector3(1f, 0.38f, 1f);
            _groundPlane = plane.transform;

            // IceSprites.Rime is 2 units wide by 1 tall, so a patch 2r across on both axes is
            // (r, 2r). The fill says WHAT the ground is; the ring says HOW FAR it goes.
            _patch = MakeAdditive("Rime", IceSprites.Rime, _palette.glow, ORDER_PATCH, _groundPlane);
            _patch.transform.localScale = new Vector3(_patchRadius, _patchRadius * 2f, 1f);

            // L5: Ring peaks at normalized radius 0.78, so a boundary at world radius r is
            // scaled r / 0.39 — the drawn edge and the authored radius are one number.
            _ring = MakeAdditive("RimeEdge", ElementalSprites.Ring, _palette.core, ORDER_RING, _groundPlane);
            _ring.transform.localScale = Vector3.one * (_patchRadius / 0.39f);
        }

        private void BuildShards()
        {
            _shardTransforms = new Transform[SHARD_COUNT];
            _shardRenderers = new SpriteRenderer[SHARD_COUNT];
            _shardSlot = new Vector3[SHARD_COUNT];
            _shardScatter = new Vector3[SHARD_COUNT];
            _shardSpin = new float[SHARD_COUNT];

            for (int i = 0; i < SHARD_COUNT; i++)
            {
                var sr = MakeAdditive($"Shard{i:00}", IceSprites.Facet(i), _palette.hotCore,
                                      ORDER_SHARD, transform);
                float size = Random.Range(0.10f, 0.26f);
                IceSprites.ScaleShard(sr.transform, size, size * Random.Range(1.4f, 2.6f));

                // A slot inside the silhouette the body occupied, narrowed towards the top so
                // the pieces read as a person rather than as a rectangle of chips.
                float heightFraction = Random.value;
                float taper = Mathf.Lerp(1f, 0.6f, heightFraction);
                _shardSlot[i] = new Vector3(
                    Random.Range(-0.5f, 0.5f) * _silhouette.x * taper,
                    (heightFraction - 0.55f) * _silhouette.y,
                    0f);

                float angle = Random.Range(0f, Mathf.PI * 2f);
                float reach = Random.Range(0.8f, 2.2f);
                _shardScatter[i] = new Vector3(Mathf.Cos(angle) * reach * _silhouette.x,
                                               Mathf.Sin(angle) * reach * _silhouette.y * 0.7f,
                                               0f);
                _shardSpin[i] = Random.Range(-420f, 420f);

                _shardTransforms[i] = sr.transform;
                _shardRenderers[i] = sr;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("GlacialLight");
            go.transform.SetParent(transform, false);
            try
            {
                _light = go.AddComponent(lightType);
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _patchRadius * 1.8f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.15f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; Destroy(go); }
        }

        private static SpriteRenderer MakeAdditive(string objectName, Sprite sprite, Color color,
                                                   int order, Transform parent)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            sr.color = new Color(color.r, color.g, color.b, 0f);
            return sr;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
