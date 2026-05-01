using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime in-game Lighting Editor (Ctrl+F3).
    /// Mirrors Python's lighting_editor: 3-panel layout with global toggles/quality,
    /// day/night cycle controls and keyframe editing, and light preset tuning.
    /// Place, move, and delete light instances on the map.
    /// </summary>
    public partial class LightingRuntimeEditor : SingletonMonoBehaviour<LightingRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Light preset catalog")]
        private LightPresetCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;
        private InputAction _ctrlModifier;
        private bool _ownsToggleAction;
        private bool _ownsCtrlModifier;

        // State
        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedPresetKey;
        private bool _singleShot;

        // Global lighting toggles
        private bool _ambientEnabled = true;
        private bool _pointLightsEnabled = true;
        private bool _shadowsEnabled;
        private bool _overlayVisible;
        private bool _labelsVisible;

        // Quality params
        private int _maxLights = 12;
        private int _maxRadius = 192;
        private int _shadowRays = 64;

        // Day/Night
        private float _dayTimeMinutes = 720f; // noon
        private float _timeScale = 0.4f;
        private float _minIntensity;

        // Drag
        private bool _dragging;
        private GameObject _dragTarget;
        private Vector3 _dragOffset;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private TextMeshProUGUI _dayTimeTmp;
        private Image _selectBtnImg, _spawnBtnImg, _deleteBtnImg;

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private RectTransform _presetButtonsParent;
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // IGameEditor
        public string EditorName => "Lighting Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleLighting, out _ownsToggleAction);
            _ctrlModifier = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.CtrlModifier, out _ownsCtrlModifier);
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (_ownsCtrlModifier) _ctrlModifier?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Ctrl+F3 only
            if (_toggleAction.WasPerformedThisFrame() && _ctrlModifier.IsPressed())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;

            // Middle-mouse camera pan — same UX as every other runtime editor.
            _cameraPan.Tick();

            UpdateDayTimeDisplay();
            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshModeButtons();
            _statusTmp.text = "Lighting Editor active. Ctrl+F3 to close.";
            Debug.Log("[LightingEditor] Activated (Ctrl+F3)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _dragging = false;
            _dragTarget = null;
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[LightingEditor] Deactivated (Ctrl+F3)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("LightingEditorCanvas", 111);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            BuildMainPanel();
            BuildDayTimePanel();
            BuildPresetsPanel();

            _tutorial = TutorialOverlay.Build(_root.transform, "LIGHTING HOTKEYS", new[]
            {
                ("Ctrl+F3","Toggle Lighting Editor"),
                ("LMB",    "Select / place / delete"),
                ("Type",   "Filter presets"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Panel 1: Main Lighting Settings (left) ──

    }
}