using System;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// What happens to a character when the day underneath their conversation turns over.
    ///
    /// <para>This is the feature, and it is the half that cannot be checked by looking at the
    /// screen: sealing a page is invisible, and clearing the verbatim window looks exactly
    /// like a character who has nothing to say. What each test here holds is a different way
    /// of getting it wrong — forgetting too much (the digest and the relationship go, and the
    /// character meets the player as a stranger every morning), forgetting too little (the
    /// panel opens on yesterday's conversation, which is what the whole feature exists to
    /// stop), or forgetting at the wrong moment (a record migrated from before the journal
    /// existed has its only copy of a day wiped into an archive that never held it).</para>
    ///
    /// <para>The day is driven through <c>lastJournalDayKey</c> and through the open page's
    /// own key rather than by moving a clock. <see cref="ChatDayClock.TodayKey"/> reads the
    /// real calendar and a day/night cycle that does not exist in EditMode, so it cannot be
    /// pushed — and it does not need to be: everything here decides by COMPARING a stored key
    /// against it.</para>
    /// </summary>
    public class ChatJournalDayRolloverTests
    {
        private const string NPC = "gatita-persona-Gatita";

        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(
                Path.GetTempPath(), "valkur_test_rollover_" + Guid.NewGuid().ToString("N"));
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

        // ── Opening after a day has passed ──────────────────────────────────

        [Test]
        public void Open_AfterADayHasPassed_ClearsTheVerbatimWindow()
        {
            NPCMemory memory = MemoryFromYesterday();
            var journal = new ChatJournal();

            bool sealedADay = journal.Open(NPC, "gatita-persona", "Gatita", memory);

            Assert.IsTrue(sealedADay);
            Assert.AreEqual(0, memory.ephemeralHistory.Count,
                "This is 'limpia el chat': the panel replays ephemeralHistory, so a window " +
                "that survives the night is yesterday's conversation still on screen.");
        }

        [Test]
        public void Open_AfterADayHasPassed_KeepsEverythingThatMakesThemKnowYou()
        {
            NPCMemory memory = MemoryFromYesterday();
            memory.friendshipScore = 42;
            memory.visitCount = 9;
            memory.digest.Add(new MemoryNote { key = "name", value = "Valkur" });

            new ChatJournal().Open(NPC, "gatita-persona", "Gatita", memory);

            Assert.AreEqual(42, memory.friendshipScore, "Forgetting the words is not forgetting the person.");
            Assert.AreEqual(9, memory.visitCount);
            Assert.AreEqual(1, memory.digest.Count);
            Assert.AreEqual("Valkur", memory.digest[0].value);
        }

        [Test]
        public void Open_AfterADayHasPassed_LeavesTheGreetingFlagAlone()
        {
            // lastGreetedDayKey is already keyed on the day and already answers "is a
            // greeting due". A second mechanism saying the same thing is two things that
            // eventually disagree about which day it is.
            NPCMemory memory = MemoryFromYesterday();
            memory.lastGreetedDayKey = "2020-01-01#1";

            new ChatJournal().Open(NPC, "gatita-persona", "Gatita", memory);

            Assert.AreEqual("2020-01-01#1", memory.lastGreetedDayKey);
        }

        [Test]
        public void Open_AfterADayHasPassed_SealsTheDayThatEnded()
        {
            NPCMemory memory = MemoryFromYesterday();
            WriteYesterdaysPage();

            new ChatJournal().Open(NPC, "gatita-persona", "Gatita", memory);

            var yesterday = ChatJournalStore.LoadPage(NPC, YESTERDAY);
            Assert.IsNotNull(yesterday);
            Assert.IsTrue(yesterday.IsSealed);
            Assert.AreEqual(1, yesterday.entries.Count, "Sealing a page must not empty it.");
        }

        [Test]
        public void Open_OnTheSameDay_ChangesNothing()
        {
            var memory = new NPCMemory
            {
                npcKey = NPC,
                lastJournalDayKey = ChatDayClock.TodayKey,
            };
            memory.ephemeralHistory.Add(new EphemeralMessage { role = "user", content = "hola" });

            bool sealedADay = new ChatJournal().Open(NPC, "gatita-persona", "Gatita", memory);

            Assert.IsFalse(sealedADay);
            Assert.AreEqual(1, memory.ephemeralHistory.Count,
                "Walking away and coming back after lunch is the same conversation.");
        }

        [Test]
        public void Open_WithNoJournalDayOnRecord_DoesNotSealAnything()
        {
            // What an NPCMemory migrated from v2 looks like: a real conversation in the
            // window, and no page anywhere that holds it. Treating an empty key as a
            // boundary would wipe the one copy of it.
            var migrated = new NPCMemory { npcKey = NPC, lastJournalDayKey = "" };
            migrated.ephemeralHistory.Add(new EphemeralMessage { role = "user", content = "hola" });

            bool sealedADay = new ChatJournal().Open(NPC, "gatita-persona", "Gatita", migrated);

            Assert.IsFalse(sealedADay);
            Assert.AreEqual(1, migrated.ephemeralHistory.Count);
            Assert.AreEqual(ChatDayClock.TodayKey, migrated.lastJournalDayKey,
                "It adopts today instead, so the NEXT boundary is detectable.");
        }

        // ── Midnight, mid-conversation ──────────────────────────────────────

        [Test]
        public void RollOverIfNewDay_SealsTheOpenPageAndStartsAFreshOne()
        {
            var memory = new NPCMemory { npcKey = NPC };
            var journal = new ChatJournal();
            journal.Open(NPC, "gatita-persona", "Gatita", memory);
            journal.RecordPlayer("Player", "buenas noches");

            // Standing the open page in yesterday is how a clock that cannot be moved is
            // simulated: RollOverIfNewDay compares the page's key against the live one.
            journal.CurrentPage.dayKey = YESTERDAY;
            journal.CurrentPage.calendarDate = YESTERDAY_DATE;

            string sealedKey = null;
            journal.OnDaySealed += key => sealedKey = key;

            Assert.IsTrue(journal.RollOverIfNewDay(memory));
            Assert.AreEqual(YESTERDAY, sealedKey,
                "The panel is told WHICH day ended so it can hold the reader's place by day.");
            Assert.AreEqual(ChatDayClock.TodayKey, journal.CurrentDayKey);
            Assert.AreEqual(0, memory.ephemeralHistory.Count);
        }

        [Test]
        public void RollOverIfNewDay_OnTheSameDayDoesNothing()
        {
            var memory = new NPCMemory { npcKey = NPC };
            var journal = new ChatJournal();
            journal.Open(NPC, "gatita-persona", "Gatita", memory);
            journal.RecordPlayer("Player", "hola");

            bool raised = false;
            journal.OnDaySealed += _ => raised = true;

            Assert.IsFalse(journal.RollOverIfNewDay(memory));
            Assert.IsFalse(raised, "It is bound to the day/night cycle and safe to call at any time.");
        }

        // ── Writing ─────────────────────────────────────────────────────────

        [Test]
        public void RecordedLinesArePersistedAsTheyHappen()
        {
            // Not archived from ephemeralHistory at the end of the day: that window holds
            // twelve messages and drops the oldest, so most of a long conversation would
            // already be gone by the time there was a day to seal.
            var memory = new NPCMemory { npcKey = NPC };
            var journal = new ChatJournal();
            journal.Open(NPC, "gatita-persona", "Gatita", memory);

            for (int i = 0; i < NPCMemory.EPHEMERAL_CAP * 2; i++)
                journal.RecordPlayer("Player", "línea " + i);

            var onDisk = ChatJournalStore.LoadPage(NPC, ChatDayClock.TodayKey);

            Assert.IsNotNull(onDisk, "Every line is written through, so a crash costs nothing.");
            Assert.AreEqual(NPCMemory.EPHEMERAL_CAP * 2, onDisk.entries.Count);
        }

        [Test]
        public void RecordingWithNoOpenPageIsHarmless()
        {
            var journal = new ChatJournal();
            Assert.DoesNotThrow(() => journal.RecordPlayer("Player", "hola"));
            Assert.IsFalse(journal.IsOpen);
        }

        [Test]
        public void DiscardAll_TakesThePagesAndTheDayKeyTogether()
        {
            var memory = new NPCMemory { npcKey = NPC };
            var journal = new ChatJournal();
            journal.Open(NPC, "gatita-persona", "Gatita", memory);
            journal.RecordPlayer("Player", "hola");
            journal.Close();
            journal.Open(NPC, "gatita-persona", "Gatita", memory);

            Assert.AreEqual(1, journal.DiscardAll(memory));
            Assert.AreEqual(0, ChatJournalStore.ListPages(NPC).Count);
            Assert.AreEqual("", memory.lastJournalDayKey,
                "Leaving yesterday's key on a wiped record makes the very next open seal a " +
                "day that no longer exists.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private const string YESTERDAY_DATE = "2020-01-01";
        private static readonly string YESTERDAY = ChatDayClock.BuildKey(YESTERDAY_DATE, 1);

        private static NPCMemory MemoryFromYesterday()
        {
            var memory = new NPCMemory { npcKey = NPC, lastJournalDayKey = YESTERDAY };
            memory.ephemeralHistory.Add(new EphemeralMessage { role = "user", content = "hola" });
            memory.ephemeralHistory.Add(new EphemeralMessage { role = "assistant", content = "buenas" });
            return memory;
        }

        private static void WriteYesterdaysPage()
        {
            var page = ChatJournalStore.LoadOrCreatePage(NPC, YESTERDAY, "gatita-persona", "Gatita");
            ChatJournalStore.Append(page, ChatJournalEntry.ROLE_PLAYER, "Player", "hola");
            ChatJournalStore.SavePage(page);
        }
    }
}
