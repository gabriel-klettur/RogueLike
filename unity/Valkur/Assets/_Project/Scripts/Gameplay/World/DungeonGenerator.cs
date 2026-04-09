using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Procedural dungeon generator: rooms connected by tunnels.
    /// Direct port of Python's roguelike_engine.map.model.generator.dungeon.DungeonGenerator.
    /// </summary>
    public static class DungeonGenerator
    {
        /// <summary>
        /// Result of dungeon generation containing the tile grid and room metadata.
        /// </summary>
        public struct Result
        {
            /// <summary>2D grid of tile characters (width × height). Access as grid[y][x].</summary>
            public char[][] Grid;
            /// <summary>List of generated rooms as (x1, y1, x2, y2) rectangles.</summary>
            public List<RectInt> Rooms;
            /// <summary>Width of the generated grid.</summary>
            public int Width;
            /// <summary>Height of the generated grid.</summary>
            public int Height;
        }

        /// <summary>
        /// Generate a dungeon with rooms connected by tunnels.
        /// </summary>
        /// <param name="config">Configuration ScriptableObject with generation parameters.</param>
        /// <param name="seed">Optional RNG seed. -1 for random.</param>
        /// <param name="avoidZone">Optional rectangle to avoid placing rooms in. Use default (all zeros) to skip.</param>
        /// <returns>A Result containing the tile grid and room metadata.</returns>
        public static Result Generate(DungeonGeneratorConfig config, int seed = -1, RectInt avoidZone = default)
        {
            var rng = seed >= 0 ? new System.Random(seed) : new System.Random();

            int width = config.ZoneWidth;
            int height = config.ZoneHeight;
            int maxAttempts = config.MaxRoomAttempts;
            int roomMin = config.RoomMinSize;
            int roomMax = config.RoomMaxSize;
            int tunnelThick = config.TunnelThickness;
            char wall = config.WallChar;
            char room = config.RoomChar;
            char tunnel = config.TunnelChar;

            // Determine max allowed rooms (Python: 'MAX', int, or None)
            int maxAllowed = config.MaxRoomsAllowed > 0
                ? config.MaxRoomsAllowed
                : maxAttempts;

            // Initialize grid with walls
            char[][] grid = new char[height][];
            for (int y = 0; y < height; y++)
            {
                grid[y] = new char[width];
                for (int x = 0; x < width; x++)
                    grid[y][x] = wall;
            }

            var rooms = new List<RectInt>();
            int attempts = 0;

            while (attempts < maxAttempts && rooms.Count < maxAllowed)
            {
                attempts++;

                int w = rng.Next(roomMin, roomMax + 1);
                int h = rng.Next(roomMin, roomMax + 1);
                int rx = rng.Next(1, width - w - 1);
                int ry = rng.Next(1, height - h - 1);
                var newRoom = new RectInt(rx, ry, w, h);

                // Check collision with existing rooms
                bool collides = false;
                for (int i = 0; i < rooms.Count; i++)
                {
                    if (Intersect(rooms[i], newRoom))
                    {
                        collides = true;
                        break;
                    }
                }
                if (collides) continue;

                // Check avoid zone
                if (avoidZone.width > 0 && avoidZone.height > 0)
                {
                    if (Intersect(avoidZone, newRoom))
                        continue;
                }

                // Paint room floor
                for (int yy = ry; yy < ry + h; yy++)
                    for (int xx = rx; xx < rx + w; xx++)
                        grid[yy][xx] = room;

                // Connect to previous room via tunnel
                if (rooms.Count > 0)
                {
                    var prev = rooms[rooms.Count - 1];
                    var prevCenter = CenterOf(prev);
                    var newCenter = CenterOf(newRoom);

                    if (rng.NextDouble() < 0.5)
                    {
                        HorizTunnel(grid, prevCenter.x, newCenter.x, prevCenter.y, tunnelThick, tunnel);
                        VertTunnel(grid, prevCenter.y, newCenter.y, newCenter.x, tunnelThick, tunnel);
                    }
                    else
                    {
                        VertTunnel(grid, prevCenter.y, newCenter.y, prevCenter.x, tunnelThick, tunnel);
                        HorizTunnel(grid, prevCenter.x, newCenter.x, newCenter.y, tunnelThick, tunnel);
                    }
                }

                rooms.Add(newRoom);
            }

            return new Result
            {
                Grid = grid,
                Rooms = rooms,
                Width = width,
                Height = height
            };
        }

        /// <summary>
        /// Connect two sets of rooms across zones by finding the closest pair
        /// and carving a tunnel between their centers (with global offsets).
        /// Port of Python's MapGenerator.expand_zone() inter-zone connection.
        /// </summary>
        public static void ConnectZones(
            char[][] grid,
            List<RectInt> parentRooms, Vector2Int parentOffset,
            List<RectInt> newRooms, Vector2Int newOffset,
            int tunnelThickness = 3, char tunnelChar = '=', int seed = -1)
        {
            if (parentRooms == null || parentRooms.Count == 0 ||
                newRooms == null || newRooms.Count == 0)
                return;

            var rng = seed >= 0 ? new System.Random(seed) : new System.Random();
            int height = grid.Length;
            int width = height > 0 ? grid[0].Length : 0;

            // Find closest room pair by Manhattan distance
            Vector2Int bestParent = default;
            Vector2Int bestNew = default;
            int minDist = int.MaxValue;

            for (int p = 0; p < parentRooms.Count; p++)
            {
                var pc = CenterOf(parentRooms[p]);
                var pGlobal = new Vector2Int(pc.x + parentOffset.x, pc.y + parentOffset.y);

                for (int n = 0; n < newRooms.Count; n++)
                {
                    var nc = CenterOf(newRooms[n]);
                    var nGlobal = new Vector2Int(nc.x + newOffset.x, nc.y + newOffset.y);

                    int dist = Mathf.Abs(pGlobal.x - nGlobal.x) + Mathf.Abs(pGlobal.y - nGlobal.y);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestParent = pGlobal;
                        bestNew = nGlobal;
                    }
                }
            }

            // Carve connecting tunnel
            if (rng.NextDouble() < 0.5)
            {
                HorizTunnel(grid, bestParent.x, bestNew.x, bestParent.y, tunnelThickness, tunnelChar);
                VertTunnel(grid, bestParent.y, bestNew.y, bestNew.x, tunnelThickness, tunnelChar);
            }
            else
            {
                VertTunnel(grid, bestParent.y, bestNew.y, bestParent.x, tunnelThickness, tunnelChar);
                HorizTunnel(grid, bestParent.x, bestNew.x, bestNew.y, tunnelThickness, tunnelChar);
            }
        }

        /// <summary>Carve a horizontal tunnel at row y from x1 to x2.</summary>
        public static void HorizTunnel(char[][] grid, int x1, int x2, int y, int thickness, char ch)
        {
            int height = grid.Length;
            if (height == 0) return;
            int width = grid[0].Length;
            if (width == 0) return;

            int half = thickness / 2;
            int xStart = Mathf.Min(x1, x2);
            int xEnd = Mathf.Max(x1, x2);

            for (int t = 0; t < thickness; t++)
            {
                int yy = y + t - half;
                if (yy < 0 || yy >= height) continue;

                int xStartClip = Mathf.Max(0, xStart);
                int xEndClip = Mathf.Min(width - 1, xEnd);

                for (int xx = xStartClip; xx <= xEndClip; xx++)
                    grid[yy][xx] = ch;
            }
        }

        /// <summary>Carve a vertical tunnel at column x from y1 to y2.</summary>
        public static void VertTunnel(char[][] grid, int y1, int y2, int x, int thickness, char ch)
        {
            int height = grid.Length;
            if (height == 0) return;
            int width = grid[0].Length;
            if (width == 0) return;

            int half = thickness / 2;
            int yStart = Mathf.Max(0, Mathf.Min(y1, y2));
            int yEnd = Mathf.Min(height - 1, Mathf.Max(y1, y2));

            for (int yy = yStart; yy <= yEnd; yy++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    int xx = x + t - half;
                    if (xx >= 0 && xx < width)
                        grid[yy][xx] = ch;
                }
            }
        }

        /// <summary>Check if two rectangles intersect (AABB overlap).</summary>
        public static bool Intersect(RectInt a, RectInt b)
        {
            return a.xMin <= b.xMax && a.xMax >= b.xMin &&
                   a.yMin <= b.yMax && a.yMax >= b.yMin;
        }

        /// <summary>Return the integer center of a RectInt.</summary>
        public static Vector2Int CenterOf(RectInt r)
        {
            return new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);
        }
    }
}
