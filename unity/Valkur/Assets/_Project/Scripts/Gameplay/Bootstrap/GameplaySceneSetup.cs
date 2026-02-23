using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;
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
            EnsureMapEditor();
            EnsureSaveLoadInput();
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

        private void EnsureMapEditor()
        {
            if (MapEditorManager.Instance != null) return;
            var editorGo = new GameObject("MapEditorManager");
            editorGo.AddComponent<MapEditorManager>();
            Debug.Log("[GameplaySceneSetup] MapEditorManager created. Press F7 to toggle.");
        }

        /// <summary>
        /// URP uses Sprite-Lit-Default material on TilemapRenderers.
        /// Without a 2D light source, all tilemaps render as solid black.
        /// This creates a Global Light 2D to illuminate the entire scene.
        /// Uses reflection to avoid hard dependency on URP assembly.
        /// 
        /// Defense in depth: tries multiple reflection strategies for lightType,
        /// logs every step, and WorldGridBuilder applies Unlit fallback if this fails.
        /// </summary>
        private void EnsureGlobalLight2D()
        {
            // Find Light2D type via reflection (avoids URP assembly reference)
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] Light2D type not found — URP 2D Renderer may not be installed. Tilemaps will use Unlit fallback.");
                return;
            }

            Debug.Log("[GameplaySceneSetup] Light2D type found: " + light2DType.FullName);

            // Check if one already exists
            if (FindObjectOfType(light2DType) != null)
            {
                Debug.Log("[GameplaySceneSetup] Global Light 2D already exists in scene.");
                return;
            }

            var lightGo = new GameObject("GlobalLight2D");
            var light = lightGo.AddComponent(light2DType);

            if (light == null)
            {
                Debug.LogError("[GameplaySceneSetup] Failed to AddComponent Light2D.");
                Destroy(lightGo);
                return;
            }

            // --- Set lightType to Global ---
            // Strategy 1: public property "lightType" (URP 12.x-14.x)
            bool lightTypeSet = false;
            var lightTypeProp = light2DType.GetProperty("lightType",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (lightTypeProp != null)
            {
                try
                {
                    // The property type is Light2D.LightType enum — get the Global value (1)
                    var enumType = lightTypeProp.PropertyType;
                    var globalValue = System.Enum.ToObject(enumType, 1); // 1 = Global
                    lightTypeProp.SetValue(light, globalValue);
                    lightTypeSet = true;
                    Debug.Log($"[GameplaySceneSetup] Light2D.lightType set to Global via property (enum={enumType.Name}, value={globalValue})");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GameplaySceneSetup] Failed to set lightType via property: {ex.Message}");
                }
            }

            // Strategy 2: serialized field "m_LightType" (fallback for different URP versions)
            if (!lightTypeSet)
            {
                var lightTypeField = light2DType.GetField("m_LightType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (lightTypeField != null)
                {
                    try
                    {
                        var enumType = lightTypeField.FieldType;
                        var globalValue = System.Enum.ToObject(enumType, 1);
                        lightTypeField.SetValue(light, globalValue);
                        lightTypeSet = true;
                        Debug.Log($"[GameplaySceneSetup] Light2D.m_LightType set to Global via field (enum={enumType.Name}, value={globalValue})");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[GameplaySceneSetup] Failed to set m_LightType via field: {ex.Message}");
                    }
                }
            }

            if (!lightTypeSet)
            {
                Debug.LogWarning("[GameplaySceneSetup] Could not set Light2D to Global type — neither property nor field found. Light may be Freeform (local only).");
            }

            // --- Set intensity ---
            var intensityProp = light2DType.GetProperty("intensity",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (intensityProp != null)
            {
                try
                {
                    intensityProp.SetValue(light, 1f);
                    Debug.Log("[GameplaySceneSetup] Light2D.intensity set to 1.0");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GameplaySceneSetup] Failed to set intensity: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] Light2D.intensity property not found.");
            }

            // --- Verify ---
            Debug.Log($"[GameplaySceneSetup] Global Light 2D created. GameObject='{lightGo.name}', Component={light.GetType().Name}, lightTypeSet={lightTypeSet}");
        }

        private void EnsureSaveLoadInput()
        {
            if (FindObjectOfType<SaveLoadInputHandler>() != null) return;
            var go = new GameObject("SaveLoadInputHandler");
            go.AddComponent<SaveLoadInputHandler>();
            Debug.Log("[GameplaySceneSetup] SaveLoadInputHandler created (F5/F9).");
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

            var selectedDef = ResolveSelectedPlayerDefinition();
            if (selectedDef != null)
            {
                EntitySetup.ConfigurePlayer(playerGo, selectedDef);
            }
            else if (defaultPlayerDef != null)
            {
                EntitySetup.ConfigurePlayer(playerGo, defaultPlayerDef);
            }
            else
            {
                Debug.LogWarning("[GameplaySceneSetup] No player definition available for spawned player.");
            }

        }

        private PlayerDefinition ResolveSelectedPlayerDefinition()
        {
            if (!PlayerSelectionState.HasExplicitSelection)
                return defaultPlayerDef;

            string selectedKey = PlayerSelectionState.SelectedPlayerKey;
            var selectedRuntimeDef = PlayerClassCatalog.CreateRuntimeDefinition(selectedKey);
            if (selectedRuntimeDef == null)
            {
                Debug.LogWarning($"[GameplaySceneSetup] Selected player class '{selectedKey}' not found in runtime catalog. Falling back to default player definition.");
                return defaultPlayerDef;
            }

            return selectedRuntimeDef;
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
