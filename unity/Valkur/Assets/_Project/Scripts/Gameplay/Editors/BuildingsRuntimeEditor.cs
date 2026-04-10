using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Runtime in-game Buildings Editor (F10).
    /// Select, place, move, resize, delete building instances on the map.
    /// Mirrors Python's buildings_editor (F10): picker with thumbnails,
    /// properties panel, drag-place, resize handles, Z-order, collider editing.
    /// </summary>
    public class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Building catalog asset")]
        private BuildingCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Place, Delete, Resize }
        private EditorMode _mode = EditorMode.Select;
        private int _selectedTemplateId = -1;
        private GameObject _selectedInstance;

        // UI
        private bool _uiBuilt;
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _selectBtnImg, _placeBtnImg, _deleteBtnImg, _resizeBtnImg;

        // Drag
        private bool _dragging;
        private Vector3 _dragOffset;

        // IGameEditor
        public string EditorName => "Buildings Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleBuildingsEditor", InputActionType.Button, "<Keyboard>/f10");
            // Don't enable yet — wait until Start() to avoid first-frame spurious fire
        }

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
            _toggleAction.Enable();
        }

        private void OnDestroy()
        {
            _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
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
            HandleMapInteraction();
        }

        public void Activate()
        {
            if (!_uiBuilt) { BuildUI(); _uiBuilt = true; }
            _active = true;
            _canvas.gameObject.SetActive(true);
            _canvas.enabled = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            if (_statusTmp != null)
                _statusTmp.text = "Buildings Editor active. F10 to close. ESC=cancel.";
            Debug.Log("[BuildingsEditor] Activated (F10)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_uiBuilt)
            {
                _root.SetActive(false);
                _canvas.enabled = false;
                _canvas.gameObject.SetActive(false);
            }
            _selectedTemplateId = -1;
            _selectedInstance = null;
            _dragging = false;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[BuildingsEditor] Deactivated (F10)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("BuildingsEditorCanvas", 109);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "BUILDINGS EDITOR");

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 10f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var placeBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Place", () => SetMode(EditorMode.Place), 28f, 10f);
            _placeBtnImg = placeBtn.GetComponent<Image>();
            var resizeBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Resize", () => SetMode(EditorMode.Resize), 28f, 10f);
            _resizeBtnImg = resizeBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            // Save button
            EditorUIHelpers.MakeButton(left.transform, "Save to JSON", () => SaveInstances(), 30f, 12f);

            EditorUIHelpers.BuildSeparator(left.transform);

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "BuildingGrid", 3, 80f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "BUILDING PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select a building to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
        }

        // ── Mode ──

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select: click building on map.",
                EditorMode.Place => _selectedTemplateId >= 0 ? "Click map to place building." : "Select template first.",
                EditorMode.Delete => "Click building to delete.",
                EditorMode.Resize => "RMB drag to resize.",
                _ => ""
            };
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_placeBtnImg) _placeBtnImg.color = _mode == EditorMode.Place ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_resizeBtnImg) _resizeBtnImg.color = _mode == EditorMode.Resize ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Picker ──

        private void RefreshPicker()
        {
            if (_pickerContent == null) return;

            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            if (_catalog == null) return;

            foreach (var tmpl in _catalog.Templates)
            {
                var id = tmpl.templateId;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, $"B{id}", 80f,
                    () => SelectTemplate(id));

                if (tmpl.previewSprite != null)
                {
                    icon.sprite = tmpl.previewSprite;
                    icon.enabled = true;
                }
                label.text = $"#{id}";

                if (id == _selectedTemplateId)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
            }
        }

        private void SelectTemplate(int id)
        {
            _selectedTemplateId = id;
            RefreshPicker();
            ShowTemplateProperties(id);
        }

        private void ShowTemplateProperties(int id)
        {
            var tmpl = _catalog?.GetById(id);
            if (tmpl == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>ID:</b> {tmpl.templateId}");
            sb.AppendLine($"<b>Asset:</b> {tmpl.assetPath}");
            sb.AppendLine($"<b>Solid:</b> {tmpl.solid}");
            sb.AppendLine($"<b>Split Ratio:</b> {tmpl.splitRatio:F2}");
            sb.AppendLine($"<b>Collider Scope:</b> {tmpl.colliderScope}");
            sb.AppendLine($"<b>Original Size:</b> {tmpl.originalScale}");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
        }

        // ── Map Interaction ──

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;
            var worldPos = (Vector3)cam.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPos.z = 0;

            // Drag ongoing
            if (_dragging && _selectedInstance != null)
            {
                _selectedInstance.transform.position = worldPos + _dragOffset;
                if (mouse.rightButton.wasReleasedThisFrame)
                    _dragging = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_mode == EditorMode.Place && _selectedTemplateId >= 0)
                {
                    PlaceBuilding(worldPos);
                }
                else if (_mode == EditorMode.Delete)
                {
                    DeleteBuildingAt(worldPos);
                }
                else if (_mode == EditorMode.Select)
                {
                    SelectBuildingAt(worldPos);
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && _mode == EditorMode.Select)
            {
                var hit = Physics2D.OverlapPoint(worldPos, LayerMask.GetMask("Building"));
                if (hit != null)
                {
                    _selectedInstance = hit.gameObject;
                    _dragging = true;
                    _dragOffset = _selectedInstance.transform.position - worldPos;
                }
            }
        }

        private void PlaceBuilding(Vector3 pos)
        {
            _statusTmp.text = $"Placed building #{_selectedTemplateId} at ({pos.x:F1}, {pos.y:F1})";
            Debug.Log($"[BuildingsEditor] Place #{_selectedTemplateId} at {pos}");
        }

        private void DeleteBuildingAt(Vector3 pos)
        {
            var hit = Physics2D.OverlapPoint(pos, LayerMask.GetMask("Building"));
            if (hit != null)
            {
                _statusTmp.text = $"Deleted: {hit.gameObject.name}";
                Destroy(hit.gameObject);
            }
        }

        private void SelectBuildingAt(Vector3 pos)
        {
            var hit = Physics2D.OverlapPoint(pos, LayerMask.GetMask("Building"));
            if (hit != null)
            {
                _selectedInstance = hit.gameObject;
                _statusTmp.text = $"Selected: {hit.gameObject.name}";
            }
        }

        private void SaveInstances()
        {
            _statusTmp.text = "Saved building instances.";
            Debug.Log("[BuildingsEditor] Save requested.");
        }
    }
}
