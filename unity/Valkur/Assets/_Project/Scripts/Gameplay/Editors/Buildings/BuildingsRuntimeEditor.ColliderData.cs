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

        private BuildingObject[] _buildingsCache;
        private bool             _buildingsCacheValid;

        internal void InvalidateBuildingCache()
        {
            _buildingsCacheValid = false;
        }

        private BuildingObject[] GetCachedBuildings()
        {
            if (!_buildingsCacheValid || _buildingsCache == null)
            {
                _buildingsCache = FindObjectsOfType<BuildingObject>();
                _buildingsCacheValid = true;
            }
            return _buildingsCache;
        }

        /// <summary>
        /// Per-frame fast path: refresh only the ACTIVE building's overlay
        /// cells. The other buildings are static while the editor is open, so
        /// they keep whatever cells the last full RefreshCollidersOverlay()
        /// pushed. This is the difference between 20 fps and 120+ fps when
        /// Show Colliders is on with many buildings in the scene.
        /// </summary>
        private void RefreshActiveBuildingOverlayCells()
        {
            if (!_collidersVisible || _activeBuilding == null) return;
            var overlay = _activeBuilding.GetComponent<BuildingColliderDebugOverlay>();
            if (overlay == null) return;
            int filled = ComputeAuthoringCellsInto(_activeBuilding, _authoringCellsScratch);
            if (filled > 0)
                overlay.SetAuthoringCells(_authoringCellsScratch);
            else
                overlay.ClearAuthoringCells();
        }

        private int RefreshCollidersOverlay()
        {
            // Compute authoring cells (the editor's working grid in world space)
            // for EVERY building in the scene — not only the currently active
            // one. This guarantees that when the user toggles "Show Colliders"
            // ON, every building's authored collision rectangles light up at
            // exactly the position where the BoxCollider2D children sit. For
            // buildings with no authored data (no editor-stored grid AND no
            // JSON grid) the overlay falls back to enumerating its own
            // BoxCollider2D children (root footprint, etc.) so the user always
            // sees SOMETHING when a building has any physical collider at all.
            //
            // Heavy path — invoked only on toggle, SetActiveBuilding, brush
            // stroke end, undo/redo, and other structural changes. Per-frame
            // updates use the lighter RefreshActiveBuildingOverlayCells.
            int total = 0;
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                var overlay = b.GetComponent<BuildingColliderDebugOverlay>();
                if (overlay == null)
                    overlay = b.gameObject.AddComponent<BuildingColliderDebugOverlay>();

                if (_collidersVisible)
                {
                    int filled = ComputeAuthoringCellsInto(b, _authoringCellsScratch);
                    if (filled > 0)
                        overlay.SetAuthoringCells(_authoringCellsScratch);
                    else
                        overlay.ClearAuthoringCells();
                }
                else
                {
                    overlay.ClearAuthoringCells();
                }

                overlay.SetVisible(_collidersVisible);
                if (_collidersVisible)
                    total += overlay.CurrentVisualCount;
            }

            return total;
        }

        /// <summary>
        /// Build the world-space cell rects for ANY building's overlay using
        /// the SAME rect/grid math <see cref="HandleColliderPaint"/> uses to
        /// map mouse clicks AND the same math <see cref="EnsureCollTile"/>
        /// uses to place physical BoxCollider2D children. Resolution order
        /// matches <see cref="ApplyCollisionStateForBuilding"/>:
        ///   1. Editor-stored CU grid (per instance), if scope = CU.
        ///   2. Editor-stored CG grid (per image).
        ///   3. JSON grid loaded by <see cref="BuildingCollisionLoader"/>.
        /// Returns null/empty when no authored data exists; callers fall back
        /// to BoxCollider2D enumeration so root-collider buildings still show.
        /// </summary>
        private List<Rect> TryComputeAuthoringCellsFor(BuildingObject building)
        {
            // Allocating overload kept for back-compat with any external caller.
            // Internal hot paths use ComputeAuthoringCellsInto with a shared
            // scratch buffer to avoid per-frame allocations.
            var cells = new List<Rect>(64);
            int filled = ComputeAuthoringCellsInto(building, cells);
            return filled > 0 ? cells : null;
        }

        /// <summary>
        /// Allocation-free variant: fills <paramref name="cells"/> with the
        /// world-space rects of every solid ("#") cell in <paramref name="building"/>'s
        /// authoring grid and returns the count. The list is cleared first so
        /// callers can reuse a single shared buffer across frames/buildings.
        /// </summary>
        private int ComputeAuthoringCellsInto(BuildingObject building, List<Rect> cells)
        {
            cells.Clear();
            if (building == null || building.Template == null) return 0;
            if (!building.TryGetWorldRect(out var rect) || rect.width <= 0f || rect.height <= 0f) return 0;

            ColliderGridData grid = ResolveStoredGridForOverlay(building);
            if (grid == null || grid.collision == null || grid.height <= 0 || grid.width <= 0) return 0;

            int rows = grid.height;
            int cols = grid.width;
            for (int row = 0; row < rows; row++)
            {
                var rowArr = grid.collision[row];
                if (rowArr == null) continue;
                for (int col = 0; col < cols && col < rowArr.Length; col++)
                {
                    if (rowArr[col] != "#") continue;
                    if (building.TryGetWorldCellRect(row, col, rows, cols, out var cell))
                        cells.Add(cell);
                }
            }
            return cells.Count;
        }

        /// <summary>
        /// Resolve the collision grid the overlay should mirror for this
        /// building. For the active building we prefer the in-progress
        /// WorkingGrid (un-saved edits visible immediately); for the others
        /// we hit the editor stores and finally the runtime JSON loader so
        /// every building reflects its true authored state.
        /// </summary>
        private ColliderGridData ResolveStoredGridForOverlay(BuildingObject building)
        {
            EnsureColliderDataLoaded();

            if (building == _activeBuilding)
            {
                var session = EnsureActiveColliderSession();
                if (session != null && session.WorkingGrid != null)
                    return session.WorkingGrid;
            }

            // Logical grid: NOT resampled by per-instance pixel size. Both CU and
            // CG return the stored topology as-is so all sharing buildings render
            // an identical pattern (cells map proportionally via TryGetWorldCellRect).
            if (string.Equals(building.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase) &&
                _colliderInstanceStore.TryGetValue(building.InstanceId, out var instanceGrid))
            {
                return CloneGrid(instanceGrid);
            }

            string imageKey = ResolveSharedScopeKey(building);
            if (TryGetSharedGrid(building, imageKey, out var imageGrid))
            {
                return CloneGrid(imageGrid);
            }

            // Note: the editor stores (_colliderImageStore / _colliderInstanceStore)
            // are populated from BOTH the live editor session AND the JSON files
            // loaded by EnsureColliderDataLoaded → so checking them is enough,
            // there is no need to also poll BuildingCollisionLoader here.
            return null;
        }

        /// <summary>
        /// Backwards-compatible wrapper kept for any external call sites; the
        /// overlay refresh now uses <see cref="TryComputeAuthoringCellsFor"/>
        /// directly so EVERY building (not just the active one) lights up.
        /// </summary>
        private List<Rect> TryComputeActiveAuthoringCells(out BuildingColliderDebugOverlay overlay)
        {
            overlay = null;
            if (_activeBuilding == null) return null;
            overlay = _activeBuilding.GetComponent<BuildingColliderDebugOverlay>();
            if (overlay == null)
                overlay = _activeBuilding.gameObject.AddComponent<BuildingColliderDebugOverlay>();
            return TryComputeAuthoringCellsFor(_activeBuilding);
        }

        private void BeginColliderStroke()
        {
            if (_colliderStroke.Active) return;
            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null) return;

            _colliderStroke.Active = true;
            _colliderStroke.Scope = session.Scope;
            _colliderStroke.ImageKey = session.ImageKey;
            _colliderStroke.InstanceId = session.InstanceId;
            _colliderStroke.Before = CloneGrid(session.WorkingGrid);
            _colliderStroke.Changed = false;
        }

        private void EndColliderStroke()
        {
            if (!_colliderStroke.Active) return;

            var strokeScope = _colliderStroke.Scope;
            string strokeImageKey = _colliderStroke.ImageKey;
            int strokeInstanceId = _colliderStroke.InstanceId;
            var before = CloneGrid(_colliderStroke.Before);
            var after = CloneGrid(GetStoredGrid(strokeScope, strokeImageKey, strokeInstanceId));
            bool changed = _colliderStroke.Changed && !GridEquals(before, after);

            _colliderStroke.Active = false;
            _colliderStroke.Before = null;
            _colliderStroke.Changed = false;

            if (!changed || after == null) return;

            _undo.Do("Paint colliders",
                () => ApplyGridSnapshot(strokeScope, strokeImageKey, strokeInstanceId, after),
                () => ApplyGridSnapshot(strokeScope, strokeImageKey, strokeInstanceId, before));
            // Auto-save only in play mode. EditMode reflection tests exercise this
            // method on temporary scene objects and must not rewrite project data.
            if (Application.isPlaying)
                SaveColliderAuthoring();
        }
    }
}
