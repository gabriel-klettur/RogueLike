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

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Runtime in-game Particles Editor (F1).
    ///
    /// UI/UX layer mirrors the professional menu-bar + draggable-panel architecture
    /// shared by Buildings (F10), Entities (F5) and Tile (F8) editors:
    ///   • 30 px menu bar + brand + dropdown buttons + tutorial + perf
    ///   • DraggablePanel + PanelChrome floating panels (Tools / Presets / Properties / Spells)
    ///   • UndoStack(64) wired to every persistent edit
    ///   • Tutorial overlay with Prev / Next stepper
    ///   • Confirm-delete modal
    ///   • Drag-from-picker → spawn at cursor
    ///   • Save / load StreamingAssets/Particles/particles_instances.json
    ///
    /// Mirrors Python <c>roguelike_editors/particles</c>:
    ///   tool_bar / picker / properties / spells_list / add_remove panels.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Particle preset catalog (ParticlePresetCatalog).")]
        private ParticlePresetCatalog _catalog;

        // ── State ────────────────────────────────────────────────────────────────
        private bool _active;

        private enum EditorMode { Select, Place, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedPresetId;

        // Currently selected world instance (yellow outline).
        private GameObject _activeInstance;
        // Drag state for moving an existing instance with RMB.
        private bool _dragging;
        private GameObject _dragTarget;
        private Vector3 _dragOffset;
        private Vector3 _dragStartWorldPos;

        // Picker filtering — search-box driven, no grouping.
        private string _searchFilter = "";

        /// <summary>
        /// Active preset category tab key. Empty or <c>__all</c> means no category gate.
        /// Combines with <see cref="_searchFilter"/> — the search runs inside the tab.
        /// </summary>
        private string _categoryFilter = "";

        // Spells-using-this-preset collapsible (Python parity).
        private bool _spellsExpanded = true;

        // Persistence flags.
        private bool _hasUnsavedInstanceChanges;
        private bool _isPersistingInstanceChanges;

        // Undo/redo (capacity 64 — same as Buildings/Entities).
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // ── Preview service ──────────────────────────────────────────────────────
        private readonly ParticlePreviewService _previewService = new ParticlePreviewService();

        // ── UI ───────────────────────────────────────────────────────────────────
        private Canvas _canvas;
        private GameObject _root;
        private ParticlesEditorUIBuilder.UIRefs _ui;

        // Open-dropdown tracking (mirrors EntitiesRuntimeEditor pattern).
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // Tutorial + confirm modal (built lazily in BuildUI).
        private GameObject _tutorialRoot;
        private TextMeshProUGUI _tutorialStepLabel, _tutorialBodyTmp;
        private int _tutorialStep;
        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1. Open editor",        "Press F1 anywhere in-game to toggle the Particles Editor."),
            ("2. Pick a preset",      "In the Presets panel, click a thumbnail to select it. Type in the search box to filter by id or display name."),
            ("3. Place an instance",  "Drag a preset from the Presets panel onto the map to spawn an emitter, or click \"Add System\" then click on the map."),
            ("4. Move an instance",   "Right-click + drag a particle instance on the map to move it. Release to commit."),
            ("5. Delete an instance", "Click \"Remove\" or press the Delete tool, then click an instance. A confirmation modal will ask before destroying."),
            ("6. Undo / Redo",        "Use the Tools panel Undo / Redo buttons (or Ctrl+Z / Ctrl+Y). Capacity is 64."),
            ("7. Save",               "Click Save in the Tools panel to write StreamingAssets/Particles/particles_instances.json. Press F1 again to close."),
        };

        // Confirm-delete modal.
        private GameObject _confirmModal;
        private TextMeshProUGUI _confirmText;
        private System.Action _pendingConfirmYes;

        // ── IGameEditor ─────────────────────────────────────────────────────────
        public string EditorName => "Particles Editor";
        public bool IsActive => _active;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            _previewService.Shutdown();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleParticles))
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }
            if (!_active) return;

            // Middle-mouse pan runs unconditionally.
            _cameraPan.Tick();
            _previewService.Tick();
            TickViewPanelInput();

            UpdatePickerDrag();
            UpdateOutlineState();

            // Suppress map click while a picker drag is in progress; the drag-drop
            // path is what spawns the emitter.
            if (_pickerDragging) return;

            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            EnsureOutlineFx();
            _previewService.Initialize(transform);
            OpenDefaultDropdowns();
            RefreshPicker();
            RefreshTable();
            RefreshViewPanel();
            RefreshModeButtons();
            RefreshSpellsPanel();
            RefreshUndoRedoLabels();
            UpdateParticleColumnsBtnLabel();
            SetStatus("Particles Editor active. F1 to close.");
            Debug.Log("[ParticlesEditor] Activated (F1)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedPresetId = null;
            _activeInstance = null;
            _hoveredInstance = null;
            _showAllOutlines = false;
            HideAllOutlineFx();
            _dragging = false;
            _dragTarget = null;
            CancelPickerDrag();
            _previewService.Shutdown();
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ParticlesEditor] Deactivated (F1)");
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        // ── Status helper ──────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }
    }
}
