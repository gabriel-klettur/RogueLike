using System.Collections.Generic;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Move-To-Layer: acts on the existing <see cref="TileEditorState.SelectedCells"/>
    /// (maintained by <c>TileEditorManager.SelectHandlers.cs</c>) to relocate tiles
    /// from the current layer to another, atomically, including a co-mutated
    /// Collision cell/tag erase.
    /// </summary>
    public partial class TileEditorManager
    {
        // ── Move To Layer (action on existing selection) ────────────────────
        //
        // Take every cell in <see cref="TileEditorState.SelectedCells"/> that holds a
        // tile on the active layer and move it to <paramref name="destLayer"/> as a
        // single atomic operation: clear the cell on the source tilemap, paint the
        // same tile on the destination tilemap. Both half-edits are recorded in one
        // <see cref="TileEditBatch"/> via the per-edit <c>TargetTilemap</c> override
        // (see <see cref="TileEdit"/> docs) so a single Ctrl+Z reverses both halves.
        //
        // Picker-only selections are filtered out by the empty <c>SelectedCells</c>
        // check — the picker has no map cells so there is nothing to move.
        // Destination-equals-source is a no-op (preserves the existing scene rather
        // than silently churning through every cell). Cells filtered by
        // <see cref="CanEditCell"/> (out-of-zone / read-only) are skipped just like
        // every other bulk operation.
        //
        // After a successful move the editor auto-switches to the destination layer
        // (so the user sees the result in context). The selection is intentionally
        // left intact so the user can verify visually and chain another action.

        internal void OnMoveToLayerClicked(TilemapLayerSetup.TilemapLayer destLayer)
        {
            if (_state.SelectedCells.Count == 0) { _ui?.SetStatus("Nothing selected"); return; }
            if (destLayer == _state.CurrentLayer)
            {
                _ui?.SetStatus($"Already on layer {destLayer}");
                return;
            }

            var srcTilemap = GetCurrentTilemap();
            var dstTilemap = GetTilemapForLayer(destLayer);
            if (srcTilemap == null || dstTilemap == null)
            {
                _ui?.SetStatus("Tilemap unavailable");
                return;
            }

            // The Collision tilemap is co-mutated within the same batch so a single
            // Ctrl+Z reverses tile move + collider erase atomically. Per the plan's
            // user-confirmed decision, Move-To-Layer DELETES the source-cell collider
            // (and its tag) when one is present — the visual tile has logically moved
            // away, so the obstacle should follow. The Undo restores the collider tile.
            var collisionTm = GetTilemapForLayer(TilemapLayerSetup.TilemapLayer.Collision);

            _undo.StartStroke(srcTilemap);
            var edits = new List<TileEdit>();
            var metadataEdits = new List<MetadataEdit>();
            int moved = 0;
            int collidersErased = 0;

            foreach (var c in _state.SelectedCells)
            {
                if (!CanEditCell(c)) continue;
                var srcTile = srcTilemap.GetTile(c);
                if (srcTile == null) continue;

                var oldDst = dstTilemap.GetTile(c);

                // Phase A: clear source
                srcTilemap.SetTile(c, null);
                edits.Add(new TileEdit(c, srcTile, null, srcTilemap));

                // Phase B: paint destination (overwrites whatever was there)
                dstTilemap.SetTile(c, srcTile);
                edits.Add(new TileEdit(c, oldDst, srcTile, dstTilemap));

                // Phase C: erase any collision cell sitting on top of the moved tile.
                // Same batch ⇒ same Ctrl+Z.
                if (collisionTm != null)
                {
                    var oldCollider = collisionTm.GetTile(c);
                    if (oldCollider != null)
                    {
                        collisionTm.SetTile(c, null);
                        edits.Add(new TileEdit(c, oldCollider, null, collisionTm));
                        if (_collisionTagMap != null)
                        {
                            string oldTag = _collisionTagMap.GetRaw(c);
                            if (oldTag != null)
                            {
                                metadataEdits.Add(new MetadataEdit(c, oldTag, null, _collisionTagMap));
                                _collisionTagMap.Clear(c);
                            }
                        }
                        collidersErased++;
                    }
                }

                moved++;
            }

            _undo.RecordEdits(edits);
            _undo.RecordMetadataEdits(metadataEdits);
            _undo.EndStroke();
            _persistence?.MarkBatchDirty(edits);
            // Autosave is DEBOUNCED (TileOverlayPersistence.Autosave): the edits above already
                // armed it through MarkBatchDirty, and it flushes off-thread after a quiet
                // period. Forcing a synchronous SaveAllDirty() here measured 6.48 ms on the
                // main thread for a single painted cell. Explicit saves (F8 close, slot
                // switch, Save button) still flush synchronously and wait for this one.

            if (moved == 0)
            {
                _ui?.SetStatus("No tiles to move on the source layer");
                return;
            }

            // Switch the editor to the destination layer so subsequent edits
            // land on the layer the user just populated. Uses the same path as
            // the right-panel layer selector — closes any in-flight stroke
            // (already closed by EndStroke above; idempotent) and refreshes UI.
            OnLayerChanged(destLayer);

            // If the move touched Collision (as source OR destination) OR if Phase C
            // erased any collider cell, the composite collider was rebuilt against stale
            // geometry; OnLayerChanged doesn't know, so explicitly rebake here.
            var collision = GetCollisionTilemap();
            bool touchedCollision = collision != null &&
                (srcTilemap == collision || dstTilemap == collision || collidersErased > 0);
            if (touchedCollision)
                RegenerateCompositeCollider(collision);

            if (collidersErased > 0)
                _ui?.SetStatus($"Moved {moved} cell(s) → {destLayer} (+{collidersErased} collider{(collidersErased == 1 ? "" : "s")} cleared)");
            else
                _ui?.SetStatus($"Moved {moved} cell(s) → {destLayer}");
            _ui?.RefreshClipboardButtons();
        }
    }
}
