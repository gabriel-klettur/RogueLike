using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Runtime
{
    /// <summary>
    /// Glue between the Udemy <c>aStarMovementPenalty</c> matrix (per-room,
    /// template-local indices) and Valkur's <see cref="PathFinder"/> (queries
    /// world-space cells). Each registered room contributes a sparse Dictionary
    /// of (worldCell → extraPenalty); <see cref="GetExtraPenalty"/> consults the
    /// merged map on every A* neighbor expansion.
    ///
    /// Typical lifecycle: <see cref="UdemyDungeonStrategy"/> creates one bridge
    /// per dungeon, calls <see cref="RegisterRoom"/> after stamping each room,
    /// and <see cref="UnregisterRoom"/> + <see cref="DetachFromPathFinder"/> on
    /// cleanup.
    /// </summary>
    public sealed class RoomPathfindingBridge : IPathFinderPenaltyProvider
    {
        // worldCell → accumulated extra penalty (0 = no override).
        private readonly Dictionary<Vector2Int, int> _penaltiesByWorldCell
            = new Dictionary<Vector2Int, int>();

        private readonly Dictionary<string, List<Vector2Int>> _cellsByRoomId
            = new Dictionary<string, List<Vector2Int>>();

        public int RegisteredRoomCount => _cellsByRoomId.Count;
        public int RegisteredCellCount => _penaltiesByWorldCell.Count;

        public int GetExtraPenalty(Vector2Int worldCell)
        {
            return _penaltiesByWorldCell.TryGetValue(worldCell, out int p) ? p : 0;
        }

        /// <summary>
        /// Project a per-room penalty matrix (template-local indices) into the
        /// world-cell map. The default penalty (<see cref="defaultPenalty"/>) is
        /// treated as "no override" and skipped — only cells that differ from
        /// the default contribute (typically preferred paths with penalty=1).
        /// </summary>
        public void RegisterRoom(Room room, int[,] penalty, int defaultPenalty)
        {
            if (room == null || penalty == null) return;

            // Replace any prior registration for the same room id.
            UnregisterRoom(room);

            int width = penalty.GetLength(0);
            int height = penalty.GetLength(1);
            var cells = new List<Vector2Int>();
            int worldOriginX = room.lowerBounds.x;
            int worldOriginY = room.lowerBounds.y;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int cellPenalty = penalty[x, y];
                    // Skip default — Physics2D walkability already handles unwalkable.
                    if (cellPenalty == defaultPenalty) continue;
                    if (cellPenalty <= 0) continue;

                    var worldCell = new Vector2Int(worldOriginX + x, worldOriginY + y);
                    _penaltiesByWorldCell[worldCell] = cellPenalty;
                    cells.Add(worldCell);
                }
            }

            if (cells.Count > 0) _cellsByRoomId[room.id] = cells;
        }

        public void UnregisterRoom(Room room)
        {
            if (room == null || string.IsNullOrEmpty(room.id)) return;
            if (!_cellsByRoomId.TryGetValue(room.id, out var cells)) return;

            for (int i = 0; i < cells.Count; i++)
                _penaltiesByWorldCell.Remove(cells[i]);

            _cellsByRoomId.Remove(room.id);
        }

        public void Clear()
        {
            _penaltiesByWorldCell.Clear();
            _cellsByRoomId.Clear();
        }

        /// <summary>
        /// Wire this bridge into the live PathFinder singleton. Safe no-op when
        /// PathFinder isn't instantiated (EditMode tests, headless boot).
        /// </summary>
        public void AttachToPathFinder()
        {
            var pathFinder = PathFinder.Instance;
            if (pathFinder != null) pathFinder.SetPenaltyProvider(this);
        }

        public void DetachFromPathFinder()
        {
            var pathFinder = PathFinder.Instance;
            if (pathFinder != null) pathFinder.SetPenaltyProvider(null);
        }
    }
}
