using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Tests for ChatSessionLogger — per-conversation file logger.
    ///
    /// All I/O is redirected to a temp directory via ChatPersistencePaths.OverrideRoot
    /// so that Application.persistentDataPath is never touched.
    /// </summary>
    public class ChatSessionLoggerTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(
                Path.GetTempPath(),
                "valkur_test_sessionlog_" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Always close any open session to release file handles before cleanup.
            ChatSessionLogger.CloseSession();

            ChatPersistencePaths.OverrideRoot = null;

            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best-effort; OS will clean up temp eventually.
            }
        }

        // ── tests ─────────────────────────────────────────────────────────────

        [Test]
        public void OpenSession_CreatesFile_WithExpectedNamePattern()
        {
            // Act
            string path = ChatSessionLogger.OpenSession("npc-key-test", "vendor");

            // Assert — file must exist.
            Assert.IsNotNull(path, "OpenSession must return a non-null path on success.");
            Assert.IsTrue(File.Exists(path),
                $"Log file must exist at the returned path: {path}");

            // Assert — filename must match the pattern: chat_session_*.log
            string filename = Path.GetFileName(path);
            StringAssert.StartsWith("chat_session_", filename,
                "Log filename must begin with 'chat_session_'.");
            StringAssert.EndsWith(".log", filename,
                "Log filename must end with '.log'.");
        }

        [Test]
        public void LogLine_AppendsExpectedFormat()
        {
            // Arrange
            ChatSessionLogger.OpenSession("format-test-npc", "generic");

            // Act
            ChatSessionLogger.LogLine("player", "Hello world!");

            // Flush + close so we can read the file.
            ChatSessionLogger.CloseSession();

            // Assert — find the session log file.
            string logDir = ChatSessionLogger.GetLogDirectory();
            string[] files = Directory.GetFiles(logDir, "chat_session_*.log");
            Assert.AreEqual(1, files.Length,
                "Exactly one session log file should exist after one session.");

            string content = File.ReadAllText(files[0]);

            // The line format is: [ISO8601] sender: text
            // We can't predict the exact timestamp, so we look for the structure.
            StringAssert.Contains("player: Hello world!", content,
                "Log content must contain 'sender: text' portion of the log line.");

            // Verify that a leading '[' is present (ISO8601 timestamp bracket).
            StringAssert.Contains("[", content,
                "Log content must contain an ISO8601 timestamp bracket '['.");
        }

        [Test]
        public void OpenSession_TwiceInARow_ClosesPrevious()
        {
            // Arrange — open first session.
            string pathA = ChatSessionLogger.OpenSession("npc-a", "vendor");
            Assert.IsTrue(ChatSessionLogger.IsSessionActive,
                "Session should be active after first OpenSession.");
            Assert.AreEqual(pathA, ChatSessionLogger.ActiveSessionPath,
                "ActiveSessionPath should match the first opened session.");

            // Act — open a second session while first is still active.
            string pathB = ChatSessionLogger.OpenSession("npc-b", "generic");

            // Assert — only the second session should be active.
            Assert.IsTrue(ChatSessionLogger.IsSessionActive,
                "Session should still be active after second OpenSession.");
            Assert.AreEqual(pathB, ChatSessionLogger.ActiveSessionPath,
                "ActiveSessionPath must switch to the second session path.");
            Assert.AreNotEqual(pathA, pathB,
                "The two sessions must produce different log paths.");

            // Assert — the first file must exist (it was closed cleanly, not deleted).
            Assert.IsTrue(File.Exists(pathA),
                "First session log file must still exist after it was implicitly closed.");
        }
    }
}
