using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Runtime in-game Buildings Editor (F10) — full feature parity with the Python
    /// roguelike_editors/buildings package.
    ///
    /// Covers all 10 migration gaps:
    ///   1. Hover cyan outline + active yellow outline + ID label (BuildingOutlineRenderer)
    ///   2. Mouse-wheel cycling between stacked buildings under the cursor
    ///   3. Add / Remove side panel (3 vertical buttons, left edge of left sidebar)
    ///   4. World-space E (delete) / D (reset) / R (resize) handles on the active building
    ///   5. Real Place: instantiates a BuildingObject via BuildingLoader root + Apply()
    ///   6. Real Save: writes StreamingAssets/Buildings/buildings_instances.json
    ///   7. Split-ratio slider + Z-bottom / Z-top –/+ controls in the right inspector
    ///   8. Collider scope CG/CU toggle + colliders-paint placeholder panel
    ///   9. 10-step interactive tutorial overlay with Prev / Next navigation
    ///  10. Confirm-delete modal with reference count (instances using same template)
    ///
    /// Mirrors Python buildings editor: building_editor_view.py + tools/* + panels/*.
    /// </summary>
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        // ── Inspector ──────────────────────────────────────────────────────────────

        [SerializeField, Tooltip("Building catalog asset (BuildingCatalog).")]
        private BuildingCatalog _catalog;

        // ── Constants matching Python (building_editor_view.py) ────────────────────

        private static readonly Color HOVER_CYAN           = new Color(0f, 1f, 1f, 1f);
        private static readonly Color HOVER_REMOVE_RED    = new Color(1f, 0f, 0f, 1f);
        private static readonly Color HOVER_REMOVE_FILL   = new Color(1f, 0f, 0f, 60f / 255f);
        private static readonly Color ACTIVE_YELLOW       = new Color(1f, 215f / 255f, 0f, 1f);
        // Orange outline shown on all buildings that share the same template as the
        // currently selected (active) building.
        private static readonly Color SAME_TEMPLATE_ORANGE = new Color(1f, 0.55f, 0f, 1f);
        private const float HOVER_THICKNESS_WORLD          = 0.06f;  // ~ 2 px @ PPU 32
        private const float ACTIVE_THICKNESS_WORLD         = 0.15f;  // ~ 5 px @ PPU 32
        private const float SAME_TEMPLATE_THICKNESS_WORLD  = 0.10f;  // ~ 3 px @ PPU 32

        // ── State ──────────────────────────────────────────────────────────────────

        private bool        _active;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        private enum EditorMode { Select, Place, Delete, Resize, Fill, Erase }
        private EditorMode  _mode = EditorMode.Select;
        private int         _selectedTemplateId = -1;

        /// <summary>
        /// Tracks which kind of entity the Properties panel is currently displaying.
        /// None      → no selection, shows idle hint text.
        /// Instance  → a placed BuildingObject is active (_activeBuilding != null).
        /// Template  → a picker slot was clicked; _activeBuilding is null/cleared.
        /// </summary>
        private enum PropertiesMode { None, Instance, Template }
        private PropertiesMode _propertiesMode = PropertiesMode.None;

        private BuildingObject _activeBuilding;
        private BuildingObject _hoveredBuilding;
        private readonly List<BuildingObject> _hoverStack = new List<BuildingObject>();
        private int _hoverIndex;
        private bool _removeMode;

        // Drag (move active with RMB)
        private bool    _dragging;
        private Vector3 _dragOffset;
        private Vector3 _dragStartWorldPos;

        // Drag-from-picker (LMB drag a slot from the Buildings panel to the map to
        // place it directly — mirrors Python building_picker_controller.start_drag).
        private bool          _pickerDragging;
        private int           _pickerDragTemplateId  = -1;
        private Vector2       _pickerDragStartScreen;
        private const float   PICKER_DRAG_THRESHOLD  = 8f; // pixels before drag activates
        // Drag preview: a UI Image rendered on the editor's Canvas (Overlay) so it
        // floats above EVERYTHING — the world map AND any panels, menus or HUD.
        // Sized to the building's actual world footprint scaled to the current camera
        // zoom, and tinted a vivid color so it's immediately obvious the user is
        // dragging a building (not a generic faded thumbnail).
        private GameObject    _dragGhostGo;
        private RectTransform _dragGhostRt;
        private Image         _dragGhostImg;
        private Image         _dragGhostOutline;            // bright outline border
        private const float   BUILDING_PPU = 32f;           // matches BUILDING_PPU in importer
        // Cyan-leaning tint that stays visibly distinct while remaining a preview.
        private static readonly Color DRAG_GHOST_TINT     = new Color(0.55f, 1f, 1f, 0.70f);
        private static readonly Color DRAG_GHOST_OUTLINE  = new Color(1f, 0.85f, 0.10f, 0.95f); // golden ring
        private const float           DRAG_GHOST_BORDER   = 10f; // px — border thickness

        // Resize (drag with R-handle)
        private bool       _resizing;
        private Vector3    _resizeStartMouse;
        private Vector2Int _resizeStartScale;

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly Valkur.Gameplay.Editors.EditorCameraPanController _cameraPan
            = new Valkur.Gameplay.Editors.EditorCameraPanController();
        private Camera  _mainCamera;

        // Outline renderers (cyan hover + yellow active + red remove)
        private BuildingOutlineRenderer _hoverFx;
        private BuildingOutlineRenderer _activeFx;

        // Orange outlines for buildings that share the same template as the active one.
        // Pooled so we never allocate per-frame; rebuilt whenever the active building changes.
        private readonly List<BuildingOutlineRenderer> _sameTemplateFxPool     = new List<BuildingOutlineRenderer>();
        private readonly List<BuildingObject>          _sameTemplateBuildings  = new List<BuildingObject>();

        // Collider-brush hover cursor (cyan, matches TileEditorGridCursor style)
        private GameObject     _collBrushCursorGo;
        private LineRenderer   _collBrushCursorLine;
        private SpriteRenderer _collBrushCursorFill;
        private Material       _collBrushCursorMat;
        private static readonly Color CollBrushCursorColor    = new Color(0f, 0.863f, 1f, 0.85f);
        private const  float          CollBrushCursorFillAlpha = 0.235f;
        private const  float          CollBrushCursorLineWidth  = 0.06f;

        // ── UI ─────────────────────────────────────────────────────────────────────

        private bool _uiBuilt;
        private Canvas _canvas;
        private GameObject _root;
        private BuildingsEditorUIBuilder.UIRefs _uiRefs;
        private readonly System.Collections.Generic.HashSet<string> _openDropdowns =
            new System.Collections.Generic.HashSet<string>();

        // Mapped from _uiRefs after BuildUI — kept for backward-compatible downstream logic
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private TextMeshProUGUI _idLabelTmp;     // floating "ID n" near active building
        private RectTransform   _idLabelRt;

        // Split-ratio horizontal line drawn over the active building (mirrors Python split_tool_view.py)
        private RectTransform _splitLineRt;
        private Image         _splitLineImg;
        // Drag handle (small square at center of split line)
        private RectTransform _splitHandleRt;
        private Image         _splitHandleImg;

        // Split-ratio drag state
        private bool  _splitDragging;
        private bool  _splitHovering;      // cursor is near the split line (hover highlight)
        private float _splitDragStartRatio;        // ratio when drag began (for undo)
        private const float SPLIT_HANDLE_WORLD_RADIUS = 0.5f;  // world-units pick radius (~16 px at PPU=32)

        private Image _selectBtnImg, _placeBtnImg, _deleteBtnImg, _resizeBtnImg;
        private Image _addBtnImg, _removeBtnImg;
        private Image _fillBtnImg;   // Fill button in the Tools panel
        private Image _eraseBtnImg;  // Erase button in the Tools panel

        // Perf probe (PERF button in menu bar, Shift+PERF to toggle)
        private BuildingsPerfProbe _perfProbe;
        private bool _buildingsVisible = true;
        private TMP_InputField _searchBox;
        private string _searchFilter = "";

        // Inspector controls (Properties panel) — built once, refreshed per active building
        private GameObject _inspectorRoot;
        private Slider _splitSlider;
        private TextMeshProUGUI _zBottomVal, _zTopVal;
        private TextMeshProUGUI _gridColsVal, _gridRowsVal;
        private TextMeshProUGUI _scopeBtnLabel;
        private Image _scopeBtnImg;

        // Floating world-space handle (R) — overlay positioned each frame at top-right of active building.
        // Delete (E) and Reset (D) moved to the Properties inspector panel.
        private GameObject _handlesRoot;
        private Button _handleR;
        private bool   _pendingResizeStart;

        // Floating Z selector badges — top and bottom of active building
        private RectTransform   _zTopBadgeRt;
        private RectTransform   _zBotBadgeRt;
        private TextMeshProUGUI _zTopBadgeTmp;
        private TextMeshProUGUI _zBotBadgeTmp;

        // Tutorial (10-step interactive)
        private GameObject _tutorialRoot;
        private TextMeshProUGUI _tutorialStepLabel, _tutorialBodyTmp;
        private int _tutorialStep;
        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1. Open editor",   "Press F10 anywhere in-game to toggle the Buildings Editor."),
            ("2. Pick template", "In the left picker, click a building thumbnail to select it. Use the search box to filter by ID or asset path."),
            ("3. Place a building", "DRAG a building thumbnail from the Buildings panel and DROP it on the map. Click-to-place is disabled — the only way to place a building is to drag it from the panel."),
            ("4. Hover & select",  "Move the mouse over a building — it outlines in CYAN. Use the mouse wheel to cycle through stacked buildings. Click to select (outline turns YELLOW)."),
            ("5. Move & resize",   "RMB-drag the active building to move it. Drag the R handle (top-right of the building) with LMB to resize proportionally."),
            ("6. Inspector edits", "On the right panel, drag the Split slider, change Z-Bottom / Z-Top with –/+, or toggle Collider Scope between CG (shared) and CU (per-instance)."),
            ("7. Remove mode",   "Click the Remove (–) button to enable remove mode — buildings highlight RED on hover. Click to delete."),
            ("8. Delete handle", "Or click the red E handle on the active building to delete it (a confirmation modal appears with reference count)."),
            ("9. Undo / Redo",   "Use the toolbar Undo / Redo buttons to revert or replay the last edits (capacity 64)."),
           ("10. Save",          "Click Save to write StreamingAssets/Buildings/buildings_instances.json. Press F10 again to close the editor."),
        };

        // Confirm-delete modal
        private GameObject _confirmModal;
        private TextMeshProUGUI _confirmText;
        private System.Action _pendingConfirmYes;

        // Undo/redo
        private readonly UndoStack _undo = new UndoStack(64);
        private bool _hasUnsavedInstanceChanges;
        private bool _isPersistingInstanceChanges;

        // Cached BuildingLoader for spawn-root + ref counting
        private BuildingLoader _buildingLoader;
        private Transform      _buildingsRoot;

        // ── Fill tool state ────────────────────────────────────────────────────────

        /// <summary>Sub-steps of the Fill flow (Idle → AwaitingSpacing → AwaitingTemplate → AwaitingTile).</summary>
        private enum FillStep { Idle, AwaitingSpacing, AwaitingTemplate, AwaitingTile }
        private FillStep _fillStep = FillStep.Idle;
        private int      _fillSpacingTiles = 2;
        private int      _fillTemplateId   = -1;
        private UnityEngine.Tilemaps.TileBase  _fillSampleTile;
        private Vector3Int                     _fillSampleCell;
        private readonly HashSet<Vector3Int>   _fillCandidateCells = new HashSet<Vector3Int>();
        private BuildingsFillPreviewOverlay    _fillOverlay;
        private Coroutine                      _fillPickerBlinkCoroutine;
        private GameObject                     _fillSpacingModal;
        private TMP_InputField                 _fillSpacingInput;
        // Header Image of the Buildings panel — pulsed during AwaitingTemplate.
        private Image                          _buildingsPanelHeaderImg;
        // World tilemap for Ground-tile sampling.
        private UnityEngine.Tilemaps.Tilemap   _worldGroundTilemap;

        // New: placement mode + size variance + session seed
        private enum FillPlacementMode { Uniform, Groves, Noise }
        private FillPlacementMode _fillPlacementMode = FillPlacementMode.Uniform;

        private bool  _fillRandomSize   = false;
        private int   _fillSizeMinPct   = 80;
        private int   _fillSizeMaxPct   = 120;

        private int   _fillGroveCount   = 3;
        private int   _fillGroveSpread  = 6;
        private float _fillNoiseScale   = 0.20f;
        private float _fillNoiseThreshold = 0.40f;

        private int   _fillSessionSeed  = 0;
        // Per-cell scale-factor hints from Groves mode (key = cell, value = 0..1, 1 = cluster center).
        // Null when not using Groves+RandomSize correlation.
        private System.Collections.Generic.Dictionary<Vector3Int, float> _fillSizeHintsByCell;

        // New UI references for the expanded dialog
        private TMP_InputField _fillSizeMinInput;
        private TMP_InputField _fillSizeMaxInput;
        private Image          _fillRandomSizeCheckImg;   // for the toggle visual
        private TMPro.TextMeshProUGUI _fillRandomSizeCheckText;
        private Image          _fillModeUniformBtnImg;
        private Image          _fillModeGrovesBtnImg;
        private Image          _fillModeNoiseBtnImg;
        private TMP_InputField _fillGroveCountInput;
        private TMP_InputField _fillGroveSpreadInput;
        private TMP_InputField _fillNoiseScaleInput;
        private TMP_InputField _fillNoiseThresholdInput;

        // ── Erase tool state ───────────────────────────────────────────────────────

        /// <summary>Sub-steps of the Erase flow.</summary>
        private enum EraseStep { Idle, AwaitingScope, AwaitingTarget, AwaitingConfirm }
        /// <summary>Scope chosen for Erase: by Tiles Area (flood-fill region) or by Zone.</summary>
        private enum EraseScope { TilesArea, Zone }

        private EraseStep  _eraseStep  = EraseStep.Idle;
        private EraseScope _eraseScope = EraseScope.Zone;
        private GameObject _eraseSubPanel;
        private Image      _eraseTilesAreaBtnImg;
        private Image      _eraseZoneBtnImg;
        private readonly List<BuildingObject>      _eraseMatches      = new List<BuildingObject>();
        private readonly HashSet<Vector3Int>       _eraseAreaCells    = new HashSet<Vector3Int>();
        private int        _eraseTemplateId = -1;
        private string     _eraseZoneId;
        private GameObject _eraseConfirmModal;
        private TextMeshProUGUI _eraseConfirmText;
        private System.Action   _eraseConfirmYes;
        // Pool of orange outlines highlighting the matches before confirmation.
        // Kept separate from _sameTemplateFxPool so the two highlight states never collide.
        private readonly List<BuildingOutlineRenderer> _eraseMatchFxPool = new List<BuildingOutlineRenderer>();

        // ── HUD hide-on-open state ─────────────────────────────────────────────────
        // Capture the active-state of each HUD when the editor opens so we can
        // restore exactly what was visible before (and not forcibly show a HUD
        // that was already hidden by the player).
        private bool _hudSpellBarWasActive;
        private bool _hudInventoryWasActive;
        private bool _hudMusicPlayerWasActive;
        private GameObject _hudMusicPlayerGo;

        // ── IGameEditor ────────────────────────────────────────────────────────────

        public string EditorName => "Buildings Editor";
        public bool IsActive => _active;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleBuildings, out _ownsToggleAction);
        }

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (_collBrushCursorMat != null) Destroy(_collBrushCursorMat);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleBuildings))
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;

            HandleKeyboardShortcuts();
            HandleCameraPan();
            HandleMapInteraction();
            UpdateCollBrushCursor();
            UpdatePickerDrag();
            UpdateOutlineState();
            UpdateFloatingHandles();
            UpdateIdLabel();
            UpdateZBadges();
            UpdateSplitLine();
            // Per-frame overlay refresh: only the ACTIVE building's geometry
            // can change live (drag, resize, split-ratio). All other buildings
            // are static while the editor is open, so a full RefreshCollidersOverlay()
            // every frame (FindObjectsOfType + ResampleGrid clones + per-overlay
            // dirty mark) was the dominant FPS cost when Show Colliders is on
            // (~20 fps with 142 buildings). Touching only the active overlay
            // cuts per-frame cost from O(N · cells) to O(cells_active).
            // Full refreshes still happen on toggle, on SetActiveBuilding, on
            // brush stroke end, on undo/redo, and on any structural change.
            if (_collidersVisible && _openDropdowns.Contains("colliders"))
                RefreshActiveBuildingOverlayCells();
        }

    }
}
