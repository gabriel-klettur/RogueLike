using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>Where a tree grows and which family it is — the family is what the ground there supports.</summary>
    public readonly struct WorldTreeSite
    {
        public readonly Vector2Int Tile;
        public readonly TreeFamily Family;

        public WorldTreeSite(Vector2Int tile, TreeFamily family)
        {
            Tile = tile;
            Family = family;
        }
    }
}
