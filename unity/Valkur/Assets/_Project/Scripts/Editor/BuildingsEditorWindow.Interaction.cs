using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    public partial class BuildingsEditorWindow
    {

        // â”€â”€ Scene view interaction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_placeMode || _selectedTemplate == null) return;

            Event e = Event.current;

            // Convert mouse position to world space
            Vector2 mousePos = HandleUtility.GUIPointToWorldRay(e.mousePosition).origin;
            _ghostWorldPos = new Vector3(
                Mathf.Round(mousePos.x * 2f) / 2f,
                Mathf.Round(mousePos.y * 2f) / 2f,
                0f);
            _ghostVisible = true;

            // Draw ghost preview (wire box showing the building footprint + full size)
            float origW = _selectedTemplate.originalScale.x;
            float origH = _selectedTemplate.originalScale.y;
            const float PPU = 32f;
            float fullH    = origH / PPU;
            float bottomH  = origH * (1f - _selectedTemplate.splitRatio) / PPU;

            Handles.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            Handles.DrawWireCube(_ghostWorldPos + new Vector3(0f, fullH * 0.5f, 0f),
                new Vector3(origW / PPU, fullH, 0.02f));

            Handles.color = new Color(1f, 0.4f, 0.4f, 0.7f);
            Handles.DrawWireCube(_ghostWorldPos + new Vector3(0f, bottomH * 0.5f, 0f),
                new Vector3(origW / PPU, bottomH, 0.02f));

            string label = $"Template #{_selectedTemplate.templateId}  [Click to place]";
            Handles.Label(_ghostWorldPos + new Vector3(0f, fullH + 0.3f, 0f), label);

            // Place on left-click
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                PlaceBuildingAtWorldPos(_selectedTemplate, _ghostWorldPos);
                e.Use();
            }

            // Cancel place mode with Escape
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _placeMode  = false;
                _ghostVisible = false;
                Repaint();
                e.Use();
            }

            sceneView.Repaint();
        }

        // â”€â”€ Placement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void PlaceBuildingAtWorldPos(BuildingTemplateData template, Vector3 worldPos)
        {
            // Assign a provisional ID (negative = temporary; proper ID assigned on save)
            int newId = GenerateProvisionalId();

            var go = new GameObject($"Building_{newId}_{template.name}");
            go.layer = 11; // World

            // Parent under an existing BuildingLoader's root if present, else root of scene
            var loader = FindObjectOfType<BuildingLoader>();
            if (loader != null)
            {
                var loaderField = typeof(BuildingLoader).GetField(
                    "_buildingsRoot",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var parentTr = loaderField?.GetValue(loader) as Transform;
                if (parentTr != null)
                    go.transform.SetParent(parentTr, worldPositionStays: false);
            }

            go.transform.position = worldPos;
            Undo.RegisterCreatedObjectUndo(go, "Place Building");

            var bObj = go.AddComponent<BuildingObject>();
            bObj.InstanceId = newId;
            bObj.ZoneName   = DetectZone(worldPos);
            bObj.Apply(template, Vector2Int.zero, -1f);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            _selectedBuilding = bObj;
            _sceneBuildings.Add(bObj);
            Selection.activeGameObject = go;

            Repaint();
        }

        private string DetectZone(Vector3 worldPos)
        {
            var zm = FindObjectOfType<ZoneManager>();
            if (zm != null) return zm.DetectZone(worldPos);
            return "Lobby";
        }

        private int GenerateProvisionalId()
        {
            // Use large negative value to avoid colliding with real IDs; will be remapped on save.
            int minId = 0;
            foreach (var b in _sceneBuildings)
                if (b != null) minId = Mathf.Min(minId, b.InstanceId);
            return minId - 1;
        }

        // â”€â”€ Persistence â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SaveInstancesToJson()
        {
            RefreshSceneBuildings();

            string dir  = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string path = Path.Combine(dir, "buildings_instances.json");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("[");

            // Reassign sequential IDs during save
            int nextId = 1;
            for (int i = 0; i < _sceneBuildings.Count; i++)
            {
                var b = _sceneBuildings[i];
                if (b == null || b.Template == null) continue;

                b.InstanceId = nextId++;

                // Convert bottom-center world position back to Python (zone-relative, px, Y-down)
                var zm = FindObjectOfType<ZoneManager>();
                string zone = b.ZoneName;
                int relX = 0, relY = 0;

                if (zm != null && zm.TryGetZone(zone, out var zd))
                {
                    int effW = (b.ScaleOverride.x > 0) ? b.ScaleOverride.x : b.Template.originalScale.x;
                    int effH = (b.ScaleOverride.y > 0) ? b.ScaleOverride.y : b.Template.originalScale.y;
                    const float PPU = 32f;
                    int zH = zm.ZoneHeightTiles;

                    // Inverse of BuildingLoader coordinate formula:
                    // worldX = gridOffset.x + (relX + effW/2) / PPU
                    // worldY = gridOffset.y + (zH - 1) - (relY + effH) / PPU
                    float worldX = b.transform.position.x;
                    float worldY = b.transform.position.y;
                    relX = Mathf.RoundToInt((worldX - zd.gridOffset.x) * PPU - effW * 0.5f);
                    relY = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - worldY) * PPU - effH);
                }

                bool isLast = (i == _sceneBuildings.Count - 1);
                float splitOverride = b.SplitRatioOverride;
                Vector2Int scaleOv  = b.ScaleOverride;

                sb.Append("  {");
                sb.Append($"\"id\": {b.InstanceId}, ");
                sb.Append($"\"template_id\": {b.Template.templateId}, ");
                sb.Append($"\"zone\": \"{zone}\", ");
                sb.Append($"\"rel_x\": {relX}, ");
                sb.Append($"\"rel_y\": {relY}");

                bool hasZOv = b.ZBottomOffset != 0 || b.ZTopOffset != 0;
                bool hasOverrides = splitOverride >= 0f || scaleOv.x > 0 || scaleOv.y > 0 || hasZOv;
                if (hasOverrides)
                {
                    sb.Append(", \"overrides\": {");
                    bool firstOv = true;
                    if (scaleOv.x > 0 || scaleOv.y > 0)
                    {
                        sb.Append($"\"scale\": [{scaleOv.x}, {scaleOv.y}]");
                        firstOv = false;
                    }
                    if (splitOverride >= 0f)
                    {
                        if (!firstOv) sb.Append(", ");
                        sb.Append($"\"split_ratio\": {splitOverride:F4}");
                        firstOv = false;
                    }
                    if (b.ZBottomOffset != 0)
                    {
                        if (!firstOv) sb.Append(", ");
                        sb.Append($"\"z_bottom\": {b.ZBottomOffset}");
                        firstOv = false;
                    }
                    if (b.ZTopOffset != 0)
                    {
                        if (!firstOv) sb.Append(", ");
                        sb.Append($"\"z_top\": {b.ZTopOffset}");
                    }
                    sb.Append("}");
                }

                sb.Append("}");
                if (!isLast) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[BuildingsEditor] Saved {nextId - 1} instances to {path}");
            EditorUtility.DisplayDialog("Buildings Saved",
                $"Saved {nextId - 1} building instances to\n{path}", "OK");
        }

        private void ReloadSceneFromJson()
        {
            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null)
            {
                EditorUtility.DisplayDialog("No BuildingLoader",
                    "No BuildingLoader found in the active scene.\n" +
                    "Add a BuildingLoader component to a GameObject and assign the catalog.",
                    "OK");
                return;
            }

            loader.LoadBuildings();
            RefreshSceneBuildings();
            Repaint();
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RefreshSceneBuildings()
        {
            _sceneBuildings.Clear();
            var found = FindObjectsOfType<BuildingObject>();
            foreach (var b in found)
                _sceneBuildings.Add(b);
        }

        private void LoadCatalog()
        {
            if (_catalog != null) return;

            // Try well-known default path
            const string DEFAULT = "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";
            _catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(DEFAULT);

            if (_catalog == null)
            {
                // Search project
                string[] guids = AssetDatabase.FindAssets("t:BuildingCatalog");
                if (guids.Length > 0)
                    _catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }
    }
}
