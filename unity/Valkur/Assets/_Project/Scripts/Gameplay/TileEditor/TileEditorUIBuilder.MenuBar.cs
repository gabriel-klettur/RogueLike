using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    public static partial class TileEditorUIBuilder
    {
        /// <summary>
        /// Builds the slim menu bar across the top of the screen.
        /// Contains: brand title, dropdown menu buttons, layer navigation, brush size, status text.
        /// </summary>
        private static void BuildMenuBar(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged,
            System.Action<string> onDropdownToggle,
            System.Action onPerfToggle = null)
        {
            refs.MenuBar = CreateUI("MenuBar", canvasT);
            var r = refs.MenuBar.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, MENUBAR_HEIGHT);

            var bg = refs.MenuBar.AddComponent<Image>();
            bg.color = TileEditorTheme.MenuBarBg;
            bg.raycastTarget = true;
            var ol = refs.MenuBar.AddComponent<Outline>();
            ol.effectColor = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(0f, -TileEditorTheme.OutlinePx);

            // Theme tracker: lets the UX panel repaint the menu bar live.
            var chrome = refs.MenuBar.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var layout = refs.MenuBar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            layout.spacing = MENUBAR_SPACING;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var t = refs.MenuBar.transform;

            // ── Brand ──
            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_W;
            var brandTmp = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text = "TILE EDITOR";
            brandTmp.fontSize = 13f;
            brandTmp.fontStyle = FontStyles.Bold;
            brandTmp.alignment = TextAlignmentOptions.Left;
            brandTmp.color = ACCENT;
            brandTmp.characterSpacing = 3f;

            BuildMenuDivider(t);

            // ── Dropdown menu buttons ──
            refs.ToolsMenuBtnImg = BuildMenuButton(t, "Tools v", TOOLS_BTN_W,
                () => onDropdownToggle?.Invoke("tools"), out refs.ToolsMenuBtnTmp);
            refs.TilesMenuBtnImg = BuildMenuButton(t, "Tiles v", TILES_BTN_W,
                () => onDropdownToggle?.Invoke("tiles"), out refs.TilesMenuBtnTmp);
            refs.LayersMenuBtnImg = BuildMenuButton(t, "Layers v", LAYERS_BTN_W,
                () => onDropdownToggle?.Invoke("layers"), out refs.LayersMenuBtnTmp);
            refs.InspectorMenuBtnImg = BuildMenuButton(t, "Inspector v", INSPECTOR_BTN_W,
                () => onDropdownToggle?.Invoke("inspector"), out refs.InspectorMenuBtnTmp);
            refs.CollidersMenuBtnImg = BuildMenuButton(t, "Colliders v", COLLIDERS_BTN_W,
                () => onDropdownToggle?.Invoke("colliders"), out refs.CollidersMenuBtnTmp);
            refs.SizeMenuBtnImg = BuildMenuButton(t, "Size v", SIZE_BTN_W,
                () => onDropdownToggle?.Invoke("size"), out refs.SizeMenuBtnTmp);

            // ── Flexible spacer ──
            var spacer = CreateUI("Spacer", t);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            BuildMenuDivider(t);

            // ── UX/Theme editor (just left of PERF) ──
            refs.UxMenuBtnImg = BuildMenuButton(t, "UX", UX_BTN_W,
                () => onDropdownToggle?.Invoke("ux"), out refs.UxMenuBtnTmp);

            // ── Perf Probe toggle (far-right) ──
            refs.PerfProbeMenuBtnImg = BuildMenuButton(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }

        private static Image BuildMenuButton(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = CreateUI($"Menu_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = MENU_BTN_NORMAL;
            c.highlightedColor = MENU_BTN_HOVER;
            c.pressedColor = MENU_BTN_OPEN;
            c.selectedColor = MENU_BTN_NORMAL;
            c.fadeDuration = 0.08f;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            tmp = AddCenteredText(go.transform, label, 11f, FontStyles.Normal, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;

            return img;
        }

        private static void BuildMenuDivider(Transform parent)
        {
            var go = CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }

        private static void BuildLayerNav(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            var group = CreateUI("LayerNav", parent);
            group.AddComponent<LayoutElement>().preferredWidth = 140f;
            var h = group.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 2f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;

            var lbl = CreateUI("LLbl", group.transform);
            lbl.AddComponent<LayoutElement>().preferredWidth = 38f;
            var lt = lbl.AddComponent<TextMeshProUGUI>();
            lt.text = "Layer";
            lt.fontSize = 9f;
            lt.alignment = TextAlignmentOptions.Right;
            lt.color = TEXT_MUTED;

            var prev = CreateUI("Prev", group.transform);
            prev.AddComponent<LayoutElement>().preferredWidth = 20f;
            MakeBtn(prev, "<", () =>
            {
                int v = (int)state.CurrentLayer - 1;
                if (v < 0) v = 8;
                onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v);
            }, 9f);

            var val = CreateUI("LayerVal", group.transform);
            val.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.LayerLabel = val.AddComponent<TextMeshProUGUI>();
            refs.LayerLabel.text = state.CurrentLayer.ToString();
            refs.LayerLabel.fontSize = 11f;
            refs.LayerLabel.fontStyle = FontStyles.Bold;
            refs.LayerLabel.alignment = TextAlignmentOptions.Center;
            refs.LayerLabel.color = ACCENT;

            var next = CreateUI("Next", group.transform);
            next.AddComponent<LayoutElement>().preferredWidth = 20f;
            MakeBtn(next, ">", () =>
            {
                int v = (int)state.CurrentLayer + 1;
                if (v > 8) v = 0;
                onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v);
            }, 9f);
        }

        private static void BuildBrushSizeNav(Transform parent, TileEditorState state, ref UIRefs refs,
            System.Action<int> onBrushSizeChanged)
        {
            var group = CreateUI("BrushNav", parent);
            group.AddComponent<LayoutElement>().preferredWidth = 100f;
            var h = group.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 2f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;

            var lbl = CreateUI("BLbl", group.transform);
            lbl.AddComponent<LayoutElement>().preferredWidth = 28f;
            var bt = lbl.AddComponent<TextMeshProUGUI>();
            bt.text = "Size";
            bt.fontSize = 9f;
            bt.alignment = TextAlignmentOptions.Right;
            bt.color = TEXT_MUTED;

            var minus = CreateUI("Minus", group.transform);
            minus.AddComponent<LayoutElement>().preferredWidth = 18f;
            MakeBtn(minus, "-", () => onBrushSizeChanged?.Invoke(Mathf.Max(1, state.BrushSize - 1)), 10f);

            var val = CreateUI("Val", group.transform);
            val.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.BrushSizeLabel = val.AddComponent<TextMeshProUGUI>();
            refs.BrushSizeLabel.text = $"{state.BrushSize}x{state.BrushSize}";
            refs.BrushSizeLabel.fontSize = 11f;
            refs.BrushSizeLabel.fontStyle = FontStyles.Bold;
            refs.BrushSizeLabel.alignment = TextAlignmentOptions.Center;
            refs.BrushSizeLabel.color = TEXT_PRIMARY;

            var plus = CreateUI("Plus", group.transform);
            plus.AddComponent<LayoutElement>().preferredWidth = 18f;
            MakeBtn(plus, "+", () => onBrushSizeChanged?.Invoke(Mathf.Min(5, state.BrushSize + 1)), 10f);
        }
    }
}
