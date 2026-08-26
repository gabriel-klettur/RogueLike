using System.Collections.Generic;
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
    ///   world_y = zoneGridOffset.y + (zoneHeightTiles - 1) - rel_y / pixelsPerUnit   (Y-flip)
    ///
    /// Attach to any persistent scene GameObject (e.g. GameplaySceneSetup).
    /// </summary>
    public partial class ParticleInstancesLoader : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField, Tooltip("Catalog of particle presets. Populated by ParticlePresetImporter.")]
        private ParticlePresetCatalog _catalog;

        [SerializeField, Tooltip("JSON file name inside StreamingAssets/Particles/. Python: particles_instances.json.")]
        private string _instancesFileName = "particles_instances.json";

        [Header("Coordinate Conversion")]
        // Inspector knob retained for designer reference / future manual rescaling;
        // the live conversion uses _tileSize because PPU is already baked into the
        // imported sprites via ValkurAssetPostprocessor. Kept serialized so prefab
        // overrides survive across editor sessions.
        #pragma warning disable CS0414
        [SerializeField, Tooltip("Pixels per Unity world unit. Must match TILE_PPU in ValkurAssetPostprocessor (32). Reference value only — runtime conversion uses _tileSize.")]
        private float _pixelsPerUnit = 32f;
        #pragma warning restore CS0414

        [SerializeField, Tooltip("World units per tile. Must match WorldGridBuilder cellSize.")]
        private float _tileSize = 1f;

        [SerializeField, Tooltip("Flip the Y axis when converting from Pygame (Y-down) to Unity (Y-up).")]
        private bool _flipY = true;

        [Header("Hierarchy")]
        [SerializeField, Tooltip("Parent transform for spawned emitters. Created automatically if null.")]
        private Transform _emittersParent;

        [Header("Performance")]
        [SerializeField, Tooltip("Disable particle emitters whose world position is far outside the camera viewport. Big FPS win when revealing distant zones (Tile Editor pan, fast travel).")]
        private bool _enableViewportCulling = true;

        [SerializeField, Tooltip("World-unit margin beyond the camera frustum where emitters stay active. Lower = more aggressive culling, higher = safer but more cost.")]
        private float _cullMarginWorldUnits = 12f;

        [SerializeField, Tooltip("Seconds between culling checks. Cheap, can stay at 0.2s.")]
        private float _cullCheckInterval = 0.2f;

        private readonly List<GameObject> _spawnedEmitters = new List<GameObject>();
        private float _nextCullCheck;
        private Camera _cullCamera;

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

        private void LateUpdate()
        {
            if (!_enableViewportCulling) return;
            if (Time.unscaledTime < _nextCullCheck) return;
            _nextCullCheck = Time.unscaledTime + _cullCheckInterval;

            if (_cullCamera == null) _cullCamera = Camera.main;
            if (_cullCamera == null) return;

            float halfH = _cullCamera.orthographicSize + _cullMarginWorldUnits;
            float halfW = halfH * _cullCamera.aspect;
            Vector3 cp = _cullCamera.transform.position;

            for (int i = 0; i < _spawnedEmitters.Count; i++)
            {
                var go = _spawnedEmitters[i];
                if (go == null) continue;
                Vector3 p = go.transform.position;
                bool inView = Mathf.Abs(p.x - cp.x) <= halfW && Mathf.Abs(p.y - cp.y) <= halfH;
                if (go.activeSelf != inView) go.SetActive(inView);
            }
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
            // Every persisted emitter in the scene, not only the ones this loader spawned.
            //
            // ParticlesRuntimeEditor saves by FindObjectsOfType<PersistedParticleInstance>, so
            // emitters placed with F1 DO reach the file — but the editor never adds them to
            // _spawnedEmitters, so clearing the tracked list left them alive across a reload
            // while the file recreated them alongside. Every map switch doubled them, and the
            // Particles editor saves automatically, so the doubling was written back rather
            // than vanishing at the next restart.
            //
            // Identical to the spawner defect fixed in SpawnerInstanceLoader; the marker
            // component exists precisely so both sides can agree on the set, and only the save
            // side was using it. See .github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md.
            foreach (var identity in FindObjectsOfType<PersistedParticleInstance>(includeInactive: true))
            {
                if (identity != null) Valkur.Core.SafeDestroy.Of(identity.gameObject);
            }
            _spawnedEmitters.Clear();
        }

        // ------------------------------------------------------------------ internal

        // InstanceStore is settable for tests; defaults to file-based in Start().
        private IParticleInstanceStore _instanceStore;

        /// <summary>
        /// Injects a custom store (e.g. <see cref="InMemoryParticleInstanceStore"/> for tests).
        /// Must be called before <c>Start()</c> or <c>Reload()</c>.
        /// </summary>
        public void SetInstanceStore(IParticleInstanceStore store)
        {
            _instanceStore = store;
        }

        private void LoadAndSpawn()
        {
            if (_instanceStore == null)
                _instanceStore = new FileParticleInstanceStore(_instancesFileName);

            string json = _instanceStore.Load();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"[ParticleInstancesLoader] instances file not found or empty.");
                return;
            }

            var zm = FindZoneManager();
            int zoneHeightTiles = zm != null ? zm.ZoneHeightTiles : 50;

            var instances = ParticleInstanceSerializer.Deserialize(json, zm, zoneHeightTiles, _tileSize, _flipY);
            if (instances == null || instances.Count == 0)
            {
                Debug.Log("[ParticleInstancesLoader] No particle instances to spawn.");
                return;
            }

            int spawned = 0;
            int migrated = 0;
            foreach (var record in instances)
            {
                bool needsSnapshot = record.Config == null;
                if (string.IsNullOrEmpty(record.PresetId)) continue;

                var preset = _catalog != null ? _catalog.GetById(record.PresetId) : null;
                if (preset == null)
                {
                    Debug.LogWarning($"[ParticleInstancesLoader] Preset not found in catalog: '{record.PresetId}'");
                    continue;
                }

                // Skip finite (one-shot) presets — they cannot function as persistent map decorations.
                if (preset.vfx != null && !preset.vfx.loops)
                {
                    Debug.LogWarning($"[ParticleInstancesLoader] Skipping finite preset '{record.PresetId}' (loops=false). Remove from JSON to suppress this warning.");
                    continue;
                }

                SpawnEmitter(preset, record.WorldPos, record);
                spawned++;
                if (needsSnapshot && record.Config != null) migrated++;
            }

            Debug.Log($"[ParticleInstancesLoader] Spawned {spawned} particle emitters.");

            // Emitters whose preset names a light get one as soon as the light loader exists.
            if (isActiveAndEnabled) StartCoroutine(AttachEmittedLights());

            if (migrated > 0) PersistMigratedConfigs(instances, migrated);
        }

        /// <summary>
        /// Writes the copy-on-place migration back to the file, once, the first time a pre-v4
        /// file is loaded.
        ///
        /// WHY IT HAS TO REACH DISK. Freezing a placement onto a copy of its preset is only a
        /// promise while it lives in RAM: an author who retunes the asset and restarts would
        /// find every un-migrated placement wearing the new values, because the loader would
        /// snapshot them again from the edited preset. One write closes it permanently.
        ///
        /// It is safe in a way the editor's save is not, and does not need the anti-wipe guard
        /// that one carries: it writes the records it just READ — every one of them, including
        /// those it could not spawn — with no scene scan and no coordinate maths. The output
        /// differs from the input by the added <c>config</c> objects and nothing else.
        ///
        /// Editor only. A player build has no author to protect, cannot write StreamingAssets,
        /// and would attempt this on every single boot. Whether an EDITOR write is allowed right
        /// now is the store's decision, not this method's: FileParticleInstanceStore already
        /// refuses EditMode writes to the real path, and a test's in-memory store accepts them,
        /// which is what lets this path be tested at all.
        /// </summary>
        private void PersistMigratedConfigs(List<ParticleInstanceRecord> records, int migrated)
        {
            if (!Application.isEditor) return;

            try
            {
                _instanceStore.Save(ParticleInstanceSerializer.SerializeRecords(records));
                Debug.Log($"[ParticleInstancesLoader] Froze {migrated} placement(s) onto their " +
                          "own configuration (copy-on-place migration, written once).");
            }
            catch (System.Exception ex)
            {
                // A failed migration is not a failed load: the instances are already spawned
                // and correct, and the next run simply tries again.
                Debug.LogWarning($"[ParticleInstancesLoader] Could not persist the copy-on-place " +
                                 $"migration: {ex.Message}");
            }
        }

    }
}