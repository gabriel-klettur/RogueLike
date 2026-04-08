using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Reads particles_instances.json from StreamingAssets/Particles/ and spawns
    /// ParticleEmitter GameObjects at the correct world positions.
    ///
    /// Maps to Python's spawn_particles_from_instances() in particles_loader.py.
    ///
    /// Coordinate conversion from Python pixel-space to Unity world-space:
    ///   world_x = zoneGridOffset.x * tileSize + rel_x / pixelsPerUnit
    ///   world_y = zoneGridOffset.y * tileSize - rel_y / pixelsPerUnit   (Y-flip)
    ///
    /// Attach to any persistent scene GameObject (e.g. GameplaySceneSetup).
    /// </summary>
    public class ParticleInstancesLoader : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField, Tooltip("Catalog of particle presets. Populated by ParticlePresetImporter.")]
        private ParticlePresetCatalog _catalog;

        [SerializeField, Tooltip("JSON file name inside StreamingAssets/Particles/. Python: particles_instances.json.")]
        private string _instancesFileName = "particles_instances.json";

        [Header("Coordinate Conversion")]
        [SerializeField, Tooltip("Pixels per Unity world unit. Must match TILE_PPU in ValkurAssetPostprocessor (32).")]
        private float _pixelsPerUnit = 32f;

        [SerializeField, Tooltip("World units per tile. Must match WorldGridBuilder cellSize.")]
        private float _tileSize = 1f;

        [SerializeField, Tooltip("Flip the Y axis when converting from Pygame (Y-down) to Unity (Y-up).")]
        private bool _flipY = true;

        [Header("Hierarchy")]
        [SerializeField, Tooltip("Parent transform for spawned emitters. Created automatically if null.")]
        private Transform _emittersParent;

        private readonly List<GameObject> _spawnedEmitters = new List<GameObject>();

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            if (_emittersParent == null)
            {
                _emittersParent = new GameObject("ParticleEmitters").transform;
                _emittersParent.SetParent(transform, false);
            }

            LoadAndSpawn();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Set the catalog and optional file name programmatically before Start() runs.
        /// Used when the loader is added at runtime by GameplaySceneSetup.
        /// </summary>
        public void Initialize(ParticlePresetCatalog catalog, string instancesFileName = null)
        {
            _catalog = catalog;
            if (!string.IsNullOrEmpty(instancesFileName))
                _instancesFileName = instancesFileName;
        }

        /// <summary>
        /// Destroy all previously spawned emitters and reload from JSON.
        /// Call after editing the instances file.
        /// </summary>
        public void Reload()
        {
            ClearAll();
            LoadAndSpawn();
        }

        /// <summary>
        /// Destroy all spawned particle emitters.
        /// Matches Python's clear_runtime_particle_entities(world).
        /// </summary>
        public void ClearAll()
        {
            foreach (var go in _spawnedEmitters)
            {
                if (go != null) Destroy(go);
            }
            _spawnedEmitters.Clear();
        }

        // ------------------------------------------------------------------ internal

        private void LoadAndSpawn()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Particles", _instancesFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ParticleInstancesLoader] instances file not found: {path}");
                return;
            }

            string json = File.ReadAllText(path);
            var instances = ParseInstancesJson(json);
            if (instances == null || instances.Count == 0)
            {
                Debug.Log("[ParticleInstancesLoader] No particle instances to spawn.");
                return;
            }

            // Build zone offset table from ZoneManager
            var zoneOffsets = BuildZoneOffsets();

            int spawned = 0;
            foreach (var inst in instances)
            {
                if (string.IsNullOrEmpty(inst.preset_id)) continue;

                var preset = _catalog != null ? _catalog.GetById(inst.preset_id) : null;
                if (preset == null)
                {
                    Debug.LogWarning($"[ParticleInstancesLoader] Preset not found in catalog: '{inst.preset_id}'");
                    continue;
                }

                Vector2 worldPos = ComputeWorldPos(inst, zoneOffsets);
                SpawnEmitter(preset, worldPos, inst);
                spawned++;
            }

            Debug.Log($"[ParticleInstancesLoader] Spawned {spawned} particle emitters.");
        }

        private Vector2 ComputeWorldPos(ParticleInstanceData inst, Dictionary<string, Vector2Int> zoneOffsets)
        {
            Vector2Int offset = Vector2Int.zero;
            if (!string.IsNullOrEmpty(inst.zone))
                zoneOffsets.TryGetValue(inst.zone, out offset);

            float wx = offset.x * _tileSize + inst.rel_x / _pixelsPerUnit;
            float wy = _flipY
                ? offset.y * _tileSize - inst.rel_y / _pixelsPerUnit
                : offset.y * _tileSize + inst.rel_y / _pixelsPerUnit;
            return new Vector2(wx, wy);
        }

        private void SpawnEmitter(ParticlePresetDefinition preset, Vector2 worldPos, ParticleInstanceData inst)
        {
            float scaleMultiplier = inst.scale_multiplier > 0f
                ? inst.scale_multiplier
                : (inst.preset_id.StartsWith("portal_", StringComparison.Ordinal) ? 2f : 1f);

            var go = new GameObject($"PE_{preset.id}_{inst.id}");
            go.transform.SetParent(_emittersParent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scaleMultiplier);

            _spawnedEmitters.Add(go);
        }

        // ------------------------------------------------------------------ zone helpers

        private Dictionary<string, Vector2Int> BuildZoneOffsets()
        {
            var result = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);

            var zm = FindZoneManager();
            if (zm == null) return result;

            foreach (var zone in zm.GetZonesSnapshot())
            {
                result[zone.zoneName] = zone.gridOffset;
            }
            return result;
        }

        private static ZoneManager FindZoneManager()
        {
            try
            {
                return UnityEngine.Object.FindObjectOfType<ZoneManager>();
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ JSON parsing

        private static List<ParticleInstanceData> ParseInstancesJson(string json)
        {
            try
            {
                // The JSON is an array of objects — JsonUtility doesn't support bare arrays,
                // so we use the same MiniJsonRuntime already in the project (from OverlayLoader).
                var parsed = MiniJsonRuntime.Deserialize(json);
                if (parsed is not List<object> list) return null;

                var result = new List<ParticleInstanceData>(list.Count);
                foreach (var item in list)
                {
                    if (item is not Dictionary<string, object> d) continue;
                    var inst = new ParticleInstanceData
                    {
                        id = GetInt(d, "id"),
                        preset_id = GetString(d, "preset_id"),
                        zone = GetString(d, "zone"),
                        rel_x = GetInt(d, "rel_x"),
                        rel_y = GetInt(d, "rel_y"),
                        scale_multiplier = GetFloat(d, "scale_multiplier", 1f)
                    };
                    result.Add(inst);
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ParticleInstancesLoader] Failed to parse instances JSON: {ex.Message}");
                return null;
            }
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return def;
        }

        private static string GetString(Dictionary<string, object> d, string key, string def = "")
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return v.ToString();
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
            {
                try { return Convert.ToSingle(v); } catch { }
            }
            return def;
        }
    }

    // ------------------------------------------------------------------ data model

    /// <summary>
    /// Plain data for one entry in particles_instances.json.
    /// Maps to Python's instances dict entries in particles_loader.py.
    /// </summary>
    [Serializable]
    public class ParticleInstanceData
    {
        /// <summary>Stable numeric id. Python: id.</summary>
        public int id;

        /// <summary>Preset key. Python: preset_id.</summary>
        public string preset_id;

        /// <summary>Zone name string linking to ZoneManager. Python: zone.</summary>
        public string zone;

        /// <summary>X pixel offset from zone origin. Python: rel_x.</summary>
        public int rel_x;

        /// <summary>Y pixel offset from zone origin (Pygame Y-down). Python: rel_y.</summary>
        public int rel_y;

        /// <summary>Optional visual scale multiplier. Python: scale_multiplier.</summary>
        public float scale_multiplier = 1f;
    }
}
