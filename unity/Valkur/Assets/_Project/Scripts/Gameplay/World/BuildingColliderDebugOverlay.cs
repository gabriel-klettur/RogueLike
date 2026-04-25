using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Visualizes the currently active BoxCollider2D shapes of a building.
    ///
    /// The overlay is derived from the real collider state:
    ///   - root footprint collider when it is enabled
    ///   - fine-grained CollTile_* child colliders when they exist
    ///
    /// Visuals are updated in place instead of being recreated every frame. This
    /// keeps Show/Hide stable while buildings move, resize, or repaint colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class BuildingColliderDebugOverlay : MonoBehaviour
    {
        private const string VISUAL_PREFIX = "_ColliderDebug_";
        private const string COLL_TILE_PREFIX        = "CollTile_";
        private const string POOLED_COLL_TILE_PREFIX = "_PooledCollTile_";
        private const float OUTLINE_WIDTH = 0.05f;
        private const float Z_OFFSET = -0.1f;

        private sealed class VisualEntry
        {
            public GameObject Host;
            public SpriteRenderer Fill;
            public LineRenderer Line;

            // Cached last-applied state so SyncVisuals can skip writing transform
            // / line / renderer properties when the inputs haven't changed. This
            // is the hot path: 142 overlays x ~5 tiles each x 60+ fps = tens of
            // thousands of property writes per second if we don't short-circuit.
            public bool HasCachedState;
            public Vector3 LastCenter;
            public Vector3 LastSize;
            public bool LastActive;
        }

        private static Sprite s_whiteSprite;
        private static Material s_lineMaterial;
        private static Material s_fillMaterial;

        private static readonly Color FillColor = new Color(1f, 0f, 0f, 0.48f);
        private static readonly Color LineColor = new Color(1f, 0f, 0f, 1f);

        private readonly List<VisualEntry> _visuals = new List<VisualEntry>();
        private bool _visible;

        // Default-mode (BoxCollider2D enumeration) cache. Re-scanned only when
        // _dirty is set OR the building transform reports hasChanged. Prevents
        // a GetComponentsInChildren call every LateUpdate across 142 overlays.
        private BoxCollider2D[] _defaultColliderCache;
        private int             _defaultColliderCount;

        // Dirty flag: when set, the next SyncVisuals rebuilds the default-mode
        // cache and re-applies ALL visuals regardless of cached state. Set by
        // SetVisible(true), SetAuthoringCells, ClearAuthoringCells, MarkDirty.
        private bool _dirty = true;

        // Authoring mode: when set, the overlay renders ONE visual per supplied
        // world-space cell rect (single source of truth = the editor's grid +
        // building rect). When cleared, the overlay falls back to its original
        // behaviour of inferring rects from the building's BoxCollider2D shapes.
        //
        // The buildings editor enables authoring mode for the active building
        // while the colliders panel is open so click-to-paint, grid storage and
        // the visual feedback all share a single coordinate system. This
        // eliminates the historical drift caused by re-deriving collider tile
        // positions through GetBuildingLocalSpriteSize / ResampleGrid.
        private bool        _authoringMode;
        private Rect[]      _authoringCells;
        private int         _authoringCellCount;

        public bool Visible => _visible;
        public int CurrentVisualCount { get; private set; }

        /// <summary>True while the overlay is rendering from supplied cell rects.</summary>
        public bool IsAuthoringMode => _authoringMode;

        /// <summary>Number of authoring cells currently driving the overlay (0 when not in authoring mode).</summary>
        public int AuthoringCellCount => _authoringMode ? _authoringCellCount : 0;

        public void SetVisible(bool visible)
        {
            if (_visible == visible && !visible)
            {
                // already hidden, nothing to do
                return;
            }
            _visible = visible;
            if (!_visible)
            {
                SetAllDebugRootsActive(false);
                // Reset cached active flag so the next SetVisible(true) does not
                // short-circuit in UpdateVisualFromWorldAabb before re-activating.
                for (int i = 0; i < _visuals.Count; i++)
                    if (_visuals[i] != null) _visuals[i].LastActive = false;
                CurrentVisualCount = 0;
                return;
            }

            _dirty = true;
            SyncVisuals();
        }

        /// <summary>
        /// Force a full re-sync on the next opportunity. Call after mutating
        /// the set of colliders (painting tiles, repooling, etc.) so the
        /// overlay rebuilds its cache and refreshes every visual.
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
            if (_visible) SyncVisuals();
        }

        /// <summary>
        /// Switch the overlay into authoring mode and replace its cell list with
        /// the supplied world-space rects. Each rect produces exactly one filled
        /// visual at that world position. Call <see cref="ClearAuthoringCells"/>
        /// to revert to BoxCollider2D-derived rendering.
        /// </summary>
        /// <param name="worldCellRects">
        /// World-space rects, one per visible collider cell. May be null/empty
        /// (in that case the overlay enters authoring mode but renders nothing).
        /// </param>
        public void SetAuthoringCells(IList<Rect> worldCellRects)
        {
            int count = worldCellRects != null ? worldCellRects.Count : 0;
            bool wasAuthoring = _authoringMode;
            _authoringMode = true;
            if (_authoringCells == null || _authoringCells.Length < count)
                _authoringCells = new Rect[Mathf.Max(count, 8)];

            // Fast path: if neither the cell count nor any individual rect
            // changed since the last call, skip the dirty mark + SyncVisuals.
            // This is critical because the editor pushes the active building's
            // cells every frame and the overlay's SyncVisuals would otherwise
            // re-apply transforms / line positions across every visual on every
            // frame, even when nothing moved.
            bool sameAsBefore = wasAuthoring && count == _authoringCellCount;
            if (sameAsBefore)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_authoringCells[i] != worldCellRects[i])
                    {
                        sameAsBefore = false;
                        break;
                    }
                }
            }

            for (int i = 0; i < count; i++)
                _authoringCells[i] = worldCellRects[i];
            _authoringCellCount = count;

            if (sameAsBefore) return;

            _dirty = true;
            if (_visible) SyncVisuals();
        }

        /// <summary>
        /// Leave authoring mode and revert to enumerating live BoxCollider2D
        /// shapes for the visuals. Idempotent.
        /// </summary>
        public void ClearAuthoringCells()
        {
            if (!_authoringMode && _authoringCellCount == 0) return;
            _authoringMode = false;
            _authoringCellCount = 0;
            _dirty = true;
            if (_visible) SyncVisuals();
        }

        private void LateUpdate()
        {
            if (!_visible) return;
            // Lazy sync: only re-apply when the building itself moved/scaled or
            // something explicitly marked us dirty. Idle overlays cost only a
            // bool check per frame. This is what lets us keep 142 overlays on
            // simultaneously without dropping below 120fps.
            bool transformMoved = transform.hasChanged;
            if (!_dirty && !transformMoved) return;
            if (transformMoved) transform.hasChanged = false;
            SyncVisuals();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null && _visuals[i].Host != null)
                    DestroyUnityObject(_visuals[i].Host);
            }
            _visuals.Clear();
        }

    }
}