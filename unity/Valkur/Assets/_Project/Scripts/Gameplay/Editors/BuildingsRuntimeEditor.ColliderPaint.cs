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
using Valkur.Gameplay.Editors.EditorKit;
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

            bool changed = false;
            for (int dr = brushStart; dr <= brushEnd; dr++)
            {
                for (int dc = brushStart; dc <= brushEnd; dc++)
                {
                    int r = row + dr;
                    int c = col + dc;
                    if (r < 0 || r >= session.WorkingGrid.height || c < 0 || c >= session.WorkingGrid.width)
                        continue;

                    // Solid mode writes "#"; Walk (UI "Erase") writes ".".
                    string next = _collBrushMode == CollBrushMode.Solid ? "#" : ".";

                    if (session.WorkingGrid.collision[r][c] == next) continue;
                    session.WorkingGrid.collision[r][c] = next;
                    changed = true;
                }
            }

            if (!changed) return;

            PersistSessionToStore(session);
            _colliderStroke.Changed = true;
            ApplyGridOverrideToBuilding(_activeBuilding, session.WorkingGrid);

            // Live propagation for Shared (CG) scope: every other building that
            // resolves to the same shared key (template-based) and has not been
            // overridden to CU must reflect the brush stroke immediately, not
            // only when the stroke ends. The cached buildings list keeps this
            // O(N) loop cheap, and CU buildings are skipped so per-instance
            // overrides remain authoritative.
            if (session.Scope == ColliderAuthoringScope.CG)
                PropagateLiveStrokeToSharedTemplates(session);

            Physics2D.SyncTransforms();
            // For CU strokes, the heavier ApplyCollisionTargetsFor is still
            // deferred to EndColliderStroke (single building → cheap there).
            // For CG strokes, propagation above already touched all matching
            // buildings, so we only need the lightweight overlay refresh here.
            RefreshActiveBuildingOverlayCells();
            RefreshCollidersPanel();
        }

        private void PropagateLiveStrokeToSharedTemplates(ActiveColliderGridSession session)
        {
            if (session == null || session.WorkingGrid == null) return;
            string sharedKey = session.ImageKey ?? string.Empty;
            if (string.IsNullOrEmpty(sharedKey)) return;

            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;
                if (ReferenceEquals(b, _activeBuilding)) continue;
                if (string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(ResolveSharedScopeKey(b), sharedKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                ApplyGridOverrideToBuilding(b, session.WorkingGrid);

                if (_collidersVisible)
                {
                    var overlay = b.GetComponent<BuildingColliderDebugOverlay>();
                    if (overlay == null)
                        overlay = b.gameObject.AddComponent<BuildingColliderDebugOverlay>();
                    int filled = ComputeAuthoringCellsInto(b, _authoringCellsScratch);
                    if (filled > 0) overlay.SetAuthoringCells(_authoringCellsScratch);
                    else overlay.ClearAuthoringCells();
                }
            }
        }

        // NOTE: Quick Actions (Fill / Clear / Revert) were removed by user request
        // to keep the colliders authoring UX strictly brush-driven (paint vs. erase).
        // Bulk operations are now achieved with a large brush size on top of LMB-drag.

        private void SaveColliderAuthoring()
        {
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

    }
}
