using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// State management + list/grid refresh logic for the Inventory Editor.
    /// Phase 1: scaffolding — entity list, slot grid and catalog grid are
    /// populated read-only. Mutations live in Phase 2.
    /// </summary>
    public partial class InventoryRuntimeEditor
    {
        // ── High-level refresh ──────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshTabHighlights();
            RefreshModeButtons();
            RefreshEntityList();
            RefreshSlotGrid();
            RefreshCatalog();
        }

        // ── Category / Side / Mode / Catalog tab toggles ────────────────────────

        private void SetCategory(EditorCategory cat)
        {
            if (_category == cat) return;
            _category = cat;
            _selectedInventory  = null;
            _selectedEntityName = null;
            RefreshTabHighlights();
            RefreshEntityList();
            RefreshSlotGrid();
            Toast($"Category: {cat}");
        }

        private void SetSide(EditorSide side)
        {
            if (_side == side) return;
            _side = side;
            RefreshTabHighlights();
            RefreshEntityList();
            RefreshSlotGrid();
            Toast(side == EditorSide.Default ? "Show Default" : "Show Active");
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            switch (mode)
            {
                case EditorMode.View:       Toast("Mode: View"); break;
                case EditorMode.AddItem:    Toast("Mode: Add Item \u2014 select an item from Catalog"); break;
                case EditorMode.DeleteItem: Toast("Mode: Delete \u2014 click a slot to remove qty"); break;
            }
        }

        private void SetCatalogTab(CatalogTab tab)
        {
            if (_catalog == tab) return;
            _catalog = tab;
            RefreshTabHighlights();
            RefreshCatalog();
        }

        private void AdjustQty(int delta)
        {
            int v = _spinnerQty + delta;
            if (v < 1) v = 1;
            if (v > 999) v = 999;
            _spinnerQty = v;
            if (_qtyInput != null) _qtyInput.text = v.ToString();
        }

        // ── Visual highlight helpers ────────────────────────────────────────────

        private void RefreshTabHighlights()
        {
            ApplyTabStyle(_playerTabImg,    _category == EditorCategory.Player);
            ApplyTabStyle(_monstersTabImg,  _category == EditorCategory.Monsters);
            ApplyTabStyle(_mapTabImg,       _category == EditorCategory.Map);

            ApplyTabStyle(_sideDefaultImg,  _side == EditorSide.Default);
            ApplyTabStyle(_sideActiveImg,   _side == EditorSide.Active);

            ApplyTabStyle(_catDefaultImg,   _catalog == CatalogTab.Default);
            ApplyTabStyle(_catGroundImg,    _catalog == CatalogTab.Ground);
        }

        private void RefreshModeButtons()
        {
            ApplyTabStyle(_viewBtnImg,        _mode == EditorMode.View);
            ApplyTabStyle(_addItemBtnImg,     _mode == EditorMode.AddItem);
            ApplyTabStyle(_deleteItemBtnImg,  _mode == EditorMode.DeleteItem);
        }

        private static void ApplyTabStyle(Image img, bool active)
        {
            if (img == null) return;
            img.color = active ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
        }

        // ── Entity list ─────────────────────────────────────────────────────────

        private void RefreshEntityList()
        {
            if (_entityListContent == null) return;

            for (int i = _entityListContent.childCount - 1; i >= 0; i--)
                Destroy(_entityListContent.GetChild(i).gameObject);

            string filter = (_entitySearch ?? "").Trim().ToLowerInvariant();

            switch (_category)
            {
                case EditorCategory.Player:
                    PopulatePlayerEntries(filter);
                    break;
                case EditorCategory.Monsters:
                    PopulateMonsterEntries(filter);
                    break;
                case EditorCategory.Map:
                    PopulateMapEntries(filter);
                    break;
            }

            if (_entityListContent.childCount == 0)
            {
                var hint = EditorUIHelpers.AddLabel(_entityListContent, "(no entries)", 11f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
                hint.alignment = TextAlignmentOptions.Center;
            }
        }

        private void PopulatePlayerEntries(string filter)
        {
            var player = EntityRegistry.Player;
            if (player == null) return;
            var inv = player.GetComponent<Inventory>();
            if (inv == null) return;

            string label = _side == EditorSide.Default
                ? "Player (Default)"
                : "Player (Active)";

            if (filter.Length > 0 && !label.ToLowerInvariant().Contains(filter)) return;

            var capturedInv = inv;
            var capturedName = label;
            EditorUIHelpers.MakeButton(_entityListContent, label,
                () => SelectInventory(capturedInv, capturedName), 28f, 11f);
        }

        private void PopulateMonsterEntries(string filter)
        {
            foreach (var monster in EntityRegistry.Monsters)
            {
                if (monster == null) continue;
                var inv = monster.GetComponent<Inventory>();
                if (inv == null) continue;
                var name = monster.name;
                if (filter.Length > 0 && !name.ToLowerInvariant().Contains(filter)) continue;

                var capturedInv  = inv;
                var capturedName = name;
                EditorUIHelpers.MakeButton(_entityListContent, name,
                    () => SelectInventory(capturedInv, capturedName), 26f, 10f);
            }
        }

        private void PopulateMapEntries(string filter)
        {
            // Phase 2: enumerate ground items / map drops via ItemPickup registry.
            var hint = EditorUIHelpers.AddLabel(_entityListContent,
                "Map drops listing \u2014 Phase 2", 10f);
            hint.color = EditorUIHelpers.TEXT_MUTED;
            hint.alignment = TextAlignmentOptions.Center;
        }

        private void SelectInventory(Inventory inv, string displayName)
        {
            _selectedInventory  = inv;
            _selectedEntityName = displayName;
            RefreshSlotGrid();
            Toast($"Viewing: {displayName}");
        }

        // ── Slot grid ───────────────────────────────────────────────────────────

        private void RefreshSlotGrid()
        {
            if (_slotGridContent == null) return;

            for (int i = _slotGridContent.childCount - 1; i >= 0; i--)
                Destroy(_slotGridContent.GetChild(i).gameObject);

            if (_ownerTmp != null)
            {
                _ownerTmp.text = _selectedInventory != null
                    ? $"<b>Owner:</b> {_selectedEntityName}   <b>Used:</b> {_selectedInventory.UsedSlots}/{_selectedInventory.Capacity}"
                    : "(no entity selected)";
            }

            if (_selectedInventory == null) return;

            int capacity = _selectedInventory.Capacity;
            for (int i = 0; i < capacity; i++)
            {
                var slot = i < _selectedInventory.Slots.Count
                    ? _selectedInventory.Slots[i]
                    : new InventorySlot(null, 0);
                BuildSlotCell(_slotGridContent, slot, i);
            }
        }

        private void BuildSlotCell(RectTransform parent, InventorySlot slot, int index)
        {
            string label = slot.IsEmpty ? "" : (slot.Quantity > 1 ? $"x{slot.Quantity}" : "");
            var (btn, icon, lblTmp) = EditorUIHelpers.MakeSlotButton(parent.transform, label, 56f,
                () => OnSlotClicked(index));

            if (!slot.IsEmpty && slot.Item != null && slot.Item.icon != null)
            {
                icon.sprite  = slot.Item.icon;
                icon.enabled = true;
            }

            if (slot.IsEmpty)
            {
                lblTmp.text = $"{index}";
                lblTmp.color = EditorUIHelpers.TEXT_MUTED;
            }
            else
            {
                lblTmp.color = EditorUIHelpers.ACCENT;
            }
        }

        private void OnSlotClicked(int index)
        {
            switch (_mode)
            {
                case EditorMode.DeleteItem:
                    Toast($"Delete slot {index} (qty={_spinnerQty}) \u2014 Phase 2");
                    break;
                default:
                    if (_selectedInventory != null && index < _selectedInventory.Slots.Count)
                    {
                        var slot = _selectedInventory.Slots[index];
                        if (!slot.IsEmpty)
                            Toast($"Slot {index}: {slot.Item.displayName} x{slot.Quantity}");
                        else
                            Toast($"Slot {index}: empty");
                    }
                    break;
            }
        }

        // ── Catalog grid ────────────────────────────────────────────────────────

        private void RefreshCatalog()
        {
            if (_catalogGridContent == null) return;

            for (int i = _catalogGridContent.childCount - 1; i >= 0; i--)
                Destroy(_catalogGridContent.GetChild(i).gameObject);

            switch (_catalog)
            {
                case CatalogTab.Default: PopulateDefaultCatalog(); break;
                case CatalogTab.Ground:  PopulateGroundCatalog();  break;
            }
        }

        private void PopulateDefaultCatalog()
        {
            string filter = (_catalogSearch ?? "").Trim().ToLowerInvariant();
            int shown = 0;
            if (_allItems != null)
            {
                for (int i = 0; i < _allItems.Length; i++)
                {
                    var item = _allItems[i];
                    if (item == null) continue;
                    var name = item.displayName ?? item.itemId ?? item.name;
                    if (filter.Length > 0 && !name.ToLowerInvariant().Contains(filter)) continue;

                    var captured = item;
                    var (btn, icon, lblTmp) = EditorUIHelpers.MakeSlotButton(
                        _catalogGridContent.transform, "", 56f,
                        () => OnCatalogPicked(captured));
                    if (item.icon != null)
                    {
                        icon.sprite = item.icon;
                        icon.enabled = true;
                    }
                    lblTmp.text = name;
                    lblTmp.color = EditorUIHelpers.TEXT_PRIMARY;
                    shown++;
                }
            }
            if (shown == 0)
            {
                var hint = EditorUIHelpers.AddLabel(_catalogGridContent,
                    "(no items match)", 10f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
                hint.alignment = TextAlignmentOptions.Center;
            }
        }

        private void PopulateGroundCatalog()
        {
            // Phase 2: enumerate items currently on the ground from ItemPickup spawns.
            var hint = EditorUIHelpers.AddLabel(_catalogGridContent,
                "Ground items listing \u2014 Phase 2", 10f);
            hint.color = EditorUIHelpers.TEXT_MUTED;
            hint.alignment = TextAlignmentOptions.Center;
        }

        private void OnCatalogPicked(ItemDefinition item)
        {
            if (item == null) return;
            string name = item.displayName ?? item.itemId ?? item.name;
            Toast($"Picked: {name} (qty={_spinnerQty}) \u2014 use 'Add to Inventory' to apply");
        }
    }
}
