using UnityEngine;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>What a bake did, measured. <see cref="Error"/> is non-null when nothing was written.</summary>
    public sealed class SeedWorldBakeResult
    {
        public string Error;
        public string Slot;

        public int ZonesX;
        public int ZonesY;
        public int Tiles;
        public int BlockedTiles;
        public int Rivers;
        public int Towns;
        public int Buildings;

        /// <summary>Cells drawn as their majority terrain because no pack could draw their corners.</summary>
        public int HardCuts;

        /// <summary>Cells no pack could draw at all (left empty). Should be 0.</summary>
        public int MissingTiles;

        /// <summary>Vertices rewritten to bridge a pair of terrains no pack draws.</summary>
        public int RepairedVertices;

        /// <summary>World position the slot will teleport the player to.</summary>
        public Vector2 SpawnWorld;

        /// <summary>The world-space tile of the map's bottom-left corner.</summary>
        public Vector2Int Origin;

        public long GenerateMs;
        public long WriteMs;
        public long Bytes;

        public bool Succeeded => Error == null;

        public static SeedWorldBakeResult Fail(string error) => new SeedWorldBakeResult { Error = error };
    }
}
