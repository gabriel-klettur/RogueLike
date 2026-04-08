using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
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
    public partial class ChatUI : SingletonMonoBehaviour<ChatUI>
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

            // Enter submits message (New Input System)
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
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
        // See ChatUI.Builder.cs for BuildUI, AppendMessageRow, ClearMessages, CreateTextRow
    }
}
