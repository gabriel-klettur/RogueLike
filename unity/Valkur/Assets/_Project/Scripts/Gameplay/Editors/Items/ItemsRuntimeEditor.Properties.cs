using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — Properties panel (right side).
    /// Mirrors Python <c>roguelike_editors/items/ui/properties_view.py</c> read-only inspector.
    /// Phase 2: read-only summary of every meaningful field, grouped by section.
    /// Phase 3: when a world instance is selected (via map click or instances row), an
    /// "Instance Actions" sub-panel is appended with editable Quantity (+/-) and a
    /// Delete button — all wired through the editor's UndoStack.
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        // Re-built each RefreshProperties() call so it reflects the live selection.
        private GameObject _instanceActionsGo;

        private void SetPropsTitle(string text)
        {
            if (_uiRefs.PropsTitle != null) _uiRefs.PropsTitle.text = text ?? "";
        }
        /// <summary>Refresh the Properties panel based on the currently selected item.</summary>
        private void RefreshProperties()
        {
            if (_uiRefs.PropsText == null) return;

            if (string.IsNullOrEmpty(_selectedItemId))
            {
                SetPropsTitle("(no item selected)");
                _uiRefs.PropsText.text = "Select an item from the grid to view its properties.";
                _uiRefs.PropsText.richText = true;
                RebuildInstanceActions();
                return;
            }

            var def = FindItemById(_selectedItemId);
            if (def == null)
            {
                SetPropsTitle(_selectedItemId);
                _uiRefs.PropsText.text = $"Item '{_selectedItemId}' not found in catalog.";
                _uiRefs.PropsText.richText = true;
                RebuildInstanceActions();
                return;
            }

            // The title strip shows the readable name; identity goes in the body
            // alongside the inspector table so users can copy-paste it.
            SetPropsTitle(string.IsNullOrEmpty(def.displayName) ? def.itemId : def.displayName);

            var sb = new StringBuilder(1024);
            sb.AppendLine($"<b>Id:</b> {def.itemId}");
            if (!string.IsNullOrEmpty(def.itemType))
                sb.AppendLine($"<b>Type:</b> {def.itemType}");
            if (!string.IsNullOrEmpty(def.description))
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Description ──</b>");
                sb.AppendLine(def.description);
            }

            sb.AppendLine();
            sb.AppendLine("<b>── Stacking ──</b>");
            sb.AppendLine($"Stackable: {def.stackable}");
            sb.AppendLine($"Max stack: {def.maxStack}");

            if (def.equipSlot != EquipSlot.None || def.damage != 0 || def.attackSpeed != 0 || def.range != 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Equipment ──</b>");
                sb.AppendLine($"Slot: {def.equipSlot}");
                sb.AppendLine($"Damage: {def.damage}");
                sb.AppendLine($"Attack speed: {def.attackSpeed}");
                sb.AppendLine($"Range: {def.range}");
                sb.AppendLine($"Crit chance: {def.critChance}");
                sb.AppendLine($"Crit multiplier: {def.critMultiplier}");
                sb.AppendLine($"Durability: {def.durability}");
            }

            sb.AppendLine();
            sb.AppendLine("<b>── Economy ──</b>");
            sb.AppendLine($"Value: {def.value}");
            sb.AppendLine($"Buy: {def.buyPrice}    Sell: {def.sellPrice}");
            sb.AppendLine($"Rarity: {def.rarity}");
            sb.AppendLine($"Required level: {def.levelRequirement}");
            sb.AppendLine($"Weight: {def.weight}");

            if (def.threshold != 0 || def.experience != 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Experience ──</b>");
                sb.AppendLine($"Threshold: {def.threshold}");
                sb.AppendLine($"XP: {def.experience}");
            }

            if (def.healing != 0 || def.mana != 0 || def.energy != 0 || def.hunger != 0 ||
                !string.IsNullOrEmpty(def.buffStat) || def.duration != 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Effect ──</b>");
                if (!string.IsNullOrEmpty(def.effect)) sb.AppendLine($"Effect id: {def.effect}");
                if (def.healing != 0) sb.AppendLine($"Healing: {def.healing}");
                if (def.mana    != 0) sb.AppendLine($"Mana: {def.mana}");
                if (def.energy  != 0) sb.AppendLine($"Energy: {def.energy}");
                if (def.hunger  != 0) sb.AppendLine($"Hunger: {def.hunger}");
                if (!string.IsNullOrEmpty(def.buffStat))
                    sb.AppendLine($"Buff: {def.buffStat} +{def.buffValue} for {def.duration:F1}s");
                else if (def.duration != 0)
                    sb.AppendLine($"Duration: {def.duration:F1}s");
            }

            sb.AppendLine();
            sb.AppendLine("<b>── Visual ──</b>");
            sb.AppendLine($"Scale (editor / map / inv): {def.scaleEditor:F2} / {def.scaleMap:F2} / {def.scaleInventory:F2}");
            sb.AppendLine($"Z-layer: {def.zLayer}");
            if (def.despawnTime > 0) sb.AppendLine($"Despawn after: {def.despawnTime:F1}s");

            _uiRefs.PropsText.text = sb.ToString();
            _uiRefs.PropsText.richText = true;

            RebuildInstanceActions();
        }

        // ── Instance Actions (Phase 3) ─────────────────────────────────────────
        // When a WorldPickup is selected, append a small editable section under the
        // Properties scroll content with: position label, Quantity − / + buttons,
        // and a Delete button. All actions are recorded in the UndoStack.

        private void RebuildInstanceActions()
        {
            // Always destroy the previous block so refresh is idempotent.
            if (_instanceActionsGo != null)
            {
                if (Application.isPlaying) Destroy(_instanceActionsGo);
                else DestroyImmediate(_instanceActionsGo);
                _instanceActionsGo = null;
            }
            if (_uiRefs.PropsContent == null) return;
            if (_selectedInstance == null || _selectedInstance.Item == null) return;

            // Container row block.
            _instanceActionsGo = new GameObject("InstanceActions", typeof(RectTransform));
            _instanceActionsGo.transform.SetParent(_uiRefs.PropsContent, false);
            var vlg = _instanceActionsGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            var bgImg = _instanceActionsGo.AddComponent<Image>();
            bgImg.color = new Color(0.12f, 0.14f, 0.18f, 0.9f);
            _instanceActionsGo.AddComponent<LayoutElement>().preferredHeight = 110f;

            var pos = _selectedInstance.transform.position;
            var sb = new StringBuilder(256);
            sb.Append("<b>── Instance ──</b>\n");
            sb.Append($"Position: ({pos.x:F2}, {pos.y:F2})\n");
            // Surface the per-instance data that the player should be able to
            // tell apart from the catalog defaults: dropId, persistence flavor,
            // remaining TTL. These belong to the WorldPickup, not the
            // ItemDefinition — same item, two different runtime states.
            if (_selectedInstance.IsPersistent && !string.IsNullOrEmpty(_selectedInstance.DropId))
            {
                sb.Append($"Drop id: <i>{_selectedInstance.DropId}</i>\n");
                sb.Append("Persistence: <b>Persistent</b>\n");
                if (_selectedInstance.IsInfiniteTtl)
                {
                    sb.Append("TTL: <b>infinite</b>");
                }
                else
                {
                    sb.Append($"TTL: {_selectedInstance.DespawnTtlSeconds:F0}s  •  remaining: {_selectedInstance.SecondsUntilExpiry:F0}s");
                }
            }
            else
            {
                sb.Append("Persistence: <b>Ephemeral</b> (won't be saved)");
            }
            AddLabel(_instanceActionsGo.transform, sb.ToString());

            // Quantity row: − [N] +
            var qtyRow = new GameObject("QtyRow", typeof(RectTransform));
            qtyRow.transform.SetParent(_instanceActionsGo.transform, false);
            var hlg = qtyRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            qtyRow.AddComponent<LayoutElement>().preferredHeight = 24f;

            AddLabel(qtyRow.transform, "Qty:", 30f);
            AddBtn(qtyRow.transform, "−", 24f, () => AdjustSelectedQuantity(-1));
            var qtyLabel = AddLabel(qtyRow.transform, _selectedInstance.Quantity.ToString(), 40f);
            qtyLabel.alignment = TextAlignmentOptions.Center;
            qtyLabel.fontStyle = FontStyles.Bold;
            AddBtn(qtyRow.transform, "+", 24f, () => AdjustSelectedQuantity(+1));

            // Delete row
            var delRow = new GameObject("DelRow", typeof(RectTransform));
            delRow.transform.SetParent(_instanceActionsGo.transform, false);
            delRow.AddComponent<LayoutElement>().preferredHeight = 24f;
            AddBtn(delRow.transform, "Delete", 0f, DeleteSelectedInstance, danger: true)
                .GetComponent<RectTransform>().sizeDelta = new Vector2(0, 24);
        }

        private TextMeshProUGUI AddLabel(Transform parent, string text, float preferredW = -1f)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.richText = true;
            tmp.fontSize = 11f;
            tmp.color = EditorUIHelpers.TEXT_PRIMARY;
            tmp.enableWordWrapping = true;
            if (preferredW > 0f) go.AddComponent<LayoutElement>().preferredWidth = preferredW;
            return tmp;
        }

        private GameObject AddBtn(Transform parent, string label, float width,
            System.Action onClick, bool danger = false)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = danger
                ? new Color(0.55f, 0.18f, 0.18f, 1f)
                : EditorUIHelpers.BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            if (width > 0f) go.AddComponent<LayoutElement>().preferredWidth = width;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            EditorUIHelpers.StretchFill(labelGo);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 11f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = danger ? Color.white : EditorUIHelpers.TEXT_PRIMARY;
            return go;
        }

        // ── Instance edit actions ────────────────────────────────────────────

        private void AdjustSelectedQuantity(int delta)
        {
            var pickup = _selectedInstance;
            if (pickup == null) return;
            int oldQty = pickup.Quantity;
            int newQty = Mathf.Max(1, oldQty + delta);
            if (newQty == oldQty) return;

            // WorldPickup.quantity is private — write via reflection (no public setter
            // exists at runtime, and we don't want to widen the API just for the editor).
            var f = typeof(WorldPickup).GetField("quantity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) { SetStatus("Cannot mutate quantity (field missing)."); return; }

            // Mirror the live mutation into the persistence cache so the saved
            // file stays in sync with what the player sees.
            var service = ResolveDropService();
            string persistDropId = pickup.IsPersistent ? pickup.DropId : null;

            _undo.Record(new UndoStack.LambdaCommand(
                $"Qty {pickup.Item?.itemId} {oldQty}→{newQty}",
                doAction: () =>
                {
                    if (pickup != null) f.SetValue(pickup, newQty);
                    if (service != null && persistDropId != null)
                        service.UpdateQuantity(persistDropId, newQty);
                },
                undoAction: () =>
                {
                    if (pickup != null) f.SetValue(pickup, oldQty);
                    if (service != null && persistDropId != null)
                        service.UpdateQuantity(persistDropId, oldQty);
                }));
            f.SetValue(pickup, newQty);
            if (service != null && persistDropId != null)
                service.UpdateQuantity(persistDropId, newQty);
            RefreshProperties();
            RebuildInstancesList();
            SetStatus($"Quantity {oldQty} → {newQty}.");
        }

        private void DeleteSelectedInstance()
        {
            var pickup = _selectedInstance;
            if (pickup == null) return;
            DeletePickup(pickup);
        }
    }
}
