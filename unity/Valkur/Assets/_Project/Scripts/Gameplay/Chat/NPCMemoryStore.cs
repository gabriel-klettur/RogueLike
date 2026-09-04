using System;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Persistence layer for per-NPC memory records.
    ///
    /// Maps to Python's per-NPC <c>memory.json</c> files under
    /// <c>data/chat/memories/{npc-key}/</c>.
    ///
    /// Atomic write strategy (prevents corruption on crash):
    ///   1. Serialize to <c>{path}.tmp</c>.
    ///   2. If <c>{path}</c> exists: <c>File.Replace(tmp, path, path.bak)</c>.
    ///   3. If <c>{path}</c> is new: <c>File.Move(tmp, path)</c>.
    ///
    /// Recovery: LoadOrCreate attempts <c>{path}.bak</c> if the primary file
    /// fails to parse.  Corrupted primaries are renamed to <c>{path}.corrupt</c>
    /// so the player doesn't lose data silently.
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
        public const int CURRENT_SCHEMA_VERSION = 1;

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

            string path = ChatPersistencePaths.MemoryPath(npcKey);
            string backupPath = path + ".bak";

            if (File.Exists(path))
            {
                NPCMemory loaded = TryLoad(path);
                if (loaded != null)
                {
                    MigrateIfNeeded(loaded);
                    return loaded;
                }

                // Primary is corrupt — try backup
                Debug.LogWarning($"[NPCMemoryStore] Primary memory file corrupt for '{npcKey}'. Attempting backup.");
                SafeMarkCorrupt(path);

                if (File.Exists(backupPath))
                {
                    NPCMemory fromBackup = TryLoad(backupPath);
                    if (fromBackup != null)
                    {
                        Debug.LogWarning($"[NPCMemoryStore] Recovered from backup for '{npcKey}'.");
                        MigrateIfNeeded(fromBackup);
                        return fromBackup;
                    }
                    Debug.LogWarning($"[NPCMemoryStore] Backup also corrupt for '{npcKey}'. Starting fresh.");
                }
            }

            return CreateFresh(npcKey, personaId);
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

            string path = ChatPersistencePaths.MemoryPath(memory.npcKey);
            string tmpPath = path + ".tmp";
            string backupPath = path + ".bak";

            try
            {
                string json = JsonUtility.ToJson(memory, prettyPrint: true);
                File.WriteAllText(tmpPath, json);

                if (File.Exists(path))
                {
                    // Atomic replace — on Windows this is a single OS call.
                    File.Replace(tmpPath, path, backupPath);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NPCMemoryStore] Save failed for '{memory.npcKey}': {ex.Message}");

                // Best-effort cleanup of stale .tmp
                SafeDelete(tmpPath);
            }
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
        public static bool Delete(string npcKey)
        {
            string path = ChatPersistencePaths.MemoryPath(npcKey);
            bool existed = File.Exists(path);

            SafeDelete(path);
            SafeDelete(path + ".bak");
            SafeDelete(path + ".tmp");
            SafeDelete(path + ".corrupt");

            return existed;
        }

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

        private static NPCMemory TryLoad(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                // JsonUtility.FromJson throws ArgumentException on malformed JSON
                // and returns a default instance on empty strings — guard both.
                if (string.IsNullOrWhiteSpace(json)) return null;
                var mem = JsonUtility.FromJson<NPCMemory>(json);
                return mem;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NPCMemoryStore] Failed to parse '{path}': {ex.Message}");
                return null;
            }
        }

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
            // Example future migration:
            // if (mem.schemaVersion == 1) { /* add new fields */ mem.schemaVersion = 2; }

            mem.schemaVersion = CURRENT_SCHEMA_VERSION;
            Debug.Log($"[NPCMemoryStore] Migrated '{mem.npcKey}' to schema v{CURRENT_SCHEMA_VERSION}.");
        }

        private static void EnsureMemoryDirectory()
        {
            string dir = ChatPersistencePaths.MemoryDirectory;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void SafeMarkCorrupt(string path)
        {
            try
            {
                string corruptPath = path + ".corrupt";
                if (File.Exists(corruptPath)) File.Delete(corruptPath);
                File.Move(path, corruptPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NPCMemoryStore] Could not rename corrupt file '{path}': {ex.Message}");
            }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }

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
