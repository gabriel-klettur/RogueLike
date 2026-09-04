using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Tests for OfflineDialogueProvider — the offline fallback that cycles
    /// through NPCPersonaDefinition.dialogueLines deterministically.
    /// </summary>
    public class OfflineDialogueProviderTests
    {
        private NPCPersonaDefinition _persona;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            // ScriptableObject must be created via CreateInstance in EditMode tests.
            _persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            _persona.personaId = "test-persona";
            _persona.displayName = "Test NPC";
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_persona != null)
                Object.DestroyImmediate(_persona);
        }

        // ── helper ────────────────────────────────────────────────────────────

        /// <summary>
        /// Calls GenerateReplyAsync synchronously and returns the result string.
        /// CancellationToken.None is safe here because OfflineDialogueProvider
        /// completes synchronously (Task.FromResult).
        /// </summary>
        private static string Ask(IChatProvider provider, NPCPersonaDefinition persona)
        {
            var task = provider.GenerateReplyAsync(
                new ChatRequest(persona, null, "test"), CancellationToken.None);
            task.Wait();
            return task.Result.Text;
        }

        // ── tests ─────────────────────────────────────────────────────────────

        [Test]
        public void GenerateReplyAsync_CyclesThroughDialogueLines()
        {
            // Arrange — 3 dialogue lines; ask 4 times → should wrap around.
            _persona.dialogueLines = new System.Collections.Generic.List<string>
            {
                "line-A", "line-B", "line-C"
            };
            var provider = new OfflineDialogueProvider();

            // Act + Assert
            Assert.AreEqual("line-A", Ask(provider, _persona), "Call 1 should return line-A.");
            Assert.AreEqual("line-B", Ask(provider, _persona), "Call 2 should return line-B.");
            Assert.AreEqual("line-C", Ask(provider, _persona), "Call 3 should return line-C.");
            Assert.AreEqual("line-A", Ask(provider, _persona), "Call 4 should wrap around to line-A.");
        }

        [Test]
        public void GenerateReplyAsync_EmptyDialogueLines_ReturnsFallback()
        {
            // Arrange — empty list, provider must not throw and must return a
            // non-null, non-empty fallback string ("..." per implementation).
            _persona.dialogueLines = new System.Collections.Generic.List<string>();
            var provider = new OfflineDialogueProvider();

            // Act
            string reply = Ask(provider, _persona);

            // Assert
            Assert.IsNotNull(reply, "Reply must not be null even when dialogueLines is empty.");
            Assert.IsNotEmpty(reply, "Reply must not be empty even when dialogueLines is empty.");

            // The documented fallback is "..."
            Assert.AreEqual("...", reply,
                "Fallback reply for empty dialogueLines should be '...' per OfflineDialogueProvider spec.");
        }
    }
}
