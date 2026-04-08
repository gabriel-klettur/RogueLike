using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Runtime in-game Items Editor (F7).
    /// Browse item catalog, view properties, spawn/delete items on map.
    /// Mirrors Python's items_editor (F7): picker grid, properties panel,
    /// spawn/delete modes, map drops list.
    /// </summary>
    public class ItemsRuntimeEditor : SingletonMonoBehaviour<ItemsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _active;
        private InputAction _toggleAction;

        private enum EditorMode { Select, Spawn, Delete }
        private EditorMode _mode = EditorMode.Select;
        private string _selectedItemId;
        private ItemDefinition _selectedDef;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _pickerContent;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _propsTmp;
        private Image _spawnBtnImg;
        private Image _deleteBtnImg;
        private Image _selectBtnImg;

        // Items loaded from Resources
        private ItemDefinition[] _allItems;

        // IGameEditor
        public string EditorName => "Items Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleItemsEditor", InputActionType.Button, "<Keyboard>/f7");
            _toggleAction.Enable();
        }

        private void Start()
        {
            _allItems = Resources.LoadAll<ItemDefinition>("Items");
            if (_allItems == null || _allItems.Length == 0)
                _allItems = Resources.LoadAll<ItemDefinition>("");
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
            _statusTmp.text = "Items Editor active. F7 to close.";
            Debug.Log("[ItemsEditor] Activated (F7)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedItemId = null;
            _selectedDef = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[ItemsEditor] Deactivated (F7)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ItemsEditorCanvas", 108);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Picker
            var left = EditorUIHelpers.MakeSidebar("PickerPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "ITEMS EDITOR");

            // Toolbar
            var toolbar = EditorUIHelpers.CreateUI("Toolbar", left.transform);
            toolbar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var selectBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Select", () => SetMode(EditorMode.Select), 28f, 11f);
            _selectBtnImg = selectBtn.GetComponent<Image>();
            var spawnBtn = EditorUIHelpers.MakeButton(toolbar.transform, "Spawn", () => SetMode(EditorMode.Spawn), 28f, 11f);
            _spawnBtnImg = spawnBtn.GetComponent<Image>();
            var deleteBtn = EditorUIHelpers.MakeDangerButton(toolbar.transform, "Delete", () => SetMode(EditorMode.Delete), 28f);
            _deleteBtnImg = deleteBtn.GetComponent<Image>();

            EditorUIHelpers.BuildSeparator(left.transform);

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(left.transform, "ItemGrid", 4, 64f, 4f);
            _pickerContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right sidebar — Properties
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 360f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "ITEM PROPERTIES");

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(pContent, "Select an item to view properties.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
        }

        // ── Mode ──

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select mode.",
                EditorMode.Spawn => _selectedDef != null ? $"Click map to spawn {_selectedDef.displayName}" : "Select item first.",
                EditorMode.Delete => "Click item drop to delete.",
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

            if (_allItems == null) return;

            foreach (var item in _allItems)
            {
                if (item == null) continue;
                var captured = item;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, item.displayName ?? item.itemId, 64f,
                    () => SelectItem(captured));

                if (item.icon != null)
                {
                    icon.sprite = item.icon;
                    icon.enabled = true;
                }
                label.text = TruncateName(item.displayName ?? item.itemId, 8);

                if (item.itemId == _selectedItemId)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
            }
        }

        private void SelectItem(ItemDefinition def)
        {
            _selectedDef = def;
            _selectedItemId = def.itemId;
            RefreshPicker();
            RefreshProperties();
        }

        private void RefreshProperties()
        {
            if (_selectedDef == null) { _propsTmp.text = "Select an item."; return; }

            var d = _selectedDef;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>ID:</b> {d.itemId}");
            sb.AppendLine($"<b>Name:</b> {d.displayName}");
            sb.AppendLine($"<b>Rarity:</b> {d.rarity}");
            sb.AppendLine($"<b>Stackable:</b> {d.stackable}  Max: {d.maxStack}");
            sb.AppendLine($"<b>Weight:</b> {d.weight}");
            sb.AppendLine();
            sb.AppendLine("<b>── Economy ──</b>");
            sb.AppendLine($"Value: {d.value}  Buy: {d.buyPrice}  Sell: {d.sellPrice}");
            sb.AppendLine($"Level Req: {d.levelRequirement}");
            sb.AppendLine();
            sb.AppendLine("<b>── Equipment ──</b>");
            sb.AppendLine($"Slot: {d.equipSlot}");
            sb.AppendLine($"Damage: {d.damage}  Atk Speed: {d.attackSpeed}");
            sb.AppendLine($"Range: {d.range}  Crit: {d.critChance}x{d.critMultiplier}");
            sb.AppendLine($"Durability: {d.durability}");
            sb.AppendLine();
            sb.AppendLine("<b>── Consumable ──</b>");
            sb.AppendLine($"Healing: {d.healing}  Mana: {d.mana}");
            sb.AppendLine($"Energy: {d.energy}  Hunger: {d.hunger}");
            sb.AppendLine($"Buff: {d.buffStat} +{d.buffValue} ({d.duration:F1}s)");

            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {d.displayName ?? d.itemId}";
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

            if (_mode == EditorMode.Spawn && _selectedDef != null)
            {
                _statusTmp.text = $"Spawned {_selectedDef.displayName} at ({worldPos.x:F1}, {worldPos.y:F1})";
                Debug.Log($"[ItemsEditor] Spawn {_selectedDef.itemId} at {worldPos}");
            }
            else if (_mode == EditorMode.Delete)
            {
                var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("Pickup"));
                if (hit != null)
                {
                    _statusTmp.text = $"Deleted: {hit.gameObject.name}";
                    Destroy(hit.gameObject);
                }
                else
                {
                    _statusTmp.text = "No item drop under cursor.";
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
