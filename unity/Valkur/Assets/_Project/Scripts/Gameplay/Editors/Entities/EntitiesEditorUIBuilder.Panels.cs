using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Entities
{
    public static partial class EntitiesEditorUIBuilder
    {
        // ── Tools Panel ───────────────────────────────────────────────────────────
        // Mirrors Python entities_tool_bar_panel: undo/redo + save/reload actions.

        private static void BuildToolsPanel(Transform canvasT, ref UIRefs refs,
            Action onUndo, Action onRedo, Action onSave, Action onReload)
        {
            refs.ToolsDropdown = MakeDrop("EntitiesToolsPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                TOOLS_W, TOOLS_H, "Tools",
                out var t, out refs.ToolsPanelDrag, narrowPanel: true);

            refs.UndoBtnImg   = AddActionBtn(t, "Undo",   44f, onUndo,   out _);
            refs.RedoBtnImg   = AddActionBtn(t, "Redo",   44f, onRedo,   out _);
            refs.SaveBtnImg   = AddActionBtn(t, "Save",   44f, onSave,   out _);
            refs.ReloadBtnImg = AddActionBtn(t, "Reload", 44f, onReload, out _);

            refs.ToolsDropdown.SetActive(false);
        }

        // ── Categories Panel ──────────────────────────────────────────────────────
        // Mirrors Python entities_picker_panel category tabs:
        // Hostiles / Neutrals / Specials / Players.

        private static void BuildCategoriesPanel(Transform canvasT, ref UIRefs refs,
            Action onHostiles, Action onNeutrals, Action onSpecials, Action onPlayers)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.CategoriesDropdown = MakeDrop("EntitiesCategoriesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                CATEGORIES_W, CATEGORIES_H, "Categories",
                out var t, out refs.CategoriesPanelDrag);

            refs.HostilesTabImg = AddTabBtn(t, "Hostiles", 36f, onHostiles, out refs.HostilesTabTmp);
            refs.NeutralsTabImg = AddTabBtn(t, "Neutrals", 36f, onNeutrals, out refs.NeutralsTabTmp);
            refs.SpecialsTabImg = AddTabBtn(t, "Specials", 36f, onSpecials, out refs.SpecialsTabTmp);
            refs.PlayersTabImg  = AddTabBtn(t, "Players",  36f, onPlayers,  out refs.PlayersTabTmp);

            refs.CategoriesDropdown.SetActive(false);
        }

        // ── Picker Panel ──────────────────────────────────────────────────────────
        // Mirrors Python entities_picker_panel grid: search box + thumbnail grid.

        private static void BuildPickerPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP + CATEGORIES_W + PANEL_GAP;
            refs.PickerDropdown = MakeDrop("EntitiesPickerPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                PICKER_W, PICKER_H, "Picker",
                out var t, out refs.PickerPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search entities\u2026",
                v => onSearchChanged?.Invoke(v ?? ""));

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(t, "EntityGrid", 3, 72f, 4f);
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = content;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.PickerDropdown.SetActive(false);
        }

        // ── Add/Remove Panel ──────────────────────────────────────────────────────
        // Mirrors Python entities_add_remove_panel:
        //   ADD_ENTITIE / REMOVE_ENTITIE / ADD_ENTITIES_ON_SYSTEM / CONFIRM.

        private static void BuildAddRemovePanel(Transform canvasT, ref UIRefs refs,
            Action onAdd, Action onRemove, Action onAddOnSystem, Action onConfirm)
        {
            refs.AddRemoveDropdown = MakeDrop("EntitiesAddRemovePanel", canvasT,
                PanelDock.TopRight, PANEL_GAP + PROPS_W + PANEL_GAP, PANEL_TOP_OFFSET,
                ADDREM_W, ADDREM_H, "Add / Remove",
                out var t, out refs.AddRemovePanelDrag);

            refs.AddBtnImg         = AddModeBtn(t, "Add",            "Spawn entity",  44f, onAdd,         out refs.AddBtnTmp);
            refs.RemoveBtnImg      = AddModeBtn(t, "Remove",         "Delete entity", 44f, onRemove,      out refs.RemoveBtnTmp);
            refs.AddOnSystemBtnImg = AddModeBtn(t, "Add on System",  "New class",     44f, onAddOnSystem, out refs.AddOnSystemBtnTmp);
            refs.ConfirmBtnImg     = AddModeBtn(t, "Confirm",        "Persist class", 44f, onConfirm,     out refs.ConfirmBtnTmp);

            // Hint
            var hintGo = CreateUI("AddRemHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            refs.AddRemoveHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.AddRemoveHintText.text               = "Select a mode then click on the map.";
            refs.AddRemoveHintText.fontSize           = 10f;
            refs.AddRemoveHintText.color              = TEXT_SECONDARY;
            refs.AddRemoveHintText.enableWordWrapping = true;
            refs.AddRemoveHintText.alignment          = TextAlignmentOptions.Center;

            refs.AddRemoveDropdown.SetActive(false);
        }
    }
}
