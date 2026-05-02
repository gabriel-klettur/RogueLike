using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads light instances from StreamingAssets/Lights/light_instances.json
    /// and spawns URP 2D Point Light for each one.
    ///
    /// Maps to Python's light_instances_service.load_light_instances()
    /// + rendering/lighting system that creates point lights from presets.
    ///
    /// Uses reflection for Light2D to avoid hard dependency on URP assembly
    /// (same pattern as GameplaySceneSetup.EnsureGlobalLight2D).
    ///
    /// Flicker is simulated via intensity oscillation in Update().
    /// </summary>
    public class WorldLightLoader : MonoBehaviour
    {
        // Subdir + filename now owned by JsonFileLightInstanceRepository; constants
        // kept here as reference / search anchor only.
        private const float PX_TO_WORLD = 1f / 16f; // Python uses 16 px per world unit for lights

        [Header("References")]
        [SerializeField, Tooltip("Catalog of light presets. Populate via 'Valkur > Lighting > Import Presets'.")]
        private LightPresetCatalog _catalog;

        /// <summary>Set the catalog from code (used by GameplaySceneSetup).</summary>
        public void SetCatalog(LightPresetCatalog catalog) => _catalog = catalog;

        [SerializeField, Tooltip("ZoneManager for resolving zone offsets.")]
        private ZoneManager _zoneManager;

        [SerializeField, Tooltip("Optional WorldConfig override. When set, its ChunkSize is the " +
                                  "fallback for zone height when no ZoneManager is available. " +
                                  "Phase 0 wiring; will become required once chunk streaming lands.")]
        private WorldConfig _worldConfig;

        [Header("Settings")]
        [SerializeField, Tooltip("Parent transform for spawned lights. Null = this transform.")]
        private Transform _lightsRoot;

        [Header("Performance")]
        [SerializeField, Tooltip("Disable Light2D GameObjects far outside the camera viewport. URP 2D lights are expensive — big FPS win when many off-screen lights exist.")]
        private bool _enableViewportCulling = true;

        [SerializeField, Tooltip("World-unit margin beyond the camera frustum where lights stay active.")]
        private float _cullMarginWorldUnits = 8f;

        [SerializeField, Tooltip("Seconds between culling checks.")]
        private float _cullCheckInterval = 0.2f;

        private float _nextCullCheck;
        private Camera _cullCamera;

        // Reflection cache for URP Light2D
        private Type _light2DType;
        private PropertyInfo _intensityProp;
        private PropertyInfo _colorProp;
        private PropertyInfo _outerRadiusProp;
        private PropertyInfo _innerRadiusProp;
        private PropertyInfo _falloffProp;
        private PropertyInfo _lightTypeProp;
        private bool _reflectionResolved;

        // Active light instances for flicker
        private readonly List<LightInstance> _activeLights = new List<LightInstance>();

        private struct LightInstance
        {
            public Component light2D;
            public float baseIntensity;
            public float flickerAmp;
            public float flickerSpeed;
            public float flickerOffset;
        }

        private void Start()
        {
            ResolveLightReflection();
            LoadInstances();
        }

        private void Update()
        {
            if (_enableViewportCulling) CullLightsByViewport();

            if (!_reflectionResolved || _intensityProp == null) return;

            float time = Time.time;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.light2D == null || inst.flickerAmp <= 0f) continue;
                if (!inst.light2D.gameObject.activeInHierarchy) continue;

                float flicker = 1f + Mathf.Sin((time + inst.flickerOffset) * inst.flickerSpeed * Mathf.PI * 2f) * inst.flickerAmp;
                try { _intensityProp.SetValue(inst.light2D, inst.baseIntensity * flicker); }
                catch { /* ignore reflection failures */ }
            }
        }

        private void CullLightsByViewport()
        {
            if (Time.unscaledTime < _nextCullCheck) return;
            _nextCullCheck = Time.unscaledTime + _cullCheckInterval;

            if (_cullCamera == null) _cullCamera = Camera.main;
            if (_cullCamera == null) return;

            float halfH = _cullCamera.orthographicSize + _cullMarginWorldUnits;
            float halfW = halfH * _cullCamera.aspect;
            Vector3 cp = _cullCamera.transform.position;

            for (int i = 0; i < _activeLights.Count; i++)
            {
                var c = _activeLights[i].light2D;
                if (c == null) continue;
                Vector3 p = c.transform.position;
                bool inView = Mathf.Abs(p.x - cp.x) <= halfW && Mathf.Abs(p.y - cp.y) <= halfH;
                if (c.gameObject.activeSelf != inView) c.gameObject.SetActive(inView);
            }
        }

        private void ResolveLightReflection()
        {
            _light2DType = Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");

            if (_light2DType == null)
            {
                Debug.LogWarning("[WorldLightLoader] Light2D type not found — URP 2D may not be installed.");
                return;
            }

            var flags = BindingFlags.Public | BindingFlags.Instance;
            _intensityProp = _light2DType.GetProperty("intensity", flags);
            _colorProp = _light2DType.GetProperty("color", flags);
            _lightTypeProp = _light2DType.GetProperty("lightType", flags);

            // URP 2D Light2D uses pointLightOuterRadius / pointLightInnerRadius
            _outerRadiusProp = _light2DType.GetProperty("pointLightOuterRadius", flags);
            _innerRadiusProp = _light2DType.GetProperty("pointLightInnerRadius", flags);
            _falloffProp = _light2DType.GetProperty("falloffIntensity", flags);

            _reflectionResolved = true;
        }

        // Repository handle. Tests inject an InMemoryLightInstanceRepository
        // through SetRepository(); production paths fall back to the JSON
        // file backend on first use so no scene wiring is required to
        // preserve the existing boot flow.
        private ILightInstanceRepository _repository;

        public void SetRepository(ILightInstanceRepository repository) => _repository = repository;

        private ILightInstanceRepository ResolveRepository()
            => _repository ?? (_repository = new JsonFileLightInstanceRepository());

        private void LoadInstances()
        {
            if (_light2DType == null || _catalog == null) return;

            var repo = ResolveRepository();
            string json = repo.ReadRawJson(WorldId.Base);
            if (json == null)
            {
                Debug.Log($"[WorldLightLoader] No light instances file in repository for {WorldId.Base}.");
                return;
            }

            var wrapper = JsonUtility.FromJson<LightInstanceArrayWrapper>("{\"items\":" + json + "}");

            if (wrapper?.items == null || wrapper.items.Length == 0)
            {
                Debug.Log("[WorldLightLoader] No light instances found in JSON.");
                return;
            }

            Transform root = _lightsRoot != null ? _lightsRoot : transform;
            int spawned = 0;

            foreach (var data in wrapper.items)
            {
                var preset = _catalog.GetByKey(data.preset_id);
                if (preset == null)
                {
                    Debug.LogWarning($"[WorldLightLoader] Unknown preset '{data.preset_id}' for light id={data.id}. Skipping.");
                    continue;
                }

                Vector2 worldPos = ComputeWorldPosition(data);
                var lightGo = new GameObject($"Light_{data.id}_{data.preset_id}");
                lightGo.transform.SetParent(root);
                lightGo.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

                var light2D = lightGo.AddComponent(_light2DType);
                ConfigureLight(light2D, preset, data);
                spawned++;

                _activeLights.Add(new LightInstance
                {
                    light2D = light2D,
                    baseIntensity = GetOverrideFloat(data.overrides, "intensity", preset.intensity),
                    flickerAmp = GetOverrideFloat(data.overrides, "flicker_amp", preset.flickerAmplitude),
                    flickerSpeed = GetOverrideFloat(data.overrides, "flicker_speed", preset.flickerSpeed),
                    flickerOffset = UnityEngine.Random.Range(0f, 10f),
                });
            }

            Debug.Log($"[WorldLightLoader] Spawned {spawned} point lights from {wrapper.items.Length} instances.");
        }

        private void ConfigureLight(Component light2D, LightPresetDefinition preset, LightInstanceData data)
        {
            // Set light type to Point (enum value 2)
            if (_lightTypeProp != null)
            {
                try
                {
                    var enumType = _lightTypeProp.PropertyType;
                    _lightTypeProp.SetValue(light2D, Enum.ToObject(enumType, 2));
                }
                catch { /* fallback */ }
            }

            float intensity = GetOverrideFloat(data.overrides, "intensity", preset.intensity);
            if (_intensityProp != null) try { _intensityProp.SetValue(light2D, intensity); } catch { }

            Color color = GetOverrideColor(data.overrides, "color", preset.color);
            if (_colorProp != null) try { _colorProp.SetValue(light2D, color); } catch { }

            float radius = GetOverrideFloat(data.overrides, "radius", preset.radius);
            float worldRadius = radius * PX_TO_WORLD;
            if (_outerRadiusProp != null) try { _outerRadiusProp.SetValue(light2D, worldRadius); } catch { }
            if (_innerRadiusProp != null) try { _innerRadiusProp.SetValue(light2D, worldRadius * preset.centerScale); } catch { }
            if (_falloffProp != null) try { _falloffProp.SetValue(light2D, preset.falloff); } catch { }
        }

        private Vector2 ComputeWorldPosition(LightInstanceData data)
        {
            Vector2 zoneOffset = Vector2.zero;
            // Fallback chain for chunk side length: live ZoneManager → injected
            // WorldConfig → the documented legacy default. Avoids the magic
            // literal 50 living in two places (here and the loader's defaults).
            float zoneHeight = _worldConfig != null
                ? _worldConfig.ChunkSize
                : WorldConfig.LegacyChunkSize;

            if (_zoneManager != null && _zoneManager.TryGetZone(data.zone, out var zoneDef))
            {
                zoneOffset = new Vector2(zoneDef.gridOffset.x, zoneDef.gridOffset.y);
                zoneHeight = _zoneManager.ZoneHeightTiles;
            }

            // Python stores rel_x, rel_y as pixel offsets (Y-down)
            // Convert to Unity world coords (Y-up):
            float worldX = zoneOffset.x + data.rel_x / 32f;
            float worldY = zoneOffset.y + (zoneHeight - 1f) - data.rel_y / 32f;
            return new Vector2(worldX, worldY);
        }

        private static float GetOverrideFloat(LightOverrides overrides, string key, float fallback)
        {
            if (overrides == null) return fallback;
            switch (key)
            {
                case "intensity": return overrides.intensity > 0 ? overrides.intensity : fallback;
                case "radius":    return overrides.radius > 0 ? overrides.radius : fallback;
                case "falloff":   return overrides.falloff > 0 ? overrides.falloff : fallback;
                case "flicker_amp":   return overrides.flicker_amp >= 0 ? overrides.flicker_amp : fallback;
                case "flicker_speed": return overrides.flicker_speed > 0 ? overrides.flicker_speed : fallback;
                default:          return fallback;
            }
        }

        private static Color GetOverrideColor(LightOverrides overrides, string key, Color fallback)
        {
            if (overrides == null || overrides.color == null || overrides.color.Length < 3)
                return fallback;
            return new Color(overrides.color[0] / 255f, overrides.color[1] / 255f, overrides.color[2] / 255f, 1f);
        }

        // ── JSON DTOs ──

        [Serializable]
        private class LightInstanceArrayWrapper
        {
            public LightInstanceData[] items;
        }

        [Serializable]
        private class LightInstanceData
        {
            public int id;
            public string preset_id;
            public string zone;
            public float rel_x;
            public float rel_y;
            public LightOverrides overrides;
        }

        [Serializable]
        private class LightOverrides
        {
            public float intensity = -1f;
            public float radius = -1f;
            public float falloff = -1f;
            public float flicker_amp = -1f;
            public float flicker_speed = -1f;
            public float[] color;
        }
    }
}
