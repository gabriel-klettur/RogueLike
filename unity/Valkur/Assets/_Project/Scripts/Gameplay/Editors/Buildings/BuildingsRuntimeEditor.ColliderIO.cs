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

        private void WriteColliderStoresToDisk(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            File.WriteAllText(
                Path.Combine(directoryPath, "buildings_collisions_by_image.json"),
                SerializeCollisionStore(_colliderImageStore));
            File.WriteAllText(
                Path.Combine(directoryPath, "buildings_collisions_by_building_instance_id.json"),
                SerializeCollisionStore(_colliderInstanceStore));

            CopyStore(_colliderImageStore, _savedColliderImageStore);
            CopyStore(_colliderInstanceStore, _savedColliderInstanceStore);
        }

        private static string SerializeCollisionStore(Dictionary<string, ColliderGridData> store)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = store.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                sb.Append("  \"").Append(EscapeJson(key)).Append("\": ");
                AppendGridJson(sb, store[key], 2);
                if (i < keys.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string SerializeCollisionStore(Dictionary<int, ColliderGridData> store)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = store.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                int key = keys[i];
                sb.Append("  \"").Append(key).Append("\": ");
                AppendGridJson(sb, store[key], 2);
                if (i < keys.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendGridJson(StringBuilder sb, ColliderGridData grid, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 2);
            string childIndent = indent + "  ";
            string rowIndent = childIndent + "  ";

            sb.AppendLine("{");
            sb.Append(childIndent).Append("\"width\": ").Append(grid?.width ?? 0).AppendLine(",");
            sb.Append(childIndent).Append("\"height\": ").Append(grid?.height ?? 0).AppendLine(",");
            sb.Append(childIndent).Append("\"collision\": [").AppendLine();
            for (int row = 0; row < (grid?.height ?? 0); row++)
            {
                sb.Append(rowIndent).Append("[");
                for (int col = 0; col < grid.width; col++)
                {
                    if (col > 0) sb.Append(", ");
                    string cell = grid.collision[row][col] == "#" ? "#" : ".";
                    sb.Append("\"").Append(cell).Append("\"");
                }
                sb.Append("]");
                if (row < grid.height - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.Append(childIndent).AppendLine("],");
            sb.Append(childIndent).Append("\"grid_ref_size\": [")
                .Append(grid?.gridRefSize.x ?? 0).Append(", ")
                .Append(grid?.gridRefSize.y ?? 0).AppendLine("]");
            sb.Append(indent).Append("}");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private void RemapColliderInstanceStore(int oldId, int newId)
        {
            if (oldId == newId) return;

            if (_colliderInstanceStore.TryGetValue(oldId, out var current))
            {
                _colliderInstanceStore.Remove(oldId);
                _colliderInstanceStore[newId] = current;
            }

            if (_savedColliderInstanceStore.TryGetValue(oldId, out var saved))
            {
                _savedColliderInstanceStore.Remove(oldId);
                _savedColliderInstanceStore[newId] = saved;
            }

            if (_activeColliderSession != null && _activeColliderSession.InstanceId == oldId)
                _activeColliderSession.InstanceId = newId;
        }

        private void PruneColliderInstanceStore(IReadOnlyList<BuildingObject> buildings)
        {
            var validIds = new HashSet<int>(
                buildings
                    .Where(b => b != null && string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.InstanceId));

            foreach (int key in _colliderInstanceStore.Keys.ToList())
            {
                if (!validIds.Contains(key))
                    _colliderInstanceStore.Remove(key);
            }

            foreach (int key in _savedColliderInstanceStore.Keys.ToList())
            {
                if (!validIds.Contains(key))
                    _savedColliderInstanceStore.Remove(key);
            }
        }

        private void RefreshCollisionFor(BuildingObject building)
        {
            if (building == null) return;
            if (_activeBuilding == building)
                _activeColliderSession = null;

            ApplyCollisionStateForBuilding(building);
            Physics2D.SyncTransforms();

            if (_collidersVisible)
                RefreshCollidersOverlay();

            if (_activeBuilding == building)
                RefreshCollidersPanel();
        }

        private int ResolveCollisionLayer()
        {
            var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
            return collisionLoader != null ? collisionLoader.CollisionLayer : 11;
        }
    }
}