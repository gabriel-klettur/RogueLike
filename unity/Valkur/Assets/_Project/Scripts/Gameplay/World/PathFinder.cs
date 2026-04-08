using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Grid-based A* pathfinder.
    /// Mirrors Python PathFinder (managers/map/pathfinding.py) exactly:
    /// Manhattan heuristic, cardinal-only movement, walkable query via tilemap.
    ///
    /// Usage:
    ///   PathFinder.Instance.FindPath(startWorld, goalWorld)
    ///   Returns a list of world-space waypoints. Empty list = no path found.
    ///
    /// Walkability is determined by Physics2D overlap (World + Building layers).
    /// No bake required — queries are live so tile changes are instant.
    /// </summary>
    public class PathFinder : Core.SingletonMonoBehaviour<PathFinder>
    {
        [Header("Grid")]
        [Tooltip("Tile size in world units (must match WorldGridBuilder PPU).")]
        [SerializeField] private float tileSize = 1f;

        [Tooltip("Radius used for walkable check via Physics2D overlap (slightly less than tileSize/2).")]
        [SerializeField] private float walkableRadius = 0.4f;

        [Header("Performance")]
        [Tooltip("Maximum nodes expanded per search. Prevents freeze on huge open maps.")]
        [SerializeField] private int maxNodes = 2000;

#pragma warning disable CS0414 // Serialized config field – used via Inspector
        [Tooltip("Maximum path length in tiles.")]
        [SerializeField] private int maxPathLength = 100;
#pragma warning restore CS0414

        // Layers that block NPC movement: World(11) + Building(14)
        private static readonly int BlockingMask = (1 << 11) | (1 << 14);

        protected override bool Persist => false;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Find a path in world space from <paramref name="start"/> to <paramref name="goal"/>.
        /// Returns world-space waypoint list (goal is last element). Empty = no path.
        /// </summary>
        public List<Vector2> FindPath(Vector2 start, Vector2 goal)
        {
            var startCell = WorldToCell(start);
            var goalCell  = WorldToCell(goal);

            if (startCell == goalCell)
                return new List<Vector2> { goal };

            if (!IsWalkable(goalCell))
                goalCell = FindNearestWalkable(goalCell, 3);

            var rawPath = AStar(startCell, goalCell);
            if (rawPath == null || rawPath.Count == 0)
                return new List<Vector2>();

            // Convert cells back to world-space centers
            var waypoints = new List<Vector2>(rawPath.Count);
            foreach (var cell in rawPath)
                waypoints.Add(CellToWorld(cell));

            // Always put actual goal position as last waypoint
            if (waypoints.Count > 0)
                waypoints[waypoints.Count - 1] = goal;

            return waypoints;
        }

        // ── A* implementation ────────────────────────────────────────────────

        private List<Vector2Int> AStar(Vector2Int start, Vector2Int goal)
        {
            var openSet = new SortedList<float, Vector2Int>(new DuplicateKeyComparer());
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float> { [start] = 0f };

            float startF = Heuristic(start, goal);
            openSet.Add(startF, start);

            int expanded = 0;

            while (openSet.Count > 0)
            {
                var current = openSet.Values[0];
                openSet.RemoveAt(0);

                if (current == goal)
                    return Reconstruct(cameFrom, current);

                if (++expanded > maxNodes)
                    break;

                foreach (var dir in Cardinals)
                {
                    var neighbor = current + dir;

                    if (!IsWalkable(neighbor)) continue;

                    float tentG = gScore[current] + 1f;
                    if (!gScore.TryGetValue(neighbor, out float oldG) || tentG < oldG)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentG;
                        float f = tentG + Heuristic(neighbor, goal);
                        openSet.Add(f, neighbor);
                    }
                }
            }

            return null; // no path
        }

        private static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom,
                                                     Vector2Int current)
        {
            var path = new List<Vector2Int> { current };
            while (cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }

        // ── Grid utilities ───────────────────────────────────────────────────

        private static readonly Vector2Int[] Cardinals =
        {
            new Vector2Int( 1,  0),
            new Vector2Int(-1,  0),
            new Vector2Int( 0,  1),
            new Vector2Int( 0, -1),
        };

        private static float Heuristic(Vector2Int a, Vector2Int b)
            => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private Vector2Int WorldToCell(Vector2 world)
            => new Vector2Int(Mathf.FloorToInt(world.x / tileSize),
                              Mathf.FloorToInt(world.y / tileSize));

        private Vector2 CellToWorld(Vector2Int cell)
            => new Vector2(cell.x * tileSize + tileSize * 0.5f,
                           cell.y * tileSize + tileSize * 0.5f);

        private bool IsWalkable(Vector2Int cell)
        {
            Vector2 center = CellToWorld(cell);
            return Physics2D.OverlapCircle(center, walkableRadius, BlockingMask) == null;
        }

        private Vector2Int FindNearestWalkable(Vector2Int cell, int searchRadius)
        {
            for (int r = 1; r <= searchRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var candidate = cell + new Vector2Int(dx, dy);
                    if (IsWalkable(candidate)) return candidate;
                }
            }
            return cell;
        }

        // ── Helper: SortedList doesn't allow duplicate keys ───────────────────
        private class DuplicateKeyComparer : IComparer<float>
        {
            public int Compare(float x, float y)
            {
                int result = x.CompareTo(y);
                return result == 0 ? 1 : result;
            }
        }
    }
}
