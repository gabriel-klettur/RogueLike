using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Input;
using Valkur.Gameplay;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies the <see cref="InputBlocker"/> integration inside
    /// <see cref="ChatInputGate"/>:
    ///
    ///   • Disabling the gate clears the blocker unconditionally so player
    ///     input is never permanently frozen if the gate object is removed
    ///     while a panel was open.
    ///   • The Update() self-heal poll detects panel state that changed
    ///     externally (e.g. a missed OnChatOpened / OnConsoleOpened event,
    ///     hot-reload teardown, panel toggled while the gate was momentarily
    ///     disabled) and resyncs the blocker on the next tick.
    ///
    /// All tests run in EditMode by injecting real <see cref="ChatSystem"/>
    /// and <see cref="DevConsole"/> instances onto the gate via reflection,
    /// then driving lifecycle methods directly. This avoids depending on
    /// Unity's coroutine scheduler (which doesn't tick in EditMode) and
    /// exercises the production polling logic without a Play Mode harness.
    /// </summary>
    [TestFixture]
    public class ChatInputGateBlockerTests
    {
        // ── Reflection helpers ───────────────────────────────────────────────

        private static readonly BindingFlags InstanceBinding =
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        private static FieldInfo GetField(System.Type t, string name)
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
            Assert.IsNotNull(f, $"Field '{name}' must exist on {obj.GetType().Name}.");
            f.SetValue(obj, value);
        }

        private static void InvokeMethod(object obj, string name)
        {
            var m = obj.GetType().GetMethod(name, InstanceBinding);
            Assert.IsNotNull(m, $"Method '{name}' must exist on {obj.GetType().Name}.");
            m.Invoke(obj, null);
        }

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            // SingletonMonoBehaviour<T> keeps its static _instance reference in its
            // own type, NOT in T. Walk up the inheritance chain to find it.
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        // ── Fixture state ────────────────────────────────────────────────────

        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // EditMode noise — Awake of injected singletons may log because their
            // dependencies (catalogs, services) aren't bootstrapped here. We only
            // care about the polling contract, so suppress.
            LogAssert.ignoreFailingMessages = true;

            // Drop any singleton references from previous fixtures so a fresh
            // ChatSystem / DevConsole can take ownership of Instance.
            ClearSingleton<ChatSystem>();
            ClearSingleton<DevConsole>();

            // Leave the blocker in a known state; tests that need it blocked
            // will set it themselves.
            InputBlocker.SetBlocked(false);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            ClearSingleton<ChatSystem>();
            ClearSingleton<DevConsole>();

            InputBlocker.SetBlocked(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Test scaffolding ─────────────────────────────────────────────────

        /// <summary>
        /// Builds a ChatInputGate already wired to fresh ChatSystem + DevConsole
        /// singletons, with both panels reported as closed. Returns the gate so
        /// the test can mutate the panels' open-state and invoke Update.
        /// </summary>
        private ChatInputGate BuildBoundGate()
        {
            var chatGo = new GameObject("[ChatSystem_Test]");
            _scene.Add(chatGo);
            var chat = chatGo.AddComponent<ChatSystem>();

            var consoleGo = new GameObject("[DevConsole_Test]");
            _scene.Add(consoleGo);
            var console = consoleGo.AddComponent<DevConsole>();

            var gateGo = new GameObject("[ChatInputGate_Test]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();

            // Inject the bound references directly so Update() doesn't try to
            // re-bind from the live SingletonMonoBehaviour.Instance graph.
            SetField(gate, "_boundChat",    chat);
            SetField(gate, "_boundConsole", console);

            return gate;
        }

        private static ChatSystem ChatOf(ChatInputGate gate)
            => (ChatSystem)GetField(typeof(ChatInputGate), "_boundChat").GetValue(gate);

        private static DevConsole ConsoleOf(ChatInputGate gate)
            => (DevConsole)GetField(typeof(ChatInputGate), "_boundConsole").GetValue(gate);

        // ── OnDisable clears the blocker ─────────────────────────────────────

        [Test]
        public void OnDisable_ClearsInputBlocker()
        {
            // Arrange — create a ChatInputGate and pre-block gameplay input to
            // simulate the state where a chat panel was open while the gate is live.
            var gateGo = new GameObject("[ChatInputGate_OnDisable_Test]");
            _scene.Add(gateGo);
            var gate = gateGo.AddComponent<ChatInputGate>();

            InputBlocker.SetBlocked(true);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Pre-condition: blocker must be true before disabling the gate.");

            // Act — invoke OnDisable directly. SetActive(false) does not reliably
            // fire MonoBehaviour.OnDisable in EditMode tests for components whose
            // Start() coroutine has not yet ticked. Direct reflection invocation
            // exercises the contract we care about: the OnDisable body runs.
            InvokeMethod(gate, "OnDisable");

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "InputBlocker must be cleared when ChatInputGate.OnDisable runs.");
        }

        // ── Update self-heal — chat opened externally ────────────────────────

        [Test]
        public void Update_WithChatOpenedExternally_SyncsBlockerToTrue()
        {
            var gate = BuildBoundGate();
            var chat = ChatOf(gate);

            // Pre-condition: nothing open, blocker idle.
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "Pre-condition: blocker must be false before opening chat.");

            // Open the chat externally — write the backing field directly so no
            // OnChatOpened event fires. This simulates the "missed event" / "hot
            // reload teardown" scenario the polling self-heal exists to recover.
            SetField(chat, "_chatOpen", true);

            // Act — drive a single Update tick.
            InvokeMethod(gate, "Update");

            // Assert — Update must have detected the divergence and re-Refreshed.
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Update must sync InputBlocker to true when chat is open " +
                "but the gate's cached state still says closed.");
        }

        // ── Update self-heal — chat closed after open ────────────────────────

        [Test]
        public void Update_WithChatClosedAfterOpen_SyncsBlockerToFalse()
        {
            var gate = BuildBoundGate();
            var chat = ChatOf(gate);

            // Step 1 — open externally and tick. State and blocker move to true.
            SetField(chat, "_chatOpen", true);
            InvokeMethod(gate, "Update");
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Pre-condition: first Update must have caught the open transition.");

            // Step 2 — close externally (again, no event fired) and tick. Update
            // must catch the reverse transition and clear the blocker.
            SetField(chat, "_chatOpen", false);
            InvokeMethod(gate, "Update");

            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "Update must clear InputBlocker when both panels are closed.");
        }

        // ── Update self-heal — console open / close ─────────────────────────

        [Test]
        public void Update_WithConsoleOpenedExternally_SyncsBlockerToTrue()
        {
            var gate = BuildBoundGate();
            var console = ConsoleOf(gate);

            Assert.IsFalse(InputBlocker.IsGameplayBlocked);

            // Mutate DevConsole's _open field directly to simulate an external
            // toggle that bypassed the OnOpened event.
            SetField(console, "_open", true);
            InvokeMethod(gate, "Update");

            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Update must sync InputBlocker to true when the dev console " +
                "is open but the gate's cached state still says closed.");
        }

        // ── Update self-heal — no transition is a no-op ─────────────────────

        [Test]
        public void Update_WithNoStateChange_LeavesBlockerUntouched()
        {
            var gate = BuildBoundGate();

            // Both panels closed and stay closed across the tick — Update must
            // NOT fire Refresh (no transition), so the blocker stays as we set it.
            InputBlocker.SetBlocked(false);
            InvokeMethod(gate, "Update");
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "Steady-closed state must not flip the blocker.");

            // Same in reverse: both panels closed but blocker pre-set to true
            // (a defensive scenario where another system had set it). Update
            // sees no panel transition → does not touch the blocker.
            InputBlocker.SetBlocked(true);
            InvokeMethod(gate, "Update");
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Update must not Refresh when neither panel transitioned, " +
                "even if the blocker was set externally.");
        }
    }
}
