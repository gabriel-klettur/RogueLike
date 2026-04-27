using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Enemies.FSM
{
    public static partial class FSMEditorUIBuilder
    {
        // ── Graph Panel (centre, NOT a draggable floating dropdown) ───────────────
        // Mirrors Python fsm_graph_panel: nodal canvas + horizontal toolbar at top
        // (select / connect / delete / zoom_in / zoom_out / mark_ini / mark_end)
        // and a colour legend / status row beneath.

        private const float GRAPH_TOOLBAR_H = 30f;

        private static void BuildGraphPanel(Transform canvasT, ref UIRefs refs,
            Action onSelect, Action onConnect, Action onDelete,
            Action onZoomIn, Action onZoomOut,
            Action onMarkIni, Action onMarkEnd,
            Action onAddNode = null, Action onCloneNode = null, Action onDisconnect = null)
        {
            var go = new GameObject("FSMGraphPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvasT, false);
            refs.GraphPanel = go;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(GRAPH_LEFT_INSET,  GRAPH_BOTTOM_INSET);
            rt.offsetMax = new Vector2(-GRAPH_RIGHT_INSET, -GRAPH_TOP_INSET);
            refs.GraphArea = rt;

            var bg          = go.GetComponent<Image>();
            bg.color        = TileEditorTheme.PanelBg;
            var ol          = go.AddComponent<Outline>();
            ol.effectColor  = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Live-repaint chrome (no header — graph is always visible).
            var chrome           = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage  = bg;
            chrome.PanelOutline  = ol;

            // ── Toolbar (top horizontal strip) ──
            var toolbarGo = CreateUI("GraphToolbar", go.transform);
            var trt       = toolbarGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot     = new Vector2(0.5f, 1f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(0f, GRAPH_TOOLBAR_H);

            var tbBg          = toolbarGo.AddComponent<Image>();
            tbBg.color        = TileEditorTheme.HeaderBg;
            tbBg.raycastTarget = true;

            var tbHlg = toolbarGo.AddComponent<HorizontalLayoutGroup>();
            tbHlg.padding             = new RectOffset(6, 6, 3, 3);
            tbHlg.spacing             = 4f;
            tbHlg.childForceExpandWidth  = false;
            tbHlg.childForceExpandHeight = true;
            tbHlg.childControlWidth      = true;
            tbHlg.childControlHeight     = true;
            tbHlg.childAlignment         = TextAnchor.MiddleLeft;

            refs.SelectToolImg     = AddGraphToolBtn(toolbarGo.transform, "Select",   onSelect);
            refs.AddNodeToolImg    = AddGraphToolBtn(toolbarGo.transform, "Add",      onAddNode);
            refs.CloneNodeToolImg  = AddGraphToolBtn(toolbarGo.transform, "Clone",    onCloneNode);
            refs.ConnectToolImg    = AddGraphToolBtn(toolbarGo.transform, "Connect",  onConnect);
            refs.DisconnectToolImg = AddGraphToolBtn(toolbarGo.transform, "Disc.",    onDisconnect);
            refs.DeleteToolImg     = AddGraphToolBtn(toolbarGo.transform, "Delete",   onDelete);
            AddGraphSeparator(toolbarGo.transform);
            AddGraphToolBtn(toolbarGo.transform, "Zoom +", onZoomIn);
            AddGraphToolBtn(toolbarGo.transform, "Zoom -", onZoomOut);

            var zoomGo = CreateUI("ZoomLbl", toolbarGo.transform);
            zoomGo.AddComponent<LayoutElement>().preferredWidth = 60f;
            refs.GraphZoomLabel           = zoomGo.AddComponent<TextMeshProUGUI>();
            refs.GraphZoomLabel.text      = "100%";
            refs.GraphZoomLabel.fontSize  = 10f;
            refs.GraphZoomLabel.color     = TEXT_SECONDARY;
            refs.GraphZoomLabel.alignment = TextAlignmentOptions.Center;

            AddGraphSeparator(toolbarGo.transform);
            refs.MarkIniToolImg = AddGraphToolBtn(toolbarGo.transform, "Mark Ini", onMarkIni);
            refs.MarkEndToolImg = AddGraphToolBtn(toolbarGo.transform, "Mark End", onMarkEnd);

            // Flexible spacer + legend chip
            CreateUI("Spacer", toolbarGo.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;
            AddLegendChip(toolbarGo.transform, "Initial", new Color(0.20f, 0.50f, 0.20f, 0.95f));
            AddLegendChip(toolbarGo.transform, "Terminal", new Color(0.55f, 0.15f, 0.15f, 0.95f));
            AddLegendChip(toolbarGo.transform, "Selected", ACCENT);

            // ── Canvas content (scrollable / pannable area) ──
            var canvasArea = CreateUI("GraphCanvas", go.transform);
            var crt        = canvasArea.GetComponent<RectTransform>();
            crt.anchorMin  = new Vector2(0f, 0f);
            crt.anchorMax  = new Vector2(1f, 1f);
            crt.offsetMin  = Vector2.zero;
            crt.offsetMax  = new Vector2(0f, -GRAPH_TOOLBAR_H);

            // Mask so panned/zoomed nodes don't bleed outside the panel.
            canvasArea.AddComponent<RectMask2D>();

            var contentGo = CreateUI("GraphContent", canvasArea.transform);
            refs.GraphContent = contentGo.GetComponent<RectTransform>();
            refs.GraphContent.anchorMin = Vector2.zero;
            refs.GraphContent.anchorMax = Vector2.one;
            refs.GraphContent.offsetMin = Vector2.zero;
            refs.GraphContent.offsetMax = Vector2.zero;
            refs.GraphContent.pivot     = new Vector2(0.5f, 0.5f);

            var infoGo = CreateUI("GraphInfo", contentGo.transform);
            refs.GraphInfoText           = infoGo.AddComponent<TextMeshProUGUI>();
            refs.GraphInfoText.text      = "Select an FSM Set to view graph.";
            refs.GraphInfoText.fontSize  = 12f;
            refs.GraphInfoText.alignment = TextAlignmentOptions.Center;
            refs.GraphInfoText.color     = TEXT_SECONDARY;
            var irt = refs.GraphInfoText.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.3f, 0.45f);
            irt.anchorMax = new Vector2(0.7f, 0.55f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
        }

        private static Image AddGraphToolBtn(Transform parent, string label, Action onClick)
        {
            var go = CreateUI($"GTool_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 60f;

            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp       = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        private static void AddGraphSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }

        private static void AddLegendChip(Transform parent, string label, Color color)
        {
            var row = CreateUI($"Legend_{label}", parent);
            row.AddComponent<LayoutElement>().preferredWidth = 80f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 4f;
            hlg.padding             = new RectOffset(2, 2, 4, 4);
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var swatch = CreateUI("Swatch", row.transform);
            swatch.AddComponent<LayoutElement>().preferredWidth = 12f;
            swatch.AddComponent<Image>().color = color;

            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var tmp       = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 9f;
            tmp.color     = TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }
}
