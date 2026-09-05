using NUnit.Framework;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The journal's day list is built from FILE NAMES rather than from an index file, so
    /// <see cref="ChatJournalPageRef"/> is the only thing standing between a directory
    /// listing and the archive the player reads.
    ///
    /// <para>Everything it gets wrong is silent. A stem that does not round-trip files a day
    /// under a second name and splits one conversation into two pages; a comparison that
    /// orders by the in-game day would shuffle the archive on every Play, since that counter
    /// is not persisted and restarts at 0; and a parser that accepts anything would turn a
    /// <c>.bak</c> into a selectable day that is always empty.</para>
    /// </summary>
    public class ChatJournalPageRefTests
    {
        [Test]
        public void FromDayKey_RoundTripsThroughItsStem()
        {
            var original = ChatJournalPageRef.FromDayKey("2026-09-05#3");
            Assert.IsTrue(original.IsValid, "A well-formed day key must produce a valid ref.");

            var reparsed = ChatJournalPageRef.FromStem(original.Stem);

            Assert.IsTrue(reparsed.IsValid, $"Stem '{original.Stem}' must parse back.");
            Assert.AreEqual(original.DayKey, reparsed.DayKey,
                "A stem that does not carry its day key back files the same day under two names, " +
                "which splits one conversation across two pages.");
            Assert.AreEqual(original.CalendarDate, reparsed.CalendarDate);
            Assert.AreEqual(original.InGameDay, reparsed.InGameDay);
        }

        [Test]
        public void FromDayKey_RebuiltKeyIsIdenticalToTheClocksOwn()
        {
            // The key is compared for EQUALITY everywhere — the greeting, the seal, the
            // "is this today" label — so a rebuilt one that merely looks the same is a bug
            // that only shows up as a day that never ends or never starts.
            string fromClock = ChatDayClock.BuildKey("2026-01-09", 12);
            var pageRef = ChatJournalPageRef.FromDayKey(fromClock);

            Assert.AreEqual(fromClock, pageRef.DayKey);
            Assert.AreEqual(fromClock, ChatJournalPageRef.FromStem(pageRef.Stem).DayKey);
        }

        [Test]
        public void FromDayKey_ZeroInGameDayIsALegitimateValue()
        {
            // 0 is what ChatDayClock answers when no day/night cycle is running — a scene
            // without one, and every EditMode test. Refusing it would mean no page could
            // ever be written in those cases.
            var pageRef = ChatJournalPageRef.FromDayKey("2026-09-05#0");

            Assert.IsTrue(pageRef.IsValid);
            Assert.AreEqual(0, pageRef.InGameDay);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("2026-09-05")]
        [TestCase("#3")]
        [TestCase("2026-09-05#")]
        [TestCase("2026-13-45#3")]
        [TestCase("hello#3")]
        [TestCase("2026-09-05#three")]
        public void FromDayKey_RefusesMalformedKeys(string key)
        {
            Assert.IsFalse(ChatJournalPageRef.FromDayKey(key).IsValid,
                $"'{key}' is not a day key. Coercing it would file a page under a date it " +
                "does not belong to, which mislabels the archive rather than failing.");
        }

        [TestCase("2026-09-05.json")]
        [TestCase("2026-09-05_d00003.json.bak")]
        [TestCase("notes")]
        [TestCase("_d00003")]
        [TestCase("2026-09-05_dxx")]
        public void FromStem_RefusesAnythingThatIsNotAPage(string stem)
        {
            // The archive directory also holds .bak files, quarantined .corrupt files and
            // temps from a write that died. Every one of them reaches the listing as a name,
            // and every one that parsed would become a day the player can select and find
            // empty.
            Assert.IsFalse(ChatJournalPageRef.FromStem(stem).IsValid);
        }

        [Test]
        public void CompareTo_OrdersNewestFirst()
        {
            var older = ChatJournalPageRef.FromDayKey("2026-09-01#4");
            var newer = ChatJournalPageRef.FromDayKey("2026-09-05#1");

            Assert.Less(newer.CompareTo(older), 0,
                "The archive opens on the most recent day; a list that starts at the " +
                "beginning of time makes the player walk all of it to reach the one they want.");
        }

        [Test]
        public void CompareTo_UsesTheInGameDayOnlyAsATieBreakWithinOneDate()
        {
            // The in-game counter is not persisted and legitimately runs backwards between
            // Play sessions, so ordering on it across dates would shuffle the archive.
            var sameDayEarly = ChatJournalPageRef.FromDayKey("2026-09-05#0");
            var sameDayLate = ChatJournalPageRef.FromDayKey("2026-09-05#7");
            var laterDateLowCounter = ChatJournalPageRef.FromDayKey("2026-09-06#0");

            Assert.Less(sameDayLate.CompareTo(sameDayEarly), 0,
                "Within one date, the higher in-game day is the more recent.");
            Assert.Less(laterDateLowCounter.CompareTo(sameDayLate), 0,
                "A later calendar date wins however low its in-game counter is.");
        }

        [Test]
        public void Label_SuppressesTheInGameDayWhenThereIsNoCycle()
        {
            // 0 means "no day/night cycle was running", not "day zero". Printing it invents
            // a day the game never showed the player.
            Assert.AreEqual("2026-09-05", ChatJournalPageRef.FromDayKey("2026-09-05#0").Label(english: false));
            StringAssert.Contains("3", ChatJournalPageRef.FromDayKey("2026-09-05#3").Label(english: false));
        }
    }
}
