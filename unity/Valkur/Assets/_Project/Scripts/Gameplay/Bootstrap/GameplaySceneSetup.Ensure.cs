using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
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
            Debug.Log("[GameplaySceneSetup] ZoneManager created.");
        }

        private void BuildWorldGrid()
        {
            var gridGo = new GameObject("WorldGridBuilder");
            _gridBuilder = gridGo.AddComponent<World.WorldGridBuilder>();
        }

        /// <summary>
        /// Load the world map. When loadFullWorld is true, uses ZoneDatabaseLoader + WorldLoader
        /// to paint all 24 zones at their correct offsets. Otherwise loads a single overlay.
        /// After world load, generates the procedural dungeon south of the lobby.
        /// </summary>
        private void LoadWorld()
        {
            if (loadFullWorld)
            {
                // 1) Load zone database → populates ZoneManager with all zones
                var dbLoaderGo = new GameObject("ZoneDatabaseLoader");
                var dbLoader = dbLoaderGo.AddComponent<World.ZoneDatabaseLoader>();
                // Call manually (Start() won't fire until next frame)
                dbLoader.LoadDatabase();

                // 2) Load full world overlays + collision grids at zone offsets
                var worldLoaderGo = new GameObject("WorldLoader");
                var worldLoader = worldLoaderGo.AddComponent<World.WorldLoader>();
                worldLoader.LoadFullWorld();

                // 3) Generate procedural dungeon at runtime
                GenerateDungeon(dbLoader);

                Debug.Log("[GameplaySceneSetup] Full multi-zone world loaded.");
            }
            else
            {
                // Legacy single-overlay mode
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

            var loaderGo = new GameObject("DungeonLoader");
            var dungeonLoader = loaderGo.AddComponent<World.DungeonLoader>();
            dungeonLoader.SetConfig(_dungeonConfig);
            dungeonLoader.GenerateAndPaint(
                _gridBuilder,
                dungeonOffX, dungeonOffY,
                lobbyOffX, lobbyOffY,
                zoneHeight,
                _dungeonSeed);
        }

    }
}
