using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Centralises all persistence-path logic for the Chat subsystem.
    ///
    /// Both NPCMemoryStore and ChatSessionLogger delegate here so the slug
    /// algorithm and OverrideRoot live in exactly one place.
    ///
    /// OverrideRoot: test assemblies set this to a temp directory so that
    /// neither Application.persistentDataPath nor real disk state is touched
    /// during unit tests.
    ///
    /// Domain Reload is OFF in this project. The static field is reset via
    /// [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] so it does not
    /// bleed across Play sessions.
    /// </summary>
    public static class ChatPersistencePaths
    {
        // ─── Override root (tests only) ────────────────────────────────────────

        /// <summary>
        /// When set, replaces <c>Application.persistentDataPath</c> as the
        /// root of every path this helper produces.
        /// Set to <c>null</c> to restore the default Unity path.
        /// </summary>
        internal static string OverrideRoot { get; set; }

        // ─── Computed roots ───────────────────────────────────────────────────

        /// <summary>
        /// Effective filesystem root.  Tests override this; production uses
        /// <c>Application.persistentDataPath</c>.
        /// </summary>
        public static string Root => string.IsNullOrEmpty(OverrideRoot)
            ? Application.persistentDataPath
            : OverrideRoot;

        /// <summary>
        /// <c>{Root}/chat/memories</c> — one JSON file per NPC.
        /// Created on first access by the store.
        /// </summary>
        public static string MemoryDirectory => Path.Combine(Root, "chat", "memories");

        /// <summary>
        /// <c>{Root}/logs/chat_sessions</c> — one log file per conversation.
        /// Created on first access by the logger.
        /// </summary>
        public static string LogDirectory => Path.Combine(Root, "logs", "chat_sessions");

        /// <summary>
        /// <c>{Root}/chat/journals</c> — one SUBDIRECTORY per NPC, holding one file per day.
        ///
        /// <para>Separate from <see cref="MemoryDirectory"/> because the two answer different
        /// questions and have opposite lifetimes. A memory record is one small document that
        /// is rewritten constantly and forgets by design; a journal is an unbounded shelf of
        /// documents that are written once and then never change. Filing them together would
        /// put a year of immutable pages in the directory that a memory load enumerates.</para>
        /// </summary>
        public static string JournalDirectory => Path.Combine(Root, "chat", "journals");

        // ─── Path helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the absolute path of <paramref name="npcKey"/>'s memory file.
        /// Does NOT create directories or the file itself.
        /// </summary>
        public static string MemoryPath(string npcKey) =>
            Path.Combine(MemoryDirectory, Slugify(npcKey) + ".json");

        /// <summary>
        /// The directory holding every journal page for <paramref name="npcKey"/>.
        /// Does NOT create it.
        /// </summary>
        public static string JournalDirectoryFor(string npcKey) =>
            Path.Combine(JournalDirectory, Slugify(npcKey));

        /// <summary>
        /// The absolute path of one journal page, addressed by the stem
        /// <see cref="ChatJournalPageRef"/> derived from its day key.
        /// </summary>
        public static string JournalPagePath(string npcKey, string pageStem) =>
            Path.Combine(JournalDirectoryFor(npcKey), pageStem + ".json");

        /// <summary>The extension every journal page carries. Used to filter the directory.</summary>
        public const string JournalPageExtension = ".json";

        /// <summary>
        /// Returns the absolute path for a new chat-session log file with a
        /// filesystem-safe timestamp.
        /// </summary>
        public static string SessionLogPath(string npcKey, string role)
        {
            string ts = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"chat_session_{Slugify(role)}_{Slugify(npcKey)}_{ts}.log";
            return Path.Combine(LogDirectory, filename);
        }

        // ─── Slug ─────────────────────────────────────────────────────────────

        private const int SlugMaxLength = 80;

        // Characters that are illegal on at least one major filesystem.
        private static readonly Regex _illegalChars =
            new Regex(@"[<>:""/\\|?*\s]+", RegexOptions.Compiled);

        /// <summary>
        /// Converts an arbitrary string to a lowercase, filesystem-safe slug.
        /// Illegal characters and whitespace are replaced with <c>_</c>;
        /// the result is capped at 80 characters.
        /// </summary>
        public static string Slugify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "_empty_";
            string slug = _illegalChars.Replace(raw.ToLowerInvariant(), "_");
            if (slug.Length > SlugMaxLength)
                slug = slug.Substring(0, SlugMaxLength);
            return slug;
        }

        // ─── Domain-Reload reset ──────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOverride()
        {
            OverrideRoot = null;
        }
    }
}
