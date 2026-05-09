using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Optional add-on consulted by <see cref="PathFinder"/> on every A* expansion
    /// to add per-cell movement cost on top of the base unit step. When no
    /// provider is set, the path finder behaves exactly as before (cost = 1 per
    /// cardinal step). Used by the Udemy dungeon system to encode "preferred
    /// path" tiles inside rooms (smaller penalty) versus default tiles.
    ///
    /// Walkable/blocked is decided separately via <c>Physics2D.OverlapCircle</c>
    /// against the World/Building layers — providers DO NOT influence that.
    /// </summary>
    public interface IPathFinderPenaltyProvider
    {
        /// <summary>
        /// Additional movement cost (≥ 0) for entering <paramref name="worldCell"/>.
        /// Returning 0 keeps the legacy uniform-cost behavior.
        /// </summary>
        int GetExtraPenalty(Vector2Int worldCell);
    }
}
