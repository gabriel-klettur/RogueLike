using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Editors
{
    public static partial class EditorUIHelpers
    {

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
        /// Adds a thin vertical scrollbar (matching the Tiles editor style) to an existing ScrollRect.
        /// Also offsets the viewport to leave room for the scrollbar, and sets visibility to Permanent.
        /// </summary>
        public static Scrollbar AddVerticalScrollbar(ScrollRect scrollRect, float sbWidth = 12f)
        {
            // Offset viewport so it does not overlap the scrollbar
            var vpRt = scrollRect.viewport;
            vpRt.offsetMax = new Vector2(-sbWidth, vpRt.offsetMax.y);

            var sbGo = CreateUI("VScrollbar", scrollRect.transform);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin        = new Vector2(1f, 0f);
            sbRt.anchorMax        = new Vector2(1f, 1f);
            sbRt.pivot            = new Vector2(1f, 1f);
            sbRt.sizeDelta        = new Vector2(sbWidth, 0f);
            sbRt.anchoredPosition = Vector2.zero;
            sbGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

            var scrollbar       = sbGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateUI("SlidingArea", sbGo.transform);
            var saRt        = slidingArea.GetComponent<RectTransform>();
            saRt.anchorMin  = Vector2.zero;
            saRt.anchorMax  = Vector2.one;
            saRt.offsetMin  = new Vector2(2f,  2f);
            saRt.offsetMax  = new Vector2(-2f, -2f);

            var handleGo        = CreateUI("Handle", slidingArea.transform);
            var hRt             = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin       = Vector2.zero;
            hRt.anchorMax       = Vector2.one;
            hRt.offsetMin       = Vector2.zero;
            hRt.offsetMax       = Vector2.zero;
            var hImg            = handleGo.AddComponent<Image>();
            hImg.color          = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            scrollbar.targetGraphic = hImg;
            scrollbar.handleRect    = hRt;

            var sbColors              = scrollbar.colors;
            sbColors.normalColor      = new Color(0.55f, 0.45f, 0.22f, 0.85f);
            sbColors.highlightedColor = new Color(0.75f, 0.62f, 0.30f, 0.95f);
            sbColors.pressedColor     = new Color(0.90f, 0.76f, 0.38f, 1f);
            scrollbar.colors          = sbColors;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            return scrollbar;
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