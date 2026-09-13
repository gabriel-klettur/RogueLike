using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Spells;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    public sealed partial class EntityAnimationPreviewService
    {
        /// <summary>Size of the drawn crosshair, as a fraction of the body's half-height.</summary>
        private const float MUZZLE_MARK_SCALE = 0.16f;

        private SpriteRenderer _muzzleMark;
        private bool _muzzleVisible;
        private Vector2 _muzzleNormalized;

        /// <summary>The renderer the preview is actually drawing, or null before a subject.</summary>
        public SpriteRenderer PrimaryRenderer => _renderers.Count > 0 ? _renderers[0] : null;

        /// <summary>Where the marker currently sits, in the muzzle's own normalized space.</summary>
        public Vector2 MuzzleNormalized => _muzzleNormalized;

        /// <summary>
        /// The AUTHORED key of the variant on the stage, or empty when the state carries none.
        ///
        /// <para>Empty and null are the same answer here on purpose: a state with no variants
        /// resolves variant -1, and "this animation" then means the state itself — which is
        /// exactly what an empty <c>CastMuzzlePoint.variantKey</c> already means.</para>
        /// </summary>
        public string VariantKeyOnStage()
        {
            var animator = Animator;
            if (animator == null) return string.Empty;
            return animator.VariantLabel(animator.CurrentState, animator.ActiveVariant)
                   ?? string.Empty;
        }

        /// <summary>The sprite currently drawn, for a readout that names the frame.</summary>
        public string FrameNameOnStage()
        {
            var sr = PrimaryRenderer;
            return sr != null && sr.sprite != null ? sr.sprite.name : string.Empty;
        }

        /// <summary>
        /// Show or hide the muzzle crosshair and place it.
        ///
        /// <para>Drawn on the stage rather than as a UI overlay because the value being authored
        /// is a fraction of the SPRITE's own bounds: on the stage it moves with the frame, the
        /// zoom and the direction for free, and every one of those would have to be re-derived
        /// to keep a screen-space dot honest.</para>
        /// </summary>
        public void SetMuzzleMarker(bool visible, Vector2 normalized)
        {
            _muzzleVisible = visible;
            _muzzleNormalized = normalized;
            UpdateMuzzleMarker();
        }

        /// <summary>
        /// The normalized muzzle a point in the stage's viewport corresponds to.
        ///
        /// <para><paramref name="viewport"/> is 0..1 across the RawImage the stage is drawn
        /// into, origin bottom-left — the same space <c>Camera.ViewportToWorldPoint</c> takes,
        /// which is why the caller converts the pointer into it and nothing here knows about
        /// RectTransforms.</para>
        /// </summary>
        public bool TryMuzzleNormalizedFromViewport(Vector2 viewport, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            var sr = PrimaryRenderer;
            if (_camera == null || sr == null || sr.sprite == null) return false;

            Bounds b = sr.bounds;
            if (b.extents.x <= 0.0001f || b.extents.y <= 0.0001f) return false;

            Vector3 world = _camera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, Mathf.Abs(_camera.transform.position.z)));

            float sign = CastMuzzle.FacingSignFor(sr.sprite, CurrentDirection);
            normalized = new Vector2(
                (world.x - b.center.x) / b.extents.x * sign,
                (world.y - b.center.y) / b.extents.y);
            return true;
        }

        /// <summary>The world point a normalized pair resolves to on the frame on screen.</summary>
        public bool TryMuzzleWorld(Vector2 normalized, out Vector3 world)
        {
            world = Vector3.zero;
            var sr = PrimaryRenderer;
            if (sr == null || sr.sprite == null) return false;

            Bounds b = sr.bounds;
            if (b.extents.x <= 0.0001f || b.extents.y <= 0.0001f) return false;

            float sign = CastMuzzle.FacingSignFor(sr.sprite, CurrentDirection);
            world = new Vector3(b.center.x + sign * b.extents.x * normalized.x,
                                b.center.y + b.extents.y * normalized.y,
                                sr.transform.position.z);
            return true;
        }

        /// <summary>
        /// The offset in WORLD units from the body's centre, which is the only form of this
        /// number an author can judge by eye. The stored fraction is unreadable on its own:
        /// 0.8 means nothing until you know the frame is 4.98 units wide.
        /// </summary>
        public bool TryMuzzleWorldOffset(Vector2 normalized, out Vector2 offset)
        {
            offset = Vector2.zero;
            var sr = PrimaryRenderer;
            if (sr == null || sr.sprite == null) return false;

            Bounds b = sr.bounds;
            offset = new Vector2(b.extents.x * normalized.x, b.extents.y * normalized.y);
            return true;
        }

        /// <summary>
        /// Re-place the crosshair against the frame currently drawn. Called every tick because
        /// the frame changes underneath it: the whole point of authoring here is watching the
        /// mark stay on the mouth — or drift off it — as the animation plays.
        /// </summary>
        private void UpdateMuzzleMarker()
        {
            if (_rigs.Count == 0) { _muzzleMark = null; return; }

            if (_muzzleMark == null)
            {
                var go = new GameObject("MuzzleMark");
                go.transform.SetParent(_rigs[0].transform, false);
                go.layer = _rigs[0].layer;
                _muzzleMark = go.AddComponent<SpriteRenderer>();
                _muzzleMark.sprite = MuzzleMarkSprite();
                _muzzleMark.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                // Above the body: the mark is the thing being placed, so a frame drawing over
                // it is a frame hiding the only feedback the author has.
                _muzzleMark.sortingOrder = 60;
                _muzzleMark.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                // The editors' own world-space marker colour: a ring around an instance this
                // editor can move. That is exactly what this is, and an overlay whose click
                // affordance is a different yellow from the Spawner and Lighting ones teaches
                // the author two things where there is one.
                _muzzleMark.color = UITheme.MARKER_RING;
            }

            _muzzleMark.enabled = _muzzleVisible;
            if (!_muzzleVisible) return;

            var sr = PrimaryRenderer;
            if (sr == null || sr.sprite == null) { _muzzleMark.enabled = false; return; }

            if (!TryMuzzleWorld(_muzzleNormalized, out Vector3 world))
            {
                _muzzleMark.enabled = false;
                return;
            }

            _muzzleMark.transform.position = world;
            float size = Mathf.Max(0.05f, sr.bounds.extents.y * MUZZLE_MARK_SCALE);
            _muzzleMark.transform.localScale = Vector3.one * size;
        }

        private static Sprite _muzzleMarkSprite;

        /// <summary>
        /// A hollow crosshair: a ring with a one-pixel gap at its centre, so the exact point
        /// being authored is the pixel the mark does NOT cover. A filled dot hides the very
        /// feature it is being aligned to.
        ///
        /// <para>Domain Reload is OFF, so a static holding a texture survives a Play-mode
        /// restart as a DESTROYED object and every later marker renders nothing — the same
        /// hazard the ground line's own sprite carries, and the same fix.</para>
        /// </summary>
        private static Sprite MuzzleMarkSprite()
        {
            if (_muzzleMarkSprite != null) return _muzzleMarkSprite;

            const int SIZE = 32;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color[SIZE * SIZE];
            float c = SIZE * 0.5f - 0.5f;
            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Ring, plus four ticks reaching out of it along the axes.
                    bool ring = r > SIZE * 0.26f && r < SIZE * 0.34f;
                    bool tick = (Mathf.Abs(dx) < 0.9f && r > SIZE * 0.34f && r < SIZE * 0.48f)
                             || (Mathf.Abs(dy) < 0.9f && r > SIZE * 0.34f && r < SIZE * 0.48f);
                    px[y * SIZE + x] = (ring || tick) ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            _muzzleMarkSprite = Sprite.Create(tex, new Rect(0, 0, SIZE, SIZE),
                                              new Vector2(0.5f, 0.5f), SIZE);
            _muzzleMarkSprite.hideFlags = HideFlags.HideAndDontSave;
            return _muzzleMarkSprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMuzzleStatics()
        {
            _muzzleMarkSprite = null;
        }
    }
}
