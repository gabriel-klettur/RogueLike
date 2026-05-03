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
    public partial class ChatUI : SingletonMonoBehaviour<ChatUI>
    {
        private const float PANEL_MIN_W = 320f;
        private const float PANEL_MIN_H = 160f;
        private const float PANEL_DEFAULT_W = 520f;
        private const float PANEL_DEFAULT_H = 250f;

        protected override bool Persist => false;

        private Canvas _canvas;
        private GameObject _panel;
        private GameObject _backdrop;
        private ScrollRect _scrollRect;
        private RectTransform _contentRect;
        private TMP_InputField _inputField;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _langButtonText;
        private readonly List<GameObject> _messageRows = new List<GameObject>();

        private bool _isBuilt;

        private void Start()
        {
            BuildUI();
            _panel.SetActive(false);
            if (_backdrop != null) _backdrop.SetActive(false);

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
            // Enter key: open / send / close, depending on current state.
            // Routed through KeyboardInputManager so the legacy backend keeps
            // it working when the new InputSystem drops events (Unity 2022.3 bug).
            bool enterPressed = Valkur.Core.Input.KeyboardInputManager.WasEnterPressedThisFrame();
            if (!enterPressed) return;

            // If the DevConsole is open, let it consume Enter instead.
            var console = Valkur.Gameplay.DevConsole.Instance;
            if (console != null && console.IsOpen) return;

            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null) return;

            if (!chatSystem.IsChatOpen)
            {
                // Chat not open — try to open with the nearest NPC, using the
                // player's world position as the proximity anchor.
                var player = EntityRegistry.PlayerTransform;
                Vector2 anchor = player != null ? (Vector2)player.position : Vector2.zero;
                chatSystem.TryOpenChat(anchor);
                return;
            }

            // Chat is open: send if the field has text, close if it is empty.
            string text = _inputField != null ? (_inputField.text?.Trim() ?? "") : "";
            if (string.IsNullOrEmpty(text))
                chatSystem.CloseChat();
            else
                SubmitInput();
        }

        // ── Event Handlers ──

        private void OnChatOpened()
        {
            if (_backdrop != null) _backdrop.SetActive(true);
            _panel.SetActive(true);
            ClearMessages();

            var chatSystem = ChatSystem.Instance;
            string npcName = chatSystem.ActivePersona != null
                ? chatSystem.ActivePersona.displayName
                : "NPC";
            _titleText.text = $"Chat — {npcName}";

            // Sync language button to the persisted preference.
            if (_langButtonText != null)
            {
                string lang = chatSystem.ActiveMemory?.preferredLanguage ?? "es";
                _langButtonText.text = lang.ToUpperInvariant();
            }

            // Show existing history
            foreach (var msg in chatSystem.History)
                AppendMessageRow(msg.sender, msg.text);

            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        private void OnChatClosed()
        {
            _panel.SetActive(false);
            if (_backdrop != null) _backdrop.SetActive(false);
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

        // ── Language toggle ──

        /// <summary>
        /// Cycles the preferred language between "es" and "en" and persists.
        /// Called by the lang button built in ChatUI.Builder.cs.
        /// </summary>
        private void ToggleLang()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem?.ActiveMemory == null) return;

            NPCMemory mem = chatSystem.ActiveMemory;
            mem.preferredLanguage = mem.preferredLanguage == "es" ? "en" : "es";

            if (_langButtonText != null)
                _langButtonText.text = mem.preferredLanguage.ToUpperInvariant();

            NPCMemoryStore.Save(mem);
        }

        // ── UI Construction ──
        // See ChatUI.Builder.cs for BuildUI, AppendMessageRow, ClearMessages, CreateTextRow
    }
}
