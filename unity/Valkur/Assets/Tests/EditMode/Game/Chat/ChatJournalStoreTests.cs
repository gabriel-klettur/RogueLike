using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The journal's persistence layer, exercised against a temp directory.
    ///
    /// <para>What these pin is the storage SHAPE, because it is the half that cannot be
    /// changed later without a migration: one directory per character, one file per day, no
    /// index. The absence of an index is the load-bearing part — a listing is derived from
    /// the directory, so there is exactly one copy of "which days exist" and nothing to fall
    /// out of step with it.</para>
    /// </summary>
    public class ChatJournalStoreTests
    {
        private const string NPC = "gatita-persona-Gatita";

        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(
                Path.GetTempPath(), "valkur_test_journal_" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            ChatPersistencePaths.OverrideRoot = null;

            try
            {
                if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
            }
            catch { /* the OS cleans temp up eventually */ }
        }

        // ── Round trip ──────────────────────────────────────────────────────

        [Test]
        public void SaveAndLoad_KeepsEveryLineInOrder()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "gatita-persona", "Gatita");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_PLAYER, "Player", "hola");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_NPC, "Gatita", "¡Hola, tesoro!");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_SYSTEM, "", "Comprado 1x pan por 3 monedas.");
            Assert.IsTrue(ChatJournalStore.SavePage(page));

            var loaded = ChatJournalStore.LoadPage(NPC, "2026-09-05#3");

            Assert.IsNotNull(loaded);
            Assert.AreEqual(3, loaded.entries.Count);
            Assert.AreEqual("hola", loaded.entries[0].text);
            Assert.AreEqual(ChatJournalEntry.ROLE_NPC, loaded.entries[1].role);
            Assert.AreEqual("Gatita", loaded.entries[1].speaker,
                "The speaker is stored per line so a page reads correctly after a rename.");
            Assert.AreEqual(ChatJournalEntry.ROLE_SYSTEM, loaded.entries[2].role);
        }

        [Test]
        public void LoadOrCreatePage_ReturnsTheSamePageForTheSameDayAndAppendsToIt()
        {
            // The in-game half of a day key runs backwards across a Play-mode restart, so
            // one calendar day is routinely left and re-entered. Creating a second page each
            // time would shatter a day into several half-empty ones.
            var first = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");
            ChatJournalStore.Append(first, ChatJournalEntry.ROLE_PLAYER, "Player", "primera");
            ChatJournalStore.SavePage(first);

            var second = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");
            ChatJournalStore.Append(second, ChatJournalEntry.ROLE_PLAYER, "Player", "segunda");
            ChatJournalStore.SavePage(second);

            Assert.AreEqual(1, ChatJournalStore.ListPages(NPC).Count,
                "One day is one page, however many times it is opened.");
            Assert.AreEqual(2, ChatJournalStore.LoadPage(NPC, "2026-09-05#3").entries.Count);
        }

        [Test]
        public void LoadOrCreatePage_UnsealsADayThatIsReEntered()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_PLAYER, "Player", "hola");
            page.sealedIso8601 = DateTime.UtcNow.ToString("o");
            ChatJournalStore.SavePage(page);

            var reopened = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");

            Assert.IsFalse(reopened.IsSealed,
                "A seal is a timestamp, not a one-way door: the same day can be come back to.");
        }

        [Test]
        public void LoadOrCreatePage_RefusesAMalformedDayKey()
        {
            Assert.IsNull(ChatJournalStore.LoadOrCreatePage(NPC, "not-a-day", "p", "Gatita"));
        }

        // ── The empty-page rule ─────────────────────────────────────────────

        [Test]
        public void SavePage_DoesNotWriteADayNobodySaidAnythingOn()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");
            page.conversations = 1;

            Assert.IsTrue(ChatJournalStore.SavePage(page));
            Assert.AreEqual(0, ChatJournalStore.ListPages(NPC).Count,
                "A conversation opened and closed in silence is not a day worth remembering; " +
                "a selector offering blank pages reads as an archive that is broken.");
        }

        [Test]
        public void SavePage_RemovesAPageThatHasBeenEmptied()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_PLAYER, "Player", "hola");
            ChatJournalStore.SavePage(page);
            Assert.AreEqual(1, ChatJournalStore.ListPages(NPC).Count);

            page.entries.Clear();
            ChatJournalStore.SavePage(page);

            Assert.AreEqual(0, ChatJournalStore.ListPages(NPC).Count,
                "Skipping an empty page is not enough — it can be emptied after it was written.");
        }

        [Test]
        public void Append_RefusesBlankText()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, "2026-09-05#3", "p", "Gatita");

            Assert.IsFalse(ChatJournalStore.Append(page, ChatJournalEntry.ROLE_NPC, "Gatita", "   "));
            Assert.AreEqual(0, page.entries.Count,
                "An empty line in the only record of a day is indistinguishable from a " +
                "message that failed to arrive.");
        }

        // ── Listing ─────────────────────────────────────────────────────────

        [Test]
        public void ListPages_IsNewestFirstAndIgnoresEverythingThatIsNotAPage()
        {
            WriteDay("2026-09-01#1", "uno");
            WriteDay("2026-09-05#4", "cinco");
            WriteDay("2026-09-03#2", "tres");

            // The kinds of file the directory really accumulates: a backup from the second
            // write of a page, a quarantined corrupt primary, and a temp from a write that
            // died. None of them is a day.
            string dir = ChatPersistencePaths.JournalDirectoryFor(NPC);
            File.WriteAllText(Path.Combine(dir, "2026-09-02_d00001.json.bak"), "{}");
            File.WriteAllText(Path.Combine(dir, "2026-09-02_d00001.json.corrupt"), "{}");
            File.WriteAllText(Path.Combine(dir, "notes.json"), "{}");

            var pages = ChatJournalStore.ListPages(NPC);

            Assert.AreEqual(3, pages.Count);
            Assert.AreEqual("2026-09-05", pages[0].CalendarDate);
            Assert.AreEqual("2026-09-03", pages[1].CalendarDate);
            Assert.AreEqual("2026-09-01", pages[2].CalendarDate);
        }

        [Test]
        public void ListPages_IsEmptyForACharacterNeverSpokenTo()
        {
            Assert.AreEqual(0, ChatJournalStore.ListPages("nobody").Count);
            Assert.AreEqual(0, ChatJournalStore.ListPages(null).Count);
        }

        // ── Recovery ────────────────────────────────────────────────────────

        [Test]
        public void LoadPage_RecoversFromTheBackupWhenThePrimaryIsCorrupt()
        {
            WriteDay("2026-09-05#3", "la buena");
            // A second write is what produces the .bak, so the recovery path has something
            // to recover FROM. Testing it without one would only prove the null return.
            var page = ChatJournalStore.LoadPage(NPC, "2026-09-05#3");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_NPC, "Gatita", "la segunda");
            ChatJournalStore.SavePage(page);

            string path = ChatPersistencePaths.JournalPagePath(
                NPC, ChatJournalPageRef.FromDayKey("2026-09-05#3").Stem);
            File.WriteAllText(path, "{ this is not json");

            var recovered = ChatJournalStore.LoadPage(NPC, "2026-09-05#3");

            Assert.IsNotNull(recovered, "A torn write must cost the last save, not the whole day.");
            Assert.AreEqual(1, recovered.entries.Count, "The backup holds the state before the last write.");
            Assert.IsTrue(File.Exists(path + ".corrupt"),
                "The unreadable primary is quarantined rather than silently thrown away.");
        }

        [Test]
        public void LoadPage_ReconcilesAPageWhoseDayKeyDisagreesWithItsFileName()
        {
            // The file NAME is what the listing used to find this page, so it is the
            // authority. A field that disagreed would be saved back under a different name
            // and leave two pages for one day.
            WriteDay("2026-09-05#3", "hola");
            string path = ChatPersistencePaths.JournalPagePath(
                NPC, ChatJournalPageRef.FromDayKey("2026-09-05#3").Stem);
            File.WriteAllText(path, File.ReadAllText(path).Replace("2026-09-05#3", "1999-01-01#9"));

            var loaded = ChatJournalStore.LoadPage(NPC, "2026-09-05#3");

            Assert.AreEqual("2026-09-05#3", loaded.dayKey);
            Assert.AreEqual("2026-09-05", loaded.calendarDate);
            Assert.AreEqual(3, loaded.inGameDay);
        }

        // ── Erasing ─────────────────────────────────────────────────────────

        [Test]
        public void DeleteAll_LeavesNothingBehindAndReportsWhatWent()
        {
            WriteDay("2026-09-01#1", "uno");
            WriteDay("2026-09-05#4", "cinco");

            Assert.AreEqual(2, ChatJournalStore.DeleteAll(NPC));
            Assert.AreEqual(0, ChatJournalStore.ListPages(NPC).Count);
            Assert.IsFalse(Directory.Exists(ChatPersistencePaths.JournalDirectoryFor(NPC)));
        }

        [Test]
        public void DeleteAll_OnAnEmptyArchiveIsHarmless()
        {
            Assert.AreEqual(0, ChatJournalStore.DeleteAll(NPC));
            Assert.AreEqual(0, ChatJournalStore.DeleteAll(null));
        }

        // ── Archives, for the console probe ─────────────────────────────────

        [Test]
        public void ListArchivedSlugs_FindsEveryCharacterWithAJournal()
        {
            WriteDay("2026-09-01#1", "uno");
            var other = ChatJournalStore.LoadOrCreatePage("pavel-persona-Pavel", "2026-09-01#1", "p", "Pavel");
            ChatJournalStore.Append(other, ChatJournalEntry.ROLE_NPC, "Pavel", "madera fresca");
            ChatJournalStore.SavePage(other);

            var slugs = ChatJournalStore.ListArchivedSlugs();

            Assert.AreEqual(2, slugs.Count);
            // Slugging is one-way, so the probe reports the directory name. Feeding it back
            // in has to work, which is the only reason ListPagesBySlug can share the path.
            foreach (string slug in slugs)
                Assert.AreEqual(1, ChatJournalStore.ListPagesBySlug(slug).Count,
                    $"A slug must address its own archive: '{slug}'.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void WriteDay(string dayKey, string line)
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, dayKey, "gatita-persona", "Gatita");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_PLAYER, "Player", line);
            ChatJournalStore.SavePage(page);
        }
    }
}
