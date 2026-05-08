using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    public static partial class ParticlesEditorUIBuilder
    {
        // ── View Panel (live particle preview) ────────────────────────────────────
        // Simplified relative to SpellsEditorUIBuilder.ViewPanel.cs:
        //   NO direction selector (N/W/E/S)
        //   NO scrubber
        //   NO character toggle
        //
        // Layout top-to-bottom:
        //   1. Preset name label (22 px, bold, accent, centered)
        //   2. Square preview surface (384 px tall, centered via HLG)
        //   3. Zoom row (30 px): [-]  label  [+]   (no-op; TODO wired to SetStatus)
        //   4. Transport row (30 px): [Play]  [0.25x]  [0.5x]  [1x]
        //   5. Status text (20 px, italic, muted)

        private static void BuildViewPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.ViewDropdown = MakeDrop("ParticlesViewPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                VIEW_W, VIEW_H, "View",
                out var t, out refs.ViewPanelDrag);

            // Re-anchor to canvas centre so the panel floats freely
            // (mirrors SpellsEditorUIBuilder.ViewPanel.cs lines 26-31).
            var rt = (RectTransform)refs.ViewDropdown.transform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(VIEW_W, VIEW_H);

            // ── 1. Preset name label ──────────────────────────────────────────
            var nameGo = CreateUI("PresetName", t);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = "(no preset selected)";
            nameTmp.fontSize  = 13f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = ACCENT;
            refs.ViewPresetNameTmp = nameTmp;

            // ── 2. Square preview surface ─────────────────────────────────────
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

            // Background + raycastTarget ON for future mouse-wheel zoom.
            var bg           = previewGo.AddComponent<Image>();
            bg.color         = EditorUIHelpers.BG_SURFACE;
            bg.raycastTarget = true;

            var rawGo = CreateUI("RT", previewGo.transform);
            EditorUIHelpers.StretchFill(rawGo);
            var raw           = rawGo.AddComponent<RawImage>();
            raw.color         = Color.white;
            raw.raycastTarget = false;
            refs.ViewRawImage = raw;

            // ── 3. Zoom row ───────────────────────────────────────────────────
            var zoomRow = CreateUI("ZoomRow", t);
            zoomRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var zoomHlg = zoomRow.AddComponent<HorizontalLayoutGroup>();
            zoomHlg.spacing                = 6f;
            zoomHlg.childForceExpandWidth  = true;
            zoomHlg.childForceExpandHeight = true;
            zoomHlg.childControlWidth      = true;
            zoomHlg.childControlHeight     = true;

            refs.ViewZoomOutBtn = EditorUIHelpers.MakeButton(zoomRow.transform, "-", null, 26f, 14f);
            var zoomLblGo = CreateUI("ZoomLbl", zoomRow.transform);
            zoomLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var zoomLbl       = zoomLblGo.AddComponent<TextMeshProUGUI>();
            zoomLbl.text      = "ZOOM  (mouse wheel over preview)";
            zoomLbl.fontSize  = 10f;
            zoomLbl.alignment = TextAlignmentOptions.Center;
            zoomLbl.color     = TEXT_MUTED;
            refs.ViewZoomInBtn = EditorUIHelpers.MakeButton(zoomRow.transform, "+", null, 26f, 14f);

            // ── 4. Transport row ──────────────────────────────────────────────
            var transportRow = CreateUI("TransportRow", t);
            transportRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var tHlg = transportRow.AddComponent<HorizontalLayoutGroup>();
            tHlg.spacing                = 4f;
            tHlg.childForceExpandWidth  = false;
            tHlg.childForceExpandHeight = true;
            tHlg.childControlWidth      = true;
            tHlg.childControlHeight     = true;
            tHlg.childAlignment         = TextAnchor.MiddleLeft;

            refs.ViewPlayPauseBtn = EditorUIHelpers.MakeButton(transportRow.transform, "Play", null, 28f, 10f);
            refs.ViewPlayPauseBtnLabel = refs.ViewPlayPauseBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            var ppLe = refs.ViewPlayPauseBtn.gameObject.GetComponent<LayoutElement>()
                    ?? refs.ViewPlayPauseBtn.gameObject.AddComponent<LayoutElement>();
            ppLe.preferredWidth = 52f;

            refs.ViewSpeed025Btn = MakeViewSpeedButton(transportRow.transform, "0.25x", 46f, out refs.ViewSpeed025BtnImg);
            refs.ViewSpeed05Btn  = MakeViewSpeedButton(transportRow.transform, "0.5x",  40f, out refs.ViewSpeed05BtnImg);
            refs.ViewSpeed1Btn   = MakeViewSpeedButton(transportRow.transform, "1x",    34f, out refs.ViewSpeed1BtnImg);

            CreateUI("TransportSpacer", transportRow.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;

            // ── 5. Status text ────────────────────────────────────────────────
            var statusGo = CreateUI("ViewStatus", t);
            statusGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
            statusTmp.text      = "idle";
            statusTmp.fontSize  = 11f;
            statusTmp.fontStyle = FontStyles.Italic;
            statusTmp.alignment = TextAlignmentOptions.Center;
            statusTmp.color     = TEXT_MUTED;
            refs.ViewStatusTmp  = statusTmp;

            refs.ViewDropdown.SetActive(false);
        }

        private static Button MakeViewSpeedButton(Transform parent, string label, float width, out Image imgOut)
        {
            var btn   = EditorUIHelpers.MakeButton(parent, label, null, 28f, 10f);
            imgOut    = btn.GetComponent<Image>();
            if (imgOut == null) imgOut = btn.gameObject.AddComponent<Image>();
            var le = btn.gameObject.GetComponent<LayoutElement>()
                  ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
        }
    }
}
