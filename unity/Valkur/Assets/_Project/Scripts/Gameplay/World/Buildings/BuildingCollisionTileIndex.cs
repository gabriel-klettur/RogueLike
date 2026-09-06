using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A building's collision tiles, addressable by cell instead of by name.
    ///
    /// <para>WHY. The tiles are child GameObjects called <c>CollTile_{row}_{col}</c>, and both
    /// the editor and the loader used to reach them by walking the children and reading
    /// <c>Transform.name</c>. That property marshals a fresh managed string out of the native
    /// object on every call, so a scan is not "a few string compares" — measured on a
    /// building with 451 children, one pass to find a pooled tile cost <b>205 µs</b>, against
    /// <b>3.1 µs</b> for the native <c>transform.Find</c> beside it. Painting calls that scan
    /// once per cell per building: a 48-cell grid fanned out to fourteen shared instances is
    /// 672 scans, and the numbers were 400 ms on mouse-down and 4 s on mouse-up.</para>
    ///
    /// <para>The child count only ever grows, too: a retired tile is renamed into a pool and
    /// never destroyed, so the scan gets slower for as long as the author keeps painting.</para>
    ///
    /// <para>This index keeps the same names — tests and the loader still find
    /// <c>CollTile_1_1</c> — and adds O(1) lookup on top: a dictionary for live cells and a
    /// stack for the pool. It builds itself from whatever children already exist the first
    /// time it is asked, so it works on buildings the loader populated before the editor
    /// opened.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingCollisionTileIndex : MonoBehaviour
    {
        public const string LivePrefix   = "CollTile_";
        public const string PooledPrefix = "_PooledCollTile_";

        private readonly Dictionary<int, Transform> _live = new Dictionary<int, Transform>(64);
        private readonly List<Transform>            _pool = new List<Transform>(64);
        private bool _built;

        /// <summary>Cell key. Rows and columns are grid indices, never negative, well under 32k.</summary>
        public static int CellKey(int row, int col) => (row << 15) | (col & 0x7FFF);

        public int LiveCount   => _live.Count;
        public int PooledCount => _pool.Count;

        /// <summary>Returns the index for <paramref name="building"/>, adding it if absent.</summary>
        public static BuildingCollisionTileIndex Of(Transform building)
        {
            if (building == null) return null;
            var index = building.GetComponent<BuildingCollisionTileIndex>();
            if (index == null) index = building.gameObject.AddComponent<BuildingCollisionTileIndex>();
            index.EnsureBuilt();
            return index;
        }

        /// <summary>
        /// One name-reading pass over the children, done once. Everything after this is
        /// dictionary and stack work.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            Rebuild();
        }

        /// <summary>Re-reads the children. Call after something outside this index renames or adds tiles.</summary>
        public void Rebuild()
        {
            _built = true;
            _live.Clear();
            _pool.Clear();

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                string name = child.name;
                if (name.StartsWith(PooledPrefix, System.StringComparison.Ordinal))
                {
                    _pool.Add(child);
                    continue;
                }
                if (!name.StartsWith(LivePrefix, System.StringComparison.Ordinal)) continue;
                if (!TryParseCell(name, out int row, out int col)) continue;
                _live[CellKey(row, col)] = child;
            }
        }

        /// <summary>The live tile for a cell, or null.</summary>
        public Transform Find(int row, int col)
        {
            EnsureBuilt();
            if (_live.TryGetValue(CellKey(row, col), out var t) && t != null) return t;
            return null;
        }

        /// <summary>
        /// A retired tile to reuse for <paramref name="row"/>/<paramref name="col"/>, renamed
        /// and re-registered, or null when the pool is empty.
        /// </summary>
        public Transform TakePooled(int row, int col)
        {
            EnsureBuilt();
            while (_pool.Count > 0)
            {
                var t = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                if (t == null) continue;                 // destroyed with the building's children
                t.name = LivePrefix + row + "_" + col;
                _live[CellKey(row, col)] = t;
                return t;
            }
            return null;
        }

        /// <summary>Records a tile this index did not hand out (a freshly created one).</summary>
        public void Register(int row, int col, Transform tile)
        {
            if (tile == null) return;
            EnsureBuilt();
            _live[CellKey(row, col)] = tile;
        }

        /// <summary>Moves one cell's tile into the pool. No-op when the cell has none.</summary>
        public Transform Retire(int row, int col)
        {
            EnsureBuilt();
            int key = CellKey(row, col);
            if (!_live.TryGetValue(key, out var t)) return null;
            _live.Remove(key);
            if (t == null) return null;
            t.name = PooledPrefix + t.GetInstanceID();
            _pool.Add(t);
            return t;
        }

        /// <summary>Moves every live tile into the pool — the whole-grid teardown.</summary>
        public void RetireAll()
        {
            EnsureBuilt();
            foreach (var kv in _live)
            {
                var t = kv.Value;
                if (t == null) continue;
                t.name = PooledPrefix + t.GetInstanceID();
                _pool.Add(t);
            }
            _live.Clear();
        }

        /// <summary>Parses <c>CollTile_{row}_{col}</c>. False for any other shape.</summary>
        public static bool TryParseCell(string name, out int row, out int col)
        {
            row = col = 0;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(LivePrefix, System.StringComparison.Ordinal))
                return false;

            int start = LivePrefix.Length;
            int sep   = name.IndexOf('_', start);
            if (sep < 0) return false;

            return int.TryParse(name.Substring(start, sep - start), out row)
                && int.TryParse(name.Substring(sep + 1), out col);
        }
    }
}
