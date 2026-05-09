using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Inputs to <see cref="DungeonBuilder.GenerateDungeon"/>. Plain POCO so
    /// callers (UdemyDungeonStrategy, EditMode tests) can configure a build
    /// without coupling to MonoBehaviours.
    /// </summary>
    public sealed class DungeonBuildRequest
    {
        public DungeonLevelSO Level { get; set; }

        /// <summary>Master node-type list. Used to resolve isEntrance / isCorridorNS / etc.</summary>
        public RoomNodeTypeListSO NodeTypeList { get; set; }

        /// <summary>Designer-tunable retry counts and A* settings.</summary>
        public DungeonConfigSO Config { get; set; }

        /// <summary>RNG seed. -1 for random.</summary>
        public int Seed { get; set; } = -1;
    }
}
