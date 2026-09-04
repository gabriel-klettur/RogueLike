using System.Collections;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The single owner of "a text panel has the keyboard, so nothing else may have it".
    /// Engaged whenever the chat panel or the dev console is open, released when both close.
    ///
    /// <para>THERE ARE THREE INPUT PATHS AND ALL THREE MUST BE SHUT, which is the whole
    /// reason this class is not two lines long. (1) The bound actions, closed by disabling
    /// the Gameplay map. (2) The helper polls -- <c>MouseInputManager</c> /
    /// <c>KeyboardInputManager</c> / <c>EditorHotkeyBindings</c> read the legacy backend to
    /// survive the 2022.3 event-drop bug, so they bypass the map entirely and are closed by
    /// <see cref="InputBlocker"/>. (3) uGUI's own <c>StandaloneInputModule</c>, which reads
    /// the legacy InputManager axes and answers to neither of the first two; it is closed by
    /// <c>sendNavigationEvents</c>. Shutting only the first two is the state this project
    /// shipped in, and it leaves Enter able to press whichever HUD button the player last
    /// clicked.</para>
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
            SetNavigationEvents(true);
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
            else if (_chatOpen || _consoleOpen)
            {
                // Re-assert while blocked. The flags above have not changed, so Refresh is
                // not called -- but PersistentEventSystem can adopt a DIFFERENT EventSystem
                // mid-conversation (it does exactly that on sceneLoaded), and a fresh one
                // arrives with sendNavigationEvents defaulted back to true. Cheap: one
                // reference compare and one bool compare per frame, and only while a panel
                // is actually up.
                SetNavigationEvents(false);
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

            // uGUI is the THIRD input path and neither of the two above reaches it.
            // Unity's StandaloneInputModule reads the legacy InputManager axes directly
            // -- measured live with the chat open: submit=Submit, horiz=Horizontal,
            // vert=Vertical, module enabled, while IsGameplayBlocked was already true and
            // the Gameplay map already disabled. Those axes are the ones CLAUDE.md calls
            // inert because no GAMEPLAY code reads them; Unity's own module does, so
            // InputBlocker cannot touch them by construction.
            //
            // What that buys an attacker of the design: click any HUD button once and it
            // stays as currentSelectedGameObject, after which Enter or Space re-activates
            // it -- and Enter is on InputBlocker.IsAlwaysAllowedKey precisely so the chat
            // can be sent. So typing a message could fire the last button the player
            // touched. Arrow keys could also walk the selection onto a different one.
            SetNavigationEvents(!shouldBlock);

            if (shouldBlock == _gameplayDisabled) return;
            if (shouldBlock) DisableGameplay();
            else             EnableGameplay();
        }

        /// <summary>
        /// Turns uGUI's move / submit / cancel events on or off.
        ///
        /// <para>NOT by clearing <c>currentSelectedGameObject</c>, which is the obvious move
        /// and breaks the thing it is protecting: the chat's own <c>TMP_InputField</c> has to
        /// STAY selected to receive a single keystroke, so deselecting locks the player out of
        /// the conversation they just opened.</para>
        ///
        /// <para><c>sendNavigationEvents</c> is the exact seam instead. In
        /// <c>StandaloneInputModule.Process</c> it guards only the move and submit branches;
        /// <c>SendUpdateEventToSelectedObject</c> -- which is what actually drives
        /// <c>TMP_InputField</c>'s typing -- runs before the guard and is untouched. So
        /// letters still reach the field, Enter still submits through the field's own
        /// handler, and no keystroke can reach a Button. Pointer events are unaffected, so
        /// the chat's own Send and Trade buttons stay clickable.</para>
        /// </summary>
        private void SetNavigationEvents(bool enabled)
            => SetNavigationEvents(UnityEngine.EventSystems.EventSystem.current, enabled);

        /// <summary>
        /// The half that does the work, taking its target explicitly.
        ///
        /// <para>Split out for the reason <c>SnowSplatMap</c> pairs EnsureBuilt with
        /// ReleaseBuffer: in Edit Mode a component added by a test never receives OnEnable, so
        /// it never lands in the EventSystem's internal list and Unity REFUSES the assignment
        /// with "Failed setting EventSystem.current to unknown EventSystem" — an error, not a
        /// silent no-op, so it also fails the test that logged it. Play Mode reaches this
        /// through the overload above; a test hands it an instance.</para>
        ///
        /// <para>Internal rather than private so the test assembly can drive it without
        /// reflection over a signature that reflection cannot type-check.</para>
        /// </summary>
        internal static void SetNavigationEvents(UnityEngine.EventSystems.EventSystem es,
                                                 bool enabled)
        {
            // Resolved per call rather than cached: PersistentEventSystem adopts the scene's
            // own EventSystem on load and disables the one it minted at boot, so a reference
            // captured once would end up configuring a dead object.
            if (es == null) return;
            if (es.sendNavigationEvents != enabled) es.sendNavigationEvents = enabled;
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
