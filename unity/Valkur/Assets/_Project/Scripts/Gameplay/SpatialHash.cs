using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Spatial hash grid for broad-phase collision/proximity queries.
    /// Maps to Python's SpatialHash used in NpcSeparationSystem and MovementCollisionSystem.
    /// O(N) insert, O(K) query where K is nearby entities (~4-8).
    /// </summary>
    public class SpatialHash<T>
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<(T item, Vector2 pos)>> _cells = new Dictionary<long, List<(T, Vector2)>>();

        public SpatialHash(float cellSize = 2f)
        {
            _cellSize = cellSize;
        }

        public void Clear()
        {
            foreach (var cell in _cells.Values)
                cell.Clear();
        }

        public void Insert(T item, Vector2 position)
        {
            long key = CellKey(position);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<(T, Vector2)>(8);
                _cells[key] = list;
            }
            list.Add((item, position));
        }

        /// <summary>
        /// Query all items within radius of position.
        /// Returns items from the 9 surrounding cells, filtered by actual distance.
        /// </summary>
        public void QueryRadius(Vector2 center, float radius, List<(T item, Vector2 pos)> results)
        {
            results.Clear();
            float radiusSq = radius * radius;
            int minX = Mathf.FloorToInt((center.x - radius) / _cellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / _cellSize);
            int minY = Mathf.FloorToInt((center.y - radius) / _cellSize);
            int maxY = Mathf.FloorToInt((center.y + radius) / _cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    long key = PackKey(x, y);
                    if (!_cells.TryGetValue(key, out var list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        float distSq = (list[i].pos - center).sqrMagnitude;
                        if (distSq <= radiusSq)
                            results.Add(list[i]);
                    }
                }
            }
        }

        private long CellKey(Vector2 pos)
        {
            int cx = Mathf.FloorToInt(pos.x / _cellSize);
            int cy = Mathf.FloorToInt(pos.y / _cellSize);
            return PackKey(cx, cy);
        }

        private static long PackKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }
    }
}
