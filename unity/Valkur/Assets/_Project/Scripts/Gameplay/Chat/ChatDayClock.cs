using System;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// What "today" means to a conversation.
    ///
    /// <para>A greeting is due once a day, and neither clock in this project can answer
    /// that alone. <c>DayNightCycle.DayCount</c> is the in-game day and is exactly right
    /// while a session runs — the sun coming up is when a neighbour greets you again — but
    /// it is NOT persisted (see the day/night roadmap), so it restarts at 0 on every Play.
    /// A record holding day 0 from yesterday would therefore match today's 0 and the
    /// character would never greet again. The real calendar alone has the opposite problem:
    /// a long session crossing three in-game dawns would greet once.</para>
    ///
    /// <para>So the key is BOTH, as text: <c>2026-09-05#3</c>. It is compared for
    /// INEQUALITY, never ordered — the in-game half legitimately goes backwards between
    /// sessions, and all a greeting needs to know is "is this a different day from the one
    /// I last said hello on".</para>
    ///
    /// <para>The calendar half is LOCAL time on purpose, unlike the ISO timestamps stored
    /// beside it: a day boundary the player experiences is midnight where they are, not in
    /// UTC.</para>
    /// </summary>
    public static class ChatDayClock
    {
        /// <summary>The key to stamp on a greeting delivered right now.</summary>
        public static string TodayKey => BuildKey(DateTime.Now, InGameDay);

        /// <summary>
        /// The in-game day, or 0 when no cycle is running (EditMode tests, a scene with no
        /// day/night). Zero is a legitimate value, not a sentinel: with no cycle the
        /// calendar half is the only thing that moves, which is the correct behaviour.
        /// </summary>
        public static int InGameDay =>
            DayNightCycle.HasInstance ? DayNightCycle.Instance.DayCount : 0;

        /// <summary>Composes a key from its two halves. Exposed for the tests.</summary>
        public static string BuildKey(DateTime localDate, int inGameDay) =>
            BuildKey(localDate.ToString("yyyy-MM-dd"), inGameDay);

        /// <summary>
        /// The same composition from a date that is ALREADY text.
        ///
        /// <para>It exists for the journal, which reconstructs a key from a page's file name
        /// and must not go through <c>DateTime</c> to do it: parsing to a date and formatting
        /// it back is a round trip through the local calendar, and a key is an opaque token
        /// compared for equality. Sharing this method is what guarantees a key built from a
        /// file name is byte-identical to the one <see cref="TodayKey"/> would have written.</para>
        /// </summary>
        public static string BuildKey(string calendarDate, int inGameDay) =>
            calendarDate + "#" + inGameDay.ToString();

        /// <summary>
        /// Whether a greeting stamped <paramref name="lastGreetedDayKey"/> is stale.
        /// An empty stamp means the character has never greeted this player at all.
        /// </summary>
        public static bool IsNewDay(string lastGreetedDayKey) =>
            string.IsNullOrEmpty(lastGreetedDayKey) ||
            !string.Equals(lastGreetedDayKey, TodayKey, StringComparison.Ordinal);
    }
}
