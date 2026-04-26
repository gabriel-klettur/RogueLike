using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Spawners;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.NPC;
using Valkur.Infrastructure;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        private void EnsureTileEditor()
        {
            if (TileEditorManager.Instance != null) return;
            var editorGo = new GameObject("TileEditorManager");
            var manager = editorGo.AddComponent<TileEditorManager>();
            editorGo.transform.SetParent(GetSceneContainer("[Editors]"), false);
            manager.SetGridBuilder(_gridBuilder);
            Debug.Log("[GameplaySceneSetup] TileEditorManager created. Press F6 to toggle.");
        }

        private void EnsureMapEditor()
        {
            if (MapEditorManager.Instance != null) return;
            var editorGo = new GameObject("MapEditorManager");
            editorGo.AddComponent<MapEditorManager>();
            editorGo.transform.SetParent(GetSceneContainer("[Editors]"), false);
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
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] Light2D type not found — URP 2D Renderer may not be installed.");
                return;
            }

            if (FindObjectOfType(light2DType) != null) return;

            var lightGo = new GameObject("GlobalLight2D");
            var light = lightGo.AddComponent(light2DType);

            if (light == null)
            {
                Debug.LogError("[GameplaySceneSetup] Failed to AddComponent Light2D.");
                Destroy(lightGo);
                return;
            }

            lightGo.transform.SetParent(GetSceneContainer("[Camera]"), false);

            bool lightTypeSet = false;
            var lightTypeProp = light2DType.GetProperty("lightType",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (lightTypeProp != null)
            {
                try
                {
                    var enumType = lightTypeProp.PropertyType;
                    var globalValue = System.Enum.ToObject(enumType, 1);
                    lightTypeProp.SetValue(light, globalValue);
                    lightTypeSet = true;
                    Debug.Log($"[GameplaySceneSetup] Light2D.lightType set to Global via property.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GameplaySceneSetup] Failed to set lightType via property: {ex.Message}");
                }
            }

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
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[GameplaySceneSetup] Failed to set m_LightType via field: {ex.Message}");
                    }
                }
            }

            if (!lightTypeSet)
                Debug.LogWarning("[GameplaySceneSetup] Could not set Light2D to Global type.");

            var intensityProp = light2DType.GetProperty("intensity",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (intensityProp != null)
            {
                try { intensityProp.SetValue(light, 1f); }
                catch (System.Exception ex) { Debug.LogWarning($"[GameplaySceneSetup] Failed to set intensity: {ex.Message}"); }
            }

            Debug.Log($"[GameplaySceneSetup] Global Light 2D created. lightTypeSet={lightTypeSet}");
        }

        private void EnsureSaveService()
        {
            if (SaveService.HasInstance) return;
            var go = new GameObject("SaveService");
            go.AddComponent<SaveService>();
            go.transform.SetParent(GetSceneContainer("[Core]"), false);
            Debug.Log("[GameplaySceneSetup] SaveService created.");
        }

        private void EnsureSaveLoadInput()
        {
            if (FindObjectOfType<SaveLoadInputHandler>() != null) return;
            var go = new GameObject("SaveLoadInputHandler");
            go.AddComponent<SaveLoadInputHandler>();
            go.transform.SetParent(GetSceneContainer("[Core]"), false);
            Debug.Log("[GameplaySceneSetup] SaveLoadInputHandler created (F5/F9).");
        }

        private void EnsureVFXManager()
        {
            bool created = VFXManager.Instance == null;
            if (created)
            {
                var vfxGo = new GameObject("VFXManager");
                vfxGo.AddComponent<VFXManager>();
                vfxGo.transform.SetParent(GetSceneContainer("[VFX]"), false);
            }

            if (_particlePresetCatalog != null)
                VFXManager.Instance.SetParticleCatalog(_particlePresetCatalog);
        }

        private void EnsureParticleInstancesLoader()
        {
            if (_particlePresetCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No ParticlePresetCatalog assigned — ambient world particles skipped.");
                return;
            }

            if (FindObjectOfType<ParticleInstancesLoader>() != null) return;

            var loaderGo = new GameObject("ParticleInstancesLoader");
            var loader = loaderGo.AddComponent<ParticleInstancesLoader>();
            loaderGo.transform.SetParent(GetSceneContainer("[VFX]"), false);
            loader.Initialize(_particlePresetCatalog);
            Debug.Log("[GameplaySceneSetup] ParticleInstancesLoader created.");
        }

        private void EnsureNPCSeparation()
        {
            if (FindObjectOfType<World.NPCSeparationSystem>() != null) return;
            var go = new GameObject("NPCSeparationSystem");
            go.AddComponent<World.NPCSeparationSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] NPCSeparationSystem created.");
        }

        private void EnsureVendorShopUI()
        {
            if (VendorShopUI.Instance != null) return;
            var go = new GameObject("VendorShopUI");
            go.AddComponent<VendorShopUI>();
            go.transform.SetParent(GetSceneContainer("[UI]"), false);
            Debug.Log("[GameplaySceneSetup] VendorShopUI created.");
        }

        private void EnsureDevConsole()
        {
            if (DevConsole.Instance != null) return;
            var go = new GameObject("DevConsole");
            go.AddComponent<DevConsole>();
            go.transform.SetParent(GetSceneContainer("[Debug]"), false);
            Debug.Log("[GameplaySceneSetup] DevConsole created (` or F4 to toggle).");
        }

        private void EnsureChatSystem()
        {
            if (FindObjectOfType<ChatSystem>() != null) return;
            var go = new GameObject("ChatSystem");
            go.AddComponent<ChatSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] ChatSystem created.");

            if (FindObjectOfType<ChatUI>() == null)
            {
                var uiGo = new GameObject("ChatUI");
                uiGo.AddComponent<ChatUI>();
                uiGo.transform.SetParent(GetSceneContainer("[UI]"), false);
                Debug.Log("[GameplaySceneSetup] ChatUI created.");
            }
        }

        private void EnsureVendorEconomyService()
        {
            if (VendorEconomyService.Instance != null) return;
            var go = new GameObject("VendorEconomyService");
            go.AddComponent<VendorEconomyService>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] VendorEconomyService created.");
        }

        private void EnsureWorldLightLoader()
        {
            if (FindObjectOfType<World.WorldLightLoader>() != null) return;

            if (_lightPresetCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No LightPresetCatalog assigned — ambient world lights skipped.");
                return;
            }

            var go = new GameObject("WorldLightLoader");
            var loader = go.AddComponent<World.WorldLightLoader>();
            go.transform.SetParent(GetSceneContainer("[World]"), false);
            loader.SetCatalog(_lightPresetCatalog);
            Debug.Log("[GameplaySceneSetup] WorldLightLoader created.");
        }

        private void EnsureBuildingCollisionLoader()
        {
            if (FindObjectOfType<World.BuildingCollisionLoader>() != null) return;
            var go = new GameObject("BuildingCollisionLoader");
            go.AddComponent<World.BuildingCollisionLoader>();
            go.transform.SetParent(GetSceneContainer("[World]"), false);
            Debug.Log("[GameplaySceneSetup] BuildingCollisionLoader created.");
        }

    }
}