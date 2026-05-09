using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Outputs from <see cref="IDungeonStrategy.TryGenerate"/>. Both strategies
    /// return rooms as <see cref="RectInt"/> bounds in world tile coordinates so
    /// downstream consumers (minimap, A*, debug overlay) don't need to know
    /// which strategy produced them.
    /// </summary>
    public sealed class DungeonGenerationResult
    {
        /// <summary>True when the strategy generated a usable dungeon.</summary>
        public bool Success { get; set; }

        /// <summary>Room bounds in world tile coordinates (Unity Y-up).</summary>
        public IReadOnlyList<RectInt> RoomBounds { get; set; }

        /// <summary>Tile coordinates of the entrance (player spawn target).</summary>
        public Vector2Int EntrancePosition { get; set; }

        /// <summary>Number of tunnel tiles carved (BSP) or 0 (Udemy uses doorways).</summary>
        public int ConnectingTunnelTileCount { get; set; }

        /// <summary>
        /// Failure reason filled when <see cref="Success"/> is false. Empty otherwise.
        /// </summary>
        public string FailureReason { get; set; } = string.Empty;

        /// <summary>Convenience empty result for failed runs.</summary>
        public static DungeonGenerationResult Failed(string reason)
        {
            return new DungeonGenerationResult
            {
                Success = false,
                RoomBounds = System.Array.Empty<RectInt>(),
                FailureReason = reason ?? string.Empty,
            };
        }
    }
}
