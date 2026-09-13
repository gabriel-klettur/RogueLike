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
            Action onAll, Action onHostiles, Action onNeutrals, Action onSpecials, Action onPlayers)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.CategoriesDropdown = MakeDrop("EntitiesCategoriesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                CATEGORIES_W, CATEGORIES_H, "Categories",
                out var t, out refs.CategoriesPanelDrag);

            refs.AllTabImg      = AddTabBtn(t, "All",      36f, onAll,      out refs.AllTabTmp);
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

            // RESPONSIVE, not three fixed columns. The panel is resizable now, and a fixed
            // column count turns every extra pixel of width into dead space on the right --
            // which is exactly what it looked like at the shipped size. GridAutoSize derives
            // the count from the width it is given and stretches the cells to fill the
            // remainder, so there is no leftover gutter at any size.
            var (scroll, content, _) = EditorUIHelpers.MakeResponsiveGridPicker(
                t, "EntityGrid", minCellSize: 64f, maxCellSize: 96f, spacing: 4f);
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 160f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = content;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            BuildPickerResizeHandle(refs.PickerDropdown);

            refs.PickerDropdown.SetActive(false);
        }

        /// <summary>Side of the square drag grip, in canvas pixels.</summary>
        private const float PICKER_RESIZE_HANDLE_PX = 16f;

        /// <summary>
        /// The Picker's bottom-right drag grip.
        ///
        /// <para><b>BottomRight because the panel is TOP-LEFT pivoted.</b> A rect grows away from
        /// its pivot and never towards it, so this is the one corner whose drag can change both
        /// width and height: a bottom-LEFT grip could only move the panel and a top-right one
        /// only its width.</para>
        ///
        /// <para>The minimum is about two cells plus the scrollbar. Narrower and the grid
        /// collapses to one column while the search box wraps -- a shape nobody wants and one
        /// the grip can otherwise be dragged into by accident. The size is remembered for free:
        /// EditorWorkspaceService already captures and restores every DraggablePanel's
        /// geometry.</para>
        /// </summary>
        private static void BuildPickerResizeHandle(GameObject panelRoot)
        {
            if (panelRoot == null) return;
            var panelRt = panelRoot.GetComponent<RectTransform>();
            if (panelRt == null) return;

            var go = CreateUI("ResizeHandle", panelRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(PICKER_RESIZE_HANDLE_PX, PICKER_RESIZE_HANDLE_PX);

            // The glyph mirrors its own MESH rather than being rotated or negatively scaled:
            // both of those turn the rect about its corner pivot and swing the grip outside
            // the very panel it resizes.
            var tri = go.AddComponent<TriangleHandleGraphic>();
            tri.color = UITheme.BORDER;
            tri.raycastTarget = true;

            var handle     = go.AddComponent<PanelResizeHandle>();
            handle.Target  = panelRt;
            handle.Corner  = ResizeGripCorner.BottomRight;
            handle.MinSize = new Vector2(200f, 220f);
            handle.MaxSize = new Vector2(900f, 1200f);
        }

        // ── Add/Remove Panel ──────────────────────────────────────────────────────
        // Mirrors Python entities_add_remove_panel:
        //   ADD_ENTITIE / REMOVE_ENTITIE / ADD_ENTITIES_ON_SYSTEM / CONFIRM.
        //
        // The catalog-authoring row (New/Rename Key + Duplicate/Rename buttons) is new:
        // "Add on System" + "Confirm" used to be a status-string stub, and there was no
        // way to duplicate or rename a definition without leaving Play Mode for the
        // Inspector. One text field feeds both verbs — Confirm reads it as the key for a
        // brand-new definition, Rename reads it as the new key/name for whichever
        // definition is selected in the Picker — so the panel does not grow a second
        // input for what is the same gesture ("type a name, then say what it names").

        private static void BuildAddRemovePanel(Transform canvasT, ref UIRefs refs,
            Action onAdd, Action onRemove, Action onAddOnSystem, Action onConfirm,
            Action<string> onNewKeyChanged, Action onDuplicate, Action onRename)
        {
            refs.AddRemoveDropdown = MakeDrop("EntitiesAddRemovePanel", canvasT,
                PanelDock.TopRight, PANEL_GAP + PROPS_W + PANEL_GAP, PANEL_TOP_OFFSET,
                ADDREM_W, ADDREM_H, "Add / Remove",
                out var t, out refs.AddRemovePanelDrag);

            refs.AddBtnImg         = AddModeBtn(t, "Add",            "Spawn entity",  44f, onAdd,         out refs.AddBtnTmp);
            refs.RemoveBtnImg      = AddModeBtn(t, "Remove",         "Delete entity", 44f, onRemove,      out refs.RemoveBtnTmp);
            refs.AddOnSystemBtnImg = AddModeBtn(t, "Add on System",  "New class",     44f, onAddOnSystem, out refs.AddOnSystemBtnTmp);
            refs.ConfirmBtnImg     = AddModeBtn(t, "Confirm",        "Persist class", 44f, onConfirm,     out refs.ConfirmBtnTmp);

            // New/Rename key text field — shared by Confirm (create) and Rename.
            var keyRowGo = CreateUI("NewKeyRow", t);
            keyRowGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var keyHlg = keyRowGo.AddComponent<HorizontalLayoutGroup>();
            keyHlg.spacing               = 6f;
            keyHlg.childForceExpandWidth  = false;
            keyHlg.childForceExpandHeight = true;
            keyHlg.childControlWidth      = true;
            keyHlg.childControlHeight     = true;
            // AddCommit fires onCommit on Enter/blur (TMP onEndEdit); Confirm/Rename also need
            // the LIVE text if the author clicks straight from typing without pressing Enter,
            // so onValueChanged feeds the same callback on every keystroke too.
            refs.NewKeyInput = UIInputField.AddCommit(keyRowGo.transform, "", v => onNewKeyChanged?.Invoke(v), 18f, 10f);
            refs.NewKeyInput.contentType = TMP_InputField.ContentType.Standard;
            refs.NewKeyInput.onValueChanged.AddListener(v => onNewKeyChanged?.Invoke(v));
            var keyLe = refs.NewKeyInput.GetComponent<LayoutElement>();
            if (keyLe != null) keyLe.flexibleWidth = 1f;

            refs.DuplicateBtnImg = AddModeBtn(t, "Duplicate", "Clone selected", 40f, onDuplicate, out refs.DuplicateBtnTmp);
            refs.RenameBtnImg    = AddModeBtn(t, "Rename",    "Apply key above", 40f, onRename,   out refs.RenameBtnTmp);

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
