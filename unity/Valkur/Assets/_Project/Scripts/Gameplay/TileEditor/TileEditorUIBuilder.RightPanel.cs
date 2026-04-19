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
        //  LAYERS DROPDOWN
        // ═════════════════════════════════════════════════════════════════

        private static void BuildLayersDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            refs.LayersDropdown = MakeDropdownPanel("LayersDropdown", canvasT,
                PanelDock.BottomRight, LayersX, LayersY, LAYERS_DROP_W, LAYERS_DROP_H,
                "Layers", out var layersContent, out refs.LayersPanelDrag);

            var t = layersContent;

            var layers = System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));
            foreach (TilemapLayerSetup.TilemapLayer layer in layers)
                BuildLayerRow(t, layer, state, ref refs, onLayerChanged);

            refs.LayersDropdown.SetActive(false);
        }

        private static void BuildLayerRow(Transform parent, TilemapLayerSetup.TilemapLayer layer,
            TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            int idx = (int)layer;
            var row = CreateUI($"Layer_{layer}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(4, 4, 1, 1);

            var bg = row.AddComponent<Image>();
            bg.color = layer == state.CurrentLayer ? LAYER_ACTIVE_BG : Color.clear;
            refs.LayerRowBgs.Add(bg);

            var visGo = CreateUI("Vis", row.transform);
            visGo.AddComponent<LayoutElement>().preferredWidth = 16f;
            var visImg = visGo.AddComponent<Image>();
            visImg.color = VIS_ON;
            refs.LayerVisIcons.Add(visImg);
            var visBtn = visGo.AddComponent<Button>();
            visBtn.targetGraphic = visImg;

            var idxGo = CreateUI("Idx", row.transform);
            idxGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var idxTmp = idxGo.AddComponent<TextMeshProUGUI>();
            idxTmp.text = idx.ToString();
            idxTmp.fontSize = 11f;
            idxTmp.alignment = TextAlignmentOptions.Center;
            idxTmp.color = ACCENT_DIM;

            var nameGo = CreateUI("Name", row.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = layer.ToString();
            nameTmp.fontSize = 11f;
            nameTmp.alignment = TextAlignmentOptions.Left;
            nameTmp.color = layer == state.CurrentLayer ? TEXT_PRIMARY : TEXT_SECONDARY;
            refs.LayerRowLabels.Add(nameTmp);

            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = bg;
            var colors = rowBtn.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = BTN_HOVER;
            rowBtn.colors = colors;
            var capLayer = layer;
            rowBtn.onClick.AddListener(() => onLayerChanged?.Invoke(capLayer));
        }

        // ═════════════════════════════════════════════════════════════════
        //  INSPECTOR DROPDOWN
        // ═════════════════════════════════════════════════════════════════

        private static void BuildInspectorDropdown(Transform canvasT, TileEditorState state, ref UIRefs refs)
        {
            refs.InspectorDropdown = MakeDropdownPanel("InspectorDropdown", canvasT,
                PanelDock.TopRight, InspectorX, InspectorY, INSPECTOR_DROP_W, INSPECTOR_DROP_H,
                "Inspector", out var inspectorContent, out refs.InspectorPanelDrag);

            var t = inspectorContent;

            BuildViewRow(t, "Hovered", CYAN_ACCENT, out refs.ViewHoveredImg, out refs.ViewHoveredLabel);
            BuildViewRow(t, "Selected", GREEN_ACCENT, out refs.ViewSelectedImg, out refs.ViewSelectedLabel);
            BuildViewRow(t, "Brush", ACCENT, out refs.ViewChoiceImg, out refs.ViewChoiceLabel);
            BuildSeparator(t);

            var lhGo = CreateUI("LayerHov", t);
            lhGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lhTmp = lhGo.AddComponent<TextMeshProUGUI>();
            lhTmp.text = "Hover Layer";
            lhTmp.fontSize = 10f;
            lhTmp.color = TEXT_MUTED;

            var lhVal = CreateUI("LHVal", t);
            lhVal.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.ViewLayerHoveredText = lhVal.AddComponent<TextMeshProUGUI>();
            refs.ViewLayerHoveredText.text = "";
            refs.ViewLayerHoveredText.fontSize = 12f;
            refs.ViewLayerHoveredText.fontStyle = FontStyles.Bold;
            refs.ViewLayerHoveredText.color = ACCENT;

            var lsGo = CreateUI("LayerSel", t);
            lsGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var lsTmp = lsGo.AddComponent<TextMeshProUGUI>();
            lsTmp.text = "Active Layer";
            lsTmp.fontSize = 10f;
            lsTmp.color = TEXT_MUTED;

            var lsVal = CreateUI("LSVal", t);
            lsVal.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.ViewLayerSelectedText = lsVal.AddComponent<TextMeshProUGUI>();
            refs.ViewLayerSelectedText.text = $"  {(int)state.CurrentLayer}: {state.CurrentLayer}";
            refs.ViewLayerSelectedText.fontSize = 12f;
            refs.ViewLayerSelectedText.fontStyle = FontStyles.Bold;
            refs.ViewLayerSelectedText.color = ACCENT;

            refs.InspectorDropdown.SetActive(false);
        }

        private static void BuildViewRow(Transform parent, string label, Color accentColor,
            out Image tileImg, out TextMeshProUGUI nameText)
        {
            var row = CreateUI($"View_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 36f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.padding = new RectOffset(2, 2, 2, 2);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 32f;
            tileImg = imgGo.AddComponent<Image>();
            tileImg.color = SLOT_BG;
            tileImg.preserveAspect = true;
            var ol = imgGo.AddComponent<Outline>();
            ol.effectColor = accentColor;
            ol.effectDistance = new Vector2(1.5f, 1.5f);

            var txtGo = CreateUI("Txt", row.transform);
            txtGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = txtGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0;
            vl.childForceExpandHeight = true;
            vl.childControlHeight = true;

            var lblGo = CreateUI("Lbl", txtGo.transform);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 9f;
            lblTmp.color = accentColor;

            var valGo = CreateUI("Val", txtGo.transform);
            nameText = valGo.AddComponent<TextMeshProUGUI>();
            nameText.text = "";
            nameText.fontSize = 12f;
            nameText.color = TEXT_PRIMARY;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ═════════════════════════════════════════════════════════════════
        //  BOTTOM LAYER INDICATOR
        // ═════════════════════════════════════════════════════════════════

        private static void BuildLayerIndicator(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            refs.LayerIndicatorPanel = CreateUI("LayerIndicator", canvasT);
            var r = refs.LayerIndicatorPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0f);
            r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 12f);
            r.sizeDelta = new Vector2(280f, 30f);

            var bg = refs.LayerIndicatorPanel.AddComponent<Image>();
            bg.color = BG_PANEL;
            var ol = refs.LayerIndicatorPanel.AddComponent<Outline>();
            ol.effectColor = ACCENT_DIM;
            ol.effectDistance = new Vector2(1f, 1f);

            var h = refs.LayerIndicatorPanel.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 0f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.padding = new RectOffset(2, 2, 0, 0);

            var t = refs.LayerIndicatorPanel.transform;

            // ◀ prev layer
            var prevGo = CreateUI("PrevLayer", t);
            prevGo.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(prevGo, "<", () =>
            {
                int v = (int)state.CurrentLayer - 1;
                if (v < 0) v = 8;
                onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v);
            }, 10f);

            // Layer label
            var labelGo = CreateUI("LayerLbl", t);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.LayerIndicator = labelGo.AddComponent<TextMeshProUGUI>();
            refs.LayerIndicator.text = $"{(int)state.CurrentLayer}: {state.CurrentLayer}";
            refs.LayerIndicator.fontSize = 14f;
            refs.LayerIndicator.fontStyle = FontStyles.Bold;
            refs.LayerIndicator.alignment = TextAlignmentOptions.Center;
            refs.LayerIndicator.color = ACCENT;

            // ▶ next layer
            var nextGo = CreateUI("NextLayer", t);
            nextGo.AddComponent<LayoutElement>().preferredWidth = 28f;
            MakeBtn(nextGo, ">", () =>
            {
                int v = (int)state.CurrentLayer + 1;
                if (v > 8) v = 0;
                onLayerChanged?.Invoke((TilemapLayerSetup.TilemapLayer)v);
            }, 10f);
        }
    }
}
