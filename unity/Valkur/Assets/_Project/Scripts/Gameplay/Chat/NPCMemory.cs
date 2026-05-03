using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Persistent, per-NPC memory record.
    ///
    /// Maps to Python's <c>data/chat/memories/{npc-key}/memory.json</c> schema:
    ///   - friendship_score  (-100..100, reserved for future use)
    ///   - ephemeral_history (last N messages, capped by EPHEMERAL_CAP)
    ///   - preferred_language ("es" | "en")
    ///   - has_greeted
    ///   - visit_count
    ///
    /// JsonUtility serialisation requirements:
    ///   - Class must be [Serializable].
    ///   - All fields must be public (JsonUtility ignores properties).
    ///   - Nested types that appear in List&lt;T&gt; must also be [Serializable].
    ///   - No Dictionary (not supported by JsonUtility).
    ///
    /// IL2CPP note: no reflection on serialised types; no System.Runtime.Serialization.
    /// </summary>
    [Serializable]
    public class NPCMemory
    {
        // ── Schema version ──────────────────────────────────────────────────
        /// <summary>
        /// Bumped whenever the schema changes.  NPCMemoryStore.Migrate() is
        /// responsible for upgrading old records to CURRENT_SCHEMA_VERSION.
        /// </summary>
        public int schemaVersion = 1;

        // ── Identity ────────────────────────────────────────────────────────
        /// <summary>
        /// Stable composite key: "{personaId}-{stableId}".
        /// This is also the basis for the filename on disk (slugified).
        /// </summary>
        public string npcKey;

        /// <summary>
        /// PersonaId copied at creation so it can be re-matched if the key
        /// format ever changes.
        /// </summary>
        public string personaId;

        // ── Progression ─────────────────────────────────────────────────────
        /// <summary>How many times the player has started a conversation.</summary>
        public int visitCount;

        /// <summary>
        /// Whether the NPC has delivered its one-time greeting.
        /// Maps to Python's <c>has_greeted</c>.
        /// </summary>
        public bool hasGreeted;

        /// <summary>
        /// Signed score in [-100, 100].  Reserved for future relationship
        /// mechanics.  Stored but not yet acted upon.
        /// </summary>
        public int friendshipScore;

        // ── Language preference ─────────────────────────────────────────────
        /// <summary>"es" or "en". Defaults to "es".</summary>
        public string preferredLanguage = "es";

        // ── Ephemeral history ───────────────────────────────────────────────
        /// <summary>
        /// Rolling window of the last EPHEMERAL_CAP messages.
        /// Oldest message is dropped when the cap is exceeded.
        /// </summary>
        public List<EphemeralMessage> ephemeralHistory = new List<EphemeralMessage>();

        // ── Debug ───────────────────────────────────────────────────────────
        /// <summary>
        /// ISO-8601 UTC timestamp of the last Save() call.  Human-readable
        /// for debugging purposes; not used by game logic.
        /// </summary>
        public string lastUpdatedIso8601;

        // ── Constants ───────────────────────────────────────────────────────
        /// <summary>Maximum number of messages kept in ephemeralHistory.</summary>
        public const int EPHEMERAL_CAP = 12;
    }

    /// <summary>
    /// A single message in the ephemeral history.
    /// Kept as a struct to avoid heap pressure from small objects in the list.
    ///
    /// JsonUtility serialises public fields on structs marked [Serializable].
    /// </summary>
    [Serializable]
    public struct EphemeralMessage
    {
        /// <summary>"user" or "assistant".</summary>
        public string role;

        /// <summary>The message text.</summary>
        public string content;

        /// <summary>ISO-8601 UTC timestamp of when this message was added.</summary>
        public string timestampIso8601;
    }
}
