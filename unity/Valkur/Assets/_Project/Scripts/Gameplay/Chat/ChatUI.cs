using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Runtime IMGUI-style chat panel UI.
    /// Shows message history, text input field, and send button.
    ///
    /// Maps to Python's ChatUISystem + ChatUIRenderer + ChatInputController.
    ///
    /// Python constants preserved:
    ///   Panel min size: 320×160 px
    ///   Panel max size: 1200×600 px
    ///   Panel default size: 520×220 px
    ///   Scroll step: 3 lines
    ///   Font size: 16 (main), 14 (small)
    /// </summary>
    public class ChatUI : SingletonMonoBehaviour<ChatUI>
    {
        private const float PANEL_MIN_W = 320f;
        private const float PANEL_MIN_H = 160f;
        private const float PANEL_DEFAULT_W = 520f;
        private const float PANEL_DEFAULT_H = 250f;

        protected override bool Persist => false;

        private Canvas _canvas;
        private GameObject _panel;
        private ScrollRect _scrollRect;
        private RectTransform _contentRect;
        private TMP_InputField _inputField;
        private TextMeshProUGUI _titleText;
        private readonly List<GameObject> _messageRows = new List<GameObject>();

        private bool _isBuilt;

        private void Start()
        {
            BuildUI();
            _panel.SetActive(false);

            var chatSystem = ChatSystem.Instance;
            if (chatSystem != null)
            {
                chatSystem.OnChatOpened += OnChatOpened;
                chatSystem.OnChatClosed += OnChatClosed;
                chatSystem.OnMessageReceived += OnMessageReceived;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            var chatSystem = ChatSystem.Instance;
            if (chatSystem != null)
            {
                chatSystem.OnChatOpened -= OnChatOpened;
                chatSystem.OnChatClosed -= OnChatClosed;
                chatSystem.OnMessageReceived -= OnMessageReceived;
            }
        }

        private void Update()
        {
            if (!_panel.activeSelf) return;

            // Enter submits message
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SubmitInput();
            }
        }

        // ── Event Handlers ──

        private void OnChatOpened()
        {
            _panel.SetActive(true);
            ClearMessages();

            var chatSystem = ChatSystem.Instance;
            string npcName = chatSystem.ActivePersona != null
                ? chatSystem.ActivePersona.displayName
                : "NPC";
            _titleText.text = $"Chat — {npcName}";

            // Show existing history
            foreach (var msg in chatSystem.History)
                AppendMessageRow(msg.sender, msg.text);

            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        private void OnChatClosed()
        {
            _panel.SetActive(false);
        }

        private void OnMessageReceived(string sender, string text)
        {
            if (!_panel.activeSelf) return;
            AppendMessageRow(sender, text);

            // Auto-scroll to bottom
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SubmitInput()
        {
            string text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatSystem.Instance?.SubmitPlayerMessage(text);
            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        // ── UI Construction ──

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
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

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
