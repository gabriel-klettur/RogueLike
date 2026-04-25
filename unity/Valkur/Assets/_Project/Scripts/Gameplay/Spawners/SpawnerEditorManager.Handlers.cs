using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    public partial class SpawnerEditorManager : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor
    {

        private void HandlePlaceMode(Vector3 worldPos)
        {
            if (_clickAction.WasPerformedThisFrame() && _selectedTemplate != null)
            {
                PlaceSpawner(_selectedTemplate, worldPos);
                // Stay in place mode for rapid placement
            }
        }

        private void HandleSelectMode(Vector3 worldPos)
        {
            if (_clickAction.WasPerformedThisFrame())
            {
                var hit = FindSpawnerAtPosition(worldPos);
                SelectInstance(hit);
            }

            if (_rightClickAction.WasPerformedThisFrame() && _selectedInstance != null)
            {
                _dragging = true;
                _dragOffset = _selectedInstance.transform.position - worldPos;
            }
        }

        private void HandleDeleteMode(Vector3 worldPos)
        {
            if (_clickAction.WasPerformedThisFrame())
            {
                var hit = FindSpawnerAtPosition(worldPos);
                if (hit != null)
                {
                    Debug.Log($"[SpawnerEditor] Deleted spawner: {hit.InstanceId}");
                    Destroy(hit.gameObject);
                    if (_selectedInstance == hit) _selectedInstance = null;
                    _mode = EditorMode.Select;
                }
            }
        }

        // ------------------------------------------------------------------
        // Spawner Operations
        // ------------------------------------------------------------------

        private void PlaceSpawner(SpawnerTemplateData template, Vector3 worldPos)
        {
            // Auto-generate instance ID
            string zone = ResolveZone(worldPos);
            int col = Mathf.RoundToInt(worldPos.x);
            int row = Mathf.RoundToInt(worldPos.y);
            string instanceId = $"{template.templateId}_{zone}_{col}_{row}";

            var go = new GameObject($"Spawner_{instanceId}");
            go.transform.position = worldPos;

            var si = go.AddComponent<SpawnerInstance>();
            var spawner = FindObjectOfType<MonsterSpawner>();
            si.Initialize(template, instanceId, zone, spawner);

            SelectInstance(si);
            Debug.Log($"[SpawnerEditor] Placed spawner '{instanceId}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        private SpawnerInstance FindSpawnerAtPosition(Vector3 worldPos)
        {
            float bestDist = 2f; // 2 world unit selection radius
            SpawnerInstance best = null;
            foreach (var si in FindObjectsOfType<SpawnerInstance>())
            {
                float dist = Vector2.Distance(si.transform.position, worldPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = si;
                }
            }
            return best;
        }

        private void SelectInstance(SpawnerInstance instance)
        {
            _selectedInstance = instance;
            UpdatePropertiesPanel();
        }

        private string ResolveZone(Vector3 worldPos)
        {
            var zm = FindObjectOfType<World.ZoneManager>();
            if (zm == null) return "Unknown";
            // Simple: check which zone contains this world position
            // For now, default to Lobby
            return "Lobby";
        }

        private void CancelCurrentMode()
        {
            if (_mode != EditorMode.Select)
            {
                _mode = EditorMode.Select;
                _selectedTemplate = null;
            }
            else
            {
                SetVisible(false);
            }
        }

        // ------------------------------------------------------------------
        // Save/Export
        // ------------------------------------------------------------------

        public void SaveInstancesToJson()
        {
            var allInstances = FindObjectsOfType<SpawnerInstance>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < allInstances.Length; i++)
            {
                var si = allInstances[i];
                Vector3 pos = si.transform.position;
                int col = Mathf.RoundToInt(pos.x);
                int row = Mathf.RoundToInt(pos.y);

                sb.Append("  {");
                sb.Append($"\"template_id\": \"{si.Template?.templateId ?? "?"}\", ");
                sb.Append($"\"zone\": \"{si.Zone}\", ");
                sb.Append($"\"tile\": [{col}, {row}], ");
                sb.Append($"\"id\": \"{si.InstanceId}\"");
                sb.Append("}");
                if (i < allInstances.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("]");

            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "Spawners", "spawners_instances.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log($"[SpawnerEditor] Saved {allInstances.Length} instances to {path}");
        }

        private enum EditorMode
        {
            Select,
            Place,
            Delete
        }
    }
}