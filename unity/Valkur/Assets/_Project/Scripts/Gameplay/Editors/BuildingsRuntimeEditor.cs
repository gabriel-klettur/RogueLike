using System.Collections.Generic;
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
            _removeMode = false;
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
            if (_idLabelRt != null) _idLabelRt.gameObject.SetActive(false);
            if (_handlesRoot != null) _handlesRoot.SetActive(false);
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
                onPaintSolid:      () => Toast("Paint solid: TODO Phase 2."),
                onPaintWalk:       () => Toast("Paint walkable: TODO Phase 2."),
                onSaveCU:          () => Toast("Save CU: TODO Phase 2."),
                onDeleteBuilding:  () => RequestDeleteActiveWithConfirm());

            // Wire panel close callbacks to keep dropdown state in sync
            if (_uiRefs.ModesPanelDrag     != null)
                _uiRefs.ModesPanelDrag.OnClose     = () => { _openDropdowns.Remove("modes");     RefreshMenuBtnHighlights(); };
            if (_uiRefs.BuildingsPanelDrag != null)
                _uiRefs.BuildingsPanelDrag.OnClose = () => { _openDropdowns.Remove("buildings"); RefreshMenuBtnHighlights(); };
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
            BuildTutorial();
            BuildConfirmModal();

            OpenAllPanels();
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
            foreach (var n in new[] { "modes", "buildings", "props" })
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

            // Resize drag
            if (_resizing && _activeBuilding != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var delta = (Vector2)(worldPos - _resizeStartMouse);
                    int newW = Mathf.Max(8, _resizeStartScale.x + Mathf.RoundToInt(delta.x * 32f));
                    int newH = Mathf.Max(8, _resizeStartScale.y + Mathf.RoundToInt(delta.y * 32f));
                    _activeBuilding.Apply(_activeBuilding.Template, new Vector2Int(newW, newH), _activeBuilding.SplitRatioOverride);
                    if (_statusTmp != null) _statusTmp.text = $"Resize → {newW}×{newH} px";
                }
                else if (mouse.rightButton.wasReleasedThisFrame)
                {
                    _resizing = false;
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
            _activeBuilding = b;
            RefreshInspector();
            if (_statusTmp != null && b != null) _statusTmp.text = $"Active: ID {b.InstanceId} ({b.Template?.name})";
        }

        private void RefreshInspector()
        {
            if (_propsTmp == null) return;
            if (_activeBuilding == null || _activeBuilding.Template == null)
            {
                _propsTmp.text = "Select a building to view properties.";
                if (_inspectorRoot != null) _inspectorRoot.SetActive(false);
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
        }

        private void OnSplitSliderChanged(float v)
        {
            if (_activeBuilding == null) return;
            float oldVal = _activeBuilding.SplitRatioOverride;
            _undo.Do($"Split {v:F2}",
                () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, v),
                () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, oldVal));
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
                () => { _activeBuilding.ColliderScopeOverride = next; RefreshInspector(); },
                () => { _activeBuilding.ColliderScopeOverride = oldOv; RefreshInspector(); });
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
                () => { b.Apply(b.Template, Vector2Int.zero, -1f); b.ZBottomOffset = 0; b.ZTopOffset = 0; b.ColliderScopeOverride = ""; RefreshInspector(); },
                () => { b.Apply(b.Template, oldScale, oldSplit); b.ZBottomOffset = oldZB; b.ZTopOffset = oldZT; b.ColliderScopeOverride = oldScope; RefreshInspector(); });
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
                    sb.Append($"\"zone\": \"{zone}\", ");
                    sb.Append($"\"rel_x\": {relX}, ");
                    sb.Append($"\"rel_y\": {relY}");

                    var sov = b.ScaleOverride;
                    bool hasOv = b.SplitRatioOverride >= 0f || sov.x > 0 || sov.y > 0;
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
                        }
                        sb.Append("}");
                    }
                    sb.Append("}");
                    if (i < all.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");

                File.WriteAllText(path, sb.ToString());
                if (_statusTmp != null) _statusTmp.text = $"Saved {all.Count} buildings → {INSTANCES_REL_PATH}";
                Debug.Log($"[BuildingsEditor] Saved {all.Count} buildings to {path}");
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
    }
}
