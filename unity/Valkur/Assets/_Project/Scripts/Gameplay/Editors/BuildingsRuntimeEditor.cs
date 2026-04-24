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
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
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
    public class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Inspector ──────────────────────────────────────────────────────────────

        [SerializeField, Tooltip("Building catalog asset (BuildingCatalog).")]
        private BuildingCatalog _catalog;

        // ── Constants matching Python (building_editor_view.py) ────────────────────

        private static readonly Color HOVER_CYAN          = new Color(0f, 1f, 1f, 1f);
        private static readonly Color HOVER_REMOVE_RED    = new Color(1f, 0f, 0f, 1f);
        private static readonly Color HOVER_REMOVE_FILL   = new Color(1f, 0f, 0f, 60f / 255f);
        private static readonly Color ACTIVE_YELLOW       = new Color(1f, 215f / 255f, 0f, 1f);
        private const float HOVER_THICKNESS_WORLD         = 0.06f;  // ~ 2 px @ PPU 32
        private const float ACTIVE_THICKNESS_WORLD        = 0.15f;  // ~ 5 px @ PPU 32

        // ── State ──────────────────────────────────────────────────────────────────

        private bool        _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Place, Delete, Resize }
        private EditorMode  _mode = EditorMode.Select;
        private int         _selectedTemplateId = -1;

        private BuildingObject _activeBuilding;
        private BuildingObject _hoveredBuilding;
        private readonly List<BuildingObject> _hoverStack = new List<BuildingObject>();
        private int _hoverIndex;
        private bool _removeMode;

        // Drag (move active with RMB)
        private bool    _dragging;
        private Vector3 _dragOffset;

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
        // Cyan-leaning, additive-feeling tint with high alpha so it pops over both the
        // map and any UI panels. Pure white was reading as a dull "shadow" before.
        private static readonly Color DRAG_GHOST_TINT     = new Color(0.55f, 1f, 1f, 0.85f);
        private static readonly Color DRAG_GHOST_OUTLINE  = new Color(1f, 0.85f, 0.10f, 0.95f); // golden ring

        // Resize (drag with R-handle)
        private bool       _resizing;
        private Vector3    _resizeStartMouse;
        private Vector2Int _resizeStartScale;

        // Middle-mouse camera pan (mirrors Python camera_pan.py / TileEditor behaviour)
        private bool    _isPanning;
        private Vector2 _panAnchorScreenPos;
        private Vector3 _panAnchorCamPos;
        private Camera  _mainCamera;

        // Outline renderers (cyan hover + yellow active + red remove)
        private BuildingOutlineRenderer _hoverFx;
        private BuildingOutlineRenderer _activeFx;

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

        // Perf probe (PERF button in menu bar, Shift+PERF to toggle)
        private BuildingsPerfProbe _perfProbe;
        private TMP_InputField _searchBox;
        private string _searchFilter = "";

        // Inspector controls (Properties panel) — built once, refreshed per active building
        private GameObject _inspectorRoot;
        private Slider _splitSlider;
        private TextMeshProUGUI _zBottomVal, _zTopVal;
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

        // Cached BuildingLoader for spawn-root + ref counting
        private BuildingLoader _buildingLoader;
        private Transform      _buildingsRoot;

        // ── IGameEditor ────────────────────────────────────────────────────────────

        public string EditorName => "Buildings Editor";
        public bool IsActive => _active;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleBuildingsEditor", InputActionType.Button, "<Keyboard>/f10");
        }

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
            _toggleAction.Enable();
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Dispose();
            if (_collBrushCursorMat != null) Destroy(_collBrushCursorMat);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (_toggleAction.WasPerformedThisFrame())
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

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[BuildingsEditor] BuildUI failed: {ex.Message}\n{ex.StackTrace}");
                    CleanupUI();
                    return;
                }
            }
            EnsureRuntimeFx();
            CacheBuildingLoader();
            _active = true;
            _canvas.gameObject.SetActive(true);
            _canvas.enabled = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            RefreshInspector();
            if (_statusTmp != null)
                _statusTmp.text = "Buildings Editor active. F10 = close. ESC = cancel.";
            _mainCamera = Camera.main;
            if (Valkur.Gameplay.CameraSetup.Instance != null)
                Valkur.Gameplay.CameraSetup.Instance.DetachFollow();
            Debug.Log("[BuildingsEditor] Activated (F10)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_uiBuilt && _root != null)
            {
                _root.SetActive(false);
                if (_canvas != null) { _canvas.enabled = false; _canvas.gameObject.SetActive(false); }
            }
            HideOutlines();
            _selectedTemplateId = -1;
            _activeBuilding = null;
            _hoveredBuilding = null;
            _hoverStack.Clear();
            _dragging = false;
            _resizing = false;
            _splitDragging = false;
            _splitHovering = false;
            _removeMode = false;
            _collBrushMode = CollBrushMode.Off;
            _activeColliderSession = null;
            _colliderStroke.Active = false;
            _isPanning = false;
            HideCollBrushCursor();
            CancelPickerDrag();
            HideConfirm();
            if (Valkur.Gameplay.CameraSetup.Instance != null)
                Valkur.Gameplay.CameraSetup.Instance.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[BuildingsEditor] Deactivated (F10)");
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        private void CleanupUI()
        {
            if (_root != null)   { Destroy(_root);   _root = null; }
            if (_canvas != null) { Destroy(_canvas.gameObject); _canvas = null; }
            _uiRefs = default;
            _openDropdowns.Clear();
            _pickerContent = null; _statusTmp = null; _propsTmp = null;
            _selectBtnImg = _placeBtnImg = _deleteBtnImg = _resizeBtnImg = null;
            _addBtnImg = _removeBtnImg = null;
            _searchBox = null;
            _inspectorRoot = null; _splitSlider = null;
            _zBottomVal = _zTopVal = null;
            _scopeBtnLabel = null; _scopeBtnImg = null;
            _handlesRoot = null; _handleR = null;
            _zTopBadgeRt = null; _zBotBadgeRt = null;
            _zTopBadgeTmp = null; _zBotBadgeTmp = null;
            _tutorialRoot = null; _tutorialStepLabel = _tutorialBodyTmp = null;
            _confirmModal = null; _confirmText = null;
            _idLabelTmp = null; _idLabelRt = null;
            _splitLineRt = null; _splitLineImg = null;
            _splitHandleRt = null; _splitHandleImg = null;
            _dragGhostGo = null; _dragGhostRt = null; _dragGhostImg = null; _dragGhostOutline = null;
            _pickerDragging = false; _pickerDragTemplateId = -1;
            _uiBuilt = false;
        }

        private void EnsureRuntimeFx()
        {
            if (_hoverFx == null)
            {
                var go = new GameObject("BuildingsEditor.HoverFx");
                go.transform.SetParent(transform, false);
                _hoverFx = go.AddComponent<BuildingOutlineRenderer>();
                _hoverFx.Configure(HOVER_CYAN, HOVER_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
            }
            if (_activeFx == null)
            {
                var go = new GameObject("BuildingsEditor.ActiveFx");
                go.transform.SetParent(transform, false);
                _activeFx = go.AddComponent<BuildingOutlineRenderer>();
                _activeFx.Configure(ACTIVE_YELLOW, ACTIVE_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
            }
        }

        private void HideOutlines()
        {
            if (_hoverFx  != null) { _hoverFx.Follow(null);  _hoverFx.SetVisible(false); }
            if (_activeFx != null) { _activeFx.Follow(null); _activeFx.SetVisible(false); }
            if (_idLabelRt  != null) _idLabelRt.gameObject.SetActive(false);
            if (_handlesRoot != null) _handlesRoot.SetActive(false);
            if (_zTopBadgeRt != null) _zTopBadgeRt.gameObject.SetActive(false);
            if (_zBotBadgeRt != null) _zBotBadgeRt.gameObject.SetActive(false);
            if (_splitLineRt   != null) _splitLineRt.gameObject.SetActive(false);
            if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
        }

        // ── Collider-brush hover cursor ───────────────────────────────────────────

        private void EnsureCollBrushCursor()
        {
            if (_collBrushCursorGo != null) return;

            _collBrushCursorMat = new Material(
                Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"))
                { hideFlags = HideFlags.HideAndDontSave };

            _collBrushCursorGo = new GameObject("BuildingsEditor.CollBrushCursor");
            _collBrushCursorGo.transform.SetParent(transform, false);

            var lr = _collBrushCursorGo.AddComponent<LineRenderer>();
            lr.useWorldSpace   = true;
            lr.loop            = true;
            lr.positionCount   = 4;
            lr.startWidth      = CollBrushCursorLineWidth;
            lr.endWidth        = CollBrushCursorLineWidth;
            lr.sortingOrder    = 998;
            lr.sharedMaterial  = _collBrushCursorMat;
            lr.startColor      = CollBrushCursorColor;
            lr.endColor        = CollBrushCursorColor;
            _collBrushCursorLine = lr;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_collBrushCursorGo.transform, false);
            _collBrushCursorFill              = fillGo.AddComponent<SpriteRenderer>();
            _collBrushCursorFill.sortingOrder  = 997;
            var fillColor = CollBrushCursorColor;
            fillColor.a   = CollBrushCursorFillAlpha;
            _collBrushCursorFill.color  = fillColor;
            _collBrushCursorFill.sprite = CreateCursorSprite();
            _collBrushCursorGo.SetActive(false);
        }

        private void HideCollBrushCursor()
        {
            if (_collBrushCursorGo != null) _collBrushCursorGo.SetActive(false);
        }

        private void UpdateCollBrushCursor()
        {
            if (!BrushOn || _activeBuilding == null)
            {
                HideCollBrushCursor();
                return;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) { HideCollBrushCursor(); return; }

            var cam = Camera.main;
            if (cam == null) { HideCollBrushCursor(); return; }

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { HideCollBrushCursor(); return; }

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos  = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
            worldPos.z = 0f;

            if (!rect.Contains(worldPos)) { HideCollBrushCursor(); return; }

            var session = EnsureActiveColliderSession();
            if (session?.WorkingGrid == null || session.WorkingGrid.width <= 0 || session.WorkingGrid.height <= 0)
            {
                HideCollBrushCursor();
                return;
            }

            int gridW = session.WorkingGrid.width;
            int gridH = session.WorkingGrid.height;
            float cellW = rect.width  / gridW;
            float cellH = rect.height / gridH;

            float u = Mathf.Clamp01((worldPos.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((worldPos.y - rect.yMin) / rect.height);
            int col = Mathf.Clamp(Mathf.FloorToInt(u * gridW), 0, gridW - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((1f - v) * gridH), 0, gridH - 1);

            // Cursor is centred on the hit cell and covers the full brush footprint.
            float cx = rect.xMin + (col + 0.5f) * cellW;
            float cy = rect.yMax - (row + 0.5f) * cellH;  // row 0 = top of building
            float halfW = _collBrushSize * cellW * 0.5f;
            float halfH = _collBrushSize * cellH * 0.5f;
            var center  = new Vector3(cx, cy, 0f);

            EnsureCollBrushCursor();
            _collBrushCursorGo.SetActive(true);

            // Border
            _collBrushCursorLine.SetPosition(0, center + new Vector3(-halfW, -halfH));
            _collBrushCursorLine.SetPosition(1, center + new Vector3( halfW, -halfH));
            _collBrushCursorLine.SetPosition(2, center + new Vector3( halfW,  halfH));
            _collBrushCursorLine.SetPosition(3, center + new Vector3(-halfW,  halfH));

            // Fill
            _collBrushCursorFill.transform.position   = center;
            _collBrushCursorFill.transform.localScale  = new Vector3(halfW * 2f, halfH * 2f, 1f);
        }

        private static Sprite CreateCursorSprite()
        {
            var tex    = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        private void CacheBuildingLoader()
        {
            if (_buildingLoader != null && _buildingsRoot != null) return;
            _buildingLoader = FindObjectOfType<BuildingLoader>();
            if (_buildingLoader != null)
            {
                var f = typeof(BuildingLoader).GetField("_buildingsRoot",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _buildingsRoot = f?.GetValue(_buildingLoader) as Transform;
            }
            // Fallback: spawn under our own transform
            if (_buildingsRoot == null) _buildingsRoot = transform;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  UI BUILD
        // ──────────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("BuildingsEditorCanvas", 109);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = BuildingsEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle:  ToggleDropdown,
                onUndo:            () => _undo.Undo(),
                onRedo:            () => _undo.Redo(),
                onSave:            () => SaveInstancesToJson(),
                onReload:          () => ReloadFromJson(),
                onModeSelect:      () => SetMode(EditorMode.Select),
                onModePlace:       () => SetMode(EditorMode.Place),
                onModeResize:      () => SetMode(EditorMode.Resize),
                onModeDelete:      () => SetMode(EditorMode.Delete),
                onAddBuilding:     () => OnAddBuildingClicked(),
                onRemoveBuilding:  () => ToggleRemoveMode(),
                onAddOnSystem:     () => OnAddOnSystemClicked(),
                onToggleTutorial:  () => ToggleTutorial(),
                onSearchChanged:   v  => { _searchFilter = v ?? ""; RefreshPicker(); },
                onSplitChanged:    f  => OnSplitSliderChanged(f),
                onZBottomMinus:    () => AdjustZ(_activeBuilding, bottom: true,  delta: -1),
                onZBottomPlus:     () => AdjustZ(_activeBuilding, bottom: true,  delta: +1),
                onZTopMinus:       () => AdjustZ(_activeBuilding, bottom: false, delta: -1),
                onZTopPlus:        () => AdjustZ(_activeBuilding, bottom: false, delta: +1),
                onColliderScope:   () => ToggleColliderScope(),
                onPaintSolid:      () => SetCollBrushMode(CollBrushMode.Solid),
                onPaintWalk:       () => SetCollBrushMode(CollBrushMode.Walk),
                onSaveCU:          () => SaveColliderAuthoring(),
                onDeleteBuilding:  () => RequestDeleteActiveWithConfirm(),
                onResetBuilding:   () => ResetActiveBuilding(),
                // Colliders panel callbacks (redesigned: ON/OFF + #/. action + scope)
                onToggleCollidersVisible: () => ToggleCollidersVisible(),
                onCollScopeToggle:        () => ToggleColliderScope(),
                onBrushPaint:                () => SetBrushAction(CollBrushMode.Solid),
                onBrushErase:                () => SetBrushAction(CollBrushMode.Walk),
                onCollBrushSizeChanged:      v  => OnCollBrushSizeChanged(v),
                onCollBrushSizeStepDown:     () => OnCollBrushSizeChanged(_collBrushSize - 1),
                onCollBrushSizeStepUp:       () => OnCollBrushSizeChanged(_collBrushSize + 1),
                onPerfToggle:                () => TogglePerfProbe());

            // Wire panel close callbacks to keep dropdown state in sync
            if (_uiRefs.ModesPanelDrag     != null)
                _uiRefs.ModesPanelDrag.OnClose     = () => { _openDropdowns.Remove("modes");     RefreshMenuBtnHighlights(); };
            if (_uiRefs.BuildingsPanelDrag != null)
                _uiRefs.BuildingsPanelDrag.OnClose = () => { _openDropdowns.Remove("buildings"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.CollidersPanelDrag != null)
                _uiRefs.CollidersPanelDrag.OnClose = () => { _openDropdowns.Remove("colliders"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.PropsPanelDrag     != null)
                _uiRefs.PropsPanelDrag.OnClose     = () => { _openDropdowns.Remove("props");     RefreshMenuBtnHighlights(); };

            // Map builder refs to private fields so all downstream logic is unchanged
            _pickerContent = _uiRefs.PickerContent;
            _statusTmp     = _uiRefs.StatusText;
            _searchBox     = _uiRefs.SearchBox;
            _propsTmp      = _uiRefs.PropsText;
            _inspectorRoot = _uiRefs.InspectorRoot;
            _splitSlider   = _uiRefs.SplitSlider;
            _zBottomVal    = _uiRefs.ZBottomVal;
            _zTopVal       = _uiRefs.ZTopVal;
            _scopeBtnImg   = _uiRefs.ScopeBtnImg;
            _scopeBtnLabel = _uiRefs.ScopeBtnLabel;
            _selectBtnImg  = _uiRefs.SelectBtnImg;
            _placeBtnImg   = _uiRefs.PlaceBtnImg;
            _resizeBtnImg  = _uiRefs.ResizeBtnImg;
            _deleteBtnImg  = _uiRefs.DeleteBtnImg;
            _addBtnImg     = _uiRefs.AddBtnImg;
            _removeBtnImg  = _uiRefs.RemoveBtnImg;

            BuildFloatingHandles();
            BuildIdLabel();
            BuildZBadges();
            BuildSplitLine();
            BuildTutorial();
            BuildConfirmModal();
            CreatePerfProbe();

            OpenAllPanels();
            RefreshBrushButtonHighlights();
            RefreshCollidersPanel();
        }

        // ── Dropdown / panel management ────────────────────────────────────────────

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
            foreach (var n in new[] { "modes", "buildings", "colliders", "props" })
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
                "buildings" => _uiRefs.BuildingsDropdown,
                "colliders" => _uiRefs.CollidersDropdown,
                "props"     => _uiRefs.PropsDropdown,
                _           => null
            };
            go?.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ModesMenuBtnImg,     _uiRefs.ModesMenuBtnTmp,     _openDropdowns.Contains("modes"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.BuildingsMenuBtnImg, _uiRefs.BuildingsMenuBtnTmp, _openDropdowns.Contains("buildings"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.CollidersMenuBtnImg, _uiRefs.CollidersMenuBtnTmp, _openDropdowns.Contains("colliders"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PropsMenuBtnImg,     _uiRefs.PropsMenuBtnTmp,     _openDropdowns.Contains("props"));
        }

        /// <summary>
        /// Floating overlay handle: only R (resize) remains — floats at the top-right of the
        /// active building. Delete and Reset have been moved to the Properties inspector panel.
        /// LMB-press+drag on the R handle resizes the building proportionally.
        /// </summary>
        private void BuildFloatingHandles()
        {
            // Container: pivot = (1,1) → top-right of badge anchors to building top-right corner,
            // so the badge sits inside the yellow selection frame at the top-right corner.
            _handlesRoot = EditorUIHelpers.CreateUI("FloatingHandles", _root.transform);
            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(1f, 0f);  // bottom-right → sits ABOVE frame at top-right
            rt.sizeDelta = new Vector2(32f, 32f); // updated proportionally each frame

            // Badge button: dark semi-transparent background + gold Outline (matches selection frame)
            var btnGo = EditorUIHelpers.CreateUI("BtnR", _handlesRoot.transform);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = Vector2.zero;
            btnRt.anchorMax = Vector2.one;
            btnRt.offsetMin = btnRt.offsetMax = Vector2.zero;

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.10f, 0.10f, 0.14f, 0.92f);

            _handleR = btnGo.AddComponent<Button>();
            var colors = _handleR.colors;
            colors.normalColor      = new Color(0.10f, 0.10f, 0.14f, 0.92f);
            colors.highlightedColor = new Color(0.90f, 0.76f, 0.38f, 0.22f); // gold hover glow
            colors.pressedColor     = EditorUIHelpers.BTN_ACTIVE;             // gold on press
            colors.selectedColor    = new Color(0.10f, 0.10f, 0.14f, 0.92f);
            colors.fadeDuration     = 0.08f;
            _handleR.colors = colors;
            _handleR.targetGraphic = img;

            // Gold border — visually ties the badge to the yellow selection outline
            var ol = btnGo.AddComponent<Outline>();
            ol.effectColor    = new Color(0.90f, 0.76f, 0.38f, 0.85f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            // "R" label in bold ACCENT gold, auto-sized to fit the badge
            var labelGo = EditorUIHelpers.CreateUI("Lbl", btnGo.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text             = "R";
            tmp.fontStyle        = FontStyles.Bold;
            tmp.color            = EditorUIHelpers.ACCENT;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin      = 8f;
            tmp.fontSizeMax      = 18f;
            tmp.overflowMode     = TextOverflowModes.Overflow;

            // EventTrigger: PointerDown starts the resize drag immediately (onClick fires on
            // release, which is too late for drag-distance tracking).
            var trigger = btnGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry   = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown
            };
            entry.callback.AddListener(_ =>
            {
                if (_activeBuilding != null)
                    _pendingResizeStart = true;
            });
            trigger.triggers.Add(entry);

            _handlesRoot.SetActive(false);
        }

        /// <summary>
        /// Horizontal cyan bar drawn at the split-ratio cut point of the active building.
        /// Mirrors Python split_tool_view.py: 3 px bar + centered draggable handle.
        /// The handle (10×10 square) can be dragged vertically to change split ratio.
        /// </summary>
        private void BuildSplitLine()
        {
            // Bar — 3 px high, width updated each frame
            var go = EditorUIHelpers.CreateUI("SplitLine", _root.transform);
            _splitLineRt = go.GetComponent<RectTransform>();
            _splitLineRt.anchorMin = _splitLineRt.anchorMax = new Vector2(0.5f, 0.5f);
            _splitLineRt.pivot = new Vector2(0.5f, 0.5f);
            _splitLineRt.sizeDelta = new Vector2(80f, 3f);  // width updated each frame
            _splitLineImg = go.AddComponent<Image>();
            _splitLineImg.color = new Color(0f, 200f / 255f, 1f, 0.85f); // cyan #00C8FF
            go.SetActive(false);

            // Handle — 24×8 wide bar at center; wider shape suggests horizontal draggability
            var hgo = EditorUIHelpers.CreateUI("SplitHandle", _root.transform);
            _splitHandleRt = hgo.GetComponent<RectTransform>();
            _splitHandleRt.anchorMin = _splitHandleRt.anchorMax = new Vector2(0.5f, 0.5f);
            _splitHandleRt.pivot = new Vector2(0.5f, 0.5f);
            _splitHandleRt.sizeDelta = new Vector2(24f, 8f);
            _splitHandleImg = hgo.AddComponent<Image>();
            _splitHandleImg.color = new Color(0f, 200f / 255f, 1f, 1f); // solid cyan
            hgo.SetActive(false);
        }

        private void BuildIdLabel()
        {
            var go = EditorUIHelpers.CreateUI("IdLabel", _root.transform);
            _idLabelRt = go.GetComponent<RectTransform>();
            _idLabelRt.anchorMin = _idLabelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _idLabelRt.pivot = new Vector2(0f, 0f);  // bottom-left anchor → sits ABOVE the frame top edge
            _idLabelRt.sizeDelta = new Vector2(80f, 20f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            var labelGo = EditorUIHelpers.CreateUI("Text", go.transform);
            EditorUIHelpers.StretchFill(labelGo);
            _idLabelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            _idLabelTmp.text = "ID -";
            _idLabelTmp.fontSize = 13f;
            _idLabelTmp.fontStyle = FontStyles.Bold;
            _idLabelTmp.alignment = TextAlignmentOptions.Center;
            _idLabelTmp.color = ACTIVE_YELLOW;
            go.SetActive(false);
        }

        private void BuildZBadges()
        {
            _zTopBadgeRt = BuildZBadge("ZTopBadge",
                () => AdjustZ(_activeBuilding, bottom: false, delta: -1),
                () => AdjustZ(_activeBuilding, bottom: false, delta: +1),
                out _zTopBadgeTmp);
            _zBotBadgeRt = BuildZBadge("ZBotBadge",
                () => AdjustZ(_activeBuilding, bottom: true, delta: -1),
                () => AdjustZ(_activeBuilding, bottom: true, delta: +1),
                out _zBotBadgeTmp);
        }

        private RectTransform BuildZBadge(string name, Action onMinus, Action onPlus,
            out TextMeshProUGUI valueTmp)
        {
            var go = EditorUIHelpers.CreateUI(name, _root.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100f, 22f);  // updated each frame in UpdateZBadges

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.07f, 0.10f, 0.88f);

            var ol = go.AddComponent<Outline>();
            ol.effectColor    = new Color(0.90f, 0.76f, 0.38f, 0.50f); // gold matches selection frame
            ol.effectDistance = new Vector2(1f, -1f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset(2, 2, 2, 2);
            hlg.spacing               = 1f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleCenter;

            // [−] button
            var minusGo  = EditorUIHelpers.CreateUI("Minus", go.transform);
            minusGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var minusImg = minusGo.AddComponent<Image>();
            minusImg.color = EditorUIHelpers.BTN_NORMAL;
            var minusBtn = minusGo.AddComponent<Button>();
            var mc = minusBtn.colors;
            mc.normalColor = EditorUIHelpers.BTN_NORMAL; mc.highlightedColor = EditorUIHelpers.BTN_HOVER;
            mc.pressedColor = EditorUIHelpers.BTN_ACTIVE; mc.fadeDuration = 0.08f;
            minusBtn.colors = mc; minusBtn.targetGraphic = minusImg;
            minusBtn.onClick.AddListener(() => { if (_activeBuilding != null) onMinus(); });
            EditorUIHelpers.AddCenteredText(minusGo.transform, "\u2212", 12f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);

            // Z: N label
            var valGo = EditorUIHelpers.CreateUI("Val", go.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            valueTmp           = valGo.AddComponent<TextMeshProUGUI>();
            valueTmp.text      = "Z: 0";
            valueTmp.fontSize  = 10f;
            valueTmp.fontStyle = FontStyles.Bold;
            valueTmp.color     = EditorUIHelpers.ACCENT;
            valueTmp.alignment = TextAlignmentOptions.Center;

            // [+] button
            var plusGo  = EditorUIHelpers.CreateUI("Plus", go.transform);
            plusGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var plusImg = plusGo.AddComponent<Image>();
            plusImg.color = EditorUIHelpers.BTN_NORMAL;
            var plusBtn = plusGo.AddComponent<Button>();
            var pc = plusBtn.colors;
            pc.normalColor = EditorUIHelpers.BTN_NORMAL; pc.highlightedColor = EditorUIHelpers.BTN_HOVER;
            pc.pressedColor = EditorUIHelpers.BTN_ACTIVE; pc.fadeDuration = 0.08f;
            plusBtn.colors = pc; plusBtn.targetGraphic = plusImg;
            plusBtn.onClick.AddListener(() => { if (_activeBuilding != null) onPlus(); });
            EditorUIHelpers.AddCenteredText(plusGo.transform, "+", 12f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);

            go.SetActive(false);
            return rt;
        }

        private void UpdateZBadges()
        {
            if (_zTopBadgeRt == null || _zBotBadgeRt == null) return;
            bool show = _activeBuilding != null && !_removeMode;
            _zTopBadgeRt.gameObject.SetActive(show);
            _zBotBadgeRt.gameObject.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect))
            {
                _zTopBadgeRt.gameObject.SetActive(false);
                _zBotBadgeRt.gameObject.SetActive(false);
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;

            // Building canvas-space width for proportional badge sizing
            Vector3 screenTR = cam.WorldToScreenPoint(new Vector3(rect.xMax, rect.yMax, 0f));
            Vector3 screenTL = cam.WorldToScreenPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            float   canvasW  = Mathf.Abs(ScreenToCanvasPos(screenTR).x - ScreenToCanvasPos(screenTL).x);
            float   badgeW   = Mathf.Clamp(canvasW * 0.65f, 60f, 160f);
            float   badgeH   = Mathf.Clamp(canvasW * 0.08f, 18f, 26f);
            float   inset    = badgeH * 0.5f + 4f;  // distance from top/bottom edge to badge center

            // Horizontal center of building in canvas space
            Vector3 screenBL  = cam.WorldToScreenPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            float   centerX   = (ScreenToCanvasPos(screenTR).x + ScreenToCanvasPos(screenTL).x) * 0.5f;

            // Top badge: just inside the top edge
            Vector2 canvasTop = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(rect.center.x, rect.yMax, 0f)));
            _zTopBadgeRt.sizeDelta        = new Vector2(badgeW, badgeH);
            _zTopBadgeRt.anchoredPosition = new Vector2(centerX, canvasTop.y - inset);

            // Bottom badge: just inside the bottom edge
            Vector2 canvasBot = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(rect.center.x, rect.yMin, 0f)));
            _zBotBadgeRt.sizeDelta        = new Vector2(badgeW, badgeH);
            _zBotBadgeRt.anchoredPosition = new Vector2(centerX, canvasBot.y + inset);

            // Update Z values
            if (_zTopBadgeTmp != null) _zTopBadgeTmp.text = $"Z: {_activeBuilding.ZTopOffset}";
            if (_zBotBadgeTmp != null) _zBotBadgeTmp.text = $"Z: {_activeBuilding.ZBottomOffset}";
        }

        private void BuildTutorial()
        {
            _tutorialRoot = EditorUIHelpers.MakePanel("Tutorial", _root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 0), new Vector2(520f, 240f));
            var vlg = _tutorialRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 14);
            vlg.spacing = 8f; vlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeTitleBar(_tutorialRoot.transform, "BUILDINGS TUTORIAL");

            _tutorialStepLabel = EditorUIHelpers.AddLabel(_tutorialRoot.transform, "", 14f);
            _tutorialStepLabel.fontStyle = FontStyles.Bold;
            _tutorialStepLabel.color = EditorUIHelpers.ACCENT;

            var bodyGo = EditorUIHelpers.CreateUI("Body", _tutorialRoot.transform);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            _tutorialBodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            _tutorialBodyTmp.fontSize = 12f;
            _tutorialBodyTmp.color = EditorUIHelpers.TEXT_PRIMARY;
            _tutorialBodyTmp.alignment = TextAlignmentOptions.TopLeft;
            _tutorialBodyTmp.enableWordWrapping = true;

            // Nav row
            var nav = EditorUIHelpers.CreateUI("Nav", _tutorialRoot.transform);
            nav.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(nav.transform, "Prev",  () => StepTutorial(-1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Next",  () => StepTutorial(+1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Close", () => _tutorialRoot.SetActive(false), 28f, 12f);

            _tutorialStep = 0;
            RefreshTutorial();
            _tutorialRoot.SetActive(false);
        }

        private void BuildConfirmModal()
        {
            _confirmModal = EditorUIHelpers.MakePanel("ConfirmModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _confirmModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            // Inner panel
            var inner = EditorUIHelpers.MakePanel("Inner", _confirmModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 200f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 12f; vlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeTitleBar(inner.transform, "CONFIRM DELETE");

            _confirmText = EditorUIHelpers.AddLabel(inner.transform, "?", 13f);
            _confirmText.color = EditorUIHelpers.TEXT_PRIMARY;
            _confirmText.alignment = TextAlignmentOptions.MidlineLeft;

            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Eliminar",
                () => { var cb = _pendingConfirmYes; HideConfirm(); cb?.Invoke(); }, 32f);
            EditorUIHelpers.MakeButton(btnRow.transform, "Cancelar", () => HideConfirm(), 32f, 12f);

            _confirmModal.SetActive(false);
        }

        private void CreatePerfProbe()
        {
            var probeGo = new GameObject("BuildingsPerfProbe");
            probeGo.transform.SetParent(transform);
            _perfProbe = probeGo.AddComponent<BuildingsPerfProbe>();
            _perfProbe.Visible = false;
            Debug.Log("[BuildingsEditor] Perf probe created (toggle via PERF button in menu bar).");
        }

        private void TogglePerfProbe()
        {
            if (_perfProbe == null) return;
            _perfProbe.Visible = !_perfProbe.Visible;
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PerfProbeMenuBtnImg, _uiRefs.PerfProbeMenuBtnTmp, _perfProbe.Visible);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PICKER + MODE
        // ──────────────────────────────────────────────────────────────────────────

        private void RefreshPicker()
        {
            if (_pickerContent == null) return;
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);
            if (_catalog == null) return;
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var tmpl in _catalog.Templates)
            {
                if (tmpl == null) continue;
                int id = tmpl.templateId;
                if (filter.Length > 0)
                {
                    string idStr = id.ToString();
                    string ap = (tmpl.assetPath ?? "").ToLowerInvariant();
                    if (!idStr.Contains(filter) && !ap.Contains(filter)) continue;
                }
                shown++;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, $"B{id}", 80f, () => SelectTemplate(id));
                if (tmpl.previewSprite != null) { icon.sprite = tmpl.previewSprite; icon.enabled = true; }
                label.text = $"#{id}";
                if (id == _selectedTemplateId)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;

                // Drag-from-picker: register PointerDown so LMB-dragging the slot
                // onto the map places the building directly (Python parity).
                int capturedId = id;
                var et  = btn.gameObject.AddComponent<EventTrigger>();
                var pde = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                pde.callback.AddListener(_ => OnPickerSlotPointerDown(capturedId));
                et.triggers.Add(pde);
            }
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} templates" : $"{shown} match '{_searchFilter}'";
        }

        private void SelectTemplate(int id)
        {
            _selectedTemplateId = id;
            RefreshPicker();
            // Placement is drag-only: do NOT auto-switch to Place mode. The user
            // must drag the slot from the picker onto the map to actually place a
            // building. A simple click only highlights the slot for inspection.
            if (_statusTmp != null)
                _statusTmp.text = $"Template #{id} highlighted. DRAG it from the panel onto the map to place.";
        }

        // ── Drag-from-picker ──────────────────────────────────────────────────────
        // Mirrors Python building_picker_controller.start_drag / place_building and
        // building_picker_view._draw_drag_preview.

        /// <summary>
        /// Creates the picker drag preview — a vivid-colored UI Image rendered on the
        /// editor's Canvas Overlay so it floats above the world AND any UI panels.
        /// Always rendered as the topmost sibling of the canvas so panels can't occlude it.
        /// </summary>
        private void BuildDragGhost()
        {
            if (_dragGhostGo != null) return;
            _dragGhostGo  = EditorUIHelpers.CreateUI("PickerDragGhost", _canvas.transform);
            _dragGhostRt  = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta  = new Vector2(80f, 80f);
            _dragGhostRt.anchorMin  = _dragGhostRt.anchorMax = new Vector2(0f, 0f);
            _dragGhostRt.pivot      = new Vector2(0.5f, 0.5f);

            // Bright outline ring as a sibling Image behind the sprite so the preview
            // reads clearly against both dark map tiles and bright UI panels.
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-6f, -6f);
            outlineRt.offsetMax = new Vector2( 6f,  6f);
            _dragGhostOutline = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            _dragGhostImg = _dragGhostGo.AddComponent<Image>();
            _dragGhostImg.raycastTarget  = false;
            _dragGhostImg.preserveAspect = true;
            _dragGhostImg.color          = DRAG_GHOST_TINT;
            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            // Force topmost so panels/menus can't render on top of the preview.
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        /// <summary>
        /// Sizes the drag-ghost RectTransform so its on-screen pixel size matches the
        /// building's actual world footprint at the current camera zoom. Returns true
        /// when the size could be computed; falls back to the default 80×80 otherwise.
        /// </summary>
        private void SizeDragGhostToWorldFootprint(BuildingTemplateData tmpl)
        {
            if (tmpl == null || _dragGhostRt == null) return;
            float worldW = Mathf.Max(0.01f, tmpl.originalScale.x / BUILDING_PPU);
            float worldH = Mathf.Max(0.01f, tmpl.originalScale.y / BUILDING_PPU);

            float pxPerWorldUnit = 32f; // safe default
            if (_mainCamera != null && _mainCamera.orthographic && _mainCamera.orthographicSize > 0.001f)
                pxPerWorldUnit = Screen.height / (2f * _mainCamera.orthographicSize);

            float scaleFactor = (_canvas != null && _canvas.scaleFactor > 0.001f) ? _canvas.scaleFactor : 1f;
            float wPx = worldW * pxPerWorldUnit / scaleFactor;
            float hPx = worldH * pxPerWorldUnit / scaleFactor;
            // Clamp so absurdly large buildings (e.g. catedrals) don't fill the entire screen.
            const float MAX_PX = 512f;
            if (wPx > MAX_PX || hPx > MAX_PX)
            {
                float k = MAX_PX / Mathf.Max(wPx, hPx);
                wPx *= k; hPx *= k;
            }
            _dragGhostRt.sizeDelta = new Vector2(wPx, hPx);
        }

        /// <summary>Called from each slot's EventTrigger.PointerDown — records drag origin.</summary>
        private void OnPickerSlotPointerDown(int templateId)
        {
            _pickerDragTemplateId  = templateId;
            _pickerDragStartScreen = Mouse.current?.position.ReadValue() ?? Vector2.zero;
        }

        /// <summary>
        /// Activates the ghost once the drag threshold is crossed, moves it with the
        /// cursor, and on LMB release over the map places the building.
        /// </summary>
        private void UpdatePickerDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screenPos = mouse.position.ReadValue();

            // Phase 1 — waiting for drag threshold
            if (!_pickerDragging && _pickerDragTemplateId >= 0)
            {
                if (mouse.leftButton.isPressed)
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        var tmpl = _catalog?.GetById(_pickerDragTemplateId);
                        if (tmpl != null)
                        {
                            _pickerDragging     = true;
                            _selectedTemplateId = _pickerDragTemplateId;
                            RefreshPicker();
                            BuildDragGhost();
                            _dragGhostImg.sprite  = tmpl.previewSprite;
                            _dragGhostImg.enabled = tmpl.previewSprite != null;
                            _dragGhostImg.color   = DRAG_GHOST_TINT;
                            // Size the on-screen ghost to match the building's real footprint.
                            SizeDragGhostToWorldFootprint(tmpl);
                            // Make sure the ghost stays above any panel that may have been
                            // re-parented or rebuilt since the editor was opened.
                            _dragGhostGo.transform.SetAsLastSibling();
                            _dragGhostGo.SetActive(true);

                            if (_statusTmp != null)
                                _statusTmp.text = $"Dragging template #{_pickerDragTemplateId} — release over the map to place.";
                        }
                    }
                }
                else
                {
                    // Released before threshold — normal click handled by Button.onClick.
                    _pickerDragTemplateId = -1;
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2 — ghost follows the cursor on the canvas. Because the ghost lives
            // on the editor's Canvas Overlay AND is forced to the last sibling, it
            // renders above the world AND above every UI panel/menu in the scene.
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mainCamera,
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Drop
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                if (!overUi && _mainCamera != null)
                {
                    Vector3 worldPos = _mainCamera.ScreenToWorldPoint(screenPos);
                    worldPos.z = 0f;
                    // Drag-only placement: PlaceBuilding() spawns at the drop
                    // position regardless of current EditorMode. We do NOT mutate
                    // _mode here so the user stays in Select after placing.
                    PlaceBuilding(worldPos);
                }
                else if (_statusTmp != null)
                {
                    _statusTmp.text = "Drag cancelled (released over UI). Drop on the map to place.";
                }
                CancelPickerDrag();
            }
        }

        /// <summary>Hides the ghost and resets all drag-from-picker state.</summary>
        private void CancelPickerDrag()
        {
            _pickerDragging       = false;
            _pickerDragTemplateId = -1;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            if (_mode != EditorMode.Resize) _resizing = false;
            RefreshModeButtons();
            if (_statusTmp == null) return;
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select: click building on map. Wheel to cycle stack. Drag thumbnails from the Buildings panel to place new ones.",
                EditorMode.Place  => "Placement is drag-only: drag a thumbnail from the Buildings panel onto the map.",
                EditorMode.Delete => "Click building to delete (with confirm).",
                EditorMode.Resize => "LMB-drag the R handle (top-right) to resize proportionally.",
                _ => ""
            };
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_placeBtnImg)  _placeBtnImg.color  = _mode == EditorMode.Place  ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_resizeBtnImg) _resizeBtnImg.color = _mode == EditorMode.Resize ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER     : new Color(0.55f, 0.15f, 0.15f, 1f);
            if (_addBtnImg)    _addBtnImg.color    = _mode == EditorMode.Place  ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_removeBtnImg) _removeBtnImg.color = _removeMode                ? EditorUIHelpers.DANGER     : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  ADD / REMOVE PANEL CALLBACKS
        // ──────────────────────────────────────────────────────────────────────────

        private void OnAddBuildingClicked()
        {
            // Placement is drag-only. The Add (+) button no longer enters a
            // "click-to-place" mode — it just reminds the user how to place.
            Toast(_selectedTemplateId >= 0
                ? $"Drag template #{_selectedTemplateId} from the Buildings panel onto the map to place it."
                : "Pick a template from the Buildings panel and DRAG it onto the map to place.");
        }

        private void ToggleRemoveMode()
        {
            _removeMode = !_removeMode;
            if (_removeMode) SetMode(EditorMode.Delete);
            RefreshModeButtons();
            Toast(_removeMode ? "Remove mode ON. Click building to delete." : "Remove mode OFF.");
        }

        private void OnAddOnSystemClicked()
        {
            // Python's add_building_on_system tool: opens a system-level placer (e.g.
            // file system browser to drop an external image as a new template).
            // Phase 2 — surface a status message for now so users know it's wired.
            Toast("Add-on-system: import external sprite as template (TODO Phase 2).");
        }

        private void ToggleCollidersMode()
        {
            // Python toggles colliders_mode which hides handles and exposes paint UI.
            // We surface this through the inspector (always visible) and just switch
            // mode label; deeper paint logic is Phase 2.
            Toast("Colliders mode toggled (paint UI in inspector — Phase 2).");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  INTERACTION (mouse + keyboard)
        // ──────────────────────────────────────────────────────────────────────────

        // ── Middle-mouse camera pan ──────────────────────────────────────────────────
        // Mirrors TileEditorManager.HandleCameraPan() and Python camera_pan.py.
        //   MMB press   → save vcam anchor
        //   MMB held    → offset vcam from anchor by screen-space delta
        //   MMB release → stop panning
        private void HandleCameraPan()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            Transform vcamT = camSetup != null ? camSetup.GetDetachedTransform() : null;
            if (vcamT == null) return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                _isPanning = true;
                _panAnchorScreenPos = mouse.position.ReadValue();
                _panAnchorCamPos = vcamT.position;
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                _isPanning = false;
            }

            if (_isPanning && mouse.middleButton.isPressed)
            {
                Vector2 currentScreenPos = mouse.position.ReadValue();
                Vector2 screenDelta = currentScreenPos - _panAnchorScreenPos;

                float unitsPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
                Vector3 worldDelta = new Vector3(screenDelta.x, screenDelta.y, 0f) * unitsPerPixel;
                Vector3 newPos = _panAnchorCamPos - worldDelta;
                newPos.z = vcamT.position.z;
                vcamT.position = newPos;
            }
        }

        private void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool ctrl = kb.ctrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) _undo.Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) _undo.Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) SaveInstancesToJson();
            if (kb.deleteKey.wasPressedThisFrame && _activeBuilding != null) RequestDeleteActiveWithConfirm();
            if (kb.dKey.wasPressedThisFrame && _activeBuilding != null && !ctrl) ResetActiveBuilding();
            if (kb.rKey.wasPressedThisFrame && _activeBuilding != null) SetMode(EditorMode.Resize);
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_confirmModal != null && _confirmModal.activeSelf) HideConfirm();
                else if (_tutorialRoot != null && _tutorialRoot.activeSelf) _tutorialRoot.SetActive(false);
                else { SaveInstancesToJson(); Deactivate(); }
            }

            // Colliders panel shortcuts — only active when the panel is open so we
            // never steal keys (especially '.') from other systems while not editing
            // colliders. All keys are explicitly read; pressing them while the panel
            // is open consumes the action regardless of any other listeners.
            if (_openDropdowns.Contains("colliders"))
                HandleColliderEditorShortcuts(kb);
        }

        private void HandleColliderEditorShortcuts(Keyboard kb)
        {
            // B → toggle brush ON/OFF
            if (kb.bKey.wasPressedThisFrame && !kb.ctrlKey.isPressed)
                SetBrushOn(!BrushOn);

            // # (Shift+3) or numpad-3 → action = Paint (writes "#")
            if (kb.digit3Key.wasPressedThisFrame && kb.shiftKey.isPressed)
                SetBrushAction(CollBrushMode.Solid);
            if (kb.numpad3Key.wasPressedThisFrame)
                SetBrushAction(CollBrushMode.Solid);

            // . (period) or numpad-. → action = Erase (writes ".")
            if (kb.periodKey.wasPressedThisFrame || kb.numpadPeriodKey.wasPressedThisFrame)
                SetBrushAction(CollBrushMode.Walk);

            // [ / ] → brush size −/+
            if (kb.leftBracketKey.wasPressedThisFrame)
                OnCollBrushSizeChanged(_collBrushSize - 1);
            if (kb.rightBracketKey.wasPressedThisFrame)
                OnCollBrushSizeChanged(_collBrushSize + 1);

            // Tab → toggle scope CG ↔ CU on the active building
            if (kb.tabKey.wasPressedThisFrame && _activeBuilding != null)
                ToggleColliderScope();
        }

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool overUi = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            var cam = Camera.main;
            if (cam == null) return;

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos  = cam.ScreenToWorldPoint(screenPos);
            worldPos.z = 0f;

            if (_colliderStroke.Active && mouse.leftButton.wasReleasedThisFrame)
            {
                EndColliderStroke();
                if (overUi) return;
            }

            // ── Hover proximity for split line (always computed, drives highlight colour)
            _splitHovering = false;
            if (!overUi && _activeBuilding != null && _activeBuilding.TryGetWorldRect(out var hoverRect))
            {
                float hsr = _activeBuilding.SplitRatioOverride >= 0f
                    ? _activeBuilding.SplitRatioOverride
                    : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);
                float hSplitY = hoverRect.yMin + hoverRect.height * (1f - hsr);
                _splitHovering = Mathf.Abs(worldPos.y - hSplitY) <= SPLIT_HANDLE_WORLD_RADIUS
                              && worldPos.x >= hoverRect.xMin - SPLIT_HANDLE_WORLD_RADIUS
                              && worldPos.x <= hoverRect.xMax + SPLIT_HANDLE_WORLD_RADIUS;
            }

            // Hover detection (skip when over UI): collect all buildings under cursor.
            if (!overUi) RecomputeHoverStack(worldPos);
            else { _hoveredBuilding = null; _hoverStack.Clear(); }

            // Wheel cycle within hover stack
            if (!overUi && _hoverStack.Count > 1)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll >  0.01f) { _hoverIndex = (_hoverIndex - 1 + _hoverStack.Count) % _hoverStack.Count; _hoveredBuilding = _hoverStack[_hoverIndex]; }
                if (scroll < -0.01f) { _hoverIndex = (_hoverIndex + 1) % _hoverStack.Count;                     _hoveredBuilding = _hoverStack[_hoverIndex]; }
            }

            // Split-ratio drag — LMB held on the split handle
            if (_splitDragging && _activeBuilding != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    if (_activeBuilding.TryGetWorldRect(out var dragRect))
                    {
                        // Map cursor Y to [0..1] within building rect, clamp [0.01..0.99]
                        float rawRatio = 1f - Mathf.Clamp01((worldPos.y - dragRect.yMin) / dragRect.height);
                        float newRatio = Mathf.Clamp(rawRatio, 0.01f, 0.99f);
                        _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, newRatio);
                        RefreshInspector();
                        if (_statusTmp != null)
                            _statusTmp.text = $"Split ratio → {newRatio:F3}";
                    }
                }
                else if (mouse.leftButton.wasReleasedThisFrame)
                {
                    float finalRatio = _activeBuilding.SplitRatioOverride;
                    float startRatio = _splitDragStartRatio;
                    // Register as undoable action only if ratio actually changed
                    if (!Mathf.Approximately(finalRatio, startRatio))
                    {
                        _undo.Do($"Split {finalRatio:F3}",
                            () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, finalRatio),
                            () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, startRatio));
                    }
                    _splitDragging = false;
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Split ratio set to {finalRatio:F3}.";
                }
                return;
            }

            // R-handle PointerDown sets _pendingResizeStart; we consume it here so
            // _resizeStartMouse is recorded at the world position for this frame.
            if (_pendingResizeStart && _activeBuilding != null)
            {
                _pendingResizeStart = false;
                _resizing         = true;
                _resizeStartMouse = worldPos;
                _resizeStartScale = (_activeBuilding.ScaleOverride.x > 0)
                    ? _activeBuilding.ScaleOverride
                    : (_activeBuilding.Template != null
                        ? _activeBuilding.Template.originalScale
                        : Vector2Int.one * 64);
                if (_statusTmp != null) _statusTmp.text = "Resize: drag to scale (proportional).";
            }

            // Resize drag — driven by LMB while _resizing is set by the R handle.
            if (_resizing && _activeBuilding != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    var delta = (Vector2)(worldPos - _resizeStartMouse);
                    // Preserve aspect ratio: dominant axis (|dx| vs |dy|) drives scale.
                    float aspect      = (float)_resizeStartScale.x / Mathf.Max(1, _resizeStartScale.y);
                    float signedDelta = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x : delta.y;
                    float pixDelta    = signedDelta * 32f;   // 32 px per world unit (building PPU)
                    int newW = Mathf.Max(8, _resizeStartScale.x + Mathf.RoundToInt(pixDelta));
                    int newH = Mathf.Max(8, Mathf.RoundToInt(newW / aspect));
                    _activeBuilding.Apply(_activeBuilding.Template, new Vector2Int(newW, newH), _activeBuilding.SplitRatioOverride);
                    if (_statusTmp != null) _statusTmp.text = $"Resize → {newW}×{newH} px (ratio {aspect:F2})";
                    RefreshInspector();
                }
                else if (mouse.leftButton.wasReleasedThisFrame)
                {
                    _resizing = false;
                    RefreshCollisionFor(_activeBuilding);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = "Resize done.";
                }
                return;
            }

            // Move drag
            if (_dragging && _activeBuilding != null)
            {
                _activeBuilding.transform.position = worldPos + _dragOffset;
                if (mouse.rightButton.wasReleasedThisFrame) _dragging = false;
                return;
            }

            if (overUi) return;

            // Collider painting — when a brush mode is active, LMB hold paints/erases
            // collider tiles on the active building. Returns early so it doesn't
            // interfere with selection/placement.
            if (_collBrushMode != CollBrushMode.Off && _activeBuilding != null
                && (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame))
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    BeginColliderStroke();
                HandleColliderPaint(worldPos);
                return;
            }

            // LMB on split handle — start split-ratio drag
            if (!overUi && mouse.leftButton.wasPressedThisFrame && _activeBuilding != null
                && _activeBuilding.TryGetWorldRect(out var checkRect))
            {
                float sr = _activeBuilding.SplitRatioOverride >= 0f
                    ? _activeBuilding.SplitRatioOverride
                    : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);
                float handleWorldY = checkRect.yMin + checkRect.height * (1f - sr);
                float distY = Mathf.Abs(worldPos.y - handleWorldY);
                // Also check horizontal proximity (within building X bounds + small margin)
                float marginX = SPLIT_HANDLE_WORLD_RADIUS;
                bool withinX = worldPos.x >= checkRect.xMin - marginX && worldPos.x <= checkRect.xMax + marginX;
                if (distY <= SPLIT_HANDLE_WORLD_RADIUS && withinX)
                {
                    _splitDragging = true;
                    _splitDragStartRatio = sr;
                    return;   // consume event
                }
            }

            // LMB — primary action
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_removeMode || _mode == EditorMode.Delete)
                {
                    if (_hoveredBuilding != null) RequestDeleteWithConfirm(_hoveredBuilding);
                    return;
                }
                // Click-to-place was removed: placement is drag-only (drag a
                // thumbnail from the Buildings panel onto the map). A bare LMB
                // click on the map only ever selects the hovered building.
                if (_hoveredBuilding != null) SetActiveBuilding(_hoveredBuilding);
            }

            // RMB on a building → move drag (resize is now LMB-drag via the R handle).
            if (mouse.rightButton.wasPressedThisFrame && _hoveredBuilding != null)
            {
                SetActiveBuilding(_hoveredBuilding);
                _dragging   = true;
                _dragOffset = _activeBuilding.transform.position - worldPos;
            }
        }

        private void RecomputeHoverStack(Vector3 worldPos)
        {
            _hoverStack.Clear();
            // OverlapPointAll returns colliders whose footprint contains worldPos.
            // Buildings only have a collider over the FOOTPRINT (below split). To
            // also catch the canopy region we test the full sprite rect explicitly.
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || !b.TryGetWorldRect(out var r)) continue;
                if (r.Contains(worldPos)) _hoverStack.Add(b);
            }
            if (_hoverStack.Count == 0) { _hoveredBuilding = null; return; }
            // Stable sort: prefer the visually-front-most (highest Y baseline = lower in world)
            _hoverStack.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            if (_hoverIndex >= _hoverStack.Count) _hoverIndex = 0;
            _hoveredBuilding = _hoverStack[_hoverIndex];
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  ACTIVE BUILDING + INSPECTOR
        // ──────────────────────────────────────────────────────────────────────────

        private void SetActiveBuilding(BuildingObject b)
        {
            bool changed = _activeBuilding != b;
            _activeBuilding = b;
            // Drop the cached session so the next paint refreshes it for the new
            // building, and refresh the overlay so the OLD active building reverts
            // to BoxCollider2D rendering and the NEW one (if any) gets authoring
            // cells pushed in.
            if (changed) _activeColliderSession = null;
            RefreshInspector();
            if (_collidersVisible) RefreshCollidersOverlay();
            if (_statusTmp != null && b != null) _statusTmp.text = $"Active: ID {b.InstanceId} ({b.Template?.name})";
        }

        private void RefreshInspector()
        {
            if (_propsTmp == null) return;
            if (_activeBuilding == null || _activeBuilding.Template == null)
            {
                _propsTmp.text = "Select a building to view properties.";
                if (_inspectorRoot != null) _inspectorRoot.SetActive(false);
                RefreshCollidersPanel();
                return;
            }
            _inspectorRoot.SetActive(true);

            var t = _activeBuilding.Template;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>ID:</b> {_activeBuilding.InstanceId}");
            sb.AppendLine($"<b>Template:</b> #{t.templateId} ({t.name})");
            sb.AppendLine($"<b>Asset:</b> {t.assetPath}");
            sb.AppendLine($"<b>Solid:</b> {t.solid}");
            sb.AppendLine($"<b>Original:</b> {t.originalScale.x}×{t.originalScale.y} px");
            var sov = _activeBuilding.ScaleOverride;
            if (sov.x > 0 || sov.y > 0) sb.AppendLine($"<b>Scale ovr:</b> {sov.x}×{sov.y}");
            sb.AppendLine($"<b>Zone:</b> {_activeBuilding.ZoneName}");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;

            // Sync inspector controls without firing callbacks
            float sr = _activeBuilding.SplitRatioOverride >= 0f
                ? _activeBuilding.SplitRatioOverride : t.splitRatio;
            _splitSlider.SetValueWithoutNotify(Mathf.Clamp(sr, _splitSlider.minValue, _splitSlider.maxValue));
            if (_zBottomVal != null) _zBottomVal.text = _activeBuilding.ZBottomOffset.ToString();
            if (_zTopVal    != null) _zTopVal.text    = _activeBuilding.ZTopOffset.ToString();
            string scope = _activeBuilding.EffectiveColliderScope;
            if (_scopeBtnLabel != null) _scopeBtnLabel.text = scope;
            if (_scopeBtnImg   != null) _scopeBtnImg.color = scope == "CU" ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
            RefreshCollidersPanel();
        }

        private void OnSplitSliderChanged(float v)
        {
            if (_activeBuilding == null) return;
            float oldVal = _activeBuilding.SplitRatioOverride;
            _undo.Do($"Split {v:F2}",
                () => { _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, v); RefreshCollisionFor(_activeBuilding); },
                () => { _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, oldVal); RefreshCollisionFor(_activeBuilding); });
        }

        private void AdjustZ(BuildingObject b, bool bottom, int delta)
        {
            if (b == null) return;
            int oldVal = bottom ? b.ZBottomOffset : b.ZTopOffset;
            int newVal = oldVal + delta;
            _undo.Do($"Z{(bottom?"B":"T")} {newVal}",
                () => { if (bottom) b.ZBottomOffset = newVal; else b.ZTopOffset = newVal; RefreshInspector(); },
                () => { if (bottom) b.ZBottomOffset = oldVal; else b.ZTopOffset = oldVal; RefreshInspector(); });
        }

        private void ToggleColliderScope()
        {
            if (_activeBuilding == null) { Toast("Select a building first."); return; }
            string current = _activeBuilding.EffectiveColliderScope;
            string next    = current == "CU" ? "CG" : "CU";
            string oldOv   = _activeBuilding.ColliderScopeOverride;
            _undo.Do($"Scope {next}",
                () => { _activeBuilding.ColliderScopeOverride = next; RefreshCollisionFor(_activeBuilding); RefreshInspector(); },
                () => { _activeBuilding.ColliderScopeOverride = oldOv; RefreshCollisionFor(_activeBuilding); RefreshInspector(); });
        }

        private void ResetActiveBuilding()
        {
            if (_activeBuilding == null) return;
            var b = _activeBuilding;
            var oldScale = b.ScaleOverride;
            var oldSplit = b.SplitRatioOverride;
            var oldZB = b.ZBottomOffset;
            var oldZT = b.ZTopOffset;
            var oldScope = b.ColliderScopeOverride;
            _undo.Do("Reset building",
                () => { b.Apply(b.Template, Vector2Int.zero, -1f); b.ZBottomOffset = 0; b.ZTopOffset = 0; b.ColliderScopeOverride = ""; RefreshCollisionFor(b); RefreshInspector(); },
                () => { b.Apply(b.Template, oldScale, oldSplit); b.ZBottomOffset = oldZB; b.ZTopOffset = oldZT; b.ColliderScopeOverride = oldScope; RefreshCollisionFor(b); RefreshInspector(); });
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PLACE / DELETE
        // ──────────────────────────────────────────────────────────────────────────

        private void PlaceBuilding(Vector3 worldPos)
        {
            if (_catalog == null) { Toast("BuildingCatalog not assigned."); return; }
            var template = _catalog.GetById(_selectedTemplateId);
            if (template == null) { Toast("Template not found."); return; }

            CacheBuildingLoader();
            int newId = NextInstanceId();
            string zoneName = DetectZoneAt(worldPos);

            BuildingObject created = null;
            _undo.Do($"Place #{template.templateId}",
                () =>
                {
                    var go = new GameObject($"Building_{newId}_{template.name}");
                    go.transform.SetParent(_buildingsRoot, worldPositionStays: false);
                    go.transform.position = worldPos;
                    go.layer = 11; // World
                    var bObj = go.AddComponent<BuildingObject>();
                    bObj.ZoneName   = zoneName;
                    bObj.InstanceId = newId;
                    bObj.Apply(template, Vector2Int.zero, -1f);
                    RefreshCollisionFor(bObj);
                    created = bObj;
                    InvalidateBuildingCache();
                    SetActiveBuilding(bObj);
                    if (_statusTmp != null) _statusTmp.text = $"Placed #{template.templateId} at ({worldPos.x:F1}, {worldPos.y:F1}) → ID {newId}";
                },
                () =>
                {
                    if (created != null) { Destroy(created.gameObject); created = null; InvalidateBuildingCache(); }
                    if (_activeBuilding == null) RefreshInspector();
                });
        }

        private void RequestDeleteActiveWithConfirm()
        {
            if (_activeBuilding != null) RequestDeleteWithConfirm(_activeBuilding);
        }

        private void RequestDeleteWithConfirm(BuildingObject b)
        {
            if (b == null || b.Template == null) return;
            int templateId = b.Template.templateId;
            int refCount = CountBuildingsUsingTemplate(templateId);
            string msg = $"Delete building ID {b.InstanceId}?\n\n" +
                         $"Template: #{templateId} ({b.Template.name})\n" +
                         $"Other instances using this template: {refCount - 1}";
            ShowConfirm(msg, () => DeleteBuilding(b));
        }

        private void DeleteBuilding(BuildingObject b)
        {
            if (b == null) return;
            var go = b.gameObject;
            Vector3 savedPos = go.transform.position;
            string  savedName = go.name;
            _undo.Do($"Delete {savedName}",
                () => { if (go) go.SetActive(false); InvalidateBuildingCache(); if (_activeBuilding == b) { _activeBuilding = null; RefreshInspector(); } },
                () => { if (go) { go.transform.position = savedPos; go.name = savedName; go.SetActive(true); InvalidateBuildingCache(); } });
            if (_statusTmp != null) _statusTmp.text = $"Deleted: {savedName}";
        }

        private int CountBuildingsUsingTemplate(int templateId)
        {
            int n = 0;
            var all = FindObjectsOfType<BuildingObject>();
            foreach (var b in all)
                if (b != null && b.Template != null && b.Template.templateId == templateId)
                    n++;
            return n;
        }

        private int NextInstanceId()
        {
            int max = 0;
            var all = FindObjectsOfType<BuildingObject>();
            foreach (var b in all) if (b != null && b.InstanceId > max) max = b.InstanceId;
            return max + 1;
        }

        private string DetectZoneAt(Vector3 worldPos)
        {
            var zm = FindObjectOfType<ZoneManager>();
            if (zm != null) return zm.DetectZone(worldPos);
            return "Lobby";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PERSISTENCE — write StreamingAssets/Buildings/buildings_instances.json
        // ──────────────────────────────────────────────────────────────────────────

        private void SaveInstancesToJson()
        {
            string dir  = Path.Combine(Application.streamingAssetsPath, "Buildings");
            string path = Path.Combine(dir, "buildings_instances.json");
            try
            {
                EnsureColliderDataLoaded();
                if (_activeColliderSession != null && _activeColliderSession.WorkingGrid != null)
                    PersistSessionToStore(_activeColliderSession);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("[");
                var zm = FindObjectOfType<ZoneManager>();
                int zH = zm != null ? zm.ZoneHeightTiles : 0;

                var all = FindObjectsOfType<BuildingObject>()
                    .Where(b => b != null && b.gameObject.activeInHierarchy && b.Template != null)
                    .OrderBy(b => b.InstanceId).ToList();

                int nextId = 1;
                for (int i = 0; i < all.Count; i++)
                {
                    var b = all[i];
                    int oldInstanceId = b.InstanceId;
                    RemapColliderInstanceStore(oldInstanceId, nextId);
                    b.InstanceId = nextId++;
                    int relX = 0, relY = 0;
                    string zone = b.ZoneName ?? "Lobby";
                    if (zm != null && zm.TryGetZone(zone, out var zd))
                    {
                        int effW = (b.ScaleOverride.x > 0) ? b.ScaleOverride.x : b.Template.originalScale.x;
                        int effH = (b.ScaleOverride.y > 0) ? b.ScaleOverride.y : b.Template.originalScale.y;
                        const float PPU = 32f;
                        float wx = b.transform.position.x;
                        float wy = b.transform.position.y;
                        relX = Mathf.RoundToInt((wx - zd.gridOffset.x) * PPU - effW * 0.5f);
                        relY = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - wy) * PPU - effH);
                    }

                    sb.Append("  {");
                    sb.Append($"\"id\": {b.InstanceId}, ");
                    sb.Append($"\"template_id\": {b.Template.templateId}, ");
                    sb.Append($"\"zone\": \"{EscapeJson(zone)}\", ");
                    sb.Append($"\"rel_x\": {relX}, ");
                    sb.Append($"\"rel_y\": {relY}");

                    var sov = b.ScaleOverride;
                    bool hasCollisionOverride = _colliderInstanceStore.TryGetValue(b.InstanceId, out var instanceGrid);
                    bool writeCollisionOverride = hasCollisionOverride &&
                        string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);
                    bool hasColliderScope = !string.IsNullOrEmpty(b.ColliderScopeOverride);
                    bool hasOv = b.SplitRatioOverride >= 0f || sov.x > 0 || sov.y > 0 || hasColliderScope || writeCollisionOverride;
                    if (hasOv)
                    {
                        sb.Append(", \"overrides\": {");
                        bool first = true;
                        if (sov.x > 0 || sov.y > 0) { sb.Append($"\"scale\": [{sov.x}, {sov.y}]"); first = false; }
                        if (b.SplitRatioOverride >= 0f)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "\"split_ratio\": {0:F4}", b.SplitRatioOverride));
                            first = false;
                        }
                        if (hasColliderScope)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"collider_scope\": \"{EscapeJson(b.ColliderScopeOverride)}\"");
                            first = false;
                        }
                        if (writeCollisionOverride && instanceGrid != null)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append("\"collision_override\": ");
                            AppendGridJson(sb, instanceGrid, 0);
                        }
                        sb.Append("}");
                    }
                    sb.Append("}");
                    if (i < all.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");

                File.WriteAllText(path, sb.ToString());
                PruneColliderInstanceStore(all);
                WriteColliderStoresToDisk(dir);
#if UNITY_EDITOR
                // Refresh the backup copy via reflection so we don't create a
                // runtime→editor assembly dependency. BuildingsDataGuard.RefreshBackup()
                // lives in Valkur.Editor (Editor-only assembly).
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    var t = System.Type.GetType(
                        "Valkur.Editor.BuildingsDataGuard, Valkur.Editor");
                    t?.GetMethod("RefreshBackup",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static)
                     ?.Invoke(null, null);
                };
