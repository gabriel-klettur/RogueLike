using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World.Dungeon.Udemy.Spawning;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Spawning
{
    public class RoomEnemyTrackerTests
    {
        private GameObject _go;
        private RoomEnemyTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Tracker");
            _tracker = _go.AddComponent<RoomEnemyTracker>();
            RoomRegistry.Clear();
            // Defensive: ensure no prior subscriber leaked into the static event.
            GameEvents.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            RoomRegistry.Clear();
            GameEvents.Clear();
        }

        [Test]
        public void OnEnemySpawned_IncrementsLiveCount()
        {
            _tracker.OnEnemySpawned("r1", "e1");
            _tracker.OnEnemySpawned("r1", "e2");
            Assert.AreEqual(2, _tracker.LiveCount("r1"));
        }

        [Test]
        public void OnEnemyKilled_DecrementsLiveCount()
        {
            _tracker.OnEnemySpawned("r1", "e1");
            _tracker.OnEnemySpawned("r1", "e2");
            _tracker.OnEnemyKilled("r1", "e1");
            Assert.AreEqual(1, _tracker.LiveCount("r1"));
        }

        [Test]
        public void OnEnemyKilled_LastKill_FiresRoomEnemiesDefeatedEvent()
        {
            string firedRoom = null;
            GameEvents.OnRoomEnemiesDefeated += id => firedRoom = id;

            _tracker.OnEnemySpawned("r1", "e1");
            _tracker.OnEnemyKilled("r1", "e1");

            Assert.AreEqual("r1", firedRoom);
            Assert.AreEqual(0, _tracker.LiveCount("r1"));
        }

        [Test]
        public void OnEnemyKilled_WhenNotLastKill_DoesNotFireEvent()
        {
            int fireCount = 0;
            GameEvents.OnRoomEnemiesDefeated += _ => fireCount++;

            _tracker.OnEnemySpawned("r1", "e1");
            _tracker.OnEnemySpawned("r1", "e2");
            _tracker.OnEnemyKilled("r1", "e1");

            Assert.AreEqual(0, fireCount);
        }

        [Test]
        public void OnEnemyKilled_UnknownEnemy_DoesNotFire()
        {
            int fireCount = 0;
            GameEvents.OnRoomEnemiesDefeated += _ => fireCount++;
            _tracker.OnEnemyKilled("r1", "ghost");
            Assert.AreEqual(0, fireCount);
        }

        [Test]
        public void Clear_ResetsAllRooms()
        {
            _tracker.OnEnemySpawned("r1", "e1");
            _tracker.OnEnemySpawned("r2", "e2");
            _tracker.Clear();
            Assert.AreEqual(0, _tracker.LiveCount("r1"));
            Assert.AreEqual(0, _tracker.LiveCount("r2"));
        }
    }
}
