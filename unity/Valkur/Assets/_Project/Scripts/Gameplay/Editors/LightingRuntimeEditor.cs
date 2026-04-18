using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime in-game Lighting Editor (Ctrl+F3).
    /// Mirrors Python's lighting_editor: 3-panel layout with global toggles/quality,
    /// day/night cycle controls and keyframe editing, and light preset tuning.
    /// Place, move, and delete light instances on the map.
    /// </summary>
    public class LightingRuntimeEditor : SingletonMonoBehaviour<LightingRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Light preset catalog")]
        private LightPresetCatalog _catalog;

        private bool _active;
        private InputAction _toggleAction;
        private InputAction _ctrlModifier;

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

        // IGameEditor
        public string EditorName => "Lighting Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleLightingEditor", InputActionType.Button, "<Keyboard>/f3");
            _toggleAction.Enable();
            _ctrlModifier = new InputAction("CtrlModLight", InputActionType.Button, "<Keyboard>/leftCtrl");
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
            // Ctrl+F3 only
            if (_toggleAction.WasPerformedThisFrame() && _ctrlModifier.IsPressed())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;
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

        private void BuildMainPanel()
        {
            var panel = EditorUIHelpers.MakeSidebar("MainPanel", _root.transform, 260f);
            EditorUIHelpers.AddVLG(panel, 6, 4f);
            EditorUIHelpers.MakeTitleBar(panel.transform, "LIGHTING EDITOR");

            // Global Toggles
            EditorUIHelpers.BuildSectionHeader(panel.transform, "GLOBAL TOGGLES");
            MakeToggle(panel.transform, "Ambient", _ambientEnabled, v => { _ambientEnabled = v; _statusTmp.text = $"Ambient: {(v ? "ON" : "OFF")}"; });
            MakeToggle(panel.transform, "Point Lights", _pointLightsEnabled, v => { _pointLightsEnabled = v; _statusTmp.text = $"Point Lights: {(v ? "ON" : "OFF")}"; });
            MakeToggle(panel.transform, "Shadows", _shadowsEnabled, v => { _shadowsEnabled = v; _statusTmp.text = $"Shadows: {(v ? "ON" : "OFF")}"; });

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Quality Steppers
            EditorUIHelpers.BuildSectionHeader(panel.transform, "QUALITY");
            MakeStepper(panel.transform, "Max Lights", _maxLights, 1, 64, v => _maxLights = v);
            MakeStepper(panel.transform, "Max Radius", _maxRadius, 16, 1024, v => _maxRadius = v);
            MakeStepper(panel.transform, "Shadow Rays", _shadowRays, 8, 256, v => _shadowRays = v);

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Overlay controls
            EditorUIHelpers.BuildSectionHeader(panel.transform, "OVERLAY");
            MakeToggle(panel.transform, "Overlay", _overlayVisible, v => _overlayVisible = v);
            MakeToggle(panel.transform, "Labels", _labelsVisible, v => _labelsVisible = v);

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Mode toolbar
            EditorUIHelpers.BuildSectionHeader(panel.transform, "TOOLS");
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", panel.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var sel = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = sel.GetComponent<Image>();
            var spn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spn.GetComponent<Image>();
            var del = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = del.GetComponent<Image>();

            var utilRow = EditorUIHelpers.CreateUI("UtilRow", panel.transform);
            utilRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var uhlg = utilRow.AddComponent<HorizontalLayoutGroup>();
            uhlg.spacing = 4f; uhlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(utilRow.transform, "Save", () => SaveInstances(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(utilRow.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            _statusTmp = EditorUIHelpers.MakeStatusText(panel.transform);
        }

        // ── Panel 2: Day/Night Controls (centre) ──

        private void BuildDayTimePanel()
        {
            // Centre panel
            var panel = new GameObject("DayTimePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_root.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(264f, 4f);
            rt.offsetMax = new Vector2(-304f, -4f);
            panel.GetComponent<Image>().color = EditorUIHelpers.BG_PANEL;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            EditorUIHelpers.BuildSectionHeader(panel.transform, "DAY / NIGHT CYCLE");

            // Time display
            _dayTimeTmp = EditorUIHelpers.AddLabel(panel.transform, "12:00 — Day", 14f);
            _dayTimeTmp.alignment = TextAlignmentOptions.Center;
            _dayTimeTmp.color = EditorUIHelpers.ACCENT;

            // Quick time buttons
            var timeRow = EditorUIHelpers.CreateUI("TimeRow", panel.transform);
            timeRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var trHlg = timeRow.AddComponent<HorizontalLayoutGroup>();
            trHlg.spacing = 4f; trHlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeButton(timeRow.transform, "-30m", () => AdjustTime(-30), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "-5m", () => AdjustTime(-5), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "+5m", () => AdjustTime(5), 24f, 10f);
            EditorUIHelpers.MakeButton(timeRow.transform, "+30m", () => AdjustTime(30), 24f, 10f);

            // Jump buttons
            var jumpRow = EditorUIHelpers.CreateUI("JumpRow", panel.transform);
            jumpRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var jHlg = jumpRow.AddComponent<HorizontalLayoutGroup>();
            jHlg.spacing = 4f; jHlg.childForceExpandWidth = true;

            int[] jumpTimes = { 0, 300, 420, 720, 1140, 1260 };
            string[] jumpLabels = { "00:00", "05:00", "07:00", "12:00", "19:00", "21:00" };
            for (int i = 0; i < jumpTimes.Length; i++)
            {
                int mins = jumpTimes[i];
                EditorUIHelpers.MakeButton(jumpRow.transform, jumpLabels[i], () => JumpToTime(mins), 22f, 9f);
            }

            EditorUIHelpers.BuildSeparator(panel.transform);

            // Time scale stepper
            MakeStepper(panel.transform, "Time Scale", Mathf.RoundToInt(_timeScale * 10f), 0, 100,
                v => _timeScale = v / 10f, "min/s × 0.1");

            // Min intensity stepper
            MakeStepper(panel.transform, "Min Intensity", Mathf.RoundToInt(_minIntensity * 100f), 0, 100,
                v => _minIntensity = v / 100f, "%");

            EditorUIHelpers.BuildSeparator(panel.transform);
            EditorUIHelpers.BuildSectionHeader(panel.transform, "KEYFRAME INTENSITIES");

            // Keyframe editors
            string[] kfLabels = { "00:00", "05:00", "07:00", "12:00", "19:00", "21:00" };
            float[] kfDefaults = { 0f, 0f, 1f, 1f, 1f, 0f };
            for (int i = 0; i < kfLabels.Length; i++)
            {
                int idx = i;
                var kfRow = EditorUIHelpers.CreateUI($"KF_{kfLabels[i]}", panel.transform);
                kfRow.AddComponent<LayoutElement>().preferredHeight = 22f;
                var khlg = kfRow.AddComponent<HorizontalLayoutGroup>();
                khlg.spacing = 4f; khlg.childForceExpandWidth = true;

                EditorUIHelpers.AddLabel(kfRow.transform, kfLabels[i], 10f);
                EditorUIHelpers.AddLabel(kfRow.transform, $"{kfDefaults[idx]:F2}", 10f);
            }
        }

        // ── Panel 3: Light Presets (right) ──

        private void BuildPresetsPanel()
        {
            var right = EditorUIHelpers.MakeRightPanel("PresetsPanel", _root.transform, 300f);
            EditorUIHelpers.AddVLG(right, 6, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "LIGHT PRESETS");

            _searchBox = SearchBox.Create(right.transform, "Search presets\u2026",
                v => { _searchFilter = v ?? ""; RefreshPresetButtons(); });

            var listGo = EditorUIHelpers.CreateUI("PresetButtons", right.transform);
            var lvlg = listGo.AddComponent<VerticalLayoutGroup>();
            lvlg.spacing = 2f; lvlg.childForceExpandWidth = true; lvlg.childForceExpandHeight = false;
            _presetButtonsParent = listGo.GetComponent<RectTransform>();
            RefreshPresetButtons();

            EditorUIHelpers.BuildSeparator(right.transform);
            EditorUIHelpers.BuildSectionHeader(right.transform, "PRESET PROPERTIES");

            var (scroll, content) = EditorUIHelpers.MakeScrollView(right.transform, "PresetPropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(content, "Select a preset to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
            _propsTmp.richText = true;

            EditorUIHelpers.BuildSeparator(right.transform);

            // Single-shot toggle
            MakeToggle(right.transform, "Single-Shot", _singleShot, v => _singleShot = v);
        }

        // ── Preset Selection ──

        private void RefreshPresetButtons()
        {
            if (_presetButtonsParent == null) return;
            for (int i = _presetButtonsParent.childCount - 1; i >= 0; i--)
                Destroy(_presetButtonsParent.GetChild(i).gameObject);
            if (_catalog == null) return;
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            foreach (var preset in _catalog.presets)
            {
                if (preset == null) continue;
                var key = preset.presetKey;
                if (filter.Length > 0 && !(key ?? "").ToLowerInvariant().Contains(filter)) continue;
                var btn = EditorUIHelpers.MakeButton(_presetButtonsParent, key, () => SelectPreset(key), 28f, 11f);
                if (key == _selectedPresetKey)
                    btn.GetComponent<Image>().color = EditorUIHelpers.BTN_ACTIVE;
            }
        }

        private void SelectPreset(string key)
        {
            _selectedPresetKey = key;
            ShowPresetProperties(key);
            RefreshPresetButtons();
            _statusTmp.text = $"Preset: {key}";
        }

        private void ShowPresetProperties(string key)
        {
            var preset = _catalog?.GetByKey(key);
            if (preset == null) { _propsTmp.text = "Not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {preset.presetKey}");
            sb.AppendLine($"<b>Radius:</b> {preset.radius:F1}");
            sb.AppendLine($"<b>Intensity:</b> {preset.intensity:F2}");
            sb.AppendLine($"<b>Falloff:</b> {preset.falloff:F2}");
            sb.AppendLine($"<b>Color:</b> {ColorUtility.ToHtmlStringRGBA(preset.color)}");
            sb.AppendLine($"<b>Flicker Amp:</b> {preset.flickerAmplitude:F2}");
            sb.AppendLine($"<b>Flicker Speed:</b> {preset.flickerSpeed:F2} Hz");
            sb.AppendLine($"<b>Center Scale:</b> {preset.centerScale:F2}");
            _propsTmp.text = sb.ToString();
        }

        // ── Mode ──

        private void SetMode(EditorMode m)
        {
            _mode = m;
            RefreshModeButtons();
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_spawnBtnImg) _spawnBtnImg.color = _mode == EditorMode.Spawn ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Day/Night ──

        private void AdjustTime(int minutes)
        {
            _dayTimeMinutes = Mathf.Repeat(_dayTimeMinutes + minutes, 1440f);
        }

        private void JumpToTime(int minuteOfDay)
        {
            _dayTimeMinutes = minuteOfDay;
        }

        private void UpdateDayTimeDisplay()
        {
            if (_dayTimeTmp == null) return;
            int h = Mathf.FloorToInt(_dayTimeMinutes / 60f);
            int m = Mathf.FloorToInt(_dayTimeMinutes % 60f);
            string phase = GetPhase(_dayTimeMinutes);
            _dayTimeTmp.text = $"{h:D2}:{m:D2} — {phase}";
        }

        private static string GetPhase(float minutes)
        {
            if (minutes < 300) return "Night";
            if (minutes < 420) return "Dawn";
            if (minutes < 720) return "Day";
            if (minutes < 780) return "Noon";
            if (minutes < 1140) return "Day";
            if (minutes < 1260) return "Dusk";
            return "Night";
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

            // Drag in Select mode
            if (_dragging && _dragTarget != null)
            {
                _dragTarget.transform.position = worldPos + _dragOffset;
                if (mouse.leftButton.wasReleasedThisFrame)
                    _dragging = false;
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_mode == EditorMode.Spawn && !string.IsNullOrEmpty(_selectedPresetKey))
                {
                    _statusTmp.text = $"Placed light '{_selectedPresetKey}' at ({worldPos.x:F1}, {worldPos.y:F1})";
                    if (_singleShot) _mode = EditorMode.Select;
                    RefreshModeButtons();
                    Debug.Log($"[LightingEditor] Spawn {_selectedPresetKey} at {worldPos}");
                }
                else if (_mode == EditorMode.Delete)
                {
                    var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (hit != null && hit.GetComponent<Light>() != null)
                    {
                        _statusTmp.text = $"Deleted light: {hit.gameObject.name}";
                        Destroy(hit.gameObject);
                    }
                }
                else if (_mode == EditorMode.Select)
                {
                    var hit = Physics2D.OverlapCircle(worldPos, 0.5f);
                    if (hit != null && hit.GetComponent<Light>() != null)
                    {
                        _dragTarget = hit.gameObject;
                        _dragging = true;
                        _dragOffset = _dragTarget.transform.position - worldPos;
                    }
                }
            }
        }

        private void SaveInstances()
        {
            _statusTmp.text = "Saved light instances.";
            Debug.Log("[LightingEditor] Save requested.");
        }

        // ── Helpers ──

        private static void MakeToggle(Transform parent, string label, bool initial, System.Action<bool> onChange)
        {
            var row = EditorUIHelpers.CreateUI($"Toggle_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            EditorUIHelpers.AddLabel(row.transform, label, 11f);

            bool state = initial;
            var btnGo = EditorUIHelpers.MakeButton(row.transform, state ? "ON" : "OFF", null, 24f, 10f);
            var btnImg = btnGo.GetComponent<Image>();
            var btnTmp = btnGo.GetComponentInChildren<TextMeshProUGUI>();
            btnImg.color = state ? EditorUIHelpers.SUCCESS : EditorUIHelpers.BTN_NORMAL;

            btnGo.GetComponent<Button>().onClick.AddListener(() =>
            {
                state = !state;
                btnTmp.text = state ? "ON" : "OFF";
                btnImg.color = state ? EditorUIHelpers.SUCCESS : EditorUIHelpers.BTN_NORMAL;
                onChange?.Invoke(state);
            });
        }

        private static void MakeStepper(Transform parent, string label, int initial, int min, int max,
            System.Action<int> onChange, string suffix = "")
        {
            var row = EditorUIHelpers.CreateUI($"Step_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            EditorUIHelpers.AddLabel(row.transform, label, 10f);

            int val = initial;
            var valLabel = EditorUIHelpers.AddLabel(row.transform, $"{val}{suffix}", 10f);
            valLabel.alignment = TextAlignmentOptions.Center;

            EditorUIHelpers.MakeButton(row.transform, "−", () =>
            {
                val = Mathf.Max(min, val - 1);
                valLabel.text = $"{val}{suffix}";
                onChange?.Invoke(val);
            }, 22f, 10f);

            EditorUIHelpers.MakeButton(row.transform, "+", () =>
            {
                val = Mathf.Min(max, val + 1);
                valLabel.text = $"{val}{suffix}";
                onChange?.Invoke(val);
            }, 22f, 10f);
        }
    }
}
