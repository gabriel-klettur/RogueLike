using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the Size dropdown panel for the runtime Tile Editor.
    /// Sits TopRight of the canvas, immediately to the LEFT of the Colliders dropdown.
    ///
    /// Controls:
    ///   • A row of 1x1..5x5 preset buttons — single-click to set BrushSize.
    ///   • A −/value/+ stepper for keyboard-friendly adjustment.
    /// State is owned by <see cref="TileEditorState.BrushSize"/>; the manager handles
    /// changes via <c>OnBrushSizeChanged</c> and the UI repaints with
    /// <see cref="TileEditorUI.RefreshBrushSizeLabel"/> (which also tints the active preset).
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Stack right-to-left: Inspector → Colliders → Size.
        private static float SizeX => PANEL_GAP + INSPECTOR_DROP_W + PANEL_GAP + COLLIDERS_DROP_W + PANEL_GAP;
        private static float SizeY => PANEL_TOP_OFFSET;

        private const int MinBrushSize = 1;
        private const int MaxBrushSize = 25;

        private static void BuildSizeDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            refs.SizeDropdown = MakeDropdownPanel("SizeDropdown", canvasT,
                PanelDock.TopRight, SizeX, SizeY, SIZE_DROP_W, SIZE_DROP_H,
                "Brush Size", out var sizeContent, out refs.SizePanelDrag);

            var t = sizeContent;

            BuildSizePresetRow(t, state, ref refs, onBrushSizeChanged);
            BuildSeparator(t);
            BuildSizeStepperRow(t, state, ref refs, onBrushSizeChanged);

            // Hint
            var hintGo = CreateUI("Hint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hint = hintGo.AddComponent<TextMeshProUGUI>();
            hint.text = "Brush size affects Brush, Eraser, Select and Collider edit modes.";
            hint.fontSize = 9f;
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.color = TEXT_MUTED;
            hint.enableWordWrapping = true;

            refs.SizeDropdown.SetActive(false);
        }

        private static void BuildSizePresetRow(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            var gridContainer = CreateUI("PresetGrid", parent);
            gridContainer.AddComponent<LayoutElement>().preferredHeight = 140f; // 5 rows * ~28px each
            
            var grid = gridContainer.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(32f, 26f); // Smaller buttons for grid
            grid.spacing = new Vector2(2f, 2f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(2, 2, 2, 2);

            // Snapshot the lists (we're inside a `ref refs` scope but we can't capture
            // ref locals in lambdas — so reassign back at the end; lists are reference types).
            var imgs = refs.BrushSizePresetImgs;
            var lbls = refs.BrushSizePresetLabels;

            for (int i = MinBrushSize; i <= MaxBrushSize; i++)
            {
                int size = i; // capture
                var btnGo = CreateUI($"Size_{size}", gridContainer.transform);
                var img = btnGo.AddComponent<Image>();
                img.color = (size == state.BrushSize) ? BTN_ACTIVE : BTN_NORMAL;

                var btn = btnGo.AddComponent<Button>();
                var c = btn.colors;
                c.normalColor = img.color;
                c.highlightedColor = BTN_HOVER;
                c.pressedColor = BTN_ACTIVE;
                c.selectedColor = img.color;
                btn.colors = c;
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onBrushSizeChanged?.Invoke(size));

                var lblGo = CreateUI("Lbl", btnGo.transform);
                var lblRect = lblGo.GetComponent<RectTransform>();
                lblRect.anchorMin = Vector2.zero;
                lblRect.anchorMax = Vector2.one;
                lblRect.offsetMin = Vector2.zero;
                lblRect.offsetMax = Vector2.zero;

                var tmp = lblGo.AddComponent<TextMeshProUGUI>();
                tmp.text = size <= 9 ? $"{size}x{size}" : $"{size}"; // Shorten text for larger numbers
                tmp.fontSize = 10f; // Smaller font for grid
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = (size == state.BrushSize) ? ACCENT : TEXT_SECONDARY;
                tmp.raycastTarget = false;

                imgs.Add(img);
                lbls.Add(tmp);
            }
        }

        private static void BuildSizeStepperRow(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            var row = CreateUI("StepperRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 28f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(2, 2, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;

            var lbl = CreateUI("LL", row.transform);
            lbl.AddComponent<LayoutElement>().preferredWidth = 44f;
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text = "Size";
            lt.fontSize = 10f;
            lt.alignment = TextAlignmentOptions.Left;
            lt.color = TEXT_MUTED;

            var minus = CreateUI("Minus", row.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(minus, "-",
                () => onBrushSizeChanged?.Invoke(Mathf.Max(MinBrushSize, state.BrushSize - 1)), 12f);

            var val = CreateUI("Val", row.transform);
            val.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.BrushSizeLabel = val.AddComponent<TextMeshProUGUI>();
            refs.BrushSizeLabel.text = $"{state.BrushSize}x{state.BrushSize}";
            refs.BrushSizeLabel.fontSize = 13f;
            refs.BrushSizeLabel.fontStyle = FontStyles.Bold;
            refs.BrushSizeLabel.alignment = TextAlignmentOptions.Center;
            refs.BrushSizeLabel.color = ACCENT;

            var plus = CreateUI("Plus", row.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(plus, "+",
                () => onBrushSizeChanged?.Invoke(Mathf.Min(MaxBrushSize, state.BrushSize + 1)), 12f);
        }
    }
}
