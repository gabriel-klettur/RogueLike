using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A blink drawn as the character's OWN outline, twice: an after-image peels apart into
    /// vertical ribbons and is pulled down into the floor where they left, and the same ribbons
    /// rise and knit shut where they arrive.
    ///
    /// <para>THE RIBBONS ARE REAL SLICES OF THE CHARACTER'S SPRITE, cut out of its texture rect
    /// with <c>Sprite.Create</c> at cast time. Anything cheaper — one squeezed copy per ribbon,
    /// a generic smear — shows the whole character N times over instead of the character taken
    /// apart, and the entire point of this gesture is that what is coming apart is recognisably
    /// THEM. If the slice fails (an unusual sprite, a texture the atlas will not hand back) the
    /// rig falls back to a single after-image rather than drawing nothing.</para>
    ///
    /// <para>DARK IS AUTHORED BY DARKENING THE COLOUR, NOT BY DROPPING THE ALPHA (L2). On an
    /// additive surface alpha is COVERAGE and colour is brightness, so a deep violet at alpha
    /// 0.85 covers the silhouette and adds dark light — which is what shadow should do. The
    /// same effect at alpha 0.2 is not dark, it is FAINT, and a faint violet ghost is a
    /// different spell.</para>
    /// </summary>
    internal sealed partial class ShadowStepFX : MonoBehaviour
    {
        internal enum Mode { Peel, Knit }

        /// <summary>How long the ribbons take to be drawn down, or to knit shut.</summary>
        private const float RIBBON_SECONDS = 0.25f;

        /// <summary>Delay between one ribbon starting and the next, in seconds.</summary>
        private const float RIBBON_STAGGER = 0.012f;

        private const int RIBBON_COUNT = 9;
        private const int PATH_MOTE_COUNT = 14;

        private const int ORDER_RIBBON = 47;
        private const int ORDER_MOTE   = 45;

        /// <summary>Alpha the ribbons hold. High on purpose — see the class note.</summary>
        private const float RIBBON_ALPHA = 0.85f;

        private Mode _mode;
        private ElementPalette _palette;
        private Vector2 _silhouette;
        private float _delay;
        private float _age;
        private float _life;

        private Sprite[] _slices;
        private SpriteRenderer[] _ribbons;
        private Vector3[] _ribbonSlot;
        private float[] _ribbonBirth;
        private Vector3 _ribbonRestScale = Vector3.one;

        private Vector3 _pathFrom;
        private Vector3 _pathTo;
        private SpriteRenderer[] _motes;

        /// <summary>
        /// The departure. The caster has already moved by the time this runs, so the pose and
        /// the silhouette are passed in rather than read back off a renderer that is now
        /// somewhere else.
        /// </summary>
        public static void Peel(Vector3 center, Vector2 silhouette, Sprite sprite, bool flipX,
                                int sortingLayerId, int sortingOrder, ElementPalette palette)
        {
            var fx = Create(Mode.Peel, center, silhouette, palette, 0f, RIBBON_SECONDS + 0.1f);
            if (fx == null) return;
            fx.BuildRibbons(sprite, flipX, sortingLayerId, sortingOrder);
        }

        /// <summary>
        /// The arrival, plus the untargetable window the spell is really bought for.
        /// <paramref name="delay"/> is the lead the departure has on it.
        /// </summary>
        public static void Knit(Transform owner, Vector3 from, Vector3 to, Vector2 silhouette,
                                Sprite sprite, bool flipX, int sortingLayerId, int sortingOrder,
                                ElementPalette palette, SpellDefinition spell, float delay)
        {
            float phase = spell != null && spell.duration > 0f ? spell.duration : 0f;
            var fx = Create(Mode.Knit, to, silhouette, palette, delay,
                            delay + RIBBON_SECONDS + phase + PHASE_SNAP_SECONDS);
            if (fx == null) return;

            fx._pathFrom = from;
            fx._pathTo = to;
            fx.BuildRibbons(sprite, flipX, sortingLayerId, sortingOrder);
            fx.BuildPathMotes();
            fx.TakeBody(owner, phase);
        }

        private static ShadowStepFX Create(Mode mode, Vector3 center, Vector2 silhouette,
                                           ElementPalette palette, float delay, float life)
        {
            // Refused outside Play Mode: the sequence advances from Update, and the arrival
            // holds the caster's alpha and their invincibility flag — both of which would be
            // stranded by a rig that never ticks.
            if (!Application.isPlaying) return null;

            var go = new GameObject("ShadowStepFX");
            go.transform.position = center;

            var fx = go.AddComponent<ShadowStepFX>();
            fx._mode = mode;
            fx._palette = palette;
            fx._silhouette = new Vector2(Mathf.Max(0.2f, silhouette.x), Mathf.Max(0.3f, silhouette.y));
            fx._delay = delay;
            fx._life = life;
            return fx;
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildRibbons(Sprite sprite, bool flipX, int sortingLayerId, int sortingOrder)
        {
            ElementalSprites.EnsureAll();

            _slices = SliceVertically(sprite, RIBBON_COUNT);
            int count = _slices != null ? _slices.Length : 1;

            _ribbons = new SpriteRenderer[count];
            _ribbonSlot = new Vector3[count];
            _ribbonBirth = new float[count];

            float widthScale = sprite != null && sprite.bounds.size.x > 0.0001f
                ? _silhouette.x / sprite.bounds.size.x
                : 1f;
            float heightScale = sprite != null && sprite.bounds.size.y > 0.0001f
                ? _silhouette.y / sprite.bounds.size.y
                : 1f;
            _ribbonRestScale = new Vector3(widthScale, heightScale, 1f);

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Ribbon{i:00}");
                go.transform.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _slices != null ? _slices[i] : sprite;
                sr.flipX = flipX;
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerID = sortingLayerId;
                sr.sortingOrder = sortingOrder + 1;
                if (sortingLayerId == 0)
                {
                    // No renderer to inherit from: fall back to the VFX layer with a small
                    // order, never Z_SKY — that is a Z DEPTH and passing it as a sorting order
                    // is what once drew every lightning bolt under the wall tops.
                    sr.sortingLayerName = SortingConfig.LAYER_VFX;
                    sr.sortingOrder = ORDER_RIBBON;
                }
                sr.color = WithAlpha(_palette.glow, 0f);

                go.transform.localScale = new Vector3(widthScale, heightScale, 1f);

                // Slot: the strip's own place inside the silhouette. Mirrored when the body is
                // flipped, or the pieces would reassemble in the wrong order.
                float slotX = _slices != null
                    ? ((i + 0.5f) / count - 0.5f) * _silhouette.x
                    : 0f;
                if (flipX) slotX = -slotX;
                _ribbonSlot[i] = new Vector3(slotX, 0f, 0f);
                go.transform.localPosition = _ribbonSlot[i];

                _ribbonBirth[i] = i * RIBBON_STAGGER;
                _ribbons[i] = sr;
            }
        }

        /// <summary>
        /// A thin line of motes along the ground the character crossed, lit in ORDER from
        /// origin to destination, so the direction of travel is legible even when the two ends
        /// are off different edges of the eye's attention.
        /// </summary>
        private void BuildPathMotes()
        {
            _motes = new SpriteRenderer[PATH_MOTE_COUNT];
            for (int i = 0; i < PATH_MOTE_COUNT; i++)
            {
                var go = new GameObject($"PathMote{i:00}");
                go.transform.SetParent(transform, worldPositionStays: true);

                float along = (i + 0.5f) / PATH_MOTE_COUNT;
                go.transform.position = Vector3.Lerp(_pathFrom, _pathTo, along)
                                      + new Vector3(Random.Range(-0.12f, 0.12f),
                                                    Random.Range(-0.18f, 0.18f), 0f);
                go.transform.localScale = Vector3.one * Random.Range(0.08f, 0.16f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.Sparkle;
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_MOTE;
                sr.color = WithAlpha(_palette.hotCore, 0f);
                _motes[i] = sr;
            }
        }

        /// <summary>
        /// Cut <paramref name="source"/> into <paramref name="count"/> vertical strips of its
        /// own texture. <c>FullRect</c> rather than the tight mesh on purpose: a tight mesh is
        /// generated from the alpha and would need the texture readable, which a packed sprite
        /// is not. Returns null rather than throwing so the caller can fall back.
        /// </summary>
        private static Sprite[] SliceVertically(Sprite source, int count)
        {
            if (source == null || source.texture == null || count < 2) return null;

            try
            {
                Rect rect = source.textureRect;
                if (rect.width < count || rect.height < 1f) return null;

                float strip = rect.width / count;
                var slices = new Sprite[count];
                for (int i = 0; i < count; i++)
                {
                    var sub = new Rect(rect.x + i * strip, rect.y, strip, rect.height);
                    slices[i] = Sprite.Create(source.texture, sub, new Vector2(0.5f, 0.5f),
                                              source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    if (slices[i] == null) return null;
                }
                return slices;
            }
            catch
            {
                return null;
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
