using System.Collections;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Disables the Gameplay input action map whenever the chat panel or the
    /// dev console is open. Re-enables when both are closed.
    ///
    /// Maps to Python's <c>register_blocker</c> UI-rect mechanism, but tighter:
    /// we disable the entire Gameplay map at the InputSystem level so no movement,
    /// attack, or spell reads can slip through while a text field is focused.
    ///
    /// Auto-bootstrapped after scene load; no manual scene placement required.
    /// Survives scene transitions via DontDestroyOnLoad.
    ///
    /// Domain Reload is OFF: <see cref="ResetStaticState"/> clears the static
    /// instance reference so each Play session creates a fresh gate.
    /// </summary>
    public sealed class ChatInputGate : MonoBehaviour
    {
        // ── Static bootstrap ────────────────────────────────────────────────

        private static ChatInputGate _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (_instance != null) return;
            var go = new GameObject("[ChatInputGate]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ChatInputGate>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
        }

        // ── State ───────────────────────────────────────────────────────────

        private bool _chatOpen;
        private bool _consoleOpen;
        private bool _gameplayDisabled;

        // Tracked so we can un-subscribe cleanly.
        private ChatSystem _boundChat;
        private Valkur.Gameplay.DevConsole _boundConsole;

        // ── Lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            // Singletons may not be ready on the same frame as AutoBoot fires.
            // A one-frame coroutine gives them time to Awake first.
            StartCoroutine(LateBind());
        }

        private IEnumerator LateBind()
        {
            // Wait one frame — SingletonMonoBehaviour.Awake() runs before Start()
            // on the same frame for objects already in the scene; for dynamically
            // spawned singletons (like ChatSystem) this frame gap is the safe point.
            yield return null;
            BindSingletons();
        }

        private void BindSingletons()
        {
            // ── Chat system ──────────────────────────────────────────────────
            var chat = ChatSystem.Instance;
            if (chat != null && chat != _boundChat)
            {
                if (_boundChat != null)
                {
                    _boundChat.OnChatOpened -= HandleChatOpened;
                    _boundChat.OnChatClosed -= HandleChatClosed;
                }
                chat.OnChatOpened += HandleChatOpened;
                chat.OnChatClosed += HandleChatClosed;
                _boundChat = chat;

                // Sync state in case chat was already open before we bound.
                if (chat.IsChatOpen) _chatOpen = true;
            }

            // ── Dev console ──────────────────────────────────────────────────
            var console = Valkur.Gameplay.DevConsole.Instance;
            if (console != null && console != _boundConsole)
            {
                if (_boundConsole != null)
                {
                    _boundConsole.OnOpened -= HandleConsoleOpened;
                    _boundConsole.OnClosed -= HandleConsoleClosed;
                }
                console.OnOpened += HandleConsoleOpened;
                console.OnClosed += HandleConsoleClosed;
                _boundConsole = console;

                // Sync state in case console was already open before we bound.
                if (console.IsOpen) _consoleOpen = true;
            }

            Refresh();
        }

        private void OnDisable()
        {
            // Re-enable gameplay map so disabling this object doesn't
            // permanently freeze player input. Also clear the central
            // blocker so the helpers stop suppressing reads.
            InputBlocker.SetBlocked(false);
            if (_gameplayDisabled) EnableGameplay();

            if (_boundChat != null)
            {
                _boundChat.OnChatOpened -= HandleChatOpened;
                _boundChat.OnChatClosed -= HandleChatClosed;
                _boundChat = null;
            }
            if (_boundConsole != null)
            {
                _boundConsole.OnOpened -= HandleConsoleOpened;
                _boundConsole.OnClosed -= HandleConsoleClosed;
                _boundConsole = null;
            }
        }

        private void Update()
        {
            // Defensive: late-bind singletons that spawn after our AutoBoot
            // (DevConsole is a lazy SingletonMonoBehaviour — it materializes
            // only when the user first hits ~). Without this, the very first
            // open of the console fires OnOpened before we are subscribed,
            // _consoleOpen stays false, the blocker never engages, and the
            // wheel keeps zooming the camera while the panel scrolls.
            if (_boundChat == null || _boundConsole == null)
                BindSingletons();

            // Self-heal: poll the live IsOpen state of both panels and
            // re-Refresh if it diverges from our cached flags. Catches any
            // missed event (e.g. the very first OnOpened fired pre-bind, or
            // a panel toggled while ChatInputGate was momentarily disabled).
            var chat = _boundChat;
            var console = _boundConsole;
            bool chatOpenNow = chat != null && chat.IsChatOpen;
            bool consoleOpenNow = console != null && console.IsOpen;
            if (chatOpenNow != _chatOpen || consoleOpenNow != _consoleOpen)
            {
                _chatOpen = chatOpenNow;
                _consoleOpen = consoleOpenNow;
                Refresh();
            }
        }

        // ── Event handlers ───────────────────────────────────────────────────

        private void HandleChatOpened()    { _chatOpen = true;    Refresh(); }
        private void HandleChatClosed()    { _chatOpen = false;   Refresh(); }
        private void HandleConsoleOpened() { _consoleOpen = true;  Refresh(); }
        private void HandleConsoleClosed() { _consoleOpen = false; Refresh(); }

        // ── Block / unblock ──────────────────────────────────────────────────

        private void Refresh()
        {
            bool shouldBlock = _chatOpen || _consoleOpen;
            // Always sync the central blocker — the input helpers
            // (MouseInputManager / KeyboardInputManager / EditorHotkeyBindings)
            // poll Mouse.current and Keyboard.current directly and would
            // otherwise bypass the action-map disable.
            InputBlocker.SetBlocked(shouldBlock);

            if (shouldBlock == _gameplayDisabled) return;
            if (shouldBlock) DisableGameplay();
            else             EnableGameplay();
        }

        private void DisableGameplay()
        {
            var svc = InputService.Instance;
            if (svc?.Gameplay?.Map == null) return;
            svc.Gameplay.Map.Disable();
            _gameplayDisabled = true;
        }

        private void EnableGameplay()
        {
            var svc = InputService.Instance;
            if (svc?.Gameplay?.Map == null) return;
            svc.Gameplay.Map.Enable();
            _gameplayDisabled = false;
        }
    }
}
