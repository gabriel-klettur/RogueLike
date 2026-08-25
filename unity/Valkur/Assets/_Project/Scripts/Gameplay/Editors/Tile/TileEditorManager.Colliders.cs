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

            // M1.10 guard: refuse Draw activation when no layer is currently selected
            // in the Apply-To-Layer picker. Painting with an empty mask would stamp
            // cells the new physics dispatch (DispatchCellToSubmaps) cannot route
            // anywhere — the user must pick at least one layer (or "*") first.
            if (!wasDraw && string.IsNullOrEmpty(_state.ActiveCollisionTag))
            {
                _ui?.SetStatus("Select at least one layer (or *) before enabling Draw.");
                return;
            }

            // End any in-flight stroke so Draw-mode entry/exit doesn't leak edits.
            _undo?.EndStroke();
            _state.IsDragging = false;
            _state.BrushStrokeCells.Clear();
            // A stroke can be force-ended here mid-drag (e.g. the user clicks this
            // very toggle to turn Draw back off) — flush any pending composite
            // rebake so it isn't lost.
            FlushPendingColliderRebake();

            _state.CurrentColliderMode = wasDraw
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Draw;

            // Mutex with the Layer-Jumps panel: mirrors OnDrawLayerJumpsClicked /
            // OnEraseLayerJumpsClicked forcing Colliders off. Without this, enabling
            // Draw Colliders while Draw/Erase Jumps was already on left BOTH modes
            // active — IsColliderEditModeActive() is checked first in
            // HandleMouseInput, so clicks silently went to Colliders while the
            // Layer-Jumps toggle kept showing "on".
            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None
                && _state.CurrentLayerJumpMode != TileEditorState.LayerJumpMode.None)
                _state.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.None;

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
            _ui?.RefreshLayerJumpsToggles(); // Layer-Jumps may have been turned OFF by the mutex
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
            FlushPendingColliderRebake();

            _state.CurrentColliderMode = wasErase
                ? TileEditorState.ColliderMode.None
                : TileEditorState.ColliderMode.Erase;

            // Mutex with the Layer-Jumps panel — see OnDrawCollidersClicked.
            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None
                && _state.CurrentLayerJumpMode != TileEditorState.LayerJumpMode.None)
                _state.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.None;

            if (_state.CurrentColliderMode != TileEditorState.ColliderMode.None)
                _state.ShowColliderOverlay = true;

            if (_state.CurrentColliderMode == TileEditorState.ColliderMode.Erase)
                _state.CurrentTool = TileEditorState.Tool.Eraser;

            ApplyColliderOverlayVisibility();
            _ui?.RefreshColliderToggles();
            _ui?.RefreshLayerJumpsToggles();
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
            _gridOverlay.SetCollisionTagMap(CollisionTags);
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

            Vector3Int cellPos = GetCellUnderMouse(collision);

            bool drawing = _state.CurrentColliderMode == TileEditorState.ColliderMode.Draw;
            TileBase tileToPaint = drawing ? GetOrCreateColliderTile() : null;

            // Use MouseInputManager so the legacy backend kicks in if the new
            // InputSystem package drops OS events (recurring Unity 2022.3 bug).
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _state.BrushStrokeCells.Clear();
                _state.SelectedCellPos = cellPos;
                _undo.StartStroke(collision);
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, canEditCell: null);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                var tagEdits = ApplyTagToEdits(edits, drawing);
                _undo.RecordMetadataEdits(tagEdits);
                AddCellsToBrushStroke(cellPos);
                _state.IsDragging = true;
                if (edits.Count > 0)
                    _colliderStrokeDirty = true;
            }
            else if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _state.IsDragging)
            {
                _state.SelectedCellPos = cellPos;
                var edits = TileBrush.Paint(collision, cellPos, tileToPaint, _state.BrushSize, canEditCell: null);
                _undo.RecordEdits(edits);
                _persistence?.MarkBatchDirty(edits);
                var tagEdits = ApplyTagToEdits(edits, drawing);
                _undo.RecordMetadataEdits(tagEdits);
                AddCellsToBrushStroke(cellPos);
                if (edits.Count > 0)
                    _colliderStrokeDirty = true;
            }
            else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _undo.EndStroke();
                _state.IsDragging = false;
                _state.BrushStrokeCells.Clear();
                // Physics geometry only needs to be correct once the stroke commits,
                // not every drag frame — see the perf note on RegenerateCompositeCollider.
                FlushPendingColliderRebake();
                // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        // ── Apply-To-Layer picker handler ───────────────────────────────

        /// <summary>
        /// User clicked one of the Apply-To-Layer buttons in the Colliders panel.
        /// Semantics (M1.10 multi-tag picker):
        /// <list type="bullet">
        ///   <item>Clicked <see cref="CollisionTagMap.Wildcard"/> ("*"):
        ///         all/clear shortcut — if the active mask is already full
        ///         (== <see cref="CollisionTagMap.FullLayerMask"/>), clears it;
        ///         otherwise sets it to FULL. The on-disk form alternates
        ///         between "*" and the empty string respectively.</item>
        ///   <item>Clicked a digit "0".."8": toggles that individual layer bit
        ///         in the active mask. The canonical CSV form is recomputed
        ///         after every toggle so the storage stays sorted + deduped.</item>
        ///   <item>Anything else (legacy CSV from a future caller): canonicalised
        ///         once and stored as-is; the picker behaves single-set for that path.</item>
        /// </list>
        /// </summary>
        internal void OnCollisionTagChanged(string tag)
        {
            int currentMask = CurrentCollisionMask();

            if (tag == CollisionTagMap.Wildcard)
            {
                // All/clear shortcut.
                bool wasFull = currentMask == CollisionTagMap.FullLayerMask;
                int nextMask = wasFull ? 0 : CollisionTagMap.FullLayerMask;
                _state.ActiveCollisionTag = nextMask == CollisionTagMap.FullLayerMask
                    ? CollisionTagMap.Wildcard
                    : string.Empty;
            }
            else if (tag != null && tag.Length == 1 && tag[0] >= '0' && tag[0] <= '8')
            {
                // Independent toggle for one of the nine digit buttons.
                int bit = 1 << (tag[0] - '0');
                int nextMask = currentMask ^ bit;
                _state.ActiveCollisionTag = nextMask == 0
                    ? string.Empty
                    : CollisionTagMap.TagFromLayerMask(nextMask);
            }
            else if (CollisionTagMap.IsValidTag(tag))
            {
                // Direct CSV set (canonicalised by the map's Set/Canonicalize path).
                _state.ActiveCollisionTag = CollisionTagMap.TagFromLayerMask(
                    CollisionTagMap.LayerMaskFromTag(tag));
            }
            else
            {
                // Garbage → safe fallback to the legacy wildcard semantic.
                _state.ActiveCollisionTag = CollisionTagMap.Wildcard;
            }

            _ui?.RefreshCollisionTagPicker();
            _ui?.SetStatus(string.IsNullOrEmpty(_state.ActiveCollisionTag)
                ? "Collider tag → (none — pick at least one layer to draw)"
                : $"Collider tag → {_state.ActiveCollisionTag}");
        }

        /// <summary>
        /// Layer mask currently selected in the Apply-To-Layer picker. Empty active
        /// tag returns 0 (no layers selected — paint is disabled until the user
        /// toggles at least one layer or clicks "*").
        /// </summary>
        private int CurrentCollisionMask()
        {
            if (string.IsNullOrEmpty(_state.ActiveCollisionTag)) return 0;
            return CollisionTagMap.LayerMaskFromTag(_state.ActiveCollisionTag);
        }

        /// <summary>
        /// Mirror every edit emitted by <see cref="TileBrush.Paint"/> into
        /// <see cref="CollisionTagMap"/>:
        ///   • Drawing → stamp <see cref="TileEditorState.ActiveCollisionTag"/> on each
        ///     cell that received a collider tile.
        ///   • Erasing → clear the cell so a future re-paint starts back at the user's
        ///     current active tag (no stale tag rides on top of a fresh paint).
        ///
        /// Now recorded into the same <see cref="TileEditBatch"/> as the visual tile
        /// edits via <see cref="MetadataEdit"/> so a single Ctrl+Z reverts both. The
        /// fallback when an Undo reinstates a collider without a tag is still the map's
        /// wildcard default (no explicit entry), which is the safe ("applies to all")
        /// choice.
        /// </summary>
        private List<MetadataEdit> ApplyTagToEdits(List<TileEdit> edits, bool drawing)
        {
            var metadataEdits = new List<MetadataEdit>();
            if (edits == null || edits.Count == 0) return metadataEdits;
            string tag = drawing ? _state.ActiveCollisionTag : null;
            if (drawing && !CollisionTagMap.IsValidTag(tag))
                tag = CollisionTagMap.Wildcard;

            for (int i = 0; i < edits.Count; i++)
            {
                var pos = edits[i].Position;
                string oldRaw = CollisionTags.GetRaw(pos);
                string newRaw = drawing ? tag : null;
                if (oldRaw == newRaw) continue; // no-op: nothing to undo
                metadataEdits.Add(new MetadataEdit(pos, oldRaw, newRaw, CollisionTags));
                CollisionTags.Set(pos, newRaw); // Set(x, null) already clears — replaces the old Set/Clear branch
            }
            return metadataEdits;
        }
    }
}