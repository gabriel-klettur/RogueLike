using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

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
        // Subdir + filename now owned by JsonFileSpawnerInstanceRepository.

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

        // Repository handle. Tests inject an InMemorySpawnerInstanceRepository
        // through SetRepository(); production paths fall back to the JSON
        // file backend on first use so existing scenes need no rewiring.
        private ISpawnerInstanceRepository _repository;

        public void SetRepository(ISpawnerInstanceRepository repository) => _repository = repository;

        private ISpawnerInstanceRepository ResolveRepository()
            => _repository ?? (_repository = new JsonFileSpawnerInstanceRepository());

        // ── Programmatic setup ──────────────────────────────────────────────────────

        /// <summary>
        /// Wire references from code (e.g. GameplaySceneSetup) and disable auto-load
        /// so the caller can invoke <see cref="LoadInstances"/> at the right time.
        /// </summary>
        public void Initialize(SpawnerTemplateCatalog catalog, MonsterSpawner monsterSpawner)
        {
            _catalog         = catalog;
            _monsterSpawner  = monsterSpawner;
            _autoLoad        = false;
        }

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

            string json = ResolveRepository().ReadRawJson(WorldId.Base);
            if (json == null)
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] No instances file in repository for {WorldId.Base}.");
                return;
            }

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
            // Every SpawnerInstance in the scene, not only the ones this loader created.
            //
            // There are exactly two creators: this loader, which tracks what it makes in
            // _instances, and the F3 editor, which builds spawners directly and never
            // registers them here. SpawnerEditorManager persists by FindObjectsOfType, so the
            // editor's spawners DO reach the file — and clearing only the tracked set left
            // them alive across a reload while the file recreated them, so the map doubled on
            // every reloadworld / map switch. Autosave then wrote the doubled set back, which
            // is why one id ended up in the file five times.
            //
            // Save and clear have to agree on the same set. FindObjectsOfType is what save
            // uses, so it is what clear uses.
            foreach (var si in FindObjectsOfType<SpawnerInstance>())
            {
                if (si != null)
                    Valkur.Core.SafeDestroy.Of(si.gameObject);
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

            // Shared with the F3 editor's save, so the round trip cannot drift. See
            // SpawnerTileMapping for what happened when only this side did the conversion.
            Vector2 world = SpawnerTileMapping.TileToWorld(
                tileCol, tileRow, zoneDef.gridOffset, _zoneManager.ZoneHeightTiles);

            // Create SpawnerInstance GO
            var go = new GameObject($"Spawner_{instanceId}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = new Vector3(world.x, world.y, 0f);

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
