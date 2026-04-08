using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime representation of a building placed in the world.
    ///
    /// Split-render technique (maps to Python BuildingView.get_parts()):
    ///   The sprite is cropped at the 'split_ratio' cut point into two child SpriteRenderers:
    ///     - Bottom ("Footprint"): rows 0..cut from the BOTTOM of the texture (the ground portion).
    ///       Sorting layer = WallsBottom  → renders UNDER entities/players.
    ///     - Top ("Canopy"): rows cut..height from the BOTTOM of the texture (the decorative upper portion).
    ///       Sorting layer = WallsTop    → renders OVER entities/players.
    ///
    ///   In Python coords (Y-down):
    ///     cut_py = height * split_ratio  (from top)
    ///     bottom portion = rows cut_py..height (lower part)
    ///     top portion    = rows 0..cut_py     (upper part)
    ///
    ///   In Unity texture coords (Y-up, origin at bottom-left of texture):
    ///     bottomTexH = height * (1 - split_ratio)
    ///     bottomRect = Rect(0, 0, width, bottomTexH)
    ///     topRect    = Rect(0, bottomTexH, width, height - bottomTexH)
    ///
    /// Collision (maps to Python collision_rect = below the split line):
    ///   A BoxCollider2D is sized to the footprint rectangle (width × bottomTexH in world units).
    ///
    /// Position anchor: the parent transform is placed at the BOTTOM-CENTER of the full sprite
    /// (ground-touch point), matching Python's y + height offset for the collision rect base.
    ///
    /// Scale override: if an instance overrides the pixel dimensions, a localScale multiplier
    /// is applied to the parent transform so both child renderers and the collider scale correctly.
    /// </summary>
    [AddComponentMenu("Valkur/World/Building Object")]
    public class BuildingObject : MonoBehaviour
    {
        // ── Pixels per Unity world unit. Must match TILE_PPU in ValkurAssetPostprocessor. ──
        private const float PPU = 32f;

        [Header("Template")]
        [Tooltip("BuildingTemplateData defining this building type. Set by BuildingLoader or by inspector.")]
        [SerializeField] private BuildingTemplateData _template;

        [Header("Instance Overrides")]
        [Tooltip("Scale in pixels. (0,0) means use template.originalScale.")]
        [SerializeField] private Vector2Int _scaleOverride;

        [Tooltip("Per-instance split ratio override in [0,1]. Values < 0 use template.splitRatio.")]
        [SerializeField, Range(-0.01f, 1f)] private float _splitRatioOverride = -1f;

        [Header("Runtime Info (read-only)")]
        [Tooltip("Zone name this building belongs to. Set by BuildingLoader.")]
        [SerializeField] private string _zoneName;

        [Tooltip("Unique instance ID from buildings_instances.json. Set by BuildingLoader.")]
        [SerializeField] private int _instanceId;

        // Child renderers created by Apply()
        private SpriteRenderer _bottomRenderer;
        private SpriteRenderer _topRenderer;
        private BoxCollider2D  _collider;

        // ── Public accessors ───────────────────────────────────────────────────────
        public BuildingTemplateData Template      => _template;
        public string               ZoneName      { get => _zoneName;           set => _zoneName = value;           }
        public int                  InstanceId    { get => _instanceId;         set => _instanceId = value;         }
        public Vector2Int           ScaleOverride { get => _scaleOverride;      set => _scaleOverride = value;       }
        public float SplitRatioOverride           { get => _splitRatioOverride; set => _splitRatioOverride = value;  }

        // ── Unity lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            // Called at runtime (play mode). Drives setup from serialized fields for
            // BuildingObjects placed directly in the scene hierarchy.
            if (_template != null)
                Apply(_template, _scaleOverride, _splitRatioOverride);
        }

        // ── Setup ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Configure this building from a template and optional per-instance overrides.
        /// Creates/reuses child GameObjects for bottom and top SpriteRenderers and
        /// sets the BoxCollider2D to cover the footprint (below-split portion).
        ///
        /// Safe to call multiple times (idempotent given the same inputs).
        /// </summary>
        public void Apply(BuildingTemplateData template, Vector2Int scaleOverride, float splitRatioOverride)
        {
            _template           = template;
            _scaleOverride      = scaleOverride;
            _splitRatioOverride = splitRatioOverride;

            if (template == null)
            {
                Debug.LogWarning($"[BuildingObject] Template is null on '{name}'.", this);
                return;
            }

            // ── 1. Resolve effective values ─────────────────────────────────────────
            float effectiveSplitRatio = (splitRatioOverride >= 0f) ? splitRatioOverride : template.splitRatio;

            int origW = template.originalScale.x;
            int origH = template.originalScale.y;
            if (origW <= 0 || origH <= 0)
            {
                Debug.LogWarning($"[BuildingObject] Template {template.templateId} has zero originalScale.", this);
                return;
            }

            int effW = (scaleOverride.x > 0) ? scaleOverride.x : origW;
            int effH = (scaleOverride.y > 0) ? scaleOverride.y : origH;

            // Scale transform so the building appears at effW×effH pixels in the world.
            transform.localScale = new Vector3((float)effW / origW, (float)effH / origH, 1f);

            // ── 2. Load texture ─────────────────────────────────────────────────────
            Sprite sourceSprite = Resources.Load<Sprite>(template.assetPath);
            if (sourceSprite == null)
            {
                Debug.LogWarning(
                    $"[BuildingObject] Sprite not found at Resources/{template.assetPath} " +
                    $"(template id={template.templateId}).", this);
                return;
            }

            Texture2D tex = sourceSprite.texture;

            // ── 3. Compute crop rects in texture-space (Unity Y=0 is BOTTOM of texture) ──
            // bottomTexH = pixel count of the footprint (lower portion in Python = ground region).
            // In Python (Y-down), split is at y=origH*splitRatio from the TOP.
            // In Unity texture (Y-up), footprint spans rows 0 .. bottomTexH.
            int bottomTexH = Mathf.RoundToInt(origH * (1f - effectiveSplitRatio));
            bottomTexH = Mathf.Clamp(bottomTexH, 1, origH - 1);
            int topTexH = origH - bottomTexH;

            // Sprites with pivot at bottom-center so local Y=0 = the bottom of each portion.
            Sprite bottomSprite = Sprite.Create(
                tex,
                new Rect(0, 0, origW, bottomTexH),
                new Vector2(0.5f, 0f),
                PPU);

            Sprite topSprite = Sprite.Create(
                tex,
                new Rect(0, bottomTexH, origW, topTexH),
                new Vector2(0.5f, 0f),
                PPU);

            // Heights in local (unscaled) Unity units
            float bottomH = bottomTexH / PPU;   // footprint height
            float topH    = topTexH    / PPU;   // canopy height

            // ── 4. Create / reuse child renderers ──────────────────────────────────
            // Parent transform sits at BOTTOM-CENTER of the full sprite.
            // Bottom child at local (0, 0) → its bottom aligns with parent.
            // Top child    at local (0, bottomH) → its bottom aligns with top of footprint.
            EnsureRenderer(ref _bottomRenderer, "Footprint",
                SortingConfig.LAYER_WALLS_BOTTOM, bottomSprite, Vector3.zero);

            EnsureRenderer(ref _topRenderer, "Canopy",
                SortingConfig.LAYER_WALLS_TOP, topSprite, new Vector3(0f, bottomH, 0f));

            // Y-sort within layer: buildings farther down on screen (lower worldY)
            // rank in front. SortingConfig.YToSortingOrder returns -(int)(y*100).
            int ySortOrder = SortingConfig.YToSortingOrder(transform.position.y);
            _bottomRenderer.sortingOrder = ySortOrder;
            _topRenderer.sortingOrder    = ySortOrder;

            // ── 5. Collider (footprint rect) ───────────────────────────────────────
            EnsureCollider();
            _collider.enabled = template.solid;
            if (template.solid)
            {
                // Collider covers the footprint portion in LOCAL (unscaled) space.
                // Transform.localScale then stretches it to effW×effH in world space.
                _collider.size   = new Vector2(origW / PPU, bottomH);
                _collider.offset = new Vector2(0f, bottomH * 0.5f);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private void EnsureRenderer(
            ref SpriteRenderer sr,
            string              childName,
            string              layerName,
            Sprite              sprite,
            Vector3             localPos)
        {
            if (sr == null)
            {
                Transform existing = transform.Find(childName);
                if (existing != null)
                    sr = existing.GetComponent<SpriteRenderer>();
            }

            if (sr == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(transform, worldPositionStays: false);
                sr = go.AddComponent<SpriteRenderer>();
            }

            sr.transform.localPosition = localPos;
            sr.sprite                  = sprite;
            sr.sortingLayerName        = layerName;
        }

        private void EnsureCollider()
        {
            if (_collider != null) return;
            _collider = GetComponent<BoxCollider2D>() ?? gameObject.AddComponent<BoxCollider2D>();
        }

        // ── Editor helpers ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Refresh renderers when inspector values change in edit mode.
            if (_template != null)
                Apply(_template, _scaleOverride, _splitRatioOverride);
        }

        private void OnDrawGizmosSelected()
        {
            if (_template == null || _template.originalScale == Vector2Int.zero) return;

            float effectiveSplitRatio = (_splitRatioOverride >= 0f) ? _splitRatioOverride : _template.splitRatio;
            int   origW   = _template.originalScale.x;
            int   origH   = _template.originalScale.y;
            float bottomH = origH * (1f - effectiveSplitRatio) / PPU;
            float topH    = origH * effectiveSplitRatio         / PPU;
            float sx      = transform.localScale.x;
            float sy      = transform.localScale.y;
            Vector3 pos   = transform.position;

            // Red: footprint / collision zone
            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.45f);
            Gizmos.DrawWireCube(
                pos + new Vector3(0f, bottomH * sy * 0.5f, 0f),
                new Vector3(origW / PPU * sx, bottomH * sy, 0.05f));

            // Blue: canopy / above-player zone
            Gizmos.color = new Color(0.25f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireCube(
                pos + new Vector3(0f, (bottomH + topH * 0.5f) * sy, 0f),
                new Vector3(origW / PPU * sx, topH * sy, 0.05f));
        }
#endif
    }
}
