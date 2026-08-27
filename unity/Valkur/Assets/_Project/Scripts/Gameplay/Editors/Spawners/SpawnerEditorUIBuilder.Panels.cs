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

        // ── Properties Panel ────────────────────────────────────────────────────
        // Mirrors Python spawner_properties_panel: a form on the currently selected
        // spawner instance, plus a context-sensitive Delete button that replaces the
        // legacy Modes panel "Delete" mode. The button is hidden whenever
        // <c>_selectedInstance == null</c> so the panel only exposes destructive
        // actions when something is actually picked.
        //
        // The form itself used to be a single read-only StringBuilder dump into one
        // TextMeshProUGUI. It is now a row-based form — read-only labels for identity
        // and runtime info, committed input fields for the numeric template fields
        // that actually drive behaviour — mirroring the pattern
        // EntitiesEditorUIBuilder.AddEditableRow established for F5's monster stats.
        // PropsFormRoot is the VerticalLayoutGroup content MakeScrollView already
        // builds; SpawnerEditorManager.RefreshPropertiesPanel clears and repopulates
        // it on every selection change via EntitiesEditorUIBuilder.AddPropertyRow /
        // AddEditableRow, reused as-is since both editors share the Gameplay assembly.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action onDeleteSelected)
        {
            refs.PropsDropdown = EditorUIHelpers.MakeDropPanel("SpawnerPropsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Scrollable content so a long form doesn't overflow the panel.
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "PropsScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 200f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            // MakeScrollView's content already carries a VerticalLayoutGroup +
            // ContentSizeFitter, which is what AddPropertyRow/AddEditableRow need from
            // their parent to stack — but the generic helper leaves childControlWidth/
            // Height at Unity's default (false), which only POSITIONS children rather
            // than sizing them. Force both on, matching the explicit setup
            // EntitiesEditorUIBuilder.MakeFormSection uses for the same job, so every
            // row actually renders at its LayoutElement-declared height instead of
            // whatever its own freshly-created RectTransform happened to default to.
            var contentVlg = content.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.childControlWidth  = true;
                contentVlg.childControlHeight = true;
            }
            refs.PropsFormRoot = content;

            // Visually divides the properties form from the destructive action below —
            // same separator the Particles Properties panel uses before its "Delete
            // Instance" button.
            EditorUIHelpers.BuildSeparator(t);

            refs.DeleteFromPropsBtnImg = EditorUIHelpers.AddDangerBtn(
                t, "Delete spawner", 28f, onDeleteSelected, out refs.DeleteFromPropsBtnTmp);
            refs.DeleteFromPropsBtnGo = refs.DeleteFromPropsBtnImg.gameObject;
            // Start hidden — RefreshPropertiesPanel flips this when a spawner
            // is selected.
            refs.DeleteFromPropsBtnGo.SetActive(false);

            refs.PropsDropdown.SetActive(false);
        }
    }
}
