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
    public partial class BuildingObject : MonoBehaviour
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

        [Tooltip("Per-instance Z-bottom offset (added to footprint sortingOrder). Maps to Python building.z_bottom.")]
        [SerializeField] private int _zBottomOffset;

        [Tooltip("Per-instance Z-top offset (added to canopy sortingOrder). Maps to Python building.z_top.")]
        [SerializeField] private int _zTopOffset;

        [Tooltip("Per-instance collider scope override: empty = use template, 'CG' = shared, 'CU' = per-instance.")]
        [SerializeField] private string _colliderScopeOverride = "";

        [Tooltip("Per-instance interactable override: -1 = inherit template, 0 = off, 1 = on. Maps to overrides.interactable in buildings_instances.json.")]
        [SerializeField] private int _interactableOverride = -1;

        [Header("Runtime Info (read-only)")]
        [Tooltip("Zone name this building belongs to. Set by BuildingLoader.")]
        [SerializeField] private string _zoneName;

        [Tooltip("Unique instance ID from buildings_instances.json. Set by BuildingLoader.")]
        [SerializeField] private int _instanceId;

        // Child renderers created by Apply()
        private SpriteRenderer _bottomRenderer;
        private SpriteRenderer _topRenderer;
        private BoxCollider2D  _collider;

        // Full (un-split) sprite last applied by Apply(). Used by the gameplay hover
        // highlight so the yellow silhouette follows the complete art, not the halves.
        private Sprite _sourceSprite;

        // ── Public accessors ───────────────────────────────────────────────────────
        public BuildingTemplateData Template      => _template;
        public string               ZoneName      { get => _zoneName;           set => _zoneName = value;           }
        public int                  InstanceId    { get => _instanceId;         set => _instanceId = value;         }
        public Vector2Int           ScaleOverride { get => _scaleOverride;      set => _scaleOverride = value;       }
        public float SplitRatioOverride           { get => _splitRatioOverride; set => _splitRatioOverride = value;  }
        public int   ZBottomOffset                { get => _zBottomOffset;      set { _zBottomOffset = value; ApplyZOffsets(); } }
        public int   ZTopOffset                   { get => _zTopOffset;         set { _zTopOffset    = value; ApplyZOffsets(); } }
        public string ColliderScopeOverride       { get => _colliderScopeOverride; set => _colliderScopeOverride = value ?? ""; }
        public int    InteractableOverride        { get => _interactableOverride;   set => _interactableOverride = value; }

        /// <summary>Full (un-split) sprite last applied, or null before <see cref="Apply"/>.</summary>
        public Sprite SourceSprite => _sourceSprite;

        /// <summary>
        /// Whether this placement is interactable: the per-instance override when set,
        /// otherwise the template's flag. Drives the player-mode hover highlight.
        /// </summary>
        public bool Interactable =>
            _interactableOverride == -1
                ? (_template != null && _template.interactable)
                : _interactableOverride == 1;

        /// <summary>
        /// Effective collider scope: instance override (if set) else template's value.
        /// "CG" = collision map shared per-image, "CU" = unique per-instance.
        /// </summary>
        public string EffectiveColliderScope =>
            string.IsNullOrEmpty(_colliderScopeOverride) ? (_template?.colliderScope ?? "CG") : _colliderScopeOverride;

        /// <summary>
        /// World-space AABB of the rendered building (full sprite, top + bottom).
        /// Returns false when the renderers haven't been built yet.
        /// Used by the runtime Buildings Editor for hover detection and outline drawing.
        /// </summary>
        public bool TryGetWorldRect(out Rect rect)
        {
            rect = default;

            // Primary path: derive from actual sprites (most accurate, accounts for texture
            // size which may differ from originalScale after import).
            if (_bottomRenderer != null && _bottomRenderer.sprite != null)
            {
                float sx = transform.localScale.x;
                float sy = transform.localScale.y;
                float bottomH = _bottomRenderer.sprite.rect.height / PPU;
                float topH    = (_topRenderer != null && _topRenderer.sprite != null)
                    ? _topRenderer.sprite.rect.height / PPU
                    : 0f;
                float spriteW = _bottomRenderer.sprite.rect.width / PPU;
                float w = spriteW * sx;
                float h = (bottomH + topH) * sy;
                Vector3 pos = transform.position;
                rect = new Rect(pos.x - w * 0.5f, pos.y, w, h);
                return true;
            }

            // Fallback: derive from template + scale override when renderers are not yet
            // set up (e.g. EditMode tests that inject the template directly without calling
            // Apply(), or buildings whose sprite failed to load).
            if (_template != null && _template.originalScale.x > 0 && _template.originalScale.y > 0)
            {
                int effW = (_scaleOverride.x > 0) ? _scaleOverride.x : _template.originalScale.x;
                int effH = (_scaleOverride.y > 0) ? _scaleOverride.y : _template.originalScale.y;
                float w = effW / PPU;
                float h = effH / PPU;
                Vector3 pos = transform.position;
                rect = new Rect(pos.x - w * 0.5f, pos.y, w, h);
                return true;
            }

            return false;
        }

        /// <summary>
        /// World-space rect of grid cell (row, col) in a (rows × cols) collision
        /// grid. Row 0 = top of the building's sprite (matches the JSON authored
        /// format and <see cref="World.BuildingsRuntimeEditor.HandleColliderPaint"/>).
        ///
        /// This is the SINGLE SOURCE OF TRUTH used by every consumer that needs
        /// per-cell geometry — the in-editor visual overlay, the click-to-paint
        /// hit test, the editor-side BoxCollider2D placement, and the runtime
        /// BuildingCollisionLoader. Sharing this helper guarantees those four
        /// systems can never drift apart.
        /// </summary>
        public bool TryGetWorldCellRect(int row, int col, int rows, int cols, out Rect cell)
        {
            cell = default;
            if (rows <= 0 || cols <= 0) return false;
            if (!TryGetWorldRect(out var rect)) return false;
            float cellW = rect.width  / cols;
            float cellH = rect.height / rows;
            float xMin = rect.xMin + col * cellW;
            float yMin = rect.yMin + (rows - 1 - row) * cellH; // row 0 = top → highest yMin
            cell = new Rect(xMin, yMin, cellW, cellH);
            return true;
        }

        /// <summary>
        /// Recompute the bottom + top renderer sortingOrders from the current
        /// transform.position.y plus the per-instance Z offsets.
        ///
        /// Must be called after any code path that mutates the building's
        /// world position outside of the Apply/place pipeline — chiefly the
        /// drag-move flow in BuildingsRuntimeEditor, which writes
        /// <c>transform.position</c> directly each frame. Without this call
        /// the building keeps its initial Y-sort and renders behind/in-front
        /// of entities at its OLD scene Y, which surfaces as visible ordering
        /// glitches when a building is dragged across other entities.
        /// </summary>
        public void RefreshSorting() => ApplyZOffsets();

        private void ApplyZOffsets()
        {
            // Z offsets act as a HARD TIER on top of the Y-sort: a +1 in
            // Z always wins against any Y-sort difference, a +N always
            // beats +(N-1). Without SortingConfig.Z_TIER_SCALE the raw
            // zOffset (±8) lost against a Y diff of 0.1 world units
            // (since YToSortingOrder contributes ±100 per world unit).
            //
            // Crucially, ALSO promote/demote the SORTING LAYER when the
            // Z is non-zero. Unity sorts by sortingLayer FIRST and
            // sortingOrder SECOND, so a Z+8 footprint sitting on
            // WallsBottom would still render BEHIND a Z=0 canopy on
            // WallsTop no matter how large its sortingOrder is. To make
            // a higher-Z building render entirely above a lower-Z one,
            // each renderer's effective layer follows its own Z sign:
            //
            //   ZBottomOffset > 0 → footprint promoted from WallsBottom
            //                       up to WallsTop, escaping the layer
            //                       hierarchy. Side-effect: the player
            //                       no longer walks "over" this footprint;
            //                       that's a deliberate trade-off the
            //                       designer opted in to by setting Z>0.
            //   ZTopOffset    < 0 → canopy demoted from WallsTop down
            //                       to WallsBottom (no longer occludes
            //                       entities). Designer-opt-in for
            //                       decorative / floor-level canopies.
            //
            // For ZTopOffset >= ZBottomOffset, a +1 nudge on the canopy
            // ensures it still wins against its OWN footprint when both
            // share a sorting layer (e.g. when both Z's are positive).
            int baseY = SortingConfig.YToSortingOrder(transform.position.y);
            if (_bottomRenderer != null)
            {
                _bottomRenderer.sortingLayerName = (_zBottomOffset > 0)
                    ? SortingConfig.LAYER_WALLS_TOP
                    : SortingConfig.LAYER_WALLS_BOTTOM;
                _bottomRenderer.sortingOrder = baseY + _zBottomOffset * SortingConfig.Z_TIER_SCALE;
            }
            if (_topRenderer != null)
            {
                _topRenderer.sortingLayerName = (_zTopOffset < 0)
                    ? SortingConfig.LAYER_WALLS_BOTTOM
                    : SortingConfig.LAYER_WALLS_TOP;
                int topOrder = baseY + _zTopOffset * SortingConfig.Z_TIER_SCALE;
                if (_zTopOffset >= _zBottomOffset) topOrder += 1;
                _topRenderer.sortingOrder = topOrder;
            }
        }

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
    }
}