using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Data;

namespace Valkur.Gameplay.World.Sky
{
    /// <summary>
    /// Gives a sprite a shadow on the ground: a projected silhouette that follows the sun, and
    /// (for creatures) a soft contact blob that never leaves the feet.
    ///
    /// Attached to the SOURCE renderer's object. Both shadows are child renderers of that
    /// transform, so they inherit its scale and its flip for free, and they draw on the source's
    /// own sorting layer one and two orders BELOW it — the body always covers the base of its
    /// shadow, and the shadow still sorts with the rest of the world by the same Y. That is the
    /// only correct place for it: a shadow on its own layer would draw over the feet of whoever
    /// stands behind, or under the ground tile in front.
    ///
    /// The silhouette is the source's OWN sprite handed to <c>Valkur/SpriteShadowProjected</c>,
    /// which shears it from a per-renderer foot line. So an animated body casts an animated
    /// shadow — the arm goes up, its shadow goes up — with no art authored anywhere.
    ///
    /// For a building the caster is attached to each half and told to sort under the
    /// FOOTPRINT: a canopy's shadow must not draw over the player the canopy draws over.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SunShadowCaster : MonoBehaviour
    {
        private const string ShadowShaderName = "Valkur/SpriteShadowProjected";
        private const string UnlitShaderName  = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        private const int    ProjectedOrderOffset = -1;
        private const int    BlobOrderOffset      = -2;

        private static readonly int FootYId = Shader.PropertyToID("_FootY");

        private static Material s_shadowMaterial;
        private static Material s_blobMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_shadowMaterial = null;
            s_blobMaterial   = null;
        }

        private SpriteRenderer _source;
        private SpriteRenderer _ground;
        private SpriteRenderer _shadow;
        private SpriteRenderer _blob;
        private MaterialPropertyBlock _mpb;
        private Sprite _lastSprite;
        private bool   _lastFlipX;
        private int    _lastOrder = int.MinValue;
        private int    _lastLayerId = int.MinValue;
        private float  _lastBlobAlpha = -1f;
        private bool   _withBlob;
        private bool   _built;

        /// <summary>The projected silhouette renderer. Test seam.</summary>
        public SpriteRenderer Shadow => _shadow;

        /// <summary>The contact blob renderer, or null when the caster carries none. Test seam.</summary>
        public SpriteRenderer Blob => _blob;

        /// <summary>The renderer whose sprite is projected.</summary>
        public SpriteRenderer Source => _source;

        /// <summary>
        /// Attach a caster to <paramref name="source"/>. <paramref name="sortUnder"/> is the
        /// renderer whose layer and order the shadows sort just beneath (defaults to the
        /// source); <paramref name="groundReference"/> is the renderer whose bottom edge is the
        /// foot line (defaults to the source). Idempotent.
        /// </summary>
        public static SunShadowCaster Attach(SpriteRenderer source, bool withBlob,
                                             SpriteRenderer sortUnder = null,
                                             SpriteRenderer groundReference = null,
                                             float groundOffsetLocal = 0f)
        {
            if (source == null) return null;
            var caster = source.GetComponent<SunShadowCaster>();
            if (caster == null) caster = source.gameObject.AddComponent<SunShadowCaster>();
            caster.Configure(source, withBlob, sortUnder, groundReference, groundOffsetLocal);
            return caster;
        }

        private SpriteRenderer _sortUnder;
        private float _groundOffset;

        /// <summary>The foot-line offset this caster was attached with. Test seam.</summary>
        public float GroundOffset => _groundOffset;

        private void Configure(SpriteRenderer source, bool withBlob, SpriteRenderer sortUnder,
                               SpriteRenderer groundReference, float groundOffsetLocal)
        {
            _source    = source;
            _withBlob  = withBlob;
            _sortUnder = sortUnder != null ? sortUnder : source;
            _ground    = groundReference != null ? groundReference : source;

            // A changed offset has to reach the shader, and the only writer is RefreshFootLine,
            // which runs on a sprite CHANGE. Re-attaching with a different offset (a template
            // re-baked, a variant swapped) would otherwise keep the old foot line forever.
            bool offsetChanged = !Mathf.Approximately(_groundOffset, groundOffsetLocal);
            _groundOffset = groundOffsetLocal;
            EnsureBuilt();
            if (offsetChanged && _shadow != null && _lastSprite != null) RefreshFootLine(_lastSprite);
        }

