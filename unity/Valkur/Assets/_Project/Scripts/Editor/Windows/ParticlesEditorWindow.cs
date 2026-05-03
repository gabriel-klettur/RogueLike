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
    /// <summary>
    /// Unity Editor counterpart of Python's ParticlesEditorController.
    ///
    /// Features:
    ///  â€¢ Scrollable preset picker (grid of named cards by kind â€” mirrors particles_picker_panel).
    ///  â€¢ Place Mode: click in the Scene view to place a PresetEmitter at world coords.
    ///  â€¢ Delete Mode: click a placed emitter in the Scene view to remove it.
    ///  â€¢ Instances list with selection, inline coord editing, and deletion.
    ///  â€¢ Load / Save particles_instances.json in StreamingAssets/Particles/.
    ///  â€¢ Live preview: spawns a ParticleEmitter in the scene when a preset is selected.
    ///
    /// Open via: Valkur > Particles > Particles Editor
    /// </summary>
    public partial class ParticlesEditorWindow : EditorWindow
    {
        private const string STREAMING_PARTICLES_DIR = "Particles";
        private const string INSTANCES_FILE = "particles_instances.json";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        // ------------------------------------------------------------------ state

        private ParticlePresetCatalog _catalog;

        // Instances managed by this editor
        private List<ParticleInstanceData> _instances = new List<ParticleInstanceData>();
        private int _nextId = 1;

        // UI state
        private Vector2 _presetScrollPos;
        private Vector2 _instanceScrollPos;
        private string _selectedPresetId;
        private int _selectedInstanceIdx = -1;

        // Modes (match Python's toolbar active_tool)
        private EditorMode _mode = EditorMode.None;

        // Preview emitter spawned in scene
        private GameObject _previewGo;

        // Zone override for new placements
        private string _selectedZone = "lobby";
        private float _scaleMultiplier = 1f;

        // Toolbar categories
        private string _filterKind = "";

        private enum EditorMode { None, Place, Delete }

        // ------------------------------------------------------------------ menu item

        [MenuItem("Valkur/Editors/Particles Editor")]
        public static void OpenWindow()
        {
            var win = GetWindow<ParticlesEditorWindow>("Particles Editor");
            win.minSize = new Vector2(420f, 500f);
            win.Show();
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            LoadCatalog();
            LoadInstances();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SetMode(EditorMode.None);
            DestroyPreview();
        }

        // ------------------------------------------------------------------ catalog load

        private void LoadCatalog()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            if (_catalog == null)
                Debug.LogWarning("[ParticlesEditorWindow] Catalog not found. Run Valkur > Particles > Import Presets first.");
        }

        // ------------------------------------------------------------------ instances persistence

        private string InstancesFilePath =>
            Path.Combine(Application.streamingAssetsPath, STREAMING_PARTICLES_DIR, INSTANCES_FILE);

        private void LoadInstances()
        {
            _instances.Clear();
            _nextId = 1;

            if (!File.Exists(InstancesFilePath)) return;

            try
            {
                string json = File.ReadAllText(InstancesFilePath);
                var rawParsed = MiniJson.Deserialize(json);

                // Support both v1 (bare array) and v2 ({"version":2,"instances":[...]}) formats.
                List<object> list = null;
                if (rawParsed is List<object> bareArray)
                {
                    list = bareArray; // v1
                }
                else if (rawParsed is Dictionary<string, object> obj &&
                         obj.TryGetValue("instances", out var inst) &&
                         inst is List<object> v2List)
                {
                    list = v2List; // v2
                }

                if (list == null) return;

                foreach (var item in list)
                {
                    if (item is not Dictionary<string, object> d) continue;
                    // v2 uses string "id" (GUID); v1 uses int. For editor window, use sequential int ids.
                    var data = new ParticleInstanceData
                    {
                        id               = GetInt(d, "id"),
                        preset_id        = GetString(d, "preset_id"),
                        zone             = GetString(d, "zone"),
                        rel_x            = GetInt(d, "rel_x"),
                        rel_y            = GetInt(d, "rel_y"),
                        scale_multiplier = GetFloat(d, "scale_multiplier", 1f)
                    };
                    _instances.Add(data);
                    if (data.id >= _nextId) _nextId = data.id + 1;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParticlesEditorWindow] Failed to load instances: {ex.Message}");
            }
        }

        private void SaveInstances()
        {
            string dir = Path.GetDirectoryName(InstancesFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < _instances.Count; i++)
            {
                var inst = _instances[i];
                sb.Append("  {");
                sb.Append($"\"id\": {inst.id}");
                sb.Append($", \"preset_id\": \"{inst.preset_id}\"");
                sb.Append($", \"zone\": \"{inst.zone}\"");
                sb.Append($", \"rel_x\": {inst.rel_x}");
                sb.Append($", \"rel_y\": {inst.rel_y}");
                if (Math.Abs(inst.scale_multiplier - 1f) > 0.001f)
                    sb.Append($", \"scale_multiplier\": {inst.scale_multiplier:F2}");
                sb.Append(i < _instances.Count - 1 ? "},\n" : "}\n");
            }
            sb.AppendLine("]");

            File.WriteAllText(InstancesFilePath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log($"[ParticlesEditorWindow] Saved {_instances.Count} instances to {InstancesFilePath}");
        }

    }
}
