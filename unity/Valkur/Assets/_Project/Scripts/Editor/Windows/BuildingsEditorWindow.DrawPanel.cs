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

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Catalog picker
            var newCatalog = (BuildingCatalog)EditorGUILayout.ObjectField(
                _catalog, typeof(BuildingCatalog), allowSceneObjects: false,
                GUILayout.Width(200f));
            if (newCatalog != _catalog)
            {
                _catalog = newCatalog;
                _selectedTemplate = null;
            }

            if (GUILayout.Button("Reload Catalog", EditorStyles.toolbarButton, GUILayout.Width(100)))
                LoadCatalog();

            GUILayout.FlexibleSpace();

            // Place mode toggle
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = _placeMode ? Color.green : Color.white;
            if (GUILayout.Button(_placeMode ? "â–  Placing" : "âœš Place Mode",
                    EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                _placeMode = !_placeMode;
                if (_placeMode && _selectedTemplate == null)
                    Debug.LogWarning("[BuildingsEditor] Select a template in the palette before placing.");
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = prev;

            GUILayout.Space(8);

            if (GUILayout.Button("Save Instances", EditorStyles.toolbarButton, GUILayout.Width(100)))
                SaveInstancesToJson();

            if (GUILayout.Button("Reload Scene", EditorStyles.toolbarButton, GUILayout.Width(100)))
                ReloadSceneFromJson();

            EditorGUILayout.EndHorizontal();
        }

        // â”€â”€ Palette â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void DrawPalette()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(PALETTE_WIDTH));
            EditorGUILayout.LabelField("Templates", EditorStyles.boldLabel);

            if (_catalog == null)
            {
                EditorGUILayout.HelpBox("Assign a BuildingCatalog in the toolbar, or run\n" +
                    "Valkur > Migration > Import Buildings from Python JSON first.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll);

            var templates = _catalog.Templates;
            int col = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (var tpl in templates)
            {
                if (tpl == null) continue;

                bool isSelected = _selectedTemplate == tpl;
                Rect r = GUILayoutUtility.GetRect(THUMB_SIZE + THUMB_PAD * 2,
                                                   THUMB_SIZE + THUMB_PAD * 2 + 14f,
                                                   GUILayout.Width(THUMB_SIZE + THUMB_PAD * 2));

                // Background highlight
                if (isSelected)
                    EditorGUI.DrawRect(r, new Color(0.3f, 0.6f, 1f, 0.35f));

                // Thumbnail
                Rect thumbRect = new Rect(r.x + THUMB_PAD, r.y + THUMB_PAD, THUMB_SIZE, THUMB_SIZE);
                if (tpl.previewSprite != null)
                {
                    var tex = AssetPreview.GetAssetPreview(tpl.previewSprite);
                    if (tex != null)
                        GUI.DrawTexture(thumbRect, tex, ScaleMode.ScaleToFit);
                    else
                        EditorGUI.DrawRect(thumbRect, new Color(0.2f, 0.2f, 0.2f));
                }
                else
                {
                    EditorGUI.DrawRect(thumbRect, new Color(0.2f, 0.2f, 0.2f));
                    GUI.Label(thumbRect, $"#{tpl.templateId}", EditorStyles.centeredGreyMiniLabel);
                }

                // Label
                string label = tpl.name.Replace("BuildingTemplate_", "#");
                GUI.Label(new Rect(r.x, r.yMax - 14f, r.width, 14f), label,
                    EditorStyles.centeredGreyMiniLabel);

                // Click to select
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedTemplate = tpl;
                    GUI.FocusControl(null);
                    Event.current.Use();
                    if (_placeMode) SceneView.RepaintAll();
                    Repaint();
                }

                col++;
                if (col >= THUMB_COLS)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // â”€â”€ Main panel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void DrawMainPanel()
        {
            EditorGUILayout.BeginVertical();

            DrawSelectedBuildingInspector();
            EditorGUILayout.Space(4);
            DrawSceneBuildingsList();

            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedBuildingInspector()
        {
            EditorGUILayout.LabelField("Selected Building", EditorStyles.boldLabel);

            if (_selectedBuilding == null)
            {
                EditorGUILayout.HelpBox("Click a building in the Scene or the list below.",
                    MessageType.None);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(false))
            {
                EditorGUILayout.ObjectField("Building Object", _selectedBuilding,
                    typeof(BuildingObject), allowSceneObjects: true);

                // Template (read-only display)
                var tpl = _selectedBuilding.Template;
                if (tpl != null)
                {
                    EditorGUILayout.LabelField("Template ID",  tpl.templateId.ToString());
                    EditorGUILayout.LabelField("Asset Path",   tpl.assetPath);
                    EditorGUILayout.LabelField("Original Scale",
                        $"{tpl.originalScale.x} Ã— {tpl.originalScale.y} px");
                }

                EditorGUI.BeginChangeCheck();

                // Split ratio override
                float currentSplit = _selectedBuilding.SplitRatioOverride >= 0f
                    ? _selectedBuilding.SplitRatioOverride
                    : (tpl != null ? tpl.splitRatio : 0.5f);

                bool useOverride = _selectedBuilding.SplitRatioOverride >= 0f;
                EditorGUILayout.BeginHorizontal();
                bool newUseOverride = EditorGUILayout.Toggle("Override Split Ratio", useOverride,
                    GUILayout.Width(180));
                float newSplit = EditorGUILayout.Slider(currentSplit, 0f, 1f);
                EditorGUILayout.EndHorizontal();

                float newSplitOverride = newUseOverride ? newSplit : -1f;

                // Scale override
                Vector2Int scaleOv = _selectedBuilding.ScaleOverride;
                Vector2Int newScale = EditorGUILayout.Vector2IntField("Scale Override (0=default)", scaleOv);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_selectedBuilding, "Edit Building");
                    _selectedBuilding.SplitRatioOverride = newSplitOverride;
                    _selectedBuilding.ScaleOverride      = newScale;
                    if (tpl != null)
                        _selectedBuilding.Apply(tpl, newScale, newSplitOverride);
                    EditorUtility.SetDirty(_selectedBuilding);
                }

                EditorGUILayout.Space(4);

                if (GUILayout.Button("Delete Selected Building", GUILayout.Height(24)))
                {
                    if (EditorUtility.DisplayDialog("Delete Building",
                        $"Delete building instance {_selectedBuilding.InstanceId}?", "Delete", "Cancel"))
                    {
                        Undo.DestroyObjectImmediate(_selectedBuilding.gameObject);
                        _selectedBuilding = null;
                        RefreshSceneBuildings();
                    }
                }
            }
        }

        private void DrawSceneBuildingsList()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Scene Buildings ({_sceneBuildings.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button("â†º", GUILayout.Width(24)))
                RefreshSceneBuildings();
            EditorGUILayout.EndHorizontal();

            _instanceScroll = EditorGUILayout.BeginScrollView(_instanceScroll, GUILayout.MaxHeight(200f));

            for (int i = _sceneBuildings.Count - 1; i >= 0; i--)
            {
                var b = _sceneBuildings[i];
                if (b == null) { _sceneBuildings.RemoveAt(i); continue; }

                bool isSelected = b == _selectedBuilding;
                Color prev = GUI.backgroundColor;
                if (isSelected) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                string tplName = b.Template != null ? b.Template.name : "?";
                EditorGUILayout.LabelField($"#{b.InstanceId}  {b.ZoneName}  {tplName}",
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Select", GUILayout.Width(55)))
                {
                    _selectedBuilding = b;
                    Selection.activeGameObject = b.gameObject;
                    SceneView.lastActiveSceneView?.Frame(
                        new Bounds(b.transform.position, Vector3.one * 5f), instant: false);
                }
                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = prev;
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