#endif
                if (_statusTmp != null) _statusTmp.text = $"Saved {all.Count} buildings → {INSTANCES_REL_PATH}";
                Debug.Log($"[BuildingsEditor] Saved {all.Count} buildings to {path}");
                RefreshCollidersPanel();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BuildingsEditor] Save failed: {ex.Message}\n{ex.StackTrace}");
                if (_statusTmp != null) _statusTmp.text = "Save FAILED — see console.";
            }
        }
        private const string INSTANCES_REL_PATH = "StreamingAssets/Buildings/buildings_instances.json";

        private void ReloadFromJson()
        {
            CacheBuildingLoader();
            if (_buildingLoader == null) { Toast("BuildingLoader not found in scene."); return; }
            ResetColliderAuthoringState();
            _buildingLoader.LoadBuildings();
            _undo.Clear();
            _activeBuilding = null;
            _hoveredBuilding = null;
            RefreshInspector();
            if (_statusTmp != null) _statusTmp.text = "Reloaded from JSON.";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  CONFIRM MODAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text = text;
            _pendingConfirmYes = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  TUTORIAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text = body;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PER-FRAME OVERLAY UPDATES (outlines + handles + ID label)
        // ──────────────────────────────────────────────────────────────────────────

        private void UpdateOutlineState()
        {
            if (_hoverFx == null || _activeFx == null) return;

            // Hover (skip if same as active to avoid double-drawing)
            if (_hoveredBuilding != null && _hoveredBuilding != _activeBuilding)
            {
                bool red = _removeMode || _mode == EditorMode.Delete;
                _hoverFx.Configure(
                    color:        red ? HOVER_REMOVE_RED : HOVER_CYAN,
                    thicknessWorld: red ? HOVER_THICKNESS_WORLD * 1.5f : HOVER_THICKNESS_WORLD,
                    drawFill:     red,
                    fillColor:    HOVER_REMOVE_FILL);
                _hoverFx.Follow(_hoveredBuilding);
            }
            else
            {
                _hoverFx.Follow(null); _hoverFx.SetVisible(false);
            }

            // Active
            if (_activeBuilding != null) _activeFx.Follow(_activeBuilding);
            else { _activeFx.Follow(null); _activeFx.SetVisible(false); }
        }

        private void UpdateFloatingHandles()
        {
            if (_handlesRoot == null) return;
            bool show = _activeBuilding != null && !_removeMode;
            _handlesRoot.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _handlesRoot.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) return;

            // Project building top-right corner to canvas (pivot=top-right → badge sits inside frame)
            Vector3 worldTopRight = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 screenTR      = cam.WorldToScreenPoint(worldTopRight);
            Vector2 canvasTR      = ScreenToCanvasPos(screenTR);

            // Compute proportional badge size from the building's canvas-space width
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screenTL     = cam.WorldToScreenPoint(worldTopLeft);
            Vector2 canvasTL     = ScreenToCanvasPos(screenTL);
            float canvasW        = Mathf.Abs(canvasTR.x - canvasTL.x);
            float handleSize     = Mathf.Clamp(canvasW * 0.20f, 20f, 52f);

            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(handleSize, handleSize);
            rt.anchoredPosition = canvasTR;
        }

        /// <summary>
        /// Each frame: position the cyan split-ratio line over the active building.
        /// The line sits at the boundary between the bottom (behind player) and top
        /// (in front of player) render layers — identical to Python's split_tool_view.py.
        /// </summary>
        private void UpdateSplitLine()
        {
            if (_splitLineRt == null) return;
            if (_activeBuilding == null || !_activeBuilding.TryGetWorldRect(out var rect))
            {
                _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                return;
            }

            // Effective split ratio: instance override (if >= 0) else template default
            float sr = _activeBuilding.SplitRatioOverride >= 0f
                ? _activeBuilding.SplitRatioOverride
                : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);

            // Split line world Y = bottom of building + bottom-portion height
            // bottomFraction = (1 - sr)  because sr is the TOP fraction (see BuildingObject docs)
            float worldSplitY = rect.yMin + rect.height * (1f - sr);

            var cam = Camera.main;
            if (cam == null)
            {
                _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                return;
            }

            // Width in canvas space = width of the building rect projected to screen
            Vector3 leftScreen  = cam.WorldToScreenPoint(new Vector3(rect.xMin, worldSplitY, 0f));
            Vector3 rightScreen = cam.WorldToScreenPoint(new Vector3(rect.xMax, worldSplitY, 0f));
            Vector2 leftCanvas  = ScreenToCanvasPos(leftScreen);
            Vector2 rightCanvas = ScreenToCanvasPos(rightScreen);
            float canvasWidth   = Vector2.Distance(leftCanvas, rightCanvas);

            Vector3 centerScreen = cam.WorldToScreenPoint(
                new Vector3(rect.center.x, worldSplitY, 0f));
            Vector2 canvasCenter = ScreenToCanvasPos(centerScreen);

            _splitLineRt.gameObject.SetActive(true);
            _splitLineRt.anchoredPosition = canvasCenter;
            _splitLineRt.sizeDelta = new Vector2(canvasWidth, 3f);

            // Handle — same center point, highlighted while dragging or cursor near it
            if (_splitHandleRt != null)
            {
                _splitHandleRt.gameObject.SetActive(true);
                _splitHandleRt.anchoredPosition = canvasCenter;

                // Highlight: white when dragging, yellow on hover, cyan otherwise
                if (_splitHandleImg != null)
                    _splitHandleImg.color = _splitDragging
                        ? Color.white
                        : _splitHovering
                            ? new Color(1f, 0.9f, 0f, 1f)           // yellow on hover
                            : new Color(0f, 200f / 255f, 1f, 1f);   // cyan normal
            }
        }

        private void UpdateIdLabel()
        {
            if (_idLabelRt == null) return;
            if (_activeBuilding == null) { _idLabelRt.gameObject.SetActive(false); return; }
            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _idLabelRt.gameObject.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) { _idLabelRt.gameObject.SetActive(false); return; }
            _idLabelRt.gameObject.SetActive(true);
            _idLabelTmp.text = $"ID {_activeBuilding.InstanceId}";
            // Place just above the top-left corner of the yellow frame (outside the frame)
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screen = cam.WorldToScreenPoint(worldTopLeft);
            // pivot=(0,1): label's top-left aligns to worldTopLeft; subtract ~3px so it sits
            // flush against the outside top edge of the frame with a tiny gap
            _idLabelRt.anchoredPosition = ScreenToCanvasPos(screen) + new Vector2(0f, 3f);
        }

        private Vector2 ScreenToCanvasPos(Vector3 screenPos)
        {
            if (_canvas == null) return Vector2.zero;
            // ScreenSpaceOverlay: pass null camera — works with any CanvasScaler config.
            var canvasRt = _canvas.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, new Vector2(screenPos.x, screenPos.y), null, out Vector2 local))
            {
                return local;
            }
            return Vector2.zero;
        }

        private void Toast(string msg)
        {
            if (_statusTmp != null) _statusTmp.text = msg;
            Debug.Log($"[BuildingsEditor] {msg}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  COLLIDER EDITING (Colliders panel)
        // ──────────────────────────────────────────────────────────────────────────

        private enum CollBrushMode { Off, Solid, Walk, Erase }
        private enum ColliderAuthoringScope { CG, CU }
        private const string CollTilePrefix = "CollTile_";
        private const string PooledCollTilePrefix = "_PooledCollTile_";

        private sealed class ColliderGridData
        {
            public int width;
            public int height;
            public string[][] collision;
            public Vector2Int gridRefSize;
        }

        private sealed class ActiveColliderGridSession
        {
            public int BuildingId;
            public int InstanceId;
            public string ImageKey;
            public ColliderAuthoringScope Scope;
            public Vector2Int EffectivePixelSize;
            public ColliderGridData WorkingGrid;
        }

        private sealed class ColliderPaintStroke
        {
            public bool Active;
            public ColliderAuthoringScope Scope;
            public string ImageKey;
            public int InstanceId;
            public ColliderGridData Before;
            public bool Changed;
        }

        private bool          _collidersVisible;
        private CollBrushMode _collBrushMode = CollBrushMode.Off;
        // Remembered action for when the brush is toggled back ON. Only Solid (=#)
        // and Walk (=.) are valid actions in the redesigned UX. The Off/Erase
        // values of CollBrushMode are kept internally for back-compat with
        // HandleColliderPaint, but Erase is no longer reachable from the UI.
        private CollBrushMode _lastBrushAction = CollBrushMode.Solid;
        private int           _collBrushSize = 1;
        private bool          _colliderDataLoaded;
        private readonly Dictionary<string, ColliderGridData> _colliderImageStore =
            new Dictionary<string, ColliderGridData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ColliderGridData> _savedColliderImageStore =
            new Dictionary<string, ColliderGridData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ColliderGridData> _colliderInstanceStore =
            new Dictionary<int, ColliderGridData>();
        private readonly Dictionary<int, ColliderGridData> _savedColliderInstanceStore =
            new Dictionary<int, ColliderGridData>();
        private ActiveColliderGridSession _activeColliderSession;
        private readonly ColliderPaintStroke _colliderStroke = new ColliderPaintStroke();

        private void ToggleCollidersVisible()
        {
            _collidersVisible = !_collidersVisible;
            if (_collidersVisible)
            {
                ReapplyAllColliderStates();
                Physics2D.SyncTransforms();
                LogColliderDiagnostics();
            }
            SetTilemapCollidersVisible(_collidersVisible);
            int total = RefreshCollidersOverlay();
            if (_uiRefs.CollVisibilityBtnLabel != null)
                _uiRefs.CollVisibilityBtnLabel.text = _collidersVisible ? "Hide Colliders" : "Show Colliders";
            RefreshCollidersPanel();
            Toast(_collidersVisible ? $"Colliders visible ({total} shapes)." : "Colliders hidden.");
        }

        /// <summary>
        /// Print a one-shot diagnostic snapshot of every BuildingObject's
        /// physical collider state (root collider + CollTile children) so we
        /// can verify in the Console exactly what the physics engine sees:
        /// per-tile world position, world size, layer, isTrigger flag. If the
        /// player walks through a "wall", the offending row will look wrong
        /// here (wrong layer, isTrigger=true, zero size, far-away position…).
        /// </summary>
        private void LogColliderDiagnostics()
        {
            int worldLayer = LayerMask.NameToLayer("World");
            var all = FindObjectsOfType<BuildingObject>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[BuildingsEditor] Show Colliders → diagnostics for {all.Length} buildings " +
                          $"(expected layer 'World' = {worldLayer}):");
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                int tiles = 0, mismatched = 0, triggers = 0;
                var boxes = b.GetComponentsInChildren<BoxCollider2D>(includeInactive: false);
                BoxCollider2D first = null;
                for (int j = 0; j < boxes.Length; j++)
                {
                    var box = boxes[j];
                    if (!box.enabled) continue;
                    if (box.transform.name.StartsWith("_ColliderDebug_", StringComparison.Ordinal)) continue;
                    tiles++;
                    if (first == null) first = box;
                    if (box.gameObject.layer != worldLayer && worldLayer >= 0) mismatched++;
                    if (box.isTrigger) triggers++;
                }
                string firstInfo = first != null
                    ? $" first={first.name} center={first.bounds.center} size={first.bounds.size} layer={LayerMask.LayerToName(first.gameObject.layer)} trigger={first.isTrigger}"
                    : " (no enabled colliders)";
                sb.AppendLine($"  • {b.name} (id={b.InstanceId}) → {tiles} active colliders, " +
                              $"{mismatched} on wrong layer, {triggers} triggers." + firstInfo);
            }
            Debug.Log(sb.ToString(), this);
        }

        private bool BrushOn => _collBrushMode != CollBrushMode.Off;

        /// <summary>
        /// Add or remove <see cref="TilemapColliderDebugOverlay"/> on every
        /// <see cref="CompositeCollider2D"/> that is backed by a <see cref="UnityEngine.Tilemaps.TilemapCollider2D"/>.
        /// Called alongside building-collider visibility changes so the user sees
        /// a single unified "Show Colliders" view covering both building BoxCollider2Ds
        /// and tile-layer composite paths.
        /// </summary>
        private static void SetTilemapCollidersVisible(bool visible)
        {
            var composites = FindObjectsOfType<CompositeCollider2D>();
            foreach (var cc in composites)
            {
                // Only decorate composites that are driven by a TilemapCollider2D —
                // skip physics-only CompositeCollider2Ds on regular rigidbodies.
                if (cc.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>() == null) continue;

                var overlay = cc.GetComponent<TilemapColliderDebugOverlay>();
                if (overlay == null && visible)
                    overlay = cc.gameObject.AddComponent<TilemapColliderDebugOverlay>();
                if (overlay != null)
                    overlay.SetVisible(visible);
            }
        }

        private void SetBrushOn(bool on)
        {
            if (on)
            {
                // Resume the last selected action; default to Paint if none.
                if (_lastBrushAction != CollBrushMode.Solid && _lastBrushAction != CollBrushMode.Walk)
                    _lastBrushAction = CollBrushMode.Solid;
                SetCollBrushMode(_lastBrushAction);
            }
            else
            {
                SetCollBrushMode(CollBrushMode.Off);
            }
        }

        private void SetBrushAction(CollBrushMode action)
        {
            // Only Paint (Solid → "#") and Erase (Walk → ".") are valid actions.
            if (action != CollBrushMode.Solid && action != CollBrushMode.Walk) return;
            // Clicking the already-active action toggles the brush OFF.
            if (BrushOn && _collBrushMode == action)
            {
                SetCollBrushMode(CollBrushMode.Off);
                return;
            }
            _lastBrushAction = action;
            SetCollBrushMode(action);
        }

        private static string ActionLabel(CollBrushMode action)
            => action == CollBrushMode.Solid ? "# Paint"
             : action == CollBrushMode.Walk  ? ". Erase"
             : action.ToString();

        private void SetCollBrushMode(CollBrushMode mode)
        {
            _collBrushMode = mode;
            if (mode == CollBrushMode.Solid || mode == CollBrushMode.Walk)
                _lastBrushAction = mode;
            RefreshBrushButtonHighlights();
            if (mode != CollBrushMode.Off && !_collidersVisible)
            {
                _collidersVisible = true;
                if (_uiRefs.CollVisibilityBtnLabel != null)
                    _uiRefs.CollVisibilityBtnLabel.text = "Hide Colliders";
                ReapplyAllColliderStates();
                Physics2D.SyncTransforms();
                SetTilemapCollidersVisible(true);
                RefreshCollidersOverlay();
            }
            if (_uiRefs.CollBrushToggleLabel != null)
                _uiRefs.CollBrushToggleLabel.text = BrushOn
                    ? $"Brush: ON ({ActionLabel(_lastBrushAction)})"
                    : "Brush: OFF";
            RefreshCollidersPanel();
            Toast(BrushOn ? $"Brush ON ({ActionLabel(_collBrushMode)})." : "Brush OFF.");
        }

        private void OnCollBrushSizeChanged(int v)
        {
            _collBrushSize = Mathf.Clamp(v, 1, 8);
            RefreshCollBrushSizePresets();
            RefreshCollidersPanel();
        }

        private void RefreshCollBrushSizePresets()
        {
            if (_uiRefs.CollBrushSizePresetImgs == null) return;
            for (int i = 0; i < _uiRefs.CollBrushSizePresetImgs.Count; i++)
            {
                int size   = i + 1;
                bool active = size == _collBrushSize;
                if (_uiRefs.CollBrushSizePresetImgs[i] != null)
                    _uiRefs.CollBrushSizePresetImgs[i].color =
                        active ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
                if (_uiRefs.CollBrushSizePresetLabels != null
                    && i < _uiRefs.CollBrushSizePresetLabels.Count
                    && _uiRefs.CollBrushSizePresetLabels[i] != null)
                    _uiRefs.CollBrushSizePresetLabels[i].color =
                        active ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_SECONDARY;
            }
            if (_uiRefs.CollBrushSizeLabel != null)
                _uiRefs.CollBrushSizeLabel.text = $"{_collBrushSize}x{_collBrushSize}";
        }

        private void RefreshBrushButtonHighlights()
        {
            // Brush ON/OFF toggle highlight.
            ApplyBrushBtnStyle(_uiRefs.CollBrushToggleImg, BrushOn);
            // Action highlight: highlight the action that would apply on next click.
            // When brush is OFF, still indicate the remembered action so the user knows
            // what will activate when they press B.
            CollBrushMode shownAction = BrushOn ? _collBrushMode : _lastBrushAction;
            ApplyBrushBtnStyle(_uiRefs.CollPaintBtnImg, shownAction == CollBrushMode.Solid);
            ApplyBrushBtnStyle(_uiRefs.CollEraseBtnImg, shownAction == CollBrushMode.Walk);
        }

        private static void ApplyBrushBtnStyle(Image img, bool selected)
        {
            if (img == null) return;
            img.color = selected ? new Color(0.20f, 0.55f, 0.85f, 1f)
                                 : new Color(0.18f, 0.18f, 0.20f, 1f);
        }

        private void RefreshCollidersPanel()
        {
            // Update scope button label whenever we refresh.
            if (_uiRefs.CollScopeBtnLabel != null)
            {
                string scopeNow = _activeBuilding != null
                    ? _activeBuilding.EffectiveColliderScope
                    : "--";
                string scopeDesc = scopeNow == "CU" ? "this only"
                                 : scopeNow == "CG" ? "all of type"
                                 : "no selection";
                _uiRefs.CollScopeBtnLabel.text = $"Scope: {scopeNow} ({scopeDesc})";
            }

            if (_uiRefs.CollTargetText == null || _uiRefs.CollStateText == null) return;

            string brushLabel = BrushOn ? $"ON {ActionLabel(_collBrushMode)}" : "OFF";

            if (_activeBuilding == null || _activeBuilding.Template == null)
            {
                _uiRefs.CollTargetText.text = "No building selected.";
                _uiRefs.CollStateText.text  = $"Grid: -- | Brush {brushLabel} x{_collBrushSize}";
                return;
            }

            EnsureColliderDataLoaded();
            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null)
            {
                _uiRefs.CollTargetText.text = $"ID {_activeBuilding.InstanceId} | Scope {_activeBuilding.EffectiveColliderScope}";
                _uiRefs.CollStateText.text  = $"Grid: -- | Brush {brushLabel} x{_collBrushSize}";
                return;
            }

            string scope = session.Scope == ColliderAuthoringScope.CU ? "CU" : "CG";
            string target = session.Scope == ColliderAuthoringScope.CU
                ? $"instance:{session.InstanceId}"
                : string.IsNullOrEmpty(session.ImageKey) ? "image:(none)" : $"image:{session.ImageKey}";
            string dirty = IsSessionDirty(session) ? "Dirty" : "Saved";
            int solids = CountSolidCells(session.WorkingGrid);
            _uiRefs.CollTargetText.text = $"ID {session.InstanceId} | Scope {scope}\n{target}";
            _uiRefs.CollStateText.text =
                $"Grid: {session.WorkingGrid.width}x{session.WorkingGrid.height} | Solids {solids} | {dirty} | Brush {brushLabel} x{_collBrushSize}";
        }

        // Reusable scratch buffer for authoring-cell computation. Authoring cell
        // sets are pushed to overlays via IList<Rect>; the overlay copies the
        // contents into its own array, so we can safely reuse this list across
        // every building/frame and avoid the per-call List<Rect>(256) allocation.
        private readonly List<Rect> _authoringCellsScratch = new List<Rect>(256);

        // Cached BuildingObject snapshot used by full-refresh paths. Invalidated
        // by InvalidateBuildingCache() whenever the editor knows the set may
        // have changed (placement, deletion, undo/redo, scene reload). Avoids
        // repeated FindObjectsOfType allocations.
        private BuildingObject[] _buildingsCache;
        private bool             _buildingsCacheValid;

        internal void InvalidateBuildingCache()
        {
            _buildingsCacheValid = false;
        }

        private BuildingObject[] GetCachedBuildings()
        {
            if (!_buildingsCacheValid || _buildingsCache == null)
            {
                _buildingsCache = FindObjectsOfType<BuildingObject>();
                _buildingsCacheValid = true;
            }
            return _buildingsCache;
        }

        /// <summary>
        /// Per-frame fast path: refresh only the ACTIVE building's overlay
        /// cells. The other buildings are static while the editor is open, so
        /// they keep whatever cells the last full RefreshCollidersOverlay()
        /// pushed. This is the difference between 20 fps and 120+ fps when
        /// Show Colliders is on with many buildings in the scene.
        /// </summary>
        private void RefreshActiveBuildingOverlayCells()
        {
            if (!_collidersVisible || _activeBuilding == null) return;
            var overlay = _activeBuilding.GetComponent<BuildingColliderDebugOverlay>();
            if (overlay == null) return;
            int filled = ComputeAuthoringCellsInto(_activeBuilding, _authoringCellsScratch);
            if (filled > 0)
                overlay.SetAuthoringCells(_authoringCellsScratch);
            else
                overlay.ClearAuthoringCells();
        }

        private int RefreshCollidersOverlay()
        {
            // Compute authoring cells (the editor's working grid in world space)
            // for EVERY building in the scene — not only the currently active
            // one. This guarantees that when the user toggles "Show Colliders"
            // ON, every building's authored collision rectangles light up at
            // exactly the position where the BoxCollider2D children sit. For
            // buildings with no authored data (no editor-stored grid AND no
            // JSON grid) the overlay falls back to enumerating its own
            // BoxCollider2D children (root footprint, etc.) so the user always
            // sees SOMETHING when a building has any physical collider at all.
            //
            // Heavy path — invoked only on toggle, SetActiveBuilding, brush
            // stroke end, undo/redo, and other structural changes. Per-frame
            // updates use the lighter RefreshActiveBuildingOverlayCells.
            int total = 0;
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                var overlay = b.GetComponent<BuildingColliderDebugOverlay>();
                if (overlay == null)
                    overlay = b.gameObject.AddComponent<BuildingColliderDebugOverlay>();

                if (_collidersVisible)
                {
                    int filled = ComputeAuthoringCellsInto(b, _authoringCellsScratch);
                    if (filled > 0)
                        overlay.SetAuthoringCells(_authoringCellsScratch);
                    else
                        overlay.ClearAuthoringCells();
                }
                else
                {
                    overlay.ClearAuthoringCells();
                }

                overlay.SetVisible(_collidersVisible);
                if (_collidersVisible)
                    total += overlay.CurrentVisualCount;
            }

            return total;
        }

        /// <summary>
        /// Build the world-space cell rects for ANY building's overlay using
        /// the SAME rect/grid math <see cref="HandleColliderPaint"/> uses to
        /// map mouse clicks AND the same math <see cref="EnsureCollTile"/>
        /// uses to place physical BoxCollider2D children. Resolution order
        /// matches <see cref="ApplyCollisionStateForBuilding"/>:
        ///   1. Editor-stored CU grid (per instance), if scope = CU.
        ///   2. Editor-stored CG grid (per image).
        ///   3. JSON grid loaded by <see cref="BuildingCollisionLoader"/>.
        /// Returns null/empty when no authored data exists; callers fall back
        /// to BoxCollider2D enumeration so root-collider buildings still show.
        /// </summary>
        private List<Rect> TryComputeAuthoringCellsFor(BuildingObject building)
        {
            // Allocating overload kept for back-compat with any external caller.
            // Internal hot paths use ComputeAuthoringCellsInto with a shared
            // scratch buffer to avoid per-frame allocations.
            var cells = new List<Rect>(64);
            int filled = ComputeAuthoringCellsInto(building, cells);
            return filled > 0 ? cells : null;
        }

        /// <summary>
        /// Allocation-free variant: fills <paramref name="cells"/> with the
        /// world-space rects of every solid ("#") cell in <paramref name="building"/>'s
        /// authoring grid and returns the count. The list is cleared first so
        /// callers can reuse a single shared buffer across frames/buildings.
        /// </summary>
        private int ComputeAuthoringCellsInto(BuildingObject building, List<Rect> cells)
        {
            cells.Clear();
            if (building == null || building.Template == null) return 0;
            if (!building.TryGetWorldRect(out var rect) || rect.width <= 0f || rect.height <= 0f) return 0;

            ColliderGridData grid = ResolveStoredGridForOverlay(building);
            if (grid == null || grid.collision == null || grid.height <= 0 || grid.width <= 0) return 0;

            int rows = grid.height;
            int cols = grid.width;
            for (int row = 0; row < rows; row++)
            {
                var rowArr = grid.collision[row];
                if (rowArr == null) continue;
                for (int col = 0; col < cols && col < rowArr.Length; col++)
                {
                    if (rowArr[col] != "#") continue;
                    if (building.TryGetWorldCellRect(row, col, rows, cols, out var cell))
                        cells.Add(cell);
                }
            }
            return cells.Count;
        }

        /// <summary>
        /// Resolve the collision grid the overlay should mirror for this
        /// building. For the active building we prefer the in-progress
        /// WorkingGrid (un-saved edits visible immediately); for the others
        /// we hit the editor stores and finally the runtime JSON loader so
        /// every building reflects its true authored state.
        /// </summary>
        private ColliderGridData ResolveStoredGridForOverlay(BuildingObject building)
        {
            EnsureColliderDataLoaded();

            if (building == _activeBuilding)
            {
                var session = EnsureActiveColliderSession();
                if (session != null && session.WorkingGrid != null)
                    return session.WorkingGrid;
            }

            Vector2Int effectiveSize = GetEffectivePixelSize(building);
            if (string.Equals(building.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase) &&
                _colliderInstanceStore.TryGetValue(building.InstanceId, out var instanceGrid))
            {
                return ResampleGrid(instanceGrid, effectiveSize.x, effectiveSize.y);
            }

            string imageKey = NormalizeAssetPath(building.Template.sourceImagePath);
            if (!string.IsNullOrEmpty(imageKey) &&
                _colliderImageStore.TryGetValue(imageKey, out var imageGrid))
            {
                return ResampleGrid(imageGrid, effectiveSize.x, effectiveSize.y);
            }

            // Note: the editor stores (_colliderImageStore / _colliderInstanceStore)
            // are populated from BOTH the live editor session AND the JSON files
            // loaded by EnsureColliderDataLoaded → so checking them is enough,
            // there is no need to also poll BuildingCollisionLoader here.
            return null;
        }

        /// <summary>
        /// Backwards-compatible wrapper kept for any external call sites; the
        /// overlay refresh now uses <see cref="TryComputeAuthoringCellsFor"/>
        /// directly so EVERY building (not just the active one) lights up.
        /// </summary>
        private List<Rect> TryComputeActiveAuthoringCells(out BuildingColliderDebugOverlay overlay)
        {
            overlay = null;
            if (_activeBuilding == null) return null;
            overlay = _activeBuilding.GetComponent<BuildingColliderDebugOverlay>();
            if (overlay == null)
                overlay = _activeBuilding.gameObject.AddComponent<BuildingColliderDebugOverlay>();
            return TryComputeAuthoringCellsFor(_activeBuilding);
        }

        private void ReapplyAllColliderStates()
        {
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                ApplyCollisionStateForBuilding(all[i]);
            }
        }

        private void BeginColliderStroke()
        {
            if (_colliderStroke.Active) return;
            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null) return;

            _colliderStroke.Active = true;
            _colliderStroke.Scope = session.Scope;
            _colliderStroke.ImageKey = session.ImageKey;
            _colliderStroke.InstanceId = session.InstanceId;
            _colliderStroke.Before = CloneGrid(session.WorkingGrid);
            _colliderStroke.Changed = false;
        }

        private void EndColliderStroke()
        {
            if (!_colliderStroke.Active) return;

            var strokeScope = _colliderStroke.Scope;
            string strokeImageKey = _colliderStroke.ImageKey;
            int strokeInstanceId = _colliderStroke.InstanceId;
            var before = CloneGrid(_colliderStroke.Before);
            var after = CloneGrid(GetStoredGrid(strokeScope, strokeImageKey, strokeInstanceId));
            bool changed = _colliderStroke.Changed && !GridEquals(before, after);

            _colliderStroke.Active = false;
            _colliderStroke.Before = null;
            _colliderStroke.Changed = false;

            if (!changed || after == null) return;

            _undo.Do("Paint colliders",
                () => ApplyGridSnapshot(strokeScope, strokeImageKey, strokeInstanceId, after),
                () => ApplyGridSnapshot(strokeScope, strokeImageKey, strokeInstanceId, before));
            // Auto-save: persist JSON immediately so the user never needs to press
            // "Save Colliders" manually after painting or erasing.
            SaveColliderAuthoring();
        }

        private void HandleColliderPaint(Vector3 worldPos)
        {
            if (_collBrushMode == CollBrushMode.Off) return;
            if (_activeBuilding == null || _activeBuilding.Template == null) return;
            if (!_activeBuilding.TryGetWorldRect(out var rect) || !rect.Contains(worldPos)) return;

            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null || session.WorkingGrid.width <= 0 || session.WorkingGrid.height <= 0)
                return;

            float u = Mathf.Clamp01((worldPos.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((worldPos.y - rect.yMin) / rect.height);
            int col = Mathf.Clamp(Mathf.FloorToInt(u * session.WorkingGrid.width), 0, session.WorkingGrid.width - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((1f - v) * session.WorkingGrid.height), 0, session.WorkingGrid.height - 1);

            int half = (_collBrushSize - 1) / 2;
            int extra = (_collBrushSize - 1) - half;
            // CollBrushMode.Erase is an internal enum value that is no longer
            // reachable from the redesigned UX (the UI "Erase" button maps to
            // CollBrushMode.Walk). Kept in the enum for undo-snapshot compatibility.

            bool changed = false;
            for (int dr = -half; dr <= extra; dr++)
            {
                for (int dc = -half; dc <= extra; dc++)
                {
                    int r = row + dr;
                    int c = col + dc;
                    if (r < 0 || r >= session.WorkingGrid.height || c < 0 || c >= session.WorkingGrid.width)
                        continue;

                    // Solid mode writes "#"; Walk (UI "Erase") writes ".".
                    string next = _collBrushMode == CollBrushMode.Solid ? "#" : ".";

                    if (session.WorkingGrid.collision[r][c] == next) continue;
                    session.WorkingGrid.collision[r][c] = next;
                    changed = true;
                }
            }

            if (!changed) return;

            PersistSessionToStore(session);
            _colliderStroke.Changed = true;
            // During an active stroke we skip the heavy ApplyCollisionTargetsFor
            // (FindObjectsOfType + ClearCollisionTiles + EnsureCollTile for every
            // building that shares this image key) to avoid 1-fps stalls on large scenes.
            // Physical colliders are synced exactly once when the stroke ends via
            // EndColliderStroke → UndoStack.Do → ApplyGridSnapshot → ApplyCollisionTargetsFor.
            // For live visual feedback we only refresh the active building's overlay cells.
            RefreshActiveBuildingOverlayCells();
            RefreshCollidersPanel();
        }

        // NOTE: Quick Actions (Fill / Clear / Revert) were removed by user request
        // to keep the colliders authoring UX strictly brush-driven (paint vs. erase).
        // Bulk operations are now achieved with a large brush size on top of LMB-drag.

        private void SaveColliderAuthoring()
        {
            SaveInstancesToJson();
        }

        /// <summary>
        /// Wipes all existing collision authoring data and assigns an all-walkable
        /// (all "." cells) CU-scope grid to every building so the user can repaint
        /// from scratch. All-walkable CU grids are preserved across sessions because
        /// the per-instance JSON loaders no longer apply the GridHasSolidCells filter.
        /// </summary>
        private void ResetAllCollidersToWalkable()
        {
            EnsureColliderDataLoaded();

            // Clear both in-memory stores and the active authoring session.
            _colliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _activeColliderSession = null;

            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;

                // Force per-instance (CU) scope so every building gets its own
                // all-walkable grid regardless of previous scope setting.
                b.ColliderScopeOverride = "CU";

                var sz       = GetEffectivePixelSize(b);
                int cols     = Mathf.Max(1, Mathf.CeilToInt(sz.x / 32f));
                int rows     = Mathf.Max(1, Mathf.CeilToInt(sz.y / 32f));
                var walkable = CreateEmptyGrid(cols, rows, sz); // all "." cells

                _colliderInstanceStore[b.InstanceId] = walkable;
                ApplyGridOverrideToBuilding(b, walkable);
            }

            InvalidateBuildingCache();
            SaveColliderAuthoring();

            if (_collidersVisible)
            {
                Physics2D.SyncTransforms();
                RefreshCollidersOverlay();
            }
            RefreshCollidersPanel();
            Toast("All colliders reset to walkable. Paint solid cells from scratch.");
            Debug.Log($"[BuildingsEditor] ResetAllCollidersToWalkable — {all.Length} buildings cleared.");
        }

        private void ResetColliderAuthoringState()
        {
            _colliderDataLoaded = false;
            _colliderImageStore.Clear();
            _savedColliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _savedColliderInstanceStore.Clear();
            _activeColliderSession = null;
            _colliderStroke.Active = false;
            _colliderStroke.Before = null;
            _colliderStroke.Changed = false;
        }

        private void EnsureColliderDataLoaded()
        {
            if (_colliderDataLoaded) return;

            _colliderImageStore.Clear();
            _savedColliderImageStore.Clear();
            _colliderInstanceStore.Clear();
            _savedColliderInstanceStore.Clear();

            LoadCollisionImageStore(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_collisions_by_image.json"), _colliderImageStore);
            LoadCollisionInstanceStore(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_collisions_by_building_instance_id.json"), _colliderInstanceStore);
            LoadInlineInstanceColliders(Path.Combine(Application.streamingAssetsPath, "Buildings", "buildings_instances.json"), _colliderInstanceStore);

            CopyStore(_colliderImageStore, _savedColliderImageStore);
            CopyStore(_colliderInstanceStore, _savedColliderInstanceStore);
            _colliderDataLoaded = true;
            _activeColliderSession = null;
        }

        private ActiveColliderGridSession EnsureActiveColliderSession()
        {
            if (_activeBuilding == null || _activeBuilding.Template == null) return null;
            EnsureColliderDataLoaded();

            Vector2Int effectiveSize = GetEffectivePixelSize(_activeBuilding);
            string imageKey = NormalizeAssetPath(_activeBuilding.Template.sourceImagePath);
            ColliderAuthoringScope scope = string.Equals(
                _activeBuilding.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase)
                ? ColliderAuthoringScope.CU
                : ColliderAuthoringScope.CG;

            if (_activeColliderSession != null &&
                _activeColliderSession.BuildingId == _activeBuilding.GetInstanceID() &&
                _activeColliderSession.InstanceId == _activeBuilding.InstanceId &&
                _activeColliderSession.Scope == scope &&
                string.Equals(_activeColliderSession.ImageKey, imageKey, StringComparison.OrdinalIgnoreCase) &&
                _activeColliderSession.EffectivePixelSize == effectiveSize)
            {
                return _activeColliderSession;
            }

            _activeColliderSession = new ActiveColliderGridSession
            {
                BuildingId = _activeBuilding.GetInstanceID(),
                InstanceId = _activeBuilding.InstanceId,
                ImageKey = imageKey,
                Scope = scope,
                EffectivePixelSize = effectiveSize,
                WorkingGrid = ResolveWorkingGridFor(_activeBuilding, scope, imageKey, _activeBuilding.InstanceId, effectiveSize)
            };
            return _activeColliderSession;
        }

        private ColliderGridData ResolveWorkingGridFor(
            BuildingObject building,
            ColliderAuthoringScope scope,
            string imageKey,
            int instanceId,
            Vector2Int effectiveSize)
        {
            if (scope == ColliderAuthoringScope.CU &&
                _colliderInstanceStore.TryGetValue(instanceId, out var instanceGrid))
            {
                return ResampleGrid(instanceGrid, effectiveSize.x, effectiveSize.y);
            }

            if (!string.IsNullOrEmpty(imageKey) &&
                _colliderImageStore.TryGetValue(imageKey, out var sharedGrid))
            {
                return ResampleGrid(sharedGrid, effectiveSize.x, effectiveSize.y);
            }

            return CreateDefaultFootprintGrid(building, effectiveSize);
        }

        private ColliderGridData CreateFallbackGridFor(BuildingObject building, ActiveColliderGridSession session)
        {
            if (session == null) return null;
            if (session.Scope == ColliderAuthoringScope.CU &&
                !string.IsNullOrEmpty(session.ImageKey) &&
                _colliderImageStore.TryGetValue(session.ImageKey, out var sharedGrid))
            {
                return ResampleGrid(sharedGrid, session.EffectivePixelSize.x, session.EffectivePixelSize.y);
            }

            return CreateDefaultFootprintGrid(building, session.EffectivePixelSize);
        }

        private static Vector2Int GetEffectivePixelSize(BuildingObject building)
        {
            if (building == null || building.Template == null) return Vector2Int.zero;
            int effW = (building.ScaleOverride.x > 0) ? building.ScaleOverride.x : building.Template.originalScale.x;
            int effH = (building.ScaleOverride.y > 0) ? building.ScaleOverride.y : building.Template.originalScale.y;
            return new Vector2Int(effW, effH);
        }

        private static ColliderGridData CreateDefaultFootprintGrid(BuildingObject building, Vector2Int effectiveSize)
        {
            int cols = Mathf.Max(1, Mathf.CeilToInt(effectiveSize.x / 32f));
            int rows = Mathf.Max(1, Mathf.CeilToInt(effectiveSize.y / 32f));
            var grid = CreateEmptyGrid(cols, rows, effectiveSize);
            if (building == null || building.Template == null || !building.Template.solid)
                return grid;

            // Only mark footprint rows as solid, matching BuildingObject.Apply() which
            // sizes the root BoxCollider2D to the footprint (below the split line) only.
            // Row 0 = top of building (canopy), Row rows-1 = bottom (footprint base).
            // footprintStartRow = first grid row (counting from top=0) that is inside
            // the footprint: footprintStartRow = ceil(rows * splitRatio).
            float splitRatio = (building.SplitRatioOverride >= 0f)
                ? building.SplitRatioOverride
                : (building.Template.splitRatio);
            int footprintStartRow = Mathf.Clamp(Mathf.CeilToInt(rows * splitRatio), 0, rows);
            for (int row = footprintStartRow; row < rows; row++)
                for (int col = 0; col < cols; col++)
                    grid.collision[row][col] = "#";

            return grid;
        }

        private static ColliderGridData CreateEmptyGrid(int cols, int rows, Vector2Int effectiveSize)
        {
            var collision = new string[rows][];
            for (int row = 0; row < rows; row++)
            {
                collision[row] = new string[cols];
                for (int col = 0; col < cols; col++)
                    collision[row][col] = ".";
            }

            return new ColliderGridData
            {
                width = cols,
                height = rows,
                collision = collision,
                gridRefSize = effectiveSize
            };
        }

        private static ColliderGridData CloneGrid(ColliderGridData source)
        {
            if (source == null) return null;

            var clone = new ColliderGridData
            {
                width = source.width,
                height = source.height,
                gridRefSize = source.gridRefSize,
                collision = new string[source.height][]
            };

            for (int row = 0; row < source.height; row++)
            {
                clone.collision[row] = new string[source.width];
                if (source.collision == null || row >= source.collision.Length || source.collision[row] == null)
                {
                    for (int col = 0; col < source.width; col++)
                        clone.collision[row][col] = ".";
                    continue;
                }

                for (int col = 0; col < source.width; col++)
                {
                    clone.collision[row][col] = col < source.collision[row].Length
                        ? (source.collision[row][col] ?? ".")
                        : ".";
                }
            }

            return clone;
        }

        private static void CopyStore(Dictionary<string, ColliderGridData> source, Dictionary<string, ColliderGridData> destination)
        {
            destination.Clear();
            foreach (var kvp in source)
                destination[kvp.Key] = CloneGrid(kvp.Value);
        }

        private static void CopyStore(Dictionary<int, ColliderGridData> source, Dictionary<int, ColliderGridData> destination)
        {
            destination.Clear();
            foreach (var kvp in source)
                destination[kvp.Key] = CloneGrid(kvp.Value);
        }

        private static int CountSolidCells(ColliderGridData grid)
        {
            if (grid == null || grid.collision == null) return 0;
            int count = 0;
            for (int row = 0; row < grid.collision.Length; row++)
            {
                if (grid.collision[row] == null) continue;
                for (int col = 0; col < grid.collision[row].Length; col++)
                {
                    if (grid.collision[row][col] == "#")
                        count++;
                }
            }
            return count;
        }

        private static bool GridHasSolidCells(ColliderGridData grid) => CountSolidCells(grid) > 0;

        private static bool GridEquals(ColliderGridData a, ColliderGridData b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.width != b.width || a.height != b.height || a.gridRefSize != b.gridRefSize) return false;

            for (int row = 0; row < a.height; row++)
            {
                for (int col = 0; col < a.width; col++)
                {
                    string av = (a.collision != null && row < a.collision.Length && a.collision[row] != null && col < a.collision[row].Length)
                        ? (a.collision[row][col] ?? ".")
                        : ".";
                    string bv = (b.collision != null && row < b.collision.Length && b.collision[row] != null && col < b.collision[row].Length)
                        ? (b.collision[row][col] ?? ".")
                        : ".";
                    if (!string.Equals(av, bv, StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        private static string NormalizeAssetPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\\", "/");
        }

        private bool IsSessionDirty(ActiveColliderGridSession session)
        {
            if (session == null) return false;
            if (session.Scope == ColliderAuthoringScope.CU)
            {
                _colliderInstanceStore.TryGetValue(session.InstanceId, out var current);
                _savedColliderInstanceStore.TryGetValue(session.InstanceId, out var saved);
                return !GridEquals(current, saved);
            }

            string key = session.ImageKey ?? string.Empty;
            _colliderImageStore.TryGetValue(key, out var currentImage);
            _savedColliderImageStore.TryGetValue(key, out var savedImage);
            return !GridEquals(currentImage, savedImage);
        }

        private void PersistSessionToStore(ActiveColliderGridSession session)
        {
            if (session == null || session.WorkingGrid == null) return;

            var snapshot = CloneGrid(session.WorkingGrid);
            if (session.Scope == ColliderAuthoringScope.CU)
                _colliderInstanceStore[session.InstanceId] = snapshot;
            else if (!string.IsNullOrEmpty(session.ImageKey))
                _colliderImageStore[session.ImageKey] = snapshot;
        }

        private ColliderGridData GetStoredGrid(ColliderAuthoringScope scope, string imageKey, int instanceId)
        {
            if (scope == ColliderAuthoringScope.CU)
            {
                _colliderInstanceStore.TryGetValue(instanceId, out var instanceGrid);
                return instanceGrid;
            }

            _colliderImageStore.TryGetValue(imageKey ?? string.Empty, out var imageGrid);
            return imageGrid;
        }

        private void ApplyGridSnapshot(ColliderAuthoringScope scope, string imageKey, int instanceId, ColliderGridData grid)
        {
            EnsureColliderDataLoaded();

            if (scope == ColliderAuthoringScope.CU)
            {
                if (grid == null) _colliderInstanceStore.Remove(instanceId);
                else _colliderInstanceStore[instanceId] = CloneGrid(grid);
            }
            else
            {
                string key = imageKey ?? string.Empty;
                if (grid == null) _colliderImageStore.Remove(key);
                else _colliderImageStore[key] = CloneGrid(grid);
            }

            _activeColliderSession = null;
            ApplyCollisionTargetsFor(scope, imageKey, instanceId);
            RefreshCollidersPanel();
        }

        private void ApplyCollisionTargetsFor(ColliderAuthoringScope scope, string imageKey, int instanceId)
        {
            // Use the cached snapshot — this is only called from structural-change sites
            // (stroke end, undo/redo, scope change) so the cache is either already valid
            // or correctly invalidated before the call.
            var all = GetCachedBuildings();
            if (scope == ColliderAuthoringScope.CU)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].InstanceId == instanceId)
                    {
                        ApplyCollisionStateForBuilding(all[i]);
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var b = all[i];
                    if (b == null || b.Template == null) continue;
                    if (!string.Equals(NormalizeAssetPath(b.Template.sourceImagePath), imageKey ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                        continue;
                    ApplyCollisionStateForBuilding(b);
                }
            }

            if (_collidersVisible)
            {
                Physics2D.SyncTransforms();
                RefreshCollidersOverlay();
            }
        }

        private void ApplyCollisionStateForBuilding(BuildingObject building)
        {
            if (building == null) return;

            if (!TryApplyAuthoredGrid(building))
            {
                var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
                if (collisionLoader != null)
                    collisionLoader.TryApplyGrid(building);
                else
                    ApplyGridOverrideToBuilding(building, null);
            }
        }

        private bool TryApplyAuthoredGrid(BuildingObject building)
        {
            if (building == null || building.Template == null) return false;
            EnsureColliderDataLoaded();

            if (string.Equals(building.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase) &&
                _colliderInstanceStore.TryGetValue(building.InstanceId, out var instanceGrid))
            {
                ApplyGridOverrideToBuilding(building, instanceGrid);
                return true;
            }

            string imageKey = NormalizeAssetPath(building.Template.sourceImagePath);
            if (!string.IsNullOrEmpty(imageKey) && _colliderImageStore.TryGetValue(imageKey, out var imageGrid))
            {
                ApplyGridOverrideToBuilding(building, imageGrid);
                return true;
            }

            return false;
        }

        private void ApplyGridOverrideToBuilding(BuildingObject building, ColliderGridData grid)
        {
            if (building == null || building.Template == null) return;

            ClearCollisionTiles(building);
            RestoreDefaultColliderState(building);

            if (grid == null) return;

            Vector2Int effectiveSize = GetEffectivePixelSize(building);
            var effectiveGrid = ResampleGrid(grid, effectiveSize.x, effectiveSize.y);
            if (effectiveGrid == null) return;

            // Apply every authored cell, even if none are solid — the user may have
            // deliberately erased all "#" cells to make a building fully walk-through.
            // All-walkable grids loaded from JSON are already filtered out at load time
            // (see LoadCollisionImageStore / LoadCollisionInstanceStore), so reaching
            // this point with zero solid cells always reflects an explicit user edit.
            // When zero CollTiles are created below, the main BoxCollider2D is still
            // disabled at the end, correctly leaving the building with no collision.
            for (int row = 0; row < effectiveGrid.height; row++)
            {
                if (effectiveGrid.collision == null || row >= effectiveGrid.collision.Length || effectiveGrid.collision[row] == null)
                    continue;

                for (int col = 0; col < effectiveGrid.width; col++)
                {
                    if (col >= effectiveGrid.collision[row].Length || effectiveGrid.collision[row][col] != "#")
                        continue;
                    EnsureCollTile(building, row, col, effectiveGrid.height, effectiveGrid.width);
                }
            }

            var main = building.GetComponent<BoxCollider2D>();
            if (main != null)
                main.enabled = false;
        }

        private void EnsureCollTile(BuildingObject building, int row, int col, int rows, int cols)
        {
            string childName = $"{CollTilePrefix}{row}_{col}";
            Transform tileTransform = building.transform.Find(childName);
            if (tileTransform == null)
                tileTransform = TryReusePooledCollTile(building.transform, childName);

            if (tileTransform == null)
            {
                var tileGo = new GameObject(childName);
                tileGo.transform.SetParent(building.transform, worldPositionStays: false);
                tileTransform = tileGo.transform;
            }

            // Single source of truth: derive the cell's WORLD rect from the
            // building's own helper so this BoxCollider2D, the visual overlay
            // and the click-to-paint hit test all share one coordinate system.
            // Then convert center+size into the building's local space (taking
            // its lossy scale into account so non-uniform scales are correct).
            if (!building.TryGetWorldCellRect(row, col, rows, cols, out var worldCell))
            {
                Debug.LogWarning(
                    $"[BuildingsRuntimeEditor] Could not compute world cell rect for {building.name} cell ({row},{col}) — collider skipped.",
                    building);
                tileTransform.gameObject.SetActive(false);
                return;
            }

            Vector3 worldCenter = new Vector3(worldCell.center.x, worldCell.center.y, 0f);
            Vector3 localCenter = building.transform.InverseTransformPoint(worldCenter);
            Vector3 lossy = building.transform.lossyScale;
            float invSx = Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f;
            float invSy = Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f;
            Vector2 localSize = new Vector2(worldCell.width * invSx, worldCell.height * invSy);

            tileTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            tileTransform.localRotation = Quaternion.identity;
            tileTransform.localScale = Vector3.one;
            tileTransform.gameObject.layer = ResolveCollisionLayer();
            tileTransform.gameObject.SetActive(true);

            var box = tileTransform.GetComponent<BoxCollider2D>();
            if (box == null)
                box = tileTransform.gameObject.AddComponent<BoxCollider2D>();
            box.enabled = true;
            box.isTrigger = false; // explicit: must block movement, not just detect
            box.offset = Vector2.zero;
            box.size = localSize;
        }

        private static Transform TryReusePooledCollTile(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.name.StartsWith(PooledCollTilePrefix, StringComparison.Ordinal))
                    continue;

                child.name = childName;
                return child;
            }

            return null;
        }

        private static Vector2 GetBuildingLocalSpriteSize(BuildingObject building)
        {
            float width = 0f;
            float height = 0f;

            var footprint = building.transform.Find("Footprint")?.GetComponent<SpriteRenderer>();
            if (footprint != null && footprint.sprite != null)
            {
                width = Mathf.Max(width, footprint.sprite.rect.width / 32f);
                height += footprint.sprite.rect.height / 32f;
            }

            var canopy = building.transform.Find("Canopy")?.GetComponent<SpriteRenderer>();
            if (canopy != null && canopy.sprite != null)
            {
                width = Mathf.Max(width, canopy.sprite.rect.width / 32f);
                height += canopy.sprite.rect.height / 32f;
            }

            var mainCollider = building.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
            {
                width = Mathf.Max(width, mainCollider.size.x);
                height = Mathf.Max(height, mainCollider.offset.y + mainCollider.size.y * 0.5f);
            }

            return new Vector2(
                Mathf.Max(0.0001f, width),
                Mathf.Max(0.0001f, height));
        }

        private static void ClearCollisionTiles(BuildingObject building)
        {
            if (building == null) return;

            int pooledIndex = 0;
            for (int i = building.transform.childCount - 1; i >= 0; i--)
            {
                var child = building.transform.GetChild(i);
                if (!child.name.StartsWith(CollTilePrefix, StringComparison.Ordinal) &&
                    !child.name.StartsWith(PooledCollTilePrefix, StringComparison.Ordinal))
                    continue;

                child.name = $"{PooledCollTilePrefix}{pooledIndex++}";
                var box = child.GetComponent<BoxCollider2D>();
                if (box != null)
                    box.enabled = false;
                child.gameObject.SetActive(false);
            }
        }

        private static void RestoreDefaultColliderState(BuildingObject building)
        {
            if (building == null || building.Template == null) return;
            var mainCollider = building.GetComponent<BoxCollider2D>();
            if (mainCollider != null)
                mainCollider.enabled = building.Template.solid;
        }

        private static ColliderGridData ResampleGrid(ColliderGridData source, int targetW_px, int targetH_px)
        {
            if (source == null) return null;
            if (source.gridRefSize == Vector2Int.zero ||
                (source.gridRefSize.x == targetW_px && source.gridRefSize.y == targetH_px))
            {
                return CloneGrid(source);
            }

            int newCols = Mathf.Max(1, Mathf.CeilToInt(targetW_px / 32f));
            int newRows = Mathf.Max(1, Mathf.CeilToInt(targetH_px / 32f));
            if (newCols == source.width && newRows == source.height)
            {
                var sameSizeClone = CloneGrid(source);
                sameSizeClone.gridRefSize = new Vector2Int(targetW_px, targetH_px);
                return sameSizeClone;
            }

            var newGrid = CreateEmptyGrid(newCols, newRows, new Vector2Int(targetW_px, targetH_px));
            for (int dr = 0; dr < newRows; dr++)
            {
                for (int dc = 0; dc < newCols; dc++)
                {
                    float srcRowStart = (float)dr / newRows * source.height;
                    float srcRowEnd = (float)(dr + 1) / newRows * source.height;
                    float srcColStart = (float)dc / newCols * source.width;
                    float srcColEnd = (float)(dc + 1) / newCols * source.width;

                    bool solid = false;
                    for (int sr = Mathf.FloorToInt(srcRowStart); sr < Mathf.CeilToInt(srcRowEnd) && sr < source.height; sr++)
                    {
                        for (int sc = Mathf.FloorToInt(srcColStart); sc < Mathf.CeilToInt(srcColEnd) && sc < source.width; sc++)
                        {
                            if (source.collision != null &&
                                sr < source.collision.Length &&
                                source.collision[sr] != null &&
                                sc < source.collision[sr].Length &&
                                source.collision[sr][sc] == "#")
                            {
                                solid = true;
                                break;
                            }
                        }
                        if (solid) break;
                    }
                    newGrid.collision[dr][dc] = solid ? "#" : ".";
                }
            }

            return newGrid;
        }

        private static ColliderGridData ParseColliderGrid(Dictionary<string, object> dict)
        {
            if (dict == null) return null;
            int width = dict.TryGetValue("width", out var w) ? Convert.ToInt32(w) : 0;
            int height = dict.TryGetValue("height", out var h) ? Convert.ToInt32(h) : 0;
            if (width <= 0 || height <= 0) return null;

            var grid = CreateEmptyGrid(width, height, Vector2Int.zero);
            if (dict.TryGetValue("collision", out var collisionRaw) && collisionRaw is List<object> rows)
            {
                for (int row = 0; row < Mathf.Min(height, rows.Count); row++)
                {
                    if (!(rows[row] is List<object> cols)) continue;
                    for (int col = 0; col < Mathf.Min(width, cols.Count); col++)
                        grid.collision[row][col] = cols[col]?.ToString() == "#" ? "#" : ".";
                }
            }

            if (dict.TryGetValue("grid_ref_size", out var refRaw) && refRaw is List<object> refList && refList.Count >= 2)
            {
                grid.gridRefSize = new Vector2Int(Convert.ToInt32(refList[0]), Convert.ToInt32(refList[1]));
            }

            return grid;
        }

        private static void LoadCollisionImageStore(string path, Dictionary<string, ColliderGridData> destination)
        {
            destination.Clear();
            if (!File.Exists(path)) return;

            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return;

            foreach (var kvp in root)
            {
                if (!(kvp.Value is Dictionary<string, object> dict)) continue;
                var grid = ParseColliderGrid(dict);
                // Skip all-walkable JSON entries: they are unintentional placeholders
                // written by old editor versions into the CG (per-image) store.
                // Per-instance (CU) stores keep all-walkable grids so that an
                // intentional "reset all to walkable" survives across sessions.
                if (grid != null && GridHasSolidCells(grid))
                    destination[NormalizeAssetPath(kvp.Key)] = grid;
            }
        }

        private static void LoadCollisionInstanceStore(string path, Dictionary<int, ColliderGridData> destination)
        {
            if (!File.Exists(path)) return;

            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return;

            foreach (var kvp in root)
            {
                if (!(kvp.Value is Dictionary<string, object> dict)) continue;
                var grid = ParseColliderGrid(dict);
                // Per-instance (CU) grids: no solid-cell filter. An all-walkable grid
                // here is intentional (e.g. produced by "Reset all to walkable").
                if (grid != null && int.TryParse(kvp.Key, out int id))
                    destination[id] = grid;
            }
        }

        private static void LoadInlineInstanceColliders(string path, Dictionary<int, ColliderGridData> destination)
        {
            if (!File.Exists(path)) return;

            var raw = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as List<object>;
            if (raw == null) return;

            for (int i = 0; i < raw.Count; i++)
            {
                if (!(raw[i] is Dictionary<string, object> entry)) continue;
                if (!entry.TryGetValue("id", out var idRaw) || idRaw == null) continue;
                if (!entry.TryGetValue("overrides", out var overridesRaw) || !(overridesRaw is Dictionary<string, object> overrides)) continue;
                if (!overrides.TryGetValue("collision_override", out var collisionRaw) || !(collisionRaw is Dictionary<string, object> collisionDict)) continue;
                var grid = ParseColliderGrid(collisionDict);
                // Per-instance (CU) inline grids: no solid-cell filter (same as
                // LoadCollisionInstanceStore — all-walkable may be intentional).
                if (grid != null)
                    destination[Convert.ToInt32(idRaw)] = grid;
            }
        }

        private void WriteColliderStoresToDisk(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            File.WriteAllText(
                Path.Combine(directoryPath, "buildings_collisions_by_image.json"),
                SerializeCollisionStore(_colliderImageStore));
            File.WriteAllText(
                Path.Combine(directoryPath, "buildings_collisions_by_building_instance_id.json"),
                SerializeCollisionStore(_colliderInstanceStore));

            CopyStore(_colliderImageStore, _savedColliderImageStore);
            CopyStore(_colliderInstanceStore, _savedColliderInstanceStore);
        }

        private static string SerializeCollisionStore(Dictionary<string, ColliderGridData> store)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = store.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                sb.Append("  \"").Append(EscapeJson(key)).Append("\": ");
                AppendGridJson(sb, store[key], 2);
                if (i < keys.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string SerializeCollisionStore(Dictionary<int, ColliderGridData> store)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var keys = store.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                int key = keys[i];
                sb.Append("  \"").Append(key).Append("\": ");
                AppendGridJson(sb, store[key], 2);
                if (i < keys.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendGridJson(StringBuilder sb, ColliderGridData grid, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 2);
            string childIndent = indent + "  ";
            string rowIndent = childIndent + "  ";

            sb.AppendLine("{");
            sb.Append(childIndent).Append("\"width\": ").Append(grid?.width ?? 0).AppendLine(",");
            sb.Append(childIndent).Append("\"height\": ").Append(grid?.height ?? 0).AppendLine(",");
            sb.Append(childIndent).Append("\"collision\": [").AppendLine();
            for (int row = 0; row < (grid?.height ?? 0); row++)
            {
                sb.Append(rowIndent).Append("[");
                for (int col = 0; col < grid.width; col++)
                {
                    if (col > 0) sb.Append(", ");
                    string cell = grid.collision[row][col] == "#" ? "#" : ".";
                    sb.Append("\"").Append(cell).Append("\"");
                }
                sb.Append("]");
                if (row < grid.height - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.Append(childIndent).AppendLine("],");
            sb.Append(childIndent).Append("\"grid_ref_size\": [")
                .Append(grid?.gridRefSize.x ?? 0).Append(", ")
                .Append(grid?.gridRefSize.y ?? 0).AppendLine("]");
            sb.Append(indent).Append("}");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private void RemapColliderInstanceStore(int oldId, int newId)
        {
            if (oldId == newId) return;

            if (_colliderInstanceStore.TryGetValue(oldId, out var current))
            {
                _colliderInstanceStore.Remove(oldId);
                _colliderInstanceStore[newId] = current;
            }

            if (_savedColliderInstanceStore.TryGetValue(oldId, out var saved))
            {
                _savedColliderInstanceStore.Remove(oldId);
                _savedColliderInstanceStore[newId] = saved;
            }

            if (_activeColliderSession != null && _activeColliderSession.InstanceId == oldId)
                _activeColliderSession.InstanceId = newId;
        }

        private void PruneColliderInstanceStore(IReadOnlyList<BuildingObject> buildings)
        {
            var validIds = new HashSet<int>(
                buildings
                    .Where(b => b != null && string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.InstanceId));

            foreach (int key in _colliderInstanceStore.Keys.ToList())
            {
                if (!validIds.Contains(key))
                    _colliderInstanceStore.Remove(key);
            }

            foreach (int key in _savedColliderInstanceStore.Keys.ToList())
            {
                if (!validIds.Contains(key))
                    _savedColliderInstanceStore.Remove(key);
            }
        }

        private void RefreshCollisionFor(BuildingObject building)
        {
            if (building == null) return;
            if (_activeBuilding == building)
                _activeColliderSession = null;

            ApplyCollisionStateForBuilding(building);
            Physics2D.SyncTransforms();

            if (_collidersVisible)
                RefreshCollidersOverlay();

            if (_activeBuilding == building)
                RefreshCollidersPanel();
        }

        private int ResolveCollisionLayer()
        {
            var collisionLoader = FindObjectOfType<BuildingCollisionLoader>();
            return collisionLoader != null ? collisionLoader.CollisionLayer : 11;
        }
    }
}
