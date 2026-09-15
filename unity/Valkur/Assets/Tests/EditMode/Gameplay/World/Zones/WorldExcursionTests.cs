using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Zones
{
    /// <summary>
    /// The return ticket of a trip out of Pepitoria.
    ///
    /// <para>Pepitoria is the hub (project decision, 2026-09-14): every other map is reached from it
    /// and returns to it. Before the ticket, a session in a generated world autosaved the player in
    /// "Pueblo inicial" — a zone the base world does not have — and leaving that world put them
    /// wherever a slot file happened to remember. Pinned here: home is the FIRST step out, arriving
    /// spends it, a save taken away records home, and a test run never writes the ticket file.</para>
    /// </summary>
    [TestFixture]
    public class WorldExcursionTests
    {
        private static readonly Vector2 Home = new Vector2(171.5f, 62.25f);

        [SetUp]
        public void SetUp() => WorldExcursion.ResetForTests();

        [TearDown]
        public void TearDown() => WorldExcursion.ResetForTests();

        [Test]
        public void AtHome_ThereIsNoTrip()
        {
            Assert.IsFalse(WorldExcursion.IsAway);
            Assert.IsNull(WorldExcursion.Destination);
            Assert.IsFalse(WorldExcursion.TryGetHome(out _, out _));
            Assert.IsFalse(WorldExcursion.TryArriveHome(out _, out _),
                "Arriving home with no trip must answer false, so a plain reload of Pepitoria keeps its own spawn.");
        }

        [Test]
        public void HoppingBetweenOtherMaps_KeepsTheFirstStepOutAsHome()
        {
            WorldExcursion.Leave(Home, "Lobby", "partida_1");
            WorldExcursion.Leave(new Vector2(-180f, -170f), "Pueblo inicial", "partida_2");

            Assert.IsTrue(WorldExcursion.TryGetHome(out var home, out var zone));
            Assert.AreEqual(Home, home, "Home is where the player left PEPITORIA from, not the last map they left.");
            Assert.AreEqual("Lobby", zone);
            Assert.AreEqual("partida_2", WorldExcursion.Destination, "The destination follows the player.");
        }

        [Test]
        public void ArrivingHome_SpendsTheTicket_AndLandsOnIt()
        {
            WorldExcursion.Leave(Home, "Lobby", "partida_1");

            Assert.IsTrue(WorldExcursion.TryArriveHome(out var at, out var zone));
            Assert.AreEqual(Home, at);
            Assert.AreEqual("Lobby", zone);
            Assert.IsFalse(WorldExcursion.IsAway, "A ticket is spent by the trip back — a second arrival must not re-teleport.");
        }

        [Test]
        public void DuringATestRun_TheTicketNeverTouchesTheUsersDisk()
        {
            string path = Path.Combine(Application.persistentDataPath, "Maps", WorldExcursion.FileName);
            bool existedBefore = File.Exists(path);
            System.DateTime stampBefore = existedBefore ? File.GetLastWriteTimeUtc(path) : default;

            WorldExcursion.Leave(Home, "Lobby", "partida_1");
            WorldExcursion.Discard();

            Assert.AreEqual(existedBefore, File.Exists(path),
                "A fixture leaving Pepitoria must not create (or delete) the real ticket: the next Play session " +
                "would read it and move the user's player.");
            if (existedBefore) Assert.AreEqual(stampBefore, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void AwayFromHome_IsABaseWorldRecordAtHome()
        {
            var record = PlayerPositionPersistence.AwayFromHome(Home, "Lobby");

            Assert.IsTrue(record.IsBaseWorld, "Home is a Pepitoria position — the checkpoint must be allowed to write it.");
            Assert.AreEqual(Home, record.Position);
            Assert.AreEqual("Lobby", record.Zone);
            Assert.AreEqual(PlayerPositionPersistence.Source.ExcursionHome, record.From);
        }

        [Test]
        public void ASaveTakenAwayFromPepitoria_RecordsHome_NotTheOtherMap()
        {
            var go = new GameObject("ExcursionSaveService");
            try
            {
                var service = go.AddComponent<SaveService>();
                WorldExcursion.Leave(Home, "Lobby", "partida_1");

                var record = service.ResolvePersistablePlayerPosition(new Vector2(14.7f, 28.5f), "Pueblo inicial");

                Assert.AreEqual(Home, record.Position,
                    "Away on another map the save must record the spot the trip left Pepitoria from. It recorded " +
                    "(14.7, 28.5) in 'Pueblo inicial' once, a zone the base world does not have.");
                Assert.AreEqual("Lobby", record.Zone);
                Assert.IsTrue(record.IsBaseWorld);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ASaveTakenAtHome_RecordsTheLivePosition()
        {
            var go = new GameObject("HomeSaveService");
            try
            {
                var service = go.AddComponent<SaveService>();
                var live = new Vector2(180f, 70f);

                var record = service.ResolvePersistablePlayerPosition(live, "Lobby");

                Assert.AreEqual(live, record.Position, "With no trip the ordinary rule stands.");
                Assert.AreEqual(PlayerPositionPersistence.Source.Live, record.From);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
