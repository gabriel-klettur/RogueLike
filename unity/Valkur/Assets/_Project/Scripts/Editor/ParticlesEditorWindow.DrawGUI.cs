using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    public partial class ParticlesEditorWindow
    {
        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(2);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPresetsPanel();
                DrawInstancesPanel();
            }
        }

        // ---------- Toolbar (mirrors particles_tool_bar_panel) ----------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Particles Editor", EditorStyles.boldLabel, GUILayout.Width(130));
                GUILayout.FlexibleSpace();

                // Mode buttons
                GUI.backgroundColor = _mode == EditorMode.Place ? Color.green : Color.white;
                if (GUILayout.Button("Place", EditorStyles.toolbarButton, GUILayout.Width(55)))
                    SetMode(_mode == EditorMode.Place ? EditorMode.None : EditorMode.Place);

                GUI.backgroundColor = _mode == EditorMode.Delete ? new Color(1f, 0.5f, 0.5f) : Color.white;
                if (GUILayout.Button("Delete", EditorStyles.toolbarButton, GUILayout.Width(55)))
                    SetMode(_mode == EditorMode.Delete ? EditorMode.None : EditorMode.Delete);

                GUI.backgroundColor = Color.white;

                GUILayout.Space(6);

                if (GUILayout.Button("Save JSON", EditorStyles.toolbarButton, GUILayout.Width(75)))
                    SaveInstances();

                if (GUILayout.Button("Reload JSON", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    LoadInstances();

                GUILayout.Space(6);

                if (GUILayout.Button("Refresh Catalog", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    LoadCatalog();
                    if (_catalog == null)
                        EditorUtility.DisplayDialog("Catalog Missing",
                            "No catalog found.\nRun: Valkur > Particles > Import Presets from Python JSON", "OK");
                }
            }

            // Zone + scale strip (mirrors particles_add_remove_panel zone selector)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Zone:", GUILayout.Width(38));
                _selectedZone = EditorGUILayout.TextField(_selectedZone, GUILayout.Width(120));
                GUILayout.Space(8);
                GUILayout.Label("Scale:", GUILayout.Width(40));
                _scaleMultiplier = EditorGUILayout.FloatField(_scaleMultiplier, GUILayout.Width(50));
                _scaleMultiplier = Mathf.Max(0.01f, _scaleMultiplier);
                GUILayout.Space(8);
                GUILayout.Label("Filter kind:", GUILayout.Width(65));
                _filterKind = EditorGUILayout.TextField(_filterKind, GUILayout.Width(100));
                GUILayout.FlexibleSpace();

                // Status label
                string modeLabel = _mode switch
                {
                    EditorMode.Place  => $"[PLACE: {_selectedPresetId ?? "none"}]",
                    EditorMode.Delete => "[DELETE MODE]",
                    _                 => ""
                };
                GUILayout.Label(modeLabel, EditorStyles.miniLabel);
            }
        }

        // ---------- Presets panel (mirrors particles_picker_panel) ----------

        private void DrawPresetsPanel()
        {
            float panelWidth = position.width * 0.40f;

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(panelWidth)))
            {
                EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);

                if (_catalog == null)
                {
                    EditorGUILayout.HelpBox("No catalog loaded.\nValkur > Particles > Import Presets from Python JSON",
                        MessageType.Warning);
                    return;
                }

                using (var scroll = new EditorGUILayout.ScrollViewScope(_presetScrollPos))
                {
                    _presetScrollPos = scroll.scrollPosition;

                    string lastKind = null;
                    foreach (var preset in _catalog.Presets)
                    {
                        if (preset == null) continue;
                        string kind = preset.vfx?.kind ?? "";

                        // Apply kind filter
                        if (!string.IsNullOrEmpty(_filterKind) &&
                            !kind.Contains(_filterKind, StringComparison.OrdinalIgnoreCase) &&
                            !preset.id.Contains(_filterKind, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Group header
                        if (kind != lastKind)
                        {
                            if (lastKind != null) EditorGUILayout.Space(2);
                            EditorGUILayout.LabelField(kind.ToUpper(), EditorStyles.miniLabel);
                            lastKind = kind;
                        }

                        bool isSelected = preset.id == _selectedPresetId;
                        GUI.backgroundColor = isSelected ? new Color(0.5f, 0.8f, 1f) : Color.white;

                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            if (GUILayout.Button(preset.displayName ?? preset.id, EditorStyles.label,
                                    GUILayout.ExpandWidth(true)))
                            {
                                SelectPreset(preset.id);
                            }

                            EditorGUILayout.LabelField(preset.type ?? "", EditorStyles.miniLabel, GUILayout.Width(60));
                        }

                        GUI.backgroundColor = Color.white;
                    }
                }

                // Selected preset details
                if (!string.IsNullOrEmpty(_selectedPresetId))
                {
                    var def = _catalog.GetById(_selectedPresetId);
                    if (def != null)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField($"id: {def.id}", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField($"kind: {def.vfx?.kind}  speed: {def.vfx?.speed:F2}  life: {def.vfx?.lifespan:F2}s",
                            EditorStyles.miniLabel);

                        if (GUILayout.Button("Place selected preset â†’"))
                        {
                            SetMode(EditorMode.Place);
                            SceneView.lastActiveSceneView?.Focus();
                        }
                        if (GUILayout.Button("Spawn preview in scene"))
                            SpawnPreview(def);
                        if (GUILayout.Button("Destroy preview"))
                            DestroyPreview();
                    }
                }
            }
        }

        // ---------- Instances panel (mirrors particles_properties_panel + add/remove) ----------

        private void DrawInstancesPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                EditorGUILayout.LabelField($"Instances ({_instances.Count})", EditorStyles.boldLabel);

                using (var scroll = new EditorGUILayout.ScrollViewScope(_instanceScrollPos))
                {
                    _instanceScrollPos = scroll.scrollPosition;

                    for (int i = 0; i < _instances.Count; i++)
                    {
                        var inst = _instances[i];
                        bool isSelected = _selectedInstanceIdx == i;
                        GUI.backgroundColor = isSelected ? new Color(0.5f, 0.8f, 1f) : Color.white;

                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                if (GUILayout.Button($"[{inst.id}] {inst.preset_id}", EditorStyles.label,
                                        GUILayout.ExpandWidth(true)))
                                    _selectedInstanceIdx = isSelected ? -1 : i;

                                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                                if (GUILayout.Button("âœ•", GUILayout.Width(22)))
                                {
                                    _instances.RemoveAt(i);
                                    if (_selectedInstanceIdx >= _instances.Count)
                                        _selectedInstanceIdx = _instances.Count - 1;
                                    GUI.backgroundColor = Color.white;
                                    break;
                                }
                                GUI.backgroundColor = Color.white;
                            }

                            if (isSelected)
                                DrawInstanceDetails(inst, i);
                        }

                        GUI.backgroundColor = Color.white;
                    }
                }
            }
        }

        private void DrawInstanceDetails(ParticleInstanceData inst, int idx)
        {
            EditorGUI.indentLevel++;

            inst.preset_id = EditorGUILayout.TextField("Preset ID", inst.preset_id);
            inst.zone      = EditorGUILayout.TextField("Zone", inst.zone);

            using (new EditorGUILayout.HorizontalScope())
            {
                inst.rel_x = EditorGUILayout.IntField("Rel X (px)", inst.rel_x);
                inst.rel_y = EditorGUILayout.IntField("Rel Y (px)", inst.rel_y);
            }

            inst.scale_multiplier = EditorGUILayout.FloatField("Scale", inst.scale_multiplier);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Focus in Scene", GUILayout.Width(110)))
                    FocusInstance(inst);
                if (GUILayout.Button("Select Preset â†’", GUILayout.Width(100)))
                    SelectPreset(inst.preset_id);
            }

            _instances[idx] = inst;
            EditorGUI.indentLevel--;
        }

    }
}
