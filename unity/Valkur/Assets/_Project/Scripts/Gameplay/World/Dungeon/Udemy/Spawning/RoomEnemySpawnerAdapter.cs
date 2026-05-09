using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Spawning
{
    /// <summary>
    /// Listens to <see cref="GameEvents.OnRoomChanged"/> and triggers enemy
    /// spawning for the freshly-entered room. Resolves the room's spawn
    /// parameters from its template (matching <see cref="DungeonLevelSO"/>)
    /// and forwards them to the active <see cref="IRoomEnemySpawner"/>.
    ///
    /// The adapter only fires once per room on first visit. Re-entering an
    /// already-cleared room is a no-op (the tracker/door state takes over).
    /// Active room/level wiring lives outside this MonoBehaviour: Phase 7
    /// installs the real spawner; tests inject a fake one.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomEnemySpawnerAdapter : MonoBehaviour
    {
        public IRoomEnemySpawner Spawner { get; set; } = new NoopRoomEnemySpawner();

        /// <summary>Active dungeon level — used to look up per-level enemy pools.</summary>
        public DungeonLevelSO ActiveLevel { get; set; }

        private readonly HashSet<string> _alreadySpawned = new HashSet<string>();

        private bool _subscribed;

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        /// <summary>
        /// Manually wire the adapter to the GameEvents bus. Useful for EditMode
        /// tests where OnEnable doesn't fire automatically.
        /// </summary>
        public void Subscribe()
        {
            if (_subscribed) return;
            GameEvents.OnRoomChanged += HandleRoomChanged;
            _subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!_subscribed) return;
            GameEvents.OnRoomChanged -= HandleRoomChanged;
            _subscribed = false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Test hooks — let EditMode tests drive the adapter directly without
        // requiring a fully wired GameEvents pipeline.
        // ─────────────────────────────────────────────────────────────────

        public int TestTriggerForRoom(Room room) => SpawnForRoom(room);
        public bool HasSpawnedFor(string roomId) => _alreadySpawned.Contains(roomId);
        public void TestResetSpawnHistory() => _alreadySpawned.Clear();

        private void HandleRoomChanged(string roomId, RectInt bounds, Vector2Int entrance, bool isCleared)
        {
            if (isCleared) return;
            if (_alreadySpawned.Contains(roomId)) return;

            var room = RoomRegistry.Get(roomId);
            if (room == null) return;

            SpawnForRoom(room);
        }

        private int SpawnForRoom(Room room)
        {
            if (room == null || string.IsNullOrEmpty(room.id)) return 0;
            if (_alreadySpawned.Contains(room.id)) return 0;

            // Phase-1 templates carry a flat list of RoomEnemySpawnParameters
            // keyed by DungeonLevelSO. Match against ActiveLevel; fall back to
            // the first entry when unset (keeps the no-level-management dev path open).
            var parameters = ResolveSpawnParameters(room);
            var pool = ResolveEnemyPool(room);

            int spawned = 0;
            if (Spawner != null && parameters != null && pool != null)
                spawned = Spawner.Spawn(room, parameters, pool);

            _alreadySpawned.Add(room.id);

            // If the spawner committed nothing (zero enemies, or no level config), the
            // room is effectively cleared — fire the defeated event so doors unlock.
            if (spawned <= 0)
            {
                room.isClearedOfEnemies = true;
                GameEvents.FireRoomEnemiesDefeated(room.id);
            }
            return spawned;
        }

        private RoomEnemySpawnParameters ResolveSpawnParameters(Room room)
        {
            if (room.roomLevelEnemySpawnParametersList == null) return null;
            for (int i = 0; i < room.roomLevelEnemySpawnParametersList.Count; i++)
            {
                var entry = room.roomLevelEnemySpawnParametersList[i];
                if (entry == null) continue;
                if (ActiveLevel == null || entry.dungeonLevel == ActiveLevel) return entry;
            }
            return null;
        }

        private IReadOnlyList<SpawnableEnemyRatio> ResolveEnemyPool(Room room)
        {
            if (room.enemiesByLevelList == null) return null;
            for (int i = 0; i < room.enemiesByLevelList.Count; i++)
            {
                var entry = room.enemiesByLevelList[i];
                if (entry == null) continue;
                if (ActiveLevel == null || entry.dungeonLevel == ActiveLevel)
                    return entry.spawnableEnemyRatioList;
            }
            return null;
        }
    }
}
