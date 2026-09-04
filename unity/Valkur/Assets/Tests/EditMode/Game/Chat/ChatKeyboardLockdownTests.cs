using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// While a conversation is open the keyboard belongs to the conversation and to nothing
    /// else. No menu may open, no panel may toggle, no spell may fire, no HUD button may be
    /// activated — typing a message must only ever type a message.
    ///
    /// <para>THERE ARE THREE INPUT PATHS and shutting two of them looks exactly like shutting
    /// all three. (1) The bound actions, closed by disabling the Gameplay map. (2) The helper
    /// polls, which OR the legacy backend and therefore bypass the map, closed by
    /// <see cref="InputBlocker"/>. (3) uGUI's own <c>StandaloneInputModule</c>, which reads the
    /// legacy InputManager axes and answers to NEITHER of the first two. Measured live with the
    /// chat open, path 3 was wide open: <c>IsGameplayBlocked=True</c> and
    /// <c>Gameplay map enabled=False</c>, and beside them a live module bound to
    /// <c>Submit</c> / <c>Horizontal</c> / <c>Vertical</c>. Since Enter is on
    /// <see cref="InputBlocker.IsAlwaysAllowedKey"/> so the chat can be SENT, sending a message
    /// would also press whichever HUD button the player last clicked.</para>
    ///
    /// <para>WHAT THIS FIXTURE CAN AND CANNOT REACH. Paths 1 and 2 are driven end to end
    /// through <c>Refresh</c>. Path 3 is split: the MECHANISM is exercised against a real
    /// <see cref="EventSystem"/>, and the WIRING to <c>Refresh</c> is asserted structurally.
    /// That split is forced, not chosen — <c>EventSystem.current</c> cannot be assigned in Edit
    /// Mode at all. Unity's internal list is populated in <c>OnEnable</c>, which never runs for
    /// a component a test adds, and the assignment is REFUSED with a logged error
    /// ("Failed setting EventSystem.current to unknown EventSystem"), which then fails the very
    /// test that tried it. Same family as the Awake/OnDestroy trap CLAUDE.md records.</para>
    /// </summary>
    [TestFixture]
    public class ChatKeyboardLockdownTests
    {
        private GameObject _eventSystemGo;
        private EventSystem _eventSystem;
        private GameObject _gateGo;
        private ChatInputGate _gate;

        [SetUp]
        public void SetUp()
        {
            _eventSystemGo = new GameObject("TestEventSystem");
            _eventSystem = _eventSystemGo.AddComponent<EventSystem>();
            _eventSystem.sendNavigationEvents = true;

            InputService.Initialize();

            _gateGo = new GameObject("TestChatInputGate");
            _gate = _gateGo.AddComponent<ChatInputGate>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gateGo != null) UnityEngine.Object.DestroyImmediate(_gateGo);
            if (_eventSystemGo != null) UnityEngine.Object.DestroyImmediate(_eventSystemGo);

            // InputBlocker is a static and Domain Reload is off, so a fixture that leaves it
            // engaged makes an unrelated later fixture fail for a reason nothing in its name
            // mentions.
            InputBlocker.SetBlocked(false);
            var svc = InputService.Instance;
            if (svc != null && svc.Gameplay != null && svc.Gameplay.Map != null)
                svc.Gameplay.Map.Enable();
        }

        private void SetPanelsOpen(bool chat, bool console)
        {
            var t = typeof(ChatInputGate);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

            t.GetField("_chatOpen", F).SetValue(_gate, chat);
            t.GetField("_consoleOpen", F).SetValue(_gate, console);
            t.GetMethod("Refresh", F).Invoke(_gate, null);
        }

        private static string GateSource()
        {
            string path = Path.Combine(Application.dataPath,
                "_Project", "Scripts", "Gameplay", "Chat", "ChatInputGate.cs");
            Assert.IsTrue(File.Exists(path), $"ChatInputGate missing at {path}");
            return File.ReadAllText(path);
        }

        // ── Paths 1 and 2, driven end to end ─────────────────────────────────

        [Test]
        public void ChatOpen_EngagesTheCentralBlocker()
        {
            SetPanelsOpen(chat: true, console: false);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Path 2: the helper polls read the legacy backend and bypass the action map " +
                "entirely, so only this flag can stop them.");
        }

        [Test]
        public void ChatOpen_DisablesTheGameplayActionMap()
        {
            SetPanelsOpen(chat: true, console: false);
            var map = InputService.Instance?.Gameplay?.Map;
            Assert.IsNotNull(map, "InputService did not bootstrap.");
            Assert.IsFalse(map.enabled, "Path 1: the bound actions must be silenced.");
        }

        [Test]
        public void ConsoleOpen_ShutsTheSamePaths()
        {
            SetPanelsOpen(chat: false, console: true);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked);
            Assert.IsFalse(InputService.Instance.Gameplay.Map.enabled,
                "The dev console has a text field for the same reason the chat does.");
        }

        [Test]
        public void ClosingBothPanels_ReleasesTheLock()
        {
            SetPanelsOpen(chat: true, console: true);
            SetPanelsOpen(chat: false, console: false);

            Assert.IsFalse(InputBlocker.IsGameplayBlocked);
            Assert.IsTrue(InputService.Instance.Gameplay.Map.enabled,
                "A lock that is never released is worse than no lock: the player closes the " +
                "conversation and the game stops answering the keyboard.");
        }

        /// <summary>
        /// Closing ONE of two open panels must not release anything. The console can be opened
        /// over a conversation, and the flags are independent.
        /// </summary>
        [Test]
        public void ClosingOnlyOnePanel_KeepsTheLock()
        {
            SetPanelsOpen(chat: true, console: true);
            SetPanelsOpen(chat: false, console: true);

            Assert.IsTrue(InputBlocker.IsGameplayBlocked);
            Assert.IsFalse(InputService.Instance.Gameplay.Map.enabled);
        }

        // ── Path 3: the mechanism, against a real EventSystem ────────────────

        [Test]
        public void Lock_StopsUguiNavigationEvents()
        {
            ChatInputGate.SetNavigationEvents(_eventSystem, false);

            Assert.IsFalse(_eventSystem.sendNavigationEvents,
                "With navigation events live, Enter or Space activates whichever Selectable is " +
                "focused and the arrow keys walk the focus onto another one. Enter is " +
                "deliberately always-allowed so the chat can be sent, so this is reachable by " +
                "simply typing a message.");
        }

        [Test]
        public void Release_RestoresUguiNavigationEvents()
        {
            ChatInputGate.SetNavigationEvents(_eventSystem, false);
            ChatInputGate.SetNavigationEvents(_eventSystem, true);

            Assert.IsTrue(_eventSystem.sendNavigationEvents,
                "Menus and gamepad navigation outside a conversation depend on this.");
        }

        [Test]
        public void Lock_ToleratesANullEventSystem()
        {
            Assert.DoesNotThrow(() => ChatInputGate.SetNavigationEvents(null, false),
                "There is no EventSystem during early boot, and the gate auto-boots at " +
                "AfterSceneLoad. Throwing there would take the whole input pipeline down.");
        }

        /// <summary>
        /// The chat's own <c>TMP_InputField</c> has to STAY selected to receive a keystroke, so
        /// the obvious implementation — clearing <c>currentSelectedGameObject</c> — locks the
        /// player out of the conversation it is protecting. Pinned because it is the first
        /// thing anyone reaches for.
        /// </summary>
        [Test]
        public void Lock_DoesNotTouchTheSelection()
        {
            StringAssert.DoesNotContain("SetSelectedGameObject", GateSource(),
                "Deselecting would stop every keystroke reaching the chat field. The lock is " +
                "sendNavigationEvents, which leaves SendUpdateEventToSelectedObject — the call " +
                "that actually drives typing — running.");
        }

        // ── Path 3: the wiring, asserted structurally ────────────────────────

        /// <summary>
        /// The mechanism tests above pass on a gate that never CALLS it. This asserts the
        /// wiring, in the three places it has to exist: engaged from Refresh, re-asserted while
        /// a panel is up (PersistentEventSystem can adopt a different EventSystem
        /// mid-conversation, and a fresh one arrives with navigation on), and released in
        /// OnDisable beside the blocker and the map.
        /// </summary>
        [Test]
        public void Refresh_DrivesTheNavigationLock()
        {
            string src = GateSource();

            StringAssert.Contains("SetNavigationEvents(!shouldBlock);", src,
                "Refresh must drive the navigation lock from the same flag as the other two paths.");
            StringAssert.Contains("SetNavigationEvents(false);", src,
                "Update must re-assert the lock while a panel is up.");
            StringAssert.Contains("SetNavigationEvents(true);", src,
                "OnDisable must release it, or disabling the gate leaves navigation dead.");
        }

        /// <summary>
        /// A gate that shuts one path and happens to be asked about that one would pass
        /// everything above. This asserts the file still NAMES all three mechanisms, so
        /// deleting one is a red test rather than a silent hole that only shows up when a
        /// player presses Enter over a button.
        /// </summary>
        [Test]
        public void ChatInputGate_StillOwnsAllThreeMechanisms()
        {
            string src = GateSource();

            StringAssert.Contains("InputBlocker.SetBlocked", src, "Path 2 mechanism is gone.");
            StringAssert.Contains("Map.Disable()", src, "Path 1 mechanism is gone.");
            StringAssert.Contains("sendNavigationEvents", src, "Path 3 mechanism is gone.");
        }

        /// <summary>
        /// Only Escape, backquote and Enter survive the block, and each has a reason: cancel,
        /// the dev console toggle, and sending the message being typed. A fourth entry is a key
        /// that works during a conversation — the whole class of defect this fixture exists to
        /// prevent — so widening that list has to break a test rather than be a one-line edit
        /// nobody reviews.
        /// </summary>
        [Test]
        public void AlwaysAllowedKeys_AreExactlyTheFourWithAReason()
        {
            var allowed = Enum.GetValues(typeof(UnityEngine.InputSystem.Key))
                .Cast<UnityEngine.InputSystem.Key>()
                .Where(InputBlocker.IsAlwaysAllowedKey)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    UnityEngine.InputSystem.Key.Escape,
                    UnityEngine.InputSystem.Key.Backquote,
                    UnityEngine.InputSystem.Key.Enter,
                    UnityEngine.InputSystem.Key.NumpadEnter,
                },
                allowed,
                "Every key that survives the input block can be pressed mid-conversation. " +
                "Adding one is adding a control the player can trigger while typing.");
        }
    }
}
