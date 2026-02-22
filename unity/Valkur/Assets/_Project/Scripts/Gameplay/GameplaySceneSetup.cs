using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Rendering;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Sets up the MainGameplay scene at runtime.
    /// Builds the world grid, spawns the player, camera, HUD, and test monsters.
    /// Temporary bootstrap until full spawner system is ported.
    /// </summary>
    public class GameplaySceneSetup : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerDefinition defaultPlayerDef;
        [SerializeField] private GameObject playerPrefab;

        [Header("Monsters (test)")]
        [SerializeField] private MonsterDefinition testMonsterDef;
        [SerializeField] private GameObject monsterPrefab;
        [SerializeField] private int testMonsterCount = 3;
        [SerializeField] private float spawnRadius = 5f;

        private WorldGridBuilder _gridBuilder;

        private void Start()
        {
            BuildWorldGrid();
            EnsureGlobalLight2D();
            EnsureVFXManager();
            EnsureTileEditor();
            SpawnPlayer();
            SpawnTestMonsters();
        }

        private void BuildWorldGrid()
        {
            var gridGo = new GameObject("WorldGridBuilder");
            _gridBuilder = gridGo.AddComponent<WorldGridBuilder>();
        }

        private void EnsureTileEditor()
        {
            if (TileEditorManager.Instance != null) return;
            var editorGo = new GameObject("TileEditorManager");
            var manager = editorGo.AddComponent<TileEditorManager>();
            manager.SetGridBuilder(_gridBuilder);
            Debug.Log("[GameplaySceneSetup] TileEditorManager created. Press F6 to toggle.");
        }

        /// <summary>
        /// URP uses Sprite-Lit-Default material on TilemapRenderers.
        /// Without a 2D light source, all tilemaps render as solid black.
        /// This creates a Global Light 2D to illuminate the entire scene.
        /// Uses reflection to avoid hard dependency on URP assembly.
        /// </summary>
        private void EnsureGlobalLight2D()
        {
            // Find Light2D type via reflection (avoids URP assembly reference)
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] Light2D type not found. URP 2D Renderer package may not be installed.");
                return;
            }

            // Check if one already exists
            if (FindObjectOfType(light2DType) != null) return;

            var lightGo = new GameObject("GlobalLight2D");
            var light = lightGo.AddComponent(light2DType);

            // Set lightType to Global (enum value 1) via reflection
            var lightTypeProp = light2DType.GetProperty("lightType");
            if (lightTypeProp != null)
                lightTypeProp.SetValue(light, 1); // 1 = Global

            // Set intensity
            var intensityProp = light2DType.GetProperty("intensity");
            if (intensityProp != null)
                intensityProp.SetValue(light, 1f);

            Debug.Log("[GameplaySceneSetup] Created Global Light 2D for URP tilemap rendering.");
        }

        private void EnsureVFXManager()
        {
            if (VFXManager.Instance != null) return;
            var vfxGo = new GameObject("VFXManager");
            vfxGo.AddComponent<VFXManager>();
        }

        private void SpawnPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No player prefab assigned.");
                return;
            }

            var playerGo = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            playerGo.tag = "Player";

            if (defaultPlayerDef != null)
                EntitySetup.ConfigurePlayer(playerGo, defaultPlayerDef);

        }

        private void SpawnTestMonsters()
        {
            if (monsterPrefab == null || testMonsterDef == null) return;

            for (int i = 0; i < testMonsterCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnRadius;
                Vector3 pos = new Vector3(offset.x, offset.y, 0f);
                var monsterGo = Instantiate(monsterPrefab, pos, Quaternion.identity);
                EntitySetup.ConfigureMonster(monsterGo, testMonsterDef);
            }
        }
    }
}
