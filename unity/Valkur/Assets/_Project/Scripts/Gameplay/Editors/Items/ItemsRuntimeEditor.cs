using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Runtime in-game Items Editor (F7) — PHASE 1: UI/UX scaffolding only.
    ///
    /// Visual chrome mirrors the Unity Tile Editor (F8) and Buildings Editor (F10):
    ///   • 30 px menu bar at top:
    ///       brand "ITEMS EDITOR" + Modes / Items / Properties / Instances dropdowns + ? + PERF.
    ///   • Floating, draggable panels for each section.
    ///
    /// Content mirrors the Python items_editor (F7) panel set:
    ///   • Title           → menu-bar brand
    ///   • Toolbar         → "items_on_map / undo / redo / tutorial_items"
    ///   • Add/Remove sub-toolbar → "Add / Remove / Add on System"
    ///       (collapsed into the Modes panel together with Select/Spawn/Delete)
    ///   • Picker grid     → Items panel (search + grid)
    ///   • Properties      → Properties panel (selected item inspector)
    ///   • Instances       → Instances panel (map drops list + params editor)
    ///   • Tutorial        → TutorialOverlay
    ///
    /// PHASE 1 scope: chrome + dropdown management + tutorial overlay only.
    /// All action callbacks (mode switches, Add/Remove/AddOnSystem, Undo/Redo, search,
    /// PERF) are placeholders that emit a status toast — Phase 2 will wire them to
    /// the data layer (ItemDefinition, drops, persistence, …).
    /// </summary>
    public partial class ItemsRuntimeEditor : SingletonMonoBehaviour<ItemsRuntimeEditor>,
        GameEditorManager.IGameEditor, Valkur.Core.IAllowsPlayerMovement
    {
        // ── State ──
        private bool _active;
        private bool _uiBuilt;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;

        // ── UI ──
        private Canvas _canvas;
        private GameObject _root;
        private ItemsEditorUIBuilder.UIRefs _uiRefs;
        private GameObject _tutorial;
        private string _searchFilter = "";

        // Dropdown open/close state — mirrors BuildingsRuntimeEditor.UI.cs
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── Catalog (Phase 2) ──
        private ItemDefinition[] _allItems;          // populated lazily on first use
        private readonly List<ItemDefinition> _filtered = new List<ItemDefinition>();
        private string _selectedItemId;              // null => no selection

        // ── Instances (Phase 2) ──
        private readonly List<WorldPickup> _instances = new List<WorldPickup>();
        private float _lastInstanceRefresh;
        private const float INSTANCE_REFRESH_INTERVAL = 0.5f;
        private WorldPickup _selectedInstance;

        // ── Map hover / outline FX ──
        // Two long-lived ItemOutlineRenderer children draw the cyan-hover and
        // yellow-active rectangles around the SpriteRenderer.bounds of the
        // pickup they Follow(). Mirrors how Buildings / Spawners / Particles
        // editors render their highlights.
        private WorldPickup _hoveredInstance;
        private Valkur.Gameplay.WorldDrops.ItemOutlineRenderer _hoverFx;
        private Valkur.Gameplay.WorldDrops.ItemOutlineRenderer _activeFx;
        // Legacy tint cache — kept temporarily while callers transition off the
        // sprite-color tinting path. Outline FX is the canonical highlight now.
        private readonly Dictionary<SpriteRenderer, Color> _originalSpriteColors
            = new Dictionary<SpriteRenderer, Color>();
        private static readonly Color HOVER_CYAN    = new Color(0.30f, 0.85f, 1.00f, 1f);
        private static readonly Color ACTIVE_YELLOW = new Color(1.00f, 0.95f, 0.30f, 1f);
        private static readonly Color DELETE_RED    = new Color(1.00f, 0.40f, 0.40f, 1f);

        // ── Drag-from-picker (Phase 3) ──
        // Mirrors BuildingsRuntimeEditor: slots emit PointerDown → record drag start;
        // once the cursor moves PICKER_DRAG_THRESHOLD pixels with LMB held, a ghost
        // image follows the cursor on the editor canvas, and on LMB release over the
        // map the selected item is dropped at that world position.
        private bool   _pickerDragging;
        private string _pickerDragItemId;
        private Vector2 _pickerDragStartScreen;
        private GameObject  _dragGhostGo;
        private RectTransform _dragGhostRt;
        private Image       _dragGhostImg;
        private Image       _dragGhostOutline;
        private const float PICKER_DRAG_THRESHOLD = 8f;
        private const float DRAG_GHOST_BORDER     = 4f;
        private const float ITEM_PPU              = 16f;
        private static readonly Color DRAG_GHOST_OUTLINE = new Color(1.00f, 0.95f, 0.30f, 0.85f);
        private static readonly Color DRAG_GHOST_TINT    = new Color(1.00f, 1.00f, 1.00f, 0.85f);

        // ── Camera focus on instance press-and-hold ──
        private Camera _mainCamera;
        private WorldPickup _holdingInstance;
        private float _holdStartTime;
        private bool _cameraDetachedByUs;
        private const float HOLD_THRESHOLD = 0.25f;  // seconds

        // ── RMB drag-to-move (mirrors Buildings) ──
        // While the right mouse button is held over a hovered drop the pickup
        // follows the cursor; on release the new position is persisted through
        // ItemDropService.UpdatePosition with full Undo support.
        private WorldPickup _movingInstance;
        private string      _moveDropId;
        private Vector3     _moveStartWorldPos;

        // ── Undo (Phase 2) ──
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // ── IGameEditor ──
        public string EditorName => "Items Editor";
        public bool IsActive => _active;

        // ── Lifecycle ──

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleItems, out _ownsToggleAction);
        }

        private void Start()
        {
            // Lazy UI build (mirrors BuildingsRuntimeEditor): nothing is created or
            // visible until the user presses F7 the first time. This avoids the menu
            // bar being briefly drawn at scene start before SetActive(false) takes effect.
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleItems))
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }

            if (!_active) return;

            // Middle-mouse pan runs unconditionally so dragging the camera works
            // even while hover/drag/hold-focus interactions are in progress.
            _cameraPan.Tick();

            HandleKeyboardShortcuts();
            HandleMapInteraction();      // hover/select WorldPickups + delete-mode click
            UpdatePickerDrag();          // drag-from-picker → SpawnAt on drop
            HandleInstanceHoldFocus();
            MaybeRefreshInstances();
            UpdateOutlineState();        // tint hovered/active sprites every frame
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ItemsEditor] BuildUI failed at: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            OpenAllPanels();
            EnsureCatalog();
            RefreshPicker();
            RefreshProperties();
            ForceRefreshInstances();

            // Detach the cinemachine follow so MMB pan can move the camera
            // freely. ReattachFollow runs in Deactivate so closing the editor
            // snaps back onto the player. Same pattern as Buildings/Tile.
            _mainCamera = Camera.main;
            Valkur.Gameplay.CameraSetup.Instance?.DetachFollow();

            SetStatus("Items Editor active. F7 to close.");
            Debug.Log("[ItemsEditor] Activated (F7)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            ReleaseCameraFocus();
            CancelPickerDrag();
            // If a RMB drag was in progress, snap the pickup back so closing
            // the editor never leaves a half-moved drop in the world.
            if (_movingInstance != null) CancelRmbMove();
            ClearAllSpriteTints();
            _hoveredInstance  = null;
            _selectedInstance = null;
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ItemsEditor] Deactivated (F7)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI build ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ItemsEditorCanvas", 108);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = ItemsEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           DoUndo,
                onRedo:           DoRedo,
                onModeSelect:     () => SetMode(EditorMode.Select),
                onModeSpawn:      () => SetMode(EditorMode.Spawn),
                onModeDelete:     () => SetMode(EditorMode.Delete),
                onAdd:            OnAddClicked,
                onRemove:         OnRemoveClicked,
                onAddOnSystem:    OnAddOnSystemClicked,
                onToggleTutorial: ToggleTutorial,
                onSearchChanged:  OnSearchChanged,
                onPerfToggle:     () => Toast("PERF overlay — not yet wired."));

            // Wire panel close (X button on the header) → keep dropdown state in sync
            WireOnClose(_uiRefs.ModesPanelDrag,     "modes");
            WireOnClose(_uiRefs.ItemsPanelDrag,     "items");
            WireOnClose(_uiRefs.PropsPanelDrag,     "props");
            WireOnClose(_uiRefs.InstancesPanelDrag, "instances");

            BuildTutorial();
            RefreshMenuBtnHighlights();
        }

        private void WireOnClose(DraggablePanel drag, string key)
        {
            if (drag == null) return;
            drag.OnClose = () =>
            {
                _openDropdowns.Remove(key);
                RefreshMenuBtnHighlights();
            };
        }

        // ── Tutorial overlay ──

        private void BuildTutorial()
        {
            _tutorial = TutorialOverlay.Build(_root.transform, "ITEMS HOTKEYS", new[]
            {
                ("F7",      "Toggle Items Editor"),
                ("LMB click", "Select / spawn / delete (per mode)"),
                ("LMB drag",  "Move a world drop"),
                ("MMB drag",  "Pan the camera"),
                ("WASD",    "Move the player"),
                ("Type",    "Filter items by name"),
                ("Ctrl+Z",  "Undo"),
                ("Ctrl+Y",  "Redo"),
                ("Esc",     "Cancel move / close editor"),
            });
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Mode handling ──

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            ItemsEditorUIBuilder.ApplyToolBtnStyle(_uiRefs.SelectBtnImg, mode == EditorMode.Select);
            ItemsEditorUIBuilder.ApplyToolBtnStyle(_uiRefs.SpawnBtnImg,  mode == EditorMode.Spawn);
            ItemsEditorUIBuilder.ApplyToolBtnStyle(_uiRefs.DeleteBtnImg, mode == EditorMode.Delete, danger: true);
            SetStatus(mode switch
            {
                EditorMode.Select => "Select: pick an item from the grid or click an instance.",
                EditorMode.Spawn  => string.IsNullOrEmpty(_selectedItemId)
                    ? "Spawn: pick an item first, then LMB on map. RMB on item icon = spawn at player."
                    : $"Spawn '{_selectedItemId}': LMB on map to drop. RMB on icon for player.",
                EditorMode.Delete => "Delete: LMB on a world drop to remove it.",
                _ => $"Mode: {mode}"
            });
        }

        // ── Dropdown management (mirrors BuildingsRuntimeEditor.UI.cs) ──

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "modes", "items", "props", "instances" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "modes"     => _uiRefs.ModesDropdown,
                "items"     => _uiRefs.ItemsDropdown,
                "props"     => _uiRefs.PropsDropdown,
                "instances" => _uiRefs.InstancesDropdown,
                _           => null
            };
            if (go != null) go.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            ItemsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ModesMenuBtnImg,     _uiRefs.ModesMenuBtnTmp,     _openDropdowns.Contains("modes"));
            ItemsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ItemsMenuBtnImg,     _uiRefs.ItemsMenuBtnTmp,     _openDropdowns.Contains("items"));
            ItemsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PropsMenuBtnImg,     _uiRefs.PropsMenuBtnTmp,     _openDropdowns.Contains("props"));
            ItemsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.InstancesMenuBtnImg, _uiRefs.InstancesMenuBtnTmp, _openDropdowns.Contains("instances"));
        }

        // ── Status / toast ──

        private void SetStatus(string msg)
        {
            if (_uiRefs.StatusText != null) _uiRefs.StatusText.text = msg;
        }

        private void Toast(string msg)
        {
            SetStatus(msg);
            Debug.Log($"[ItemsEditor] {msg}");
        }
    }
}
