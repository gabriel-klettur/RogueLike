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

        [Tooltip("Tile layer (0..8) the FOOTPRINT sits directly above. Default 4 keeps it under the player. Maps to overrides.layer_bottom.")]
        [SerializeField, Range(0, SortingConfig.MAX_VISUAL_LAYER)] private int _zBottom = SortingConfig.DEFAULT_PROP_Z_BOTTOM;

        [Tooltip("Tile layer (0..8) the CANOPY sits directly above. Default 6 keeps it over the player. Maps to overrides.layer_top.")]
        [SerializeField, Range(0, SortingConfig.MAX_VISUAL_LAYER)] private int _zTop = SortingConfig.DEFAULT_PROP_Z_TOP;

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

        // What the building looked like before it was destroyed, so a regrow can put it back.
        // Captured on the first remains swap only: a second swap would snapshot the STUMP and
        // make the regrow a no-op that looks like a bug in the regrow clock.
        private bool   _hasPristineSnapshot;
        private Sprite _pristineFootprintSprite;
        private Vector3 _pristineLocalScale = Vector3.one;
        private float  _pristineSplitRatioOverride = -1f;
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
        /// <summary>
        /// Tile layer (0..8) the footprint sits directly above. Z IS the layer: it selects the
        /// sorting slot through <see cref="SortingConfig.PropSortingLayer"/>, so whether this
        /// half draws over or under a painted wall is answered by the layer name and never by
        /// an order. Clamped, and re-applied on the frame it changes so the Buildings editor
        /// sees it move.
        /// </summary>
        public int ZBottom
        {
            get => _zBottom;
            set { _zBottom = Mathf.Clamp(value, 0, SortingConfig.MAX_VISUAL_LAYER); ApplySorting(); }
        }

        /// <summary>Tile layer (0..8) the canopy sits directly above. See <see cref="ZBottom"/>.</summary>
        public int ZTop
        {
            get => _zTop;
            set { _zTop = Mathf.Clamp(value, 0, SortingConfig.MAX_VISUAL_LAYER); ApplySorting(); }
        }
        public string ColliderScopeOverride       { get => _colliderScopeOverride; set => _colliderScopeOverride = value ?? ""; }
        public int    InteractableOverride        { get => _interactableOverride;   set => _interactableOverride = value; }

        /// <summary>Full (un-split) sprite last applied, or null before <see cref="Apply"/>.</summary>
        public Sprite SourceSprite => _sourceSprite;

        /// <summary>
        /// The lower half of the sprite — the part drawn UNDER the player, and the part a
        /// blow actually lands on. A tree's trunk, a house's ground floor.
        /// </summary>
        public SpriteRenderer FootprintRenderer => _bottomRenderer;

        /// <summary>
        /// The upper half, drawn OVER the player. A tree's canopy, a roof.
        /// </summary>
        public SpriteRenderer CanopyRenderer => _topRenderer;

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
        public void RefreshSorting() => ApplySorting();

        private void ApplySorting()
        {
            // Z IS the layer. Each half names the tile layer it sits directly above, and
            // SortingConfig.PropSortingLayer hands back the slot between the tiles of that
            // layer and those of the next — so whether a building draws over or under a
            // painted wall is decided by sorting-LAYER comparison, by name, never by
            // sortingOrder arithmetic. Inside one slot the Y-sort orders buildings against
            // each other the same way it orders entities.
            //
            // This replaced a Z that was a signed TIER multiplied into sortingOrder and
            // promoted a half to WallsTop on its sign. That ladder ended at WallsTop, which is
            // below the layer-7 and layer-8 tile slots, so no value of Z could put a building
            // over a wall painted up there — an author pressing "+" got nothing, silently. Four
            // of 301 shipped placements used it, which is what a control that cannot do the
            // job looks like in data. The multiplier went with it: the incident it caused
            // (100000 wrapping the 16-bit sort key to -27880) cannot recur when nothing is
            // multiplied.
            int baseY = SortingConfig.YToSortingOrder(transform.position.y);

            if (_bottomRenderer != null)
            {
                _bottomRenderer.sortingLayerName = SortingConfig.PropSortingLayer(_zBottom);
                _bottomRenderer.sortingOrder = baseY;
            }
            if (_topRenderer != null)
            {
                _topRenderer.sortingLayerName = SortingConfig.PropSortingLayer(_zTop);
                // One above the footprint: when both halves share a slot the canopy must still
                // win, or the two z-fight at equal order and the tie-break is scene order.
                _topRenderer.sortingOrder = baseY + 1;
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