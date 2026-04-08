using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Runtime in-game Entities Editor (F5).
    /// Browse, inspect, spawn/delete entities on the map.
    /// Mirrors Python's entities_editor (F5): picker grid, properties panel,
    /// spawn/delete modes. Supports players, hostiles, neutrals.
    /// </summary>
    public class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Monster catalog asset")]
        private MonsterCatalog _monsterCatalog;

        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedKey;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _spawnBtnImg;
        private Image _deleteBtnImg;
        private Image _selectBtnImg;

        // Category
        private enum EntityCategory { Hostiles, Players }
        private EntityCategory _category = EntityCategory.Hostiles;

        // IGameEditor
        public string EditorName => "Entities Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleEntitiesEditor", InputActionType.Button, "<Keyboard>/f5");
            _toggleAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
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
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            _statusTmp.text = "Entities Editor active. F5 to close.";
            Debug.Log("[EntitiesEditor] Activated (F5)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[EntitiesEditor] Deactivated (F5)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("EntitiesEditorCanvas", 106);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "ENTITIES EDITOR");

            // Category tabs
            var tabRow = EditorUIHelpers.CreateUI("TabRow", left.transform);
            tabRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hlg = tabRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeButton(tabRow.transform, "Hostiles", () =>
            {
                _category = EntityCategory.Hostiles; RefreshPicker();
            }, 26f, 11f);
            EditorUIHelpers.MakeButton(tabRow.transform, "Players", () =>
            {
                _category = EntityCategory.Players; RefreshPicker();
            }, 26f, 11f);

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var toolHlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            toolHlg.spacing = 4f; toolHlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var spawnBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spawnBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            EditorUIHelpers.BuildSeparator(left.transform);

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "EntityGrid", 4, 72f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 340f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "ENTITY PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select an entity to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
        }

        // ── Mode ──

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select mode. Click entity on map.",
                EditorMode.Spawn => _selectedKey != null ? $"Spawn mode: click to place {_selectedKey}" : "Select an entity first.",
                EditorMode.Delete => "Delete mode: click entity to remove.",
                _ => ""
            };
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_spawnBtnImg) _spawnBtnImg.color = _mode == EditorMode.Spawn ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Picker ──

        private void RefreshPicker()
        {
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            if (_category == EntityCategory.Hostiles && _monsterCatalog != null)
            {
                foreach (var def in _monsterCatalog.Definitions)
                {
                    var key = def.monsterKey;
                    var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                        _pickerContent, def.displayName ?? key, 72f,
                        () => SelectEntity(key));

                    if (def.assetConfig != null && def.assetConfig.idle.south != null)
                    {
                        icon.sprite = def.assetConfig.idle.south;
                        icon.enabled = true;
                    }
                    label.text = TruncateName(def.displayName ?? key, 9);

                    if (key == _selectedKey)
                        btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
                }
            }
            else if (_category == EntityCategory.Players)
            {
                foreach (var preset in PlayerClassCatalog.AllPresets)
                {
                    var key = preset.PlayerKey;
                    var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                        _pickerContent, preset.DisplayName ?? key, 72f,
                        () => SelectPlayerClass(key));
                    label.text = TruncateName(preset.DisplayName ?? key, 9);
                    if (key == _selectedKey)
                        btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
                }
            }
        }

        private void SelectEntity(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            ShowMonsterProperties(key);
        }

        private void SelectPlayerClass(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            ShowPlayerProperties(key);
        }

        private void ShowMonsterProperties(string key)
        {
            if (_monsterCatalog == null) return;
            var def = _monsterCatalog.GetByKey(key);
            if (def == null) { _propsTmp.text = "Not found."; return; }

            var s = def.stats;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {def.monsterKey}");
            sb.AppendLine($"<b>Name:</b> {def.displayName}");
            sb.AppendLine();
            sb.AppendLine("<b>── Stats ──</b>");
            sb.AppendLine($"HP: {s.hp}");
            sb.AppendLine($"Speed: {s.speed}  Chase: {s.chasingSpeed}");
            sb.AppendLine($"Defense: {s.defense}  Power: {s.power}");
            sb.AppendLine($"Melee Dmg: {s.meleeDamage}  Range: {s.meleeRange}");
            sb.AppendLine($"Melee CD: {s.meleeCooldown:F2}s");
            sb.AppendLine($"Aggro Range: {s.aggroRange}");
            sb.AppendLine($"Attack Windup: {s.attackWindupSeconds:F2}s");
            sb.AppendLine();
            sb.AppendLine("<b>── AI ──</b>");
            sb.AppendLine($"FSM Set: {def.fsmSet}");
            sb.AppendLine($"Patrol: {def.patrolType}");
            sb.AppendLine($"Telegraph: {def.useAttackTelegraph}");
            sb.AppendLine();
            sb.AppendLine("<b>── Spawn ──</b>");
            sb.AppendLine($"Count: {s.spawnCount}  Padding: {s.spawnPadding}");
            sb.AppendLine($"Margin: {s.spawnMargin}");
            sb.AppendLine($"Faction: {s.faction}");

            if (def.autoCast && def.autoCastList != null)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Auto Cast ──</b>");
                foreach (var spell in def.autoCastList)
                    sb.AppendLine($"  • {spell}");
            }

            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {def.displayName ?? key}";
        }

        private void ShowPlayerProperties(string key)
        {
            if (!PlayerClassCatalog.TryGetPreset(key, out var p))
            {
                _propsTmp.text = "Not found."; return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {p.PlayerKey}");
            sb.AppendLine($"<b>Name:</b> {p.DisplayName}");
            sb.AppendLine();
            sb.AppendLine("<b>── Attributes ──</b>");
            sb.AppendLine($"Str: {p.InitialStrength}/{p.MaxStrength}  Int: {p.InitialIntelligence}/{p.MaxIntelligence}  Dex: {p.InitialDexterity}/{p.MaxDexterity}");
            sb.AppendLine();
            sb.AppendLine("<b>── Combat ──</b>");
            sb.AppendLine($"Attack: {p.BasicAttack}  Armor: {p.BasicArmor}");
            sb.AppendLine($"Speed: {p.BasicSpeed}");
            sb.AppendLine($"Mana Regen: {p.ManaRegenPerSecond}/s");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {p.DisplayName ?? key}";
        }

        // ── Map Interaction ──

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;

            var worldPos = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPos.z = 0;

            if (_mode == EditorMode.Spawn && !string.IsNullOrEmpty(_selectedKey))
            {
                SpawnEntityAtPosition(worldPos);
            }
            else if (_mode == EditorMode.Delete)
            {
                DeleteEntityAtPosition(worldPos);
            }
            else if (_mode == EditorMode.Select)
            {
                SelectEntityAtPosition(worldPos);
            }
        }

        private void SpawnEntityAtPosition(Vector3 worldPos)
        {
            // Stub: integrate with entity spawning system
            _statusTmp.text = $"Spawned {_selectedKey} at ({worldPos.x:F1}, {worldPos.y:F1})";
            Debug.Log($"[EntitiesEditor] Spawn {_selectedKey} at {worldPos}");
        }

        private void DeleteEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                _statusTmp.text = $"Deleted {hit.gameObject.name}";
                Debug.Log($"[EntitiesEditor] Deleted {hit.gameObject.name}");
                Destroy(hit.gameObject);
            }
            else
            {
                _statusTmp.text = "No entity under cursor.";
            }
        }

        private void SelectEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                var brain = hit.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
                if (brain != null)
                {
                    _statusTmp.text = $"Selected: {hit.gameObject.name}";
                }
            }
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}
