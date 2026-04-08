using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Runtime in-game Inventory Editor (F6).
    /// Inspect and modify player/monster/map inventories.
    /// Mirrors Python's inventory_editor (F6): category tabs (player/monsters/map),
    /// entity list, inventory grid, drag-drop, add/delete items.
    /// </summary>
    public class InventoryRuntimeEditor : SingletonMonoBehaviour<InventoryRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _active;
        private InputAction _toggleAction;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _detailsTmp;
        private RectTransform _entityListContent;
        private RectTransform _gridContent;
        private TextMeshProUGUI _categoryLabel;

        // State
        private enum Category { Player, Monsters, Map }
        private Category _category = Category.Player;
        private Inventory.Inventory _selectedInventory;

        // IGameEditor
        public string EditorName => "Inventory Editor";
        public bool IsActive => _active;

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleInventoryEditor", InputActionType.Button, "<Keyboard>/f6");
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
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _category = Category.Player;
            RefreshEntityList();
            _statusTmp.text = "Inventory Editor active. F6 to close.";
            Debug.Log("[InventoryEditor] Activated (F6)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedInventory = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[InventoryEditor] Deactivated (F6)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("InventoryEditorCanvas", 107);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Left sidebar — Entity list
            var left = EditorUIHelpers.MakeSidebar("EntityPanel", _root.transform, 280f);
            EditorUIHelpers.AddVLG(left, 8, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "INVENTORY EDITOR");

            // Category tabs
            var tabRow = EditorUIHelpers.CreateUI("TabRow", left.transform);
            tabRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hlg = tabRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeButton(tabRow.transform, "Player", () =>
            {
                _category = Category.Player; RefreshEntityList();
            }, 26f, 11f);
            EditorUIHelpers.MakeButton(tabRow.transform, "Monsters", () =>
            {
                _category = Category.Monsters; RefreshEntityList();
            }, 26f, 11f);
            EditorUIHelpers.MakeButton(tabRow.transform, "Map", () =>
            {
                _category = Category.Map; RefreshEntityList();
            }, 26f, 11f);

            _categoryLabel = EditorUIHelpers.AddLabel(left.transform, "Player", 12f, TextAlignmentOptions.Center);
            _categoryLabel.color = EditorUIHelpers.ACCENT;

            EditorUIHelpers.BuildSeparator(left.transform);

            var (entScroll, entContent) = EditorUIHelpers.MakeScrollView(left.transform, "EntityList");
            _entityListContent = entContent;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);

            // Right panel — Inventory grid + details
            var right = EditorUIHelpers.MakeRightPanel("GridPanel", _root.transform, 360f);
            EditorUIHelpers.AddVLG(right, 8, 4f);
            EditorUIHelpers.BuildSectionHeader(right.transform, "INVENTORY SLOTS");

            var (gridScroll, gridContent) = EditorUIHelpers.MakeGridPicker(
                right.transform, "InvGrid", 5, 56f, 4f);
            _gridContent = gridContent;

            EditorUIHelpers.BuildSeparator(right.transform);
            EditorUIHelpers.BuildSectionHeader(right.transform, "DETAILS", 12f);

            var (dScroll, dContent) = EditorUIHelpers.MakeScrollView(right.transform, "DetailsScroll");
            _detailsTmp = EditorUIHelpers.AddLabel(dContent, "Select an inventory to inspect.", 11f);
            _detailsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
        }

        // ── Entity List ──

        private void RefreshEntityList()
        {
            for (int i = _entityListContent.childCount - 1; i >= 0; i--)
                Destroy(_entityListContent.GetChild(i).gameObject);

            _categoryLabel.text = _category.ToString();

            if (_category == Category.Player)
            {
                var player = EntityRegistry.Player;
                if (player != null)
                {
                    var inv = player.GetComponent<Inventory.Inventory>();
                    if (inv != null)
                    {
                        EditorUIHelpers.MakeButton(_entityListContent, "Player", () => SelectInventory(inv), 28f, 11f);
                    }
                }
            }
            else if (_category == Category.Monsters)
            {
                foreach (var monster in EntityRegistry.Monsters)
                {
                    if (monster == null) continue;
                    var inv = monster.GetComponent<Inventory.Inventory>();
                    if (inv == null) continue;
                    var name = monster.name;
                    var capturedInv = inv;
                    EditorUIHelpers.MakeButton(_entityListContent, name, () => SelectInventory(capturedInv), 26f, 10f);
                }
            }

            ClearGrid();
        }

        private void SelectInventory(Inventory.Inventory inv)
        {
            _selectedInventory = inv;
            RefreshGrid();
            _statusTmp.text = $"Viewing: {inv.gameObject.name}";
        }

        // ── Grid ──

        private void RefreshGrid()
        {
            ClearGrid();
            if (_selectedInventory == null)
            {
                _detailsTmp.text = "No inventory selected.";
                return;
            }

            // Use reflection or public API to read slots
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Owner:</b> {_selectedInventory.gameObject.name}");
            sb.AppendLine($"<b>Full:</b> {_selectedInventory.IsFull}");
            _detailsTmp.text = sb.ToString();
            _detailsTmp.richText = true;
        }

        private void ClearGrid()
        {
            for (int i = _gridContent.childCount - 1; i >= 0; i--)
                Destroy(_gridContent.GetChild(i).gameObject);
        }
    }
}
