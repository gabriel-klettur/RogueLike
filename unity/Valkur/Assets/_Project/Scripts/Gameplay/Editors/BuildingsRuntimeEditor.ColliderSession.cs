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

        private void EnsureColliderDataLoaded()
        {
            if (_colliderDataLoaded) return;

            _colliderImageStore.Clear();
            _savedColliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _savedColliderInstanceStore.Clear();

            LoadCollisionImageStore(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_collisions_by_image.json"), _colliderImageStore);
            LoadCollisionInstanceStore(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_collisions_by_building_instance_id.json"), _colliderInstanceStore);
            LoadInlineInstanceColliders(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_instances.json"), _colliderInstanceStore);

            CopyStore(_colliderImageStore, _savedColliderImageStore);
            CopyStore(_colliderInstanceStore, _savedColliderInstanceStore);
            _colliderDataLoaded = true;
            _activeColliderSession = null;
        }

        private ActiveColliderGridSession EnsureActiveColliderSession()
        {
            if (_activeBuilding == null || _activeBuilding.Template == null) return null;
            EnsureColliderDataLoaded();

            Vector2Int effectiveSize = GetEffectivePixelSize(_activeBuilding);
            string imageKey = NormalizeAssetPath(_activeBuilding.Template.sourceImagePath);
            ColliderAuthoringScope scope = string.Equals(
                _activeBuilding.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase)
                ? ColliderAuthoringScope.CU
                : ColliderAuthoringScope.CG;

            if (_activeColliderSession != null &&
                _activeColliderSession.BuildingId == _activeBuilding.GetInstanceID() &&
                _activeColliderSession.InstanceId == _activeBuilding.InstanceId &&
                _activeColliderSession.Scope == scope &&
                string.Equals(_activeColliderSession.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase) &&
                _activeColliderSession.EffectivePixelSize == effectiveSize)
            {
                return _activeColliderSession;
            }

            _activeColliderSession = new ActiveColliderGridSession
            {
                BuildingId = _activeBuilding.GetInstanceID(),
                InstanceId = _activeBuilding.InstanceId,
                ImageKey = imageKey,
                Scope = scope,
                EffectivePixelSize = effectiveSize,
                WorkingGrid = ResolveWorkingGridFor(_activeBuilding, scope, imageKey, _activeBuilding.InstanceId, effectiveSize)
            };
            return _activeColliderSession;
        }

        private ColliderGridData ResolveWorkingGridFor(
            BuildingObject building,
            ColliderAuthoringScope scope,
            string imageKey,
            int instanceId,
            Vector2Int effectiveSize)
        {
            if (scope == ColliderAuthoringScope.CU &&
                _colliderInstanceStore.TryGetValue(instanceId, out var instanceGrid))
            {
                return ResampleGrid(instanceGrid, effectiveSize.x, effectiveSize.y);
            }

            if (!string.IsNullOrEmpty(imageKey) &&
                _colliderImageStore.TryGetValue(imageKey, out var sharedGrid))
            {
                return ResampleGrid(sharedGrid, effectiveSize.x, effectiveSize.y);
            }

            return CreateDefaultFootprintGrid(building, effectiveSize);
        }

        private ColliderGridData CreateFallbackGridFor(BuildingObject building, ActiveColliderGridSession session)
        {
            if (session == null) return null;
            if (session.Scope == ColliderAuthoringScope.CU &&
                !string.IsNullOrEmpty(session.ImageKey) &&
                _colliderImageStore.TryGetValue(session.ImageKey, out var sharedGrid))
            {
                return ResampleGrid(sharedGrid, session.EffectivePixelSize.x, session.EffectivePixelSize.y);
            }

            return CreateDefaultFootprintGrid(building, session.EffectivePixelSize);
        }

        private static Vector2Int GetEffectivePixelSize(BuildingObject building)
        {
            if (building == null || building.Template == null) return Vector2Int.zero;
            int effW = (building.ScaleOverride.x > 0) ? building.ScaleOverride.x : building.Template.originalScale.x;
            int effH = (building.ScaleOverride.y > 0) ? building.ScaleOverride.y : building.Template.originalScale.y;
            return new Vector2Int(effW, effH);
        }

        private static ColliderGridData CreateDefaultFootprintGrid(BuildingObject building, Vector2Int effectiveSize)
        {
            int cols = Mathf.Max(1, Mathf.CeilToInt(effectiveSize.x / 32f));
            int rows = Mathf.Max(1, Mathf.CeilToInt(effectiveSize.y / 32f));
            var grid = CreateEmptyGrid(cols, rows, effectiveSize);
            if (building == null || building.Template == null || !building.Template.solid)
                return grid;

            // Only mark footprint rows as solid, matching BuildingObject.Apply() which
            // sizes the root BoxCollider2D to the footprint (below the split line) only.
            // Row 0 = top of building (canopy), Row rows-1 = bottom (footprint base).
            // footprintStartRow = first grid row (counting from top=0) that is inside
            // the footprint: footprintStartRow = ceil(rows * splitRatio).
            float splitRatio = (building.SplitRatioOverride >= 0f)
                ? building.SplitRatioOverride
                : (building.Template.splitRatio);
            int footprintStartRow = Mathf.Clamp(Mathf.CeilToInt(rows * splitRatio), 0, rows);
            for (int row = footprintStartRow; row < rows; row++)
                for (int col = 0; col < cols; col++)
                    grid.collision[row][col] = "#";

            return grid;
        }

        private static ColliderGridData CreateEmptyGrid(int cols, int rows, Vector2Int effectiveSize)
        {
            var collision = new string[rows][];
            for (int row = 0; row < rows; row++)
            {
                collision[row] = new string[cols];
                for (int col = 0; col < cols; col++)
                    collision[row][col] = ".";
            }

            return new ColliderGridData
            {
                width = cols,
                height = rows,
                collision = collision,
                gridRefSize = effectiveSize
            };
        }

        private static ColliderGridData CloneGrid(ColliderGridData source)
        {
            if (source == null) return null;

            var clone = new ColliderGridData
            {
                width = source.width,
                height = source.height,
                gridRefSize = source.gridRefSize,
                collision = new string[source.height][]
            };

            for (int row = 0; row < source.height; row++)
            {
                clone.collision[row] = new string[source.width];
                if (source.collision == null || row >= source.collision.Length || source.collision[row] == null)
                {
                    for (int col = 0; col < source.width; col++)
                        clone.collision[row][col] = ".";
                    continue;
                }

                for (int col = 0; col < source.width; col++)
                {
                    clone.collision[row][col] = col < source.collision[row].Length
                        ? (source.collision[row][col] ?? ".")
                        : ".";
                }
            }

            return clone;
        }

        private static void CopyStore(Dictionary<string, ColliderGridData> source, Dictionary<string, ColliderGridData> destination)
        {
            destination.Clear();
            foreach (var kvp in source)
                destination[kvp.Key] = CloneGrid(kvp.Value);
        }

        private static void CopyStore(Dictionary<int, ColliderGridData> source, Dictionary<int, ColliderGridData> destination)
        {
            destination.Clear();
            foreach (var kvp in source)
                destination[kvp.Key] = CloneGrid(kvp.Value);
        }

        private static int CountSolidCells(ColliderGridData grid)
        {
            if (grid == null || grid.collision == null) return 0;
            int count = 0;
            for (int row = 0; row < grid.collision.Length; row++)
            {
                if (grid.collision[row] == null) continue;
                for (int col = 0; col < grid.collision[row].Length; col++)
                {
                    if (grid.collision[row][col] == "#")
                        count++;
                }
            }
            return count;
        }

        private static bool GridHasSolidCells(ColliderGridData grid) => CountSolidCells(grid) > 0;

        private static bool GridEquals(ColliderGridData a, ColliderGridData b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.width != b.width || a.height != b.height || a.gridRefSize != b.gridRefSize) return false;

            for (int row = 0; row < a.height; row++)
            {
                for (int col = 0; col < a.width; col++)
                {
                    string av = (a.collision != null && row < a.collision.Length && a.collision[row] != null && col < a.collision[row].Length)
                        ? (a.collision[row][col] ?? ".")
                        : ".";
                    string bv = (b.collision != null && row < b.collision.Length && b.collision[row] != null && col < b.collision[row].Length)
                        ? (b.collision[row][col] ?? ".")
                        : ".";
                    if (!string.Equals(av, bv, StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        private static string NormalizeAssetPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\\", "/");
        }

    }
}