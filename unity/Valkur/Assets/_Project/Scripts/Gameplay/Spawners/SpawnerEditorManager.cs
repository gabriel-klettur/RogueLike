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
    public class SpawnerEditorManager : SingletonMonoBehaviour<SpawnerEditorManager>
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

        // ------------------------------------------------------------------
        // UI Construction
        // ------------------------------------------------------------------

        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("SpawnerEditorCanvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Root panel
            _root = CreatePanel(canvasGo.transform, "RootPanel", new Color(0.05f, 0.05f, 0.1f, 0.85f));
            var rootRT = _root.GetComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 0f);
            rootRT.anchorMax = new Vector2(0.25f, 1f);
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            var vlg = _root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6f;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            // Title
            CreateLabel(_root.transform, "Spawner Editor (F3)", 18, new Color(0.9f, 0.8f, 0.3f), 30f);

            // Toolbar
            _toolbarPanel = CreatePanel(_root.transform, "Toolbar", new Color(0.1f, 0.1f, 0.15f));
            _toolbarPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);
            var hlg = _toolbarPanel.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;

            CreateToolButton(_toolbarPanel.transform, "Select", () => _mode = EditorMode.Select);
            CreateToolButton(_toolbarPanel.transform, "Place", () => _mode = EditorMode.Place);
            CreateToolButton(_toolbarPanel.transform, "Delete", () => _mode = EditorMode.Delete);
            CreateToolButton(_toolbarPanel.transform, "Save", SaveInstancesToJson);

            // Template list (scrollable)
            CreateLabel(_root.transform, "Templates:", 14, Color.white, 20f);
            var scrollGo = CreateScrollView(_root.transform, "TemplateScroll", 300f);
            _templateListContent = scrollGo.transform;

            // Status
            _statusText = CreateLabel(_root.transform, "Mode: Select", 12, Color.gray, 20f);

            // Properties panel
            CreateLabel(_root.transform, "Properties:", 14, Color.white, 20f);
            _propsText = CreateLabel(_root.transform, "(none selected)", 11, Color.gray, 100f);

            PopulateTemplateList();
        }

        private void PopulateTemplateList()
        {
            foreach (var row in _templateRows)
                Destroy(row);
            _templateRows.Clear();

            if (_catalog == null) return;

            foreach (var tmpl in _catalog.Templates)
            {
                if (tmpl == null) continue;
                var btnGo = new GameObject(tmpl.templateId);
                btnGo.transform.SetParent(_templateListContent, worldPositionStays: false);
                var rt = btnGo.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0, 28);

                var img = btnGo.AddComponent<Image>();
                img.color = new Color(0.15f, 0.15f, 0.22f);

                var btn = btnGo.AddComponent<Button>();
                var captured = tmpl;
                btn.onClick.AddListener(() =>
                {
                    _selectedTemplate = captured;
                    _mode = EditorMode.Place;
                });

                var textGo = new GameObject("Label");
                textGo.transform.SetParent(btnGo.transform, worldPositionStays: false);
                var textRT = textGo.AddComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(6, 0);
                textRT.offsetMax = new Vector2(-6, 0);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = tmpl.templateId;
                tmp.fontSize = 12;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;

                _templateRows.Add(btnGo);
            }
        }

        private void UpdateStatusText()
        {
            if (_statusText == null) return;
            string modeStr = _mode.ToString();
            if (_mode == EditorMode.Place && _selectedTemplate != null)
                modeStr += $" ({_selectedTemplate.templateId})";
            _statusText.text = $"Mode: {modeStr}";
        }

        private void UpdatePropertiesPanel()
        {
            if (_propsText == null) return;
            if (_selectedInstance == null || _selectedInstance.Template == null)
            {
                _propsText.text = "(none selected)";
                return;
            }

            var t = _selectedInstance.Template;
            var pos = _selectedInstance.transform.position;
            _propsText.text =
                $"ID: {_selectedInstance.InstanceId}\n" +
                $"Template: {t.templateId}\n" +
                $"Zone: {_selectedInstance.Zone}\n" +
                $"Pos: ({pos.x:F1}, {pos.y:F1})\n" +
                $"Type: {t.spawnerType}\n" +
                $"Trigger: {t.triggerType} (r={t.triggerRadius})\n" +
                $"Waves: {t.waves?.Count ?? 0}\n" +
                $"State: {_selectedInstance.State}";
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);
            if (!visible)
            {
                _mode = EditorMode.Select;
                _selectedTemplate = null;
                _dragging = false;
            }
        }

        // ------------------------------------------------------------------
        // UI Helpers
        // ------------------------------------------------------------------

        private static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var img = go.AddComponent<Image>();
            img.color = color;
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, int fontSize, Color color, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            return tmp;
        }

        private static void CreateToolButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(60, 30);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.3f, 0.5f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(action);
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, worldPositionStays: false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 11;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        private static GameObject CreateScrollView(Transform parent, string name, float height)
        {
            var scrollGo = new GameObject(name);
            scrollGo.transform.SetParent(parent, worldPositionStays: false);
            var scrollRT = scrollGo.AddComponent<RectTransform>();
            scrollRT.sizeDelta = new Vector2(0, height);
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0.08f, 0.08f, 0.12f);
            var sr = scrollGo.AddComponent<ScrollRect>();

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, worldPositionStays: false);
            var vpRT = viewport.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.clear;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, worldPositionStays: false);
            var contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0, 0);
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;

            sr.content = contentRT;
            sr.viewport = vpRT;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            return content;
        }

        private enum EditorMode
        {
            Select,
            Place,
            Delete
        }
    }
}
