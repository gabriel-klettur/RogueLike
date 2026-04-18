using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Shared design tokens and UI factory for all runtime in-game editors.
    /// Matches the dark theme from TileEditorUIHelpers.
    /// </summary>
    public static class EditorUIHelpers
    {
        // ── Design Tokens ──
        public static readonly Color BG_PANEL       = new Color(0.09f, 0.09f, 0.12f, 0.94f);
        public static readonly Color BG_SURFACE     = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color BG_ELEVATED    = new Color(0.17f, 0.17f, 0.22f, 1f);
        public static readonly Color ACCENT         = new Color(0.90f, 0.76f, 0.38f, 1f);
        public static readonly Color ACCENT_DIM     = new Color(0.90f, 0.76f, 0.38f, 0.45f);
        public static readonly Color ACCENT_BG      = new Color(0.90f, 0.76f, 0.38f, 0.15f);
        public static readonly Color TEXT_PRIMARY    = new Color(0.93f, 0.93f, 0.96f, 1f);
        public static readonly Color TEXT_SECONDARY  = new Color(0.60f, 0.62f, 0.68f, 1f);
        public static readonly Color TEXT_MUTED      = new Color(0.42f, 0.44f, 0.50f, 1f);
        public static readonly Color BTN_NORMAL      = new Color(0.16f, 0.16f, 0.21f, 1f);
        public static readonly Color BTN_HOVER       = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color BTN_ACTIVE      = new Color(0.90f, 0.76f, 0.38f, 0.55f);
        public static readonly Color SLOT_BG         = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color SLOT_HOVER      = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color SLOT_SELECTED   = new Color(0.90f, 0.76f, 0.38f, 0.65f);
        public static readonly Color BORDER          = new Color(0.90f, 0.76f, 0.38f, 0.35f);
        public static readonly Color SEPARATOR       = new Color(0.25f, 0.25f, 0.30f, 0.6f);
        public static readonly Color DANGER          = new Color(0.90f, 0.30f, 0.30f, 1f);
        public static readonly Color SUCCESS         = new Color(0.30f, 0.90f, 0.45f, 1f);

        public const float PANEL_PAD      = 10f;
        public const float SECTION_SPACING = 6f;
        public const float SIDEBAR_WIDTH  = 300f;

        // ── Canvas Factory ──

        public static Canvas CreateEditorCanvas(string name, int sortOrder = 100)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1600, 800);
            go.AddComponent<GraphicRaycaster>();
            UILayerHelper.SetUILayerRecursive(go);
            return canvas;
        }

        public static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject MakePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUI(name, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot;
            r.anchoredPosition = anchoredPos; r.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = BG_PANEL;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = BORDER; ol.effectDistance = new Vector2(1f, 1f);
            return go;
        }

        /// <summary>Left-anchored sidebar panel.</summary>
        public static GameObject MakeSidebar(string name, Transform parent, float width = 300f)
        {
            return MakePanel(name, parent,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(width, 0));
        }

        /// <summary>Right-anchored sidebar panel.</summary>
        public static GameObject MakeRightPanel(string name, Transform parent, float width = 300f)
        {
            return MakePanel(name, parent,
                new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(0, 0), new Vector2(width, 0));
        }

        public static Button MakeButton(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f, float fontSize = 13f)
        {
            var go = CreateUI("Btn_" + label, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor = BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, TEXT_PRIMARY);
            return btn;
        }

        public static Button MakeDangerButton(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f)
        {
            var btn = MakeButton(parent, label, onClick, height);
            var c = btn.colors;
            c.normalColor = new Color(0.55f, 0.15f, 0.15f, 1f);
            c.highlightedColor = new Color(0.70f, 0.20f, 0.20f, 1f);
            c.pressedColor = DANGER;
            btn.colors = c;
            return btn;
        }

        public static TextMeshProUGUI AddCenteredText(Transform parent, string text,
            float size, FontStyles style, Color color)
        {
            var go = CreateUI("Txt", parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = color;
            return tmp;
        }

        public static TextMeshProUGUI AddLabel(Transform parent, string text,
            float fontSize = 12f, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = CreateUI("Label", parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 6f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Normal;
            tmp.alignment = align; tmp.color = TEXT_SECONDARY;
            return tmp;
        }

        public static void BuildSectionHeader(Transform parent, string text, float fontSize = 14f)
        {
            var go = CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 8f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = ACCENT;
            tmp.characterSpacing = 4f;
        }

        public static void BuildSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }

        public static void StretchFill(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
        }

        /// <summary>Creates a ScrollView with VerticalLayoutGroup content.</summary>
        public static (ScrollRect scroll, RectTransform content) MakeScrollView(
            Transform parent, string name, float height = 0f)
        {
            var scrollGo = CreateUI(name, parent);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
            scrollRt.sizeDelta = Vector2.zero;
            if (height > 0f) scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var mask = scrollGo.AddComponent<RectMask2D>();
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = BG_SURFACE;

            // Viewport
            var viewport = CreateUI("Viewport", scrollGo.transform);
            StretchFill(viewport);

            // Content
            var content = CreateUI("Content", viewport.transform);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.content = contentRt;
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 20f;

            return (sr, contentRt);
        }

        /// <summary>Creates an item slot button with optional icon.</summary>
        public static (Button button, Image icon, TextMeshProUGUI label) MakeSlotButton(
            Transform parent, string text, float size = 64f,
            UnityEngine.Events.UnityAction onClick = null)
        {
            var go = CreateUI("Slot", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size;
            var bg = go.AddComponent<Image>();
            bg.color = SLOT_BG;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = SLOT_BG; c.highlightedColor = SLOT_HOVER;
            c.pressedColor = SLOT_SELECTED;
            btn.colors = c;
            btn.targetGraphic = bg;
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Icon child
            var iconGo = CreateUI("Icon", go.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.1f, 0.2f);
            iconRt.anchorMax = new Vector2(0.9f, 0.9f);
            iconRt.sizeDelta = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.enabled = false;

            // Label child
            var labelTmp = AddLabel(go.transform, text, 9f, TextAlignmentOptions.Bottom);
            var labelRt = labelTmp.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 0.25f);
            labelRt.sizeDelta = Vector2.zero;
            labelTmp.alignment = TextAlignmentOptions.Center;

            return (btn, icon, labelTmp);
        }

        /// <summary>
        /// Creates a title bar for an editor panel.
        /// Image on parent, TMP on child to avoid dual-Graphic conflict.
        /// </summary>
        public static TextMeshProUGUI MakeTitleBar(Transform parent, string title, float height = 36f)
        {
            var go = CreateUI("TitleBar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);

            var labelGo = CreateUI("Label", go.transform);
            StretchFill(labelGo);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = title;
            tmp.fontSize = 16f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = ACCENT;
            tmp.characterSpacing = 6f;
            return tmp;
        }

        /// <summary>
        /// Creates a grid layout panel for picker/catalog display.
        /// </summary>
        public static (ScrollRect scroll, RectTransform content) MakeGridPicker(
            Transform parent, string name, int columns = 5, float cellSize = 64f, float spacing = 4f)
        {
            var (scroll, content) = MakeScrollView(parent, name);
            // DestroyImmediate is required here: Object.Destroy is deferred to
            // end-of-frame, so AddComponent<GridLayoutGroup> would fail because
            // Unity prevents two LayoutGroup components on the same GameObject.
            var existingVlg = content.GetComponent<VerticalLayoutGroup>();
            if (existingVlg != null)
                Object.DestroyImmediate(existingVlg);
            var glg = content.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(cellSize, cellSize);
            glg.spacing = new Vector2(spacing, spacing);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = columns;
            glg.padding = new RectOffset(4, 4, 4, 4);
            return (scroll, content);
        }

        /// <summary>Adds a VLG with padding to a panel.</summary>
        public static VerticalLayoutGroup AddVLG(GameObject panel, int pad = 8, float spacing = 6f)
        {
            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            vlg.spacing = spacing;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return vlg;
        }

        /// <summary>Creates a simple status text at the bottom of a panel.</summary>
        public static TextMeshProUGUI MakeStatusText(Transform parent)
        {
            var go = CreateUI("Status", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 11f;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TEXT_MUTED;
            return tmp;
        }

        /// <summary>Creates a text input field.</summary>
        public static TMP_InputField MakeInputField(Transform parent, string placeholder = "...",
            float height = 30f)
        {
            var go = CreateUI("InputField", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = BG_SURFACE;

            // Text area
            var textArea = CreateUI("TextArea", go.transform);
            StretchFill(textArea);

            // Placeholder
            var phGo = CreateUI("Placeholder", textArea.transform);
            StretchFill(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder; phTmp.fontSize = 12f;
            phTmp.fontStyle = FontStyles.Italic; phTmp.color = TEXT_MUTED;

            // Text
            var txtGo = CreateUI("Text", textArea.transform);
            StretchFill(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 12f; txtTmp.color = TEXT_PRIMARY;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.fontAsset = txtTmp.font;

            return input;
        }

        /// <summary>
        /// Creates a confirmation dialog overlay.
        /// </summary>
        public static (GameObject root, TextMeshProUGUI message, Button confirmBtn, Button cancelBtn)
            MakeConfirmDialog(Transform parent, string title)
        {
            var overlay = CreateUI("ConfirmDialog", parent);
            var rt = overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            var overlayImg = overlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);

            var panel = CreateUI("Panel", overlay.transform);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.3f, 0.35f);
            panelRt.anchorMax = new Vector2(0.7f, 0.65f);
            panelRt.sizeDelta = Vector2.zero;
            panel.AddComponent<Image>().color = BG_PANEL;
            panel.AddComponent<Outline>().effectColor = ACCENT;
            var vlg = AddVLG(panel, 16, 12f);

            BuildSectionHeader(panel.transform, title);
            var msg = AddLabel(panel.transform, "", 13f, TextAlignmentOptions.Center);
            msg.color = TEXT_PRIMARY;

            var btnRow = CreateUI("BtnRow", panel.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;

            Button confirmBtn = null, cancelBtn = null;
            confirmBtn = MakeDangerButton(btnRow.transform, "Confirm", null, 32f);
            cancelBtn = MakeButton(btnRow.transform, "Cancel", null, 32f);

            overlay.SetActive(false);
            return (overlay, msg, confirmBtn, cancelBtn);
        }
    }
}
