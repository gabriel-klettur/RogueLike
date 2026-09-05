using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// One day of conversation with one character, as it is kept on disk.
    ///
    /// <para>WHY A PAGE PER DAY AND A FILE PER PAGE. Retention here is unbounded — a save
    /// that has run for a year holds a year of pages — so the two obvious layouts both fail
    /// at the far end. One document per NPC has to be rewritten in full on every line the
    /// player types, which is a cost that grows with how long they have played; one document
    /// for everything adds a lock nobody needs. A file per page means the only thing ever
    /// rewritten is TODAY, whose size is bounded by how much a person types in a day, and
    /// yesterday is immutable the moment it is sealed.</para>
    ///
    /// <para>THERE IS NO INDEX FILE, on purpose. An index is a cache of what the directory
    /// already knows, and a cache of the filesystem is a thing that can disagree with it —
    /// silently, since nothing throws when a page exists and the index has never heard of it.
    /// The day list is built from the FILE NAMES instead (see
    /// <see cref="ChatJournalPageRef"/>), which cannot desynchronise because there is only
    /// one copy of the fact.</para>
    ///
    /// <para>JsonUtility rules apply, the same ones <see cref="NPCMemory"/> records: the type
    /// is <c>[Serializable]</c>, fields are public, properties are ignored, nested types in a
    /// <c>List&lt;T&gt;</c> are serialisable too, and there is no Dictionary anywhere.</para>
    /// </summary>
    [Serializable]
    public class ChatJournalPage
    {
        /// <summary>
        /// Bumped whenever this layout changes.
        /// <see cref="ChatJournalStore"/> owns the migration.
        /// </summary>
        public int schemaVersion = 1;

        // ── Identity ────────────────────────────────────────────────────────

        /// <summary>The composite key from <see cref="ChatDayClock"/>, e.g. <c>2026-09-05#3</c>.</summary>
        public string dayKey;

        /// <summary>
        /// Local calendar date, <c>yyyy-MM-dd</c>. Stored separately from
        /// <see cref="dayKey"/> so the viewer can label a page without re-parsing a key whose
        /// format is <see cref="ChatDayClock"/>'s business rather than the journal's.
        /// </summary>
        public string calendarDate;

        /// <summary>
        /// The in-game day this page was opened on, or 0 when no day/night cycle was running.
        ///
        /// <para>It is NOT an ordinal and must never be sorted on alone: the cycle does not
        /// persist its count, so it restarts at 0 on every Play and legitimately goes
        /// backwards between sessions. It is a label, and the calendar date is what orders
        /// the shelf.</para>
        /// </summary>
        public int inGameDay;

        /// <summary>The <c>{personaId}-{displayName}</c> key the memory record is filed under.</summary>
        public string npcKey;

        /// <summary>Persona id, copied so a page can be re-matched if the key format changes.</summary>
        public string personaId;

        /// <summary>
        /// What to call this character at the top of the page. Captured per page rather than
        /// resolved at read time, because a page is a record of a conversation that happened
        /// and renaming a character does not rewrite yesterday.
        /// </summary>
        public string displayName;

        // ── Timeline ────────────────────────────────────────────────────────

        /// <summary>ISO-8601 UTC of the first line written to this page.</summary>
        public string openedIso8601;

        /// <summary>ISO-8601 UTC of the last line written to this page.</summary>
        public string lastWrittenIso8601;

        /// <summary>
        /// ISO-8601 UTC of the moment the day rolled over and this page stopped accepting
        /// lines. Empty while the page is today's.
        ///
        /// <para>A page can be sealed and then WRITTEN AGAIN, and that is not a bug: the
        /// in-game half of a day key goes backwards across a Play-mode restart, so the same
        /// calendar day can be left and re-entered. Re-entering clears the seal and appends,
        /// which is why sealing is a timestamp and not a one-way door.</para>
        /// </summary>
        public string sealedIso8601;

        /// <summary>How many conversations were opened with this character on this day.</summary>
        public int conversations;

        // ── Content ─────────────────────────────────────────────────────────

        /// <summary>Everything said, in order, verbatim.</summary>
        public List<ChatJournalEntry> entries = new List<ChatJournalEntry>();

        // ── Derived ─────────────────────────────────────────────────────────

        /// <summary>True once the day has rolled over and this page is history.</summary>
        public bool IsSealed => !string.IsNullOrEmpty(sealedIso8601);

        /// <summary>Nothing was ever said on this day. Such a page is never written.</summary>
        public bool IsEmpty => entries == null || entries.Count == 0;
    }

    /// <summary>
    /// One line of a journal page.
    ///
    /// <para>A struct for the same reason <see cref="EphemeralMessage"/> is one: a day's page
    /// holds hundreds of these and they are never referenced individually.</para>
    /// </summary>
    [Serializable]
    public struct ChatJournalEntry
    {
        /// <summary>
        /// <c>"user"</c>, <c>"assistant"</c> or <c>"system"</c> — the same vocabulary
        /// <see cref="EphemeralMessage.role"/> uses, so a page and a memory record describe
        /// the same conversation in the same terms.
        ///
        /// <para><c>"system"</c> is the journal's own addition: a completed trade or a day
        /// boundary is part of what happened and is not something either party SAID.</para>
        /// </summary>
        public string role;

        /// <summary>
        /// Who is speaking, as it should be shown. Stored rather than derived from
        /// <see cref="role"/> so a page reads correctly after the character is renamed and
        /// without the viewer needing the persona at all.
        /// </summary>
        public string speaker;

        /// <summary>What was said.</summary>
        public string text;

        /// <summary>ISO-8601 UTC of when it was said.</summary>
        public string timestampIso8601;

        /// <summary>Role constant for a line the player typed.</summary>
        public const string ROLE_PLAYER = "user";

        /// <summary>Role constant for a line the character spoke.</summary>
        public const string ROLE_NPC = "assistant";

        /// <summary>Role constant for something the game noted rather than anybody saying it.</summary>
        public const string ROLE_SYSTEM = "system";
    }
}
