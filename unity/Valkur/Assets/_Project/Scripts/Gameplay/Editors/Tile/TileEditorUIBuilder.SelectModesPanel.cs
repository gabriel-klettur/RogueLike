using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;
using Valkur.UIKit;
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
            System.Action onClearSelectionClicked,
            System.Action<int> onMoveToLayerClicked)
        {
            refs.SelectModesDropdown = EditorUIHelpers.MakeDropPanel("SelectModesDropdown", canvasT,
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

            // ── Move-To-Layer section: slider + commit button ───────────────
            BuildMoveToLayerSection(t, state, ref refs, onMoveToLayerClicked);

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

        // Slim slider geometry — matches the Brush Size slider's visual style so
        // both runtime panels feel consistent. Kept private to this builder; the
        // Size panel owns its own copy because its constants differ (range 1..25).
        private const float MOVE_SLIDER_ROW_H   = 28f;
        private const float MOVE_SLIDER_TRACK_H = 4f;
        private const float MOVE_SLIDER_THUMB   = 14f;

        /// <summary>
        /// Section that lets the user move the current map selection to any of the
        /// nine <see cref="TilemapLayerSetup.TilemapLayer"/> layers via a slim 0..8
        /// slider and a "Move" commit button. The destination index is echoed to
        /// the value label so the user knows what the next click will commit to.
        /// </summary>
        private static void BuildMoveToLayerSection(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<int> onMoveToLayerClicked)
        {
            // Section header — small caps, left-aligned, mirrors the existing
            // section-label style used elsewhere in the editor.
            var hdrGo = CreateUI("MoveSectionLbl", parent);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var hdr = hdrGo.AddComponent<TextMeshProUGUI>();
            hdr.text = "MOVE TO LAYER";
            hdr.fontSize = 9f;
            hdr.fontStyle = FontStyles.Bold;
            hdr.alignment = TextAlignmentOptions.Left;
            hdr.color = TEXT_SECONDARY;
            hdr.raycastTarget = false;

            // Value label — dynamic "Target: {idx}: {LayerName}".
            int initial = (int)state.CurrentLayer;
            var valueGo = CreateUI("MoveValueLbl", parent);
            valueGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            refs.MoveToLayerValueLabel = valueGo.AddComponent<TextMeshProUGUI>();
            refs.MoveToLayerValueLabel.text = FormatMoveToLayerLabel(initial);
            refs.MoveToLayerValueLabel.fontSize = 11f;
            refs.MoveToLayerValueLabel.alignment = TextAlignmentOptions.Center;
            refs.MoveToLayerValueLabel.color = ACCENT;
            refs.MoveToLayerValueLabel.raycastTarget = false;

            // Slider row — slim track, integer steps over [0, 8].
            var sliderRow = CreateUI("MoveSliderRow", parent);
            sliderRow.AddComponent<LayoutElement>().preferredHeight = MOVE_SLIDER_ROW_H;

            var sliderHost = CreateUI("SliderHost", sliderRow.transform);
            var hostRt = sliderHost.GetComponent<RectTransform>();
            hostRt.anchorMin = new Vector2(0f, 0f);
            hostRt.anchorMax = new Vector2(1f, 1f);
            hostRt.pivot     = new Vector2(0.5f, 0.5f);
            hostRt.offsetMin = new Vector2(4f, 0f);
            hostRt.offsetMax = new Vector2(-4f, 0f);

            refs.MoveToLayerSlider = UISlider.MakeSlimTrack(sliderHost.transform, "Slider",
                min: 0,
                max: 8, // 9 TilemapLayer values, 0..8
                initial: initial,
                onValueChanged: null, // label sync wired in TileEditorUI.Builder
                hitHeight:   MOVE_SLIDER_ROW_H,
                trackHeight: MOVE_SLIDER_TRACK_H,
                thumbSize:   MOVE_SLIDER_THUMB,
                trackColor:  new Color(0.18f, 0.20f, 0.24f, 1f),
                fillColor:   ACCENT_DIM,
                handleColor: ACCENT);
            refs.MoveToLayerSlider.wholeNumbers = true;
            UIFactory.StretchFill(refs.MoveToLayerSlider.gameObject);

            // Attach the pointer-release relay to the slider's own GameObject.
            // Coexists with Selectable's drag handling (only observes events,
            // never consumes). The commit happens here on release rather than
            // through a separate "Apply" button — matches the user's expectation
            // that picking a layer with the slider IS the action.
            refs.MoveToLayerSliderRelay = refs.MoveToLayerSlider.gameObject.AddComponent<MoveLayerSliderRelay>();
            var sliderCap = refs.MoveToLayerSlider;
            refs.MoveToLayerSliderRelay.OnReleased = () =>
                onMoveToLayerClicked?.Invoke(Mathf.RoundToInt(sliderCap.value));

            // Footer hint so the user immediately understands the slider IS the
            // commit (no hidden button to discover).
            var hintGo = CreateUI("MoveHint", parent);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var moveHint = hintGo.AddComponent<TextMeshProUGUI>();
            moveHint.text = "Release slider to move selection";
            moveHint.fontSize = 9f;
            moveHint.alignment = TextAlignmentOptions.Center;
            moveHint.color = TEXT_MUTED;
            moveHint.raycastTarget = false;
        }

        /// <summary>
        /// Build the "Target: {idx}: {LayerName}" string shown beneath the section
        /// header. Public-internal because the UI refresh path (see
        /// <see cref="TileEditorUI.RefreshMoveToLayerLabel"/>) must produce the
        /// exact same format whenever the slider moves.
        /// </summary>
        internal static string FormatMoveToLayerLabel(int sliderValue)
        {
            int idx = Mathf.Clamp(sliderValue, 0, 8);
            var layer = (TilemapLayerSetup.TilemapLayer)idx;
            return $"Target: {idx}: {layer}";
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
