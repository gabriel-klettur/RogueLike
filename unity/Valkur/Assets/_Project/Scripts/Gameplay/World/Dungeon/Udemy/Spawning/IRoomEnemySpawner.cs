using System.Collections.Generic;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Spawning
{
    /// <summary>
    /// Plug point for the actual enemy-spawning implementation. <see cref="RoomEnemySpawnerAdapter"/>
    /// resolves the active <see cref="IRoomEnemySpawner"/> at runtime — Phase 6 ships only an
    /// abstract contract + a no-op default; Phase 7 wires the concrete implementation against
    /// <c>SpawnerTemplateCatalog</c> + <c>SpawnerInstance</c>.
    /// </summary>
    public interface IRoomEnemySpawner
    {
        /// <summary>
        /// Spawn enemies for a room when the player first enters it. Implementations
        /// should respect the per-level budget in <paramref name="parameters"/>
        /// (min/max total, min/max concurrent, intervals) and pick enemy template ids
        /// from the weighted ratios in <paramref name="enemyPool"/>.
        /// </summary>
        /// <returns>Number of enemies the implementation committed to spawning.</returns>
        int Spawn(
            Room room,
            RoomEnemySpawnParameters parameters,
            IReadOnlyList<SpawnableEnemyRatio> enemyPool);
    }

    /// <summary>
    /// Default no-op spawner. Used until Phase 7 installs the real one.
    /// Returns 0 so rooms with no spawn implementation get auto-cleared.
    /// </summary>
    public sealed class NoopRoomEnemySpawner : IRoomEnemySpawner
    {
        public int Spawn(
            Room room,
            RoomEnemySpawnParameters parameters,
            IReadOnlyList<SpawnableEnemyRatio> enemyPool) => 0;
    }
}
