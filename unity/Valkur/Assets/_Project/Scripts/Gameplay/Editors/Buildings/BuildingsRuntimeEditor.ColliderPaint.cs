using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void HandleColliderPaint(Vector3 worldPos)
        {
            if (_collBrushMode == CollBrushMode.Off) return;
            if (_activeBuilding == null || _activeBuilding.Template == null) return;
            if (!_activeBuilding.TryGetWorldRect(out var rect) || !rect.Contains(worldPos)) return;

            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null || session.WorkingGrid.width <= 0 || session.WorkingGrid.height <= 0)
                return;

            float u = Mathf.Clamp01((worldPos.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((worldPos.y - rect.yMin) / rect.height);
            int col = Mathf.Clamp(Mathf.FloorToInt(u * session.WorkingGrid.width), 0, session.WorkingGrid.width - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((1f - v) * session.WorkingGrid.height), 0, session.WorkingGrid.height - 1);

            int brushStart = -(_collBrushSize / 2);
            int brushEnd = brushStart + _collBrushSize - 1;
            // CollBrushMode.Erase is an internal enum value that is no longer
            // reachable from the redesigned UX (the UI "Erase" button maps to
            // CollBrushMode.Walk). Kept in the enum for undo-snapshot compatibility.

            // Solid mode writes "#"; Walk (UI "Erase") writes ".".
            bool solidNow = _collBrushMode == CollBrushMode.Solid;
            string next = solidNow ? "#" : ".";

            bool changed = false;
            _collPaintChangedCellsScratch.Clear();
            for (int dr = brushStart; dr <= brushEnd; dr++)
            {
                for (int dc = brushStart; dc <= brushEnd; dc++)
                {
                    int r = row + dr;
                    int c = col + dc;
                    if (r < 0 || r >= session.WorkingGrid.height || c < 0 || c >= session.WorkingGrid.width)
                        continue;

                    if (session.WorkingGrid.collision[r][c] == next) continue;
                    session.WorkingGrid.collision[r][c] = next;
                    changed = true;
                    var cell = new Vector2Int(r, c);                 // x = row, y = col
                    _collPaintChangedCellsScratch.Add(cell);
                    // Remembered for the whole stroke so EndColliderStroke can hand the
                    // siblings a delta instead of the whole grid.
                    _colliderStroke.ChangedCells[cell] = solidNow;
                }
            }

            if (!changed) return;

            PersistSessionToStore(session);
            _colliderStroke.Changed = true;

            // Incremental apply: touch only the cells that changed in THIS
            // brush stamp instead of tearing down and rebuilding every tile
            // on the building. ApplyGridOverrideToBuilding is O(total solid
            // cells) — fine for the single call EndColliderStroke makes once
            // per stroke, but ruinous once per mouse-move sample on a
            // mostly-solid footprint, which is exactly the Erase workflow.
            int gridRows = session.WorkingGrid.height;
            int gridCols = session.WorkingGrid.width;
            for (int i = 0; i < _collPaintChangedCellsScratch.Count; i++)
            {
                var cell = _collPaintChangedCellsScratch[i];
                ApplyGridCellToBuilding(_activeBuilding, cell.x, cell.y, gridRows, gridCols, solidNow);
            }
            // Per-cell colliders supersede the whole-sprite root collider once
            // any grid is authored — mirror ApplyGridOverrideToBuilding's
            // invariant without re-walking the grid to decide it.
            var mainCollider = _activeBuilding.GetComponent<BoxCollider2D>();
            if (mainCollider != null) mainCollider.enabled = false;

            // CG (shared) scope propagates to its siblings when the stroke ENDS, not on
            // every mouse-move sample, and it goes as a DELTA.
            //
            // Live propagation was the dominant cost in this editor. Measured on the shipped
            // world (302 buildings, 249 of them CG) painting a tree fourteen instances share:
            // every newly-solid cell materialised a collider tile AND, with Show Colliders on,
            // an overlay visual, on the active building AND on each sibling, synchronously.
            //   brush 1 -> 32.3 ms/sample, brush 4 -> 123.0 ms, brush 8 -> 149.2 ms
            // i.e. 6-30 fps while painting. None of it was in the grid maths: cloning the grid
            // measured 0.007 ms, the physics sync 0.001 ms, the panel refresh 0.005 ms.
            //
            // The siblings are not skipped, only deferred: EndColliderStroke hands them the
            // cells this stroke changed. What the author drags over is the building under the
            // cursor, and that still updates every sample.
            //
            // Physics2D.SyncTransforms moved with it, to once per stroke. Nothing queries
            // physics mid-stroke: the brush hit-tests the building's own rect, not colliders.
            RefreshActiveBuildingOverlayCells();
            RefreshCollidersPanel();
        }

        // ── Debounced authoring save ──────────────────────────────────────────────

        /// <summary>
        /// How long the editor waits, after the last stroke, before writing the authoring
        /// files. Short enough that a crash costs one stroke, long enough that a burst of
        /// brush strokes writes once.
        /// </summary>
        private const float COLLIDER_SAVE_DEBOUNCE_SECONDS = 1.0f;

        private Coroutine _pendingColliderSave;
        private bool      _colliderSaveQueued;

        /// <summary>
        /// Ask for the authoring files to be written soon.
        ///
        /// <para>Every stroke used to write them immediately, and
        /// <see cref="SaveInstancesToJson"/> serialises ALL 302 building instances:
        /// measured at 51 ms, on every mouse release. Painting a wall is a dozen short
        /// strokes, so that was half a second of stalls the author reads as the editor
        /// being slow at erasing.</para>
        ///
        /// <para>Deferring only moves WHEN, never WHETHER: the pending write is flushed by
        /// <see cref="FlushPendingColliderSave"/> from every exit — deactivating the editor,
        /// switching map slot, the explicit Save button, and OnDestroy, which is what Play
        /// Mode stopping runs.</para>
        /// </summary>
        private void RequestColliderSave()
        {
            _colliderSaveQueued = true;

            // No coroutine outside Play Mode (and none on a disabled component): write now.
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                FlushPendingColliderSave();
                return;
            }

            if (_pendingColliderSave != null) StopCoroutine(_pendingColliderSave);
            _pendingColliderSave = StartCoroutine(ColliderSaveAfterDelay());
        }

        private System.Collections.IEnumerator ColliderSaveAfterDelay()
        {
            // Realtime: the editor is usable while the game is paused.
            yield return new WaitForSecondsRealtime(COLLIDER_SAVE_DEBOUNCE_SECONDS);
            _pendingColliderSave = null;
            FlushPendingColliderSave();
        }

        /// <summary>Writes a queued save now. No-op when nothing is queued. Idempotent.</summary>
        internal void FlushPendingColliderSave()
        {
            if (_pendingColliderSave != null)
            {
                StopCoroutine(_pendingColliderSave);
                _pendingColliderSave = null;
            }
            if (!_colliderSaveQueued) return;
            _colliderSaveQueued = false;
            SaveColliderAuthoring();
        }

        // NOTE: Quick Actions (Fill / Clear / Revert) were removed by user request
        // to keep the colliders authoring UX strictly brush-driven (paint vs. erase).
        // Bulk operations are now achieved with a large brush size on top of LMB-drag.

        private void SaveColliderAuthoring()
        {
            // An explicit save satisfies whatever the debounce was still holding.
            _colliderSaveQueued = false;
            SaveInstancesToJson();
        }

        /// <summary>
        /// Wipes all existing collision authoring data and assigns an all-walkable
        /// (all "." cells) CU-scope grid to every building so the user can repaint
        /// from scratch. All-walkable CU grids are preserved across sessions because
        /// the per-instance JSON loaders no longer apply the GridHasSolidCells filter.
        /// </summary>
        private void ResetAllCollidersToWalkable()
        {
            EnsureColliderDataLoaded();

            // Clear both in-memory stores and the active authoring session.
            _colliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _activeColliderSession = null;

            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;

                // Force per-instance (CU) scope so every building gets its own
                // all-walkable grid regardless of previous scope setting.
                b.ColliderScopeOverride = "CU";

                var sz       = GetEffectivePixelSize(b);
                int cols     = Mathf.Max(1, Mathf.CeilToInt(sz.x / 32f));
                int rows     = Mathf.Max(1, Mathf.CeilToInt(sz.y / 32f));
                var walkable = CreateEmptyGrid(cols, rows, sz); // all "." cells

                _colliderInstanceStore[b.InstanceId] = walkable;
                ApplyGridOverrideToBuilding(b, walkable);
            }

            InvalidateBuildingCache();
            SaveColliderAuthoring();

            if (_collidersVisible)
            {
                Physics2D.SyncTransforms();
                RefreshCollidersOverlay();
            }
            RefreshCollidersPanel();
            Toast("All colliders reset to walkable. Paint solid cells from scratch.");
            Debug.Log($"[BuildingsEditor] ResetAllCollidersToWalkable — {all.Length} buildings cleared.");
        }

        private void ResetColliderAuthoringState()
        {
            _colliderDataLoaded = false;
            _colliderImageStore.Clear();
            _savedColliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _savedColliderInstanceStore.Clear();
            _activeColliderSession = null;
            _colliderStroke.Active = false;
            _colliderStroke.Before = null;
            _colliderStroke.Changed = false;
        }

        /// <summary>
        /// Called by <see cref="Valkur.Gameplay.MapEditor.MapEditorManager"/>
        /// whenever the user switches map slots (BeginNewMap / LoadMapSlot).
        /// We must drop the cached collider stores: they were loaded from the
        /// OUTGOING slot's files and would otherwise be written back over the
        /// incoming slot on the next save. Dropping any in-flight stroke /
        /// session prevents a paint that started in slot A from persisting
        /// into slot B.
        ///
        /// Also flushes any pending unsaved instance edits so they reach the
        /// outgoing slot's file BEFORE the slot pointer flips. The order is
        /// critical: persist → reset cache → next load resolves the new slot.
        /// </summary>
        public void NotifyActiveMapSlotChanged()
        {
            // Persist still-pending edits to the OUTGOING slot. Skip when the
            // editor has never been activated (no UI, no edits to flush).
            // The debounced collider save goes first, or a stroke painted seconds before
            // the slot flipped would be written into the INCOMING slot's file.
            FlushPendingColliderSave();
            if (_uiBuilt && _hasUnsavedInstanceChanges)
                PersistDirtyInstanceChanges("Active map slot changed");

            ResetColliderAuthoringState();
            _activeBuilding = null;
            _hoveredBuilding = null;
            _hoverStack.Clear();
            InvalidateBuildingCache();
        }

    }
}
