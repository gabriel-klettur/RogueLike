using System;
using System.Globalization;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// A page of the journal, identified without opening it.
    ///
    /// <para>The day list in the panel needs a label and an order for every day on record,
    /// and unbounded retention means "every day on record" can be years of them. Opening
    /// each page to read its header would make drawing the list cost the whole archive, so
    /// the two facts the list needs — the calendar date and the in-game day — are encoded in
    /// the FILE NAME and this type is how they get in and out of it.</para>
    ///
    /// <para>That also settles a question an index file would have left open: a page file
    /// and an index entry are two copies of the same fact, and nothing throws when they
    /// disagree. Here there is one copy. A page that appears on disk is in the list; a page
    /// that is deleted is out of it; there is nothing to keep in step.</para>
    ///
    /// <para>ORDER IS (date, in-game day), COMPARED AS A DATE AND AN INT — never as the
    /// filename string. The zero padding in the stem is cosmetic, and a save that somehow
    /// reached in-game day 100000 would sort correctly anyway. The in-game half is only a
    /// tie-break inside one calendar date, because it does not persist across a Play session
    /// and legitimately runs backwards.</para>
    /// </summary>
    public readonly struct ChatJournalPageRef : IComparable<ChatJournalPageRef>
    {
        /// <summary>The composite key from <see cref="ChatDayClock"/>, e.g. <c>2026-09-05#3</c>.</summary>
        public string DayKey { get; }

        /// <summary>Local calendar date as <c>yyyy-MM-dd</c>.</summary>
        public string CalendarDate { get; }

        /// <summary>In-game day. A label and a tie-break, never an ordinal — see the type note.</summary>
        public int InGameDay { get; }

        /// <summary>The file name, without extension, this page lives under.</summary>
        public string Stem { get; }

        /// <summary>False for the default value, which is what a failed parse returns.</summary>
        public bool IsValid => !string.IsNullOrEmpty(DayKey);

        private ChatJournalPageRef(string dayKey, string calendarDate, int inGameDay, string stem)
        {
            DayKey = dayKey;
            CalendarDate = calendarDate;
            InGameDay = inGameDay;
            Stem = stem;
        }

        /// <summary>
        /// The reference for <paramref name="dayKey"/>, or an invalid one when the key is not
        /// shaped like <see cref="ChatDayClock"/> writes them.
        ///
        /// <para>A malformed key is refused rather than coerced. Coercing it would file the
        /// page under a date it does not belong to, and a journal that quietly mislabels a
        /// day is worse than one that declines to write it.</para>
        /// </summary>
        public static ChatJournalPageRef FromDayKey(string dayKey)
        {
            if (string.IsNullOrEmpty(dayKey)) return default;

            int hash = dayKey.IndexOf('#');
            if (hash <= 0 || hash == dayKey.Length - 1) return default;

            string date = dayKey.Substring(0, hash);
            if (!IsCalendarDate(date)) return default;

            if (!int.TryParse(dayKey.Substring(hash + 1), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int inGameDay))
                return default;

            return new ChatJournalPageRef(dayKey, date, inGameDay, BuildStem(date, inGameDay));
        }

        /// <summary>
        /// The reference a file name describes, or an invalid one when the name is not a page.
        ///
        /// <para>The directory can hold things that are not pages — a <c>.bak</c>, a
        /// quarantined <c>.corrupt</c>, a temp from a write that died — and every one of them
        /// arrives here as a name that must simply not become a day in the list.</para>
        /// </summary>
        public static ChatJournalPageRef FromStem(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return default;

            int sep = stem.IndexOf(STEM_SEPARATOR, StringComparison.Ordinal);
            if (sep <= 0) return default;

            string date = stem.Substring(0, sep);
            if (!IsCalendarDate(date)) return default;

            string dayDigits = stem.Substring(sep + STEM_SEPARATOR.Length);
            if (dayDigits.Length == 0) return default;
            if (!int.TryParse(dayDigits, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int inGameDay))
                return default;

            return new ChatJournalPageRef(
                ChatDayClock.BuildKey(date, inGameDay), date, inGameDay, stem);
        }

        /// <summary>
        /// Newest first, which is the order the panel opens on: the day a player wants is
        /// almost always the last one, and a list that starts at the beginning of time makes
        /// them walk the whole archive to reach it.
        /// </summary>
        public int CompareTo(ChatJournalPageRef other)
        {
            int byDate = string.CompareOrdinal(other.CalendarDate, CalendarDate);
            if (byDate != 0) return byDate;
            return other.InGameDay.CompareTo(InGameDay);
        }

        /// <summary>
        /// How this page reads in the day selector: the date, plus the in-game day when there
        /// is one to name.
        ///
        /// <para>The in-game day is suppressed at 0 rather than shown as "Día 0", because 0
        /// is what <see cref="ChatDayClock.InGameDay"/> answers when NO cycle is running —
        /// a conversation in a scene with no day/night, or in a test. Printing it would
        /// invent a day that the game never told the player about.</para>
        /// </summary>
        public string Label(bool english)
        {
            if (!IsValid) return "";
            if (InGameDay <= 0) return CalendarDate;
            return CalendarDate + (english ? " · Day " : " · Día ") + InGameDay;
        }

        public override string ToString() => IsValid ? Stem : "<invalid page>";

        // ── Stem encoding ───────────────────────────────────────────────────

        /// <summary>
        /// What separates the two halves of a stem. Not <c>#</c>, which
        /// <see cref="ChatDayClock"/> uses and which <c>ChatPersistencePaths.Slugify</c>
        /// would leave alone but which is awkward in a shell and in a URL, and not a bare
        /// <c>_</c>, which appears inside neither half and so cannot be confused for one.
        /// </summary>
        private const string STEM_SEPARATOR = "_d";

        /// <summary>
        /// Digits reserved for the in-game day. Enough that the stems of a normal save all
        /// sort correctly as plain strings too, which makes the directory readable by hand;
        /// the code never relies on it (see <see cref="CompareTo"/>).
        /// </summary>
        private const int DAY_DIGITS = 5;

        private static string BuildStem(string calendarDate, int inGameDay)
        {
            // Negative is not reachable from DayCount, and clamping is still the right answer
            // for a hand-edited key: a minus sign in a file name is legal and sorts wrongly.
            int day = inGameDay < 0 ? 0 : inGameDay;
            return calendarDate + STEM_SEPARATOR +
                   day.ToString(new string('0', DAY_DIGITS), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether <paramref name="text"/> is a <c>yyyy-MM-dd</c> date. Parsed rather than
        /// pattern-matched, so <c>2026-13-45</c> is refused as well as <c>hello</c>.
        /// </summary>
        private static bool IsCalendarDate(string text) =>
            DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out _);
    }
}
