using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the SelectModes dropdown panel that appears immediately to the right of the
    /// Tools panel whenever the Select tool is active. Three radio rows let the user pick
    /// the active <see cref="TileEditorState.SelectMode"/> (Single / Rect / Multi) and a
    /// row of four square buttons exposes Copy / Cut / Paste / Clear-Selection.
    ///
    /// Visibility is controlled by <see cref="TileEditorUI.RefreshToolHighlights"/>: panel
    /// is shown when CurrentTool == Select and hidden otherwise. The user-validated UX
    /// decision is that <see cref="TileEditorState.SelectedCells"/> is also cleared when
    /// leaving Select; the clipboard, however, persists across tool changes.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Stack on the LEFT edge of the canvas, immediately right of Tools.
        private static float SelectModesX => PANEL_GAP + TOOLS_DROP_W + PANEL_GAP;
        private static float SelectModesY => PANEL_TOP_OFFSET;

        private static void BuildSelectModesDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.SelectMode> onSelectModeChanged,
            System.Action onCopyClicked,
            System.Action onCutClicked,
            System.Action onPasteClicked,
            System.Action onClearSelectionClicked)
        {
            refs.SelectModesDropdown = MakeDropdownPanel("SelectModesDropdown", canvasT,
                PanelDock.TopLeft, SelectModesX, SelectModesY,
                SELECT_MODES_DROP_W, SELECT_MODES_DROP_H,
                "Select Modes", out var content, out refs.SelectModesPanelDrag);

            var t = content;

            // ── Three radio rows ────────────────────────────────────────────
            BuildColliderToggleRow(t, "Single",
                state.CurrentSelectMode == TileEditorState.SelectMode.Single,
                () => onSelectModeChanged?.Invoke(TileEditorState.SelectMode.Single),
                out refs.ModeSingleToggleImg, out refs.ModeSingleToggleLabel);

            BuildColliderToggleRow(t, "Rect",
                state.CurrentSelectMode == TileEditorState.SelectMode.Rect,
                () => onSelectModeChanged?.Invoke(TileEditorState.SelectMode.Rect),
                out refs.ModeRectToggleImg, out refs.ModeRectToggleLabel);

            BuildColliderToggleRow(t, "Multi",
                state.CurrentSelectMode == TileEditorState.SelectMode.Multi,
                () => onSelectModeChanged?.Invoke(TileEditorState.SelectMode.Multi),
                out refs.ModeMultiToggleImg, out refs.ModeMultiToggleLabel);

            BuildSeparator(t);

            // ── Clipboard action row: [Copy][Cut][Paste][Clear] ─────────────
            BuildClipboardActionRow(t, ref refs,
                onCopyClicked, onCutClicked, onPasteClicked, onClearSelectionClicked);

            BuildSeparator(t);

            // Hint text (kbd shortcuts)
            var hintGo = CreateUI("Hint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hint = hintGo.AddComponent<TextMeshProUGUI>();
            hint.text = "Ctrl+C copy · Ctrl+X cut · Ctrl+V paste · Esc / RMB clear";
            hint.fontSize = 9f;
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.color = TEXT_MUTED;
            hint.enableWordWrapping = true;

            refs.SelectModesDropdown.SetActive(false);
        }

        /// <summary>
        /// Horizontal row of four equally-sized clipboard action buttons. Paste's
        /// interactable state is driven separately by <c>RefreshClipboardButtons</c>.
        /// </summary>
        private static void BuildClipboardActionRow(Transform parent, ref UIRefs refs,
            System.Action onCopy, System.Action onCut, System.Action onPaste, System.Action onClear)
        {
            var row = CreateUI("ClipboardRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 36f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;

            refs.CopyButton  = MakeClipboardActionBtn(row.transform, "Copy",  "C", onCopy,  out refs.CopyButtonImg);
            refs.CutButton   = MakeClipboardActionBtn(row.transform, "Cut",   "X", onCut,   out refs.CutButtonImg);
            refs.PasteButton = MakeClipboardActionBtn(row.transform, "Paste", "V", onPaste, out refs.PasteButtonImg);
            refs.ClearSelectionButton = MakeClipboardActionBtn(row.transform, "Clear", "esc", onClear,
                out refs.ClearSelectionButtonImg);
        }

        private static Button MakeClipboardActionBtn(Transform parent, string name, string letter,
            System.Action onClicked, out Image bgImg)
        {
            var go = CreateUI($"Action_{name}", parent);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bgImg = go.AddComponent<Image>();
            bgImg.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            c.disabledColor    = new Color(BTN_NORMAL.r, BTN_NORMAL.g, BTN_NORMAL.b, 0.35f);
            btn.colors = c;
            btn.targetGraphic = bgImg;
            btn.onClick.AddListener(() => onClicked?.Invoke());

            // Vertical stack: big letter on top, small label below.
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(2, 2, 2, 2);
            v.spacing = 0f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childAlignment = TextAnchor.MiddleCenter;

            var letterGo = CreateUI("L", go.transform);
            letterGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var lt = letterGo.AddComponent<TextMeshProUGUI>();
            lt.text = letter;
            lt.fontSize = 13f;
            lt.fontStyle = FontStyles.Bold;
            lt.alignment = TextAlignmentOptions.Center;
            lt.color = ACCENT;
            lt.raycastTarget = false;

            var nameGo = CreateUI("N", go.transform);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            var nt = nameGo.AddComponent<TextMeshProUGUI>();
            nt.text = name;
            nt.fontSize = 9f;
            nt.alignment = TextAlignmentOptions.Center;
            nt.color = TEXT_SECONDARY;
            nt.raycastTarget = false;

            return btn;
        }
    }
}
