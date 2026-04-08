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

    }
}
