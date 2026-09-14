using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Loads spawner instances from the active map slot's <c>spawners_instances.json</c>,
    /// resolves presets from a <see cref="SpawnerTemplateCatalog"/>, and spawns
    /// <see cref="SpawnerInstance"/> GameObjects into the scene.
    ///
    /// <para>It also owns the <b>v1 → v2 migration</b>: a row with no <c>config</c> block is
    /// frozen against its preset as it loads and the file is written back once. See
    /// <see cref="MigrateIfNeeded"/> for why the write-back is not optional.</para>
    /// </summary>
    public class SpawnerInstanceLoader : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Catalog of all SpawnerTemplateData presets.")]
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
                // A custom map slot with no spawners is a normal state (a fresh map, a generated
                // world); only the base world is expected to ship this file. Warning on the
                // steady state trains the reader to scroll past the console.
                if (Valkur.Core.MapEditorActiveSlot.IsDefault(Valkur.Core.MapEditorActiveSlot.Read()))
                    Debug.LogWarning($"[SpawnerInstanceLoader] No instances file in repository for {WorldId.Base}.");
                else
                    Debug.Log("[SpawnerInstanceLoader] Active map slot has no spawners file (empty map).");
                return;
            }

            var records = SpawnerInstanceSerializer.ParseAll(json);
            if (records == null)
            {
                Debug.LogError("[SpawnerInstanceLoader] Failed to parse instances JSON.");
                return;
            }

            // BEFORE spawning, so a frozen row and the object built from it cannot disagree.
            bool migrated = SpawnerInstanceSerializer.Freeze(records, _catalog);

            int loaded = 0;
            foreach (var record in records)
            {
                if (TryCreateInstance(record))
                    loaded++;
            }

            Debug.Log($"[SpawnerInstanceLoader] Loaded {loaded}/{records.Count} spawner instances.");

            if (migrated) MigrateIfNeeded(records);
        }

        /// <summary>
        /// Writes the migrated records back, once.
        ///
        /// <para><b>Why the write is mandatory.</b> Freezing in memory alone lasts one
        /// session, so retuning a preset and restarting would re-snapshot every un-migrated
        /// placement from the new values — the very coupling copy-on-place removes, coming
        /// back in through the file. The Particles loader carries the identical note.</para>
        ///
        /// <para>It emits every record that was READ, including the ones
        /// <see cref="TryCreateInstance"/> refused, so a placement whose zone is not
        /// registered survives a migration instead of being quietly dropped. That is also why
        /// it needs no anti-wipe guard: it cannot write fewer rows than it read.</para>
        ///
        /// <para>Editor + Play Mode only. In a build StreamingAssets is read-only on several
        /// platforms; in EditMode the test runner would replace shipped data with whatever a
        /// fixture had loaded — the same pollution guard the editor's save carries. Authors on
        /// other machines migrate the first time they press Play; shipped data is migrated
        /// deterministically by <c>Valkur &gt; Spawners &gt; Migrate Instances To v2</c>.</para>
        /// </summary>
        private void MigrateIfNeeded(IReadOnlyList<SpawnerInstanceRecord> records)
        {
            if (!Application.isEditor || !Application.isPlaying) return;

            try
            {
                ResolveRepository().WriteRawJson(WorldId.Base,
                                                 SpawnerInstanceSerializer.Serialize(records));
                Debug.Log($"[SpawnerInstanceLoader] Migrated {records.Count} spawner record(s) " +
                          $"to schema v{SpawnerInstanceSerializer.SchemaVersion} — each placement " +
                          "now owns its own configuration.");
            }
            catch (System.Exception e)
            {
                // A failed migration is not a failed load: the records are frozen in memory
                // and this session behaves correctly either way.
                Debug.LogWarning($"[SpawnerInstanceLoader] Could not write the v2 migration " +
                                 $"({e.Message}). The spawners loaded normally; the file stays v1.");
            }
        }

        public void ClearInstances()
        {
            // Every SpawnerInstance in the scene, not only the ones this loader created.
            //
            // There are exactly two creators: this loader, which tracks what it makes in
            // _instances, and the Spawner editor, which builds spawners directly and never
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
                    SafeDestroy.Of(si.gameObject);
            }
            _instances.Clear();
        }

        private bool TryCreateInstance(SpawnerInstanceRecord record)
        {
            if (record == null) return false;

            var preset = _catalog != null ? _catalog.GetById(record.TemplateId) : null;

            // A missing preset is only fatal when the row has no config of its own. Once a
            // placement owns its configuration it no longer needs the asset it came from —
            // which is the whole point of copy-on-place, and it means deleting a preset from
            // the catalogue can no longer silently unplace every spawner made from it.
            if (preset == null && record.Config == null)
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] Preset '{record.TemplateId}' not found " +
                                 $"and instance '{record.InstanceId}' carries no config of its own.");
                return false;
            }

            if (!_zoneManager.TryGetZone(record.Zone, out var zoneDef))
            {
                Debug.LogWarning($"[SpawnerInstanceLoader] Zone '{record.Zone}' not registered (instance '{record.InstanceId}').");
                return false;
            }

            // Shared with the editor's save, so the round trip cannot drift. See
            // SpawnerTileMapping for what happened when only this side did the conversion.
            Vector2 world = SpawnerTileMapping.TileToWorld(
                record.Tile.x, record.Tile.y, zoneDef.gridOffset, _zoneManager.ZoneHeightTiles);

            var go = new GameObject($"Spawner_{record.InstanceId}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = new Vector3(world.x, world.y, 0f);

            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(preset, record.Config, record.InstanceId, record.Zone, _monsterSpawner);

            _instances.Add(si);
            return true;
        }
    }
}
