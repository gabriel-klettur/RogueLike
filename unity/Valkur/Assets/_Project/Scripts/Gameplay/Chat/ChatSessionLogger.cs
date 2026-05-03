using System;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Logs chat session messages to a per-conversation plain-text file.
    ///
    /// Maps to Python's <c>logs/chat_sessions/chat_session_{role}_{npc}_{ts}.log</c>
    /// files written by the chat router.
    ///
    /// Thread safety: Unity is single-threaded but a lock is held around all
    /// file operations so that future async code (e.g. LLM worker) can safely
    /// call LogLine() from a background thread.
    ///
    /// Domain Reload is OFF.  Static mutable state (_writer, _activeSessionPath)
    /// is reset via [RuntimeInitializeOnLoadMethod(SubsystemRegistration)].
    ///
    /// Path and slug logic delegates to ChatPersistencePaths.
    /// </summary>
    public static class ChatSessionLogger
    {
        // ─── State ────────────────────────────────────────────────────────────

        private static StreamWriter _writer;
        private static string _activeSessionPath;
        private static readonly object _lock = new object();

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>True while a session log file is open.</summary>
        public static bool IsSessionActive
        {
            get { lock (_lock) return _writer != null; }
        }

        /// <summary>
        /// Absolute path of the currently open log file.
        /// Null if no session is active.
        /// </summary>
        public static string ActiveSessionPath
        {
            get { lock (_lock) return _activeSessionPath; }
        }

        /// <summary>
        /// Opens a new session log file for <paramref name="npcKey"/> /
        /// <paramref name="role"/>.  If a session is already active, it is
        /// closed first — streams are never accumulated.
        ///
        /// Returns the absolute path of the newly created file, or null on
        /// failure (error is logged to Unity console).
        /// </summary>
        public static string OpenSession(string npcKey, string role)
        {
            lock (_lock)
            {
                CloseSessionInternal();

                EnsureLogDirectory();

                string path = ChatPersistencePaths.SessionLogPath(npcKey, role);

                try
                {
                    _writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    _activeSessionPath = path;

                    // Write a header line so the file is never empty
                    _writer.WriteLine($"# Chat session — npc={npcKey} role={role}");
                    _writer.WriteLine($"# Opened: {DateTime.UtcNow:o}");
                    _writer.WriteLine();

                    Debug.Log($"[ChatSessionLogger] Session opened: {path}");
                    return path;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChatSessionLogger] Could not open session log at '{path}': {ex.Message}");
                    _writer = null;
                    _activeSessionPath = null;
                    return null;
                }
            }
        }

        /// <summary>
        /// Writes a single line to the active log in the format
        /// <c>[{utc-iso8601}] {sender}: {text}</c>.
        ///
        /// No-op (with a warning) if no session is currently open.
        /// </summary>
        public static void LogLine(string sender, string text)
        {
            lock (_lock)
            {
                if (_writer == null)
                {
                    Debug.LogWarning("[ChatSessionLogger] LogLine called but no session is open.");
                    return;
                }

                try
                {
                    _writer.WriteLine($"[{DateTime.UtcNow:o}] {sender}: {text}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChatSessionLogger] LogLine failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Flushes and closes the active session log.
        /// Safe to call when no session is open (no-op).
        /// </summary>
        public static void CloseSession()
        {
            lock (_lock)
            {
                CloseSessionInternal();
            }
        }

        /// <summary>
        /// Returns the directory where session log files are stored.
        /// Creates it if it does not exist.
        /// </summary>
        public static string GetLogDirectory()
        {
            EnsureLogDirectory();
            return ChatPersistencePaths.LogDirectory;
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>Must be called while holding <c>_lock</c>.</summary>
        private static void CloseSessionInternal()
        {
            if (_writer == null) return;

            try
            {
                _writer.WriteLine();
                _writer.WriteLine($"# Closed: {DateTime.UtcNow:o}");
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatSessionLogger] Error while closing session: {ex.Message}");
            }
            finally
            {
                _writer = null;
                _activeSessionPath = null;
            }
        }

        private static void EnsureLogDirectory()
        {
            string dir = ChatPersistencePaths.LogDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ─── Domain-Reload reset ──────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lock (_lock)
            {
                // Dispose cleanly without writing the footer — domain reload
                // tears down the AppDomain so file handles must be released.
                try { _writer?.Dispose(); } catch { /* ignore */ }
                _writer = null;
                _activeSessionPath = null;
            }
        }
    }
}
