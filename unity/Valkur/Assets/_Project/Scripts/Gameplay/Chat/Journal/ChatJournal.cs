using System;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The live half of the journal: which page a conversation is writing to, and what
    /// happens to a character's memory when the day underneath it turns over.
    ///
    /// <para>WHY THE JOURNAL IS WRITTEN AS IT HAPPENS. The obvious design is to archive
    /// <c>NPCMemory.ephemeralHistory</c> at the end of the day, and it silently loses most of
    /// the conversation: that window holds twelve messages and drops the oldest, so anything
    /// said earlier is already gone by the time there is a day to seal. The journal therefore
    /// hangs off the same seam every message already passes through and keeps the whole
    /// day.</para>
    ///
    /// <para>WHAT A DAY BOUNDARY DOES, in one place. It seals yesterday's page — which stops
    /// nothing, since a page is appended to and a seal is a timestamp — and it CLEARS the
    /// verbatim window. That second half is the point of the feature: the panel opens on a
    /// blank transcript and the character greets the player again, instead of resuming a
    /// conversation from a day that is over. What deliberately survives is everything that
    /// makes them know you — <c>digest</c>, <c>friendshipScore</c>, <c>visitCount</c> — so
    /// forgetting the words is not forgetting the person.</para>
    ///
    /// <para>WHY IT IS AN OBJECT AND NOT A STATIC. <see cref="ChatSystem"/> holds exactly one
    /// and it lives for exactly one conversation, so the lifetime is already expressed by the
    /// field that holds it. A static would need a Domain-Reload reset hook to say the same
    /// thing less clearly, and would make two conversations at once — which the tests do —
    /// impossible to write.</para>
    /// </summary>
    public sealed class ChatJournal
    {
        private ChatJournalPage _page;
        private string _npcKey;
        private string _personaId;
        private string _displayName;

        /// <summary>True between <see cref="Open"/> and <see cref="Close"/>.</summary>
        public bool IsOpen => _page != null;

        /// <summary>The page being written to, or null when no conversation is open.</summary>
        public ChatJournalPage CurrentPage => _page;

        /// <summary>The day key of the open page, or an empty string.</summary>
        public string CurrentDayKey => _page != null ? _page.dayKey : "";

        /// <summary>Which character's journal this is, or an empty string.</summary>
        public string NpcKey => _npcKey ?? "";

        /// <summary>
        /// Raised when a day boundary sealed a page and cleared the verbatim memory, so the
        /// panel can throw away the transcript it is showing. Carries the day key that was
        /// just sealed.
        ///
        /// <para>An event rather than a return value because the boundary can arrive in the
        /// MIDDLE of a conversation, from the day/night cycle, with nobody calling anything.</para>
        /// </summary>
        public event Action<string> OnDaySealed;

        // ── Lifetime ────────────────────────────────────────────────────────

        /// <summary>
        /// Starts writing this conversation to today's page, sealing yesterday's first if the
        /// day has turned over since the character was last spoken to.
        /// </summary>
        /// <returns>Whether a day was sealed, i.e. whether <paramref name="memory"/> was cleared.</returns>
        public bool Open(string npcKey, string personaId, string displayName, NPCMemory memory)
        {
            Close();

            _npcKey = npcKey;
            _personaId = personaId;
            _displayName = displayName;

            bool sealedADay = SealPreviousDay(memory, ChatDayClock.TodayKey);

            _page = ChatJournalStore.LoadOrCreatePage(npcKey, ChatDayClock.TodayKey, personaId, displayName);
            if (_page == null)
            {
                // A malformed day key. The conversation still runs, is still remembered and
                // is still logged; only the journal sits this one out.
                Debug.LogWarning($"[ChatJournal] No page could be opened for '{npcKey}'.");
                return sealedADay;
            }

            _page.conversations++;
            if (memory != null) memory.lastJournalDayKey = _page.dayKey;

            return sealedADay;
        }

        /// <summary>
        /// Checks whether the day has turned over UNDER an open conversation and, if so,
        /// seals the page, clears the verbatim memory and starts a fresh page.
        ///
        /// <para>Bound to the day/night cycle by <see cref="ChatSystem"/>, and safe to call
        /// at any time: on the same day it does nothing at all.</para>
        /// </summary>
        /// <returns>Whether a day was sealed.</returns>
        public bool RollOverIfNewDay(NPCMemory memory)
        {
            if (_page == null) return false;

            string today = ChatDayClock.TodayKey;
            if (string.Equals(_page.dayKey, today, StringComparison.Ordinal)) return false;

            string closing = _page.dayKey;
            SealOpenPage();
            ClearVerbatimMemory(memory);

            _page = ChatJournalStore.LoadOrCreatePage(_npcKey, today, _personaId, _displayName);
            if (_page != null)
            {
                _page.conversations++;
                if (memory != null) memory.lastJournalDayKey = _page.dayKey;
            }

            OnDaySealed?.Invoke(closing);
            return true;
        }

        /// <summary>Flushes and lets go of the open page. Safe to call twice.</summary>
        public void Close()
        {
            if (_page == null) return;

            ChatJournalStore.SavePage(_page);
            _page = null;
        }

        /// <summary>
        /// Throws away this character's whole journal, including the page being written, and
        /// forgets which day the memory record was last on.
        ///
        /// <para>Reached from the panel's Reset control. Clearing <c>lastJournalDayKey</c>
        /// matters as much as deleting the files: leaving yesterday's key on a record whose
        /// history has just been wiped would make the very next open seal a day that no
        /// longer exists and clear a memory that is already empty.</para>
        /// </summary>
        public int DiscardAll(NPCMemory memory)
        {
            _page = null;

            int removed = ChatJournalStore.DeleteAll(_npcKey);
            if (memory != null) memory.lastJournalDayKey = "";
            return removed;
        }

        // ── Writing ─────────────────────────────────────────────────────────

        /// <summary>Records a line the player typed.</summary>
        public void RecordPlayer(string speaker, string text) =>
            Record(ChatJournalEntry.ROLE_PLAYER, speaker, text);

        /// <summary>Records a line the character spoke.</summary>
        public void RecordNpc(string speaker, string text) =>
            Record(ChatJournalEntry.ROLE_NPC, speaker, text);

        /// <summary>
        /// Records something that happened rather than something anybody said — a completed
        /// trade, most usefully. It is part of the day and reads oddly attributed to either
        /// party, which is why the role exists.
        /// </summary>
        public void RecordEvent(string text) =>
            Record(ChatJournalEntry.ROLE_SYSTEM, "", text);

        /// <summary>
        /// Appends and persists one line.
        ///
        /// <para>SAVED PER LINE, deliberately. The page is one day of one conversation — a
        /// few kilobytes — and it is the only copy of the day that outlives the session, so
        /// the alternative is losing the whole day to a crash in exchange for a write this
        /// small. It is also why pages are per day rather than one document per character:
        /// with a single growing file, this write would cost the whole archive.</para>
        /// </summary>
        private void Record(string role, string speaker, string text)
        {
            if (_page == null) return;
            if (!ChatJournalStore.Append(_page, role, speaker, text)) return;

            ChatJournalStore.SavePage(_page);
        }

        // ── The day boundary ────────────────────────────────────────────────

        /// <summary>
        /// Seals the page the character was last spoken to on, when that was a different day
        /// from <paramref name="today"/>, and clears what they remember verbatim.
        ///
        /// <para>An EMPTY <c>lastJournalDayKey</c> is not a boundary and must not be treated
        /// as one: it means either a character who has never been spoken to, or a record
        /// migrated from before the journal existed. Sealing there would wipe a conversation
        /// the journal never recorded, which is the one case where the archive cannot make
        /// the loss good.</para>
        /// </summary>
        private bool SealPreviousDay(NPCMemory memory, string today)
        {
            string previous = memory != null ? memory.lastJournalDayKey : null;
            if (string.IsNullOrEmpty(previous)) return false;
            if (string.Equals(previous, today, StringComparison.Ordinal)) return false;

            ChatJournalPage stale = ChatJournalStore.LoadPage(_npcKey, previous);
            if (stale != null)
            {
                stale.sealedIso8601 = DateTime.UtcNow.ToString("o");
                ChatJournalStore.SavePage(stale);
            }

            ClearVerbatimMemory(memory);
            return true;
        }

        /// <summary>Stamps the open page as finished and writes it.</summary>
        private void SealOpenPage()
        {
            if (_page == null) return;

            _page.sealedIso8601 = DateTime.UtcNow.ToString("o");
            ChatJournalStore.SavePage(_page);
        }

        /// <summary>
        /// Forgets the words and keeps the person: the twelve-message window goes, the
        /// durable digest, the relationship and the visit count stay.
        ///
        /// <para><c>lastGreetedDayKey</c> is deliberately left alone. It is already keyed on
        /// the day and already answers "is a greeting due", so clearing it here would be a
        /// second mechanism saying the same thing — and the two would eventually disagree
        /// about which day it is.</para>
        /// </summary>
        private static void ClearVerbatimMemory(NPCMemory memory)
        {
            memory?.ephemeralHistory?.Clear();
        }
    }
}
