using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the Size dropdown panel for the runtime Tile Editor.
    /// Sits TopRight of the canvas, immediately to the LEFT of the Colliders dropdown.
    ///
    /// Controls:
    ///   • Big value label (NxN).
    ///   • A slim horizontal slider (1..25, integer steps) flanked by "1" / "25"
    ///     labels. Shares the brush-size state with the menu-bar's −/+ stepper.
    /// State is owned by <see cref="TileEditorState.BrushSize"/>; the manager handles
    /// changes via <c>OnBrushSizeChanged</c> and the UI repaints with
    /// <see cref="TileEditorUI.RefreshBrushSizeLabel"/>.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Stack right-to-left: Inspector → Colliders → Size.
        private static float SizeX => PANEL_GAP + TILE_INSPECTOR_DROP_W + PANEL_GAP + COLLIDERS_DROP_W + PANEL_GAP;
        private static float SizeY => PANEL_TOP_OFFSET;

        // Slider geometry (constants so the row's manual anchoring stays in sync
        // with the slim-track helper's expected dimensions).
        private const float SIZE_SLIDER_ROW_H   = 28f;
        private const float SIZE_SLIDER_TRACK_H = 4f;
        private const float SIZE_SLIDER_THUMB   = 14f;
        private const float SIZE_END_LABEL_W    = 18f;
        private const float SIZE_END_LABEL_GAP  = 6f;

        private static void BuildSizeDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            refs.SizeDropdown = MakeDropdownPanel("SizeDropdown", canvasT,
                PanelDock.TopRight, SizeX, SizeY, SIZE_DROP_W, SIZE_DROP_H,
                "Brush Size", out var sizeContent, out refs.SizePanelDrag);

            var t = sizeContent;

            BuildSizeValueLabel(t, state, ref refs);
            BuildSizeSliderRow(t, state, ref refs, onBrushSizeChanged);

            refs.SizeDropdown.SetActive(false);
        }

        private static void BuildSizeValueLabel(Transform parent, TileEditorState state, ref UIRefs refs)
        {
            var go = CreateUI("Value", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 32f;
            refs.BrushSizeLabel = go.AddComponent<TextMeshProUGUI>();
            refs.BrushSizeLabel.text = $"{state.BrushSize}x{state.BrushSize}";
            refs.BrushSizeLabel.fontSize = 22f;
            refs.BrushSizeLabel.fontStyle = FontStyles.Bold;
            refs.BrushSizeLabel.alignment = TextAlignmentOptions.Center;
            refs.BrushSizeLabel.color = ACCENT;
            refs.BrushSizeLabel.raycastTarget = false;
        }

        /// <summary>
        /// Slim slider with min/max end labels. Built with explicit anchors instead
        /// of a HorizontalLayoutGroup because the inner Slider needs a stretched
        /// RectTransform (handle pivots break visually if the host has a default
        /// 100×100 sizeDelta inherited from a non-layout-controlled parent).
        /// </summary>
        private static void BuildSizeSliderRow(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            var row = CreateUI("SliderRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = SIZE_SLIDER_ROW_H;

            // Min label — anchored to the row's left edge.
            BuildEndLabel(row.transform, "MinLbl",
                TileEditorConstants.MinBrushSize.ToString(),
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                pivot:     new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(2f, 0f));

            // Max label — anchored to the row's right edge.
            BuildEndLabel(row.transform, "MaxLbl",
                TileEditorConstants.MaxBrushSize.ToString(),
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot:     new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-2f, 0f));

            // Slider host — stretches between the two end labels with a small gap.
            var sliderHost = CreateUI("SliderHost", row.transform);
            var hostRt = sliderHost.GetComponent<RectTransform>();
            hostRt.anchorMin = new Vector2(0f, 0f);
            hostRt.anchorMax = new Vector2(1f, 1f);
            hostRt.pivot     = new Vector2(0.5f, 0.5f);
            float pad = SIZE_END_LABEL_W + SIZE_END_LABEL_GAP + 2f;
            hostRt.offsetMin = new Vector2(pad, 0f);
            hostRt.offsetMax = new Vector2(-pad, 0f);

            refs.BrushSizeSlider = UISlider.MakeSlimTrack(sliderHost.transform, "Slider",
                min: TileEditorConstants.MinBrushSize,
                max: TileEditorConstants.MaxBrushSize,
                initial: state.BrushSize,
                onValueChanged: v => onBrushSizeChanged?.Invoke(Mathf.RoundToInt(v)),
                hitHeight:   SIZE_SLIDER_ROW_H,
                trackHeight: SIZE_SLIDER_TRACK_H,
                thumbSize:   SIZE_SLIDER_THUMB,
                trackColor:  new Color(0.18f, 0.20f, 0.24f, 1f),
                fillColor:   ACCENT_DIM,
                handleColor: ACCENT);
            refs.BrushSizeSlider.wholeNumbers = true;

            // Stretch-fill the slider inside its host so the handle's pivots resolve
            // to the host's (and therefore the row's) actual dimensions.
            UIFactory.StretchFill(refs.BrushSizeSlider.gameObject);
        }

        private static void BuildEndLabel(Transform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos)
        {
            var go = CreateUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot     = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(SIZE_END_LABEL_W, 0f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TEXT_MUTED;
            tmp.raycastTarget = false;
        }
    }
}
