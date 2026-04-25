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
    /// Toggled with F3 (bare, without Ctrl — maps to Python's spawner_editor_manager.py toggle).
    /// Ctrl+F3 opens the Lighting Editor instead.
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
    public partial class SpawnerEditorManager : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor
    {
        [Header("References")]
        [Tooltip("Catalog of spawner templates for the template list.")]
        [SerializeField] private SpawnerTemplateCatalog _catalog;

        [Tooltip("Camera used for screen-to-world conversion.")]
        [SerializeField] private Camera _camera;

        // --- Input ---
        private InputAction _toggleAction;
        private InputAction _ctrlModifier;
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

        // IGameEditor
        public string EditorName => "Spawner Editor";
        public bool IsActive => _visible;

        public void Activate()
        {
            if (!_visible) SetVisible(true);
        }

        public void Deactivate()
        {
            if (_visible) SetVisible(false);
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleSpawnerEditor", InputActionType.Button);
            _toggleAction.AddBinding("<Keyboard>/f3");
            _toggleAction.Enable();
            _ctrlModifier = new InputAction("CtrlModSpawner", InputActionType.Button, "<Keyboard>/leftCtrl");
            _ctrlModifier.Enable();

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
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        private void Update()
        {
            if (_toggleAction.WasPerformedThisFrame() && !_ctrlModifier.IsPressed())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    SetVisible(!_visible);
            }

            if (!_visible) return;

            if (_escapeAction.WasPerformedThisFrame())
                CancelCurrentMode();

            HandleInput();
            UpdateStatusText();
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Disable(); _toggleAction?.Dispose();
            _ctrlModifier?.Disable(); _ctrlModifier?.Dispose();
            _clickAction?.Disable(); _clickAction?.Dispose();
            _rightClickAction?.Disable(); _rightClickAction?.Dispose();
            _escapeAction?.Disable(); _escapeAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
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

    }
}