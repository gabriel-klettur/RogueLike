using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spells
{
    public static partial class SpellsEditorUIBuilder
    {
        // ── View Panel (live preview) ─────────────────────────────────────────────
        // Floating, draggable panel hosting a square RawImage bound to the
        // SpellPreviewService RenderTexture, plus a 4-direction selector, character
        // toggle, zoom controls, transport row, and scrubber.

        private static void BuildViewPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.ViewDropdown = MakeDrop("SpellsViewPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                VIEW_W, VIEW_H, "View",
                out var t, out refs.ViewPanelDrag);

            // Re-anchor to canvas centre so the panel floats freely.
            var rt = (RectTransform)refs.ViewDropdown.transform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(VIEW_W, VIEW_H);

            var nameGo = CreateUI("SpellName", t);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = "(no spell selected)";
            nameTmp.fontSize  = 13f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = ACCENT;
            refs.ViewSpellNameTmp = nameTmp;

            // Square preview surface — RawImage bound to the SpellPreviewService RT.
            var previewWrap = CreateUI("PreviewWrap", t);
            previewWrap.AddComponent<LayoutElement>().preferredHeight = 384f;
            var previewLayout = previewWrap.AddComponent<HorizontalLayoutGroup>();
            previewLayout.childAlignment        = TextAnchor.MiddleCenter;
            previewLayout.childForceExpandWidth  = false;
            previewLayout.childForceExpandHeight = false;
            previewLayout.childControlWidth      = true;
            previewLayout.childControlHeight     = true;

            var previewGo = CreateUI("Preview", previewWrap.transform);
            var previewLe = previewGo.AddComponent<LayoutElement>();
            previewLe.preferredWidth  = 384f;
            previewLe.preferredHeight = 384f;
            refs.ViewPreviewArea = (RectTransform)previewGo.transform;

            // Background + raycast target ON so the area receives pointer-enter/exit
            // events for mouse-wheel zoom.
            var bg           = previewGo.AddComponent<Image>();
            bg.color         = EditorUIHelpers.BG_SURFACE;
            bg.raycastTarget = true;

            var rawGo = CreateUI("RT", previewGo.transform);
            EditorUIHelpers.StretchFill(rawGo);
            var raw           = rawGo.AddComponent<RawImage>();
            raw.color         = Color.white;
            raw.raycastTarget = false;
            refs.ViewRawImage = raw;

            // Direction selector — 4 buttons in a single row [N | W | E | S].
            var dirRow = CreateUI("DirRow", t);
            dirRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var dirHlg = dirRow.AddComponent<HorizontalLayoutGroup>();
            dirHlg.spacing                = 6f;
            dirHlg.childForceExpandWidth  = true;
            dirHlg.childForceExpandHeight = true;
            dirHlg.childControlWidth      = true;
            dirHlg.childControlHeight     = true;

            refs.ViewDirNBtn = EditorUIHelpers.MakeButton(dirRow.transform, "N", null, 28f, 11f);
            refs.ViewDirWBtn = EditorUIHelpers.MakeButton(dirRow.transform, "W", null, 28f, 11f);
            refs.ViewDirEBtn = EditorUIHelpers.MakeButton(dirRow.transform, "E", null, 28f, 11f);
            refs.ViewDirSBtn = EditorUIHelpers.MakeButton(dirRow.transform, "S", null, 28f, 11f);

            // Character overlay toggle row.
            var charRow = CreateUI("CharacterRow", t);
            charRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var charHlg = charRow.AddComponent<HorizontalLayoutGroup>();
            charHlg.spacing                = 6f;
            charHlg.childForceExpandWidth  = true;
            charHlg.childForceExpandHeight = true;
            charHlg.childControlWidth      = true;
            charHlg.childControlHeight     = true;
            MakeCharacterToggleButton(charRow.transform, out refs.ViewCharacterToggleBtn,
                out refs.ViewCharacterToggleBtnImg, out refs.ViewCharacterToggleLabel);

            // Zoom row — [-]  [+]  + label between them.
            var zoomRow = CreateUI("ZoomRow", t);
            zoomRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var zoomHlg = zoomRow.AddComponent<HorizontalLayoutGroup>();
            zoomHlg.spacing                = 6f;
            zoomHlg.childForceExpandWidth  = true;
            zoomHlg.childForceExpandHeight = true;
            zoomHlg.childControlWidth      = true;
            zoomHlg.childControlHeight     = true;

            refs.ViewZoomOutBtn = EditorUIHelpers.MakeButton(zoomRow.transform, "-",   null, 26f, 14f);
            var zoomLblGo = CreateUI("ZoomLbl", zoomRow.transform);
            zoomLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var zoomLbl       = zoomLblGo.AddComponent<TextMeshProUGUI>();
            zoomLbl.text      = "ZOOM  (mouse wheel over preview)";
            zoomLbl.fontSize  = 10f;
            zoomLbl.alignment = TextAlignmentOptions.Center;
            zoomLbl.color     = TEXT_MUTED;
            refs.ViewZoomInBtn  = EditorUIHelpers.MakeButton(zoomRow.transform, "+",   null, 26f, 14f);

            BuildTransportRow(t, ref refs);
            BuildScrubberRow(t, ref refs);

            var statusGo = CreateUI("ViewStatus", t);
            statusGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
            statusTmp.text      = "idle";
            statusTmp.fontSize  = 11f;
            statusTmp.fontStyle = FontStyles.Italic;
            statusTmp.alignment = TextAlignmentOptions.Center;
            statusTmp.color     = TEXT_MUTED;
            refs.ViewStatusTmp = statusTmp;

            refs.ViewDropdown.SetActive(false);
        }

        private static void BuildTransportRow(Transform t, ref UIRefs refs)
        {
            var transportRow = CreateUI("TransportRow", t);
            transportRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var transportHlg = transportRow.AddComponent<HorizontalLayoutGroup>();
            transportHlg.spacing                = 4f;
            transportHlg.childForceExpandWidth  = false;
            transportHlg.childForceExpandHeight = true;
            // childControlWidth=true is required for flexibleWidth on child LayoutElements
            // (spacer) to be respected by the HorizontalLayoutGroup.
            transportHlg.childControlWidth      = true;
            transportHlg.childControlHeight     = true;
            transportHlg.childAlignment         = TextAnchor.MiddleLeft;

            refs.ViewPlayPauseBtn = EditorUIHelpers.MakeButton(transportRow.transform, "Play", null, 28f, 10f);
            refs.ViewPlayPauseBtnLabel = refs.ViewPlayPauseBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            var ppLe = refs.ViewPlayPauseBtn.gameObject.GetComponent<LayoutElement>()
                    ?? refs.ViewPlayPauseBtn.gameObject.AddComponent<LayoutElement>();
            ppLe.preferredWidth = 52f;

            refs.ViewSpeed025Btn    = MakeSpeedButton(transportRow.transform, "0.25x", 46f, out refs.ViewSpeed025BtnImg);
            refs.ViewSpeed05Btn     = MakeSpeedButton(transportRow.transform, "0.5x",  40f, out refs.ViewSpeed05BtnImg);
            refs.ViewSpeed1Btn      = MakeSpeedButton(transportRow.transform, "1x",    34f, out refs.ViewSpeed1BtnImg);

            CreateUI("TransportSpacer", transportRow.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;

            refs.ViewPrevFrameBtn = EditorUIHelpers.MakeButton(transportRow.transform, "<<", null, 28f, 10f);
            var prevLe = refs.ViewPrevFrameBtn.gameObject.GetComponent<LayoutElement>()
                      ?? refs.ViewPrevFrameBtn.gameObject.AddComponent<LayoutElement>();
            prevLe.preferredWidth = 34f;

            refs.ViewNextFrameBtn = EditorUIHelpers.MakeButton(transportRow.transform, ">>", null, 28f, 10f);
            var nextLe = refs.ViewNextFrameBtn.gameObject.GetComponent<LayoutElement>()
                      ?? refs.ViewNextFrameBtn.gameObject.AddComponent<LayoutElement>();
            nextLe.preferredWidth = 34f;
        }

        private static void BuildScrubberRow(Transform t, ref UIRefs refs)
        {
            var scrubRow = CreateUI("ScrubRow", t);
            scrubRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            var scrubHlg = scrubRow.AddComponent<HorizontalLayoutGroup>();
            scrubHlg.spacing                = 6f;
            scrubHlg.childForceExpandWidth  = false;
            scrubHlg.childForceExpandHeight = true;
            // childControlWidth=true so the slider's flexibleWidth=1 is honoured.
            scrubHlg.childControlWidth      = true;
            scrubHlg.childControlHeight     = true;
            scrubHlg.childAlignment         = TextAnchor.MiddleLeft;
            scrubHlg.padding                = new RectOffset(4, 4, 0, 0);

            refs.ViewFrameSlider = UISlider.Make(
                scrubRow.transform, min: 0f, max: 1f, initial: 0f,
                onValueChanged: null, height: 16f, thumbSize: 10f,
                trackColor: new Color(0.18f, 0.18f, 0.22f, 1f),
                fillColor:  ACCENT,
                handleColor: new Color(0.80f, 0.80f, 0.90f, 0.85f));
            {
                var sle = refs.ViewFrameSlider.gameObject.GetComponent<LayoutElement>()
                       ?? refs.ViewFrameSlider.gameObject.AddComponent<LayoutElement>();
                sle.flexibleWidth   = 1f;
                sle.preferredHeight = 16f;
            }

            var counterGo = CreateUI("FrameCounter", scrubRow.transform);
            var counterLe = counterGo.AddComponent<LayoutElement>();
            counterLe.preferredWidth = 90f;
            var counterTmp = counterGo.AddComponent<TMPro.TextMeshProUGUI>();
            counterTmp.text      = "Frame 0 / 0";
            counterTmp.fontSize  = 10f;
            counterTmp.alignment = TMPro.TextAlignmentOptions.Right;
            counterTmp.color     = TEXT_MUTED;
            refs.ViewFrameCounterLabel = counterTmp;
        }

        /// <summary>
        /// Creates a speed-selector button (0.25x / 0.5x / 1x) with a LayoutElement
        /// of the specified preferred width.
        /// </summary>
        private static Button MakeSpeedButton(Transform parent, string label, float width, out Image imgOut)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, null, 28f, 10f);
            imgOut  = btn.GetComponent<Image>();
            if (imgOut == null) imgOut = btn.gameObject.AddComponent<Image>();
            var le = btn.gameObject.GetComponent<LayoutElement>()
                  ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
        }

        /// <summary>
        /// Creates a full-width character toggle button styled like the speed buttons
        /// (dark background when OFF, amber when ON).
        /// </summary>
        private static void MakeCharacterToggleButton(Transform parent,
            out Button btnOut, out Image imgOut, out TMPro.TextMeshProUGUI labelOut)
        {
            var go = CreateUI("CharacterToggleBtn", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 28f;

            var img   = go.AddComponent<Image>();
            img.color = UITheme.BTN_HOVER;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = UITheme.BTN_HOVER;
            c.highlightedColor = new Color(0.32f, 0.32f, 0.40f, 1f);
            c.pressedColor     = UITheme.ACCENT;
            c.selectedColor    = UITheme.BTN_HOVER;
            c.fadeDuration     = 0.08f;
            btn.colors         = c;
            btn.targetGraphic  = img;

            var lbl           = AddCenteredText(go.transform, "Character: OFF", 10f, FontStyles.Bold, TEXT_MUTED);
            lbl.alignment     = TextAlignmentOptions.Center;

            btnOut   = btn;
            imgOut   = img;
            labelOut = lbl;
        }
    }
}
