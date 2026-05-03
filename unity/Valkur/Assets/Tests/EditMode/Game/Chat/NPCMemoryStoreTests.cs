using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Tests for NPCMemoryStore and the ChatPersistencePaths.OverrideRoot mechanism.
    ///
    /// Each test redirects disk I/O to a temporary directory so that
    /// Application.persistentDataPath is never touched and tests are fully isolated.
    /// </summary>
    public class NPCMemoryStoreTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            // Route all ChatPersistencePaths to a dedicated temp folder.
            _testRoot = Path.Combine(
                Path.GetTempPath(),
                "valkur_test_chat_" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;

            // Suppress any incidental Unity log warnings from the static systems.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Reset override so subsequent tests / prod sessions use the real path.
            ChatPersistencePaths.OverrideRoot = null;

            // Best-effort cleanup of temp directory.
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Ignore — the OS will clean up temp eventually.
            }
        }

        // ── tests ─────────────────────────────────────────────────────────────

        [Test]
        public void LoadOrCreate_NewNpc_ReturnsDefaults()
        {
            // Act — no file exists yet, so defaults should be returned.
            var mem = NPCMemoryStore.LoadOrCreate("new-npc", "persona-a");

            // Assert
            Assert.IsNotNull(mem, "LoadOrCreate must never return null.");
            Assert.AreEqual(0, mem.visitCount, "visitCount should default to 0.");
            Assert.IsFalse(mem.hasGreeted, "hasGreeted should default to false.");
            Assert.AreEqual("es", mem.preferredLanguage,
                "preferredLanguage should default to 'es'.");
            Assert.IsNotNull(mem.ephemeralHistory,
                "ephemeralHistory list must be initialised.");
            Assert.AreEqual(0, mem.ephemeralHistory.Count,
                "ephemeralHistory should be empty for a new NPC.");
        }

        [Test]
        public void Save_ThenLoad_RoundTripsAllFields()
        {
            // Arrange — build a memory with mutated fields.
            var original = NPCMemoryStore.LoadOrCreate("round-trip-npc", "persona-b");
            original.visitCount = 7;
            original.hasGreeted = true;
            original.friendshipScore = 42;
            original.preferredLanguage = "en";
            NPCMemoryStore.AppendEphemeral(original, "user", "Hello!");
            NPCMemoryStore.AppendEphemeral(original, "assistant", "Hi there!");

            // Act
            NPCMemoryStore.Save(original);
            var loaded = NPCMemoryStore.LoadOrCreate("round-trip-npc", "persona-b");

            // Assert — all mutated fields survive the round-trip.
            Assert.AreEqual(7, loaded.visitCount, "visitCount must round-trip.");
            Assert.IsTrue(loaded.hasGreeted, "hasGreeted must round-trip.");
            Assert.AreEqual(42, loaded.friendshipScore, "friendshipScore must round-trip.");
            Assert.AreEqual("en", loaded.preferredLanguage, "preferredLanguage must round-trip.");
            Assert.AreEqual(2, loaded.ephemeralHistory.Count,
                "ephemeralHistory entry count must round-trip.");
            Assert.AreEqual("Hello!", loaded.ephemeralHistory[0].content,
                "First ephemeral message content must round-trip.");
            Assert.AreEqual("Hi there!", loaded.ephemeralHistory[1].content,
                "Second ephemeral message content must round-trip.");
        }

        [Test]
        public void AppendEphemeral_ExceedsCap_DropsOldest()
        {
            // Arrange
            var mem = NPCMemoryStore.LoadOrCreate("cap-test-npc", "persona-c");
            int overCap = NPCMemory.EPHEMERAL_CAP + 3; // 15

            for (int i = 0; i < overCap; i++)
                NPCMemoryStore.AppendEphemeral(mem, "user", $"msg-{i}");

            // Assert — count capped at EPHEMERAL_CAP (12).
            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP, mem.ephemeralHistory.Count,
                $"ephemeralHistory count must not exceed {NPCMemory.EPHEMERAL_CAP}.");

            // The oldest messages (0..2) must have been dropped.
            Assert.AreEqual("msg-3", mem.ephemeralHistory[0].content,
                "The oldest surviving message should be msg-3 after 15 appends with cap=12.");

            // The most recent message must be the last one added.
            Assert.AreEqual($"msg-{overCap - 1}", mem.ephemeralHistory[mem.ephemeralHistory.Count - 1].content,
                "The most recent message must be the last appended.");
        }

        [Test]
        public void Save_AtomicWrite_LeavesNoTmpFile()
        {
            // Arrange
            var mem = NPCMemoryStore.LoadOrCreate("atomic-npc", "persona-d");
            mem.visitCount = 1;

            // Act
            NPCMemoryStore.Save(mem);

            // Assert — the .tmp file must not survive a successful Save().
            string path = NPCMemoryStore.GetMemoryPath("atomic-npc");
            string tmpPath = path + ".tmp";

            Assert.IsTrue(File.Exists(path),
                "The primary .json file must exist after Save().");
            Assert.IsFalse(File.Exists(tmpPath),
                ".tmp file must not exist after a successful atomic Save() — it should have been moved/replaced.");
        }
    }
}
