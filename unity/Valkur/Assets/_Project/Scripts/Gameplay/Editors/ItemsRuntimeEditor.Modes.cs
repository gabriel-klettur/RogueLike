using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Items
{
    public partial class ItemsRuntimeEditor : SingletonMonoBehaviour<ItemsRuntimeEditor>, GameEditorManager.IGameEditor
    {


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

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var item in _allItems)
            {
                if (item == null) continue;
                if (filter.Length > 0)
                {
                    string name = (item.displayName ?? item.itemId ?? "").ToLowerInvariant();
                    string id = (item.itemId ?? "").ToLowerInvariant();
                    if (!name.Contains(filter) && !id.Contains(filter)) continue;
                }
                shown++;
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
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} items" : $"{shown} match '{_searchFilter}'";
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