using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {
        // ── Per-layer tilemap cache (drives "Show Tile Layer" overlay) ────────
        // Resolved lazily on first toggle-ON. The array is reused across frames so
        // there's exactly one allocation per editor session. Worldload / scene
        // reset clears the cache via InvalidateLayerTilemapsCache below.
        private Tilemap[] _layerTilemapsCache;

        private Tilemap[] EnsureLayerTilemapsCache()
        {
            if (_layerTilemapsCache == null)
                _layerTilemapsCache = new Tilemap[9];

            // Re-resolve any null slots — covers both the lazy first call and
            // recovery after a zone reload that may have destroyed a tilemap.
            bool needsResolve = false;
            for (int i = 0; i < _layerTilemapsCache.Length; i++)
                if (_layerTilemapsCache[i] == null) { needsResolve = true; break; }

            if (needsResolve && worldGridBuilder != null)
            {
                for (int i = 0; i < _layerTilemapsCache.Length; i++)
                {
                    var layer = (TilemapLayerSetup.TilemapLayer)i;
                    _layerTilemapsCache[i] = worldGridBuilder.GetTilemap(layer);
                }
            }

            return _layerTilemapsCache;
        }

        internal void InvalidateLayerTilemapsCache()
        {
            if (_layerTilemapsCache == null) return;
            for (int i = 0; i < _layerTilemapsCache.Length; i++)
                _layerTilemapsCache[i] = null;
        }


        // ── Helpers ──

        // Per-frame cache for GetCurrentTilemap / GetCollisionTilemap. WorldGridBuilder.GetTilemap
        // does a Transform.Find + GetComponent each call; without this cache the tile
        // editor pays 6+ Find calls per frame (UpdateGridCursor, UpdateViewPanelHover,
        // HandleMouseInput, etc.). Reset every frame by InvalidateTilemapFrameCache.
        private int      _tilemapCacheFrame      = -1;
        private Tilemap  _cachedCurrentTilemap;
        private TilemapLayerSetup.TilemapLayer _cachedCurrentLayer;
        private Tilemap  _cachedCollisionTilemap;

        // Per-frame cache for IsPointerOverUI. EventSystem.IsPointerOverGameObject
        // raycasts the entire UI canvas tree; calling it from each of the four
        // hot paths (HandleMouseInput, UpdateGridCursor, UpdateViewPanelHover,
        // CommitRectSelection) is wasted work — the value can't change inside
        // one frame.
        private int  _pointerOverUiFrame = -1;
        private bool _pointerOverUiCached;

        private void InvalidatePointerOverUiFrameCache()
        {
            _pointerOverUiFrame = -1;
        }

        internal bool IsPointerOverUiCached()
        {
            int f = Time.frameCount;
            if (_pointerOverUiFrame == f) return _pointerOverUiCached;
            _pointerOverUiFrame   = f;
            _pointerOverUiCached  = _input != null && _input.IsPointerOverUI();
            return _pointerOverUiCached;
        }

        /// <summary>
        /// Reset the per-frame tilemap cache. Called at the top of <see cref="Update"/>
        /// so any layer change made earlier in the frame is honoured on the next frame.
        /// </summary>
        private void InvalidateTilemapFrameCache()
        {
            _tilemapCacheFrame = Time.frameCount;
            _cachedCurrentTilemap = null;
            _cachedCollisionTilemap = null;
        }

        // ── CollisionTagMap host (analogue of TerrainMap) ────────────────
        // Lazy property so EditMode test fixtures that construct the manager
        // without going through Start() still get a valid map on first access.
        private CollisionTagMap _collisionTagMap;

        /// <summary>
        /// Per-cell tag layer parallel to the Collision tilemap. Resolves to
        /// <see cref="CollisionTagMap.Wildcard"/> when no explicit tag is stored — that
        /// keeps legacy maps (and any cell painted before the user changes
        /// <see cref="TileEditorState.ActiveCollisionTag"/>) at the pre-feature behaviour.
        /// </summary>
        public CollisionTagMap CollisionTags => _collisionTagMap ??= new CollisionTagMap();

        private Tilemap GetCurrentTilemap()
        {
            if (worldGridBuilder == null) return null;
            if (_tilemapCacheFrame == Time.frameCount
                && _cachedCurrentTilemap != null
                && _cachedCurrentLayer == _state.CurrentLayer)
                return _cachedCurrentTilemap;
            _cachedCurrentLayer = _state.CurrentLayer;
            _cachedCurrentTilemap = worldGridBuilder.GetTilemap(_state.CurrentLayer);
            return _cachedCurrentTilemap;
        }

        /// <summary>
        /// Resolve the tilemap for an arbitrary layer (i.e. not necessarily the active one).
        /// Mirrors <see cref="GetCurrentTilemap"/> but uncached — caller is the rare cross-layer
        /// operation (Move-To-Layer), where caching a second slot would complicate frame
        /// invariants for little gain.
        /// </summary>
        private Tilemap GetTilemapForLayer(TilemapLayerSetup.TilemapLayer layer)
        {
            return worldGridBuilder != null ? worldGridBuilder.GetTilemap(layer) : null;
        }

        private Vector3Int GetCellUnderMouse(Tilemap tilemap)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            // Use MouseInputManager so the legacy backend supplies the position
            // when the new InputSystem package's Mouse.current is stale at (0,0).
            Vector3 screenPos = (Vector3)Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(screenPos);
            mouseWorld.z = 0f;
            return tilemap.WorldToCell(mouseWorld);
        }

        private Vector3 GetCellWorldCenter(Tilemap tilemap, Vector3Int cellPos)
        {
            Vector3 bottomLeft = tilemap.CellToWorld(cellPos);
            Vector3 cellSize = tilemap.cellSize;
            return bottomLeft + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        }

        // The Tile Editor must allow Brush, Eraser, Fill and Collider Draw/Erase
        // to operate on EVERY cell of EVERY zone. Earlier the F11 MapEditor could
        // install an `_editConstraint` (ZoneManager.IsTileInEditableZone) that
        // silently rejected paints in zones flagged `editableInTileEditor=false`
        // (e.g. the lobby) and in any cell outside a defined zone. That produced
        // dead spots on the map where the brush appeared to do nothing.
        //
        // Per product requirement the gate is now disabled at this single
        // choke point. SetEditConstraint / ClearEditConstraint remain on the
        // public API for backwards compatibility but no longer affect editing.
        // If a future need arises to re-introduce zone locks, restore the
        // original body and audit every TileBrush.* call site.
        private bool CanEditCell(Vector3Int cellPos) => true;

        protected override void OnDestroy()
        {
            _input?.Dispose();
            DisposeColliderTile();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }
    }
}
