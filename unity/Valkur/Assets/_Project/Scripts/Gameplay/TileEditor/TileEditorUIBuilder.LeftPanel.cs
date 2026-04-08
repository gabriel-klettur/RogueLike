using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        private static void BuildLeftPanel(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged)
        {
            refs.LeftPanel = MakePanel("LeftPanel", canvasT,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(PANEL_PAD, 0f), new Vector2(LEFT_WIDTH, -16f));

            var layout = refs.LeftPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 8);
            layout.spacing = SECTION_SPACING;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            refs.LeftPanel.AddComponent<CanvasGroup>();

            var t = refs.LeftPanel.transform;
            BuildSectionHeader(t, "TILE EDITOR", 20f);
            BuildSeparator(t);
            BuildToolbar(t, state, ref refs, onToolChanged);
            BuildSeparator(t);
            BuildLayerAndBrushRow(t, state, ref refs, onLayerChanged, onBrushSizeChanged);
            BuildSeparator(t);
            BuildSelectedTilePreview(t, ref refs);
            BuildSeparator(t);
            BuildSectionLabel(t, "CATEGORIES");
            BuildCategoryScroll(t, ref refs);
            BuildSectionLabel(t, "TILES");
            BuildTilePicker(t, ref refs);
            BuildTileCountRow(t, ref refs);
            BuildSeparator(t);
            BuildStatusBar(t, ref refs);
        }

        private static void BuildToolbar(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged)
        {
            var go = CreateUI("Toolbar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 44f;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            CreateToolBtn(go.transform, "Brush", "B", TileEditorState.Tool.Brush, state, ref refs, onToolChanged);
            CreateToolBtn(go.transform, "Erase", "E", TileEditorState.Tool.Eraser, state, ref refs, onToolChanged);
            CreateToolBtn(go.transform, "Fill", "F", TileEditorState.Tool.Fill, state, ref refs, onToolChanged);
            CreateToolBtn(go.transform, "Pick", "I", TileEditorState.Tool.Eyedropper, state, ref refs, onToolChanged);
            CreateToolBtn(go.transform, "Select", "S", TileEditorState.Tool.Select, state, ref refs, onToolChanged);
        }

        private static void CreateToolBtn(Transform parent, string label, string shortcut,
            TileEditorState.Tool tool, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged)
        {
            var go = CreateUI($"Tool_{tool}", parent);
            var img = go.AddComponent<Image>();
            bool active = tool == state.CurrentTool;
            img.color = active ? BTN_ACTIVE : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL; c.highlightedColor = BTN_HOVER; c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => onToolChanged?.Invoke(cap));

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.spacing = -2f;

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label; lblTmp.fontSize = 11f; lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = active ? ACCENT : TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut; keyTmp.fontSize = 9f;
            keyTmp.alignment = TextAlignmentOptions.Center; keyTmp.color = TEXT_MUTED;

            refs.ToolButtonImages[tool] = img;
            refs.ToolButtonTexts[tool] = lblTmp;
        }

        private static void BuildLayerAndBrushRow(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged,
            System.Action<int> onBrushSizeChanged)
        {
            var layerRow = CreateUI("LayerRow", parent);
            layerRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var lh = layerRow.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 4f; lh.childForceExpandWidth = false; lh.childForceExpandHeight = true;
            lh.childControlWidth = true; lh.childControlHeight = true;

            var layerLbl = CreateUI("LLbl", layerRow.transform);
            layerLbl.AddComponent<LayoutElement>().preferredWidth = 44f;
            var lt = layerLbl.AddComponent<TextMeshProUGUI>();
            lt.text = "Layer"; lt.fontSize = 11f; lt.alignment = TextAlignmentOptions.Left; lt.color = TEXT_SECONDARY;

            var prev = CreateUI("Prev", layerRow.transform);
            prev.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(prev, "<", () => { int v = (int)state.CurrentLayer - 1; if (v < 0) v = 8; onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); }, 10f);

            var lbl = CreateUI("LayerVal", layerRow.transform);
            lbl.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.LayerLabel = lbl.AddComponent<TextMeshProUGUI>();
            refs.LayerLabel.text = state.CurrentLayer.ToString();
            refs.LayerLabel.fontSize = 13f; refs.LayerLabel.fontStyle = FontStyles.Bold;
            refs.LayerLabel.alignment = TextAlignmentOptions.Center; refs.LayerLabel.color = ACCENT;

            var next = CreateUI("Next", layerRow.transform);
            next.AddComponent<LayoutElement>().preferredWidth = 26f;
            MakeBtn(next, ">", () => { int v = (int)state.CurrentLayer + 1; if (v > 8) v = 0; onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v); }, 10f);

            var spacer = CreateUI("Spacer", layerRow.transform);
            spacer.AddComponent<LayoutElement>().preferredWidth = 8f;

            var brushLbl = CreateUI("BLbl", layerRow.transform);
            brushLbl.AddComponent<LayoutElement>().preferredWidth = 34f;
            var bt = brushLbl.AddComponent<TextMeshProUGUI>();
            bt.text = "Size"; bt.fontSize = 11f; bt.alignment = TextAlignmentOptions.Left; bt.color = TEXT_SECONDARY;

            var minus = CreateUI("Minus", layerRow.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 22f;
            MakeBtn(minus, "-", () => onBrushSizeChanged?.Invoke(Mathf.Max(1, state.BrushSize - 1)), 12f);

            var val = CreateUI("Val", layerRow.transform);
            val.AddComponent<LayoutElement>().preferredWidth = 36f;
            refs.BrushSizeLabel = val.AddComponent<TextMeshProUGUI>();
            refs.BrushSizeLabel.text = $"{state.BrushSize}x{state.BrushSize}";
            refs.BrushSizeLabel.fontSize = 12f; refs.BrushSizeLabel.fontStyle = FontStyles.Bold;
            refs.BrushSizeLabel.alignment = TextAlignmentOptions.Center; refs.BrushSizeLabel.color = TEXT_PRIMARY;

            var plus = CreateUI("Plus", layerRow.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 22f;
            MakeBtn(plus, "+", () => onBrushSizeChanged?.Invoke(Mathf.Min(5, state.BrushSize + 1)), 12f);
        }

        private static void BuildSelectedTilePreview(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("SelectedPreview", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 56f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 48f;
            refs.SelectedTilePreviewImg = imgGo.AddComponent<Image>();
            refs.SelectedTilePreviewImg.color = SLOT_BG;
            refs.SelectedTilePreviewImg.preserveAspect = true;
            var outline = imgGo.AddComponent<Outline>();
            outline.effectColor = ACCENT; outline.effectDistance = new Vector2(1.5f, 1.5f);

            var infoGo = CreateUI("Info", row.transform);
            infoGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = infoGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2f; vl.childForceExpandHeight = false; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childControlWidth = true;

            var labelGo = CreateUI("Lbl", infoGo.transform);
            labelGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = "SELECTED"; labelTmp.fontSize = 9f; labelTmp.color = TEXT_MUTED;
            labelTmp.characterSpacing = 2f;

            var nameGo = CreateUI("Name", infoGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            refs.SelectedTileNameText = nameGo.AddComponent<TextMeshProUGUI>();
            refs.SelectedTileNameText.text = "(none)"; refs.SelectedTileNameText.fontSize = 13f;
            refs.SelectedTileNameText.alignment = TextAlignmentOptions.Left;
            refs.SelectedTileNameText.color = TEXT_PRIMARY;
            refs.SelectedTileNameText.enableWordWrapping = true;
        }

        private static void BuildCategoryScroll(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = 56f; le.minHeight = 32f;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            refs.CategoryTabsContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;

            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(130f, 24f);
            gl.spacing = new Vector2(3f, 3f);
            gl.padding = new RectOffset(3, 3, 3, 3);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = cr; sr.viewport = vp.GetComponent<RectTransform>();
        }

        private static void BuildTilePicker(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("TileScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f; le.minHeight = 140f;
            refs.TileScrollRect = scrollGo.AddComponent<ScrollRect>();
            refs.TileScrollRect.horizontal = false; refs.TileScrollRect.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            refs.TileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1); cr.sizeDelta = Vector2.zero;
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(52f, 52f);
            gl.spacing = new Vector2(4f, 4f);
            gl.padding = new RectOffset(4, 4, 4, 4);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 5;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            refs.TileScrollRect.content = cr;
            refs.TileScrollRect.viewport = vp.GetComponent<RectTransform>();
        }

        private static void BuildTileCountRow(Transform parent, ref UIRefs refs)
        {
            var go = CreateUI("TileCount", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.TileCountText = go.AddComponent<TextMeshProUGUI>();
            refs.TileCountText.text = ""; refs.TileCountText.fontSize = 10f;
            refs.TileCountText.alignment = TextAlignmentOptions.Right; refs.TileCountText.color = TEXT_MUTED;
        }

        private static void BuildStatusBar(Transform parent, ref UIRefs refs)
        {
            var go = CreateUI("Status", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            refs.StatusText = go.AddComponent<TextMeshProUGUI>();
            refs.StatusText.text = "F6 Toggle  |  B E F I S Tools  |  Scroll Layer  |  Ctrl+Z Undo";
            refs.StatusText.fontSize = 9f; refs.StatusText.alignment = TextAlignmentOptions.Center;
            refs.StatusText.color = TEXT_MUTED;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  RIGHT SIDEBAR
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    }
}
