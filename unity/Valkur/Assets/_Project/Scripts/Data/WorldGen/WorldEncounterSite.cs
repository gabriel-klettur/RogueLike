using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Where a hostile encounter goes, and how dangerous the ground there is: 0 beside the starting
    /// town, 1 at the far edge of the world. The Gameplay side turns that into a spawner preset and
    /// a level bonus; this side only knows geography.
    /// </summary>
    public readonly struct WorldEncounterSite
    {
        public readonly Vector2Int Tile;
        public readonly float Difficulty;

        public WorldEncounterSite(Vector2Int tile, float difficulty)
        {
            Tile = tile;
            Difficulty = difficulty;
        }
    }
}
