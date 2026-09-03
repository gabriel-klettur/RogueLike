using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Low-level UI widget factories shared by all Map Editor panels.
    /// Mirrors the helpers in TileEditorUIHelpers but scoped to the Map Editor
    /// layout constants (panel header heights, scrollbar width, dock geometry).
    /// </summary>
    public static partial class MapEditorUIBuilder
    {
        // ── Panel dock ────────────────────────────────────────────────────────────

        private enum PanelDock { TopLeft, TopRight }

        private static void ApplyDock(RectTransform r, PanelDock dock,
            float xOff, float yOff, float width, float height)
        {
            switch (dock)
            {
                case PanelDock.TopLeft:
                    r.anchorMin        = new Vector2(0f, 1f);
                    r.anchorMax        = new Vector2(0f, 1f);
                    r.pivot            = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
                case PanelDock.TopRight:
                    r.anchorMin        = new Vector2(1f, 1f);
                    r.anchorMax        = new Vector2(1f, 1f);
                    r.pivot            = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-xOff, -yOff);
                    break;
                default:
                    r.anchorMin        = new Vector2(0f, 1f);
                    r.anchorMax        = new Vector2(0f, 1f);
                    r.pivot            = new Vector2(0f, 1f);
                    r.anchoredPosition = new Vector2(xOff, -yOff);
                    break;
            }
            r.sizeDelta = new Vector2(width, height);
        }

        // ── Floating panel shell ──────────────────────────────────────────────────

        /// <summary>Creates a floating dropdown panel shell with header + content area + DraggablePanel.</summary>
        private static GameObject MakeDrop(
            string name, Transform canvasT,
            PanelDock dock, float xOff, float yOff, float width, float height,
            string title, out Transform contentOut, out DraggablePanel dragOut)
        {
            var go = CreateUI(name, canvasT);
            var r  = go.GetComponent<RectTransform>();
            ApplyDock(r, dock, xOff, yOff, width, height);

            var img           = go.AddComponent<Image>();
            img.color         = TileEditorTheme.PanelBg;
            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = TileEditorTheme.Border;
            ol.effectDistance = new Vector2(TileEditorTheme.OutlinePx, TileEditorTheme.OutlinePx);

            // Header
            var hdrGo          = CreateUI("PanelHeader", go.transform);
            var hdrRt          = hdrGo.GetComponent<RectTransform>();
            hdrRt.anchorMin        = new Vector2(0f, 1f);
            hdrRt.anchorMax        = new Vector2(1f, 1f);
            hdrRt.pivot            = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = Vector2.zero;
            hdrRt.sizeDelta        = new Vector2(0f, PANEL_HDR_H);

            var hdrImg          = hdrGo.AddComponent<Image>();
            hdrImg.color        = TileEditorTheme.HeaderBg;
            hdrImg.raycastTarget = true;

            var hdrHlg = hdrGo.AddComponent<HorizontalLayoutGroup>();
            hdrHlg.padding             = new RectOffset(8, 8, 0, 0);
            hdrHlg.spacing             = 0f;
            hdrHlg.childForceExpandWidth  = false;
            hdrHlg.childForceExpandHeight = true;
            hdrHlg.childControlWidth      = true;
            hdrHlg.childControlHeight     = true;
            hdrHlg.childAlignment         = TextAnchor.MiddleLeft;

            var titleGo               = CreateUI("Title", hdrGo.transform);
            titleGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var titleTmp              = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text             = title;
            titleTmp.fontSize         = 10f;
            titleTmp.fontStyle        = FontStyles.Bold;
            titleTmp.color            = TileEditorTheme.HeaderTitle;
            titleTmp.characterSpacing = 1.5f;
            titleTmp.alignment        = TextAlignmentOptions.Left;
            titleTmp.enableWordWrapping = false;
            titleTmp.overflowMode     = TextOverflowModes.Truncate;
            titleTmp.raycastTarget    = false;

            // Separator
            var sepGo              = CreateUI("HdrSep", go.transform);
            var sepRt              = sepGo.GetComponent<RectTransform>();
            sepRt.anchorMin        = new Vector2(0f, 1f);
            sepRt.anchorMax        = new Vector2(1f, 1f);
            sepRt.pivot            = new Vector2(0f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -PANEL_HDR_H);
            sepRt.sizeDelta        = new Vector2(0f, 1f);
            var sepImg             = sepGo.AddComponent<Image>();
            sepImg.color           = TileEditorTheme.Separator;

            // Content area
            var contentGo     = CreateUI("Content", go.transform);
            var contentRt     = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, -(PANEL_HDR_H + 1f));

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding             = new RectOffset(8, 8, 6, 6);
            layout.spacing             = 4f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            contentGo.AddComponent<CanvasGroup>();

            // DraggablePanel
            var drag         = go.AddComponent<DraggablePanel>();
            drag.DragHeader  = hdrRt;
            drag.ContentRoot = contentGo;
            go.AddComponent<CanvasGroup>();

            // PanelChrome
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

        // ── Menu bar widgets ──────────────────────────────────────────────────────

        private static void AddMenuDivider(Transform parent)
        {
            var go = CreateUI("Div", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 1f;
            go.AddComponent<Image>().color = BORDER;
        }

        private static Image AddMenuBtn(Transform parent, string label, float width,
            UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI tmp)
        {
            var go = CreateUI($"MenuBtn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var img = go.AddComponent<Image>();
            img.color = MENU_BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = MENU_BTN_NORMAL;
            c.highlightedColor = MENU_BTN_HOVER;
            c.pressedColor     = MENU_BTN_OPEN;
            c.selectedColor    = MENU_BTN_NORMAL;
            c.fadeDuration     = 0.08f;
            btn.colors        = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            tmp           = AddCenteredText(go.transform, label, 11f, FontStyles.Normal, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return img;
        }

        // ── Panel action buttons ──────────────────────────────────────────────────

        private static Button AddActionBtn(Transform parent, string label, float height,
            System.Action onClick, bool danger = false)
        {
            var go  = CreateUI($"Btn_{label}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var img = go.AddComponent<Image>();
            img.color = danger ? UITheme.DANGER_IDLE : BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = danger ? UITheme.DANGER_IDLE : BTN_NORMAL;
            c.highlightedColor = danger ? new Color(0.70f, 0.20f, 0.20f, 1f) : BTN_HOVER;
            c.pressedColor     = danger ? RED_ACCENT                           : BTN_ACTIVE;
            c.selectedColor    = c.normalColor;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            AddCenteredText(go.transform, label, 12f, FontStyles.Bold, TEXT_PRIMARY);
            return btn;
        }

        private static void AddArrowBtn(Transform parent, string arrow, System.Action onClick)
        {
            var go  = CreateUI($"ArrowBtn_{arrow}", parent);
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            AddCenteredText(go.transform, arrow, 16f, FontStyles.Bold, TEXT_PRIMARY);
        }

        private static GameObject MakeRow(string name, Transform parent, float height)
        {
            var go = CreateUI(name, parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            return go;
        }

        // ── Scroll view ───────────────────────────────────────────────────────────

        // Width of the scrollbar track pinned to the right edge of the zones list.
        // Matches TILES_SCROLLBAR_W from TileEditorUIHelpers for visual consistency.
        private const float ZONES_SCROLLBAR_W = 12f;

        /// <summary>
        /// Builds a scroll view with a permanent thin-gold vertical scrollbar that
        /// matches the style used in Tile Editor and Buildings Editor.
        /// </summary>
        private static GameObject MakeScrollView(string name, Transform parent,
            out RectTransform content, float minHeight = 200f)
        {
            var root   = CreateUI(name, parent);
            var rootImg = root.AddComponent<Image>();
            rootImg.color = new Color(0.06f, 0.06f, 0.08f, 1f);

            // Viewport leaves a gutter on the right for the scrollbar track.
            var viewport = CreateUI("Viewport", root.transform);
            var vpRt     = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-ZONES_SCROLLBAR_W, 0f);
            viewport.AddComponent<RectMask2D>();

            // Content grows downward from the top of the viewport.
            var contentGo = CreateUI("Content", viewport.transform);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin        = new Vector2(0f, 1f);
            contentRt.anchorMax        = new Vector2(1f, 1f);
            contentRt.pivot            = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;
            content = contentRt;

            var vLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            vLayout.padding                = new RectOffset(4, 4, 4, 4);
            vLayout.spacing                = 3f;
            vLayout.childControlWidth      = true;
            vLayout.childControlHeight     = false;
            vLayout.childForceExpandWidth  = true;
            vLayout.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect — same sensitivity as TileEditor tile picker.
            var scroll             = root.AddComponent<ScrollRect>();
            scroll.viewport        = vpRt;
            scroll.content         = contentRt;
            scroll.horizontal      = false;
            scroll.vertical        = true;
            scroll.scrollSensitivity = 24f;
            scroll.movementType    = ScrollRect.MovementType.Clamped;

            // Gold scrollbar widget — same look as Tile / Buildings editors.
            AddZonesScrollbar(root.transform, scroll);

            return root;
        }

        /// <summary>
        /// Builds the thin permanent vertical scrollbar pinned to the right edge
        /// of the zone-list scroll container. Gold handle, dark track.
        /// </summary>
        private static void AddZonesScrollbar(Transform scrollRoot, ScrollRect scrollRect)
        {
            var sbGo = CreateUI("VScrollbar", scrollRoot);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin        = new Vector2(1f, 0f);
            sbRt.anchorMax        = new Vector2(1f, 1f);
            sbRt.pivot            = new Vector2(1f, 1f);
            sbRt.sizeDelta        = new Vector2(ZONES_SCROLLBAR_W, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            sbGo.AddComponent<Image>().color = UITheme.SCROLL_TRACK;

            var scrollbar       = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateUI("SlidingArea", sbGo.transform);
            var saRt        = slidingArea.GetComponent<RectTransform>();
            saRt.anchorMin  = Vector2.zero;
            saRt.anchorMax  = Vector2.one;
            saRt.offsetMin  = new Vector2(2f,  2f);
            saRt.offsetMax  = new Vector2(-2f, -2f);

            var handleGo    = CreateUI("Handle", slidingArea.transform);
            var hRt         = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin   = Vector2.zero;
            hRt.anchorMax   = Vector2.one;
            hRt.offsetMin   = Vector2.zero;
            hRt.offsetMax   = Vector2.zero;
            var hImg        = handleGo.AddComponent<Image>();
            hImg.color      = UITheme.SCROLL_HANDLE;

            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect    = hRt;

            var sbColors              = scrollbar.colors;
            sbColors.normalColor      = UITheme.SCROLL_HANDLE;
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor     = UITheme.ACCENT;
            scrollbar.colors          = sbColors;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        // ── Input field ───────────────────────────────────────────────────────────

        private static TMPro.TMP_InputField MakeTmpInput(GameObject host, string placeholder)
        {
            var bg    = host.AddComponent<Image>();
            bg.color  = new Color(0.13f, 0.14f, 0.18f, 1f);

            var viewport = CreateUI("Viewport", host.transform);
            var vpRt     = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(8f, 4f);
            vpRt.offsetMax = new Vector2(-8f, -4f);
            viewport.AddComponent<RectMask2D>();

            var textGo  = CreateUI("Text", viewport.transform);
            var textRt  = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = Vector2.zero;
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize  = 13f;
            textTmp.color     = TEXT_PRIMARY;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var phGo  = CreateUI("Placeholder", viewport.transform);
            var phRt  = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.sizeDelta = Vector2.zero;
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text      = placeholder;
            phTmp.fontSize  = 13f;
            phTmp.color     = new Color(0.55f, 0.58f, 0.65f, 0.75f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var input = host.AddComponent<TMPro.TMP_InputField>();
            input.textViewport    = vpRt;
            input.textComponent   = textTmp;
            input.placeholder     = phTmp;
            input.lineType        = TMPro.TMP_InputField.LineType.SingleLine;
            input.characterLimit  = 64;
            return input;
        }

        // ── Toggle widget ─────────────────────────────────────────────────────────

        private static Toggle MakeToggle(Transform parent)
        {
            var root = CreateUI("Toggle", parent);
            root.AddComponent<LayoutElement>().preferredWidth = 28f;
            var rRt = root.GetComponent<RectTransform>();
            rRt.sizeDelta = new Vector2(28f, 28f);

            var bg    = CreateUI("Background", root.transform);
            var bgRt  = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.1f, 0.1f);
            bgRt.anchorMax = new Vector2(0.9f, 0.9f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.14f, 0.18f, 1f);

            var check   = CreateUI("Checkmark", bg.transform);
            var checkRt = check.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.2f, 0.2f);
            checkRt.anchorMax = new Vector2(0.8f, 0.8f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;
            var checkImg = check.AddComponent<Image>();
            checkImg.color = new Color(0.40f, 0.88f, 0.40f, 1f);

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = checkImg;
            return toggle;
        }

        // ── Properties panel helpers ──────────────────────────────────────────────

        /// <summary>Creates a two-column label + value row inside a panel VLG.</summary>
        private static TextMeshProUGUI BuildPropRow(Transform parent, string label)
        {
            var row = CreateUI($"PropRow_{label}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 22f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Lbl", row.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 66f;
            var lbl           = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text          = $"{label}:";
            lbl.fontSize      = 10f;
            lbl.color         = TEXT_SECONDARY;
            lbl.alignment     = TextAlignmentOptions.MidlineLeft;
            lbl.raycastTarget = false;

            var valGo = CreateUI("Val", row.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var val                  = valGo.AddComponent<TextMeshProUGUI>();
            val.text                 = "—";
            val.fontSize             = 10f;
            val.color                = TEXT_PRIMARY;
            val.fontStyle            = FontStyles.Bold;
            val.alignment            = TextAlignmentOptions.MidlineLeft;
            val.enableWordWrapping   = false;
            val.raycastTarget        = false;
            return val;
        }
    }
}
