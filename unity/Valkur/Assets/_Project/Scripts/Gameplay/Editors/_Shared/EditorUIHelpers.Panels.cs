using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared floating-panel + button primitives extracted from the per-editor
    /// builders (Buildings/Entities/Particles/FSM/Items/Inventory/Map/Spells)
    /// where the same factory was duplicated verbatim. New editors should call
    /// these directly; legacy editors keep their private wrappers for now.
    ///
    /// Visuals follow <see cref="TileEditorTheme"/> so every editor stays in
    /// sync with the live UX-panel theme tweaks.
    /// </summary>
    public static partial class EditorUIHelpers
    {
        // ── Floating panel factory ───────────────────────────────────────────────
        // Mirrors the duplicated MakeDrop in BuildingsEditorUIBuilder.Widgets,
        // EntitiesEditorUIBuilder.Widgets, ParticlesEditorUIBuilder.Widgets, etc.
        // Returns the panel root, content transform, and the DraggablePanel
        // component so the caller can wire dropdown toggling.

        public static GameObject MakeDropPanel(
            string name, Transform canvasT,
            TileEditorUIHelpers.PanelDock dock,
            float xOff, float yOff, float width, float height,
            string title,
            out Transform contentOut, out DraggablePanel dragOut,
            bool narrowPanel = false)
        {
            var go = UIFactory.CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyPanelDock(r, dock, xOff, yOff, width, height);

            var img           = go.AddComponent<Image>();
            img.color         = TileEditorTheme.PanelBg;
            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo              = UIFactory.CreateUI("PanelHeader", go.transform);
            var hdrRt              = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, TileEditorUIHelpers.PANEL_HDR_H);

            var hdrImg           = hdrGo.AddComponent<Image>();
            hdrImg.color         = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.spacing                = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            TextMeshProUGUI titleTmp = null;
            if (!narrowPanel)
            {
                hdrHlg.padding = new RectOffset(8, 8, 0, 0);
                var titleGo                 = UIFactory.CreateUI("Title", hdrGo.transform);
                titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                titleTmp                    = titleGo.AddComponent<TextMeshProUGUI>();
                titleTmp.text               = title?.ToUpper() ?? string.Empty;
                titleTmp.fontSize           = 10f;
                titleTmp.fontStyle          = FontStyles.Bold;
                titleTmp.color              = TileEditorTheme.HeaderTitle;
                titleTmp.characterSpacing   = 1.5f;
                titleTmp.alignment          = TextAlignmentOptions.Left;
                titleTmp.enableWordWrapping = false;
                titleTmp.overflowMode       = TextOverflowModes.Truncate;
                titleTmp.raycastTarget      = false;
            }

            // Separator
            var sepGo              = UIFactory.CreateUI("HdrSep", go.transform);
            var sepRt              = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin        = new Vector2(0f, 1f);
            sepRt.anchorMax        = new Vector2(1f, 1f);
            sepRt.pivot            = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -TileEditorUIHelpers.PANEL_HDR_H);
            sepRt.sizeDelta        = new Vector2(0f, 1f);
            var sepImg             = sepGo.AddComponent<Image>();
            sepImg.color           = TileEditorTheme.Separator;

            // Content area
            var contentGo       = UIFactory.CreateUI("Content", go.transform);
            var contentRt       = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(TileEditorUIHelpers.PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding                = new RectOffset(8, 8, 6, 6);
            layout.spacing                = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            contentGo.AddComponent<CanvasGroup>();

            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            var chrome             = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = img;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            contentOut = contentGo.transform;
            dragOut    = drag;
            return go;
        }

        public static void ApplyPanelDock(RectTransform r,
            TileEditorUIHelpers.PanelDock dock,
            float xOff, float yOff, float width, float height)
        {
            switch (dock)
            {
                case TileEditorUIHelpers.PanelDock.TopLeft:
                    r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f);
                    r.pivot     = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case TileEditorUIHelpers.PanelDock.TopRight:
                    r.anchorMin = new Vector2(1f, 1f); r.anchorMax = new Vector2(1f, 1f);
                    r.pivot     = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                case TileEditorUIHelpers.PanelDock.BottomLeft:
                    r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 0f);
                    r.pivot     = new Vector2(0f, 0f);
                    r.anchoredPosition = new Vector2(xOff, yOff);
                    break;
                case TileEditorUIHelpers.PanelDock.BottomRight:
                    r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 0f);
                    r.pivot     = new Vector2(1f, 0f);
                    r.anchoredPosition = new Vector2(-xOff, yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }

        // ── Compact action button (Tools-panel style) ────────────────────────────

        public static Image AddActionBtn(Transform parent, string label, float height,
            Action onClick, out TextMeshProUGUI tmp,
            float fontSize = 10f, FontStyles style = FontStyles.Bold)
        {
            var go = UIFactory.CreateUI($"Act_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = UITheme.BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = UITheme.BTN_NORMAL;
            c.highlightedColor = UITheme.BTN_HOVER;
            c.pressedColor     = UITheme.BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            tmp           = UILabel.AddCenteredText(go.transform, label, fontSize, style, UITheme.TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        public static Image AddDangerBtn(Transform parent, string label, float height,
            Action onClick, out TextMeshProUGUI tmp)
        {
            var go = UIFactory.CreateUI($"Danger_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = UITheme.DANGER_IDLE;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = UITheme.DANGER_IDLE;
            c.highlightedColor = new Color(0.75f, 0.20f, 0.20f, 1f);
            c.pressedColor     = UITheme.DANGER;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            tmp           = UILabel.AddCenteredText(go.transform, label, 10f, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        // ── Mode button with optional sub-label (Add/Remove panel style) ─────────

        public static Image AddModeBtn(Transform parent, string label, string sub,
            float height, Action onClick, out TextMeshProUGUI labelTmp)
        {
            var go = UIFactory.CreateUI($"Mode_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = UITheme.BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = UITheme.BTN_NORMAL;
            c.highlightedColor = UITheme.BTN_HOVER;
            c.pressedColor     = UITheme.BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var vl = go.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment         = TextAnchor.MiddleCenter;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.spacing                = 0f;
            vl.padding                = new RectOffset(2, 2, 4, 4);

            var lblGo = UIFactory.CreateUI("Lbl", go.transform);
            lblGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            labelTmp           = lblGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text      = label;
            labelTmp.fontSize  = 11f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color     = UITheme.TEXT_PRIMARY;

            if (!string.IsNullOrEmpty(sub))
            {
                var subGo = UIFactory.CreateUI("Sub", go.transform);
                subGo.AddComponent<LayoutElement>().preferredHeight = 11f;
                var subTmp       = subGo.AddComponent<TextMeshProUGUI>();
                subTmp.text      = sub;
                subTmp.fontSize  = 9f;
                subTmp.alignment = TextAlignmentOptions.Center;
                subTmp.color     = UITheme.TEXT_MUTED;
            }
            return img;
        }

        // ── Menu-bar button highlight (toggled when its dropdown opens) ──────────

        public static void ApplyMenuBtnStyle(Image img, TextMeshProUGUI tmp, bool isOpen)
        {
            if (img != null)
                img.color = isOpen ? TileEditorUIHelpers.MENU_BTN_OPEN : TileEditorUIHelpers.MENU_BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isOpen ? UITheme.ACCENT      : UITheme.TEXT_PRIMARY;
                tmp.fontStyle = isOpen ? FontStyles.Bold     : FontStyles.Normal;
            }
        }

        // ── Menu-bar primitives (extracted from per-editor BuildMenuBar copies) ─

        public static void AddMenuDivider(Transform parent)
        {
            var go = UIFactory.CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = TileEditorUIHelpers.BORDER;
        }

        public static Image AddMenuBtn(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = UIFactory.CreateUI($"MenuBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img = go.AddComponent<Image>();
            img.color = TileEditorUIHelpers.MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = TileEditorUIHelpers.MENU_BTN_NORMAL;
            c.highlightedColor = TileEditorUIHelpers.MENU_BTN_HOVER;
            c.pressedColor     = TileEditorUIHelpers.MENU_BTN_OPEN;
            c.selectedColor    = TileEditorUIHelpers.MENU_BTN_NORMAL;
            c.fadeDuration     = 0.08f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            tmp           = UILabel.AddCenteredText(go.transform, label, 11f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }
    }
}
