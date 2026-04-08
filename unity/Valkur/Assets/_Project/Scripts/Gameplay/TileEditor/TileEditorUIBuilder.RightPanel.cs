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
        private static void BuildRightSidebar(Transform canvasT, TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            float sideColumnX = PANEL_PAD + LEFT_WIDTH + 12f;

            // View panel (top-right)
            refs.ViewPanel = MakePanel("ViewPanel", canvasT,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(sideColumnX, -8f), new Vector2(RIGHT_WIDTH, 240f));

            var vLayout = refs.ViewPanel.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(10, 10, 8, 8);
            vLayout.spacing = 4f;
            vLayout.childForceExpandWidth = true; vLayout.childForceExpandHeight = false;
            vLayout.childControlWidth = true; vLayout.childControlHeight = true;

            BuildSectionLabel(refs.ViewPanel.transform, "INSPECTOR");
            BuildViewRow(refs.ViewPanel.transform, "Hovered", CYAN_ACCENT, out refs.ViewHoveredImg, out refs.ViewHoveredLabel);
            BuildViewRow(refs.ViewPanel.transform, "Selected", GREEN_ACCENT, out refs.ViewSelectedImg, out refs.ViewSelectedLabel);
            BuildViewRow(refs.ViewPanel.transform, "Brush", ACCENT, out refs.ViewChoiceImg, out refs.ViewChoiceLabel);
            BuildSeparator(refs.ViewPanel.transform);

            var lhGo = CreateUI("LayerHov", refs.ViewPanel.transform);
            lhGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lhTmp = lhGo.AddComponent<TextMeshProUGUI>();
            lhTmp.text = "Hover Layer"; lhTmp.fontSize = 10f; lhTmp.color = TEXT_MUTED;
            var lhVal = CreateUI("LHVal", refs.ViewPanel.transform);
            lhVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            refs.ViewLayerHoveredText = lhVal.AddComponent<TextMeshProUGUI>();
            refs.ViewLayerHoveredText.text = ""; refs.ViewLayerHoveredText.fontSize = 12f;
            refs.ViewLayerHoveredText.fontStyle = FontStyles.Bold; refs.ViewLayerHoveredText.color = ACCENT;

            var lsGo = CreateUI("LayerSel", refs.ViewPanel.transform);
            lsGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            var lsTmp = lsGo.AddComponent<TextMeshProUGUI>();
            lsTmp.text = "Active Layer"; lsTmp.fontSize = 10f; lsTmp.color = TEXT_MUTED;
            var lsVal = CreateUI("LSVal", refs.ViewPanel.transform);
            lsVal.AddComponent<LayoutElement>().preferredHeight = 18f;
            refs.ViewLayerSelectedText = lsVal.AddComponent<TextMeshProUGUI>();
            refs.ViewLayerSelectedText.text = $"  {(int)state.CurrentLayer}: {state.CurrentLayer}";
            refs.ViewLayerSelectedText.fontSize = 12f;
            refs.ViewLayerSelectedText.fontStyle = FontStyles.Bold; refs.ViewLayerSelectedText.color = ACCENT;

            // Layers panel (below view panel)
            refs.LayersPanel = MakePanel("LayersPanel", canvasT,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(sideColumnX, -256f), new Vector2(RIGHT_WIDTH, 552f));

            var lLayout = refs.LayersPanel.AddComponent<VerticalLayoutGroup>();
            lLayout.padding = new RectOffset(8, 8, 6, 6);
            lLayout.spacing = 2f;
            lLayout.childForceExpandWidth = true; lLayout.childForceExpandHeight = false;
            lLayout.childControlWidth = true; lLayout.childControlHeight = true;

            BuildSectionLabel(refs.LayersPanel.transform, "LAYERS");

            var layers = System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));
            foreach (TilemapLayerSetup.TilemapLayer layer in layers)
                BuildLayerRow(refs.LayersPanel.transform, layer, state, ref refs, onLayerChanged);
        }

        private static void BuildViewRow(Transform parent, string label, Color accentColor,
            out Image tileImg, out TextMeshProUGUI nameText)
        {
            var row = CreateUI($"View_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 38f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
            h.padding = new RectOffset(2, 2, 2, 2);

            var imgGo = CreateUI("Img", row.transform);
            imgGo.AddComponent<LayoutElement>().preferredWidth = 34f;
            tileImg = imgGo.AddComponent<Image>();
            tileImg.color = SLOT_BG; tileImg.preserveAspect = true;
            var ol = imgGo.AddComponent<Outline>();
            ol.effectColor = accentColor; ol.effectDistance = new Vector2(1.5f, 1.5f);

            var txtGo = CreateUI("Txt", row.transform);
            txtGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var vl = txtGo.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 0; vl.childForceExpandHeight = true; vl.childControlHeight = true;

            var lblGo = CreateUI("Lbl", txtGo.transform);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label; lblTmp.fontSize = 9f; lblTmp.color = accentColor;

            var valGo = CreateUI("Val", txtGo.transform);
            nameText = valGo.AddComponent<TextMeshProUGUI>();
            nameText.text = ""; nameText.fontSize = 12f; nameText.color = TEXT_PRIMARY;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void BuildLayerRow(Transform parent, TilemapLayerSetup.TilemapLayer layer,
            TileEditorState state, ref UIRefs refs,
            System.Action<TilemapLayerSetup.TilemapLayer> onLayerChanged)
        {
            int idx = (int)layer;
            var row = CreateUI($"Layer_{layer}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;
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
            idxTmp.text = idx.ToString(); idxTmp.fontSize = 11f;
            idxTmp.alignment = TextAlignmentOptions.Center; idxTmp.color = ACCENT_DIM;

            var nameGo = CreateUI("Name", row.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = layer.ToString(); nameTmp.fontSize = 11f;
            nameTmp.alignment = TextAlignmentOptions.Left;
            nameTmp.color = layer == state.CurrentLayer ? TEXT_PRIMARY : TEXT_SECONDARY;
            refs.LayerRowLabels.Add(nameTmp);

            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = bg;
            var colors = rowBtn.colors;
            colors.normalColor = Color.clear; colors.highlightedColor = BTN_HOVER;
            rowBtn.colors = colors;
            var capLayer = layer;
            rowBtn.onClick.AddListener(() => onLayerChanged?.Invoke(capLayer));
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        //  BOTTOM LAYER INDICATOR
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private static void BuildLayerIndicator(Transform canvasT, TileEditorState state, ref UIRefs refs)
        {
            refs.LayerIndicatorPanel = CreateUI("LayerIndicator", canvasT);
            var r = refs.LayerIndicatorPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0f); r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 12f);
            r.sizeDelta = new Vector2(240f, 34f);
            var bg = refs.LayerIndicatorPanel.AddComponent<Image>();
            bg.color = BG_PANEL;
            var ol = refs.LayerIndicatorPanel.AddComponent<Outline>();
            ol.effectColor = ACCENT_DIM; ol.effectDistance = new Vector2(1f, 1f);

            refs.LayerIndicator = AddCenteredText(refs.LayerIndicatorPanel.transform,
                $"{(int)state.CurrentLayer}: {state.CurrentLayer}", 16f, FontStyles.Bold, ACCENT);
        }
    }
}
