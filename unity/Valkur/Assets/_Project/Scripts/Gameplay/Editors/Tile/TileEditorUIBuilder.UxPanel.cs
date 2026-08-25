using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Builds the UX/theme editor dropdown panel.
    /// Lets the user tweak every panel chrome color (panel bg, header bg, border,
    /// separator, header title, menu bar bg, accent text) and the outline thickness
    /// at runtime.  All edits are applied live via <see cref="TileEditorTheme.ApplyToAll"/>.
    /// </summary>
    public static partial class TileEditorUIBuilder
    {
        // Stack from the right edge: PERF (8) | UX (8) | …  →  UX panel docks under the UX btn
        private static float UxX => PANEL_GAP + PERF_BTN_W + PANEL_GAP + UX_BTN_W + PANEL_GAP;
        private static float UxY => PANEL_TOP_OFFSET;

        private static void BuildUxDropdown(Transform canvasT, ref UIRefs refs)
        {
            refs.UxDropdown = EditorUIHelpers.MakeDropPanel("UxDropdown", canvasT,
                PanelDock.TopRight, UxX, UxY, UX_DROP_W, UX_DROP_H,
                "UI / UX", out var uxContent, out refs.UxPanelDrag);

            var t = uxContent;

            BuildSectionLabel(t, "PANEL");
            BuildColorEditor(t, "Background",   () => TileEditorTheme.PanelBg,     v => TileEditorTheme.PanelBg     = v);
            BuildColorEditor(t, "Header BG",    () => TileEditorTheme.HeaderBg,    v => TileEditorTheme.HeaderBg    = v);
            BuildColorEditor(t, "Header Title", () => TileEditorTheme.HeaderTitle, v => TileEditorTheme.HeaderTitle = v);

            BuildSeparator(t);
            BuildSectionLabel(t, "BORDER");
            BuildColorEditor(t, "Color",     () => TileEditorTheme.Border,    v => TileEditorTheme.Border    = v);
            BuildColorEditor(t, "Separator", () => TileEditorTheme.Separator, v => TileEditorTheme.Separator = v);
            BuildSliderRow  (t, "Width (px)", 0f, 4f, () => TileEditorTheme.OutlinePx, v => TileEditorTheme.OutlinePx = v);

            BuildSeparator(t);
            BuildSectionLabel(t, "MENU BAR");
            BuildColorEditor(t, "Background", () => TileEditorTheme.MenuBarBg, v => TileEditorTheme.MenuBarBg = v);

            BuildSeparator(t);

            // Reset button
            var resetGo = CreateUI("ResetBtn", t);
            resetGo.AddComponent<LayoutElement>().preferredHeight = 28f;
            MakeBtn(resetGo, "Reset to Defaults",
                () => TileEditorTheme.ResetToDefaults(), 11f);

            refs.UxDropdown.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Slider rows
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Compact row: [label 80px] [slider flex] [value 36px].  Edits a float on
        /// <see cref="TileEditorTheme"/> in real time and pushes the change to all panels.
        /// </summary>
        private static void BuildSliderRow(Transform parent, string label,
            float minVal, float maxVal, Func<float> getter, Action<float> setter,
            string format = "F2")
        {
            var row = CreateUI("SliderRow_" + label, parent);
            row.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            // Label
            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 80f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label;
            lblTmp.fontSize = 10f;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.color = TEXT_SECONDARY;

            // Slider
            var sliderGo = CreateUI("Slider", row.transform);
            sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var slider = BuildSlider(sliderGo, minVal, maxVal, getter());

            // Value text
            var valGo = CreateUI("Val", row.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 36f;
            var valTmp = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.fontSize = 10f;
            valTmp.alignment = TextAlignmentOptions.MidlineRight;
            valTmp.color = TEXT_PRIMARY;
            valTmp.text = getter().ToString(format);

            slider.onValueChanged.AddListener(v =>
            {
                setter(v);
                valTmp.text = v.ToString(format);
                TileEditorTheme.ApplyToAll();
            });
        }

        /// <summary>
        /// Color editor block: 4 RGBA slider rows + a small color preview swatch on the right.
        /// Compact (≈ 96 px tall total).
        /// </summary>
        private static void BuildColorEditor(Transform parent, string label,
            Func<Color> getter, Action<Color> setter)
        {
            // Wrapper holds the title row + the 2x2 grid of channel sliders side-by-side
            // with a swatch.  Total height ≈ 14 (title) + 4*18 = 86.
            var box = CreateUI("ColorBox_" + label, parent);
            box.AddComponent<LayoutElement>().preferredHeight = 14f + 4f * 18f;
            var v = box.AddComponent<VerticalLayoutGroup>();
            v.spacing = 1f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.padding = new RectOffset(4, 4, 2, 2);

            // ── Header row: label on the left, color swatch on the right ──
            var hdrGo = CreateUI("Hdr", box.transform);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 14f;
            var hdrH = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrH.spacing = 4f;
            hdrH.childForceExpandWidth = false;
            hdrH.childForceExpandHeight = true;
            hdrH.childControlWidth = true;
            hdrH.childControlHeight = true;
            hdrH.childAlignment = TextAnchor.MiddleLeft;

            var lblGo = CreateUI("Lbl", hdrGo.transform);
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text = label.ToUpper();
            lblTmp.fontSize = 9f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.color = TEXT_SECONDARY;
            lblTmp.characterSpacing = 2f;

            // Swatch (small color preview)
            var swatchGo = CreateUI("Swatch", hdrGo.transform);
            swatchGo.AddComponent<LayoutElement>().preferredWidth = 22f;
            var swatchImg = swatchGo.AddComponent<Image>();
            swatchImg.color = getter();
            var swatchOl = swatchGo.AddComponent<Outline>();
            swatchOl.effectColor = new Color(0f, 0f, 0f, 0.6f);
            swatchOl.effectDistance = new Vector2(1f, 1f);

            // ── Per-channel slider rows (R, G, B, A) ──
            BuildChannelRow(box.transform, "R", () => getter().r, x => { var c = getter(); c.r = x; setter(c); swatchImg.color = c; });
            BuildChannelRow(box.transform, "G", () => getter().g, x => { var c = getter(); c.g = x; setter(c); swatchImg.color = c; });
            BuildChannelRow(box.transform, "B", () => getter().b, x => { var c = getter(); c.b = x; setter(c); swatchImg.color = c; });
            BuildChannelRow(box.transform, "A", () => getter().a, x => { var c = getter(); c.a = x; setter(c); swatchImg.color = c; });
        }

        /// <summary>One R/G/B/A row inside a color editor.</summary>
        private static void BuildChannelRow(Transform parent, string ch,
            Func<float> getter, Action<float> setter)
        {
            var row = CreateUI("Ch_" + ch, parent);
            row.AddComponent<LayoutElement>().preferredHeight = 16f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            // Channel letter
            var letterGo = CreateUI("L", row.transform);
            letterGo.AddComponent<LayoutElement>().preferredWidth = 12f;
            var letterTmp = letterGo.AddComponent<TextMeshProUGUI>();
            letterTmp.text = ch;
            letterTmp.fontSize = 10f;
            letterTmp.fontStyle = FontStyles.Bold;
            letterTmp.alignment = TextAlignmentOptions.Midline;
            letterTmp.color = ChannelTint(ch);

            // Slider
            var sliderGo = CreateUI("S", row.transform);
            sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var slider = BuildSlider(sliderGo, 0f, 1f, getter());

            // Value text
            var valGo = CreateUI("V", row.transform);
            valGo.AddComponent<LayoutElement>().preferredWidth = 32f;
            var valTmp = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.fontSize = 9f;
            valTmp.alignment = TextAlignmentOptions.MidlineRight;
            valTmp.color = TEXT_PRIMARY;
            valTmp.text = getter().ToString("F2");

            slider.onValueChanged.AddListener(v =>
            {
                setter(v);
                valTmp.text = v.ToString("F2");
                TileEditorTheme.ApplyToAll();
            });
        }

        private static Color ChannelTint(string ch) => ch switch
        {
            "R" => new Color(1f, 0.45f, 0.45f, 1f),
            "G" => new Color(0.45f, 1f, 0.55f, 1f),
            "B" => new Color(0.55f, 0.65f, 1f, 1f),
            _   => TEXT_SECONDARY,
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Generic compact slider
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Builds a minimal Unity UI Slider (background bar + filled portion + handle)
        /// inside <paramref name="container"/>.  Fills its rect.
        /// </summary>
        private static Slider BuildSlider(GameObject container, float minVal, float maxVal, float initialVal)
        {
            // Background
            var bgGo = CreateUI("Bg", container.transform);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.35f);
            bgRt.anchorMax = new Vector2(1f, 0.65f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.20f, 0.20f, 0.26f, 1f);

            // Fill area (full stretch)
            var fillAreaGo = CreateUI("FillArea", container.transform);
            var faRt = fillAreaGo.GetComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.35f);
            faRt.anchorMax = new Vector2(1f, 0.65f);
            faRt.offsetMin = new Vector2(2f, 0f);
            faRt.offsetMax = new Vector2(-2f, 0f);

            var fillGo = CreateUI("Fill", fillAreaGo.transform);
            var fRt = fillGo.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero;
            fRt.anchorMax = new Vector2(0f, 1f);
            fRt.offsetMin = Vector2.zero;
            fRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = ACCENT;

            // Handle area (full stretch)
            var handleAreaGo = CreateUI("HandleArea", container.transform);
            var haRt = handleAreaGo.GetComponent<RectTransform>();
            haRt.anchorMin = new Vector2(0f, 0f);
            haRt.anchorMax = new Vector2(1f, 1f);
            haRt.offsetMin = new Vector2(4f, 0f);
            haRt.offsetMax = new Vector2(-4f, 0f);

            var handleGo = CreateUI("Handle", handleAreaGo.transform);
            var hRt = handleGo.GetComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(10f, 0f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = TEXT_PRIMARY;

            // Slider component (must be added LAST, on the container)
            var slider = container.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.fillRect      = fRt;
            slider.handleRect    = hRt;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.minValue      = minVal;
            slider.maxValue      = maxVal;
            slider.wholeNumbers  = false;
            slider.value         = Mathf.Clamp(initialVal, minVal, maxVal);

            return slider;
        }
    }
}
