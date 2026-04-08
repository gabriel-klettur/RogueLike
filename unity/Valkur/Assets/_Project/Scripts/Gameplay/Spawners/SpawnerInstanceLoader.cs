using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Loads spawner instances from StreamingAssets/Spawners/spawners_instances.json,
    /// resolves templates from a SpawnerTemplateCatalog, and spawns SpawnerInstance
    /// GameObjects into the scene.
    ///
    /// Maps to Python's load of spawners_instances.json + spawners_templates.json.
    /// </summary>
    public class SpawnerInstanceLoader : MonoBehaviour
    {
        private const float PPU = 32f;
        private const string STREAMING_SUBFOLDER = "Spawners";
        private const string INSTANCES_FILE = "spawners_instances.json";

        [Header("References")]
        [Tooltip("Catalog of all SpawnerTemplateData SOs.")]
        [SerializeField] private SpawnerTemplateCatalog _catalog;

        [Tooltip("ZoneManager for coordinate conversion.")]
        [SerializeField] private World.ZoneManager _zoneManager;

        [Tooltip("MonsterSpawner to queue spawn requests into.")]
        [SerializeField] private MonsterSpawner _monsterSpawner;

        [Header("Settings")]
        [SerializeField] private bool _autoLoad = true;

        private readonly List<SpawnerInstance> _instances = new List<SpawnerInstance>();
        public IReadOnlyList<SpawnerInstance> Instances => _instances;

        private void Start()
        {
            if (_autoLoad)
                LoadInstances();
        }

        public void LoadInstances()
        {
            ClearInstances();

            if (_catalog == null)
            {
                Debug.LogError("[SpawnerInstanceLoader] SpawnerTemplateCatalog not assigned.", this);
                return;
            }

            if (_zoneManager == null)
            {
                _zoneManager = FindObjectOfType<World.ZoneManager>();
                if (_zoneManager == null)
                {
                    Debug.LogError("[SpawnerInstanceLoader] ZoneManager not found.", this);
                    return;
                }
            }

            string jsonPath = Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILE);
            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] Instances file not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var rawList = MiniJsonRuntime.Deserialize(json) as List<object>;
            if (rawList == null)
            {
                Debug.LogError("[SpawnerInstanceLoader] Failed to parse instances JSON.");
                return;
            }

            int loaded = 0;
            foreach (var item in rawList)
            {
                if (item is Dictionary<string, object> dict)
                {
                    if (TryCreateInstance(dict))
                        loaded++;
                }
            }

            Debug.Log($"[SpawnerInstanceLoader] Loaded {loaded}/{rawList.Count} spawner instances.");
        }

        public void ClearInstances()
        {
            foreach (var si in _instances)
            {
                if (si != null)
                    Destroy(si.gameObject);
            }
            _instances.Clear();
        }

        private bool TryCreateInstance(Dictionary<string, object> dict)
        {
            string templateId = GetString(dict, "template_id");
            string zone = GetString(dict, "zone", "Lobby");
            string instanceId = GetString(dict, "id");

            var template = _catalog != null ? _catalog.GetById(templateId) : null;
            if (template == null)
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] Template '{templateId}' not found (instance '{instanceId}').");
                return false;
            }

            if (!_zoneManager.TryGetZone(zone, out var zoneDef))
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] Zone '{zone}' not registered (instance '{instanceId}').");
                return false;
            }

            // Tile coords → world position
            int tileCol = 0, tileRow = 0;
            if (dict.TryGetValue("tile", out var tileObj) && tileObj is List<object> tileList && tileList.Count >= 2)
            {
                tileCol = Convert.ToInt32(tileList[0]);
                tileRow = Convert.ToInt32(tileList[1]);
            }

            int zoneH = _zoneManager.ZoneHeightTiles;
            float worldX = zoneDef.gridOffset.x + tileCol;
            float worldY = zoneDef.gridOffset.y + (zoneH - 1) - tileRow;

            // Create SpawnerInstance GO
            var go = new GameObject($"Spawner_{instanceId}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = new Vector3(worldX, worldY, 0f);

            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(template, instanceId, zone, _monsterSpawner);

            // Parse per-instance overrides
            if (dict.TryGetValue("overrides", out var ovObj) && ovObj is Dictionary<string, object> overrides)
            {
                si.ApplyOverrides(overrides);
            }

            _instances.Add(si);
            return true;
        }

        private static string GetString(Dictionary<string, object> d, string key, string fallback = "")
        {
            if (d.TryGetValue(key, out var v) && v is string s)
                return s;
            return fallback;
        }
    }
}
