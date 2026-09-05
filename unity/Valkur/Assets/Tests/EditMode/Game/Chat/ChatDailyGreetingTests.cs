using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for <see cref="ChatDayClock"/> and the v1 → v2 memory migration
    /// behind it.
    ///
    /// <para>The greeting used to be once per LIFETIME, so from the second visit on the
    /// panel opened in silence. Making it once per DAY needs a notion of "today" that
    /// neither clock in this project can supply alone: the in-game day is not persisted and
    /// restarts at 0 every Play, and the calendar cannot see an in-game dawn. The composite
    /// key is the answer, and the property that makes it safe is that it is only ever
    /// compared for INEQUALITY — the in-game half legitimately goes backwards between
    /// sessions.</para>
    /// </summary>
    [TestFixture]
    public class ChatDailyGreetingTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _root = Path.Combine(Path.GetTempPath(), "valkur-greet-" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _root;
        }

        [TearDown]
        public void TearDown()
        {
            ChatPersistencePaths.OverrideRoot = null;
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── The key ─────────────────────────────────────────────────────────

        [Test]
        public void BuildKey_CarriesBothHalves()
        {
            string key = ChatDayClock.BuildKey(new DateTime(2026, 9, 5), 3);

            StringAssert.Contains("2026-09-05", key, "The calendar half is what survives a restart.");
            StringAssert.Contains("3", key, "The in-game half is what re-greets after an in-game dawn.");
        }

        [Test]
        public void BuildKey_DiffersWhenEitherHalfMoves()
        {
            string monday = ChatDayClock.BuildKey(new DateTime(2026, 9, 5), 0);

            Assert.AreNotEqual(monday, ChatDayClock.BuildKey(new DateTime(2026, 9, 6), 0),
                "A new real day must re-arm the greeting even inside one endless in-game day.");
            Assert.AreNotEqual(monday, ChatDayClock.BuildKey(new DateTime(2026, 9, 5), 1),
                "An in-game dawn must re-arm it even inside one real afternoon.");
        }

        [Test]
        public void TodayKey_IsStableWithinAFrame()
        {
            Assert.AreEqual(ChatDayClock.TodayKey, ChatDayClock.TodayKey,
                "Two reads in the same session must agree, or the greeting fires on every open.");
        }

        [Test]
        public void IsNewDay_EmptyStamp_IsAlwaysNew()
        {
            Assert.IsTrue(ChatDayClock.IsNewDay(null), "A record that never greeted must greet.");
            Assert.IsTrue(ChatDayClock.IsNewDay(""), "An empty stamp is the same statement.");
        }

        [Test]
        public void IsNewDay_TodaysStamp_IsNotNew()
        {
            Assert.IsFalse(ChatDayClock.IsNewDay(ChatDayClock.TodayKey),
                "Walking away and coming back an hour later is not a new day.");
        }

        [Test]
        public void IsNewDay_YesterdaysStamp_IsNew()
        {
            string yesterday = ChatDayClock.BuildKey(DateTime.Now.AddDays(-1), ChatDayClock.InGameDay);

            Assert.IsTrue(ChatDayClock.IsNewDay(yesterday));
        }

        [Test]
        public void IsNewDay_AStampFromAHigherInGameDay_IsStillNew()
        {
            // The in-game counter restarts at 0 on every Play, so a stamp from last night
            // can be NUMERICALLY AHEAD of today's. Comparing with an ordering rather than
            // an inequality is how such a record would never greet again.
            string lastSession = ChatDayClock.BuildKey(DateTime.Now.AddDays(-1), 99);

            Assert.IsTrue(ChatDayClock.IsNewDay(lastSession));
        }

        // ── Migration ───────────────────────────────────────────────────────

        [Test]
        public void LoadOrCreate_V1Record_ComesBackUnstampedAndReadyToGreet()
        {
            // A v1 file, exactly as the shipped game wrote them: schema 1, a hasGreeted bit
            // and no day key at all.
            const string npcKey = "gatita-v1";
            Directory.CreateDirectory(ChatPersistencePaths.MemoryDirectory);
            File.WriteAllText(ChatPersistencePaths.MemoryPath(npcKey),
                "{\"schemaVersion\":1,\"npcKey\":\"" + npcKey + "\",\"personaId\":\"p\"," +
                "\"visitCount\":9,\"hasGreeted\":true,\"friendshipScore\":0," +
                "\"preferredLanguage\":\"es\",\"ephemeralHistory\":[]}");

            NPCMemory loaded = NPCMemoryStore.LoadOrCreate(npcKey, "p");

            Assert.AreEqual(NPCMemoryStore.CURRENT_SCHEMA_VERSION, loaded.schemaVersion,
                "A v1 record must be migrated on load, not left to be re-migrated forever.");
            Assert.IsTrue(string.IsNullOrEmpty(loaded.lastGreetedDayKey),
                "Stamping today on migration would suppress today's greeting for a character " +
                "who last said hello months ago. An empty stamp greets once, now.");
            Assert.IsTrue(ChatDayClock.IsNewDay(loaded.lastGreetedDayKey));
            Assert.AreEqual(9, loaded.visitCount, "Migration must not lose what v1 did hold.");
            Assert.IsNotNull(loaded.digest, "The new list must exist after a migration, not be null.");
        }

        [Test]
        public void SaveAndLoad_DayStamp_RoundTrips()
        {
            const string npcKey = "gatita-roundtrip";
            var memory = NPCMemoryStore.LoadOrCreate(npcKey, "p");
            memory.lastGreetedDayKey = ChatDayClock.TodayKey;
            NPCMemoryStore.Save(memory);

            NPCMemory reloaded = NPCMemoryStore.LoadOrCreate(npcKey, "p");

            Assert.AreEqual(memory.lastGreetedDayKey, reloaded.lastGreetedDayKey);
            Assert.IsFalse(ChatDayClock.IsNewDay(reloaded.lastGreetedDayKey),
                "Closing the game and coming back the same day must not re-greet — that is " +
                "the whole reason the stamp is persisted rather than held in memory.");
        }
    }
}
