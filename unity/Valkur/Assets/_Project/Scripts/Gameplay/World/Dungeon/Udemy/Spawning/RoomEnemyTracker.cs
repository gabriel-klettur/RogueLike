using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World.Dungeon.Udemy.Runtime;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Spawning
{
    /// <summary>
    /// Tracks how many enemies are still alive per room. When the count
    /// drops to zero, fires <see cref="GameEvents.OnRoomEnemiesDefeated"/>
    /// and tells the matching <see cref="InstantiatedRoom"/> to unlock its doors.
    ///
    /// Enemy registration is explicit: the spawner calls <see cref="OnEnemySpawned"/>
    /// (with a stable enemy id + roomId) and <see cref="OnEnemyKilled"/> when the
    /// enemy dies. This keeps the tracker decoupled from Valkur's combat events
    /// and makes EditMode tests trivial.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomEnemyTracker : MonoBehaviour
    {
        // roomId → set of alive enemy ids.
        private readonly Dictionary<string, HashSet<string>> _aliveByRoomId
            = new Dictionary<string, HashSet<string>>();

        public int LiveCount(string roomId)
        {
            return _aliveByRoomId.TryGetValue(roomId, out var alive) ? alive.Count : 0;
        }

        public void OnEnemySpawned(string roomId, string enemyId)
        {
            if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(enemyId)) return;
            if (!_aliveByRoomId.TryGetValue(roomId, out var alive))
            {
                alive = new HashSet<string>();
                _aliveByRoomId[roomId] = alive;
            }
            alive.Add(enemyId);
        }

        public void OnEnemyKilled(string roomId, string enemyId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (!_aliveByRoomId.TryGetValue(roomId, out var alive)) return;
            if (!alive.Remove(enemyId)) return;
            if (alive.Count > 0) return;

            // All enemies in this room defeated — close the per-room set,
            // fire the public event, and unlock the room's doors.
            _aliveByRoomId.Remove(roomId);

            var room = RoomRegistry.Get(roomId);
            if (room != null)
            {
                room.isClearedOfEnemies = true;
                if (room.instantiatedRoom is InstantiatedRoom inst)
                    inst.UnlockDoors();
            }

            GameEvents.FireRoomEnemiesDefeated(roomId);
        }

        /// <summary>Drop all per-room state. Call on dungeon teardown.</summary>
        public void Clear() => _aliveByRoomId.Clear();
    }
}
