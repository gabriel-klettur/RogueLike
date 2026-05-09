using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;
using Valkur.Gameplay.World.Dungeon.Udemy.Spawning;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Spawning
{
    public class RoomEnemySpawnerAdapterTests
    {
        private GameObject _go;
        private RoomEnemySpawnerAdapter _adapter;
        private RecordingSpawner _spawner;
        private DungeonLevelSO _level;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Adapter");
            _adapter = _go.AddComponent<RoomEnemySpawnerAdapter>();
            _spawner = new RecordingSpawner();
            _adapter.Spawner = _spawner;
            _level = ScriptableObject.CreateInstance<DungeonLevelSO>();
            _adapter.ActiveLevel = _level;
            RoomRegistry.Clear();
            GameEvents.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_level);
            RoomRegistry.Clear();
            GameEvents.Clear();
        }

        [Test]
        public void TestTrigger_RoomWithMatchingLevel_DelegatesToSpawner()
        {
            var room = MakeRoomWithSpawnConfig(_level, totalEnemies: 3, ratio: ("goblin", 1));
            _spawner.NextSpawnReturnValue = 3;

            int spawned = _adapter.TestTriggerForRoom(room);

            Assert.AreEqual(3, spawned);
            Assert.AreEqual(1, _spawner.Calls.Count);
            Assert.AreEqual(room, _spawner.Calls[0].Room);
            Assert.AreEqual(3, _spawner.Calls[0].Parameters.minTotalEnemiesToSpawn);
            Assert.AreEqual("goblin", _spawner.Calls[0].Pool[0].enemyTemplateId);
        }

        [Test]
        public void TestTrigger_RoomWithoutMatchingLevel_FallbackToFirstEntry_StillSpawns()
        {
            // Adapter.ActiveLevel = null path keeps the dev workflow open.
            _adapter.ActiveLevel = null;
            var room = MakeRoomWithSpawnConfig(_level, totalEnemies: 2, ratio: ("orc", 1));
            _spawner.NextSpawnReturnValue = 2;

            _adapter.TestTriggerForRoom(room);

            Assert.AreEqual(1, _spawner.Calls.Count);
        }

        [Test]
        public void TestTrigger_SpawnerReturnsZero_FiresRoomEnemiesDefeated_AndMarksCleared()
        {
            string firedRoomId = null;
            GameEvents.OnRoomEnemiesDefeated += id => firedRoomId = id;

            var room = MakeRoomWithSpawnConfig(_level, totalEnemies: 0, ratio: ("orc", 1));
            _spawner.NextSpawnReturnValue = 0;

            _adapter.TestTriggerForRoom(room);

            Assert.AreEqual(room.id, firedRoomId);
            Assert.IsTrue(room.isClearedOfEnemies);
        }

        [Test]
        public void TestTrigger_SecondCallForSameRoom_NoOp()
        {
            var room = MakeRoomWithSpawnConfig(_level, totalEnemies: 2, ratio: ("orc", 1));
            _spawner.NextSpawnReturnValue = 2;

            _adapter.TestTriggerForRoom(room);
            _adapter.TestTriggerForRoom(room);

            Assert.AreEqual(1, _spawner.Calls.Count);
            Assert.IsTrue(_adapter.HasSpawnedFor(room.id));
        }

        [Test]
        public void TestTrigger_NullRoom_NoOp()
        {
            Assert.AreEqual(0, _adapter.TestTriggerForRoom(null));
            Assert.AreEqual(0, _spawner.Calls.Count);
        }

        [Test]
        public void HandleRoomChanged_ResolvesViaRoomRegistry()
        {
            var room = MakeRoomWithSpawnConfig(_level, totalEnemies: 1, ratio: ("orc", 1));
            _spawner.NextSpawnReturnValue = 1;
            RoomRegistry.Register(room);

            // EditMode skips OnEnable; subscribe manually.
            _adapter.Subscribe();
            try
            {
                GameEvents.FireRoomChanged(room.id, new RectInt(0, 0, 1, 1), Vector2Int.zero, isClearedOfEnemies: false);
                Assert.AreEqual(1, _spawner.Calls.Count);
            }
            finally
            {
                _adapter.Unsubscribe();
            }
        }

        // Helpers ─────────────────────────────────────────────────────────

        private static Room MakeRoomWithSpawnConfig(
            DungeonLevelSO level, int totalEnemies, (string id, int weight) ratio)
        {
            var room = new Room { id = System.Guid.NewGuid().ToString() };
            room.roomLevelEnemySpawnParametersList = new List<RoomEnemySpawnParameters>
            {
                new RoomEnemySpawnParameters
                {
                    dungeonLevel = level,
                    minTotalEnemiesToSpawn = totalEnemies,
                    maxTotalEnemiesToSpawn = totalEnemies,
                    minConcurrentEnemies = 1,
                    maxConcurrentEnemies = 3,
                    minSpawnInterval = 0.5f,
                    maxSpawnInterval = 1f,
                },
            };
            room.enemiesByLevelList = new List<SpawnableEnemyByLevel>
            {
                new SpawnableEnemyByLevel
                {
                    dungeonLevel = level,
                    spawnableEnemyRatioList = new List<SpawnableEnemyRatio>
                    {
                        new SpawnableEnemyRatio { enemyTemplateId = ratio.id, ratio = ratio.weight },
                    },
                },
            };
            return room;
        }

        private sealed class RecordingSpawner : IRoomEnemySpawner
        {
            public int NextSpawnReturnValue;
            public readonly List<Call> Calls = new List<Call>();

            public int Spawn(Room room, RoomEnemySpawnParameters parameters,
                IReadOnlyList<SpawnableEnemyRatio> enemyPool)
            {
                Calls.Add(new Call { Room = room, Parameters = parameters, Pool = enemyPool });
                return NextSpawnReturnValue;
            }

            public sealed class Call
            {
                public Room Room;
                public RoomEnemySpawnParameters Parameters;
                public IReadOnlyList<SpawnableEnemyRatio> Pool;
            }
        }
    }
}
