using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Chat
{
    public partial class ChatUI
    {
        private void BuildUI()
        {
            if (_isBuilt) return;
            _isBuilt = true;

            // Screen-space overlay canvas
            var canvasGo = new GameObject("ChatCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1600, 800);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Modal backdrop — invisible fullscreen image that captures clicks
            // outside the panel and closes the chat. Must be the first child
            // so the panel (created next) draws on top and absorbs clicks
            // landing inside its rect. The backdrop is toggled together with
            // the panel in OnChatOpened/Closed — leaving it always-on would
            // make EventSystem.IsPointerOverGameObject() return true for the
            // entire screen and break things like the camera zoom.
            _backdrop = new GameObject("Backdrop");
            _backdrop.transform.SetParent(canvasGo.transform, false);
            var backdropRt = _backdrop.AddComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = _backdrop.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0f); // alpha=0 → invisible but raycastable
            backdropImg.raycastTarget = true;
            var backdropBtn = _backdrop.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(() => ChatSystem.Instance?.CloseChat());

            // Panel
            _panel = new GameObject("ChatPanel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 0f);
            panelRt.pivot = new Vector2(0f, 0f);
            panelRt.anchoredPosition = new Vector2(20f, 20f);
            panelRt.sizeDelta = new Vector2(PANEL_DEFAULT_W, PANEL_DEFAULT_H);

            var panelImg = _panel.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Title bar
            var titleGo = CreateTextRow(_panel.transform, "Chat — NPC", 18, Color.white, TextAlignmentOptions.Center);
            _titleText = titleGo.GetComponentInChildren<TextMeshProUGUI>();
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 28f;

            // Scroll area
            var scrollGo = new GameObject("ScrollArea");
            scrollGo.transform.SetParent(_panel.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.minHeight = 80f;

            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0.05f, 0.05f, 0.08f, 0.6f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = true;

            _scrollRect = scrollGo.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.scrollSensitivity = 20f;

            // Content container
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            _contentRect = contentGo.AddComponent<RectTransform>();
            _contentRect.anchorMin = new Vector2(0, 1);
            _contentRect.anchorMax = new Vector2(1, 1);
            _contentRect.pivot = new Vector2(0.5f, 1);
            _contentRect.sizeDelta = new Vector2(0, 0);

            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.padding = new RectOffset(4, 4, 4, 4);
            contentVlg.spacing = 2f;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect.content = _contentRect;
            _scrollRect.viewport = scrollRt;

            // Input row
            var inputRow = new GameObject("InputRow");
            inputRow.transform.SetParent(_panel.transform, false);
            var irRt = inputRow.AddComponent<RectTransform>();
            var irLe = inputRow.AddComponent<LayoutElement>();
            irLe.preferredHeight = 32f;

            var irHlg = inputRow.AddComponent<HorizontalLayoutGroup>();
            irHlg.spacing = 4f;
            irHlg.childForceExpandHeight = true;

            // Input field
            var inputGo = new GameObject("InputField");
            inputGo.transform.SetParent(inputRow.transform, false);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.flexibleWidth = 1f;

            // TMP child for input text area
            var textAreaGo = new GameObject("Text Area");
            textAreaGo.transform.SetParent(inputGo.transform, false);
            var textAreaRt = textAreaGo.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(6, 2);
            textAreaRt.offsetMax = new Vector2(-6, -2);

            var inputText = new GameObject("Text");
            inputText.transform.SetParent(textAreaGo.transform, false);
            var itRt = inputText.AddComponent<RectTransform>();
            itRt.anchorMin = Vector2.zero;
            itRt.anchorMax = Vector2.one;
            itRt.offsetMin = Vector2.zero;
            itRt.offsetMax = Vector2.zero;
            var itTmp = inputText.AddComponent<TextMeshProUGUI>();
            itTmp.fontSize = 14;
            itTmp.color = Color.white;

            var placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textAreaGo.transform, false);
            var phRt = placeholder.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var phTmp = placeholder.AddComponent<TextMeshProUGUI>();
            phTmp.text = "Escribe un mensaje...";
            phTmp.fontSize = 14;
            phTmp.color = new Color(0.5f, 0.5f, 0.5f);
            phTmp.fontStyle = FontStyles.Italic;

            _inputField = inputGo.AddComponent<TMP_InputField>();
            _inputField.textViewport = textAreaRt;
            _inputField.textComponent = itTmp;
            _inputField.placeholder = phTmp;
            _inputField.caretColor = Color.white;

            // Send button
            var sendGo = new GameObject("SendButton");
            sendGo.transform.SetParent(inputRow.transform, false);
            var sendImg = sendGo.AddComponent<Image>();
            sendImg.color = new Color(0.2f, 0.5f, 0.3f, 1f);
            var sendLe = sendGo.AddComponent<LayoutElement>();
            sendLe.preferredWidth = 60f;

            var sendTxtGo = new GameObject("Text");
            sendTxtGo.transform.SetParent(sendGo.transform, false);
            var stRt = sendTxtGo.AddComponent<RectTransform>();
            stRt.anchorMin = Vector2.zero;
            stRt.anchorMax = Vector2.one;
            stRt.offsetMin = Vector2.zero;
            stRt.offsetMax = Vector2.zero;
            var stTmp = sendTxtGo.AddComponent<TextMeshProUGUI>();
            stTmp.text = "Enviar";
            stTmp.fontSize = 14;
            stTmp.color = Color.white;
            stTmp.alignment = TextAlignmentOptions.Center;

            var sendBtn = sendGo.AddComponent<Button>();
            sendBtn.onClick.AddListener(SubmitInput);

            // Close button
            var closeGo = new GameObject("CloseButton");
            closeGo.transform.SetParent(_panel.transform, false);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 24f;
            var closeBg = closeGo.AddComponent<Image>();
            closeBg.color = new Color(0.5f, 0.15f, 0.15f, 1f);

            var closeTxtGo = new GameObject("Text");
            closeTxtGo.transform.SetParent(closeGo.transform, false);
            var ctRt = closeTxtGo.AddComponent<RectTransform>();
            ctRt.anchorMin = Vector2.zero;
            ctRt.anchorMax = Vector2.one;
            ctRt.offsetMin = Vector2.zero;
            ctRt.offsetMax = Vector2.zero;
            var ctTmp = closeTxtGo.AddComponent<TextMeshProUGUI>();
            ctTmp.text = "Cerrar (ESC)";
            ctTmp.fontSize = 12;
            ctTmp.color = Color.white;
            ctTmp.alignment = TextAlignmentOptions.Center;

            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(() => ChatSystem.Instance?.CloseChat());

            // ── Language toggle button (top-right corner of panel) ────────────
            // Built outside the VerticalLayoutGroup so it floats as an overlay.
            var langBtnGo = new GameObject("LangButton");
            langBtnGo.transform.SetParent(_panel.transform, false);
            var langRt = langBtnGo.AddComponent<RectTransform>();
            langRt.anchorMin = new Vector2(1f, 1f);
            langRt.anchorMax = new Vector2(1f, 1f);
            langRt.pivot = new Vector2(1f, 1f);
            langRt.anchoredPosition = new Vector2(-6f, -6f);
            langRt.sizeDelta = new Vector2(42f, 22f);

            var langImg = langBtnGo.AddComponent<Image>();
            langImg.color = new Color(0.18f, 0.25f, 0.45f, 1f);

            // Label child (Image + TMP on separate objects avoids NullRef gotcha)
            var langLblGo = new GameObject("LangLabel");
            langLblGo.transform.SetParent(langBtnGo.transform, false);
            var langLblRt = langLblGo.AddComponent<RectTransform>();
            langLblRt.anchorMin = Vector2.zero;
            langLblRt.anchorMax = Vector2.one;
            langLblRt.offsetMin = Vector2.zero;
            langLblRt.offsetMax = Vector2.zero;
            _langButtonText = langLblGo.AddComponent<TextMeshProUGUI>();
            _langButtonText.text = "ES";
            _langButtonText.fontSize = 12;
            _langButtonText.color = Color.white;
            _langButtonText.alignment = TextAlignmentOptions.Center;

            var langBtn = langBtnGo.AddComponent<Button>();
            langBtn.onClick.AddListener(ToggleLang);
        }

        private void AppendMessageRow(string sender, string text)
        {
            bool isPlayer = sender == "Player";
            Color senderColor = isPlayer ? Color.cyan : new Color(1f, 0.8f, 0.4f);
            string formatted = $"<color=#{ColorUtility.ToHtmlStringRGB(senderColor)}>{sender}</color>: {text}";

            var row = CreateTextRow(_contentRect.transform, formatted, 14, Color.white, TextAlignmentOptions.TopLeft);
            _messageRows.Add(row);
        }

        private void ClearMessages()
        {
            foreach (var row in _messageRows)
                if (row != null) Destroy(row);
            _messageRows.Clear();
        }

        private static GameObject CreateTextRow(Transform parent, string text, float fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("MsgRow");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            tmp.richText = true;
            tmp.overflowMode = TextOverflowModes.Overflow;

            return go;
        }
    }
}
