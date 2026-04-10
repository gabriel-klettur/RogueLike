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
        // ═════════════════════════════════════════════════════════════════
        //  Dropdown X positions (cumulative from menu bar layout)
        // ═════════════════════════════════════════════════════════════════

        private static float DropdownX_Tools =>
            MENUBAR_PAD_H + TITLE_W + MENUBAR_SPACING + 1f + MENUBAR_SPACING;
        private static float DropdownX_Tiles =>
            DropdownX_Tools + TOOLS_BTN_W + MENUBAR_SPACING;
        private static float DropdownX_Layers =>
            DropdownX_Tiles + TILES_BTN_W + MENUBAR_SPACING;
        private static float DropdownX_Inspector =>
            DropdownX_Layers + LAYERS_BTN_W + MENUBAR_SPACING;

        // ═════════════════════════════════════════════════════════════════
        //  TOOLS DROPDOWN
        // ═════════════════════════════════════════════════════════════════

        private static void BuildToolsDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TileEditorState.Tool> onToolChanged,
            System.Action<int> onBrushSizeChanged)
        {
            refs.ToolsDropdown = MakeDropdownPanel("ToolsDropdown", canvasT,
                DropdownX_Tools, TOOLS_DROP_W, TOOLS_DROP_H);

            var t = refs.ToolsDropdown.transform;

            BuildSectionLabel(t, "TOOLS");

            // Tool buttons row
            var toolRow = CreateUI("ToolRow", t);
            toolRow.AddComponent<LayoutElement>().preferredHeight = 44f;
            var h = toolRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            CreateToolBtn(toolRow.transform, "Brush", "B", TileEditorState.Tool.Brush, state, ref refs, onToolChanged);
            CreateToolBtn(toolRow.transform, "Erase", "E", TileEditorState.Tool.Eraser, state, ref refs, onToolChanged);
            CreateToolBtn(toolRow.transform, "Fill", "F", TileEditorState.Tool.Fill, state, ref refs, onToolChanged);
            CreateToolBtn(toolRow.transform, "Pick", "I", TileEditorState.Tool.Eyedropper, state, ref refs, onToolChanged);
            CreateToolBtn(toolRow.transform, "Select", "S", TileEditorState.Tool.Select, state, ref refs, onToolChanged);

            BuildSeparator(t);

            // Shortcuts help
            var help = CreateUI("Help", t);
            help.AddComponent<LayoutElement>().preferredHeight = 14f;
            var helpTmp = help.AddComponent<TextMeshProUGUI>();
            helpTmp.text = "Scroll=Layer  |  Ctrl+Z=Undo  |  B E F I S";
            helpTmp.fontSize = 9f;
            helpTmp.alignment = TextAlignmentOptions.Center;
            helpTmp.color = TEXT_MUTED;

            refs.ToolsDropdown.SetActive(false);
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
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            var cap = tool;
            btn.onClick.AddListener(() => onToolChanged?.Invoke(cap));

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.spacing = -2f;

            var lblGo = CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 11f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.color = active ? ACCENT : TEXT_SECONDARY;

            var keyGo = CreateUI("Key", go.transform);
            keyGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var keyTmp = keyGo.AddComponent<TextMeshProUGUI>();
            keyTmp.text = shortcut;
            keyTmp.fontSize = 9f;
            keyTmp.alignment = TextAlignmentOptions.Center;
            keyTmp.color = TEXT_MUTED;

            refs.ToolButtonImages[tool] = img;
            refs.ToolButtonTexts[tool] = lblTmp;
        }

        // ═════════════════════════════════════════════════════════════════
        //  TILES DROPDOWN (categories + tile grid + selected preview)
        // ═════════════════════════════════════════════════════════════════

        private static void BuildTilesDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.TilesDropdown = MakeDropdownPanel("TilesDropdown", canvasT,
                DropdownX_Tiles, TILES_DROP_W, TILES_DROP_H);

            var t = refs.TilesDropdown.transform;

            // Selected tile preview row
            BuildSelectedTilePreview(t, ref refs);
            BuildSeparator(t);

            // Categories
            BuildSectionLabel(t, "CATEGORIES");
            BuildCategoryScroll(t, ref refs);
            BuildSeparator(t);

            // Tile grid
            BuildSectionLabel(t, "TILES");
            BuildTilePicker(t, ref refs);
            BuildTileCountRow(t, ref refs);

            refs.TilesDropdown.SetActive(false);
        }

        private static void BuildSelectedTilePreview(Transform parent, ref UIRefs refs)
        {
            var row = CreateUI("SelectedPreview", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 48f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 4, 4);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 40f;
            refs.SelectedTilePreviewImg = imgGo.AddComponent<Image>();
            refs.SelectedTilePreviewImg.color = SLOT_BG;
            refs.SelectedTilePreviewImg.preserveAspect = true;
            var outline = imgGo.AddComponent<Outline>();
            outline.effectColor = ACCENT;
            outline.effectDistance = new Vector2(1.5f, 1.5f);

            var infoGo = CreateUI("Info", row.transform);
            infoGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = infoGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 1f;
            vl.childForceExpandHeight = false;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childControlWidth = true;

            var labelGo = CreateUI("Lbl", infoGo.transform);
            labelGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = "SELECTED";
            labelTmp.fontSize = 8f;
            labelTmp.color = TEXT_MUTED;
            labelTmp.characterSpacing = 2f;

            var nameGo = CreateUI("Name", infoGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            refs.SelectedTileNameText = nameGo.AddComponent<TextMeshProUGUI>();
            refs.SelectedTileNameText.text = "(none)";
            refs.SelectedTileNameText.fontSize = 12f;
            refs.SelectedTileNameText.alignment = TextAlignmentOptions.Left;
            refs.SelectedTileNameText.color = TEXT_PRIMARY;
            refs.SelectedTileNameText.enableWordWrapping = true;
        }

        private static void BuildCategoryScroll(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("CatScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.minHeight = 28f;
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            refs.CategoryTabsContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1);
            cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1);
            cr.sizeDelta = Vector2.zero;

            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(140f, 22f);
            gl.spacing = new Vector2(3f, 2f);
            gl.padding = new RectOffset(3, 3, 2, 2);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 2;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = cr;
            sr.viewport = vp.GetComponent<RectTransform>();
        }

        private static void BuildTilePicker(Transform parent, ref UIRefs refs)
        {
            var scrollGo = CreateUI("TileScroll", parent);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 120f;
            refs.TileScrollRect = scrollGo.AddComponent<ScrollRect>();
            refs.TileScrollRect.horizontal = false;
            refs.TileScrollRect.vertical = true;

            var vp = CreateUI("VP", scrollGo.transform);
            StretchFill(vp);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            vp.AddComponent<Image>().color = BG_SURFACE;

            var content = CreateUI("Content", vp.transform);
            refs.TileGridContent = content.transform;
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1);
            cr.anchorMax = new Vector2(1, 1);
            cr.pivot = new Vector2(0, 1);
            cr.sizeDelta = Vector2.zero;
            var gl = content.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(50f, 50f);
            gl.spacing = new Vector2(3f, 3f);
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
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            refs.TileCountText = go.AddComponent<TextMeshProUGUI>();
            refs.TileCountText.text = "";
            refs.TileCountText.fontSize = 9f;
            refs.TileCountText.alignment = TextAlignmentOptions.Right;
            refs.TileCountText.color = TEXT_MUTED;
        }

        // ═════════════════════════════════════════════════════════════════
        //  SHARED: Dropdown panel factory
        // ═════════════════════════════════════════════════════════════════

        private static GameObject MakeDropdownPanel(string name, Transform canvasT,
            float xPos, float width, float height)
        {
            var go = CreateUI(name, canvasT);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(xPos, -MENUBAR_HEIGHT);
            r.sizeDelta = new Vector2(width, height);

            var img = go.AddComponent<Image>();
            img.color = DROPDOWN_BG;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = DROPDOWN_BORDER;
            ol.effectDistance = new Vector2(1f, 1f);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            go.AddComponent<CanvasGroup>();

            return go;
        }
    }
}
