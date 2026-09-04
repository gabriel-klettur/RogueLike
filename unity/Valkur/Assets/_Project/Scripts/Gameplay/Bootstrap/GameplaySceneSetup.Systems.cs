using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
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
        /// <summary>
        /// Where the LLM settings asset lives. A SUBFOLDER, like every other Resources.Load
        /// in this project — an empty path is a full-tree scan of every asset under
        /// Resources/ and logs a missing-script error for each one it cannot resolve.
        /// </summary>
        private const string CHAT_LLM_SETTINGS_RESOURCE_PATH = "Chat/ChatLlmSettings";

        private void EnsureTileEditor()
        {
            if (TileEditorManager.Instance != null) return;
            var editorGo = new GameObject("TileEditorManager");
            var manager = editorGo.AddComponent<TileEditorManager>();
            editorGo.transform.SetParent(GetSceneContainer("[Editors]"), false);
            manager.SetGridBuilder(_gridBuilder);
            Debug.Log("[GameplaySceneSetup] TileEditorManager created. Press F8 to toggle.");
        }

        private void EnsureMapEditor()
        {
            if (MapEditorManager.Instance != null) return;
            var editorGo = new GameObject("MapEditorManager");
            editorGo.AddComponent<MapEditorManager>();
            editorGo.transform.SetParent(GetSceneContainer("[Editors]"), false);
            Debug.Log("[GameplaySceneSetup] MapEditorManager created. Press F11 to toggle.");
        }

        /// <summary>
        /// Sorting layers the ambient (day/night) light is allowed to darken.
        ///
        /// In: every layer that carries world surface or its inhabitants.
        /// Out, on purpose:
        ///   • Projectiles / VFX — emissive by art direction; particles answer to the
        ///     cycle through <c>ParticleEmitter.AmbientLight</c>, which keeps its own floor.
        ///   • UI_World / Overlay — health bars, facing arrows and editor rulers must stay
        ///     readable at midnight.
        ///
        /// Overhead IS in: it carries the OverheadDetails tilemap (canopies and the like),
        /// and a tree crown that stays noon-bright over a night-blue floor is the exact
        /// artefact this mask exists to avoid.
        ///
        /// A LIT renderer on a layer that is NOT in this mask renders BLACK, so this list
        /// and the set of layers converted to Sprite-Lit-Default must move together.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of sorting-layer names, built once from string literals. Holds no Unity objects and is never mutated after init, so it cannot go stale across a Play session.")]
        private static readonly string[] AmbientLitSortingLayers =
        {
            "Default", "Background", "Ground", "FloorDecals", "ObjectsLow",
            "WallsBottom", "Entities", "Decorations", "WallsTop", "ObjectsHigh",
            "Overhead", "EntitiesOverhead"
        };

        /// <summary>
        /// Ensures the scene owns exactly one URP 2D <b>Global</b> Light2D, repairing the
        /// authored one when it has drifted.
        ///
        /// Typed against URP deliberately. <c>Valkur.Gameplay.asmdef</c> already references
        /// <c>Unity.RenderPipelines.Universal.Runtime</c>, so the old reflection path bought
        /// nothing and cost correctness: it wrote <c>Enum.ToObject(enumType, 1)</c> with the
        /// comment "1 = Global", but URP 14's enum is
        /// <c>Parametric=0, Freeform=1, Sprite=2, Point=3, Global=4</c> — the light came out
        /// Freeform, and the scene's authored one was left a Point light of radius 1. For
        /// months the day/night tint reached no pixel at all. Compile-time typing makes that
        /// class of bug impossible; see <c>.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md</c>.
        /// </summary>
        private void EnsureGlobalLight2D()
        {
            var existing = FindObjectsOfType<Light2D>();

            Light2D global = null;
            foreach (var l in existing)
            {
                if (l.lightType == Light2D.LightType.Global) { global = l; break; }
            }

            // No Global light — but the scene may still carry the authored one in a broken
            // state. Adopt and repair it rather than adding a second light beside it, which
            // is what the old early-return effectively did (it saw *a* Light2D and gave up).
            if (global == null)
            {
                foreach (var l in existing)
                {
                    if (l.name.IndexOf("Global", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    global = l;
                    Debug.LogWarning(
                        $"[GameplaySceneSetup] '{l.name}' was authored as {l.lightType}; repairing it to Global.");
                    break;
                }
            }

            if (global == null)
            {
                var lightGo = new GameObject("Global Light 2D");
                lightGo.transform.SetParent(GetSceneContainer("[Camera]"), false);
                global = lightGo.AddComponent<Light2D>();
                Debug.Log("[GameplaySceneSetup] No Global Light 2D in scene — created one.");
            }

            global.lightType       = Light2D.LightType.Global;
            global.blendStyleIndex = 0;   // Multiply — this is the ambient darkening layer.
            global.color           = Color.white;
            global.intensity       = 1f;  // Neutral until DayNightCycle takes over in Update.
            ApplyAmbientSortingLayerMask(global);

            // Prime the shared material chooser before any world renderer asks, so nothing
            // races the probe and falls back to unlit for the rest of the session.
            Valkur.Core.Rendering.WorldSpriteMaterials.NotifyAmbientLightReady();
        }

        /// <summary>
        /// Writes <see cref="AmbientLitSortingLayers"/> onto the light's layer mask.
        ///
        /// URP exposes no public setter — <c>Light2D.m_ApplyToSortingLayers</c> is a private
        /// SerializeField — so this is the one reflection write left in the lighting path.
        /// It is safe to do at runtime: <c>Light2DManager.GetGlobalColor</c> re-reads the
        /// array through <c>IsLitLayer</c> on every frame, with no cache in between.
        ///
        /// Doing nothing is not an option: URP's <c>Awake</c> fills a null mask with EVERY
        /// sorting layer, which would darken the HUD-ish layers too, and the mask the scene
        /// shipped was frozen when only 8 of today's 16 layers existed.
        /// </summary>
        private static void ApplyAmbientSortingLayerMask(Light2D light)
        {
            var ids = new List<int>(AmbientLitSortingLayers.Length);
            foreach (var layerName in AmbientLitSortingLayers)
            {
                int id = SortingLayer.NameToID(layerName);
                if (SortingLayer.IsValid(id)) ids.Add(id);
                else Debug.LogWarning(
                    $"[GameplaySceneSetup] Sorting layer '{layerName}' does not exist — ambient light will skip it.");
            }

            var field = typeof(Light2D).GetField("m_ApplyToSortingLayers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
            {
                Debug.LogWarning(
                    "[GameplaySceneSetup] Light2D.m_ApplyToSortingLayers not found — ambient light keeps its authored mask.");
                return;
            }

            field.SetValue(light, ids.ToArray());
            Debug.Log($"[GameplaySceneSetup] Ambient light applies to {ids.Count} sorting layers.");
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

        /// <summary>
        /// Creates the A* service the chase states consume.
        ///
        /// <see cref="World.PathFinder"/> is a <c>SingletonMonoBehaviour</c> whose
        /// <c>Instance</c> is assigned in <c>Awake</c> and never self-creates, and no
        /// scene or prefab in the project referenced it — so <c>PathFinder.Instance</c>
        /// was null for the whole life of the game and both
        /// <c>ChaseState</c> and <c>AlertChaseState</c> permanently took their
        /// straight-line fallback. Every monster beelined into the first corner
        /// between it and the player.
        /// </summary>
        private void EnsurePathFinder()
        {
            if (FindObjectOfType<World.PathFinder>() != null) return;
            var go = new GameObject("PathFinder");
            go.AddComponent<World.PathFinder>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            Debug.Log("[GameplaySceneSetup] PathFinder created.");
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

            // BEFORE the ChatSystem, because its OnSingletonAwake resolves IChatProvider
            // from the ServiceLocator and falls back to offline when nothing is registered.
            // Registering afterwards would leave every conversation on the offline provider
            // for the life of the session, with nothing to show it had happened.
            EnsureChatProvider();

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

        /// <summary>
        /// Chooses who answers an NPC line, and registers it for <c>ChatSystem</c> to find.
        ///
        /// Always registers SOMETHING: with no settings asset, no key or no network, the
        /// offline provider answers from the persona's authored lines. The language model is
        /// an upgrade layered on top of a game that already talks, never a dependency.
        /// </summary>
        private void EnsureChatProvider()
        {
            if (ServiceLocator.TryGet<IChatProvider>(out _)) return;

            var offline = new OfflineDialogueProvider();
            var settings = Resources.Load<ChatLlmSettings>(CHAT_LLM_SETTINGS_RESOURCE_PATH);

            if (settings == null)
            {
                ServiceLocator.Register<IChatProvider>(offline);
                Debug.Log("[GameplaySceneSetup] Chat provider: offline (no ChatLlmSettings asset).");
                return;
            }

            var provider = new OpenAiChatProvider(settings, offline);
            ServiceLocator.Register<IChatProvider>(provider);

            // Says whether a key resolved, never what it resolved to.
            Debug.Log($"[GameplaySceneSetup] Chat provider: {provider.ProviderName} " +
                      $"(mode={settings.mode}, online={provider.IsOnline}).");
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