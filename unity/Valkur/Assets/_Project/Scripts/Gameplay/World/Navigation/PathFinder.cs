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

        [Tooltip("Maximum path length in tiles. A longer solution is truncated and the " +
                 "follower repaths from where it gets to.")]
        [SerializeField] private int maxPathLength = 100;

        [Tooltip("How many A* searches may run in one frame across ALL callers. A pack of " +
                 "chasers repaths on independent timers that drift into alignment, so " +
                 "without a ceiling twenty monsters can each spend maxNodes expansions in " +
                 "the same frame. A refused search is not a lost path: the follower keeps " +
                 "the one it has and asks again next frame.")]
        [SerializeField] private int maxSearchesPerFrame = 4;

        // Layers that block NPC movement. World(11) + Building(14) alone see only
        // the building boxes — the painted collision cells live on WorldL0..WorldAll,
        // so a path solved without them routes straight through walls and water.
        private static int BlockingMask => Layering.WorldCollisionLayers.BlockingMask();

        protected override bool Persist => false;

        // Optional add-on consulted on every A* expansion. Null = legacy behavior.
        private IPathFinderPenaltyProvider _penaltyProvider;

        /// <summary>
        /// Inject (or clear with null) the optional <see cref="IPathFinderPenaltyProvider"/>.
        /// When set, A* adds <see cref="IPathFinderPenaltyProvider.GetExtraPenalty"/> to the
        /// unit step cost on every neighbor expansion. Used by the Udemy dungeon
        /// system to bias paths toward "preferred path" tiles.
        /// </summary>
        public void SetPenaltyProvider(IPathFinderPenaltyProvider provider)
        {
            _penaltyProvider = provider;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Find a path in world space from <paramref name="start"/> to <paramref name="goal"/>.
        /// Returns world-space waypoint list (goal is last element). Empty = no path.
        /// </summary>
        public List<Vector2> FindPath(Vector2 start, Vector2 goal)
        {
            var result = new List<Vector2>();
            TryFindPath(start, goal, result);
            return result;
        }

        /// <summary>
        /// Frame budget. Reset lazily on the first request of each frame rather than from an
        /// Update, so the ceiling holds even when this component is disabled or the caller
        /// runs from FixedUpdate.
        /// </summary>
        private int _budgetFrame = -1;
        private int _searchesThisFrame;

        /// <summary>True when another search may run this frame.</summary>
        public bool HasSearchBudget
        {
            get
            {
                if (maxSearchesPerFrame <= 0) return true;
                if (Time.frameCount != _budgetFrame) return true;
                return _searchesThisFrame < maxSearchesPerFrame;
            }
        }

        /// <summary>
        /// Non-allocating, budgeted <see cref="FindPath"/>.
        ///
        /// <para>Returns FALSE without touching <paramref name="into"/> when this frame's
        /// search budget is already spent — which is the whole point: a follower that gets
        /// false keeps the waypoints it already had and tries again next frame, instead of
        /// the fight paying for twenty full searches in one frame the first time every
        /// monster's repath timer lines up.</para>
        ///
        /// <para>Returns TRUE with an EMPTY list when the search ran and found nothing. The
        /// two outcomes must be distinguishable: "no path exists" means fall back to direct
        /// movement, "not this frame" means keep going as you were.</para>
        /// </summary>
        public bool TryFindPath(Vector2 start, Vector2 goal, List<Vector2> into)
        {
            if (into == null) return false;

            if (maxSearchesPerFrame > 0)
            {
                if (Time.frameCount != _budgetFrame)
                {
                    _budgetFrame = Time.frameCount;
                    _searchesThisFrame = 0;
                }
                else if (_searchesThisFrame >= maxSearchesPerFrame)
                {
                    return false;
                }
                _searchesThisFrame++;
            }

            into.Clear();
            FillPath(start, goal, into);
            return true;
        }

        private void FillPath(Vector2 start, Vector2 goal, List<Vector2> into)
        {
            var startCell = WorldToCell(start);
            var goalCell  = WorldToCell(goal);

            if (startCell == goalCell)
            {
                into.Add(goal);
                return;
            }

            if (!IsWalkable(goalCell))
                goalCell = FindNearestWalkable(goalCell, 3);

            var rawPath = AStar(startCell, goalCell);
            if (rawPath == null || rawPath.Count == 0)
                return;

            // Drop the start cell. Reconstruct returns the path INCLUDING the cell the
            // caller is already standing in, and its world centre is almost never where
            // the caller actually is — so the follower's first move was toward its own
            // tile centre, i.e. backwards, once per repath (every 0.5 s while chasing).
            int first = (rawPath[0] == startCell && rawPath.Count > 1) ? 1 : 0;

            // maxPathLength had been a serialized field with a #pragma silencing the
            // "never used" warning around it — an authored ceiling that bounded nothing.
            // Truncating is safe because the follower repaths on its own timer: it walks
            // the part of the solution it was given and asks again from there.
            int last = rawPath.Count;
            if (maxPathLength > 0 && last - first > maxPathLength)
                last = first + maxPathLength;
            bool truncated = last < rawPath.Count;

            for (int i = first; i < last; i++)
                into.Add(CellToWorld(rawPath[i]));

            // The real goal replaces the last TILE CENTRE, but only when the path actually
            // reaches it — on a truncated path the final waypoint is a place along the way,
            // and overwriting it with the goal would teleport the follower's aim past every
            // wall the truncation left unsolved.
            if (into.Count > 0 && !truncated)
                into[into.Count - 1] = goal;

            SmoothPath(start, into);
        }

        /// <summary>
        /// String-pulling: drop every waypoint the follower can simply walk past.
        ///
        /// A* returns one waypoint per TILE, and the follower steers straight at each one
        /// in turn — so an open diagonal run came out as a visible zig-zag between tile
        /// centres, and a straight corridor cost one course correction per tile. Keeping a
        /// waypoint is only worth it when the geometry actually requires the turn.
        ///
        /// The test is the same <see cref="LineOfSight"/> the aggro and melee checks use,
        /// so "can I walk straight there" means exactly what it means everywhere else in
        /// the game. It is affordable now precisely because that helper exists: before it,
        /// this pass would have been a second, differently-masked raycast implementation.
        ///
        /// Cost is O(n) casts on a path of n tiles, against the up-to-8,000 walkability
        /// probes the search itself may do — and it usually REDUCES total work downstream,
        /// because a chasing monster stops recomputing a heading every tile.
        /// </summary>
        private static void SmoothPath(Vector2 start, List<Vector2> waypoints)
        {
            if (waypoints.Count < 2) return;

            Vector2 from = start;
            int i = 0;
            while (i < waypoints.Count - 1)
            {
                // If the waypoint AFTER this one is directly reachable, this one is a
                // corner the path did not actually need to turn at.
                if (LineOfSight.IsClear(from, waypoints[i + 1]))
                {
                    waypoints.RemoveAt(i);
                    continue;   // re-test the same index against the new successor
                }

                from = waypoints[i];
                i++;
            }
        }

        /// <summary>
        /// Drop the memoised walkability grid. MUST be called whenever the painted
        /// collision changes — <see cref="Layering.WorldCollisionBaker"/> calls it after
        /// every rebake — or the solver keeps routing around walls that were erased and
        /// straight through walls that were painted.
        /// </summary>
        public static void InvalidateWalkability()
        {
            if (HasInstance) Instance._walkCache.Clear();
        }

        // ── A* implementation ────────────────────────────────────────────────

        // Reused across every search. A chasing pack repaths 40 times a second between
        // them; allocating a SortedList, two Dictionaries and two Lists per call fed the
        // garbage collector in exactly the scenario a fight creates.
        private readonly MinHeap _open = new MinHeap(256);
        private readonly Dictionary<Vector2Int, Vector2Int> _cameFrom = new Dictionary<Vector2Int, Vector2Int>(512);
        private readonly Dictionary<Vector2Int, float> _gScore = new Dictionary<Vector2Int, float>(512);
        private readonly List<Vector2Int> _path = new List<Vector2Int>(128);

        private List<Vector2Int> AStar(Vector2Int start, Vector2Int goal)
        {
            _open.Clear();
            _cameFrom.Clear();
            _gScore.Clear();

            _gScore[start] = 0f;
            _open.Push(Heuristic(start, goal), start);

            int expanded = 0;

            while (_open.Count > 0)
            {
                var current = _open.Pop();

                // A cell can be pushed several times with improving scores; the stale
                // copies surface later and are skipped here. The old SortedList could
                // not do this — its comparer never returned 0 specifically so duplicate
                // keys would be kept, so every stale entry stayed in the set forever.
                if (_gScore.TryGetValue(current, out float gCur) == false) continue;

                if (current == goal)
                    return Reconstruct(current);

                if (++expanded > maxNodes)
                    break;

                for (int i = 0; i < _neighbours.Length; i++)
                {
                    var dir = _neighbours[i];
                    var neighbor = current + dir;

                    if (!IsWalkable(neighbor)) continue;

                    bool diagonal = dir.x != 0 && dir.y != 0;
                    if (diagonal)
                    {
                        // Never cut a wall corner: a diagonal step is legal only when both
                        // orthogonal cells it passes between are open. Without this the
                        // solver happily slides through the gap where two walls meet.
                        if (!IsWalkable(new Vector2Int(current.x + dir.x, current.y)) ||
                            !IsWalkable(new Vector2Int(current.x, current.y + dir.y)))
                            continue;
                    }

                    // Base step cost; an optional penalty provider can add to it.
                    // Provider is null in vanilla Valkur; the Udemy dungeon system installs one.
                    float stepCost = diagonal ? Sqrt2 : 1f;
                    if (_penaltyProvider != null)
                    {
                        int extra = _penaltyProvider.GetExtraPenalty(neighbor);
                        if (extra > 0) stepCost += extra;
                    }

                    float tentG = gCur + stepCost;
                    if (!_gScore.TryGetValue(neighbor, out float oldG) || tentG < oldG)
                    {
                        _cameFrom[neighbor] = current;
                        _gScore[neighbor] = tentG;
                        _open.Push(tentG + Heuristic(neighbor, goal), neighbor);
                    }
                }
            }

            return null; // no path
        }

        private List<Vector2Int> Reconstruct(Vector2Int current)
        {
            _path.Clear();
            _path.Add(current);
            while (_cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                _path.Add(current);
            }
            _path.Reverse();
            return _path;
        }

        /// <summary>
        /// Array-backed binary min-heap. Replaces a <c>SortedList</c> whose <c>Add</c> is
        /// an O(n) array shift — on a 2000-node search with a wide frontier that is the
        /// dominant cost, and it grew worse the longer the path got.
        /// </summary>
        private sealed class MinHeap
        {
            private float[] _keys;
            private Vector2Int[] _values;
            private int _count;

            public MinHeap(int capacity)
            {
                _keys = new float[capacity];
                _values = new Vector2Int[capacity];
            }

            public int Count => _count;
            public void Clear() => _count = 0;

            public void Push(float key, Vector2Int value)
            {
                if (_count == _keys.Length) Grow();

                int i = _count++;
                _keys[i] = key;
                _values[i] = value;

                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(parent, i);
                    i = parent;
                }
            }

            public Vector2Int Pop()
            {
                var top = _values[0];
                _count--;
                if (_count > 0)
                {
                    _keys[0] = _keys[_count];
                    _values[0] = _values[_count];

                    int i = 0;
                    while (true)
                    {
                        int l = (i << 1) + 1, r = l + 1, smallest = i;
                        if (l < _count && _keys[l] < _keys[smallest]) smallest = l;
                        if (r < _count && _keys[r] < _keys[smallest]) smallest = r;
                        if (smallest == i) break;
                        Swap(i, smallest);
                        i = smallest;
                    }
                }
                return top;
            }

            private void Grow()
            {
                System.Array.Resize(ref _keys, _keys.Length * 2);
                System.Array.Resize(ref _values, _values.Length * 2);
            }

            private void Swap(int a, int b)
            {
                float k = _keys[a]; _keys[a] = _keys[b]; _keys[b] = k;
                Vector2Int v = _values[a]; _values[a] = _values[b]; _values[b] = v;
            }
        }

        // ── Grid utilities ───────────────────────────────────────────────────

        private static readonly float Sqrt2 = Mathf.Sqrt(2f);

        /// <summary>
        /// Eight neighbours, cardinals first so equal-cost ties resolve to a straight
        /// step. The solver used to be cardinal-only with a Manhattan heuristic, which
        /// turned every diagonal approach into a right-angled staircase of one-tile
        /// waypoints — and because the follower steers straight at each waypoint, the
        /// monster visibly zig-zagged across open ground while paying a node per tile.
        /// Instance-level, not static: this type is a singleton, so one array either way,
        /// and a static collection would need a Domain-Reload reset hook to earn its keep.
        /// </summary>
        private readonly Vector2Int[] _neighbours =
        {
            new Vector2Int( 1,  0),
            new Vector2Int(-1,  0),
            new Vector2Int( 0,  1),
            new Vector2Int( 0, -1),
            new Vector2Int( 1,  1),
            new Vector2Int( 1, -1),
            new Vector2Int(-1,  1),
            new Vector2Int(-1, -1),
        };

        /// <summary>
        /// Octile distance — the exact cost of the cheapest unobstructed 8-way path, so
        /// it stays admissible (never overestimates) while being far better informed than
        /// Manhattan was once diagonals exist. Manhattan over an 8-way grid OVERestimates
        /// diagonal runs, which would have made the search return non-optimal paths.
        /// </summary>
        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            float dx = Mathf.Abs(a.x - b.x);
            float dy = Mathf.Abs(a.y - b.y);
            return (dx + dy) + (Sqrt2 - 2f) * Mathf.Min(dx, dy);
        }

        private Vector2Int WorldToCell(Vector2 world)
            => new Vector2Int(Mathf.FloorToInt(world.x / tileSize),
                              Mathf.FloorToInt(world.y / tileSize));

        private Vector2 CellToWorld(Vector2Int cell)
            => new Vector2(cell.x * tileSize + tileSize * 0.5f,
                           cell.y * tileSize + tileSize * 0.5f);

        /// <summary>
        /// Memoised walkability. Each cell used to cost a live
        /// <c>Physics2D.OverlapCircle</c> EVERY time it was probed — four probes per
        /// expansion, up to <c>maxNodes</c> expansions, so as many as 8,000 physics
        /// queries per search, and the same terrain was re-probed from scratch by every
        /// monster on every repath. The cache is dropped wholesale by
        /// <see cref="InvalidateWalkability"/> when the painted collision changes.
        /// </summary>
        private readonly Dictionary<Vector2Int, bool> _walkCache = new Dictionary<Vector2Int, bool>(4096);

        private bool IsWalkable(Vector2Int cell)
        {
            if (_walkCache.TryGetValue(cell, out bool cached)) return cached;

            Vector2 center = CellToWorld(cell);
            bool walkable = Physics2D.OverlapCircle(center, walkableRadius, BlockingMask) == null;
            _walkCache[cell] = walkable;
            return walkable;
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

    }
}
