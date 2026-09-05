using System;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Persistence layer for per-NPC memory records.
    ///
    /// Maps to Python's per-NPC <c>memory.json</c> files under
    /// <c>data/chat/memories/{npc-key}/</c>.
    ///
    /// <para>The atomic write, the <c>.bak</c> recovery and the quarantine of a corrupt
    /// primary all live in <see cref="ChatJsonFile"/> now, and are shared with
    /// <see cref="ChatJournalStore"/>. They used to be spelled out here, which was fine
    /// while this was the only store in the subsystem and would have become two
    /// implementations of the same three lines the moment the journal arrived.</para>
    ///
    /// Domain Reload is OFF.  Static state is reset via
    /// [RuntimeInitializeOnLoadMethod(SubsystemRegistration)].
    ///
    /// All path and slug logic lives in ChatPersistencePaths.
    /// </summary>
    public static class NPCMemoryStore
    {
        // ─── Schema ───────────────────────────────────────────────────────────

        /// <summary>
        /// Current schema version.  Increment here and add a migration branch
        /// in Migrate() whenever the NPCMemory layout changes.
        /// </summary>
        /// <summary>
        /// v2 replaced the lifetime <c>hasGreeted</c> bit with
        /// <c>lastGreetedDayKey</c> (a greeting is due once a DAY) and added the durable
        /// <c>digest</c>. v3 added <c>lastJournalDayKey</c>, which is how a conversation
        /// notices that the day turned over while the player was elsewhere.
        /// </summary>
        public const int CURRENT_SCHEMA_VERSION = 3;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the NPCMemory for <paramref name="npcKey"/>, loading from
        /// disk if a file exists.  Never returns null.
        ///
        /// Recovery order on parse failure:
        ///   primary (.json) → backup (.bak) → fresh NPCMemory
        /// </summary>
        public static NPCMemory LoadOrCreate(string npcKey, string personaId)
        {
            EnsureMemoryDirectory();

            NPCMemory loaded = ChatJsonFile.ReadOrRecover<NPCMemory>(
                ChatPersistencePaths.MemoryPath(npcKey), $"memory of '{npcKey}'");

            if (loaded == null) return CreateFresh(npcKey, personaId);

            MigrateIfNeeded(loaded);
            return loaded;
        }

        /// <summary>
        /// Persists <paramref name="memory"/> to disk using an atomic
        /// write (tmp → replace → optional backup).
        /// </summary>
        public static void Save(NPCMemory memory)
        {
            if (memory == null)
            {
                Debug.LogError("[NPCMemoryStore] Save called with null NPCMemory.");
                return;
            }

            EnsureMemoryDirectory();

            memory.lastUpdatedIso8601 = DateTime.UtcNow.ToString("o");

            ChatJsonFile.WriteAtomic(
                ChatPersistencePaths.MemoryPath(memory.npcKey), memory,
                $"memory of '{memory.npcKey}'");
        }

        /// <summary>
        /// Erases everything remembered about <paramref name="npcKey"/> — the record, its
        /// backup and any quarantined copy — and reports whether anything was there.
        ///
        /// <para>Exists for the Reset button in the chat panel, which is a TESTING control:
        /// a conversation is the one system here whose behaviour depends on its own history,
        /// so trying a change means meeting the character for the first time again, and
        /// without this that means hunting down a file under
        /// <c>Application.persistentDataPath</c> between runs.</para>
        ///
        /// <para>The backup is deleted too, and deliberately. Leaving it would let the
        /// recovery path in <see cref="LoadOrCreate"/> resurrect the conversation the moment
        /// the next write failed — a reset that quietly un-resets itself later is worse than
        /// no reset at all.</para>
        /// </summary>
        public static bool Delete(string npcKey) =>
            ChatJsonFile.Delete(ChatPersistencePaths.MemoryPath(npcKey));

        /// <summary>
        /// Appends a message to <paramref name="memory"/>'s ephemeral history,
        /// dropping the oldest entry when the cap is exceeded.
        /// Does NOT call Save(); the caller decides when to persist.
        /// </summary>
        public static void AppendEphemeral(NPCMemory memory, string role, string content)
        {
            if (memory == null)
            {
                Debug.LogError("[NPCMemoryStore] AppendEphemeral called with null NPCMemory.");
                return;
            }

            var msg = new EphemeralMessage
            {
                role = role,
                content = content,
                timestampIso8601 = DateTime.UtcNow.ToString("o")
            };

            memory.ephemeralHistory.Add(msg);

            while (memory.ephemeralHistory.Count > NPCMemory.EPHEMERAL_CAP)
                memory.ephemeralHistory.RemoveAt(0);
        }

        // ─── Path helpers (delegate to ChatPersistencePaths) ─────────────────

        /// <summary>
        /// Returns the directory where memory files are stored.
        /// Created on first call if it does not exist.
        /// </summary>
        public static string GetMemoryDirectory()
        {
            EnsureMemoryDirectory();
            return ChatPersistencePaths.MemoryDirectory;
        }

        /// <summary>Returns the absolute path of <paramref name="npcKey"/>'s memory file.</summary>
        public static string GetMemoryPath(string npcKey) =>
            ChatPersistencePaths.MemoryPath(npcKey);

        /// <summary>Converts an arbitrary string to a filesystem-safe lowercase slug.</summary>
        public static string Slugify(string raw) =>
            ChatPersistencePaths.Slugify(raw);

        // ─── Private helpers ──────────────────────────────────────────────────

        private static NPCMemory CreateFresh(string npcKey, string personaId)
        {
            return new NPCMemory
            {
                schemaVersion = CURRENT_SCHEMA_VERSION,
                npcKey = npcKey,
                personaId = personaId,
                preferredLanguage = "es"
            };
        }

        private static void MigrateIfNeeded(NPCMemory mem)
        {
            if (mem.schemaVersion < CURRENT_SCHEMA_VERSION)
                Migrate(mem);
        }

        /// <summary>
        /// Stub migration: bump schemaVersion to current.
        /// Add explicit migration branches here as the schema evolves.
        /// </summary>
        private static void Migrate(NPCMemory mem)
        {
            if (mem.schemaVersion < 3)
            {
                // A record written before the journal existed has a verbatim window that no
                // page ever recorded. An EMPTY key means "no page yet", which makes the first
                // conversation adopt today without sealing anything — the alternative,
                // stamping some past day on it, would wipe that window into an archive that
                // does not contain it. See ChatJournal.SealPreviousDay.
                mem.lastJournalDayKey = "";
            }

            if (mem.schemaVersion < 2)
            {
                // A v1 record only knew whether the greeting had EVER been said. Stamping
                // today's key on it would suppress today's greeting for a character who
                // last said hello months ago; an empty stamp greets once, now, and the
                // daily rhythm starts from there. A record that never greeted keeps the
                // same empty stamp and is indistinguishable, which is correct — both are
                // "greet on the next conversation".
                mem.lastGreetedDayKey = "";
                if (mem.digest == null) mem.digest = new System.Collections.Generic.List<MemoryNote>();
            }

            mem.schemaVersion = CURRENT_SCHEMA_VERSION;
            Debug.Log($"[NPCMemoryStore] Migrated '{mem.npcKey}' to schema v{CURRENT_SCHEMA_VERSION}.");
        }

        private static void EnsureMemoryDirectory() =>
            ChatJsonFile.EnsureDirectory(ChatPersistencePaths.MemoryDirectory);

        // ─── Domain-Reload reset ──────────────────────────────────────────────

        // NPCMemoryStore has no static mutable state beyond what ChatPersistencePaths
        // already resets (OverrideRoot).  This method is a no-op placeholder so
        // that if state is added later the pattern is already in place.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Nothing to reset here; ChatPersistencePaths.ResetOverride() handles OverrideRoot.
        }
    }
}
