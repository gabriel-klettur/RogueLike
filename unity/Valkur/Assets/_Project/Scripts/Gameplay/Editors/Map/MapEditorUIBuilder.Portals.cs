using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Portal-placement UI: a single Actions-panel button that arms the
    /// placement flow plus a modal dialog that confirms the destination
    /// after the user has clicked the source on the map.
    ///
    /// Mirrors the AddZone dialog architecture so the UX reads identically:
    /// arm via toolbar → click world → fill modal → confirm.
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        public partial struct UIRefs
        {
            // Place Portal button (lives on the Actions panel).
            public Image   PlacePortalBtnImage;
            public Outline PlacePortalBtnOutline;

            // Place Portal dialog.
            public GameObject       PlacePortalDialog;
            public TextMeshProUGUI  PlacePortalSourceText;
            public TMP_Dropdown     PlacePortalDestDropdown;
            public Toggle           PlacePortalUseCenterToggle;
            public TMP_InputField   PlacePortalDestXInput;
            public TMP_InputField   PlacePortalDestYInput;
            public TMP_InputField   PlacePortalRadiusInput;
            public Button           PlacePortalConfirmBtn;
            public Button           PlacePortalCancelBtn;
        }

        /// <summary>
        /// Callback bundle for the portal-placement UI. Kept as a struct so
        /// adding a new callback (e.g. for Phase 2 destination preview) does
        /// not perturb every call site of <see cref="BuildAll"/>.
        /// </summary>
        public struct PortalCallbacks
        {
            public Action OnBeginPlace;                                    // toolbar button
            public Action OnCancelPlace;                                   // dialog Cancel
            // (destination zone, useCenter, destWorldXY, activationRadius)
            public Action<string, bool, Vector2, float> OnConfirmPlace;
        }

        // ── Builders ────────────────────────────────────────────────────────────

        private const float PORTAL_DLG_W = 380f;
        private const float PORTAL_DLG_H = 320f;

        private static void BuildPlacePortalDialog(Transform canvasT, ref UIRefs refs,
            PortalCallbacks callbacks)
        {
            // Modal backdrop.
            var dlg                  = CreateUI("PlacePortalDialog", canvasT);
            var dlgRT                = dlg.GetComponent<RectTransform>();
            dlgRT.anchorMin          = Vector2.zero;
            dlgRT.anchorMax          = Vector2.one;
            dlgRT.offsetMin          = Vector2.zero;
            dlgRT.offsetMax          = Vector2.zero;
            dlg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            dlg.SetActive(false);
            refs.PlacePortalDialog   = dlg;

            // Centered card.
            var card                 = CreateUI("Card", dlg.transform);
            var cardRT               = card.GetComponent<RectTransform>();
            cardRT.anchorMin         = new Vector2(0.5f, 0.5f);
            cardRT.anchorMax         = new Vector2(0.5f, 0.5f);
            cardRT.pivot             = new Vector2(0.5f, 0.5f);
            cardRT.anchoredPosition  = Vector2.zero;
            cardRT.sizeDelta         = new Vector2(PORTAL_DLG_W, PORTAL_DLG_H);

            var cardBg               = card.AddComponent<Image>();
            cardBg.color             = BG_PANEL;
            var cardOl               = card.AddComponent<Outline>();
            cardOl.effectColor       = BORDER;
            cardOl.effectDistance    = new Vector2(1f, -1f);

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding              = new RectOffset(16, 16, 14, 14);
            vlg.spacing              = 8f;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = false;

            // Title.
            var title                = CreateUI("Title", card.transform);
            var titleTmp             = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text            = "PLACE PORTAL";
            titleTmp.fontSize        = 13f;
            titleTmp.fontStyle       = FontStyles.Bold;
            titleTmp.alignment       = TextAlignmentOptions.Center;
            titleTmp.color           = ACCENT;
            title.AddComponent<LayoutElement>().preferredHeight = 22f;

            // Source position read-out.
            refs.PlacePortalSourceText = AddDialogLine(card.transform,
                "Source: (waiting for click)");

            // Destination zone dropdown.
            AddDialogLine(card.transform, "Destination zone:");
            refs.PlacePortalDestDropdown = AddDropdown(card.transform);

            // Use-zone-center toggle.
            refs.PlacePortalUseCenterToggle = AddDialogToggle(card.transform,
                "Use destination zone centre", initialOn: true);

            // Explicit destination X/Y inputs.
            var destRow                  = CreateUI("DestRow", card.transform);
            destRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            var destHlg = destRow.AddComponent<HorizontalLayoutGroup>();
            destHlg.spacing                = 8f;
            destHlg.childForceExpandHeight = true;
            destHlg.childForceExpandWidth  = true;
            destHlg.childControlHeight     = true;
            destHlg.childControlWidth      = true;
            refs.PlacePortalDestXInput = AddLabeledInput(destRow.transform, "X");
            refs.PlacePortalDestYInput = AddLabeledInput(destRow.transform, "Y");

            // Activation radius.
            refs.PlacePortalRadiusInput = AddLabeledInput(card.transform,
                "Activation radius (0 = default)");
            refs.PlacePortalRadiusInput.text = "0";

            // Buttons row.
            var buttonsRow = CreateUI("Buttons", card.transform);
            buttonsRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var btnHlg = buttonsRow.AddComponent<HorizontalLayoutGroup>();
            btnHlg.spacing                = 8f;
            btnHlg.childForceExpandHeight = true;
            btnHlg.childForceExpandWidth  = true;
            btnHlg.childControlHeight     = true;
            btnHlg.childControlWidth      = true;

            // C# disallows capturing a `ref` parameter inside a lambda, so
            // copy each widget reference into a local before wiring callbacks.
            // The Confirm closure is the only one that reads back, but
            // capturing all of them up-front keeps the call site readable.
            var dropdown      = refs.PlacePortalDestDropdown;
            var useCenterTgl  = refs.PlacePortalUseCenterToggle;
            var destXInput    = refs.PlacePortalDestXInput;
            var destYInput    = refs.PlacePortalDestYInput;
            var radiusInput   = refs.PlacePortalRadiusInput;

            refs.PlacePortalCancelBtn = AddDialogButton(buttonsRow.transform, "Cancel",
                () => callbacks.OnCancelPlace?.Invoke());
            refs.PlacePortalConfirmBtn = AddDialogButton(buttonsRow.transform, "Place",
                () =>
                {
                    string z = dropdown != null && dropdown.options.Count > 0
                        ? dropdown.options[Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1)].text
                        : string.Empty;
                    bool useCenter = useCenterTgl != null && useCenterTgl.isOn;
                    float dx = ParseFloat(destXInput, 0f);
                    float dy = ParseFloat(destYInput, 0f);
                    float r  = ParseFloat(radiusInput, 0f);
                    callbacks.OnConfirmPlace?.Invoke(z, useCenter, new Vector2(dx, dy), r);
                });
        }

        private static TextMeshProUGUI AddDialogLine(Transform parent, string text)
        {
            var go  = CreateUI("Line", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text       = text;
            tmp.fontSize   = 11f;
            tmp.color      = TEXT_PRIMARY;
            tmp.alignment  = TextAlignmentOptions.Left;
            return tmp;
        }

        private static TMP_Dropdown AddDropdown(Transform parent)
        {
            var go = CreateUI("Dropdown", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 28f;
            var bg = go.AddComponent<Image>();
            bg.color = SLOT_BG;
            var dd = go.AddComponent<TMP_Dropdown>();

            // Caption (always-visible label).
            var captionGo  = CreateUI("Label", go.transform);
            var capRT      = captionGo.GetComponent<RectTransform>();
            capRT.anchorMin = Vector2.zero;
            capRT.anchorMax = Vector2.one;
            capRT.offsetMin = new Vector2(8f, 2f);
            capRT.offsetMax = new Vector2(-8f, -2f);
            var capTmp     = captionGo.AddComponent<TextMeshProUGUI>();
            capTmp.fontSize  = 11f;
            capTmp.color     = TEXT_PRIMARY;
            capTmp.alignment = TextAlignmentOptions.Left;
            dd.captionText   = capTmp;

            // Template (the runtime-instantiated list root).
            var template     = CreateUI("Template", go.transform);
            var tmplRT       = template.GetComponent<RectTransform>();
            tmplRT.anchorMin = new Vector2(0f, 0f);
            tmplRT.anchorMax = new Vector2(1f, 0f);
            tmplRT.pivot     = new Vector2(0.5f, 1f);
            tmplRT.sizeDelta = new Vector2(0f, 130f);
            template.AddComponent<Image>().color = SLOT_BG;
            template.SetActive(false);
            // Required ScrollRect + Viewport + Content + Item plumbing.
            var viewport = CreateUI("Viewport", template.transform);
            var vpRT     = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(2f, 2f);
            vpRT.offsetMax = new Vector2(-2f, -2f);
            viewport.AddComponent<Image>();
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content     = CreateUI("Content", viewport.transform);
            var contentRT   = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, 28f);

            var itemGo      = CreateUI("Item", content.transform);
            var itemRT      = itemGo.GetComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0f, 0.5f);
            itemRT.anchorMax = new Vector2(1f, 0.5f);
            itemRT.sizeDelta = new Vector2(0f, 22f);
            var itemTgl     = itemGo.AddComponent<Toggle>();
            var itemImg     = itemGo.AddComponent<Image>();
            itemImg.color   = SLOT_BG;
            itemTgl.targetGraphic = itemImg;

            var itemLabel    = CreateUI("Item Label", itemGo.transform);
            var itemLabelRT  = itemLabel.GetComponent<RectTransform>();
            itemLabelRT.anchorMin = Vector2.zero;
            itemLabelRT.anchorMax = Vector2.one;
            itemLabelRT.offsetMin = new Vector2(8f, 0f);
            itemLabelRT.offsetMax = new Vector2(-8f, 0f);
            var itemLabelTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemLabelTmp.fontSize = 11f;
            itemLabelTmp.color    = TEXT_PRIMARY;

            dd.template     = tmplRT;
            dd.itemText     = itemLabelTmp;
            dd.targetGraphic = bg;

            return dd;
        }

        private static Toggle AddDialogToggle(Transform parent, string label, bool initialOn)
        {
            var go = CreateUI("Toggle_" + label, parent);
            go.AddComponent<LayoutElement>().preferredHeight = 22f;
            var tgl    = go.AddComponent<Toggle>();
            var bgGo   = CreateUI("Box", go.transform);
            var bgImg  = bgGo.AddComponent<Image>();
            bgImg.color = SLOT_BG;
            var bgRT   = bgGo.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.5f);
            bgRT.anchorMax = new Vector2(0f, 0.5f);
            bgRT.pivot     = new Vector2(0f, 0.5f);
            bgRT.sizeDelta = new Vector2(16f, 16f);

            var checkGo  = CreateUI("Check", bgGo.transform);
            var checkRT  = checkGo.GetComponent<RectTransform>();
            checkRT.anchorMin = Vector2.zero;
            checkRT.anchorMax = Vector2.one;
            checkRT.offsetMin = new Vector2(2f, 2f);
            checkRT.offsetMax = new Vector2(-2f, -2f);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = ACCENT;
            tgl.graphic = checkImg;
            tgl.targetGraphic = bgImg;

            var labelGo = CreateUI("Label", go.transform);
            var labelRT = labelGo.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(1f, 1f);
            labelRT.offsetMin = new Vector2(22f, 0f);
            labelRT.offsetMax = new Vector2(0f, 0f);
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text     = label;
            labelTmp.fontSize = 11f;
            labelTmp.color    = TEXT_PRIMARY;
            labelTmp.alignment = TextAlignmentOptions.Left;

            tgl.SetIsOnWithoutNotify(initialOn);
            return tgl;
        }

        private static TMP_InputField AddLabeledInput(Transform parent, string label)
        {
            var wrap = CreateUI("Field_" + label, parent);
            wrap.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = wrap.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlHeight = true;
            hlg.childControlWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth  = false;

            var labelGo = CreateUI("Label", wrap.transform);
            labelGo.AddComponent<LayoutElement>().preferredWidth = 22f;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text     = label;
            labelTmp.fontSize = 11f;
            labelTmp.color    = TEXT_SECONDARY;
            labelTmp.alignment = TextAlignmentOptions.Right;

            var fieldGo = CreateUI("Input", wrap.transform);
            fieldGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var bg = fieldGo.AddComponent<Image>();
            bg.color = SLOT_BG;
            var input = fieldGo.AddComponent<TMP_InputField>();

            var textGo = CreateUI("Text", fieldGo.transform);
            var textRT = textGo.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(6f, 2f);
            textRT.offsetMax = new Vector2(-6f, -2f);
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = 11f;
            textTmp.color    = TEXT_PRIMARY;
            input.textComponent = textTmp;
            input.textViewport  = (RectTransform)fieldGo.transform;
            input.targetGraphic = bg;
            input.contentType   = TMP_InputField.ContentType.DecimalNumber;
            input.text          = "0";
            return input;
        }

        private static Button AddDialogButton(Transform parent, string label, Action onClick)
        {
            var go  = CreateUI("Btn_" + label, parent);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lbl = CreateUI("Label", go.transform);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
            var lblTmp     = lbl.AddComponent<TextMeshProUGUI>();
            lblTmp.text       = label;
            lblTmp.alignment  = TextAlignmentOptions.Center;
            lblTmp.fontSize   = 12f;
            lblTmp.color      = TEXT_PRIMARY;
            return btn;
        }

        private static float ParseFloat(TMP_InputField field, float fallback)
        {
            if (field == null) return fallback;
            return float.TryParse(field.text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        // ── Public helpers used by MapEditorUI ──────────────────────────────────

        public static void PopulatePortalDestinationDropdown(TMP_Dropdown dd,
            IList<string> zoneNames, string preferred)
        {
            if (dd == null) return;
            dd.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            int preferredIndex = 0;
            for (int i = 0; i < (zoneNames?.Count ?? 0); i++)
            {
                opts.Add(new TMP_Dropdown.OptionData(zoneNames[i]));
                if (!string.IsNullOrEmpty(preferred) &&
                    string.Equals(zoneNames[i], preferred, StringComparison.OrdinalIgnoreCase))
                    preferredIndex = i;
            }
            dd.AddOptions(opts);
            if (opts.Count > 0)
                dd.SetValueWithoutNotify(preferredIndex);
        }
    }
}
