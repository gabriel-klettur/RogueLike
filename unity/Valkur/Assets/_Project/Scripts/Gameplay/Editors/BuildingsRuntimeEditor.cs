using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
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
        private TMP_InputField _searchBox;
        private string _searchFilter = "";

        // Inspector controls (Properties panel) — built once, refreshed per active building
        private GameObject _inspectorRoot;
        private Slider _splitSlider;
        private TextMeshProUGUI _zBottomVal, _zTopVal;
        private TextMeshProUGUI _scopeBtnLabel;
        private Image _scopeBtnImg;

        // Floating world-space handles (E/D/R) — overlay positioned each frame
        private GameObject _handlesRoot;
        private Button _handleE, _handleD, _handleR;

        // Tutorial (10-step interactive)
        private GameObject _tutorialRoot;
        private TextMeshProUGUI _tutorialStepLabel, _tutorialBodyTmp;
        private int _tutorialStep;
        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1. Open editor",   "Press F10 anywhere in-game to toggle the Buildings Editor."),
            ("2. Pick template", "In the left picker, click a building thumbnail to select it. Use the search box to filter by ID or asset path."),
            ("3. Place a building", "Click the Add (+) button or press the Place toolbar button, then click on the map to drop the selected template."),
            ("4. Hover & select",  "Move the mouse over a building — it outlines in CYAN. Use the mouse wheel to cycle through stacked buildings. Click to select (outline turns YELLOW)."),
            ("5. Move & resize",   "RMB-drag the active building to move it. Click the R handle (top-right of the building) to enter resize mode, then RMB-drag."),
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
            UpdateOutlineState();
            UpdateFloatingHandles();
            UpdateIdLabel();
            UpdateSplitLine();
            // Re-push the authoring cells every frame the colliders panel is
            // open so the overlay always tracks the active building's current
            // world rect (move, resize, split-ratio change, etc.). Cheap: a few
            // hundred rect copies at most.
            if (_collidersVisible && _openDropdowns.Contains("colliders"))
                RefreshCollidersOverlay();
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
            _handlesRoot = null; _handleE = _handleD = _handleR = null;
            _tutorialRoot = null; _tutorialStepLabel = _tutorialBodyTmp = null;
            _confirmModal = null; _confirmText = null;
            _idLabelTmp = null; _idLabelRt = null;
            _splitLineRt = null; _splitLineImg = null;
            _splitHandleRt = null; _splitHandleImg = null;
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
            if (_splitLineRt   != null) _splitLineRt.gameObject.SetActive(false);
            if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
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
                // Colliders panel callbacks (redesigned: ON/OFF + #/. action + scope)
                onToggleCollidersVisible: () => ToggleCollidersVisible(),
                onCollScopeToggle:        () => ToggleColliderScope(),
                onBrushToggle:            () => SetBrushOn(!BrushOn),
                onBrushPaint:             () => SetBrushAction(CollBrushMode.Solid),
                onBrushErase:             () => SetBrushAction(CollBrushMode.Walk),
                onCollBrushSizeChanged:   v  => OnCollBrushSizeChanged(v),
                onCollSave:               () => SaveColliderAuthoring());

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
            BuildSplitLine();
            BuildTutorial();
            BuildConfirmModal();

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
        /// Floating overlay handles E (delete, red), D (reset, white), R (resize, blue)
        /// drawn at the top-right of the active building (mirrors Python default_tool_view).
        /// </summary>
        private void BuildFloatingHandles()
        {
            _handlesRoot = EditorUIHelpers.CreateUI("FloatingHandles", _root.transform);
            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1, 1);                  // top-right anchored to building corner
            rt.sizeDelta = new Vector2(150f, 50f);
            var hlg = _handlesRoot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            _handleE = EditorUIHelpers.MakeDangerButton(_handlesRoot.transform, "E", () => RequestDeleteActiveWithConfirm(), 48f);
            _handleE.GetComponent<LayoutElement>().preferredWidth = 48f;
            _handleD = EditorUIHelpers.MakeButton(_handlesRoot.transform, "D", () => ResetActiveBuilding(), 48f, 18f);
            _handleD.GetComponent<LayoutElement>().preferredWidth = 48f;
            _handleR = EditorUIHelpers.MakeButton(_handlesRoot.transform, "R", () => SetMode(EditorMode.Resize), 48f, 18f);
            _handleR.GetComponent<LayoutElement>().preferredWidth = 48f;

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
            _idLabelRt.pivot = new Vector2(0, 0);
            _idLabelRt.sizeDelta = new Vector2(120f, 22f);
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
            }
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} templates" : $"{shown} match '{_searchFilter}'";
        }

        private void SelectTemplate(int id)
        {
            _selectedTemplateId = id;
            RefreshPicker();
            // If user picked a template while not in Place mode, auto-switch
            if (_mode != EditorMode.Place) SetMode(EditorMode.Place);
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            if (_mode != EditorMode.Resize) _resizing = false;
            RefreshModeButtons();
            if (_statusTmp == null) return;
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select: click building on map. Wheel to cycle stack.",
                EditorMode.Place  => _selectedTemplateId >= 0 ? "Click map to place selected template." : "Pick a template first.",
                EditorMode.Delete => "Click building to delete (with confirm).",
                EditorMode.Resize => "RMB-drag the active building to resize.",
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
            if (_selectedTemplateId < 0)
            {
                Toast("Pick a template from the grid first.");
                return;
            }
            SetMode(EditorMode.Place);
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

            // Resize drag
            if (_resizing && _activeBuilding != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var delta = (Vector2)(worldPos - _resizeStartMouse);
                    // Preserve aspect ratio (mirrors Python resize_tool.py):
                    //   delta = max(dx, dy) — largest axis wins
                    //   new_height = new_width / aspect_ratio
                    float aspect = (float)_resizeStartScale.x / Mathf.Max(1, _resizeStartScale.y);
                    float pixDelta = Mathf.Max(delta.x, delta.y) * 32f;
                    int newW = Mathf.Max(8, _resizeStartScale.x + Mathf.RoundToInt(pixDelta));
                    int newH = Mathf.Max(8, Mathf.RoundToInt(newW / aspect));
                    _activeBuilding.Apply(_activeBuilding.Template, new Vector2Int(newW, newH), _activeBuilding.SplitRatioOverride);
                    if (_statusTmp != null) _statusTmp.text = $"Resize → {newW}×{newH} px (ratio {aspect:F2})";
                    RefreshInspector();
                }
                else if (mouse.rightButton.wasReleasedThisFrame)
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
                if (_mode == EditorMode.Place && _selectedTemplateId >= 0)
                {
                    PlaceBuilding(worldPos);
                    return;
                }
                // Default: select hovered
                if (_hoveredBuilding != null) SetActiveBuilding(_hoveredBuilding);
            }

            // RMB on active building → start move; on hovered → switch active + drag
            if (mouse.rightButton.wasPressedThisFrame && _hoveredBuilding != null)
            {
                if (_mode == EditorMode.Resize)
                {
                    SetActiveBuilding(_hoveredBuilding);
                    _resizing = true;
                    _resizeStartMouse = worldPos;
                    _resizeStartScale = (_activeBuilding.ScaleOverride.x > 0)
                        ? _activeBuilding.ScaleOverride
                        : _activeBuilding.Template.originalScale;
                }
                else
                {
                    SetActiveBuilding(_hoveredBuilding);
                    _dragging = true;
                    _dragOffset = _activeBuilding.transform.position - worldPos;
                }
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
                    SetActiveBuilding(bObj);
                    if (_statusTmp != null) _statusTmp.text = $"Placed #{template.templateId} at ({worldPos.x:F1}, {worldPos.y:F1}) → ID {newId}";
                },
                () =>
                {
                    if (created != null) { Destroy(created.gameObject); created = null; }
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
                () => { if (go) go.SetActive(false); if (_activeBuilding == b) { _activeBuilding = null; RefreshInspector(); } },
                () => { if (go) { go.transform.position = savedPos; go.name = savedName; go.SetActive(true); } });
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

            // Project building top-right corner to screen → set handles' top-right pivot there
            Vector3 worldTopRight = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 screen = cam.WorldToScreenPoint(worldTopRight);
            Vector2 canvasPos = ScreenToCanvasPos(screen);

            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.anchoredPosition = canvasPos;
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
            // Place above the top-left corner of the building
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screen = cam.WorldToScreenPoint(worldTopLeft);
            _idLabelRt.anchoredPosition = ScreenToCanvasPos(screen) + new Vector2(0f, 26f);
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
            // Only Paint (Solid → "#") and Erase (Walk → ".") are valid actions in the new UX.
            if (action != CollBrushMode.Solid && action != CollBrushMode.Walk) return;
            _lastBrushAction = action;
            if (BrushOn)
            {
                SetCollBrushMode(action);
            }
            else
            {
                // Brush is OFF — just remember the choice and refresh the panel so the
                // user can see which action will be applied when they toggle back ON.
                RefreshBrushButtonHighlights();
                RefreshCollidersPanel();
                Toast($"Brush action set to {ActionLabel(action)} (brush is OFF — press B to enable).");
            }
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
                RefreshCollidersOverlay();
            }
            if (_uiRefs.CollBrushToggleLabel != null)
                _uiRefs.CollBrushToggleLabel.text = BrushOn
                    ? $"Brush: ON ({ActionLabel(_lastBrushAction)})"
                    : "Brush: OFF";
            RefreshCollidersPanel();
            Toast(BrushOn ? $"Brush ON ({ActionLabel(_collBrushMode)})." : "Brush OFF.");
        }

        private void OnCollBrushSizeChanged(float v)
        {
            _collBrushSize = Mathf.Clamp(Mathf.RoundToInt(v), 1, 8);
            if (_uiRefs.CollBrushSizeVal != null)
                _uiRefs.CollBrushSizeVal.text = _collBrushSize.ToString();
            if (_uiRefs.CollBrushSizeSlider != null
                && !Mathf.Approximately(_uiRefs.CollBrushSizeSlider.value, _collBrushSize))
            {
                _uiRefs.CollBrushSizeSlider.SetValueWithoutNotify(_collBrushSize);
            }
            RefreshCollidersPanel();
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

        private int RefreshCollidersOverlay()
        {
            if (_collidersVisible)
                Physics2D.SyncTransforms();

            // Compute authoring cells (the editor's working grid in world space)
            // for EVERY building in the scene — not only the currently active
            // one. This guarantees that when the user toggles "Show Colliders"
            // ON, every building's authored collision rectangles light up at
            // exactly the position where the BoxCollider2D children sit. For
            // buildings with no authored data (no editor-stored grid AND no
            // JSON grid) the overlay falls back to enumerating its own
            // BoxCollider2D children (root footprint, etc.) so the user always
            // sees SOMETHING when a building has any physical collider at all.
            int total = 0;
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                var overlay = b.GetComponent<BuildingColliderDebugOverlay>();
                if (overlay == null)
                    overlay = b.gameObject.AddComponent<BuildingColliderDebugOverlay>();

                if (_collidersVisible)
                {
                    var cells = TryComputeAuthoringCellsFor(b);
                    if (cells != null && cells.Count > 0)
                        overlay.SetAuthoringCells(cells);
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
            if (building == null || building.Template == null) return null;
            if (!building.TryGetWorldRect(out var rect) || rect.width <= 0f || rect.height <= 0f) return null;

            ColliderGridData grid = ResolveStoredGridForOverlay(building);
            if (grid == null || grid.collision == null || grid.height <= 0 || grid.width <= 0) return null;

            int rows = grid.height;
            int cols = grid.width;
            var cells = new List<Rect>(Mathf.Min(rows * cols, 256));
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
            return cells;
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
            ColliderGridData fallback = _collBrushMode == CollBrushMode.Erase
                ? CreateFallbackGridFor(_activeBuilding, session)
                : null;

            bool changed = false;
            for (int dr = -half; dr <= extra; dr++)
            {
                for (int dc = -half; dc <= extra; dc++)
                {
                    int r = row + dr;
                    int c = col + dc;
                    if (r < 0 || r >= session.WorkingGrid.height || c < 0 || c >= session.WorkingGrid.width)
                        continue;

                    string next = ".";
                    if (_collBrushMode == CollBrushMode.Solid) next = "#";
                    else if (_collBrushMode == CollBrushMode.Erase && fallback != null)
                        next = fallback.collision[r][c];

                    if (session.WorkingGrid.collision[r][c] == next) continue;
                    session.WorkingGrid.collision[r][c] = next;
                    changed = true;
                }
            }

            if (!changed) return;

            PersistSessionToStore(session);
            _colliderStroke.Changed = true;
            ApplyCollisionTargetsFor(session.Scope, session.ImageKey, session.InstanceId);
            RefreshCollidersPanel();
        }

        // NOTE: Quick Actions (Fill / Clear / Revert) were removed by user request
        // to keep the colliders authoring UX strictly brush-driven (paint vs. erase).
        // Bulk operations are now achieved with a large brush size on top of LMB-drag.

        private void SaveColliderAuthoring()
        {
            SaveInstancesToJson();
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

            float split = building.SplitRatioOverride >= 0f
                ? building.SplitRatioOverride
                : building.Template.splitRatio;
            float footprintTopNorm = Mathf.Clamp01(1f - split);

            for (int row = 0; row < rows; row++)
            {
                float cellBottomNorm = (float)(rows - 1 - row) / rows;
                if (cellBottomNorm >= footprintTopNorm) continue;
                for (int col = 0; col < cols; col++)
                    grid.collision[row][col] = "#";
            }

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
            var all = FindObjectsOfType<BuildingObject>();
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

            if (!GridHasSolidCells(effectiveGrid))
            {
                var mainCollider = building.GetComponent<BoxCollider2D>();
                if (mainCollider != null)
                    mainCollider.enabled = false;
                return;
            }

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
                if (grid != null)
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
