using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Persistent, per-NPC memory record.
    ///
    /// Maps to Python's <c>data/chat/memories/{npc-key}/memory.json</c> schema:
    ///   - friendship_score  (-100..100)
    ///   - ephemeral_history (last N messages, capped by EPHEMERAL_CAP)
    ///   - preferred_language ("es" | "en")
    ///   - has_greeted        (v1 only — superseded by lastGreetedDayKey)
    ///   - visit_count
    ///
    /// JsonUtility serialisation requirements:
    ///   - Class must be [Serializable].
    ///   - Fields must be public, or private and [SerializeField]; properties are ignored.
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
        public int schemaVersion = 3;

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
        /// The day this character last greeted the player, as
        /// <see cref="ChatDayClock.TodayKey"/> wrote it. Empty means never.
        ///
        /// <para>A greeting used to be once per LIFETIME (the v1 <c>hasGreeted</c> bit), so
        /// from the second visit on the panel opened in silence with an old transcript in
        /// it and nothing acknowledging that the player had walked up. Once a day is the
        /// behaviour a neighbour has: pleased to see you this morning, not startled to see
        /// you again after lunch.</para>
        /// </summary>
        public string lastGreetedDayKey;

        /// <summary>
        /// The day whose journal page this character's conversation is currently being
        /// written to. Empty means the journal has never opened a page for them.
        ///
        /// <para>It is what makes the day boundary detectable OFFLINE. Nothing sweeps every
        /// character at midnight — a memory record is only ever loaded when its character is
        /// spoken to — so "the day turned over while you were away" has to be answerable from
        /// the record itself, by comparing this against <see cref="ChatDayClock.TodayKey"/>
        /// the moment the conversation opens.</para>
        ///
        /// <para>Deliberately NOT merged with <see cref="lastGreetedDayKey"/>, which looks
        /// like the same fact and is not: a greeting is due once per day and is spoken at the
        /// TOP of a conversation, while this tracks which page the conversation is being
        /// APPENDED to and moves again when midnight arrives mid-chat. Folding them would
        /// make one of the two wrong every time they diverge, silently.</para>
        /// </summary>
        public string lastJournalDayKey;

        /// <summary>
        /// Signed score in [-100, 100]. Written by <see cref="ChatRelationship"/> from what
        /// the player says and does, and read by <c>PersonaPromptBuilder</c>, which turns it
        /// into prose rather than handing the model a number.
        /// </summary>
        public int friendshipScore;

        // ── Language preference ─────────────────────────────────────────────
        /// <summary>
        /// "es" or "en". Seeded from <see cref="ChatLanguage.Current"/> every time a
        /// conversation opens — the preference belongs to the player, and this field is
        /// only how it reaches the prompt builder.
        /// </summary>
        public string preferredLanguage = "es";

        // ── Ephemeral history ───────────────────────────────────────────────
        /// <summary>
        /// Rolling window of the last EPHEMERAL_CAP messages.
        /// Oldest message is dropped when the cap is exceeded.
        /// </summary>
        public List<EphemeralMessage> ephemeralHistory = new List<EphemeralMessage>();

        // ── Durable digest ──────────────────────────────────────────────────
        /// <summary>
        /// What survives the rolling window: a handful of facts the player volunteered and
        /// events worth remembering, written by <see cref="ChatMemoryDigest"/>.
        ///
        /// <para>Without it the character forgets literally everything past
        /// <see cref="EPHEMERAL_CAP"/> messages, so "he remembers me" was only ever
        /// <see cref="visitCount"/>. Entries are keyed and deduplicated, so telling someone
        /// your name twice does not spend two slots.</para>
        /// </summary>
        public List<MemoryNote> digest = new List<MemoryNote>();

        // ── Debug ───────────────────────────────────────────────────────────
        /// <summary>
        /// ISO-8601 UTC timestamp of the last Save() call.  Human-readable
        /// for debugging purposes; not used by game logic.
        /// </summary>
        public string lastUpdatedIso8601;

        // ── Constants ───────────────────────────────────────────────────────
        /// <summary>Maximum number of messages kept in ephemeralHistory.</summary>
        public const int EPHEMERAL_CAP = 12;

        /// <summary>
        /// Maximum number of durable notes kept. Small on purpose: every one of them is
        /// billed on every message once it reaches the prompt, and a character who recites
        /// twenty remembered facts reads as a database rather than as someone who knows you.
        /// </summary>
        public const int DIGEST_CAP = 8;
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

    /// <summary>
    /// One durable thing this character knows about the player.
    ///
    /// <para><see cref="key"/> is symbolic ("name", "origin", "hates", "trade:bread_01")
    /// and is what makes the entry replaceable; <see cref="value"/> is the captured text.
    /// The pair is stored rather than a finished sentence so the note can be rendered in
    /// whichever language the conversation is being held in — a digest written as prose
    /// would be frozen in the language the player happened to be using that day.</para>
    /// </summary>
    [Serializable]
    public struct MemoryNote
    {
        /// <summary>Symbolic kind. Two notes with the same key are the same fact.</summary>
        public string key;

        /// <summary>What was captured, in the player's own words where there were any.</summary>
        public string value;

        /// <summary>ISO-8601 UTC timestamp of when this note was last written.</summary>
        public string timestampIso8601;
    }
}
