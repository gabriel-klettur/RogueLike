using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// SpawnerEditor — map interaction (Select/Place/Delete + drag) and
    /// JSON persistence of placed instances.
    /// </summary>
    public partial class SpawnerEditorManager
    {
        private const float SELECTION_RADIUS_WORLD = 1.5f;
        private const string STREAMING_SUBFOLDER   = "Spawners";
        private const string INSTANCES_FILENAME    = "spawners_instances.json";

        // ── Map interaction (called every Update while active) ───────────────────

        private void HandleMapInteraction()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            // Don't react to clicks that land on UI panels.
            if (IsPointerOverEditorUI()) return;

            Vector2 screen = MouseInputManager.GetScreenMousePosition();
            Vector3 world  = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;

            switch (_mode)
            {
                case EditorMode.Place:  HandlePlaceMode(world);  break;
                case EditorMode.Select: HandleSelectMode(world); break;
                case EditorMode.Delete: HandleDeleteMode(world); break;
            }

            // Right-mouse drag of the currently selected instance.
            if (_dragging && _selectedInstance != null)
            {
                _selectedInstance.transform.position = world + _dragOffset;
                if (_rightClickAction != null && _rightClickAction.WasReleasedThisFrame())
                {
                    _dragging = false;
                    SetStatus($"Moved '{_selectedInstance.InstanceId}'.");
                    RefreshPropertiesPanel();
                }
            }
        }

        private static bool IsPointerOverEditorUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private void HandlePlaceMode(Vector3 worldPos)
        {
            if (_clickAction != null && _clickAction.WasPerformedThisFrame() && _selectedTemplate != null)
                PlaceSpawner(_selectedTemplate, worldPos);
        }

        private void HandleSelectMode(Vector3 worldPos)
        {
            if (_clickAction != null && _clickAction.WasPerformedThisFrame())
            {
                var hit = FindSpawnerAtPosition(worldPos);
                SelectInstance(hit);
                SetStatus(hit == null ? "Nothing under cursor." : $"Selected '{hit.InstanceId}'.");
            }

            if (_rightClickAction != null && _rightClickAction.WasPerformedThisFrame() && _selectedInstance != null)
            {
                _dragging   = true;
                _dragOffset = _selectedInstance.transform.position - worldPos;
                SetStatus($"Dragging '{_selectedInstance.InstanceId}' (release RMB to drop).");
            }
        }

        private void HandleDeleteMode(Vector3 worldPos)
        {
            if (_clickAction == null || !_clickAction.WasPerformedThisFrame()) return;

            var hit = FindSpawnerAtPosition(worldPos);
            if (hit == null)
            {
                SetStatus("Nothing under cursor.");
                return;
            }

            string id = hit.InstanceId;
            Destroy(hit.gameObject);
            if (_selectedInstance == hit) _selectedInstance = null;
            SetStatus($"Deleted '{id}'.");
            RefreshPropertiesPanel();
        }

        // ── Spawner ops ─────────────────────────────────────────────────────────

        private void PlaceSpawner(SpawnerTemplateData template, Vector3 worldPos)
        {
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
            SetStatus($"Placed '{instanceId}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        private SpawnerInstance FindSpawnerAtPosition(Vector3 worldPos)
        {
            float bestDist  = SELECTION_RADIUS_WORLD;
            SpawnerInstance best = null;
            foreach (var si in FindObjectsOfType<SpawnerInstance>())
            {
                float d = Vector2.Distance(si.transform.position, worldPos);
                if (d < bestDist) { bestDist = d; best = si; }
            }
            return best;
        }

        private void SelectInstance(SpawnerInstance instance)
        {
            _selectedInstance = instance;
            RefreshPropertiesPanel();
        }

        private string ResolveZone(Vector3 worldPos)
        {
            // TODO: route through ZoneManager.GetZoneAt(worldPos) once the zone
            // editor exposes a public lookup. Defaults to Lobby for parity with
            // the original placement helper.
            _ = worldPos;
            return "Lobby";
        }

        private void CancelCurrentMode()
        {
            if (_mode != EditorMode.Select)
            {
                SetMode(EditorMode.Select);
                _selectedTemplate = null;
                SetStatus("Cancelled — back to Select.");
                return;
            }
            Deactivate();
        }

        // ── Persistence (Save) ──────────────────────────────────────────────────

        public void SaveInstancesToJson()
        {
            var all = FindObjectsOfType<SpawnerInstance>();
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < all.Length; i++)
            {
                var si  = all[i];
                var pos = si.transform.position;
                int col = Mathf.RoundToInt(pos.x);
                int row = Mathf.RoundToInt(pos.y);

                sb.Append("  {");
                sb.Append($"\"template_id\": \"{si.Template?.templateId ?? "?"}\", ");
                sb.Append($"\"zone\": \"{si.Zone}\", ");
                sb.Append($"\"tile\": [{col}, {row}], ");
                sb.Append($"\"id\": \"{si.InstanceId}\"");
                sb.Append('}');
                if (i < all.Length - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("]");

            string path = Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILENAME);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            SetStatus($"Saved {all.Length} instance(s).");
            Debug.Log($"[SpawnerEditor] Saved {all.Length} instance(s) → {path}");
        }
    }
}
