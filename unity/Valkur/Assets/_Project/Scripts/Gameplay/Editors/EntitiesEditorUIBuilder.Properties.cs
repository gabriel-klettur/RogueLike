using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Entities
{
    public static partial class EntitiesEditorUIBuilder
    {
        // ── Properties Panel ──────────────────────────────────────────────────────
        // Mirrors Python entities_properties_panel: structured form grouped by
        // sections (Identity / Stats / AI / Spawn / Auto-Cast / Assets).
        // Sections are scrollable; each section is filled by the runtime editor
        // when an entity is selected.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("EntitiesPropsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Hint text (visible when nothing is selected)
            var hintGo = CreateUI("PropsHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 28f;
            refs.PropsHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.PropsHintText.text               = "Select an entity from the Picker.";
            refs.PropsHintText.fontSize           = 11f;
            refs.PropsHintText.fontStyle          = FontStyles.Italic;
            refs.PropsHintText.color              = TEXT_SECONDARY;
            refs.PropsHintText.alignment          = TextAlignmentOptions.Center;
            refs.PropsHintText.enableWordWrapping = true;

            // Scrollable form
            var (scroll, content) = MakePropsScroll(t);
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 320f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            refs.PropsFormRoot       = content;
            refs.PropsIdentitySection = MakeFormSection(content, "Identity");
            refs.PropsStatsSection    = MakeFormSection(content, "Stats");
            refs.PropsAISection       = MakeFormSection(content, "AI");
            refs.PropsSpawnSection    = MakeFormSection(content, "Spawn");
            refs.PropsAutoCastSection = MakeFormSection(content, "Auto-Cast");
            refs.PropsAssetsSection   = MakeFormSection(content, "Assets");

            refs.PropsDropdown.SetActive(false);
        }

        private static (ScrollRect, RectTransform) MakePropsScroll(Transform parent)
        {
            var srGo = CreateUI("PropsScroll", parent);
            var sr   = srGo.AddComponent<ScrollRect>();
            sr.horizontal             = false;
            sr.vertical               = true;
            sr.movementType           = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity      = 18f;
            srGo.AddComponent<RectMask2D>();

            var viewportGo                   = CreateUI("Viewport", srGo.transform);
            var vr                           = viewportGo.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            sr.viewport                      = vr;

            var contentGo                    = CreateUI("Content", viewportGo.transform);
            var cr                           = contentGo.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot     = new Vector2(0f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta        = new Vector2(0f, 0f);

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding             = new RectOffset(2, 2, 2, 6);
            vlg.spacing             = 8f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            var fitter           = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = cr;
            return (sr, cr);
        }

        private static RectTransform MakeFormSection(Transform parent, string title)
        {
            var go = CreateUI($"Section_{title}", parent);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding             = new RectOffset(0, 0, 0, 0);
            vlg.spacing             = 2f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Section header
            var hdrGo = CreateUI("Header", go.transform);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var hdrImg          = hdrGo.AddComponent<Image>();
            hdrImg.color        = MENUBAR_BG;
            var hdrTmp          = AddCenteredText(hdrGo.transform, title.ToUpper(), 10f, FontStyles.Bold, ACCENT);
            hdrTmp.alignment    = TextAlignmentOptions.MidlineLeft;
            hdrTmp.margin       = new Vector4(8f, 0f, 0f, 0f);
            hdrTmp.characterSpacing = 1.5f;

            // Body container — runtime editor fills this with rows
            var bodyGo = CreateUI("Body", go.transform);
            var body   = bodyGo.GetComponent<RectTransform>();
            var bvlg   = bodyGo.AddComponent<VerticalLayoutGroup>();
            bvlg.padding             = new RectOffset(8, 8, 4, 4);
            bvlg.spacing             = 2f;
            bvlg.childForceExpandWidth  = true;
            bvlg.childForceExpandHeight = false;
            bvlg.childControlWidth      = true;
            bvlg.childControlHeight     = true;
            bodyGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return body;
        }

        // ── Public helper used by the runtime editor to fill rows ────────────────

        public static void AddPropertyRow(RectTransform sectionBody, string label, string value)
        {
            var rowGo = CreateUI($"Row_{label}", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 16f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 110f;
            var lblTmp           = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text          = label;
            lblTmp.fontSize      = 10f;
            lblTmp.color         = TEXT_SECONDARY;
            lblTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode  = TextOverflowModes.Truncate;

            var valGo = CreateUI("Value", rowGo.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var valTmp           = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.text          = value ?? "";
            valTmp.fontSize      = 10f;
            valTmp.fontStyle     = FontStyles.Bold;
            valTmp.color         = TEXT_PRIMARY;
            valTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            valTmp.enableWordWrapping = false;
            valTmp.overflowMode  = TextOverflowModes.Truncate;
        }

        public static void ClearSection(RectTransform sectionBody)
        {
            if (sectionBody == null) return;
            for (int i = sectionBody.childCount - 1; i >= 0; i--)
            {
                var child = sectionBody.GetChild(i);
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else                       Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