        /// <summary>
        /// Build the child renderers. Public because Unity calls no Awake on a component added
        /// in Edit Mode, and the fixture has to reach the same code Play Mode does.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built || _source == null) return;
            _built = true;
            _mpb ??= new MaterialPropertyBlock();

            _shadow = MakeChild("SunShadow");
            _shadow.sharedMaterial = ShadowMaterial();

            if (_withBlob)
            {
                _blob = MakeChild("GroundShadow");
                _blob.sprite         = ShadowSprites.Blob;
                _blob.sharedMaterial = BlobMaterial();
            }

            Sync();
        }

        private SpriteRenderer MakeChild(string childName)
        {
            var existing = _source.transform.Find(childName);
            var go = existing != null ? existing.gameObject : new GameObject(childName);
            if (existing == null)
            {
                go.transform.SetParent(_source.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale    = Vector3.one;
            }
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Simple;
            sr.color    = Color.white;
            return sr;
        }

        private void LateUpdate() => Sync();

        /// <summary>
        /// Bring the shadows up to date with the body: sprite, flip, sorting, foot line,
        /// visibility. Public so a test can drive one frame without a player loop.
        /// </summary>
        public void Sync()
        {
            if (_source == null || _shadow == null) return;

            bool sourceVisible = _source.enabled && _source.gameObject.activeInHierarchy;

            var sprite = _source.sprite;
            bool spriteChanged = sprite != _lastSprite;
            if (spriteChanged)
            {
                _lastSprite = sprite;
                _shadow.sprite = sprite;
                RefreshFootLine(sprite);
            }

            if (_source.flipX != _lastFlipX)
            {
                _lastFlipX = _source.flipX;
                _shadow.flipX = _lastFlipX;
            }

            // Sorting follows the body, written only when the body's changed: every write to
            // sortingOrder dirties URP's batcher, and a standing building never changes.
            var sortSrc = _sortUnder != null ? _sortUnder : _source;
            int layerId = sortSrc.sortingLayerID;
            int order   = sortSrc.sortingOrder;
            if (layerId != _lastLayerId || order != _lastOrder)
            {
                _lastLayerId = layerId;
                _lastOrder   = order;
                _shadow.sortingLayerID = layerId;
                _shadow.sortingOrder   = order + ProjectedOrderOffset;
                if (_blob != null)
                {
                    _blob.sortingLayerID = layerId;
                    _blob.sortingOrder   = order + BlobOrderOffset;
                }
            }

            bool projected = sourceVisible && sprite != null && SunShadowState.Alpha > 0.005f;
            if (_shadow.enabled != projected) _shadow.enabled = projected;

            if (_blob != null)
            {
                float alpha = sourceVisible && sprite != null ? SunShadowState.BlobAlpha : 0f;
                bool blobOn = alpha > 0.005f;
                if (_blob.enabled != blobOn) _blob.enabled = blobOn;
                if (blobOn && !Mathf.Approximately(alpha, _lastBlobAlpha))
                {
                    _lastBlobAlpha = alpha;
                    _blob.color = new Color(0f, 0f, 0f, alpha);
                }
                if (spriteChanged && sprite != null) PlaceBlob(sprite);
            }
        }

        /// <summary>
        /// The foot line in the SOURCE's object space: the bottom of the ground-reference
        /// renderer, RAISED by the offset the caster was attached with. For a creature that is
        /// its own sprite's bottom edge, whatever the pivot; for a canopy it is the footprint's
        /// bottom edge, so both halves shear from one line.
        ///
        /// <para>The offset is what separates the RECT from the ART. A sprite's bottom edge is
        /// the base of the thing drawn on it only when the ink reaches the bottom row, and 98 of
        /// the 1256 building PNGs leave empty canvas underneath — up to 24 % of the height, which
        /// is four world units on a shop. Sheared from the rect, such a shadow starts below the
        /// ground the building stands on and slides out from under it; the caller passes the
        /// distance up to the ink, and the shear starts where the art does.</para>
        /// </summary>
        private void RefreshFootLine(Sprite sprite)
        {
            if (sprite == null) return;
            float footY;
            if (_ground == _source || _ground == null)
            {
                footY = sprite.bounds.min.y;
            }
            else
            {
                var groundWorld = new Vector3(0f, _ground.bounds.min.y, 0f);
                footY = _source.transform.InverseTransformPoint(groundWorld).y;
            }
            footY += _groundOffset;
            // GET before SET, always: a SpriteRenderer keeps its own _MainTex in the same
            // block, and replacing the block with a fresh one drops the sprite's texture on the
            // floor — the shader then samples a white default, every texel reads alpha 1, and
            // the shadow is the sprite's whole RECT sheared onto the ground. Measured: the big
            // tree's canopy rect painted the entire street blue.
            _shadow.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FootYId, footY);
            _shadow.SetPropertyBlock(_mpb);
            _footY = footY;

            // CULLING BOUNDS. The shear happens in the vertex shader, and Unity culls the
            // renderer by the sprite's UNSHEARED bounds — so a tree whose trunk is a screen
            // width off to the side has its shadow lying across the view and Unity does not
            // draw it until the trunk itself scrolls in. Reported from play as "the shadows
            // appear about a second late at the edge of the screen". The local bounds are
            // widened to the shear's full reach at the horizon on both sides (the sun moves)
            // and the foot line kept, so the box covers every shadow the sun can cast.
            _shadow.localBounds = CullingBoundsFor(sprite.bounds, footY, SkyStyle.Active.skewMax);
        }

        /// <summary>
        /// The box that contains a sprite's sheared shadow at ANY hour: the sprite's own
        /// rect widened on both sides by the horizon shear of its full height above the foot
        /// line. Pure, so the fixture can pin that it is wider than the sprite.
        /// </summary>
        public static Bounds CullingBoundsFor(Bounds sprite, float footY, float skewMax)
        {
            float height = Mathf.Max(0f, sprite.max.y - footY);
            float reach  = height * Mathf.Abs(skewMax);
            var min = new Vector3(sprite.min.x - reach, Mathf.Min(sprite.min.y, footY), sprite.min.z - 0.1f);
            var max = new Vector3(sprite.max.x + reach, sprite.max.y, sprite.max.z + 0.1f);
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        private float _footY;

        /// <summary>The foot line last written to the shear shader. Test seam.</summary>
        public float FootY => _footY;

        private void PlaceBlob(Sprite sprite)
        {
            var style = SkyStyle.Active;
            float width  = Mathf.Max(0.2f, sprite.bounds.size.x * style.blobWidthFactor);
            float height = width * style.blobHeightFactor;
            var unit = ShadowSprites.BlobUnitSize;
            _blob.transform.localScale    = new Vector3(width / unit.x, height / unit.y, 1f);
            _blob.transform.localPosition = new Vector3(sprite.bounds.center.x, sprite.bounds.min.y, 0f);
        }

        private static Material ShadowMaterial()
        {
            if (s_shadowMaterial != null) return s_shadowMaterial;
            var shader = Shader.Find(ShadowShaderName) ?? Shader.Find(UnlitShaderName);
            s_shadowMaterial = new Material(shader) { name = "SunShadow", hideFlags = HideFlags.HideAndDontSave };
            return s_shadowMaterial;
        }

        private static Material BlobMaterial()
        {
            if (s_blobMaterial != null) return s_blobMaterial;
            // Unlit on purpose: a contact shadow is the absence of light, so the ambient must
            // not darken it a second time, and the flash of a hit must not light it.
            var shader = Shader.Find(UnlitShaderName) ?? Shader.Find("Sprites/Default");
            s_blobMaterial = new Material(shader) { name = "GroundShadowBlob", hideFlags = HideFlags.HideAndDontSave };
            return s_blobMaterial;
        }
    }
}
