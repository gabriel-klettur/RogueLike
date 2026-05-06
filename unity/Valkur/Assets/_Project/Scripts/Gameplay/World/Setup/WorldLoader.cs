using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Gameplay.World.Worlds;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads the full multi-zone world by iterating over the zone database
    /// and painting each zone's overlay and collision data at the correct offset.
    /// Maps to Python's WorldManager + MapManager that loads all zone overlays
    /// and collision grids for the base world.
    /// </summary>
    public class WorldLoader : MonoBehaviour
    {
        [Tooltip("Zone database loader that provides zone entries.")]
        [SerializeField] private ZoneDatabaseLoader _databaseLoader;

        [Tooltip("WorldGridBuilder for tilemap access.")]
        [SerializeField] private WorldGridBuilder _gridBuilder;

        [Tooltip("Load world automatically after database is loaded.")]
        [SerializeField] private bool _autoLoad = true;

        private int _overlaysLoaded;
        private int _collisionsLoaded;

        /// <summary>Number of overlay files successfully loaded.</summary>
        public int OverlaysLoaded => _overlaysLoaded;

        /// <summary>Number of collision files successfully loaded.</summary>
        public int CollisionsLoaded => _collisionsLoaded;

        private void Start()
        {
            if (_autoLoad)
                LoadFullWorld();
        }

        /// <summary>
        /// Load all zone overlays and collision grids from the zone database.
        /// Synchronous wrapper that drains the progressive coroutine in one
        /// shot — used by autoLoad and tests that don't need stage reporting.
        /// </summary>
        public void LoadFullWorld()
        {
            // The progressive iterator only yields plain `null` between work
            // chunks, so MoveNext() drives the entire pipeline to completion
            // without any frame waiting. Same end state as the previous
            // monolithic implementation.
            var iter = LoadFullWorldProgressively(null);
            while (iter.MoveNext()) { }
        }

        /// <summary>
        /// Coroutine variant of <see cref="LoadFullWorld"/> that yields between
        /// each sub-stage and reports a label via <paramref name="reportStage"/>
        /// (typically wired to <c>LoadingReporter.ReportStage</c>) so the loading
        /// screen can show "Painting zone overlays" / "Linking world colliders"
        /// / "Applying tile overrides" with the bar advancing between them.
        ///
        /// Without yields the entire world-load (24+ zone overlays + collisions
        /// + tile-editor overrides) ran on one frame and surfaced as a single
        /// "Loading world" stage that froze the loading screen.
        /// </summary>
        public System.Collections.IEnumerator LoadFullWorldProgressively(System.Action<string> reportStage)
        {
            if (_databaseLoader == null)
            {
                _databaseLoader = FindObjectOfType<ZoneDatabaseLoader>();
                if (_databaseLoader == null)
                {
                    Debug.LogError("[WorldLoader] ZoneDatabaseLoader not found.", this);
                    yield break;
                }
            }

            if (_gridBuilder == null)
            {
                _gridBuilder = FindObjectOfType<WorldGridBuilder>();
                if (_gridBuilder == null)
                {
                    Debug.LogError("[WorldLoader] WorldGridBuilder not found.", this);
                    yield break;
                }
            }

            _overlaysLoaded = 0;
            _collisionsLoaded = 0;

            var entries = _databaseLoader.Entries;
            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning("[WorldLoader] No zone entries in database.");
                yield break;
            }

            // Defensive dedup: avoid painting the same overlay/collision into the same
            // (offsetX, offsetY) twice. This guards against malformed zones_database.json
            // and prevents stacked tilemap fillrate cost on layers like Ground.
            var paintedOverlays   = new HashSet<(int, int, string)>();
            var paintedCollisions = new HashSet<(int, int, string)>();
            int skippedOverlays   = 0;
            int skippedCollisions = 0;

            // Each overlay must paint within its declared zone footprint. Pass these
            // dimensions to OverlayLoader so any out-of-bounds tile is skipped with a
            // logged warning instead of bleeding into the neighbouring zone.
            int zoneW = _databaseLoader.ZoneWidthTiles;
            int zoneH = _databaseLoader.ZoneHeightTiles;

            // Resolve the active world so overlay/collision file paths nest under
            // StreamingAssets/Worlds/<slug>/ for non-base worlds. WorldId.Base keeps
            // the legacy flat layout (StreamingAssets/Maps, StreamingAssets/Collisions).
            var worldManager = ServiceLocator.Get<IWorldManager>();
            var activeWorldId = worldManager?.Active?.WorldId ?? WorldId.Base;
            string mapsDir       = WorldStreamingPaths.DirectoryFor(activeWorldId, "Maps");
            string collisionsDir = WorldStreamingPaths.DirectoryFor(activeWorldId, "Collisions");

            // ── Pass 1: overlays ────────────────────────────────────────────
            // Mid-pass yields keep the loading screen responsive when the
            // world has many zones. ~6 yields total across 24 zones gives
            // smooth bar advancement without measurable per-pass overhead.
            reportStage?.Invoke("Painting zone overlays");
            yield return null;
            int batchSize = Mathf.Max(1, entries.Count / 6);
            int processed = 0;
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.overlayFile))
                {
                    var key = (entry.offsetX, entry.offsetY, entry.overlayFile);
                    if (paintedOverlays.Add(key))
                    {
                        string overlayPath = Path.Combine(mapsDir, entry.overlayFile);
                        OverlayLoader.LoadOverlayFromPath(overlayPath, _gridBuilder,
                            entry.offsetX, entry.offsetY,
                            clearLayerRegion: false, regionWidth: zoneW, regionHeight: zoneH);
                        _overlaysLoaded++;
                    }
                    else
                    {
                        Debug.LogWarning($"[WorldLoader] Skipped duplicate overlay '{entry.overlayFile}' at ({entry.offsetX},{entry.offsetY}).");
                        skippedOverlays++;
                    }
                }
                processed++;
                if (processed % batchSize == 0) yield return null;
            }

            // ── Pass 2: collisions ──────────────────────────────────────────
            reportStage?.Invoke("Linking world colliders");
            yield return null;
            processed = 0;
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.collisionFile))
                {
                    var key = (entry.offsetX, entry.offsetY, entry.collisionFile);
                    if (paintedCollisions.Add(key))
                    {
                        LoadCollisionGrid(collisionsDir, entry.collisionFile,
                            entry.offsetX, entry.offsetY, zoneW, zoneH);
                    }
                    else
                    {
                        skippedCollisions++;
                    }
                }
                processed++;
                if (processed % batchSize == 0) yield return null;
            }

            // ── Pass 3: persisted tile-editor overrides ─────────────────────
            // Restores edits the user made in previous play sessions
            // (one JSON per zone in persistentDataPath/MapOverrides).
            reportStage?.Invoke("Applying tile overrides");
            yield return null;
            var zoneManager = FindObjectOfType<ZoneManager>();
            if (zoneManager != null)
                Valkur.Gameplay.TileEditor.TileOverlayPersistence.ApplyAllOverrides(_gridBuilder, zoneManager);

            Debug.Log($"[WorldLoader] Full world loaded: {_overlaysLoaded} overlays, " +
                      $"{_collisionsLoaded} collision grids across {entries.Count} zones " +
                      $"(skipped duplicates: {skippedOverlays} overlays, {skippedCollisions} collisions).");
        }

        /// <summary>
        /// Parse a collision JSON file (50x50 grid of "#"/"."/"=") and paint
        /// wall tiles onto the Collision tilemap layer.
        /// "#" = solid wall, "." = walkable, "=" = special connector.
        /// When <paramref name="maxWidth"/>/<paramref name="maxHeight"/> &gt; 0, any cell outside
        /// the zone footprint is skipped and a single warning is logged.
        /// </summary>
        private void LoadCollisionGrid(string collisionsDir, string collisionFileName,
            int offsetX, int offsetY, int maxWidth = 0, int maxHeight = 0)
        {
            string jsonPath = Path.Combine(collisionsDir, collisionFileName);

            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[WorldLoader] Collision file not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var rows = MiniJsonRuntime.Deserialize(json) as List<object>;
            if (rows == null)
            {
                Debug.LogError($"[WorldLoader] Failed to parse collision file: {collisionFileName}");
                return;
            }

            var collisionTilemap = _gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            if (collisionTilemap == null)
            {
                Debug.LogWarning("[WorldLoader] Collision tilemap layer not found.");
                return;
            }

            int cellsSet = 0;
            int cellsClipped = 0;
            int rowCount = rows.Count;

            for (int y = 0; y < rowCount; y++)
            {
                var row = rows[y] as List<object>;
                if (row == null) continue;

                for (int x = 0; x < row.Count; x++)
                {
                    string cell = row[x] as string;
                    if (cell != "#") continue;  // Only paint solid walls

                    // Y-flip: row 0 in Python is top, row 0 in Unity tilemap is bottom
                    int flippedY = rowCount - 1 - y;

                    // Bounds clip — refuse to paint a wall outside the declared zone footprint.
                    if (maxWidth > 0 && x >= maxWidth) { cellsClipped++; continue; }
                    if (maxHeight > 0 && flippedY >= maxHeight) { cellsClipped++; continue; }

                    var tile = GetWallCollisionTile();
                    collisionTilemap.SetTile(
                        new Vector3Int(offsetX + x, offsetY + flippedY, 0), tile);
                    cellsSet++;
                }
            }

            if (cellsClipped > 0)
                Debug.LogWarning($"[WorldLoader] Collision '{collisionFileName}': " +
                                 $"{cellsClipped} cell(s) clipped to zone footprint {maxWidth}x{maxHeight}.");

            if (cellsSet > 0)
            {
                _collisionsLoaded++;
                Debug.Log($"[WorldLoader] Collision '{collisionFileName}': {cellsSet} wall cells " +
                          $"at offset ({offsetX},{offsetY}).");
            }
        }

        private static TileBase _wallTile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _wallTile = null;
        }

        /// <summary>
        /// Get or create a simple collision tile for wall cells.
        /// Uses Grid collider type for TilemapCollider2D.
        /// </summary>
        private static TileBase GetWallCollisionTile()
        {
            if (_wallTile != null) return _wallTile;

            // Try to load a dedicated wall sprite, fall back to a plain tile
            var sprite = Resources.Load<Sprite>("Tiles/wall");
            if (sprite == null)
                sprite = Resources.Load<Sprite>("Tiles/floor");

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = new Color(1f, 1f, 1f, 0f); // Invisible collision-only tile
            tile.colliderType = Tile.ColliderType.Grid;
            _wallTile = tile;
            return tile;
        }
    }
}
