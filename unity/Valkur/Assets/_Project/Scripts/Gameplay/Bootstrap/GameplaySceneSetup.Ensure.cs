using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
using Valkur.Gameplay.World.Chunks;
using Valkur.Gameplay.World.Dungeon.Strategy;
using Valkur.Gameplay.World.Worlds;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        private void EnsureZoneManager()
        {
            if (FindObjectOfType<World.ZoneManager>() != null) return;
            var zoneManagerGo = new GameObject("ZoneManager");
            zoneManagerGo.AddComponent<World.ZoneManager>();
            zoneManagerGo.transform.SetParent(GetSceneContainer("[Core]"), false);
            Debug.Log("[GameplaySceneSetup] ZoneManager created.");
        }

        // Wires the DayNightCycle singleton into the scene if no inspector-
        // authored instance is present. The component lazy-resolves the URP
        // Global Light 2D on its first Update so it must exist before any
        // gameplay frame runs — this step lives between EnsureZoneManager
        // (early) and SpawnPlayer (late). Idempotent: noop if an inspector-
        // wired DayNightCycle already lives in the scene.
        private void EnsureDayNightCycle()
        {
            if (FindObjectOfType<World.DayNightCycle>() != null) return;
            var go = new GameObject("DayNightCycle");
            go.AddComponent<World.DayNightCycle>();
            go.transform.SetParent(GetSceneContainer("[World]"), false);
            Debug.Log("[GameplaySceneSetup] DayNightCycle created.");
        }

        // ── Phase 1: WorldManager wiring ──────────────────────────────────────────
        // Creates the IWorldManager service, loads + activates the legacy "base"
        // WorldDescriptor so Active is non-null from the moment any downstream
        // step (TileOverlayPersistence, MapEditorManager, SaveService) runs.
        // Existing scenes/prefabs do not have to wire anything: when no
        // WorldDescriptor asset is supplied, the in-code legacy fallback is
        // built so single-world boot stays byte-compatible. Multi-world builds
        // wire the assets later via [SerializeField] on this script.
        //
        // Idempotent: repeated calls are safe — the manager lives in
        // ServiceLocator and is reused across reentrancy paths (DevConsole's
        // reset, SaveService rehydration, etc.).
        private void EnsureWorldManager()
        {
            if (ServiceLocator.TryGet<IWorldManager>(out var existing) && existing.Active != null)
                return;

            var manager = existing ?? new WorldManager();
            if (existing == null)
                ServiceLocator.Register<IWorldManager>(manager);

            // Phase 1: prefer the designer-wired descriptor when one is set
            // in the inspector. Falls back to the in-code legacy base so a
            // scene without any wiring still boots single-world byte-for-byte.
            var descriptor = initialWorld != null
                ? initialWorld
                : WorldDescriptor.CreateLegacyBase();
            try
            {
                manager.LoadAndActivateAsync(descriptor).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameplaySceneSetup] WorldManager activation failed: {ex.Message}");
            }
            Debug.Log($"[GameplaySceneSetup] WorldManager active world: {manager.Active?.WorldId}");
        }

        private void BuildWorldGrid()
        {
            var gridGo = new GameObject("WorldGridBuilder");
            _gridBuilder = gridGo.AddComponent<World.WorldGridBuilder>();
            gridGo.transform.SetParent(GetSceneContainer("[World]"), false);
        }

        /// <summary>
        /// Load the world map. When loadFullWorld is true, uses ZoneDatabaseLoader + WorldLoader
        /// to paint all 24 zones at their correct offsets. Otherwise loads a single overlay.
        /// After world load, generates the procedural dungeon south of the lobby.
        ///
        /// Phase 2.6: when the active descriptor opts into chunk streaming
        /// (<see cref="WorldDescriptor.UseChunkStreaming"/> = true) the legacy
        /// load is skipped — chunks are produced procedurally and painted
        /// on-demand by <see cref="EnsureProceduralChunkStreamer"/> after the
        /// player has spawned.
        /// </summary>
        private void LoadWorld()
        {
            // Synchronous wrapper — drains LoadWorldProgressively in one shot
            // for code paths that don't have access to a coroutine context.
            // The progressive coroutine only yields plain `null`, so MoveNext
            // pumps the entire pipeline to completion synchronously.
            var iter = LoadWorldProgressively();
            while (iter.MoveNext()) { }
        }

        /// <summary>
        /// Progressive world load — yields between sub-stages so the loading
        /// screen can advance ("Loading zone database" → "Painting zone
        /// overlays" → "Linking world colliders" → "Applying tile overrides"
        /// → "Generating procedural dungeon") instead of freezing on a single
        /// monolithic "Loading world" stage.
        /// </summary>
        private System.Collections.IEnumerator LoadWorldProgressively()
        {
            if (initialWorld != null && initialWorld.UseChunkStreaming)
            {
                Debug.Log($"[GameplaySceneSetup] Procedural chunk streaming enabled for " +
                          $"'{initialWorld.Slug}' — skipping legacy world/overlay load. " +
                          $"Streamer will be wired after player spawn.");
                yield break;
            }

            if (loadFullWorld)
            {
                // 1) Zone database — populates ZoneManager with all zones.
                Report("Loading zone database"); yield return null;
                var dbLoaderGo = new GameObject("ZoneDatabaseLoader");
                var dbLoader = dbLoaderGo.AddComponent<World.ZoneDatabaseLoader>();
                // Start() would load the database a second time a frame later; we load
                // it explicitly below so the bootstrap controls the ordering.
                dbLoader.SetAutoLoad(false);
                dbLoaderGo.transform.SetParent(GetSceneContainer("[World]"), false);
                dbLoader.LoadDatabase();

                // 2) Overlays + collisions — driven progressively. Sub-stages
                //    "Painting zone overlays" / "Linking world colliders" /
                //    "Applying tile overrides" are reported by WorldLoader.
                var worldLoaderGo = new GameObject("WorldLoader");
                var worldLoader = worldLoaderGo.AddComponent<World.WorldLoader>();
                // Same double-load trap: the progressive load below is the real one.
                // Left on, Start() repainted every zone, re-parsed every overlay and
                // re-baked every collider a frame after this finished.
                worldLoader.SetAutoLoad(false);
                worldLoaderGo.transform.SetParent(GetSceneContainer("[World]"), false);
                yield return worldLoader.LoadFullWorldProgressively(stage =>
                {
                    Report(stage);
                });

                // 3) Procedural dungeon generation south of the lobby.
                Report("Generating procedural dungeon"); yield return null;
                GenerateDungeon(dbLoader);

                Debug.Log("[GameplaySceneSetup] Full multi-zone world loaded.");
            }
            else
            {
                // Legacy single-overlay mode.
                if (!string.IsNullOrEmpty(overlayFile) && _gridBuilder != null)
                {
                    World.OverlayLoader.LoadOverlay(overlayFile, _gridBuilder);
                    Debug.Log($"[GameplaySceneSetup] Single overlay '{overlayFile}' loaded.");
                }
            }
        }

        /// <summary>
        /// Generate the procedural dungeon south of the lobby and paint onto the world tilemap.
        /// Reads zone offsets from the database loader. Skipped if no config is assigned.
        /// </summary>
        private void GenerateDungeon(World.ZoneDatabaseLoader dbLoader)
        {
            if (!_generateBspDungeon)
            {
                Debug.Log("[GameplaySceneSetup] BSP dungeon generation skipped (generateBspDungeon = false).");
                return;
            }

            if (_dungeonConfig == null)
            {
                // Create a runtime config with default values matching Python constants
                _dungeonConfig = ScriptableObject.CreateInstance<DungeonGeneratorConfig>();
                Debug.Log("[GameplaySceneSetup] No DungeonGeneratorConfig assigned — using default runtime config.");
            }

            if (_gridBuilder == null)
            {
                Debug.LogError("[GameplaySceneSetup] WorldGridBuilder not available for dungeon generation.");
                return;
            }

            // Find lobby and dungeon zone offsets from database
            int lobbyOffX = 50, lobbyOffY = 50; // defaults matching zones_database.json
            int dungeonOffX = 50, dungeonOffY = 100;
            int zoneHeight = 50;

            if (dbLoader != null && dbLoader.Entries != null)
            {
                zoneHeight = dbLoader.ZoneHeightTiles;
                foreach (var entry in dbLoader.Entries)
                {
                    if (string.Equals(entry.name, "Lobby", System.StringComparison.OrdinalIgnoreCase))
                    {
                        lobbyOffX = entry.offsetX;
                        lobbyOffY = entry.offsetY;
                    }
                    else if (string.Equals(entry.name, "dungeon", System.StringComparison.OrdinalIgnoreCase))
                    {
                        dungeonOffX = entry.offsetX;
                        dungeonOffY = entry.offsetY;
                    }
                }
            }

            // Route through the IDungeonStrategy abstraction. The BSP path is the
            // legacy default; UdemyDungeonStrategy plugs in here for "Dungeon v1".
            // Registering on every call is a no-op after the first thanks to
            // last-write-wins; this keeps the strategy's _dungeonConfig fresh.
            var bspStrategy = new BspDungeonStrategy(_dungeonConfig);
            DungeonStrategyResolver.Register(bspStrategy);

            var ctx = new DungeonGenerationContext
            {
                GridBuilder = _gridBuilder,
                DungeonOffsetX = dungeonOffX,
                DungeonOffsetY = dungeonOffY,
                LobbyOffsetX = lobbyOffX,
                LobbyOffsetY = lobbyOffY,
                ZoneHeight = zoneHeight,
                Seed = _dungeonSeed,
                SceneContainer = GetSceneContainer("[World]"),
            };

            var strategy = DungeonStrategyResolver.Resolve(BspDungeonStrategy.StrategyId);
            strategy.TryGenerate(ctx, out _);
        }

        // One-shot installer for the slot-watcher MonoBehaviour. Idempotent —
        // re-finds itself instead of duplicating across hot-reloads. Lives in
        // a top-level GameObject (not under [World]) so a future ClearWorld
        // call can't sweep it away mid-session.
        private void EnsureDungeonSlotBootstrap()
        {
            if (FindObjectOfType<World.Dungeon.Udemy.Bootstrap.DungeonSlotBootstrap>() != null) return;
            var go = new GameObject("[DungeonSlotBootstrap]");
            go.AddComponent<World.Dungeon.Udemy.Bootstrap.DungeonSlotBootstrap>();
            Debug.Log("[GameplaySceneSetup] DungeonSlotBootstrap installed.");
        }

        /// <summary>
        /// Phase 2.6 wiring: when the active world is procedural, build the
        /// chunk-streaming pipeline (biome → provider → painter → streamer)
        /// and follow the just-spawned player. No-op for hand-crafted worlds.
        ///
        /// This step lives after <see cref="SpawnPlayer"/> in the bootstrap
        /// because the streamer needs a focus <see cref="Transform"/>, and
        /// the player is the natural focus for single-player builds.
        /// </summary>
        private void EnsureProceduralChunkStreamer()
        {
            if (initialWorld == null || !initialWorld.UseChunkStreaming) return;
            if (initialWorld.Config == null)
            {
                Debug.LogError($"[GameplaySceneSetup] Cannot wire chunk streamer: " +
                               $"WorldDescriptor '{initialWorld.Slug}' has no WorldConfig.");
                return;
            }

            Tilemap groundTilemap = _gridBuilder != null
                ? _gridBuilder.GetTilemap(World.TilemapLayerSetup.TilemapLayer.Ground)
                : null;
            if (groundTilemap == null)
            {
                Debug.LogError("[GameplaySceneSetup] Cannot wire chunk streamer: " +
                               "Ground tilemap not found on WorldGridBuilder.");
                return;
            }

            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] Procedural streaming enabled but no " +
                                 "player exists yet — streamer will be created without a focus " +
                                 "and will sit idle until configured manually.");
            }

            ProceduralWorldFactory.ProceduralWorld streamed;
            try
            {
                streamed = ProceduralWorldFactory.Build(initialWorld);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameplaySceneSetup] ProceduralWorldFactory failed for " +
                               $"'{initialWorld.Slug}': {ex.Message}");
                return;
            }

            var resolver = TileRegistryNameResolver.Build(streamed.Tiles);
            var painter  = new TilemapChunkPainter(new Tilemap[] { groundTilemap }, resolver, streamed.ChunkSize);

            var streamerGo = new GameObject("ChunkStreamer");
            streamerGo.transform.SetParent(GetSceneContainer("[World]"), false);
            var streamer = streamerGo.AddComponent<ChunkStreamerBehaviour>();
            streamer.Configure(
                streamed.Provider,
                painter,
                activeRadius: initialWorld.ActiveRadius,
                chunkSize:    streamed.ChunkSize,
                worldId:      initialWorld.Id,
                focus:        player != null ? player.transform : null);

            Debug.Log($"[GameplaySceneSetup] ChunkStreamer wired for world '{initialWorld.Slug}' " +
                      $"(biome={initialWorld.BiomeKind}, radius={initialWorld.ActiveRadius}, " +
                      $"chunkSize={streamed.ChunkSize}).");
        }

    }
}
