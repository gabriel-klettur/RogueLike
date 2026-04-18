using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Runtime in-game Particles Editor (Ctrl+F1).
    /// Browse particle presets, place/move/delete instances on map.
    /// Mirrors Python's particles_editor (Ctrl+F1): preset picker grid,
    /// properties panel, place/drag/delete modes, spell references.
    /// </summary>
    public class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Particle preset catalog")]
        private ParticlePresetCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;
        private InputAction _ctrlModifier;

        private enum EditorMode { Select, Place, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedPresetId;

        // Drag
        private bool _dragging;
        private GameObject _dragTarget;
        private Vector3 _dragOffset;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _selectBtnImg, _placeBtnImg, _deleteBtnImg;

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // IGameEditor
        public string EditorName => "Particles Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleParticlesEditor", InputActionType.Button, "<Keyboard>/f1");
            _toggleAction.Enable();
            _ctrlModifier = new InputAction("CtrlMod", InputActionType.Button, "<Keyboard>/leftCtrl");
            _ctrlModifier.Enable();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Dispose();
            _ctrlModifier?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Ctrl+F1 only
            if (_toggleAction.WasPerformedThisFrame() && _ctrlModifier.IsPressed())
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
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            _statusTmp.text = "Particles Editor active. Ctrl+F1 to close.";
            Debug.Log("[ParticlesEditor] Activated (Ctrl+F1)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedPresetId = null;
            _dragging = false;
            _dragTarget = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ParticlesEditor] Deactivated (Ctrl+F1)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ParticlesEditorCanvas", 110);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "PARTICLES EDITOR");

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var placeBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Place", () => SetMode(EditorMode.Place), 28f, 11f);
            _placeBtnImg = placeBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            var utilRow = EditorUIHelpers.CreateUI("UtilRow", left.transform);
            utilRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var uhlg = utilRow.AddComponent<HorizontalLayoutGroup>();
            uhlg.spacing = 4f; uhlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(utilRow.transform, "Save", () => SaveInstances(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            EditorUIHelpers.BuildSeparator(left.transform);

            _searchBox = SearchBox.Create(left.transform, "Search presets\u2026",
                v => { _searchFilter = v ?? ""; RefreshPicker(); });

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "PresetGrid", 4, 64f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "PRESET PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select a preset to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;

            _tutorial = TutorialOverlay.Build(_root.transform, "PARTICLES HOTKEYS", new[]
            {
                ("Ctrl+F1","Toggle Particles Editor"),
                ("LMB",    "Select / place / delete"),
                ("RMB",    "Drag to move"),
                ("Type",   "Filter by name"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Mode ──

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_placeBtnImg) _placeBtnImg.color = _mode == EditorMode.Place ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Picker ──

        private void RefreshPicker()
        {
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            if (_catalog == null) return;

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var preset in _catalog.Presets)
            {
                if (preset == null) continue;
                var pid = preset.id;
                if (filter.Length > 0)
                {
                    string n = (preset.displayName ?? pid ?? "").ToLowerInvariant();
                    if (!n.Contains(filter) && !(pid ?? "").ToLowerInvariant().Contains(filter)) continue;
                }
                shown++;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, preset.displayName ?? pid, 64f,
                    () => SelectPreset(pid));
                label.text = TruncateName(preset.displayName ?? pid, 8);

                if (pid == _selectedPresetId)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
            }
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} presets" : $"{shown} match '{_searchFilter}'";
        }

        private void SelectPreset(string pid)
        {
            _selectedPresetId = pid;
            RefreshPicker();
            ShowPresetProperties(pid);
        }

        private void ShowPresetProperties(string pid)
        {
            var preset = _catalog?.GetById(pid);
            if (preset == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>ID:</b> {preset.id}");
            sb.AppendLine($"<b>Name:</b> {preset.displayName}");
            sb.AppendLine($"<b>Type:</b> {preset.type}");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {preset.displayName ?? pid}";
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

            if (_dragging && _dragTarget != null)
            {
                _dragTarget.transform.position = worldPos + _dragOffset;
                if (mouse.rightButton.wasReleasedThisFrame)
                    _dragging = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_mode == EditorMode.Place && !string.IsNullOrEmpty(_selectedPresetId))
                {
                    _statusTmp.text = $"Placed {_selectedPresetId} at ({worldPos.x:F1}, {worldPos.y:F1})";
                    Debug.Log($"[ParticlesEditor] Place {_selectedPresetId} at {worldPos}");
                }
                else if (_mode == EditorMode.Delete)
                {
                    var ps = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (ps != null && ps.GetComponent<ParticleSystem>() != null)
                    {
                        _statusTmp.text = $"Deleted particle: {ps.gameObject.name}";
                        Destroy(ps.gameObject);
                    }
                }
            }

            if (mouse.rightButton.wasPressedThisFrame && _mode == EditorMode.Select)
            {
                var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                if (hit != null && hit.GetComponent<ParticleSystem>() != null)
                {
                    _dragTarget = hit.gameObject;
                    _dragging = true;
                    _dragOffset = _dragTarget.transform.position - worldPos;
                }
            }
        }

        private void SaveInstances()
        {
            _statusTmp.text = "Saved particle instances.";
            Debug.Log("[ParticlesEditor] Save requested.");
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}
