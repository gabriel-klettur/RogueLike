using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The conversation's own written record, and what a day boundary does to it.
    ///
    /// <para>A partial rather than a second component, because every seam it hooks is already
    /// here: the message path, the open and close of a conversation, and the memory record it
    /// clears. A separate MonoBehaviour would have to be told about all three and would then
    /// be a second thing that can be missing from a scene.</para>
    /// </summary>
    public partial class ChatSystem
    {
        /// <summary>
        /// The live journal. One object for the life of this system, re-pointed at whichever
        /// character is being spoken to — see <see cref="ChatJournal"/> for why it is an
        /// object and not a static.
        /// </summary>
        private readonly ChatJournal _journal = new ChatJournal();

        /// <summary>
        /// Whether this system is currently listening to the day/night cycle. Tracked rather
        /// than inferred, because Domain Reload is OFF and a static event that is subscribed
        /// twice is subscribed forever.
        /// </summary>
        private bool _watchingDayChange;

        /// <summary>
        /// Raised when the day turned over in the MIDDLE of a conversation: the page was
        /// sealed, the verbatim memory cleared, and whatever the panel was showing is now a
        /// transcript of yesterday. Carries the day key that was sealed.
        /// </summary>
        public event Action<string> OnDayRolledOver;

        // ── Query surface for the panel ──────────────────────────────────────

        /// <summary>
        /// The key the open conversation's records are filed under, or an empty string.
        /// The journal viewer needs it to list pages, and it is the memory record's own key
        /// rather than a second identity — two keys for one character is how an archive ends
        /// up half under one name and half under another.
        /// </summary>
        public string ActiveNpcKey => _activeMemory != null ? _activeMemory.npcKey : "";

        /// <summary>Every day on record for the character being spoken to, newest first.</summary>
        public List<ChatJournalPageRef> ListJournalPages() =>
            ChatJournalStore.ListPages(ActiveNpcKey);

        /// <summary>
        /// One page of the open conversation's journal, or null.
        ///
        /// <para>TODAY comes from the live page rather than from disk. They agree — every
        /// line is written through — but reading the file back would make the viewer's answer
        /// depend on a write having completed, and a save that failed would show the player a
        /// day that is missing its last sentence with nothing saying so.</para>
        /// </summary>
        public ChatJournalPage LoadJournalPage(ChatJournalPageRef pageRef)
        {
            if (!pageRef.IsValid) return null;

            ChatJournalPage live = _journal.CurrentPage;
            if (live != null && string.Equals(live.dayKey, pageRef.DayKey, StringComparison.Ordinal))
                return live;

            return ChatJournalStore.LoadPage(ActiveNpcKey, pageRef);
        }

        // ── Lifetime, driven from OpenChat / CloseChat ───────────────────────

        /// <summary>
        /// Opens today's page for <paramref name="npcName"/> and seals yesterday's if the day
        /// turned over while the player was away.
        ///
        /// <para>Called BEFORE the transcript is replayed from memory. That order is the
        /// whole of "the chat is clean the next day": sealing clears
        /// <c>ephemeralHistory</c>, and <c>SeedHistoryFromMemory</c> reads it — replaying
        /// first would fill the panel with yesterday and then empty the record underneath
        /// it, leaving the two disagreeing until the next open.</para>
        /// </summary>
        private void OpenJournalForConversation(string npcName)
        {
            string displayName = _activePersona != null && !string.IsNullOrEmpty(_activePersona.displayName)
                ? _activePersona.displayName
                : npcName;

            _journal.Open(_activeMemory?.npcKey, _activePersona?.personaId, displayName, _activeMemory);
            WatchDayChange(true);
        }

        /// <summary>Flushes the page and stops watching the clock.</summary>
        private void CloseJournalForConversation()
        {
            WatchDayChange(false);
            _journal.Close();
        }

        /// <summary>
        /// Releases the day/night subscription when this system goes away.
        ///
        /// <para><c>CloseChat</c> already does it on every ordinary exit, and this is the
        /// path that has none: a scene change, a Play-mode stop, the singleton losing a
        /// duplicate guard. Without it a destroyed ChatSystem stays reachable from a static
        /// delegate for the rest of the session, which with Domain Reload OFF means until the
        /// Editor is restarted.</para>
        /// </summary>
        protected override void OnDestroy()
        {
            WatchDayChange(false);
            base.OnDestroy();
        }

        /// <summary>Writes one line of the open conversation to today's page.</summary>
        private void RecordToJournal(string sender, string text)
        {
            if (sender == PLAYER_SENDER) _journal.RecordPlayer(sender, text);
            else _journal.RecordNpc(sender, text);
        }

        /// <summary>
        /// Notes something that HAPPENED in the conversation rather than something either
        /// party said — a completed trade. It belongs in the day's record and reads wrong
        /// attributed to a speaker.
        /// </summary>
        internal void RecordEventToJournal(string text) => _journal.RecordEvent(text);

        /// <summary>
        /// Throws away everything written about the character being spoken to.
        /// Part of the Reset control; see <see cref="ChatJournal.DiscardAll"/> for why the
        /// journal has to go with the memory rather than outliving it.
        /// </summary>
        private void DiscardJournalForConversation()
        {
            int removed = _journal.DiscardAll(_activeMemory);
            if (removed > 0)
                Debug.Log($"[ChatSystem] Discarded {removed} journal page(s) for '{ActiveNpcKey}'.");
        }

        // ── The day boundary, mid-conversation ───────────────────────────────

        /// <summary>
        /// Subscribes to or releases <c>DayNightCycle.OnDayChanged</c>, exactly once either
        /// way.
        ///
        /// <para>The event is a STATIC delegate on a class with Domain Reload off, which is
        /// the shape that leaks: a handler added twice fires twice and a handler never removed
        /// keeps a destroyed <c>ChatSystem</c> alive for the rest of the session. The guard
        /// flag is what makes both impossible rather than merely unlikely.</para>
        /// </summary>
        private void WatchDayChange(bool watch)
        {
            if (watch == _watchingDayChange) return;
            _watchingDayChange = watch;

            if (watch) DayNightCycle.OnDayChanged += OnGameDayChanged;
            else DayNightCycle.OnDayChanged -= OnGameDayChanged;
        }

        /// <summary>
        /// Midnight arrived while the panel was open.
        ///
        /// <para>The conversation is not interrupted — the player keeps the panel and the
        /// character keeps standing there — but the DAY it belongs to is over: the page is
        /// sealed, the verbatim memory cleared, the transcript emptied and the greeting
        /// spoken again, which is what a new morning looks like from inside a conversation.
        /// </para>
        /// </summary>
        private void OnGameDayChanged(int newDay)
        {
            if (!_chatOpen) return;
            if (!_journal.RollOverIfNewDay(_activeMemory)) return;

            _history.Clear();
            _pendingChunks.Clear();

            GreetForNewDay();

            if (_activeMemory != null) NPCMemoryStore.Save(_activeMemory);

            OnHistoryReset?.Invoke();
            OnDayRolledOver?.Invoke(_journal.CurrentDayKey);
        }

        /// <summary>
        /// Says the greeting if one is due, and stamps the day it was said on.
        ///
        /// <para>Shared by <c>OpenChat</c> and the midnight rollover so there is one answer
        /// to "has this character said hello today". Two copies of this is how a character
        /// ends up greeting twice in one morning, or not at all.</para>
        /// </summary>
        private bool GreetForNewDay()
        {
            if (_activePersona == null || _activeMemory == null) return false;
            if (string.IsNullOrEmpty(_activePersona.greeting)) return false;
            if (!ChatDayClock.IsNewDay(_activeMemory.lastGreetedDayKey)) return false;

            string npcLabel = !string.IsNullOrEmpty(_activePersona.displayName)
                ? _activePersona.displayName
                : ActiveNpcKey;

            AddMessage(npcLabel, _activePersona.greeting);
            ShowTargetBubble(_activePersona.greeting, NPC_BUBBLE_TTL_MS);
            _activeMemory.lastGreetedDayKey = ChatDayClock.TodayKey;

            // The greeting is authored text that never passed through a provider, so it
            // carries no expression of its own and has to be read the same way an offline
            // line is. Set BEFORE OnChatOpened fires, so the panel builds with the right face
            // rather than opening neutral and correcting itself.
            SetExpression(ClassifySpoken(_activePersona.greeting));
            return true;
        }

        // ── Modal overlays over the panel ────────────────────────────────────

        /// <summary>
        /// True while something is covering the conversation — today, the journal viewer.
        ///
        /// <para>It exists because Escape has exactly one owner in this subsystem, and it is
        /// <see cref="ChatSystem"/>. Without this the viewer would have to read the key
        /// itself and race the close: two readers of one key in an undefined Update order is
        /// how a single press both dismisses the overlay and closes the panel behind it, or
        /// does neither, depending on the frame.</para>
        /// </summary>
        public bool HasModalOverlay { get; private set; }

        /// <summary>
        /// Raised when Escape is pressed while an overlay is up. The overlay dismisses
        /// itself and the conversation stays open.
        /// </summary>
        public event Action OnOverlayDismissRequested;

        /// <summary>Declares that an overlay is or is not covering the conversation.</summary>
        public void SetModalOverlay(bool open) => HasModalOverlay = open;

        /// <summary>
        /// Escape, resolved in one place: dismiss the overlay if there is one, otherwise
        /// close the conversation.
        /// </summary>
        private void HandleEscape()
        {
            if (HasModalOverlay)
            {
                OnOverlayDismissRequested?.Invoke();
                return;
            }

            CloseChat();
        }
    }
}
