using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Bakes the world's own art into a small terrain atlas the map draws from: a miniature
    /// of every tile layer and every placed building, rendered by an offscreen camera.
    ///
    /// <para><b>Why a camera and not per-tile colours.</b> Tiles alone would miss the one thing
    /// a town map is made of — its buildings, which are sprites placed on sixteen sorting
    /// layers with a Y-sort, not tiles — and reproducing that sort by hand is a second copy of
    /// the renderer. A camera renders exactly what the player sees, sorted exactly as the game
    /// sorts it. Measured before this was written: one 32x32-unit chunk at 32 px/unit is
    /// 3.7 ms, with correct roofs, trees, water and paths.</para>
    ///
    /// <para><b>What the bake must NOT see, and how.</b> Entities, their bars, particles,
    /// weather and every other moving thing live on the same layer as the tiles, so a culling
    /// mask cannot separate them (and all 32 physics layers are spent, so no layer can be added
    /// for it). The bake keeps an explicit WHITELIST — the grid's tilemap renderers and each
    /// building's sprite renderers — and sets <c>forceRenderingOff</c> on everything else for
    /// the length of one synchronous <c>Camera.Render</c>, restoring it in a <c>finally</c>.
    /// Lighting is neutralised the same way: the Global Light2D is held white at 1 and every
    /// other light at 0, so the map does not bake a midnight world or a torch's glow into the
    /// ground. Nothing renders a frame in between, so nothing can see either change.</para>
    ///
    /// <para><b>Progressive, nearest first.</b> The shipped world is 400x250 units — 104
    /// chunks. Baking them all in one frame would be a 0.4 s hitch; the bake spends
    /// <see cref="MinimapStyle.bakeBudgetMs"/> per frame on the dirty chunk nearest the view,
    /// so the ground under the player is ready on the first frame and the rest fills in behind
    /// it. A tile edit or a moved building dirties only the chunks it touches.</para>
    /// </summary>
    public sealed partial class MinimapWorldBaker : IDisposable
    {
        private readonly Transform _host;
        private readonly MinimapStyle _style;

        private Camera _camera;
        private RenderTexture _atlas;
        private RectInt _bounds;
        private int _ppu;
        private int _chunk;
        private int _cols, _rows;
        private bool[] _dirty;
        private int _dirtyCount;
        private bool _rescanBounds = true;
        private string _worldKey;
        private float _nextBuildingScan;
        private bool _subscribed;
        private int _bakedTotal;

        private WorldGridBuilder _grid;
        private BuildingLoader _buildings;

        private readonly Dictionary<int, Rect> _buildingRects = new Dictionary<int, Rect>();
        private readonly HashSet<int> _seenBuildings = new HashSet<int>();

        public MinimapWorldBaker(Transform host, MinimapStyle style)
        {
            _host = host;
            _style = style;
            Subscribe();
        }

        /// <summary>The baked terrain, or null before anything could be baked.</summary>
        public Texture Atlas => _atlas;

        /// <summary>World rect the atlas covers: (x0, y0, width, height).</summary>
        public Vector4 AtlasWorldRect => new Vector4(_bounds.xMin, _bounds.yMin, Mathf.Max(1, _bounds.width), Mathf.Max(1, _bounds.height));

        /// <summary>Chunks still waiting to be baked.</summary>
        public int PendingChunks => _dirtyCount;

        /// <summary>Chunks the atlas holds.</summary>
        public int ChunkCount => _cols * _rows;

        /// <summary>Chunks baked since this baker was created. Diagnostics.</summary>
        public int BakedTotal => _bakedTotal;

        /// <summary>Pixels per world unit of the current atlas.</summary>
        public int PixelsPerUnit => _ppu;

        /// <summary>Mark everything for rebake (the world was swapped or reloaded).</summary>
        public void MarkAllDirty()
        {
            _rescanBounds = true;
        }

        /// <summary>Mark every chunk a world rect touches for rebake.</summary>
        public void MarkDirty(Rect world)
        {
            if (_dirty == null || _chunk <= 0) return;
            int cx0 = Mathf.FloorToInt((world.xMin - _bounds.xMin) / _chunk);
            int cy0 = Mathf.FloorToInt((world.yMin - _bounds.yMin) / _chunk);
            int cx1 = Mathf.FloorToInt((world.xMax - _bounds.xMin) / _chunk);
            int cy1 = Mathf.FloorToInt((world.yMax - _bounds.yMin) / _chunk);
            if (cx1 < 0 || cy1 < 0 || cx0 >= _cols || cy0 >= _rows)
            {
                // Outside the atlas: the world grew, so the bounds have to be rescanned.
                _rescanBounds = true;
                return;
            }
            for (int cy = Mathf.Max(0, cy0); cy <= Mathf.Min(_rows - 1, cy1); cy++)
            for (int cx = Mathf.Max(0, cx0); cx <= Mathf.Min(_cols - 1, cx1); cx++)
                SetDirty(cy * _cols + cx);
        }

        /// <summary>
        /// Advance the bake: rescan when needed, then bake the dirty chunks nearest
        /// <paramref name="focus"/> within the frame budget.
        /// </summary>
        public void Tick(Vector2 focus, string worldKey)
        {
            if (!Application.isPlaying) return;
            if (!ResolveWorld()) return;

            if (worldKey != _worldKey)
            {
                _worldKey = worldKey;
                _rescanBounds = true;
            }

            if (Time.unscaledTime >= _nextBuildingScan)
            {
                _nextBuildingScan = Time.unscaledTime + Mathf.Max(0.1f, _style.buildingScanSeconds);
                ScanBuildings();
            }

            if (_rescanBounds) RescanBounds();
            if (_dirtyCount <= 0 || _atlas == null) return;

            BakeDirty(focus);
        }

        public void Dispose()
        {
            Unsubscribe();
            if (_atlas != null)
            {
                _atlas.Release();
                DestroySafe(_atlas);
                _atlas = null;
            }
            if (_camera != null) DestroySafe(_camera.gameObject);
            _camera = null;
        }

        // ── World discovery ────────────────────────────────────────────────

        private float _nextLookup;

        private bool ResolveWorld()
        {
            // FindObjectOfType walks the scene; once a second is plenty for objects that are
            // created at boot, and an interior with no BuildingLoader must not pay it per frame.
            if ((_grid == null || _buildings == null) && Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                if (_grid == null) _grid = UnityEngine.Object.FindObjectOfType<WorldGridBuilder>();
                if (_buildings == null) _buildings = UnityEngine.Object.FindObjectOfType<BuildingLoader>();
            }
            return _grid != null && _grid.Grid != null;
        }

        private void RescanBounds()
        {
            _rescanBounds = false;
            bool any = false;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;

            foreach (var tm in _grid.Grid.GetComponentsInChildren<Tilemap>())
            {
                var tr = tm.GetComponent<TilemapRenderer>();
                if (tr == null || !tr.enabled) continue;
                if (tm.GetUsedTilesCount() == 0) continue;
                // Compress only here — on a world swap or a growth event, never per frame —
                // because ClearAllTiles leaves the old allocation bounds behind and an interior
                // would otherwise bake the whole outdoor world's empty rectangle.
                tm.CompressBounds();
                var b = tm.cellBounds;
                if (b.size.x <= 0 || b.size.y <= 0) continue;
                any = true;
                x0 = Mathf.Min(x0, b.xMin); y0 = Mathf.Min(y0, b.yMin);
                x1 = Mathf.Max(x1, b.xMax); y1 = Mathf.Max(y1, b.yMax);
            }

            if (_buildings != null)
            {
                var list = _buildings.SpawnedBuildings;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    if (b == null || !b.isActiveAndEnabled || !b.TryGetWorldRect(out var r)) continue;
                    any = true;
                    x0 = Mathf.Min(x0, Mathf.FloorToInt(r.xMin)); y0 = Mathf.Min(y0, Mathf.FloorToInt(r.yMin));
                    x1 = Mathf.Max(x1, Mathf.CeilToInt(r.xMax)); y1 = Mathf.Max(y1, Mathf.CeilToInt(r.yMax));
                }
            }

            if (!any) return;

            int chunk = Mathf.Max(8, _style.chunkUnits);
            x0 = FloorTo(x0, chunk); y0 = FloorTo(y0, chunk);
            x1 = CeilTo(x1, chunk);  y1 = CeilTo(y1, chunk);
            var bounds = new RectInt(x0, y0, x1 - x0, y1 - y0);

            int ppu = Mathf.Clamp(_style.bakePixelsPerUnit, 1, 64);
            int maxDim = Mathf.Max(256, _style.maxAtlasSize);
            while (ppu > 1 && (bounds.width * ppu > maxDim || bounds.height * ppu > maxDim)) ppu--;

            bool reallocate = _atlas == null || !bounds.Equals(_bounds) || ppu != _ppu || chunk != _chunk;
            _bounds = bounds;
            _ppu = ppu;
            _chunk = chunk;
            _cols = bounds.width / chunk;
            _rows = bounds.height / chunk;

            if (reallocate) AllocateAtlas();
            _dirty = new bool[_cols * _rows];
            _dirtyCount = 0;
            for (int i = 0; i < _dirty.Length; i++) SetDirty(i);
        }

        private void ScanBuildings()
        {
            if (_buildings == null || _dirty == null) return;
            _seenBuildings.Clear();
            var list = _buildings.SpawnedBuildings;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b == null) continue;
                int id = b.GetInstanceID();
                _seenBuildings.Add(id);
                Rect r = default;
                bool live = b.isActiveAndEnabled && b.TryGetWorldRect(out r);
                if (_buildingRects.TryGetValue(id, out var old))
                {
                    if (live && Approximately(old, r)) continue;
                    MarkDirty(old);
                }
                if (live)
                {
                    MarkDirty(r);
                    _buildingRects[id] = r;
                }
                else _buildingRects.Remove(id);
            }

            // Buildings that were destroyed: their ground is bare now.
            if (_buildingRects.Count > _seenBuildings.Count)
            {
                s_removed.Clear();
                foreach (var kv in _buildingRects) if (!_seenBuildings.Contains(kv.Key)) s_removed.Add(kv.Key);
                foreach (int id in s_removed) { MarkDirty(_buildingRects[id]); _buildingRects.Remove(id); }
            }
        }

        private static List<int> s_removed = new List<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMinimapWorldBakerStatics()
        {
            s_removed = new List<int>();
        }

        private static bool Approximately(Rect a, Rect b)
            => Mathf.Abs(a.xMin - b.xMin) < 0.01f && Mathf.Abs(a.yMin - b.yMin) < 0.01f &&
               Mathf.Abs(a.width - b.width) < 0.01f && Mathf.Abs(a.height - b.height) < 0.01f;

        // ── Tile edits ─────────────────────────────────────────────────────

        private void Subscribe()
        {
            if (_subscribed) return;
            Tilemap.tilemapTileChanged += OnTilesChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            Tilemap.tilemapTileChanged -= OnTilesChanged;
            _subscribed = false;
        }

        private void OnTilesChanged(Tilemap map, Tilemap.SyncTile[] tiles)
        {
            if (tiles == null || tiles.Length == 0 || _grid == null || _grid.Grid == null) return;
            if (map == null || map.transform.parent != _grid.Grid.transform) return;

            // A world load paints tens of thousands of cells at once; that is a rebake of
            // everything, and walking the array would only arrive at the same answer slower.
            if (tiles.Length > 4096) { _rescanBounds = true; return; }

            for (int i = 0; i < tiles.Length; i++)
            {
                var p = tiles[i].position;
                MarkDirty(new Rect(p.x, p.y, 1f, 1f));
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void SetDirty(int index)
        {
            if (_dirty[index]) return;
            _dirty[index] = true;
            _dirtyCount++;
        }

        private static int FloorTo(int v, int step) => Mathf.FloorToInt((float)v / step) * step;
        private static int CeilTo(int v, int step) => Mathf.CeilToInt((float)v / step) * step;

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
