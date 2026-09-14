using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;
using Debug = UnityEngine.Debug;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Keeps the zones around the player painted when the active map slot is a LIVE Seed World,
    /// and drops the far ones. Phase 5 of <c>.github/SEED_WORLD_ROADMAP.md</c>.
    ///
    /// <para><b>A zone is the chunk.</b> The rest of the game already speaks in 50x50 zones —
    /// overlays, zone names, the Tile editor's save unit, buildings and spawners keyed by zone —
    /// so streaming by zone means every other system sees an ordinary map whose far zones happen
    /// to be blank. The older <c>ChunkStreamer</c> knows one tile layer and nothing about any of
    /// that, which is why it is not the vehicle here.</para>
    ///
    /// <para><b>It paints through <c>OverlayLoader</c>, the path every map uses.</b> The Collision
    /// layer it writes reaches <c>WorldCollisionBaker</c> through <c>tilemapTileChanged</c>, the
    /// same incremental route a brush stroke takes; the only extra step is dropping the
    /// pathfinder's walkability cache, which that route does not do by itself.</para>
    ///
    /// <para><b>It never decides a slot changed from a single signal.</b> A world wipe bumps
    /// <see cref="WorldGridBuilder.ClearGeneration"/> (a slot load, an interior, <c>reloadtiles</c>),
    /// and the active slot file is re-read on that and on a slow timer as a backstop for a boot
    /// that loads its slot without wiping anything.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeedWorldLiveStreamer : MonoBehaviour
    {
        /// <summary>Zones within this Chebyshev distance of the player's zone are kept painted.</summary>
        public const int LoadRadius = 1;

        /// <summary>Zones beyond this distance are dropped. Wider than the load radius so a
        /// player pacing along a zone border does not load and unload the same zone every step.</summary>
        public const int UnloadRadius = 2;

        /// <summary>Time spent loading NEIGHBOUR zones per frame. The zone under the player is
        /// always loaded at once, whatever it costs.</summary>
        public const float FrameBudgetMs = 8f;

        private const float SlotPollSeconds = 1f;

        private WorldGridBuilder _grid;
        private SeedWorldLiveWorld _world;
        private string _slot;
        private int _clearGeneration = -1;
        private float _nextSlotPoll;
        private bool _slotDirty = true;
        private bool _suspended;
        private System.DateTime _markerWriteUtc;

        private readonly HashSet<Vector2Int> _loaded = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _scratch = new List<Vector2Int>();
        private TileBase[] _emptyBlock;

        private static SeedWorldLiveStreamer s_instance;

        // Domain Reload is OFF: a destroyed streamer from the last Play session must not answer.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            s_instance = null;
        }

        public static SeedWorldLiveStreamer Instance => s_instance;

        /// <summary>
        /// The streamer, created the first time a generated world is entered. The boot no longer
        /// installs it: while Seed World is a lab (<see cref="SeedWorldLab"/>) nothing in a normal
        /// session may depend on it, and a session never starts inside a generated world, so the
        /// only moment one is needed is the moment the lab walks the player into one.
        /// </summary>
        public static SeedWorldLiveStreamer EnsureInstance()
        {
            if (s_instance != null) return s_instance;
            var existing = FindObjectOfType<SeedWorldLiveStreamer>();
            if (existing != null) return existing;
            return new GameObject("[SeedWorldLiveStreamer]").AddComponent<SeedWorldLiveStreamer>();
        }

        public SeedWorldLiveWorld World => _world;
        public string ActiveSlot => _slot;
        public int LoadedZoneCount => _loaded.Count;
        public int ZonesGenerated { get; private set; }
        public int ZonesFromDisk { get; private set; }
        public int ZonesUnloaded { get; private set; }
        public long LastZoneMs { get; private set; }
        public long WorstZoneMs { get; private set; }
        public string LastOpenError { get; private set; }

        public bool IsLoaded(Vector2Int zone) => _loaded.Contains(zone);

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { enabled = false; return; }
            s_instance = this;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        /// <summary>Re-read the active slot on the next frame. The Seed World editor calls it after a build.</summary>
        public void RequestResync() => _slotDirty = true;

        /// <summary>
        /// Drive a world without the active-slot file, which lives under persistentDataPath and
        /// would make a fixture write the real machine's state.
        /// </summary>
        internal void AttachForTests(WorldGridBuilder grid, SeedWorldLiveWorld world)
        {
            _grid = grid;
            _world = world;
            _slot = world != null ? world.Slot : null;
            _clearGeneration = grid != null ? grid.ClearGeneration : -1;
            _loaded.Clear();
            _slotDirty = false;
        }

        /// <summary>
        /// Adopt the active slot NOW rather than next frame: whatever a world wipe cleared is
        /// forgotten and the slot is re-read. For a caller that is about to call <see cref="SyncAll"/>
        /// in the same frame it loaded a slot.
        /// </summary>
        public void OpenNow()
        {
            if (_grid == null) _grid = FindObjectOfType<WorldGridBuilder>();
            if (_grid != null && _grid.ClearGeneration != _clearGeneration)
            {
                _clearGeneration = _grid.ClearGeneration;
                _loaded.Clear();
            }
            _slotDirty = false;
            RefreshSlot(true);
        }

        private void Update()
        {
            if (_grid == null)
            {
                _grid = FindObjectOfType<WorldGridBuilder>();
                if (_grid == null) return;
            }

            // Inside an interior the tilemaps hold the interior; whatever this painted is gone.
            if (WorldTransitionService.IsBaseWorldContentSuspended)
            {
                _loaded.Clear();
                _suspended = true;
                return;
            }
            if (_suspended) { _suspended = false; _slotDirty = true; }

            if (_grid.ClearGeneration != _clearGeneration)
            {
                _clearGeneration = _grid.ClearGeneration;
                _loaded.Clear();
                _slotDirty = true;
            }

            if (_slotDirty || Time.unscaledTime >= _nextSlotPoll)
            {
                bool force = _slotDirty;
                _slotDirty = false;
                _nextSlotPoll = Time.unscaledTime + SlotPollSeconds;
                RefreshSlot(force);
            }

            if (_world == null) return;
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            Sync(player.position);
        }

        /// <summary>
        /// Re-read the active slot. Unforced (the timer) it only reacts to a different slot name;
        /// forced (a world wipe, a build) it also notices the same slot rebuilt, by the marker's
        /// write time — replanning costs a few hundred milliseconds, so an interior exit into the
        /// same world keeps the plan it has.
        /// </summary>
        private void RefreshSlot(bool force)
        {
            string slot = MapEditorActiveSlot.Read();
            bool sameSlot = string.Equals(slot, _slot, System.StringComparison.OrdinalIgnoreCase);
            if (sameSlot && !force) return;

            var request = SeedWorldBakeRequest.ForSlot(slot);
            var markerTime = request != null && System.IO.File.Exists(request.MarkerPath)
                ? System.IO.File.GetLastWriteTimeUtc(request.MarkerPath) : default;
            if (sameSlot && markerTime == _markerWriteUtc) return;

            _slot = slot;
            _markerWriteUtc = markerTime;
            _world = null;
            _loaded.Clear();
            LastOpenError = null;

            if (request == null) { LastOpenError = "slot base"; return; }
            if (!System.IO.File.Exists(request.MarkerPath)) { LastOpenError = "no es Seed World"; return; }

            var sw = Stopwatch.StartNew();
            _world = SeedWorldLiveWorld.TryOpen(request, new SeedWorldTilePalette(TerrainCatalogLoader.Load()), out string error);
            LastOpenError = error;
            if (_world != null)
                Debug.Log($"[SeedWorldLive] '{slot}' abierto en vivo: {_world.Plan.ZonesX}x{_world.Plan.ZonesY} zonas, " +
                          $"plan en {sw.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Make the painted set match what can be seen: the player's zone and its neighbours, plus
        /// every zone the camera's view touches. The camera matters because the runtime editors
        /// pan and zoom it away from the player — an author scrolling the Tile editor across a
        /// live world would otherwise look at blank zones with trees standing on nothing.
        /// </summary>
        public void Sync(Vector2 playerWorld) => Sync(playerWorld, CameraViewRect());

        public void Sync(Vector2 playerWorld, Rect? view) => SyncStep(playerWorld, view);

        /// <summary>One frame's worth of streaming; true when anything was loaded or dropped.</summary>
        private bool SyncStep(Vector2 playerWorld, Rect? view)
        {
            if (_world == null || _grid == null) return false;
            var plan = _world.Plan;
            var focus = ZoneAt(playerWorld);

            // Two areas, never their bounding box: an author who pans the camera to a far corner
            // needs that corner and the player's surroundings, not every zone in between.
            var around = Clip(new RectInt(focus.x - LoadRadius, focus.y - LoadRadius, 2 * LoadRadius + 1, 2 * LoadRadius + 1));
            RectInt? seen = null;
            if (view.HasValue)
            {
                var v = view.Value;
                var lo = ZoneAt(new Vector2(v.xMin - ViewMarginTiles, v.yMin - ViewMarginTiles));
                var hi = ZoneAt(new Vector2(v.xMax + ViewMarginTiles, v.yMax + ViewMarginTiles));
                // A camera zoomed out over a huge world would ask for hundreds of zones; beyond the
                // cap the view simply shows the edge of what is painted, centred on the camera.
                var centre = ZoneAt(v.center);
                lo = Vector2Int.Max(lo, centre - new Vector2Int(MaxViewZoneSpan, MaxViewZoneSpan));
                hi = Vector2Int.Min(hi, centre + new Vector2Int(MaxViewZoneSpan, MaxViewZoneSpan));
                seen = Clip(new RectInt(lo.x, lo.y, hi.x - lo.x + 1, hi.y - lo.y + 1));
            }

            bool changed = false;
            int keep = UnloadRadius - LoadRadius;

            // Dropping a zone costs ~8 ms (clearing painted tiles raises every tile-change
            // listener), and crossing a border leaves three zones behind at once; one per frame
            // spreads that out. Only a pile-up — a teleport, a camera zoomed out and back — drops
            // everything out of range in one go.
            _scratch.Clear();
            foreach (var zone in _loaded)
                if (!Inside(around, zone, keep) && !(seen.HasValue && Inside(seen.Value, zone, keep)))
                    _scratch.Add(zone);
            if (_scratch.Count > 0)
            {
                FlushTileEdits();
                _scratch.Sort((a, b) => (b - focus).sqrMagnitude.CompareTo((a - focus).sqrMagnitude));
                int drop = _loaded.Count > MaxLoadedZones ? _scratch.Count : 1;
                for (int i = 0; i < drop; i++) UnloadZone(_scratch[i]);
                changed = true;
            }

            if (!_loaded.Contains(focus)) { LoadZone(focus); changed = true; }

            _scratch.Clear();
            CollectMissing(around);
            if (seen.HasValue) CollectMissing(seen.Value);
            _scratch.Sort((a, b) => (a - focus).sqrMagnitude.CompareTo((b - focus).sqrMagnitude));

            var budget = Stopwatch.StartNew();
            int loadedNow = 0;
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (budget.Elapsed.TotalMilliseconds > FrameBudgetMs) break;
                LoadZone(_scratch[i]);
                loadedNow++;
                changed = true;
            }
            PendingZoneCount = _scratch.Count - loadedNow;

            if (changed) PathFinder.InvalidateWalkability();
            return changed;
        }

        private RectInt Clip(RectInt r)
        {
            int x0 = Mathf.Max(0, r.xMin), y0 = Mathf.Max(0, r.yMin);
            int x1 = Mathf.Min(_world.Plan.ZonesX, r.xMax), y1 = Mathf.Min(_world.Plan.ZonesY, r.yMax);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        private static bool Inside(RectInt r, Vector2Int zone, int grow)
            => zone.x >= r.xMin - grow && zone.x < r.xMax + grow && zone.y >= r.yMin - grow && zone.y < r.yMax + grow;

        private void CollectMissing(RectInt r)
        {
            for (int zy = r.yMin; zy < r.yMax; zy++)
                for (int zx = r.xMin; zx < r.xMax; zx++)
                {
                    var zone = new Vector2Int(zx, zy);
                    if (!_loaded.Contains(zone) && !_scratch.Contains(zone)) _scratch.Add(zone);
                }
        }

        /// <summary>Above this many painted zones every zone out of range is dropped at once.</summary>
        public const int MaxLoadedZones = 40;

        /// <summary>Zones inside the wanted area still waiting for a frame with budget left.</summary>
        public int PendingZoneCount { get; private set; }

        /// <summary>Tiles of margin around the camera view that are kept painted, so panning
        /// does not reveal a zone edge before its tiles arrive.</summary>
        public const int ViewMarginTiles = 8;

        /// <summary>Zones either side of the camera centre the view may ask for.</summary>
        public const int MaxViewZoneSpan = 4;

        private Vector2Int ZoneAt(Vector2 world)
        {
            var plan = _world.Plan;
            int tx = Mathf.FloorToInt(world.x) - plan.Origin.x, ty = Mathf.FloorToInt(world.y) - plan.Origin.y;
            return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((float)tx / plan.ZoneSize), 0, plan.ZonesX - 1),
                                  Mathf.Clamp(Mathf.FloorToInt((float)ty / plan.ZoneSize), 0, plan.ZonesY - 1));
        }

        private static Rect? CameraViewRect()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return null;
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var p = cam.transform.position;
            return new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
        }

        /// <summary>Load everything the player's surroundings need now, ignoring the frame budget.</summary>
        public void SyncAll(Vector2 playerWorld)
        {
            if (_world == null) return;
            for (int i = 0; i < 256; i++)
                if (!SyncStep(playerWorld, null)) break;
        }

        private void LoadZone(Vector2Int zone)
        {
            var sw = Stopwatch.StartNew();
            var root = _world.ZoneOverlay(zone.x, zone.y, out bool fromDisk);
            var offset = _world.Plan.ZoneOffset(zone.x, zone.y);
            int z = _world.ZoneSize;

            // An edited file may paint fewer cells than a generated zone; clear first so no
            // generated tile survives under it.
            if (fromDisk) ClearZoneTiles(offset, z);

            OverlayLoader.LoadOverlayFromRoot(root, _grid, offset.x, offset.y,
                clearLayerRegion: false, regionWidth: z, regionHeight: z,
                sourceLabel: fromDisk ? _world.OverlayPathFor(zone.x, zone.y) : $"(seed world {_world.Plan.ZoneNames[zone.x, zone.y]})");

            if (fromDisk && TileEditorManager.HasInstance)
            {
                OverlayLoader.ApplyCollisionTags(root, TileEditorManager.Instance.CollisionTags, offset.x, offset.y);
                OverlayLoader.ApplyLayerJumps(root, TileEditorManager.Instance.LayerJumps, offset.x, offset.y);
            }

            _loaded.Add(zone);
            if (fromDisk) ZonesFromDisk++; else ZonesGenerated++;
            LastZoneMs = sw.ElapsedMilliseconds;
            if (LastZoneMs > WorstZoneMs) WorstZoneMs = LastZoneMs;
        }

        private void UnloadZone(Vector2Int zone)
        {
            ClearZoneTiles(_world.Plan.ZoneOffset(zone.x, zone.y), _world.ZoneSize);
            _loaded.Remove(zone);
            ZonesUnloaded++;
        }

        private void ClearZoneTiles(Vector2Int offset, int z)
        {
            if (_emptyBlock == null || _emptyBlock.Length != z * z) _emptyBlock = new TileBase[z * z];
            var bounds = new BoundsInt(offset.x, offset.y, 0, z, z, 1);
            using (WorldStreamingSignals.UnloadingTiles())
            {
                var tilemaps = _grid.Grid != null ? _grid.Grid.GetComponentsInChildren<Tilemap>(true) : null;
                if (tilemaps == null) return;
                for (int i = 0; i < tilemaps.Length; i++)
                {
                    // Most of the grid's layers never hold a tile out here; skipping them is free.
                    var used = tilemaps[i].cellBounds;
                    if (used.xMax <= bounds.xMin || used.xMin >= bounds.xMax ||
                        used.yMax <= bounds.yMin || used.yMin >= bounds.yMax) continue;
                    tilemaps[i].SetTilesBlock(bounds, _emptyBlock);
                }
            }
        }

        /// <summary>
        /// A zone about to be dropped may hold brush strokes not yet on disk; clearing its tiles
        /// first would take them with it. Saving them makes the zone an edited one, which the next
        /// load reads back from its file instead of generating.
        /// </summary>
        private static void FlushTileEdits()
        {
            if (!TileEditorManager.HasInstance) return;
            var persistence = TileEditorManager.Instance.Persistence;
            if (persistence != null && persistence.HasUnsavedChanges) persistence.SaveAllDirty();
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
