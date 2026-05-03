using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Input;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies the <see cref="InputBlocker"/> integration inside
    /// <see cref="ChatInputGate"/>:
    ///
    ///   • Disabling / destroying the gate clears the blocker unconditionally
    ///     so player input is never permanently frozen if the gate object is
    ///     removed while a panel was open.
    ///
    /// Runtime Update() tests (self-heal with live ChatSystem / DevConsole
    /// singletons) are excluded from EditMode because they require the full
    /// singleton graph that only materialises in Play Mode. Those scenarios are
    /// marked [Ignore] with an explanation so the intent is documented.
    /// </summary>
    [TestFixture]
    public class ChatInputGateBlockerTests
    {
        private GameObject _gateGo;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            // Leave the blocker in a known state; tests that need it blocked
            // will set it themselves.
            InputBlocker.SetBlocked(false);
        }

        [TearDown]
        public void TearDown()
        {
            // Destroy the gate object if the test did not already do so.
            if (_gateGo != null)
                Object.DestroyImmediate(_gateGo);
            _gateGo = null;

            InputBlocker.SetBlocked(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── OnDisable clears the blocker ─────────────────────────────────────

        [Test]
        public void OnDisable_ClearsInputBlocker()
        {
            // Arrange — create a ChatInputGate and pre-block gameplay input to
            // simulate the state where a chat panel was open while the gate is live.
            _gateGo = new GameObject("[ChatInputGate_Test]");
            var gate = _gateGo.AddComponent<ChatInputGate>();

            InputBlocker.SetBlocked(true);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked,
                "Pre-condition: blocker must be true before disabling the gate.");

            // Act — invoke OnDisable directly. SetActive(false) does not reliably
            // fire MonoBehaviour.OnDisable in EditMode tests for components whose
            // Start() coroutine has not yet ticked, so the test would otherwise
            // observe a "fake stale" state. Direct reflection invocation is the
            // canonical workaround used elsewhere in the suite (see
            // ParticlesEditorLifecycleTests.InvokeMethod) and exercises the
            // exact contract we care about: the OnDisable body runs.
            var onDisable = typeof(ChatInputGate).GetMethod(
                "OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(onDisable,
                "ChatInputGate.OnDisable must exist as a private instance method.");
            onDisable.Invoke(gate, null);

            // Assert — OnDisable must call InputBlocker.SetBlocked(false).
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "InputBlocker must be cleared when ChatInputGate.OnDisable runs.");
        }

        // ── Runtime self-heal tests (require singleton graph) ───────────────

        [Test]
        [Ignore("Requires runtime ChatSystem singleton — run in PlayMode or integration test suite.")]
        public void Update_WithChatOpenedExternally_SyncsBlockerToTrue()
        {
            // In Play Mode: create a ChatInputGate, open ChatSystem.IsChatOpen
            // externally, manually call Update(), and assert InputBlocker.IsGameplayBlocked.
        }

        [Test]
        [Ignore("Requires runtime ChatSystem singleton — run in PlayMode or integration test suite.")]
        public void Update_WithChatClosedAfterOpen_SyncsBlockerToFalse()
        {
            // In Play Mode: create a ChatInputGate, open then close the chat,
            // call Update(), and assert that IsGameplayBlocked becomes false.
        }
    }
}
