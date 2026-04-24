using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Colliders panel logic for the runtime Tile Editor:
    ///   • toggle handlers (Show / Draw / Erase)
    ///   • mouse routing into the Collision tilemap when Draw or Erase is active
    ///   • shared invisible "wall" tile factory (mirrors WorldLoader.GetWallCollisionTile)
    ///   • CompositeCollider2D regeneration after edits so Physics2D queries stay in sync
    ///
    /// State is owned by <see cref="TileEditorState"/> (ShowColliderOverlay,
    /// CurrentColliderMode). The UI just emits click signals; this file flips state and
    /// asks the UI to repaint.
    /// </summary>
    public partial class TileEditorManager
    {
        // Cached invisible Tile used to fill collision cells. Created lazily on first
        // draw and disposed in OnDestroy(). Sprite is intentionally invisible (alpha 0)
        // because the red overlay is drawn by TileEditorGridOverlay, not by this tile.
        private Tile _colliderTile;

        // ── Toggle handlers (called by UI buttons) ────────────────────────

        private void OnShowCollidersClicked()
        {
            _state.ShowColliderOverlay = !_state.ShowColliderOverlay;
            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.SetStatus(_state.ShowColliderOverlay ? "Colliders visible" : "Colliders hidden");
        }

        private void OnDrawCollidersClicked()
        {
            bool wasDraw = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            // End any in-flight stroke so Draw-mode entry/exit doesn't leak edits.
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            _state.CurrentColliderMode = wasDraw
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Draw;

            // Auto-enable overlay when entering an editing mode so the user sees their work.
            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None)
                _state.ShowColliderOverlay = true;

            // Mirror cursor footprint by setting the matching tool (so the brush preview
            // square is shown). Layer is NOT switched — drawing routes to Collision
            // explicitly via HandleColliderInput().
            if (_state.CurrentColliderMode == TileEditorState.ColliderMode.Draw)
                _state.CurrentTool = TileEditorState.Tool.Brush;

            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentColliderMode == TileEditorState.ColliderMode.Draw
                ? "Collider draw mode (LMB to paint)"
                : "Collider edit mode disabled");
        }

        private void OnEraseCollidersClicked()
        {
            bool wasErase = _state.CurrentColliderMode == TileEditorState.ColliderMode.Erase;
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();

            _state.CurrentColliderMode = wasErase
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Erase;

            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None)
                _state.ShowColliderOverlay = true;

            if (_state.CurrentColliderMode == TileEditorState.ColliderMode.Erase)
                _state.CurrentTool = TileEditorState.Tool.Eraser;

            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshToolHighlights();
            _ui?.SetStatus(_state.CurrentColliderMode == TileEditorState.ColliderMode.Erase
                ? "Collider erase mode (LMB to remove)"
                : "Collider edit mode disabled");
        }

        // ── Overlay binding ──────────────────────────────────────────────

        /// <summary>
        /// Push the current Show/Hide state into the GL grid overlay and ensure the
        /// Collision tilemap reference is bound so the overlay can sample painted cells.
        /// </summary>
        private void ApplyColliderOverlayVisibility()
        {
            if (_gridOverlay == null) return;
            _gridOverlay.SetCollisionTilemap(GetCollisionTilemap());
            _gridOverlay.SetShowColliderOverlay(_state.ShowColliderOverlay);
        }

        // ── Mouse routing for collider edit modes ────────────────────────

        /// <summary>
        /// True when the current collider mode owns the mouse — the regular
        /// per-tool handlers in TileEditorManager.InputHandlers should be skipped.
        /// </summary>
        private bool IsColliderEditModeActive()
        {
            return _state != null && _state.CurrentColliderMode != TileEditorState.ColliderMode.None;
        }

        /// <summary>
        /// Apply the current collider mode (Draw or Erase) to the cell under the mouse.
        /// Mirrors HandleBrushInput / HandleEraserInput but always targets the Collision
        /// tilemap and uses the cached invisible collider tile.
        /// </summary>
        private void HandleColliderInput()
        {
            var collision = GetCollisionTilemap();
            if (collision == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector3Int cellPos = GetCellUnderMouse(collision);

            bool drawing = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            TileBase tileToPaint = drawing ? GetOrCreateColliderTile() : null;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(collision);
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
                else if (!CanEditCell(cellPos))
                    _ui.SetStatus("Blocked: zone is not editable. Use F11 Map Editor.");
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, CanEditCell);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                AddCellsToBrushStroke(cellPos);
                if (edits.Count > 0)
                    RegenerateCompositeCollider(collision);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private Tilemap GetCollisionTilemap()
        {
            if (worldGridBuilder == null) return null;
            return worldGridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
        }

        /// <summary>
        /// Build (once) and cache an invisible Tile suitable for the Collision tilemap.
        /// Matches WorldLoader.GetWallCollisionTile semantics: alpha-zero sprite, Grid
        /// collider type so TilemapCollider2D + CompositeCollider2D pick it up.
        /// </summary>
        private TileBase GetOrCreateColliderTile()
        {
            if (_colliderTile != null) return _colliderTile;

            var sprite = Resources.Load<Sprite>("Tiles/wall");
            if (sprite == null) sprite = Resources.Load<Sprite>("Tiles/floor");

            _colliderTile = ScriptableObject.CreateInstance<Tile>();
            _colliderTile.sprite = sprite;
            _colliderTile.color = new Color(1f, 1f, 1f, 0f);
            _colliderTile.colliderType = Tile.ColliderType.Grid;
            _colliderTile.hideFlags = HideFlags.HideAndDontSave;
            _colliderTile.name = "TileEditorColliderTile";
            return _colliderTile;
        }

        /// <summary>
        /// The Collision tilemap is wired with a CompositeCollider2D in
        /// <c>generationType = Manual</c> mode (see WorldGridBuilder). After painting or
        /// erasing collision tiles we must explicitly regenerate the composite shape so
        /// <summary>
        /// The Collision tilemap is wired with a CompositeCollider2D in
        /// <c>generationType = Manual</c> mode (see WorldGridBuilder). After painting or
        /// erasing collision tiles we must explicitly regenerate the composite shape so
        /// Physics2D queries (raycasts, OverlapBox, etc.) reflect the new geometry.
        ///
        /// Race fix: <see cref="UnityEngine.Tilemaps.TilemapCollider2D"/> queues
        /// <c>SetTile</c> changes and processes them the FOLLOWING frame. Calling
        /// <c>GenerateGeometry()</c> on the same frame as the paint produces a
        /// composite with <c>pathCount = 0</c> even after <c>RefreshAllTiles()</c>.
        /// We therefore call refresh + bake immediately (so editor visualizers see
        /// the in-progress geometry) AND schedule a deferred bake one frame later
        /// (the only call that actually populates Physics2D query results).
        /// Regression guard: <c>PlayerTileCollisionPlayTests.PaintRefresh_BakeNextFrame_*</c>.
        /// </summary>
        private void RegenerateCompositeCollider(Tilemap collision)
        {
            var composite = collision.GetComponent<CompositeCollider2D>();
            if (composite == null) return;
            collision.RefreshAllTiles();
            composite.GenerateGeometry();
            StartCoroutine(DeferredCompositeRebake(collision, composite));
        }

        private System.Collections.IEnumerator DeferredCompositeRebake(
            Tilemap collision, CompositeCollider2D composite)
        {
            yield return null; // Wait one frame for TilemapCollider2D to ingest queued SetTile changes.
            if (collision == null || composite == null) yield break;
            collision.RefreshAllTiles();
            composite.GenerateGeometry();
        }

        private void DisposeColliderTile()
        {
            if (_colliderTile == null) return;
            if (Application.isPlaying) Destroy(_colliderTile);
            else DestroyImmediate(_colliderTile);
            _colliderTile = null;
        }

        // ── Bulk operations ──────────────────────────────────────────────

        // Layers considered "solid" for auto-collider generation. Mirrors the visual
        // intuition: walls and large objects block the player; ground and floor decals
        // do not. OverheadDetails is purely cosmetic (rendered above entities).
        private static readonly TilemapLayerSetup.TilemapLayer[] s_solidSourceLayers =
        {
            TilemapLayerSetup.TilemapLayer.WallsBottom,
            TilemapLayerSetup.TilemapLayer.WallsTop,
            TilemapLayerSetup.TilemapLayer.Decorations,
            TilemapLayerSetup.TilemapLayer.ObjectsLow,
            TilemapLayerSetup.TilemapLayer.ObjectsHigh,
        };

        /// <summary>
        /// One-shot bulk generator: paints an invisible collision tile on every cell
        /// that already has a visual tile in any of the "solid" source layers
        /// (walls, decorations, low/high objects). Skips cells the user is not
        /// allowed to edit (zone editability check via <see cref="CanEditCell"/>),
        /// rebakes the composite, and marks the zone dirty so Save persists it.
        /// </summary>
        private void OnAutoGenerateCollidersClicked()
        {
            if (worldGridBuilder == null) { _ui?.SetStatus("Auto-collider: WorldGridBuilder missing."); return; }

            var collision = GetCollisionTilemap();
            if (collision == null) { _ui?.SetStatus("Auto-collider: Collision tilemap missing."); return; }

            var tile = GetOrCreateColliderTile();
            int painted = 0;
            int skippedLocked = 0;
            int alreadySolid = 0;
            var seen = new HashSet<Vector3Int>();
            var edits = new List<TileEdit>();

            foreach (var srcLayer in s_solidSourceLayers)
            {
                var src = worldGridBuilder.GetTilemap(srcLayer);
                if (src == null) continue;

                var bounds = src.cellBounds;
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    for (int x = bounds.xMin; x < bounds.xMax; x++)
                    {
                        var pos = new Vector3Int(x, y, 0);
                        if (!seen.Add(pos)) continue;
                        if (src.GetTile(pos) == null) continue;

                        if (collision.GetTile(pos) != null) { alreadySolid++; continue; }
                        if (!CanEditCell(pos)) { skippedLocked++; continue; }

                        var prev = collision.GetTile(pos);
                        collision.SetTile(pos, tile);
                        edits.Add(new TileEdit(pos, prev, tile));
                        painted++;
                    }
                }
            }

            if (painted > 0)
            {
                _undo?.StartStroke(collision);
                _undo?.RecordEdits(edits);
                _undo?.EndStroke();
                _persistence?.MarkBatchDirty(edits);
                RegenerateCompositeCollider(collision);
            }

            // Auto-show overlay so user sees the result.
            if (!_state.ShowColliderOverlay)
            {
                _state.ShowColliderOverlay = true;
                ApplyColliderOverlayVisibility();
                _ui?.RefreshColliderToggles();
            }

            _ui?.SetStatus($"Auto-colliders: painted {painted}, kept {alreadySolid}, locked {skippedLocked}.");
            Debug.Log($"[TileEditor] Auto-collider scan: painted={painted} kept={alreadySolid} locked={skippedLocked}");
        }

        /// <summary>
        /// Bulk-erase every collision cell on the active Collision tilemap. Marks
        /// the zone dirty so Save persists the empty layer. Confirmation lives in
        /// the UI (button hint); here we just execute.
        /// </summary>
        private void OnClearAllCollidersClicked()
        {
            if (worldGridBuilder == null) { _ui?.SetStatus("Clear-collider: WorldGridBuilder missing."); return; }

            var collision = GetCollisionTilemap();
            if (collision == null) { _ui?.SetStatus("Clear-collider: Collision tilemap missing."); return; }

            int cleared = 0;
            int skippedLocked = 0;
            var edits = new List<TileEdit>();
            var bounds = collision.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var pos = new Vector3Int(x, y, 0);
                    var prev = collision.GetTile(pos);
                    if (prev == null) continue;
                    if (!CanEditCell(pos)) { skippedLocked++; continue; }

                    collision.SetTile(pos, null);
                    edits.Add(new TileEdit(pos, prev, null));
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                _undo?.StartStroke(collision);
                _undo?.RecordEdits(edits);
                _undo?.EndStroke();
                _persistence?.MarkBatchDirty(edits);
                RegenerateCompositeCollider(collision);
            }

            _ui?.SetStatus($"Cleared {cleared} collision cells (locked {skippedLocked}).");
            Debug.Log($"[TileEditor] Clear-all-colliders: cleared={cleared} locked={skippedLocked}");
        }
    }
}
