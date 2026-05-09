using UnityEngine;

namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Inputs to <see cref="IDungeonStrategy.TryGenerate"/>. Plain POCO so strategies
    /// can be unit-tested without Unity scene setup.
    /// </summary>
    public sealed class DungeonGenerationContext
    {
        /// <summary>World grid builder providing tilemap layer access for painting.</summary>
        public WorldGridBuilder GridBuilder { get; set; }

        /// <summary>Tile X offset of the dungeon zone in world coords.</summary>
        public int DungeonOffsetX { get; set; }

        /// <summary>Tile Y offset of the dungeon zone in world coords.</summary>
        public int DungeonOffsetY { get; set; }

        /// <summary>Tile X offset of the lobby zone (for connector tunnel).</summary>
        public int LobbyOffsetX { get; set; }

        /// <summary>Tile Y offset of the lobby zone (for connector tunnel).</summary>
        public int LobbyOffsetY { get; set; }

        /// <summary>Height of each zone in tiles (Y-flip helper).</summary>
        public int ZoneHeight { get; set; }

        /// <summary>RNG seed. -1 for random.</summary>
        public int Seed { get; set; } = -1;

        /// <summary>Active world / map-slot identifier (informational).</summary>
        public string WorldSlug { get; set; }

        /// <summary>Optional parent transform for any spawned GameObjects.</summary>
        public Transform SceneContainer { get; set; }
    }
}
