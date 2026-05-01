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

        private bool IsSessionDirty(ActiveColliderGridSession session)
        {
            if (session == null) return false;
            if (session.Scope == ColliderAuthoringScope.CU)
            {
                _colliderInstanceStore.TryGetValue(session.InstanceId, out var current);
                _savedColliderInstanceStore.TryGetValue(session.InstanceId, out var saved);
                return !GridEquals(current, saved);
            }

            string key = session.ImageKey ?? string.Empty;
            _colliderImageStore.TryGetValue(key, out var currentImage);
            _savedColliderImageStore.TryGetValue(key, out var savedImage);
            return !GridEquals(currentImage, savedImage);
        }

        private void PersistSessionToStore(ActiveColliderGridSession session)
        {
            if (session == null || session.WorkingGrid == null) return;

            var snapshot = CloneGrid(session.WorkingGrid);
            if (session.Scope == ColliderAuthoringScope.CU)
                _colliderInstanceStore[session.InstanceId] = snapshot;
            else if (!string.IsNullOrEmpty(session.ImageKey))
                _colliderImageStore[session.ImageKey] = snapshot;
        }

        private ColliderGridData GetStoredGrid(ColliderAuthoringScope scope, string imageKey, int instanceId)
        {
            if (scope == ColliderAuthoringScope.CU)
            {
                _colliderInstanceStore.TryGetValue(instanceId, out var instanceGrid);
                return instanceGrid;
            }

            _colliderImageStore.TryGetValue(imageKey ?? string.Empty, out var imageGrid);
            return imageGrid;
        }

        private void ApplyGridSnapshot(ColliderAuthoringScope scope, string imageKey, int instanceId, ColliderGridData grid)
        {
            EnsureColliderDataLoaded();

            if (scope == ColliderAuthoringScope.CU)
            {
                if (grid == null) _colliderInstanceStore.Remove(instanceId);
                else _colliderInstanceStore[instanceId] = CloneGrid(grid);
            }
            else
            {
                string key = imageKey ?? string.Empty;
                if (grid == null) _colliderImageStore.Remove(key);
                else _colliderImageStore[key] = CloneGrid(grid);
            }

            _activeColliderSession = null;
            ApplyCollisionTargetsFor(scope, imageKey, instanceId);
            RefreshCollidersPanel();
        }

        private void ApplyCollisionTargetsFor(ColliderAuthoringScope scope, string imageKey, int instanceId)
        {
            // Use the cached snapshot — this is only called from structural-change sites
            // (stroke end, undo/redo, scope change) so the cache is either already valid
            // or correctly invalidated before the call.
            var all = GetCachedBuildings();
            if (scope == ColliderAuthoringScope.CU)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].InstanceId == instanceId)
                    {
                        ApplyCollisionStateForBuilding(all[i]);
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var b = all[i];
                    if (b == null || b.Template == null) continue;
                    if (!string.Equals(ResolveSharedScopeKey(b), imageKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ApplyCollisionStateForBuilding(b);
                }
            }

            if (_collidersVisible)
            {
                Physics2D.SyncTransforms();
                RefreshCollidersOverlay();
            }
        }

        private void ApplyCollisionStateForBuilding(BuildingObject building)
        {
            if (building == null) return;

            if (!TryApplyAuthoredGrid(building))
            {
                var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
                if (collisionLoader != null)
                    collisionLoader.TryApplyGrid(building);
                else
                    ApplyGridOverrideToBuilding(building, null);
            }
        }

        private bool TryApplyAuthoredGrid(BuildingObject building)
        {
            if (building == null || building.Template == null) return false;
            EnsureColliderDataLoaded();

            if (string.Equals(building.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase) &&
                _colliderInstanceStore.TryGetValue(building.InstanceId, out var instanceGrid))
            {
                ApplyGridOverrideToBuilding(building, instanceGrid);
                return true;
            }

            string imageKey = ResolveSharedScopeKey(building);
            if (TryGetSharedGrid(building, imageKey, out var imageGrid))
            {
                ApplyGridOverrideToBuilding(building, imageGrid);
                return true;
            }

            return false;
        }

        private void ApplyGridOverrideToBuilding(BuildingObject building, ColliderGridData grid)
        {
            if (building == null || building.Template == null) return;

            ClearCollisionTiles(building);
            RestoreDefaultColliderState(building);

            if (grid == null) return;

            // The grid is a LOGICAL N×M topology applied as-is. Cell-to-world
            // mapping is proportional via BuildingObject.TryGetWorldCellRect()
            // inside EnsureCollTile, so different-size instances naturally get
            // proportionally-sized cells without any pixel-based resampling.
            for (int row = 0; row < grid.height; row++)
            {
                if (grid.collision == null || row >= grid.collision.Length || grid.collision[row] == null)
                    continue;

                for (int col = 0; col < grid.width; col++)
                {
                    if (col >= grid.collision[row].Length || grid.collision[row][col] != "#")
                        continue;
                    EnsureCollTile(building, row, col, grid.height, grid.width);
                }
            }

            var main = building.GetComponent<BoxCollider2D>();
            if (main != null)
                main.enabled = false;
        }

    }
}