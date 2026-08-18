using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Input;
using Valkur.Gameplay;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Covers <see cref="ChatInputGate"/>'s own state machine — the part that
    /// <c>Tests/EditMode/Game/Input/ChatInputGateBlockerTests.cs</c> deliberately
    /// leaves alone. That sibling fixture only exercises <c>OnDisable</c> clearing
    /// the blocker and the <c>Update()</c> self-heal poll on an already-bound gate.
    ///
    /// What this fixture pins down instead:
    ///
    ///   • <b>Focus acquisition</b> — <c>BindSingletons</c> subscribes to
    ///     <see cref="ChatSystem"/> / <see cref="DevConsole"/> and adopts a panel
    ///     that was <i>already</i> open before the gate booted (the AutoBoot race
    ///     the production comment calls out).
    ///   • <b>Focus release</b> — <c>OnDisable</c> unsubscribes so a later panel
    ///     open cannot re-block through a dangling delegate, and re-enables the
    ///     Gameplay action map so the player is never permanently frozen.
    ///   • <b>Submit / cancel handling</b> — closing a panel is the only signal the
    ///     gate gets that a text field released focus, so the OR of the two panels
    ///     must survive one of them closing while the other is still up.
    ///   • <b>Repeated open/close</b> — <see cref="InputBlocker"/> is a latch, not
    ///     a reference counter. Opening twice must not arm a counter that a single
    ///     close fails to drain, and closing twice must not fire a spurious
    ///     <c>OnBlockChanged</c>.
    ///   • <b>Double subscription</b> — <c>Update()</c> re-enters
    ///     <c>BindSingletons</c> on every frame while either singleton is missing
    ///     (the lazy DevConsole case), so the re-bind guard has to hold.
    ///
    /// The gate exposes no public API of its own: it is driven entirely by the two
    /// panels' events. Tests therefore raise those events through the real
    /// subscription path (the field-backed <c>OnChatOpened</c> / <c>OnOpened</c>
    /// delegates) rather than calling <c>ChatSystem.OpenChat</c>, which would drag
    /// in disk I/O, EntityRegistry lookups and ChatBubble construction that have
    /// nothing to do with the gate. Per the project's cardinal input rule, no test
    /// here reads <c>Keyboard.current</c>, <c>Mouse.current</c> or
    /// <c>UnityEngine.Input</c>.
    /// </summary>
    [TestFixture]
    public class ChatInputGateTests
    {
        // ── Reflection helpers ───────────────────────────────────────────────

        private static readonly BindingFlags InstanceBinding =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        private static FieldInfo GetField(Type t, string name)
        {
            while (t != null)
            {
                var f = t.GetField(name, InstanceBinding);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = GetField(obj.GetType(), name);
            Assert.IsTrue(f != null, "Field '" + name + "' must exist on " + obj.GetType().Name + ".");
            f.SetValue(obj, value);
        }

        private static object GetFieldValue(object obj, string name)
        {
            var f = GetField(obj.GetType(), name);
            Assert.IsTrue(f != null, "Field '" + name + "' must exist on " + obj.GetType().Name + ".");
            return f.GetValue(obj);
        }

        private static void InvokeMethod(object obj, string name)
        {
            var m = obj.GetType().GetMethod(name, InstanceBinding);
            Assert.IsTrue(m != null, "Method '" + name + "' must exist on " + obj.GetType().Name + ".");
            m.Invoke(obj, null);
        }

        /// <summary>Number of handlers currently attached to a field-like event.</summary>
        private static int SubscriberCount(object target, string eventFieldName)
        {
            var d = GetFieldValue(target, eventFieldName) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        private static void RaiseEvent(object target, string eventFieldName)
        {
            var handler = GetFieldValue(target, eventFieldName) as Action;
            if (handler != null) handler.Invoke();
        }

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            // SingletonMonoBehaviour<T> keeps its static _instance on the generic
            // base type, not on T itself. Walk up until we find it.
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        /// <summary>
        /// Force-publishes <paramref name="instance"/> as the SingletonMonoBehaviour
        /// Instance for <typeparamref name="T"/>.
        ///
        /// EditMode does not run MonoBehaviour.Awake on AddComponent, so
        /// SingletonMonoBehaviour never claims the static _instance slot and
        /// ChatSystem.Instance / DevConsole.Instance stay null. Without this the
        /// gate's real BindSingletons() finds nothing to subscribe to and every
        /// wiring assertion below would pass vacuously (or fail) for the wrong
        /// reason. Writing the field directly is idempotent even in the editor
        /// versions where Awake does fire.
        /// </summary>
        private static void SetSingleton<T>(T instance) where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, instance); return; }
                type = type.BaseType;
            }
            Assert.Fail("SingletonMonoBehaviour<" + typeof(T).Name +
                        ">._instance was not found — the singleton base changed shape.");
        }

        // ── Panel drivers (mirror what ChatSystem / DevConsole really do) ────

        private static void OpenChat(ChatSystem chat)
        {
            SetField(chat, "_chatOpen", true);
            RaiseEvent(chat, "OnChatOpened");
        }

        private static void CloseChat(ChatSystem chat)
        {
            SetField(chat, "_chatOpen", false);
            RaiseEvent(chat, "OnChatClosed");
        }

        private static void OpenConsole(DevConsole console)
        {
            SetField(console, "_open", true);
            RaiseEvent(console, "OnOpened");
        }

        private static void CloseConsole(DevConsole console)
        {
            SetField(console, "_open", false);
            RaiseEvent(console, "OnClosed");
        }

        // ── Fixture state ────────────────────────────────────────────────────

        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<bool> _blockEvents = new List<bool>();
        private Action<bool> _blockProbe;
        private bool _touchedInputService;

        private ChatSystem _chat;
        private DevConsole _console;

        [SetUp]
        public void SetUp()
        {
            // ChatSystem / DevConsole Awake log when their catalogs and services are
            // absent — irrelevant here, we only care about the gate's wiring.
            LogAssert.ignoreFailingMessages = true;

            ClearSingleton<ChatSystem>();
            ClearSingleton<DevConsole>();

            InputBlocker.SetBlocked(false);

            _blockEvents.Clear();
            _blockProbe = blocked => _blockEvents.Add(blocked);
            InputBlocker.OnBlockChanged += _blockProbe;

            _touchedInputService = false;
            _chat = null;
            _console = null;
        }

        [TearDown]
        public void TearDown()
        {
            InputBlocker.OnBlockChanged -= _blockProbe;
            _blockProbe = null;

            foreach (var go in _scene) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();

            ClearSingleton<ChatSystem>();
            ClearSingleton<DevConsole>();

            if (_touchedInputService) InputService.ResetForTests();

            InputBlocker.SetBlocked(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Scaffolding ──────────────────────────────────────────────────────

        private ChatSystem CreateChat()
        {
            var go = new GameObject("[ChatSystem_GateTest]");
            _scene.Add(go);
            _chat = go.AddComponent<ChatSystem>();
            SetSingleton(_chat);   // EditMode: Awake never runs, so Instance stays null.
            return _chat;
        }

        private DevConsole CreateConsole()
        {
            var go = new GameObject("[DevConsole_GateTest]");
            _scene.Add(go);
            _console = go.AddComponent<DevConsole>();
            SetSingleton(_console);   // EditMode: Awake never runs, so Instance stays null.
            return _console;
        }

        /// <summary>
        /// Builds a gate plus the requested panels. When <paramref name="bind"/> is
        /// true the production <c>BindSingletons</c> runs, so the gate is wired up
        /// through the same code path AutoBoot uses — subscriptions included.
        /// </summary>
        private ChatInputGate BuildGate(bool withChat = true, bool withConsole = true,
                                        bool bind = true)
        {
            if (withChat) CreateChat();
            if (withConsole) CreateConsole();

            var gateGo = new GameObject("[ChatInputGate_Test]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();

            if (bind) InvokeMethod(gate, "BindSingletons");
            return gate;
        }

        /// <summary>
        /// Boots InputService and leaves the Gameplay map in the state a live
        /// gameplay scene leaves it in (enabled), so the gate has something
        /// meaningful to disable.
        /// </summary>
        private InputService BootInputServiceWithGameplayEnabled()
        {
            _touchedInputService = true;
            var svc = InputService.Initialize();
            Assume.That(svc, Is.Not.Null,
                "InputService could not bootstrap from Resources/Input — " +
                "the gameplay-map tests cannot run without it.");
            svc.Gameplay.Map.Enable();
            return svc;
        }

        // ── Focus acquisition at bind time ───────────────────────────────────

        [Test]
        public void BindSingletons_WithChatAlreadyOpen_EngagesBlocker()
        {
            // The gate AutoBoots AfterSceneLoad and only binds a frame later, so a
            // chat panel can legitimately already be open by the time it subscribes.
            var chat = CreateChat();
            SetField(chat, "_chatOpen", true);

            var gateGo = new GameObject("[ChatInputGate_LateBoot]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();
            InvokeMethod(gate, "BindSingletons");

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "BindSingletons must adopt a chat panel that was already open — " +
                "otherwise the gate boots believing nothing holds focus and the " +
                "player keeps moving while typing.");
        }

        [Test]
        public void BindSingletons_WithConsoleAlreadyOpen_EngagesBlocker()
        {
            // DevConsole is a lazy singleton: it materialises on the first '~' press,
            // which is the same press that opens it. The gate can therefore only ever
            // learn about that first open through this bind-time sync.
            var console = CreateConsole();
            SetField(console, "_open", true);

            var gateGo = new GameObject("[ChatInputGate_LateBootConsole]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();
            InvokeMethod(gate, "BindSingletons");

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "BindSingletons must adopt a dev console that was already open, or " +
                "the very first '~' leaves the wheel zooming the camera while the " +
                "console panel scrolls.");
        }

        [Test]
        public void BindSingletons_WithBothPanelsClosed_ForcesBlockerClear()
        {
            // Another system — or a torn-down previous session — left the latch armed.
            InputBlocker.SetBlocked(true);

            BuildGate();

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "Binding with both panels closed must take ownership of the latch " +
                "and clear it — a stale 'blocked' left behind by a destroyed gate " +
                "would otherwise freeze gameplay input indefinitely.");
        }

        [Test]
        public void BindSingletons_CalledRepeatedly_DoesNotDoubleSubscribe()
        {
            // No DevConsole in the scene, so Update() re-enters BindSingletons on
            // every single frame. If the 'chat != _boundChat' guard ever regresses,
            // the chat handlers multiply once per frame.
            var gate = BuildGate(withChat: true, withConsole: false, bind: true);

            Assert.AreEqual(1, SubscriberCount(_chat, "OnChatOpened"),
                "A single bind must attach exactly one OnChatOpened handler.");

            for (int i = 0; i < 5; i++) InvokeMethod(gate, "Update");

            Assert.AreEqual(1, SubscriberCount(_chat, "OnChatOpened"),
                "Five Update ticks with a missing DevConsole must not re-subscribe " +
                "OnChatOpened — the per-frame re-bind loop would leak handlers.");
            Assert.AreEqual(1, SubscriberCount(_chat, "OnChatClosed"),
                "OnChatClosed must likewise stay at a single handler.");
        }

        // ── Open / close transitions ────────────────────────────────────────

        [Test]
        public void ChatOpened_ThenClosed_TogglesBlockerExactlyOnceEachWay()
        {
            BuildGate();
            _blockEvents.Clear();

            OpenChat(_chat);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "OnChatOpened must engage the blocker through the subscribed handler.");

            CloseChat(_chat);
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "OnChatClosed must release the blocker once no panel holds focus.");

            CollectionAssert.AreEqual(new[] { true, false }, _blockEvents,
                "One open/close cycle must produce exactly one true and one false " +
                "OnBlockChanged notification — consumers that latch on the event " +
                "desync on any extra edge.");
        }

        [Test]
        public void ChatOpened_Twice_DoesNotRefireBlockChanged()
        {
            BuildGate();
            _blockEvents.Clear();

            OpenChat(_chat);
            OpenChat(_chat);

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "The blocker must stay engaged across a duplicated open.");
            Assert.AreEqual(1, _blockEvents.Count,
                "InputBlocker is a latch, not a reference counter: a duplicated " +
                "OnChatOpened must not emit a second OnBlockChanged edge.");
        }

        [Test]
        public void ChatOpenedTwice_ThenClosedOnce_ReleasesBlocker()
        {
            BuildGate();

            OpenChat(_chat);
            OpenChat(_chat);
            CloseChat(_chat);

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "Because the gate tracks one boolean per panel rather than a nesting " +
                "count, a single close after a duplicated open must fully release " +
                "input — a refcount here would strand the player unable to move.");
        }

        [Test]
        public void ChatClosed_Twice_LeavesBlockerClearAndFiresOnce()
        {
            BuildGate();
            OpenChat(_chat);
            _blockEvents.Clear();

            CloseChat(_chat);
            CloseChat(_chat);

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "A redundant close must leave the blocker released.");
            Assert.AreEqual(1, _blockEvents.Count,
                "A redundant OnChatClosed (Escape-cancel and the submit path both " +
                "closing the panel in the same frame) must not emit a second edge.");
        }

        // ── The OR-of-two-panels contract ───────────────────────────────────

        [Test]
        public void ChatClosed_WhileConsoleStillOpen_KeepsBlockerEngaged()
        {
            BuildGate();

            OpenConsole(_console);
            OpenChat(_chat);
            CloseChat(_chat);

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Closing chat while the dev console is still open must keep input " +
                "blocked — releasing here would let keystrokes typed into the " +
                "console leak through into gameplay.");
        }

        [Test]
        public void BothPanelsClosedSequentially_ReleasesBlockerOnlyOnLastClose()
        {
            BuildGate();

            OpenChat(_chat);
            OpenConsole(_console);
            _blockEvents.Clear();

            CloseConsole(_console);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "The console closing first must not release the blocker while chat " +
                "still holds focus.");

            CloseChat(_chat);
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "The blocker releases only once the last panel closes.");

            CollectionAssert.AreEqual(new[] { false }, _blockEvents,
                "Exactly one falling edge must be emitted across the two closes.");
        }

        // ── Focus release: OnDisable ────────────────────────────────────────

        [Test]
        public void OnDisable_UnsubscribesFromPanels_SoLaterOpenDoesNotReblock()
        {
            var gate = BuildGate();
            OpenChat(_chat);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Pre-condition: an open chat blocks input.");

            InvokeMethod(gate, "OnDisable");

            Assert.AreEqual(0, SubscriberCount(_chat, "OnChatOpened"),
                "OnDisable must detach the chat handlers, otherwise a destroyed gate " +
                "keeps a live delegate and throws MissingReferenceException on the " +
                "next panel open (Domain Reload is OFF, so statics survive).");
            Assert.AreEqual(0, SubscriberCount(_console, "OnOpened"),
                "OnDisable must detach the dev console handlers too.");

            // Re-open both panels: nothing is listening, so the latch must stay clear.
            CloseChat(_chat);
            OpenChat(_chat);
            OpenConsole(_console);

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "A disabled gate must not keep blocking input on later panel opens.");
        }

        [Test]
        public void OnDisable_ThenRebind_ReattachesHandlersExactlyOnce()
        {
            // Mirrors the gate object being disabled and re-enabled (scene reload,
            // hot reload): the second bind has to work, and work only once.
            var gate = BuildGate();
            InvokeMethod(gate, "OnDisable");
            Assert.AreEqual(0, SubscriberCount(_chat, "OnChatOpened"),
                "Pre-condition: OnDisable left no chat subscribers.");

            InvokeMethod(gate, "BindSingletons");

            Assert.AreEqual(1, SubscriberCount(_chat, "OnChatOpened"),
                "Re-binding after OnDisable must reattach exactly one handler — " +
                "OnDisable nulls _boundChat precisely so this path can re-arm.");

            OpenChat(_chat);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "The re-bound gate must react to panel events again.");
        }

        [Test]
        public void OnDisable_CalledTwice_IsIdempotent()
        {
            var gate = BuildGate();
            OpenChat(_chat);

            InvokeMethod(gate, "OnDisable");
            Assert.DoesNotThrow(() => InvokeMethod(gate, "OnDisable"),
                "A second OnDisable (disable immediately followed by destroy) must " +
                "not throw on the already-nulled bound references.");

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "The blocker must remain released after a redundant OnDisable.");
            Assert.AreEqual(0, SubscriberCount(_chat, "OnChatOpened"),
                "The redundant OnDisable must not re-add or resurrect handlers.");
        }

        // ── Late binding of a singleton that spawns after the gate ──────────

        [Test]
        public void Update_WithConsoleAppearingAfterBoot_LateBindsAndAdoptsOpenState()
        {
            // Gate booted with no DevConsole in the scene at all.
            var gate = BuildGate(withChat: true, withConsole: false, bind: true);
            Assert.IsTrue(GetFieldValue(gate, "_boundConsole") as DevConsole == null,
                "Pre-condition: no console bound yet.");

            // The console materialises on the first '~' press — already open by the
            // time the gate's next Update runs, and its OnOpened already fired into
            // the void.
            var console = CreateConsole();
            SetField(console, "_open", true);

            InvokeMethod(gate, "Update");

            Assert.IsFalse(GetFieldValue(gate, "_boundConsole") as DevConsole == null,
                "Update must late-bind a DevConsole that spawned after the gate.");
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Late binding must also adopt the console's already-open state — " +
                "this is exactly the missed-first-OnOpened bug the re-bind exists for.");
        }

        [Test]
        public void Update_WithNoPanelsPresent_ForcesBlockerClear()
        {
            // Gate alive in a scene where both singletons are gone (destroyed while a
            // panel was open, for instance). Nothing can ever close them again, so
            // the gate must not leave input latched off.
            var gateGo = new GameObject("[ChatInputGate_Orphan]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();

            InputBlocker.SetBlocked(true);
            InvokeMethod(gate, "Update");

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "An orphaned gate with neither chat nor console must drive the " +
                "blocker back to false — otherwise a panel destroyed while open " +
                "leaves the player permanently unable to act.");
        }

        // ── Gameplay action-map focus (requires a live InputService) ────────

        [Test]
        public void ChatOpened_WithInputService_DisablesGameplayMapAndCloseReenablesIt()
        {
            var svc = BootInputServiceWithGameplayEnabled();
            BuildGate();
            Assume.That(svc.Gameplay.Map.enabled, Is.True,
                "Pre-condition: the gameplay map is enabled before the panel opens.");

            OpenChat(_chat);
            Assert.IsFalse(svc.Gameplay.Map.enabled,
                "Opening chat must disable the Gameplay action map, not merely set " +
                "the blocker — bound-action callsites bypass the blocker entirely.");

            CloseChat(_chat);
            Assert.IsTrue(svc.Gameplay.Map.enabled,
                "Closing the last panel must re-enable the Gameplay action map.");
        }

        [Test]
        public void OnDisable_WhileChatOpen_ReenablesGameplayMap()
        {
            var svc = BootInputServiceWithGameplayEnabled();
            var gate = BuildGate();

            OpenChat(_chat);
            Assume.That(svc.Gameplay.Map.enabled, Is.False,
                "Pre-condition: the gameplay map is disabled while chat is open.");

            InvokeMethod(gate, "OnDisable");

            Assert.IsTrue(svc.Gameplay.Map.enabled,
                "Disabling the gate while a panel is open must restore the Gameplay " +
                "map — otherwise removing the gate object freezes the player for good.");
        }

        [Test]
        public void ChatOpened_WithoutInputService_StillEngagesBlocker()
        {
            // Boot-race / headless case: the gate can be told a panel opened before
            // InputService exists. Disabling the map is impossible then, but the
            // central latch must still engage, because the helper-polling callsites
            // (MouseInputManager / KeyboardInputManager) consult only that latch.
            _touchedInputService = true;
            InputService.ResetForTests();
            Assume.That(InputService.Instance, Is.Null,
                "Pre-condition: no InputService instance for this test.");

            BuildGate();
            OpenChat(_chat);

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "InputBlocker must be set before Refresh's action-map work, so a " +
                "missing InputService cannot silently skip the whole block.");
        }
    }
}
