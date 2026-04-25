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
            string imageKey = ResolveSharedScopeKey(_activeBuilding);
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

        // The collider grid is a LOGICAL N×M topology that belongs to the
        // shared image (CG) or the building instance (CU). It is NEVER resampled
        // by per-instance pixel size: all buildings using the same image share
        // the exact same grid, and each one maps the cells proportionally to its
        // own world rect via BuildingObject.TryGetWorldCellRect(). This keeps
        // the painted pattern visually consistent across instances of any size.
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
                return CloneGrid(instanceGrid);
            }

            if (TryGetSharedGrid(building, imageKey, out var sharedGrid))
            {
                return CloneGrid(sharedGrid);
            }

            return CreateDefaultFootprintGrid(building);
        }

        private ColliderGridData CreateFallbackGridFor(BuildingObject building, ActiveColliderGridSession session)
        {
            if (session == null) return null;
            if (session.Scope == ColliderAuthoringScope.CU &&
                TryGetSharedGrid(building, session.ImageKey, out var sharedGrid))
            {
                return CloneGrid(sharedGrid);
            }

            return CreateDefaultFootprintGrid(building);
        }

        private static Vector2Int GetEffectivePixelSize(BuildingObject building)
        {
            if (building == null || building.Template == null) return Vector2Int.zero;
            int effW = (building.ScaleOverride.x > 0) ? building.ScaleOverride.x : building.Template.originalScale.x;
            int effH = (building.ScaleOverride.y > 0) ? building.ScaleOverride.y : building.Template.originalScale.y;
            return new Vector2Int(effW, effH);
        }

        // Default grid resolution is derived from the TEMPLATE's natural size
        // (originalScale), not the per-instance effective size, so all instances
        // of the same template (regardless of scale override) get the same
        // default grid — a pre-condition for shared CG colliders to work.
        private static ColliderGridData CreateDefaultFootprintGrid(BuildingObject building)
        {
            Vector2Int natural = (building != null && building.Template != null)
                ? building.Template.originalScale
                : Vector2Int.zero;
            if (natural.x <= 0) natural.x = 32;
            if (natural.y <= 0) natural.y = 32;

            int cols = Mathf.Max(1, Mathf.CeilToInt(natural.x / 32f));
            int rows = Mathf.Max(1, Mathf.CeilToInt(natural.y / 32f));
            var grid = CreateEmptyGrid(cols, rows, natural);
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

        // Primary shared-scope key: all buildings whose sprite comes from the same
        // source image file (sourceImagePath) should share a single CG collider grid.
        // Previously this returned "template:{templateId}", which prevented buildings
        // with different templateIds (but the same image) from sharing — fixed here.
        private static string ResolveSharedScopeKey(BuildingObject building)
        {
            if (building == null || building.Template == null) return string.Empty;
            return NormalizeAssetPath(building.Template.sourceImagePath ?? string.Empty);
        }

        // Backward-compat fallback: data saved before this fix used templateId as key.
        private static string ResolveLegacyImageScopeKey(BuildingObject building)
        {
            if (building == null || building.Template == null) return string.Empty;
            return $"template:{building.Template.templateId}";
        }

        private bool TryGetSharedGrid(BuildingObject building, string sharedKey, out ColliderGridData sharedGrid)
        {
            sharedGrid = null;

            if (!string.IsNullOrEmpty(sharedKey) &&
                _colliderImageStore.TryGetValue(sharedKey, out sharedGrid))
            {
                return true;
            }

            // Backward compatibility: older sessions stored CG by source image path.
            string legacyImageKey = ResolveLegacyImageScopeKey(building);
            if (!string.IsNullOrEmpty(legacyImageKey) &&
                !string.Equals(legacyImageKey, sharedKey, StringComparison.OrdinalIgnoreCase) &&
                _colliderImageStore.TryGetValue(legacyImageKey, out sharedGrid))
            {
                return true;
            }

            return false;
        }

    }
}