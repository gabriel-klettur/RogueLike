using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Dirt roads joining the towns. Pure and deterministic.
    ///
    /// <para><b>Which towns:</b> a minimum spanning tree grown from the starting town, so every town
    /// can be walked to and no two roads run side by side to the same place.</para>
    ///
    /// <para><b>Where:</b> a cheapest path on a COARSE grid of <see cref="CellTiles"/>-tile cells,
    /// straightened by line of sight, then drawn back onto tiles. The coarse grid is what keeps it
    /// affordable: one climate sample per cell instead of per tile keeps a 400-tile world's roads to
    /// milliseconds, and the preview re-plans on every change of a setting. Every drawn tile is
    /// checked again at full resolution, so a cell that let a river corner through does not put a
    /// road in the water — the road simply breaks there, as it does where there is no bridge.</para>
    ///
    /// <para><b>Out of an arm, never through a town.</b> A road starts at the end of the main
    /// street that faces the other town and leaves in that street's own rows; other towns are
    /// crossed only along their streets. Road tiles near a town join its <see cref="WorldTown.StreetTiles"/>,
    /// which is what keeps the lots off them — lots reach <see cref="LotReach"/> tiles past the
    /// radius.</para>
    /// </summary>
    public static class WorldRoads
    {
        /// <summary>Side of a planning cell, in tiles.</summary>
        public const int CellTiles = 4;

        /// <summary>Road width in tiles.</summary>
        public const int Width = 2;

        /// <summary>Coarse cells of slack around the two towns a search may wander into.</summary>
        public const int SearchMarginCells = 16;

        /// <summary>A search that expands this many cells without arriving gives the road up.</summary>
        public const int MaxSearchCells = 60000;

        /// <summary>How far past a town's radius its lots can stand (WorldTownLots tests radius + 4).</summary>
        public const int LotReach = 5;

        /// <summary>Clear coarse ground kept around a town that is neither end of the road.</summary>
        private const int TownCellClearance = 3;

        private const int StraightCost = 10;
        private const int DiagonalCost = 14;

        /// <summary>
        /// The roads of a world and the tiles they paint. Mutates the towns: road tiles within
        /// <see cref="LotReach"/> of a town are added to its streets, so plan roads BEFORE lots.
        /// </summary>
        public static List<WorldRoad> Plan(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                           IReadOnlyList<WorldTown> towns, out HashSet<Vector2Int> roadTiles)
        {
            var roads = new List<WorldRoad>();
            roadTiles = new HashSet<Vector2Int>();
            if (climate == null || towns == null || towns.Count < 2) return roads;
            if (!climate.Settings.roadsBetweenTowns) return roads;

            var grid = new CoarseGrid(climate, riverTiles, towns);
            foreach (var (a, b) in SpanningTree(towns))
            {
                var path = Route(grid, towns[a], towns[b]);
                if (path == null) continue;
                roads.Add(new WorldRoad(a, b, path));
                Rasterise(climate, riverTiles, towns, path, roadTiles);
            }

            JoinStreets(towns, roadTiles);
            return roads;
        }

        // ── Which towns ────────────────────────────────────────────────────────

        /// <summary>Prim's tree from town 0, ties broken by index so the plan never depends on hash order.</summary>
        internal static List<(int, int)> SpanningTree(IReadOnlyList<WorldTown> towns)
        {
            var edges = new List<(int, int)>();
            int n = towns.Count;
            var inTree = new bool[n];
            inTree[0] = true;
            for (int added = 1; added < n; added++)
            {
                long best = long.MaxValue;
                int bestA = -1, bestB = -1;
                for (int a = 0; a < n; a++)
                {
                    if (!inTree[a]) continue;
                    for (int b = 0; b < n; b++)
                    {
                        if (inTree[b]) continue;
                        long d = (towns[a].Center - towns[b].Center).sqrMagnitude;
                        if (d < best) { best = d; bestA = a; bestB = b; }
                    }
                }
                if (bestB < 0) break;
                inTree[bestB] = true;
                edges.Add((bestA, bestB));
            }
            return edges;
        }

        // ── Where ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The tile a road leaves a town from: one past the end of the main arm that best faces
        /// <paramref name="toward"/>. The arms are the town's longest straight streets, and a road
        /// that leaves in their rows meets them without cutting a lot.
        /// </summary>
        internal static Vector2Int ArmExit(WorldTown town, Vector2Int toward, out Vector2Int armEnd)
        {
            var d = toward - town.Center;
            var c = town.Center;
            int r = town.Radius;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            {
                if (d.x >= 0) { armEnd = new Vector2Int(c.x + r - 1, c.y); return new Vector2Int(c.x + r, c.y); }
                armEnd = new Vector2Int(c.x - r, c.y);
                return new Vector2Int(c.x - r - 1, c.y);
            }
            if (d.y >= 0) { armEnd = new Vector2Int(c.x, c.y + r - 1); return new Vector2Int(c.x, c.y + r); }
            armEnd = new Vector2Int(c.x, c.y - r);
            return new Vector2Int(c.x, c.y - r - 1);
        }

        private static List<Vector2Int> Route(CoarseGrid grid, WorldTown from, WorldTown to)
        {
            var startTile = ArmExit(from, to.Center, out var startArm);
            var goalTile = ArmExit(to, from.Center, out var goalArm);
            // Step LotReach tiles clear of each town before the coarse search takes over, so the
            // first and last cells are outside both towns' lots.
            var startOut = startTile + Outward(from, startTile) * LotReach;
            var goalOut = goalTile + Outward(to, goalTile) * LotReach;

            var start = grid.CellOf(startOut);
            var goal = grid.CellOf(goalOut);
            if (!grid.InBounds(start) || !grid.InBounds(goal)) return null;

            var cells = grid.Search(start, goal, from.Index, to.Index);
            if (cells == null) return null;
            cells = grid.Straighten(cells, from.Index, to.Index);

            var path = new List<Vector2Int>();
            AppendLine(path, startArm, startOut);
            for (int i = 0; i < cells.Count; i++)
                AppendLine(path, path[path.Count - 1], grid.CentreOf(cells[i]));
            AppendLine(path, path[path.Count - 1], goalOut);
            AppendLine(path, path[path.Count - 1], goalArm);
            return path;
        }

        /// <summary>The unit step pointing away from the town's centre along the arm the exit sits on.</summary>
        private static Vector2Int Outward(WorldTown town, Vector2Int exit)
        {
            var d = exit - town.Center;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return new Vector2Int(d.x >= 0 ? 1 : -1, 0);
            return new Vector2Int(0, d.y >= 0 ? 1 : -1);
        }

        /// <summary>Bresenham from <paramref name="a"/> (exclusive when already the last point) to <paramref name="b"/>.</summary>
        private static void AppendLine(List<Vector2Int> path, Vector2Int a, Vector2Int b)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                var p = new Vector2Int(x0, y0);
                if (path.Count == 0 || path[path.Count - 1] != p) path.Add(p);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // ── Tiles ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A centre-line tile paints a <see cref="Width"/>-square, so a diagonal stretch is a
        /// continuous band. Each tile is checked at full resolution: water and highland are never
        /// paved, and inside a town only its streets are.
        /// </summary>
        private static void Rasterise(WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                      IReadOnlyList<WorldTown> towns, List<Vector2Int> path,
                                      HashSet<Vector2Int> roadTiles)
        {
            foreach (var p in path)
                for (int dy = 0; dy < Width; dy++)
                    for (int dx = 0; dx < Width; dx++)
                    {
                        var t = new Vector2Int(p.x + dx, p.y + dy);
                        if (roadTiles.Contains(t)) continue;
                        if (!WorldTowns.IsBuildable(climate, riverTiles, t)) continue;
                        if (InsideTownOffStreet(towns, t)) continue;
                        roadTiles.Add(t);
                    }
        }

        private static bool InsideTownOffStreet(IReadOnlyList<WorldTown> towns, Vector2Int t)
        {
            for (int i = 0; i < towns.Count; i++)
                if (towns[i].Contains(t, 1) && !towns[i].StreetTiles.Contains(t) && !OnArmRows(towns[i], t))
                    return true;
            return false;
        }

        /// <summary>
        /// The rows (or columns) of a main arm continued past its end: where a road leaves the town.
        /// A tile there within radius + 1 is not a street yet, and refusing it would leave a gap
        /// between the arm and its road.
        /// </summary>
        private static bool OnArmRows(WorldTown town, Vector2Int t)
        {
            var c = town.Center;
            int mw = WorldTowns.MainStreetWidth / 2;
            int reach = town.Radius + 1;
            bool horizontal = t.y >= c.y - mw && t.y <= c.y + mw && Mathf.Abs(t.x - c.x) >= town.Radius - 1 && Mathf.Abs(t.x - c.x) <= reach;
            bool vertical = t.x >= c.x - mw && t.x <= c.x + mw && Mathf.Abs(t.y - c.y) >= town.Radius - 1 && Mathf.Abs(t.y - c.y) <= reach;
            return horizontal || vertical;
        }

        /// <summary>Road tiles a town's lots could reach become that town's streets.</summary>
        private static void JoinStreets(IReadOnlyList<WorldTown> towns, HashSet<Vector2Int> roadTiles)
        {
            foreach (var town in towns)
                foreach (var t in roadTiles)
                    if (town.Contains(t, LotReach)) town.StreetTiles.Add(t);
        }

        // ── The coarse search ─────────────────────────────────────────────────

        private sealed class CoarseGrid
        {
            private readonly WorldClimate _climate;
            private readonly HashSet<Vector2Int> _rivers;
            private readonly IReadOnlyList<WorldTown> _towns;
            private readonly int _cols;
            private readonly int _rows;
            private readonly sbyte[] _open; // -1 unknown, 0 blocked, 1 open

            public CoarseGrid(WorldClimate climate, HashSet<Vector2Int> rivers, IReadOnlyList<WorldTown> towns)
            {
                _climate = climate;
                _rivers = rivers;
                _towns = towns;
                var s = climate.Settings;
                _cols = Mathf.Max(1, s.widthTiles / CellTiles);
                _rows = Mathf.Max(1, s.heightTiles / CellTiles);
                _open = new sbyte[_cols * _rows];
                for (int i = 0; i < _open.Length; i++) _open[i] = -1;
            }

            public Vector2Int CellOf(Vector2Int tile) => new Vector2Int(Floor(tile.x), Floor(tile.y));
            public Vector2Int CentreOf(Vector2Int cell) => new Vector2Int(cell.x * CellTiles + CellTiles / 2, cell.y * CellTiles + CellTiles / 2);
            public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < _cols && cell.y < _rows;

            private static int Floor(int v) => v >= 0 ? v / CellTiles : (v - CellTiles + 1) / CellTiles;

            /// <summary>
            /// Walkable at the cell's centre, no river tile anywhere in it, and clear of every town
            /// that is not one of the road's two ends.
            /// </summary>
            private bool Open(Vector2Int cell, int fromTown, int toTown)
            {
                if (!InBounds(cell)) return false;
                if (TooCloseToATown(cell, fromTown, toTown)) return false;

                int i = cell.y * _cols + cell.x;
                if (_open[i] < 0)
                {
                    bool open = WorldTowns.IsBuildable(_climate, _rivers, CentreOf(cell));
                    if (open && _rivers != null)
                        for (int y = 0; y < CellTiles && open; y++)
                            for (int x = 0; x < CellTiles; x++)
                                if (_rivers.Contains(new Vector2Int(cell.x * CellTiles + x, cell.y * CellTiles + y))) { open = false; break; }
                    _open[i] = (sbyte)(open ? 1 : 0);
                }
                return _open[i] == 1;
            }

            /// <summary>
            /// Other towns are kept at a distance; the road's own two towns only out of their
            /// interiors, since it leaves and enters them along an arm drawn outside the search.
            /// </summary>
            private bool TooCloseToATown(Vector2Int cell, int fromTown, int toTown)
            {
                var centre = CentreOf(cell);
                for (int t = 0; t < _towns.Count; t++)
                {
                    bool end = t == fromTown || t == toTown;
                    int reach = _towns[t].Radius + (end ? 2 : TownCellClearance * CellTiles);
                    if ((centre - _towns[t].Center).sqrMagnitude <= reach * reach) return true;
                }
                return false;
            }

            /// <summary>A* over cells, 8-connected, octile heuristic, bounded by a box around both ends.</summary>
            public List<Vector2Int> Search(Vector2Int start, Vector2Int goal, int fromTown, int toTown)
            {
                if (!Open(start, fromTown, toTown) || !Open(goal, fromTown, toTown)) return null;

                int minX = Mathf.Max(0, Mathf.Min(start.x, goal.x) - SearchMarginCells);
                int minY = Mathf.Max(0, Mathf.Min(start.y, goal.y) - SearchMarginCells);
                int maxX = Mathf.Min(_cols - 1, Mathf.Max(start.x, goal.x) + SearchMarginCells);
                int maxY = Mathf.Min(_rows - 1, Mathf.Max(start.y, goal.y) + SearchMarginCells);
                int w = maxX - minX + 1, h = maxY - minY + 1;

                var g = new int[w * h];
                var parent = new int[w * h];
                var closed = new bool[w * h];
                for (int i = 0; i < g.Length; i++) { g[i] = int.MaxValue; parent[i] = -1; }

                int Local(Vector2Int c) => (c.y - minY) * w + (c.x - minX);
                var heap = new MinHeap();
                int s = Local(start), goalIndex = Local(goal);
                g[s] = 0;
                heap.Push(Heuristic(start, goal), 0, s);

                int expanded = 0;
                while (heap.Count > 0)
                {
                    heap.Pop(out _, out _, out int current);
                    if (closed[current]) continue;
                    closed[current] = true;
                    if (current == goalIndex) return Unwind(parent, current, w, minX, minY);
                    if (++expanded > MaxSearchCells) return null;

                    int cx = current % w + minX, cy = current / w + minY;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < minX || ny < minY || nx > maxX || ny > maxY) continue;
                            var next = new Vector2Int(nx, ny);
                            int ni = Local(next);
                            if (closed[ni] || !Open(next, fromTown, toTown)) continue;
                            // A diagonal step needs both orthogonal neighbours open, or the drawn
                            // line squeezes through the corner of a blocked cell.
                            if (dx != 0 && dy != 0 &&
                                (!Open(new Vector2Int(cx + dx, cy), fromTown, toTown) || !Open(new Vector2Int(cx, cy + dy), fromTown, toTown)))
                                continue;

                            int cost = g[current] + (dx != 0 && dy != 0 ? DiagonalCost : StraightCost);
                            if (cost >= g[ni]) continue;
                            g[ni] = cost;
                            parent[ni] = current;
                            int hcost = Heuristic(next, goal);
                            heap.Push(cost + hcost, hcost, ni);
                        }
                }
                return null;
            }

            private static List<Vector2Int> Unwind(int[] parent, int index, int w, int minX, int minY)
            {
                var cells = new List<Vector2Int>();
                for (int i = index; i >= 0; i = parent[i]) cells.Add(new Vector2Int(i % w + minX, i / w + minY));
                cells.Reverse();
                return cells;
            }

            private static int Heuristic(Vector2Int a, Vector2Int b)
            {
                int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
                return StraightCost * (dx + dy) + (DiagonalCost - 2 * StraightCost) * Mathf.Min(dx, dy);
            }

            /// <summary>
            /// Keeps only the cells where the road has to turn: from each kept cell, the farthest
            /// later cell reachable in a straight line over open cells. A* on a grid zigzags between
            /// equally cheap routes; a road that does reads as noise.
            /// </summary>
            public List<Vector2Int> Straighten(List<Vector2Int> cells, int fromTown, int toTown)
            {
                if (cells.Count <= 2) return cells;
                var kept = new List<Vector2Int> { cells[0] };
                int anchor = 0;
                while (anchor < cells.Count - 1)
                {
                    int next = anchor + 1;
                    for (int j = cells.Count - 1; j > anchor + 1; j--)
                        if (LineOpen(cells[anchor], cells[j], fromTown, toTown)) { next = j; break; }
                    kept.Add(cells[next]);
                    anchor = next;
                }
                return kept;
            }

            private bool LineOpen(Vector2Int a, Vector2Int b, int fromTown, int toTown)
            {
                var line = new List<Vector2Int>();
                AppendLine(line, a, b);
                for (int i = 0; i < line.Count; i++)
                    if (!Open(line[i], fromTown, toTown)) return false;
                return true;
            }
        }

        /// <summary>Binary heap of (f, h, index), smallest f first, then smallest h, then smallest index.</summary>
        private sealed class MinHeap
        {
            private readonly List<(int f, int h, int i)> _items = new List<(int, int, int)>();

            public int Count => _items.Count;

            public void Push(int f, int h, int i)
            {
                _items.Add((f, h, i));
                int k = _items.Count - 1;
                while (k > 0)
                {
                    int p = (k - 1) / 2;
                    if (!Less(_items[k], _items[p])) break;
                    (_items[k], _items[p]) = (_items[p], _items[k]);
                    k = p;
                }
            }

            public void Pop(out int f, out int h, out int i)
            {
                var top = _items[0];
                f = top.f; h = top.h; i = top.i;
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int k = 0;
                while (true)
                {
                    int l = 2 * k + 1, r = l + 1, m = k;
                    if (l < _items.Count && Less(_items[l], _items[m])) m = l;
                    if (r < _items.Count && Less(_items[r], _items[m])) m = r;
                    if (m == k) break;
                    (_items[k], _items[m]) = (_items[m], _items[k]);
                    k = m;
                }
            }

            private static bool Less((int f, int h, int i) a, (int f, int h, int i) b)
                => a.f != b.f ? a.f < b.f : a.h != b.h ? a.h < b.h : a.i < b.i;
        }
    }
}
