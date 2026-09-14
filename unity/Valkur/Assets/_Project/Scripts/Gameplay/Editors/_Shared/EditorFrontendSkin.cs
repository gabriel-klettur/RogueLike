using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Reskins what the shared editor helpers built into the pre-game menus' language — the
    /// loading bar's bevelled housing, tabs that fill like the bar, bevelled buttons — WITHOUT
    /// changing those helpers.
    ///
    /// <para><b>Opt-in per editor.</b> <c>MakeDropPanel</c>, <c>UIButton</c> and friends keep the
    /// tool dialect for every editor that does not call this; an editor adopts the look by
    /// calling these after it builds, which is why one editor can try it and be reverted by
    /// deleting one partial file. The General Editor and Misiones are the ones that do.</para>
    ///
    /// <para><b>Reskinned by DISABLING, not deleting.</b> A helper's flat image stays at alpha 0
    /// — it still catches the pointer, which is what keeps a click inside a panel from reaching
    /// the world — and outlines are switched off rather than removed, so a later
    /// <c>EnsureChrome</c> does not re-add them on top.</para>
    ///
    /// <para><b>A panel's geometry stays derived by its owner.</b> The workspace restores the
    /// size a panel HAD; an editor that changes its panel constants must re-apply them on
    /// restore, the defect that pushed the launcher's fifth column outside its frame.</para>
    /// </summary>
    public static class EditorFrontendSkin
    {
        public const float TabFontMax = 9f;
        public const float TabFontMin = 6f;
        private const float TabTextPad = 4f;

        public static MenuStyle Style => MenuStyle.Active;
        public static Color Gold => Style != null ? Style.Gold : UITheme.ACCENT;
        public static Color Ink => Style != null ? Style.textOnSelection : UITheme.TEXT_PRIMARY;

        /// <summary>The bevelled frame behind a <c>MakeDropPanel</c> panel, its header drawn by the frame.</summary>
        public static BevelFrameGraphic SkinDropPanel(GameObject panelRoot, Color tint, float thickness = 4f)
        {
            if (panelRoot == null) return null;

            var flat = panelRoot.GetComponent<Image>();
            if (flat != null) flat.color = Color.clear;
            var outline = panelRoot.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;

            var frame = BevelFrameGraphic.Create(panelRoot.transform, "FrontendFrame");
            frame.Thickness = thickness;
            frame.ShadowScale = 1.3f;
            frame.Brackets = true;
            frame.Tint = tint;
            frame.HeaderHeight = Mathf.Max(0f, TileEditorUIHelpers.PANEL_HDR_H - thickness);
            frame.HeaderGemLit = 0.85f;
            frame.transform.SetAsFirstSibling();

            var header = panelRoot.transform.Find("PanelHeader");
            if (header != null)
            {
                var headerImg = header.GetComponent<Image>();
                if (headerImg != null) headerImg.color = Color.clear;
                var title = header.Find("Title")?.GetComponent<TextMeshProUGUI>();
                if (title != null)
                {
                    title.color = tint;
                    title.fontSize = 11f;
                    title.characterSpacing = 3f;
                }
            }
            var sep = panelRoot.transform.Find("HdrSep")?.GetComponent<Image>();
            if (sep != null) sep.color = Color.clear;
            return frame;
        }

        /// <summary>An additive, soft mote layer over <paramref name="parent"/>, reaching <paramref name="pad"/> past it.</summary>
        public static MenuFxLayer CreateMotes(Transform parent, string name, int capacity, float pad)
        {
            var style = Style;
            var layer = MenuFxLayer.Create(parent, MenuArt.Get(style), capacity, FrontendKit.Get(style).Additive);
            layer.name = name;
            layer.UseSoftMotes = true;
            var rt = layer.rectTransform;
            rt.offsetMin = new Vector2(-pad, -pad);
            rt.offsetMax = new Vector2(pad, pad);
            // A layout group on the parent would otherwise size it like a row.
            layer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            layer.transform.SetAsLastSibling();
            return layer;
        }

        /// <summary>
        /// One tab for a <c>HorizontalLayoutGroup</c> strip: a transparent hit target, the bar's
        /// fill when chosen and its empty groove when not, and a label that shrinks to fit.
        /// </summary>
        public static Button BuildTab(Transform row, string name, string label, Action onClick,
                                      out FrontendFillGraphic fill, out FrontendHoverGraphic groove, out TextMeshProUGUI tmp)
        {
            var go = UIFactory.CreateUI(name, row);
            var hit = go.AddComponent<Image>();
            hit.color = Color.clear;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            groove = FrontendHoverGraphic.Create(go.transform, "Groove");
            groove.Tint = Gold;
            fill = FrontendFillGraphic.Create(go.transform, "Fill");
            fill.Profile = FrontendFillProfile.Row;
            fill.Border = true;
            fill.Tint = groove.Tint;
            fill.rectTransform.offsetMin = new Vector2(1f, 1f);
            fill.rectTransform.offsetMax = new Vector2(-1f, -1f);

            tmp = UILabel.AddCenteredText(go.transform, label, TabFontMax, FontStyles.Bold, UITheme.TEXT_SECONDARY);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 1f;
            // A tab label SHRINKS to fit and never runs past its tab: "HERRAMIENTAS" in the
            // launcher was printed over its neighbour's frame before this.
            tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = TabFontMin;
            tmp.fontSizeMax = TabFontMax;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            var trt = tmp.rectTransform;
            trt.offsetMin = new Vector2(TabTextPad, trt.offsetMin.y);
            trt.offsetMax = new Vector2(-TabTextPad, trt.offsetMax.y);
            tmp.raycastTarget = false;
            return btn;
        }

        public static void PaintTab(FrontendFillGraphic fill, FrontendHoverGraphic groove, TextMeshProUGUI tmp, bool on)
        {
            if (fill != null) fill.enabled = on;
            if (groove != null) groove.enabled = !on;
            if (tmp != null) tmp.color = on ? Ink : UITheme.TEXT_SECONDARY;
        }

        /// <summary>
        /// Turns a flat <see cref="Button"/> into a bevelled one with a tinted face that brightens on
        /// hover. The label keeps its place; its colour is chosen for the face.
        /// </summary>
        public static BevelFrameGraphic SkinButton(Button btn, Color rest, Color lit, float thickness = 3f)
        {
            if (btn == null) return null;
            var hit = btn.GetComponent<Image>();
            if (hit != null) hit.color = Color.clear;
            btn.transition = Selectable.Transition.None;
            var face = BevelFrameGraphic.Create(btn.transform, "Face");
            face.Thickness = thickness;
            face.ShadowScale = 0.45f;
            face.Brackets = false;
            face.Tint = Gold;
            face.Face = rest;
            face.transform.SetAsFirstSibling();
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            foreach (var label in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
                label.color = ReadableOn(rest);
            HoverFace(btn.gameObject, face, rest, lit);
            return face;
        }

        /// <summary>Hover wiring for a bevelled face that replaced a flat button image.</summary>
        public static void HoverFace(GameObject target, BevelFrameGraphic face, Color rest, Color lit)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (face != null) { face.Face = lit; face.Glow = 1f; } });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { if (face != null) { face.Face = rest; face.Glow = 0f; } });
            trigger.triggers.Add(exit);
        }

        /// <summary>A notched rule as a layout row, replacing <c>BuildSeparator</c>'s flat line.</summary>
        public static FrontendRuleGraphic Rule(Transform parent, float height = 10f)
        {
            var rule = FrontendRuleGraphic.Create(parent, "Rule", vertical: false, Gold);
            var le = rule.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            return rule;
        }

        /// <summary>The ink that reads on <paramref name="face"/>: dark over a light face, light over a dark one.</summary>
        public static Color ReadableOn(Color face)
            => (0.2126f * face.r + 0.7152f * face.g + 0.0722f * face.b) > 0.42f ? Ink : UITheme.TEXT_PRIMARY;
    }
}
