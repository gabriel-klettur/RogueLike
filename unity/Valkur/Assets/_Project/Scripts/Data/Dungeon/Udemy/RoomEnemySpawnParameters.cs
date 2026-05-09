using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Spawn budget and pacing for one (room, dungeon-level) tuple. The room
    /// enemy spawner uses these min/max ranges to roll concrete numbers when
    /// the player enters the room for the first time.
    /// </summary>
    [System.Serializable]
    public class RoomEnemySpawnParameters
    {
        [Tooltip("Dungeon level these parameters apply to.")]
        public DungeonLevelSO dungeonLevel;

        [Tooltip("Minimum total enemies that will spawn over the lifetime of this room.")]
        [Min(0)] public int minTotalEnemiesToSpawn;

        [Tooltip("Maximum total enemies that will spawn over the lifetime of this room.")]
        [Min(0)] public int maxTotalEnemiesToSpawn;

        [Tooltip("Minimum number of enemies alive simultaneously.")]
        [Min(0)] public int minConcurrentEnemies;

        [Tooltip("Maximum number of enemies alive simultaneously.")]
        [Min(0)] public int maxConcurrentEnemies;

        [Tooltip("Minimum delay (seconds) between spawn attempts.")]
        [Min(0f)] public float minSpawnInterval;

        [Tooltip("Maximum delay (seconds) between spawn attempts.")]
        [Min(0f)] public float maxSpawnInterval;
    }
}
