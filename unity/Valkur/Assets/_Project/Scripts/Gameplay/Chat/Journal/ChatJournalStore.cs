using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Persistence for the conversation journal: one directory per character, one file per
    /// day, no index.
    ///
    /// <para>WHAT THIS IS NOT. <see cref="ChatSessionLogger"/> already writes every line to a
    /// plain-text file, and that file is a DIAGNOSTIC — one per conversation, named by a
    /// timestamp, unreadable in game and unbounded on disk. The journal is player-facing
    /// content: addressable by character and day, structured enough to draw, and the thing
    /// the panel's Diario button reads. Neither replaces the other and both are cheap.</para>
    ///
    /// <para>WHAT IT DOES NOT DO. It has no notion of "today" and no idea when a day ends;
    /// it stores and retrieves pages. Deciding that the day has rolled over, sealing
    /// yesterday and clearing what the character remembers verbatim all belong to
    /// <see cref="ChatJournal"/>, which is the live half. Splitting them is what lets the
    /// rollover rules be tested without a filesystem and the filesystem be tested without a
    /// clock.</para>
    ///
    /// <para>Every read and write goes through <see cref="ChatJsonFile"/>, so a journal page
    /// gets the same atomic write, the same backup and the same corrupt-file recovery as a
    /// memory record — one implementation, not two.</para>
    ///
    /// <para>Domain Reload is OFF. This class holds no mutable static state; the only static
    /// it depends on is <c>ChatPersistencePaths.OverrideRoot</c>, which resets itself.</para>
    /// </summary>
    public static class ChatJournalStore
    {
        /// <summary>
        /// Current page layout. Increment together with <see cref="ChatJournalPage.schemaVersion"/>
        /// and add a branch to <see cref="Migrate"/>.
        /// </summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        // ─── Reading ──────────────────────────────────────────────────────────

        /// <summary>
        /// Every day on record for <paramref name="npcKey"/>, newest first. Empty when this
        /// character has never been talked to.
        ///
        /// <para>Built from FILE NAMES — no page is opened and no index is consulted, so the
        /// cost is one directory listing however long the save has been running. Anything in
        /// the directory that is not a page (<c>.bak</c>, <c>.corrupt</c>, a temp from a
        /// write that died) fails to parse as a stem and is skipped rather than becoming a
        /// day the player can select and find empty.</para>
        /// </summary>
        public static List<ChatJournalPageRef> ListPages(string npcKey)
        {
            var refs = new List<ChatJournalPageRef>();
            if (string.IsNullOrEmpty(npcKey)) return refs;

            string dir = ChatPersistencePaths.JournalDirectoryFor(npcKey);
            if (!Directory.Exists(dir)) return refs;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + ChatPersistencePaths.JournalPageExtension);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatJournalStore] Could not list journal of '{npcKey}': {ex.Message}");
                return refs;
            }

            for (int i = 0; i < files.Length; i++)
            {
                var pageRef = ChatJournalPageRef.FromStem(Path.GetFileNameWithoutExtension(files[i]));
                if (pageRef.IsValid) refs.Add(pageRef);
            }

            refs.Sort();
            return refs;
        }

        /// <summary>
        /// Every character who has a journal on disk, by the SLUG their directory is named
        /// after — which is what a filesystem can answer, since slugging is one-way.
        ///
        /// <para>For the console probe, and for it alone. Nothing in the game asks "who have
        /// I ever talked to": a conversation always arrives holding the npcKey of whoever is
        /// standing in front of the player, and a second way of naming a character is how an
        /// archive ends up half under one name and half under another.</para>
        /// </summary>
        public static List<string> ListArchivedSlugs()
        {
            var slugs = new List<string>();
            if (!Directory.Exists(ChatPersistencePaths.JournalDirectory)) return slugs;

            try
            {
                foreach (string dir in Directory.GetDirectories(ChatPersistencePaths.JournalDirectory))
                    slugs.Add(Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatJournalStore] Could not list journals: {ex.Message}");
            }

            slugs.Sort(StringComparer.Ordinal);
            return slugs;
        }

        /// <summary>
        /// The pages in one archive directory, addressed by its slug rather than by an
        /// npcKey. Companion to <see cref="ListArchivedSlugs"/>; same probe-only purpose.
        /// </summary>
        public static List<ChatJournalPageRef> ListPagesBySlug(string slug)
        {
            // Slugging is idempotent — it only ever replaces characters a path cannot hold —
            // so a slug fed back through it comes out unchanged, and the ordinary path can
            // serve both callers instead of a second one that could disagree with it.
            return ListPages(slug);
        }

        /// <summary>
        /// The page for <paramref name="dayKey"/>, or null when that day was never written.
        /// A page recovered from its backup is returned as if nothing had happened.
        /// </summary>
        public static ChatJournalPage LoadPage(string npcKey, string dayKey)
        {
            var pageRef = ChatJournalPageRef.FromDayKey(dayKey);
            return pageRef.IsValid ? LoadPage(npcKey, pageRef) : null;
        }

        /// <summary>The page a listing entry points at, or null when it will not read.</summary>
        public static ChatJournalPage LoadPage(string npcKey, ChatJournalPageRef pageRef)
        {
            if (string.IsNullOrEmpty(npcKey) || !pageRef.IsValid) return null;

            string path = ChatPersistencePaths.JournalPagePath(npcKey, pageRef.Stem);
            var page = ChatJsonFile.ReadOrRecover<ChatJournalPage>(
                path, $"journal page '{pageRef.Stem}' of '{npcKey}'");

            if (page == null) return null;

            Migrate(page);
            RepairIdentity(page, npcKey, pageRef);
            return page;
        }

        /// <summary>
        /// The page for <paramref name="dayKey"/>, created empty when it does not exist yet.
        /// Never returns null for a well-formed key.
        ///
        /// <para>Returning an EXISTING page for a day that was already sealed is deliberate:
        /// the in-game half of a day key runs backwards across a Play-mode restart, so the
        /// same day is routinely re-entered, and appending to the page that is already there
        /// is what stops one calendar day becoming several half-empty ones.</para>
        /// </summary>
        public static ChatJournalPage LoadOrCreatePage(
            string npcKey, string dayKey, string personaId, string displayName)
        {
            var pageRef = ChatJournalPageRef.FromDayKey(dayKey);
            if (!pageRef.IsValid)
            {
                Debug.LogWarning($"[ChatJournalStore] Refusing to open a page for malformed day key '{dayKey}'.");
                return null;
            }

            ChatJournalPage existing = LoadPage(npcKey, pageRef);
            if (existing != null)
            {
                // Re-entering a day the player had left. The seal is what marks a page as
                // finished, so taking it off is the whole of re-opening one.
                existing.sealedIso8601 = "";
                if (!string.IsNullOrEmpty(displayName)) existing.displayName = displayName;
                return existing;
            }

            return new ChatJournalPage
            {
                schemaVersion = CURRENT_SCHEMA_VERSION,
                dayKey = pageRef.DayKey,
                calendarDate = pageRef.CalendarDate,
                inGameDay = pageRef.InGameDay,
                npcKey = npcKey,
                personaId = personaId,
                displayName = displayName,
                openedIso8601 = DateTime.UtcNow.ToString("o"),
            };
        }

        // ─── Writing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Persists <paramref name="page"/>. An EMPTY page is not written — and if one is
        /// already on disk it is removed.
        ///
        /// <para>That rule is what keeps the day list honest. A conversation the player
        /// opened and closed without saying anything is not a day worth remembering, and a
        /// selector offering four blank pages between two real ones makes the archive read as
        /// broken. It has to delete rather than merely skip, because a page can be emptied —
        /// by the Reset control — after it was written.</para>
        /// </summary>
        public static bool SavePage(ChatJournalPage page)
        {
            if (page == null)
            {
                Debug.LogError("[ChatJournalStore] SavePage called with null page.");
                return false;
            }

            var pageRef = ChatJournalPageRef.FromDayKey(page.dayKey);
            if (!pageRef.IsValid)
            {
                Debug.LogError($"[ChatJournalStore] Page of '{page.npcKey}' has malformed day key '{page.dayKey}'.");
                return false;
            }

            string path = ChatPersistencePaths.JournalPagePath(page.npcKey, pageRef.Stem);

            if (page.IsEmpty)
            {
                ChatJsonFile.Delete(path);
                return true;
            }

            page.lastWrittenIso8601 = DateTime.UtcNow.ToString("o");
            return ChatJsonFile.WriteAtomic(
                path, page, $"journal page '{pageRef.Stem}' of '{page.npcKey}'");
        }

        /// <summary>
        /// Appends one line to <paramref name="page"/>. Does NOT save — the caller decides
        /// when, the same contract <c>NPCMemoryStore.AppendEphemeral</c> uses.
        ///
        /// <para>Blank text is refused. The journal is the only record of a conversation that
        /// survives the day, and an empty line in it is indistinguishable from a message that
        /// failed to arrive.</para>
        /// </summary>
        public static bool Append(ChatJournalPage page, string role, string speaker, string text)
        {
            if (page == null || string.IsNullOrWhiteSpace(text)) return false;

            page.entries ??= new List<ChatJournalEntry>();
            page.entries.Add(new ChatJournalEntry
            {
                role = role,
                speaker = speaker,
                text = text,
                timestampIso8601 = DateTime.UtcNow.ToString("o"),
            });

            if (string.IsNullOrEmpty(page.openedIso8601))
                page.openedIso8601 = page.entries[0].timestampIso8601;

            return true;
        }

        // ─── Erasing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Removes every page ever written for <paramref name="npcKey"/> and the directory
        /// that held them, reporting how many pages went.
        ///
        /// <para>This is what the panel's Reset control calls, and it has to. Reset means
        /// "this character has never met you"; leaving the journal behind would leave a
        /// stranger with a written record of conversations they have no memory of, which is
        /// a worse state than either of the two it sits between.</para>
        /// </summary>
        public static int DeleteAll(string npcKey)
        {
            if (string.IsNullOrEmpty(npcKey)) return 0;

            string dir = ChatPersistencePaths.JournalDirectoryFor(npcKey);
            if (!Directory.Exists(dir)) return 0;

            int removed = ListPages(npcKey).Count;

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatJournalStore] Could not delete journal of '{npcKey}': {ex.Message}");
                return 0;
            }

            return removed;
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Brings an older page up to <see cref="CURRENT_SCHEMA_VERSION"/>.
        /// A no-op today; the branch structure is here so the first real migration is an
        /// addition rather than a redesign.
        /// </summary>
        private static void Migrate(ChatJournalPage page)
        {
            if (page.schemaVersion >= CURRENT_SCHEMA_VERSION) return;

            page.schemaVersion = CURRENT_SCHEMA_VERSION;
            Debug.Log($"[ChatJournalStore] Migrated a page of '{page.npcKey}' to v{CURRENT_SCHEMA_VERSION}.");
        }

        /// <summary>
        /// Fills in identity a page is missing, from what the caller already knew to look
        /// for it.
        ///
        /// <para>The file NAME is the authority on which day a page is, not the field inside
        /// it: a hand-edited or half-written record whose <c>dayKey</c> disagrees with where
        /// it is filed would otherwise be saved back under a different name, leaving two
        /// pages for one day. Reconciling on load means the disagreement is resolved once,
        /// in favour of the thing the listing already used.</para>
        /// </summary>
        private static void RepairIdentity(ChatJournalPage page, string npcKey, ChatJournalPageRef pageRef)
        {
            page.dayKey = pageRef.DayKey;
            page.calendarDate = pageRef.CalendarDate;
            page.inGameDay = pageRef.InGameDay;
            if (string.IsNullOrEmpty(page.npcKey)) page.npcKey = npcKey;
            page.entries ??= new List<ChatJournalEntry>();
        }
    }
}
