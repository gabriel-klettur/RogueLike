using UnityEngine;
using Valkur.Data;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Inventory
{
    public partial class InventoryUI
    {
        private static readonly string[] CURRENCY_ITEM_IDS =
            { "gold", "coins", "coin", "gold_coin" };

        private void RefreshAll()
        {
            UpdateHeaderInfo();
            UpdateEquipmentView();
            RefreshSlots();
            UpdateGold();
            UpdateSlotHighlights();
            UpdateTooltip();
        }

        // Reads directly from the player's equipment storage (no auto-mirror
        // from the bag). Empty cells keep their placeholder label visible so
        // the user can see what each slot is for.
        private void UpdateEquipmentView()
        {
            if (_equipIcons == null) return;

            var slots = _playerInventory != null ? _playerInventory.EquipmentSlots : null;
            for (int i = 0; i < EquipmentView.SLOT_COUNT && i < _equipIcons.Length; i++)
            {
                var slot = (slots != null && i < slots.Count) ? slots[i] : default;
                bool hasItem = !slot.IsEmpty;
                _equipIcons[i].enabled  = hasItem;
                _equipIcons[i].sprite   = hasItem ? (slot.Item.icon ?? slot.Item.iconSmall) : null;
                if (_equipQtyTexts[i] != null)
                    _equipQtyTexts[i].text = (hasItem && slot.Quantity > 1) ? slot.Quantity.ToString() : "";
                if (_equipLabels[i] != null)
                    _equipLabels[i].enabled = !hasItem;
            }
        }

        private void RefreshSlots()
        {
            if (_slotObjects == null) return;

            int slotCount = _slotObjects.Length;
            var slots = _playerInventory != null ? _playerInventory.Slots : null;
            int playerSlotCount = slots != null ? slots.Count : 0;

            for (int i = 0; i < slotCount; i++)
            {
                if (slots != null && i < playerSlotCount && !slots[i].IsEmpty)
                {
                    var slot = slots[i];
                    _slotIcons[i].enabled    = true;
                    _slotIcons[i].sprite     = slot.Item.icon ?? slot.Item.iconSmall;
                    _slotQuantities[i].text  = slot.Quantity > 1 ? slot.Quantity.ToString() : "";
                }
                else
                {
                    _slotIcons[i].enabled   = false;
                    _slotQuantities[i].text = "";
                }
            }
        }

        private void UpdateHeaderInfo()
        {
            // Name (class display name → fallback to playerKey or "Hero")
            if (_hdrNameText != null)
            {
                string name = _playerDef != null && !string.IsNullOrEmpty(_playerDef.displayName)
                    ? _playerDef.displayName
                    : (PlayerSelectionState.SelectedPlayerKey ?? "Hero");
                if (!string.IsNullOrEmpty(name))
                    name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                _hdrNameText.text = name;
            }

            // Level + xp%
            if (_hdrLevelText != null)
            {
                int lvl = _playerXp != null ? Mathf.Max(1, _playerXp.Level) : 1;
                int pct = _playerXp != null ? Mathf.RoundToInt(_playerXp.NormalizedProgress * 100f) : 0;
                _hdrLevelText.text = $"Lvl {lvl} ({pct}%)";
            }

            // Portrait + body avatar — mirror the player sprite (same as Python)
            Sprite sp = _playerSprite != null ? _playerSprite.sprite : null;
            if (_portraitImg != null)
            {
                _portraitImg.sprite  = sp;
                _portraitImg.enabled = sp != null;
            }
        }

        private void UpdateGold()
        {
            if (_goldText == null) return;

            int total = 0;
            if (_playerWallet != null) total += _playerWallet.Coins;

            // Also sum currency item-id stacks in the inventory (Python parity).
            if (_playerInventory != null)
            {
                var slots = _playerInventory.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].IsEmpty) continue;
                    string id = slots[i].Item.itemId;
                    for (int k = 0; k < CURRENCY_ITEM_IDS.Length; k++)
                    {
                        if (string.Equals(id, CURRENCY_ITEM_IDS[k], System.StringComparison.OrdinalIgnoreCase))
                        {
                            total += slots[i].Quantity;
                            break;
                        }
                    }
                }
            }
            _goldText.text = total.ToString();
        }

        private void UpdateSlotHighlights()
        {
            if (_slotBackgrounds == null) return;
            for (int i = 0; i < _slotBackgrounds.Length; i++)
                _slotBackgrounds[i].color = (i == _selectedSlot) ? SLOT_SELECTED : SLOT_BG;
        }

        private void UpdateTooltip()
        {
            if (_tooltipText == null) return;

            if (_playerInventory != null && _selectedSlot >= 0 &&
                _selectedSlot < _playerInventory.Slots.Count)
            {
                var slot = _playerInventory.Slots[_selectedSlot];
                if (!slot.IsEmpty)
                {
                    string desc = !string.IsNullOrEmpty(slot.Item.description)
                        ? slot.Item.description
                        : "Sin descripcion";
                    _tooltipText.text  = $"<b>{slot.Item.displayName}</b> x{slot.Quantity}\n{desc}";
                    _tooltipText.color = TEXT_PRIMARY;
                    return;
                }
            }

            _tooltipText.text  = "Tab/I close  |  Q drop  |  double-click use  |  drag to move";
            _tooltipText.color = TEXT_MUTED;
        }
    }
}
