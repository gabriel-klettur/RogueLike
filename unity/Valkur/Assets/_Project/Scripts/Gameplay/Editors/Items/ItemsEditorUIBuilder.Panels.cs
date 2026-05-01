using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor panel builders — PHASE 1 UI/UX scaffolding.
    /// Each panel mirrors a Python items_editor sub-panel:
    ///   • Modes      ← Toolbar + AddRemove panels (combined into one narrow column)
    ///   • Items      ← Picker panel (search + grid)
    ///   • Properties ← Properties panel (selected item inspector)
    ///   • Instances  ← InstancesPanel + ParamsPanel (combined, drops list + params editor)
    /// </summary>
    public static partial class ItemsEditorUIBuilder
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the GameObject has a LayoutElement with flexibleHeight=1.
        /// EditorUIHelpers.MakeScrollView only adds a LayoutElement when called
        /// with an explicit height; without it we have to add one ourselves so the
        /// scroll view fills the remaining vertical space inside its parent VLG.
        /// </summary>
        private static void EnsureFlexibleHeight(GameObject go, float flex = 1f)
        {
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleHeight = flex;
        }

        // ── Modes Panel (60 px narrow, top-left) ──────────────────────────────────
        // Modes: Select / Spawn / Delete   +   Add / Remove / Add-on-System
        // Plus Undo / Redo as inline action buttons.

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onModeSelect, Action onModeSpawn, Action onModeDelete,
            Action onAdd,        Action onRemove,   Action onAddOnSystem,
            Action onUndo,       Action onRedo)
        {
            refs.ModesDropdown = MakeDrop("ItemsModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag, narrowPanel: true);

            // Mode buttons (highlight reflects active mode)
            refs.SelectBtnImg = AddToolBtn(t, "Sel", "ect",   BTN_H, onModeSelect);
            refs.SpawnBtnImg  = AddToolBtn(t, "Spwn", "+",    BTN_H, onModeSpawn);
            refs.DeleteBtnImg = AddDangerToolBtn(t, "Del", "X", BTN_H, onModeDelete);

            AddInlineSeparator(t);
            AddSectionLabel(t, "ADD");

            refs.AddBtnImg         = AddToolBtn(t, "Add",  "to map", BTN_H, onAdd);
            refs.RemoveBtnImg      = AddDangerToolBtn(t, "Rem", "from map", BTN_H, onRemove);
            refs.AddOnSystemBtnImg = AddToolBtn(t, "+Sys", "system", BTN_H, onAddOnSystem);

            AddInlineSeparator(t);
            AddSectionLabel(t, "EDIT");

            AddActionBtn(t, "Undo", 24f, onUndo);
            AddActionBtn(t, "Redo", 24f, onRedo);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Items Panel (256 px, picker, top-left after Modes) ────────────────────
        // Search box + 3-column grid catalog of ItemDefinitions.

        private static void BuildItemsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP;
            refs.ItemsDropdown = MakeDrop("ItemsCatalogPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET,
                ITEMS_W, ITEMS_H, "Items",
                out var t, out refs.ItemsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search items…", onSearchChanged);

            var (scroll, gridContent) = EditorUIHelpers.MakeGridPicker(
                t, "ItemsGrid", columns: 3, cellSize: 64f, spacing: 4f);
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = gridContent;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);
            refs.StatusText.text = "Phase 1 — UI scaffolding (catalog grid Phase 2)";

            refs.ItemsDropdown.SetActive(false);
        }

        // ── Properties Panel (250 px, top-right) ──────────────────────────────────
        // Inspector for the selected ItemDefinition (Phase 2: real property editor).

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("ItemsPropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Title strip
            var titleGo = CreateUI("PropsTitle", t);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var titleTmp        = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text       = "(no item selected)";
            titleTmp.fontSize   = 12f;
            titleTmp.fontStyle  = FontStyles.Bold;
            titleTmp.alignment  = TextAlignmentOptions.Center;
            titleTmp.color      = ACCENT;
            refs.PropsText      = titleTmp;

            AddInlineSeparator(t);

            // Scrollable content area for future fields (id / name / image / params / …)
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "PropsScroll");
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PropsContent = content;

            // Phase 1 hint placeholder
            var hintGo = CreateUI("PropsHint", content);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 60f;
            var hintTmp        = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text       = "Properties editor will be wired in Phase 2.\n\nFields planned:\n  • id\n  • display name\n  • icon / image\n  • params (price, count, …)";
            hintTmp.fontSize   = 10f;
            hintTmp.fontStyle  = FontStyles.Italic;
            hintTmp.alignment  = TextAlignmentOptions.TopLeft;
            hintTmp.color      = TEXT_SECONDARY;
            hintTmp.enableWordWrapping = true;

            refs.PropsDropdown.SetActive(false);
        }

        // ── Instances Panel (280 px, bottom-right) ────────────────────────────────
        // Lists items currently dropped on the map + per-instance params (Phase 2).

        private static void BuildInstancesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.InstancesDropdown = MakeDrop("ItemsInstancesPanel", canvasT,
                PanelDock.BottomRight, PANEL_GAP, PANEL_GAP,
                INSTANCES_W, INSTANCES_H, "Instances",
                out var t, out refs.InstancesPanelDrag);

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "InstancesScroll");
            EnsureFlexibleHeight(scroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.InstancesListContent = content;

            // Empty-state hint
            var hintGo = CreateUI("InstancesHint", content);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 80f;
            var hintTmp        = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text       = "(no item drops on the map)\n\nUse Add mode to drop items.\nClick a drop in this list to inspect or edit its params.";
            hintTmp.fontSize   = 10f;
            hintTmp.fontStyle  = FontStyles.Italic;
            hintTmp.alignment  = TextAlignmentOptions.Center;
            hintTmp.color      = TEXT_MUTED;
            hintTmp.enableWordWrapping = true;
            refs.InstancesHint = hintTmp;

            refs.InstancesDropdown.SetActive(false);
        }
    }
}
