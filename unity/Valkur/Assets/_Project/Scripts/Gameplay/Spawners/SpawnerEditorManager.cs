using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// In-game visual editor for spawner placement and management.
    /// Toggled with F3 (maps to Python's spawner_editor_manager.py toggle).
    ///
    /// Features:
    ///   - F3: Toggle editor overlay
    ///   - Template list panel with search/filter
    ///   - Click-to-place spawner instance on map
    ///   - Select/drag existing spawner instances
    ///   - Properties panel for selected spawner
    ///   - Save/load to StreamingAssets JSON
    ///
    /// MVC-lite: state in this class, rendering via UGUI canvas.
    /// Python had ~14 sub-modules; this is a consolidated Unity port.
    /// </summary>
    public partial class SpawnerEditorManager : SingletonMonoBehaviour<SpawnerEditorManager>
    {
        [Header("References")]
        [Tooltip("Catalog of spawner templates for the template list.")]
        [SerializeField] private SpawnerTemplateCatalog _catalog;

        [Tooltip("Camera used for screen-to-world conversion.")]
        [SerializeField] private Camera _camera;

        // --- Input ---
        private InputAction _toggleAction;
        private InputAction _clickAction;
        private InputAction _rightClickAction;
        private InputAction _escapeAction;

        // --- State ---
        private bool _visible;
        private EditorMode _mode = EditorMode.Select;
        private SpawnerTemplateData _selectedTemplate;
        private SpawnerInstance _selectedInstance;
        private bool _dragging;
        private Vector3 _dragOffset;

        // --- UI ---
        private Canvas _canvas;
        private GameObject _root;
        private Transform _templateListContent;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _propsText;
        private GameObject _toolbarPanel;
        private readonly List<GameObject> _templateRows = new List<GameObject>();
        private readonly List<GameObject> _gizmoMarkers = new List<GameObject>();

        public bool IsVisible => _visible;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleSpawnerEditor", InputActionType.Button);
            _toggleAction.AddBinding("<Keyboard>/f3");
            _toggleAction.Enable();

            _clickAction = new InputAction("SpawnerEditorClick", InputActionType.Button);
            _clickAction.AddBinding("<Mouse>/leftButton");
            _clickAction.Enable();

            _rightClickAction = new InputAction("SpawnerEditorRightClick", InputActionType.Button);
            _rightClickAction.AddBinding("<Mouse>/rightButton");
            _rightClickAction.Enable();

            _escapeAction = new InputAction("SpawnerEditorEscape", InputActionType.Button);
            _escapeAction.AddBinding("<Keyboard>/escape");
            _escapeAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            SetVisible(false);
        }

        private void Update()
        {
            if (_toggleAction.WasPerformedThisFrame())
                SetVisible(!_visible);

            if (!_visible) return;

            if (_escapeAction.WasPerformedThisFrame())
                CancelCurrentMode();

            HandleInput();
            UpdateStatusText();
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Disable(); _toggleAction?.Dispose();
            _clickAction?.Disable(); _clickAction?.Dispose();
            _rightClickAction?.Disable(); _rightClickAction?.Dispose();
            _escapeAction?.Disable(); _escapeAction?.Dispose();
            base.OnDestroy();
        }

        // ------------------------------------------------------------------
        // Input Handling
        // ------------------------------------------------------------------

        private void HandleInput()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector3 worldPos = _camera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0f));
            worldPos.z = 0f;

            switch (_mode)
            {
                case EditorMode.Place:
                    HandlePlaceMode(worldPos);
                    break;
                case EditorMode.Select:
                    HandleSelectMode(worldPos);
                    break;
                case EditorMode.Delete:
                    HandleDeleteMode(worldPos);
                    break;
            }

            // Dragging
            if (_dragging && _selectedInstance != null)
            {
                _selectedInstance.transform.position = worldPos + _dragOffset;
                if (_rightClickAction.WasReleasedThisFrame())
                    _dragging = false;
            }
        }

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
