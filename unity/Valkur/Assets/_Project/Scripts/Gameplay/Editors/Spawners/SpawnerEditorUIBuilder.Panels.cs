using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spawners
{
    public static partial class SpawnerEditorUIBuilder
    {
        // ── Tools Panel ─────────────────────────────────────────────────────────
        // Mirrors Python spawner_tool_bar_panel: undo/redo + save/reload actions.

        private static void BuildToolsPanel(Transform canvasT, ref UIRefs refs,
            Action onUndo, Action onRedo, Action onSave, Action onReload)
        {
            refs.ToolsDropdown = EditorUIHelpers.MakeDropPanel("SpawnerToolsPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                TOOLS_W, TOOLS_H, "Tools",
                out var t, out refs.ToolsPanelDrag);

            refs.UndoBtnImg   = EditorUIHelpers.AddActionBtn(t, "Undo",   44f, onUndo,   out _);
            refs.RedoBtnImg   = EditorUIHelpers.AddActionBtn(t, "Redo",   44f, onRedo,   out _);
            refs.SaveBtnImg   = EditorUIHelpers.AddActionBtn(t, "Save",   44f, onSave,   out _);
            refs.ReloadBtnImg = EditorUIHelpers.AddActionBtn(t, "Reload", 44f, onReload, out _);

            refs.ToolsDropdown.SetActive(false);
        }

        // ── Picker Panel ────────────────────────────────────────────────────────
        // Mirrors Python spawner_picker_panel: search box + scrollable template list.

        private static void BuildPickerPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.PickerDropdown = EditorUIHelpers.MakeDropPanel("SpawnerPickerPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                PICKER_W, PICKER_H, "Picker",
                out var t, out refs.PickerPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search templates…",
                v => onSearchChanged?.Invoke(v ?? string.Empty));

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "TemplateScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = content;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.PickerDropdown.SetActive(false);
        }

        // ── Modes Panel ─────────────────────────────────────────────────────────
        // Mirrors Python spawner_add_remove_panel: Select / Place / Delete.

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onSelect, Action onPlace, Action onDelete)
        {
            refs.ModesDropdown = EditorUIHelpers.MakeDropPanel("SpawnerModesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP + PROPS_W + PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag);

            refs.SelectBtnImg = EditorUIHelpers.AddModeBtn(t, "Select", "Pick a spawner",  44f, onSelect, out refs.SelectBtnTmp);
            refs.PlaceBtnImg  = EditorUIHelpers.AddModeBtn(t, "Place",  "Stamp template",  44f, onPlace,  out refs.PlaceBtnTmp);
            refs.DeleteBtnImg = EditorUIHelpers.AddModeBtn(t, "Delete", "Remove spawner",  44f, onDelete, out refs.DeleteBtnTmp);

            var hintGo = EditorUIHelpers.CreateUI("ModesHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            refs.ModesHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.ModesHintText.text               = "Pick a mode then click on the map.";
            refs.ModesHintText.fontSize           = 10f;
            refs.ModesHintText.color              = UITheme.TEXT_SECONDARY;
            refs.ModesHintText.enableWordWrapping = true;
            refs.ModesHintText.alignment          = TextAlignmentOptions.Center;

            refs.ModesDropdown.SetActive(false);
        }

        // ── Properties Panel ────────────────────────────────────────────────────
        // Mirrors Python spawner_properties_panel: read-only form on the
        // currently selected spawner instance.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = EditorUIHelpers.MakeDropPanel("SpawnerPropsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Scrollable content so long property dumps don't overflow.
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "PropsScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            var textGo = EditorUIHelpers.CreateUI("PropsText", content);
            var rt     = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0f, 1f);

            var fitter = textGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            refs.PropsText                     = textGo.AddComponent<TextMeshProUGUI>();
            refs.PropsText.text                = "<i>No spawner selected.</i>";
            refs.PropsText.fontSize            = 11f;
            refs.PropsText.color               = UITheme.TEXT_PRIMARY;
            refs.PropsText.alignment           = TextAlignmentOptions.TopLeft;
            refs.PropsText.enableWordWrapping  = true;
            refs.PropsText.richText            = true;
            refs.PropsText.margin              = new Vector4(8f, 4f, 12f, 4f);

            refs.PropsDropdown.SetActive(false);
        }
    }
}
