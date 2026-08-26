using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Loads light instances from <c>StreamingAssets/Lights/light_instances.json</c>
    /// and spawns one URP 2D Point Light per record.
    ///
    /// Maps to Python's <c>light_instances_service.load_light_instances()</c> +
    /// <c>roguelike_engine.rendering.lighting</c> point-light creation.
    ///
    /// Uses the typed URP API. The reflection this class used to carry was not free:
    /// it wrote the wrong <c>lightType</c> constant, so every placed light was a Sprite
    /// light with no cookie and drew nothing for months. See
    /// <c>.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md</c>.
    ///
    /// Runtime API (<see cref="RegisterRuntimeLight"/> / <see cref="RemoveLight"/> /
    /// <see cref="MoveLight"/> / <see cref="SaveAll"/>) lets the in-game Lighting Editor
    /// (Ctrl+F3) author lights live without re-importing the JSON.
    /// </summary>
    public class WorldLightLoader : MonoBehaviour
    {
        // Subdir + filename now owned by JsonFileLightInstanceRepository; constants
        // kept here as reference / search anchor only.
        private const float PX_TO_WORLD = 1f / 32f; // Buildings PPU=32; lights coords share the buildings grid.

        /// <summary>
        /// Blend style placed lights render into. Index 1 of the 2D Renderer's four styles
        /// is authored as Additive (see Assets/Settings/Renderer2D.asset); index 0 is the
        /// Multiply style the ambient day/night light owns. A torch belongs on the additive
        /// layer — it adds light to a dark world, it does not filter it.
        /// </summary>
        private const int PointLightBlendStyleIndex = 1;

        /// <summary>
        /// Blend style 0 is the Multiply style the day/night Global light writes into. A placed
        /// light put here does not compete with the ambient — the two accumulate into the same
        /// buffer, so the surface ends up scaled by (ambient + light) and keeps its own colour and
        /// texture instead of being painted over.
        /// </summary>
        private const int AmbientBlendStyleIndex = 0;

        public static WorldLightLoader Instance { get; private set; }

        // Domain Reload OFF — clear the static singleton handle on every
        // Play Mode entry so a leaked GameObject from a prior session
        // (e.g. a test fixture that didn't destroy its loader) cannot
        // pin Instance to a destroyed component. OnDestroy already covers
        // the normal path; this is the belt-and-braces cover.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
        }

        [Header("References")]
        [SerializeField, Tooltip("Catalog of light presets. Populate via 'Valkur > Lighting > Import Presets'.")]
        private LightPresetCatalog _catalog;

        /// <summary>Set the catalog from code (used by GameplaySceneSetup).</summary>
        public void SetCatalog(LightPresetCatalog catalog) => _catalog = catalog;

        /// <summary>Read-only access to the catalog (used by the runtime editor).</summary>
        public LightPresetCatalog Catalog => _catalog;

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

        // ── Runtime API surface ──────────────────────────────────────────────
        // Toggling this off disables every spawned point light unconditionally
        // (used while the day/night cycle is inside its lights-disable window).
        // Designers can also flip this from the Lighting Editor → "Point Lights".
        private bool _pointLightsEnabled = true;
        public bool PointLightsEnabled
        {
            get => _pointLightsEnabled;
            set
            {
                if (_pointLightsEnabled == value) return;
                _pointLightsEnabled = value;
                ApplyPointLightsVisibility();
            }
        }


        // Active light instances — backing store for flicker, save, runtime edits.
        private readonly List<LightInstance> _activeLights = new List<LightInstance>();

        /// <summary>
        /// Records read from disk that could NOT be spawned — an unknown preset key, typically.
        /// They are kept verbatim and re-emitted on save.
        ///
        /// Without this, renaming a preset key silently deletes every light using it: the records
        /// fail to spawn with a warning, and the next save re-serialises only what is live.
        /// ParticleInstanceSerializer already does the same thing for the same reason.
        /// </summary>
        private readonly List<LightInstanceData> _unspawnedRecords = new List<LightInstanceData>();

        /// <summary>
        /// Mutable record of a live light. Survives the Light2D component so we can
        /// re-serialise the whole list back into <c>light_instances.json</c> when the
        /// runtime editor calls <see cref="SaveAll"/>.
        /// </summary>
        private class LightInstance
        {
            public int       id;          // Stable id (1-based; preserved across save/load).
            public string    presetId;    // Catalog key.
            public string    zone;        // Zone id at author time (lobby / zone_x_y / "").
            public float     relX;        // Pixel offset within zone, X (Y-down to match Python).
            public float     relY;        // Pixel offset within zone, Y.
            public Color?    overrideColor;
            public float?    overrideIntensity;
            public float?    overrideRadius;
            /// <summary>
            /// Present because the schema has always had a "falloff" key and the loader has
            /// always parsed it — into a field nobody read, and that the live-light serialiser
            /// did not write. An authored falloff was therefore ignored at runtime AND deleted
            /// from the file by the next save.
            /// </summary>
            public float?    overrideFalloff;
            public float?    overrideFlickerAmp;
            public float?    overrideFlickerSpeed;

            public Light2D   light2D;     // Spawned Light2D (URP) — the body of the light.

            /// <summary>
            /// The additive core, on a child object, present only when the preset's surfaceMix is
            /// above 0. URP hardcodes a blend style to be purely multiplicative or purely additive
            /// (Light2DBlendStyle.blendFactors returns 1/0 or 0/1 and the BlendFactors struct beside
            /// it is unused), so a light that both illuminates a surface and glows over it has to be
            /// two Light2Ds.
            /// </summary>
            public Light2D   coreLight;

            /// <summary>
            /// The intensities the surface mix resolved to, before flicker. The flicker scales
            /// these rather than baseIntensity: writing baseIntensity straight onto the body would
            /// throw away the mix and the gain on the first animated frame, so the pool would jump
            /// to a different look one frame after it spawned.
            /// </summary>
            public float     bodyIntensity;
            public float     coreIntensity;
            public GameObject go;         // Owning GameObject.

            /// <summary>
            /// False for lights DERIVED from other world content — a lamp-post building that
            /// carries its own light, for instance. They take part in the day/night gate, the
            /// flicker and the viewport culling like any other, but they are rebuilt from their
            /// source on every load, so writing them into light_instances.json would duplicate
            /// them a little more on every save.
            /// </summary>
            public bool      persistent = true;

            // Effective per-frame flicker animation values.
            public float     baseIntensity;
            public float     flickerAmp;
            public float     flickerSpeed;
            public float     flickerOffset;
            public LightPresetDefinition.FlickerStyle flickerStyle;
        }

        public IReadOnlyList<GameObject> ActiveLightObjects
        {
            get
            {
                var list = new List<GameObject>(_activeLights.Count);
                foreach (var l in _activeLights) if (l.go != null) list.Add(l.go);
                return list;
            }
        }

        /// <summary>
        /// Every light currently spawned, INCLUDING the ones derived from light-fixture
        /// buildings. Good for a "what is on screen" readout; wrong for anything that reasons
        /// about the file, because <see cref="SaveAll"/> does not write derived lights.
        /// </summary>
        public int ActiveLightCount => _activeLights.Count;

        /// <summary>
        /// Only the lights that <see cref="SaveAll"/> would actually write — i.e. the authored
        /// ones. Anything guarding a save has to use THIS: a world whose authored lights failed
        /// to spawn but whose lamp-post buildings did has a non-zero ActiveLightCount and zero
        /// records to save, which is exactly how an empty array gets written over a good file.
        /// </summary>
        public int PersistentLightCount
        {
            get
            {
                int n = 0;
                foreach (var l in _activeLights) if (l.go != null && l.persistent) n++;
                return n + _unspawnedRecords.Count;
            }
        }

        /// <summary>
        /// The authored lights only — the ones an editor can move, delete and save. Derived
        /// lights are excluded because deleting one is meaningless: it comes back with its
        /// building on the next load.
        /// </summary>
        public IReadOnlyList<GameObject> PersistentLightObjects
        {
            get
            {
                var list = new List<GameObject>(_activeLights.Count);
                foreach (var l in _activeLights) if (l.go != null && l.persistent) list.Add(l.go);
                return list;
            }
        }

        /// <summary>
        /// Bumped every time this loader throws away its world and builds another one — a map-slot
        /// switch, a <c>reloadworld</c>, a Reload.
        ///
        /// Anything holding light ids across time has to watch it. The editor's undo history
        /// addresses lights by id, and an id only means something against the world it was
        /// recorded in; replay it against the next world and it edits whichever light happens to
        /// wear that number now. A counter is used rather than an event because there is no
        /// unsubscribe to get wrong, and because Domain Reload is off in this project — a static
        /// event that outlives a Play session is its own class of bug.
        /// </summary>
        public int WorldGeneration { get; private set; }

        /// <summary>Lights owned by a light-fixture building rather than by the light file.</summary>
        public int DerivedLightCount
        {
            get
            {
                int n = 0;
                foreach (var l in _activeLights) if (l.go != null && !l.persistent) n++;
                return n;
            }
        }

        /// <summary>
        /// Records read off disk that this session could not turn into a light — an unknown
        /// preset key, a zone that is not loaded. They are still written back verbatim, so the
        /// count is worth surfacing: it is the gap between what the panel lists and what a save
        /// will contain.
        /// </summary>
        public int UnspawnedRecordCount => _unspawnedRecords.Count;

        /// <summary>Find the live <see cref="LightInstance"/>-backed GameObject closest to <paramref name="worldPos"/> within <paramref name="maxRadius"/> world units, or null.</summary>
        public GameObject FindNearestLight(Vector3 worldPos, float maxRadius)
        {
            GameObject best = null;
            float bestSq = maxRadius * maxRadius;
            foreach (var inst in _activeLights)
            {
                if (inst.go == null) continue;
                float sq = (inst.go.transform.position - worldPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = inst.go; }
            }
            return best;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DayNightCycle.OnLightsEnabledChanged -= OnDayNightLightsToggle;
        }

        private void Start()
        {
            LoadInstances();
            // Subscribe AFTER initial load so the very first cycle pulse already
            // sees a populated _activeLights list.
            DayNightCycle.OnLightsEnabledChanged += OnDayNightLightsToggle;
            // Apply the initial visibility based on the current cycle state — DayNightCycle
            // may not have fired its event yet on first load.
            if (DayNightCycle.HasInstance && !DayNightCycle.Instance.LightsEnabledNow)
                _pointLightsEnabled = false;
            ApplyPointLightsVisibility();

            if (ShadowsInUse) StartCoroutine(AttachShadowCastersOnce());
        }

        /// <summary>
        /// Give every solid building a shadow caster, once, after the world has finished
        /// spawning. Deferred because BuildingLoader populates the world across several frames
        /// and a caster attached before the sprite exists would size itself from nothing.
        ///
        /// Only runs when a preset actually casts — see <see cref="ShadowsInUse"/>.
        /// </summary>
        private IEnumerator AttachShadowCastersOnce()
        {
            // Wait for the world to stop growing rather than for a fixed number of frames:
            // BuildingLoader spawns across several, and a two-frame wait caught 0 of 170
            // buildings. Poll rather than count frames so a slower load still lands.
            const float pollSeconds = 0.25f;
            const int   maxPolls    = 40;      // 10 s ceiling
            int previous = -1, stableFor = 0;
            for (int i = 0; i < maxPolls && stableFor < 2; i++)
            {
                yield return new WaitForSeconds(pollSeconds);
                int count = FindObjectsOfType<BuildingObject>().Length;
                if (count > 0 && count == previous) stableFor++;
                else { stableFor = 0; previous = count; }
            }

            int attached = 0;
            foreach (var building in FindObjectsOfType<BuildingObject>())
            {
                building.EnsureShadowCaster();
                attached++;
            }
            Debug.Log($"[WorldLightLoader] Shadow casters evaluated on {attached} building(s).");
        }

        private void Update()
        {
            if (_enableViewportCulling) CullLightsByViewport();

            if (!_pointLightsEnabled) return;

            float time = Time.time;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.light2D == null || inst.flickerAmp <= 0f) continue;
                if (!inst.light2D.gameObject.activeInHierarchy) continue;

                float factor = FlickerFactor(inst, time);
                inst.light2D.intensity = inst.bodyIntensity * factor;
                // The core breathes with the body. Driving only one of them would make the pool
                // change COLOUR as it flickers, because the two halves reach the frame through
                // different terms of the composite.
                if (inst.coreLight != null) inst.coreLight.intensity = inst.coreIntensity * factor;
            }
        }

        /// <summary>
        /// The wobble applied to a light's authored intensity this frame.
        ///
        /// Flame uses two octaves of Perlin noise rather than a sine. A sine is periodic, and the eye
        /// finds the period within a second or two — a torch driven by one reads as a pulsing bulb.
        /// Fire is aperiodic: a slow body with a faster flutter riding on it, which is what the two
        /// octaves are. The per-instance offset keeps neighbouring torches from breathing in unison.
        /// </summary>
        private static float FlickerFactor(LightInstance inst, float time)
        {
            switch (inst.flickerStyle)
            {
                case LightPresetDefinition.FlickerStyle.Steady:
                    return 1f;

                case LightPresetDefinition.FlickerStyle.Pulse:
                    return 1f + Mathf.Sin((time + inst.flickerOffset) * inst.flickerSpeed * Mathf.PI * 2f) * inst.flickerAmp;

                default:
                {
                    float t = (time + inst.flickerOffset) * inst.flickerSpeed;
                    // Perlin returns [0,1] centred near 0.5; remap to [-1,1] so the mean intensity
                    // stays the authored one instead of drifting brighter.
                    float body    = Mathf.PerlinNoise(t,          inst.flickerOffset)        * 2f - 1f;
                    float flutter = Mathf.PerlinNoise(t * 3.7f,   inst.flickerOffset + 17f)  * 2f - 1f;
                    return 1f + (body * 0.7f + flutter * 0.3f) * inst.flickerAmp;
                }
            }
        }

        private void OnDayNightLightsToggle(bool enabled) => PointLightsEnabled = enabled;

        private void ApplyPointLightsVisibility()
        {
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.go == null) continue;
                inst.go.SetActive(_pointLightsEnabled);
            }
        }

        private void CullLightsByViewport()
        {
            if (Time.unscaledTime < _nextCullCheck) return;
            _nextCullCheck = Time.unscaledTime + _cullCheckInterval;

            if (!_pointLightsEnabled) return;
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
            _unspawnedRecords.Clear();

            if (_catalog == null)
            {
                // Used to be a bare `return`. Zero authored lights and not one line in the console
                // to say why — and because SaveAll writes whatever is live, the next Ctrl+S in the
                // Lighting editor then wrote an empty array over the file.
                Debug.LogError("[WorldLightLoader] No LightPresetCatalog — NO authored lights were " +
                                "loaded. Saving from the Lighting editor in this state would write an " +
                                "empty file; the save guard will refuse it.");
                return;
            }

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

            NormaliseRecordIds(wrapper.items);

            int spawned = 0;
            foreach (var data in wrapper.items)
            {
                // Per-record, not per-file. A single throw used to abandon the rest of the loop,
                // so every record AFTER the bad one reached neither the scene nor the preserved
                // set — and the next save deleted them all. A record with no "zone" key did
                // exactly that: ZoneManager.TryGetZone hands a null straight to Dictionary
                // .TryGetValue, which throws ArgumentNullException.
                try
                {
                    if (SpawnFromData(data) != null) { spawned++; continue; }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WorldLightLoader] Light record id={data?.id} " +
                                    $"(preset '{data?.preset_id}', zone '{data?.zone}') threw " +
                                    $"{ex.GetType().Name} while spawning: {ex.Message}. It is kept " +
                                    "verbatim and will be written back unchanged.");
                }
                _unspawnedRecords.Add(data);   // kept verbatim so a save cannot delete it
            }

            if (_unspawnedRecords.Count > 0)
                Debug.LogWarning($"[WorldLightLoader] {_unspawnedRecords.Count} light record(s) could not " +
                                  "be spawned and will be preserved verbatim on save rather than dropped.");

            Debug.Log($"[WorldLightLoader] Spawned {spawned} point lights from {wrapper.items.Length} instances.");
        }

        // ── Runtime authoring API (used by LightingRuntimeEditor) ────────────

        /// <summary>
        /// Destroy every spawned light and re-read the instance file for the
        /// currently-active Map Editor slot. Called on slot switch so the scene
        /// mirrors the map the user just opened instead of keeping the previous
        /// map's lamps burning.
        ///
        /// Note this deliberately does NOT save first — the Map Editor flushes
        /// pending light edits to the OUTGOING slot before flipping the pointer.
        /// </summary>
        public void Reload()
        {
            ClearSpawnedLights();
            LoadInstances();
        }

        /// <summary>
        /// Drop the loader's entire in-memory view of the light file — the spawned lights AND the
        /// records it kept for verbatim re-emission — without touching disk.
        ///
        /// The second half matters on a map-slot switch. The preserved records belong to the
        /// OUTGOING slot's file; leaving them behind would make PersistentLightCount non-zero for
        /// a world with no lights in it, and a save against the incoming slot would carry the
        /// outgoing slot's records into it.
        /// </summary>
        public void ClearSpawnedLights()
        {
            for (int i = _activeLights.Count - 1; i >= 0; i--)
            {
                var inst = _activeLights[i];

                // A derived light is its building's, not this loader's. Destroying one here left
                // it gone for the whole session, because nothing re-registers it: BuildingObject
                // attaches its light once, when the building spawns. ReloadAllWorldContent
                // reloads the buildings FIRST and this loader second, so every lamp-post light in
                // the world was created and then immediately destroyed on every map-slot switch
                // and every `reloadworld`. Leave them alone — when their building goes, Unity
                // destroys them with it, and the null check below prunes the record.
                if (inst.persistent && inst.go != null) DestroyLightObject(inst.go);
                if (inst.persistent || inst.go == null) _activeLights.RemoveAt(i);
            }
            _unspawnedRecords.Clear();
            _nextLightId = 0;   // reseeded from the incoming world on first use
            WorldGeneration++;
        }

        /// <summary>
        /// Spawn a fresh runtime light at <paramref name="worldPos"/> using preset
        /// <paramref name="presetKey"/>. Returns null when the preset is missing or
        /// URP 2D is not installed.
        /// </summary>
        public GameObject RegisterRuntimeLight(string presetKey, Vector3 worldPos)
        {
            if (_catalog == null || _catalog.GetByKey(presetKey) == null)
            {
                Debug.LogWarning($"[WorldLightLoader] Cannot register runtime light — preset '{presetKey}' missing.");
                return null;
            }

            var data = new LightInstanceData
            {
                id        = NextLightId(),
                preset_id = presetKey,
                zone      = ResolveZoneAt(worldPos, out var rel),
                rel_x     = rel.x,
                rel_y     = rel.y,
                overrides = null,
            };
            return SpawnFromData(data, overridePosition: worldPos);
        }

        /// <summary>
        /// Spawn a light that belongs to another piece of world content — a lamp-post building,
        /// say — rather than to <c>light_instances.json</c>.
        ///
        /// It joins the same list as authored lights, so it inherits the day/night gate, the
        /// flicker and the viewport culling for free, but it is never saved because its source
        /// already describes it.
        ///
        /// The GameObject is parented to <paramref name="owner"/> so it follows the building
        /// when the F10 editor drags it, and counter-scaled so the owner's non-uniform sprite
        /// scale cannot stretch the light's radius into an ellipse.
        /// </summary>
        public GameObject RegisterDerivedLight(string presetKey, Vector3 worldPos, Transform owner)
        {
            var preset = _catalog?.GetByKey(presetKey);
            if (preset == null)
            {
                Debug.LogWarning($"[WorldLightLoader] Cannot derive light — preset '{presetKey}' missing from the catalog.");
                return null;
            }

            var go = new GameObject($"DerivedLight_{presetKey}");
            go.transform.SetParent(owner != null ? owner : (_lightsRoot != null ? _lightsRoot : transform));
            go.transform.position = worldPos;
            if (owner != null)
            {
                var owned = owner.lossyScale;
                go.transform.localScale = new Vector3(
                    Mathf.Approximately(owned.x, 0f) ? 1f : 1f / owned.x,
                    Mathf.Approximately(owned.y, 0f) ? 1f : 1f / owned.y,
                    1f);
            }

            var inst = new LightInstance
            {
                id            = 0,
                presetId      = presetKey,
                zone          = "",
                light2D       = go.AddComponent<Light2D>(),
                go            = go,
                persistent    = false,
                flickerOffset = UnityEngine.Random.Range(0f, 10f),
            };

            ApplyPresetToLight(inst, preset);
            if (!_pointLightsEnabled) go.SetActive(false);

            _activeLights.Add(inst);
            return go;
        }

        /// <summary>
        /// Split a light between illuminating the surface and glowing over it.
        ///
        /// URP's 2D shader composites as <c>albedo * modulate + additive</c>, so an additive light
        /// never touches the sprite underneath — it lays its colour on top, flat. That is why a
        /// torch pool erased the cobblestone: measured, the ground's relative texture contrast fell
        /// from 0.75 outside the pool to 0.15 inside it, and the light's own chroma arrived at 0.19
        /// against 0.39 for the same light on the multiply buffer.
        ///
        /// So the body of the light moves onto the ambient's multiply buffer, where it scales the
        /// surface and keeps its texture and its colour, and a small additive core is added back
        /// for the part that should read as too bright to look at. Both halves are one authored
        /// light; the second Light2D exists only because URP will not let one style do both.
        /// </summary>
        private void ApplySurfaceMix(LightInstance inst, LightPresetDefinition preset, float worldRadius)
        {
            float mix = Mathf.Clamp01(preset.surfaceMix);

            if (mix <= 0f)
            {
                // Purely additive — the original behaviour. Retire any core this light used to have
                // rather than leaving a stale one burning.
                inst.light2D.blendStyleIndex = PointLightBlendStyleIndex;
                inst.light2D.intensity       = inst.baseIntensity;
                inst.bodyIntensity           = inst.baseIntensity;
                inst.coreIntensity           = 0f;
                if (inst.coreLight != null)
                {
                    DestroyLightObject(inst.coreLight.gameObject);
                    inst.coreLight = null;
                }
                return;
            }

            // Body: onto the same multiply buffer the ambient writes to, so the two ADD there and
            // the surface is scaled by their sum instead of being painted over.
            inst.light2D.blendStyleIndex = AmbientBlendStyleIndex;
            inst.bodyIntensity           = inst.baseIntensity * mix * Mathf.Max(0.5f, preset.surfaceGain);
            inst.light2D.intensity       = inst.bodyIntensity;

            if (inst.coreLight == null)
            {
                var coreGo = new GameObject("Core");
                coreGo.transform.SetParent(inst.go.transform, false);
                coreGo.transform.localPosition = Vector3.zero;
                inst.coreLight = coreGo.AddComponent<Light2D>();
            }

            var core = inst.coreLight;
            core.lightType             = Light2D.LightType.Point;
            core.blendStyleIndex       = PointLightBlendStyleIndex;
            core.color                 = inst.light2D.color;
            inst.coreIntensity         = inst.baseIntensity * (1f - mix);
            core.intensity             = inst.coreIntensity;
            core.pointLightOuterRadius = worldRadius * Mathf.Clamp(preset.coreScale, 0.05f, 1f);
            core.pointLightInnerRadius = core.pointLightOuterRadius * Mathf.Clamp01(preset.centerScale);
            core.falloffIntensity      = inst.light2D.falloffIntensity;
            core.shadowsEnabled        = false;   // the body already casts, if anything does
        }

        /// <summary>
        /// Convert an authored colour into the radiance URP's 2D lights actually consume.
        ///
        /// The project renders in Linear colour space, and every sprite texture is converted on
        /// import — but <c>Light2D.color</c> is a plain C# field handed to the shader verbatim,
        /// with no conversion anywhere in URP's 2D path. So an artist picking (255, 200, 140) in
        /// the colour wheel was having those sRGB numbers used as linear radiance, and the encode
        /// back to display pulled every channel ratio toward 1: 0.784^(1/2.2) = 0.895,
        /// 0.549^(1/2.2) = 0.766. An authored saturation of 0.45 reached the screen as 0.16.
        ///
        /// Measured on the Magic preset (authored saturation 0.529): rendered 0.225 before this
        /// conversion, 0.410 after — the single change that makes a torch read as fire instead of
        /// as warm grey fog. Note the peak channel is untouched (linear(1.0) == 1.0), so a light
        /// does not lose its brightness ceiling; what drops is the luminance of the channels that
        /// were never supposed to be that bright, which is the whole point.
        /// </summary>
        private static Color ToRadiance(Color authored)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        /// <summary>Move a previously-spawned light to <paramref name="worldPos"/>; updates the persisted record's zone + rel coords.</summary>
        public void MoveLight(GameObject lightGo, Vector3 worldPos)
        {
            if (lightGo == null) return;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.go != lightGo) continue;
                // Authored lights only. A derived light is a CHILD of its building and follows it
                // by parenting; moving one here displaces the flame from its lamp-post until the
                // next load, writes zone/rel coords onto a record SaveAll never emits, and cannot
                // be undone because there is no snapshot to capture.
                if (!inst.persistent)
                {
                    Debug.LogWarning($"[WorldLightLoader] Refusing to move '{lightGo.name}' — it is " +
                                      "owned by a building. Move the building instead.");
                    return;
                }
                inst.go.transform.position = worldPos;
                // Update the persisted coords — keeps SaveAll round-trip stable.
                inst.zone = ResolveZoneAt(worldPos, out var rel);
                inst.relX = rel.x;
                inst.relY = rel.y;
                return;
            }
        }

        /// <summary>Destroy a previously-spawned light and remove it from the persistent record set.</summary>
        public void RemoveLight(GameObject lightGo)
        {
            if (lightGo == null) return;
            for (int i = _activeLights.Count - 1; i >= 0; i--)
            {
                if (_activeLights[i].go != lightGo) continue;
                DestroyLightObject(_activeLights[i].go);
                _activeLights.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// Everything needed to bring one light back exactly as it was — id included.
        ///
        /// A runtime editor's undo cannot re-create a deleted light from its preset key: the key
        /// says what family it belongs to, not what it WAS. Before this existed, undoing a delete
        /// called <see cref="RegisterRuntimeLight"/>, which minted a fresh id and no overrides, so
        /// a torch that had been tuned to a specific colour, intensity and radius came back as a
        /// stock torch with a different id. Measured live: id=1 with an authored colour came back
        /// as id=15 with none.
        ///
        /// It is a value object on purpose. It holds no GameObject, so it cannot go stale, and it
        /// stays valid across the destroy it was captured for.
        /// </summary>
        public sealed class LightSnapshot
        {
            public int     Id;
            public string  PresetId;
            public string  Zone;
            public float   RelX;
            public float   RelY;
            public Vector3 WorldPosition;

            public Color?  OverrideColor;
            public float?  OverrideIntensity;
            public float?  OverrideRadius;
            public float?  OverrideFalloff;
            public float?  OverrideFlickerAmp;
            public float?  OverrideFlickerSpeed;
        }

        /// <summary>
        /// Find a live light by its stable id, or null.
        ///
        /// Editor undo commands address lights by id rather than by captured GameObject. A
        /// captured reference dies with the object and cannot be revived: the redo of a spawn
        /// re-created the light but had nowhere to write the new reference, so the following undo
        /// looked at a corpse, did nothing, and left an orphan no further undo could reach.
        /// </summary>
        public GameObject FindLightById(int id)
        {
            if (id <= 0) return null;
            foreach (var inst in _activeLights)
                if (inst.id == id && inst.go != null) return inst.go;
            return null;
        }

        /// <summary>
        /// The catalog key a live light was built from — including a derived one, which
        /// <see cref="CaptureLight"/> deliberately refuses.
        ///
        /// The editor used to recover this by string-parsing the GameObject's name, which only
        /// worked because the name happens to be built from the key. Any preset key containing an
        /// underscore already broke it, and renaming the object broke it silently.
        /// </summary>
        public string GetLightPresetKey(GameObject lightGo)
        {
            if (lightGo == null) return null;
            foreach (var inst in _activeLights)
                if (inst.go == lightGo) return inst.presetId;
            return null;
        }

        /// <summary>
        /// Capture a light whole, for an undo that has to put it back. Returns null for a light
        /// this loader does not own, and for a derived light — a derived light is rebuilt from its
        /// building on every load, so restoring one from a snapshot would duplicate it.
        /// </summary>
        public LightSnapshot CaptureLight(GameObject lightGo)
        {
            if (lightGo == null) return null;
            foreach (var inst in _activeLights)
            {
                if (inst.go != lightGo) continue;
                if (!inst.persistent) return null;
                if (inst.id <= 0)
                {
                    // Every undo command addresses its light by id. An id of 0 is the derived
                    // sentinel that FindLightById refuses, so a snapshot carrying one produces a
                    // command that can never find its target — a silent no-op that still consumes
                    // a history step. Refuse to make one.
                    Debug.LogWarning($"[WorldLightLoader] '{lightGo.name}' has no usable id " +
                                      $"({inst.id}); it cannot take part in undo.");
                    return null;
                }
                return new LightSnapshot
                {
                    Id                   = inst.id,
                    PresetId             = inst.presetId,
                    Zone                 = inst.zone,
                    RelX                 = inst.relX,
                    RelY                 = inst.relY,
                    WorldPosition        = inst.go.transform.position,
                    OverrideColor        = inst.overrideColor,
                    OverrideIntensity    = inst.overrideIntensity,
                    OverrideRadius       = inst.overrideRadius,
                    OverrideFalloff      = inst.overrideFalloff,
                    OverrideFlickerAmp   = inst.overrideFlickerAmp,
                    OverrideFlickerSpeed = inst.overrideFlickerSpeed,
                };
            }
            return null;
        }

        /// <summary>
        /// Bring a captured light back, keeping its id so the record on disk stays the same record.
        ///
        /// If that id has since been taken — the author deleted a light, kept working, and only
        /// then undid — a fresh one is minted rather than shipping a file with two records under
        /// one id. Losing the id is a smaller harm than a duplicate the loader resolves by read
        /// order, and it is reported so it is not silent.
        /// </summary>
        public GameObject RestoreLight(LightSnapshot snapshot)
        {
            if (snapshot == null) return null;

            int id = snapshot.Id;
            if (id <= 0 || FindLightById(id) != null)
            {
                int minted = NextLightId();
                if (id > 0)
                    Debug.LogWarning($"[WorldLightLoader] Restoring light id={id} but that id is " +
                                     $"already in use; it comes back as id={minted}.");
                id = minted;
            }

            var data = new LightInstanceData
            {
                id        = id,
                preset_id = snapshot.PresetId,
                zone      = snapshot.Zone,
                rel_x     = snapshot.RelX,
                rel_y     = snapshot.RelY,
                overrides = null,
            };
            var go = SpawnFromData(data, overridePosition: snapshot.WorldPosition);
            if (go == null) return null;

            // Overrides are re-applied through the live path rather than through the JSON one so
            // a null stays a null: ApplyJsonOverrides reads -1 sentinels, and round-tripping a
            // Color? through them would turn "no override" into a real black.
            OverrideLight(go,
                color:        snapshot.OverrideColor,
                intensity:    snapshot.OverrideIntensity,
                radius:       snapshot.OverrideRadius,
                falloff:      snapshot.OverrideFalloff,
                flickerAmp:   snapshot.OverrideFlickerAmp,
                flickerSpeed: snapshot.OverrideFlickerSpeed);
            return go;
        }

        /// <summary>
        /// Destroy a spawned light's GameObject, in Play Mode or out of it.
        ///
        /// <c>Object.Destroy</c> is deferred to the end of the frame and is refused outright
        /// outside Play Mode — it logs "Destroy may not be called from edit mode!" and does
        /// nothing. That made the teardown paths unexercisable from an EditMode test: the very
        /// fixtures written to prove that a reload does not eat the lamp-post lights could not
        /// actually reload anything.
        /// </summary>
        private static void DestroyLightObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }

        /// <summary>Apply a per-instance override on top of the catalog preset and re-configure the live Light2D.</summary>
        public void OverrideLight(GameObject lightGo,
            Color? color = null, float? intensity = null, float? radius = null,
            float? falloff = null, float? flickerAmp = null, float? flickerSpeed = null)
        {
            if (lightGo == null) return;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.go != lightGo) continue;
                if (!inst.persistent)
                {
                    // Same reason as MoveLight: the override would look right until the next load
                    // and would never be written, because SaveAll skips derived records.
                    Debug.LogWarning($"[WorldLightLoader] Refusing to override '{lightGo.name}' — it " +
                                      "is owned by a building; edit its light preset instead.");
                    return;
                }
                if (color.HasValue)        inst.overrideColor        = color;
                if (intensity.HasValue)    inst.overrideIntensity    = intensity;
                if (radius.HasValue)       inst.overrideRadius       = radius;
                if (falloff.HasValue)      inst.overrideFalloff      = falloff;
                if (flickerAmp.HasValue)   inst.overrideFlickerAmp   = flickerAmp;
                if (flickerSpeed.HasValue) inst.overrideFlickerSpeed = flickerSpeed;

                // Re-apply the cached preset + new overrides to the live Light2D.
                var preset = _catalog?.GetByKey(inst.presetId);
                if (preset != null) ApplyPresetToLight(inst, preset);
                return;
            }
        }

        /// <summary>
        /// Ratio below which a save is treated as an accident rather than an edit. Deleting more
        /// than half the lights in one sitting is possible but rare; a half-loaded world is not.
        /// </summary>
        private const float SaveDropRefusalRatio = 0.5f;

        /// <summary>Returned by <see cref="SaveAll"/> when the guard refused to write.</summary>
        public const int SaveAborted = -1;

        /// <summary>
        /// Persist all currently-active lights back to <c>light_instances.json</c>.
        ///
        /// Returns the number of records written, or <see cref="SaveAborted"/> when the anti-wipe
        /// guard refused. That guard exists because this method is a whole-file overwrite of
        /// authored data with no undo: before it, a world whose lights had not loaded — a missing
        /// catalog, a cleared map slot, a renamed preset — wrote a five-byte empty array over the
        /// file and reported "Saved 0 light instance(s)" as a success. It is the same accident
        /// that reduced particles_instances.json to 4 bytes, and MapEditorManager already
        /// documents the hazard for THIS file.
        ///
        /// <paramref name="force"/> is the deliberate escape hatch for actually deleting
        /// everything; nothing calls it automatically.
        /// </summary>
        public int SaveAll(bool force = false)
        {
            var sb = new StringBuilder(1024);
            sb.Append("[\n");
            bool first = true;
            int written = 0;
            foreach (var inst in _activeLights)
            {
                if (inst.go == null) continue;
                if (!inst.persistent) continue;   // derived from a building — see LightInstance.persistent
                if (!first) sb.Append(",\n");
                first = false;
                AppendInstance(sb, inst);
                written++;
            }
            // Records that could not be spawned are re-emitted exactly as they were read, so a
            // renamed preset key cannot silently delete every light that used it.
            foreach (var data in _unspawnedRecords)
            {
                if (!first) sb.Append(",\n");
                first = false;
                AppendRecordData(sb, data);
                written++;
            }
            sb.Append("\n]\n");

            if (!force && !MayOverwrite(written, out string refusal))
            {
                Debug.LogError($"[WorldLightLoader] ABORTING save — {refusal} File NOT written. " +
                                "The world was probably cleared or only partially loaded; restart Play " +
                                "Mode to reload the last good on-disk state. If the drop is intentional, " +
                                "call SaveAll(force: true).");
                return SaveAborted;
            }

            ResolveRepository().WriteRawJson(WorldId.Base, sb.ToString());
            Debug.Log($"[WorldLightLoader] Saved {written} light instance(s) to repository.");
            return written;
        }

        /// <summary>
        /// Compare what is about to be written against what is already on disk. Modelled on the
        /// Particles editor's guard, which refuses the same two shapes of accident.
        /// </summary>
        private bool MayOverwrite(int aboutToWrite, out string refusal)
        {
            refusal = null;
            int onDisk = CountRecordsOnDisk();
            if (onDisk <= 0) return true;   // nothing to lose

            if (aboutToWrite == 0)
            {
                refusal = $"the world holds 0 authored lights but the file holds {onDisk}.";
                return false;
            }
            if (aboutToWrite < onDisk * SaveDropRefusalRatio)
            {
                refusal = $"the world holds {aboutToWrite} authored lights but the file holds {onDisk} " +
                          "— too large a drop to be an edit.";
                return false;
            }
            return true;
        }

        /// <summary>How many records the file currently holds.</summary>
        private int CountRecordsOnDisk()
        {
            try
            {
                string json = ResolveRepository().ReadRawJson(WorldId.Base);
                if (string.IsNullOrWhiteSpace(json)) return 0;
                var wrapper = JsonUtility.FromJson<LightInstanceArrayWrapper>("{\"items\":" + json + "}");
                return wrapper?.items?.Length ?? 0;
            }
            catch (Exception ex)
            {
                // Unreadable is NOT the same as empty: refuse to treat a read failure as
                // permission to overwrite.
                Debug.LogWarning("[WorldLightLoader] Could not read the existing light file to guard the " +
                                  $"save ({ex.GetType().Name}). Treating it as populated.");
                return int.MaxValue;
            }
        }

        // ── Spawn helpers (shared by load + runtime authoring) ───────────────

        private GameObject SpawnFromData(LightInstanceData data, Vector3? overridePosition = null)
        {
            var preset = _catalog?.GetByKey(data.preset_id);
            if (preset == null)
            {
                Debug.LogWarning($"[WorldLightLoader] Unknown preset '{data.preset_id}' for light id={data.id}. Skipping.");
                return null;
            }

            // A named zone this world does not have cannot be resolved, and falling back to a
            // zero offset does not produce "roughly right" — it produces a light somewhere else
            // entirely, which the author then drags, and ResolveZoneAt rebases the record into
            // whichever zone that wrong position lands in. The record is corrupted on disk by an
            // edit that looked like a correction. Refuse to place it; the caller preserves it
            // verbatim. An EMPTY zone is a different thing and stays legal: it means the coords
            // are already absolute.
            if (!overridePosition.HasValue && !string.IsNullOrEmpty(data.zone))
            {
                var zones = ResolveZoneManager();
                if (zones != null && !zones.TryGetZone(data.zone, out _))
                {
                    Debug.LogError($"[WorldLightLoader] Light id={data.id} names zone '{data.zone}', " +
                                    "which this world does not have. It is NOT placed — placing it " +
                                    "would put it in the wrong part of the map and the first edit " +
                                    "would rebase it onto the wrong zone on disk. The record is kept " +
                                    "verbatim.");
                    return null;
                }
            }

            Vector3 worldPos;
            if (overridePosition.HasValue)
            {
                worldPos = overridePosition.Value;
            }
            else
            {
                Vector2 zoneWorld = ComputeWorldPosition(data);
                worldPos = new Vector3(zoneWorld.x, zoneWorld.y, 0f);
            }

            Transform root = _lightsRoot != null ? _lightsRoot : transform;
            var go = new GameObject($"Light_{data.id}_{data.preset_id}");
            go.transform.SetParent(root);
            go.transform.position = worldPos;
            var light2D = go.AddComponent<Light2D>();

            var inst = new LightInstance
            {
                id           = data.id,
                presetId     = data.preset_id,
                zone         = data.zone,
                relX         = data.rel_x,
                relY         = data.rel_y,
                light2D      = light2D,
                go           = go,
                flickerOffset = UnityEngine.Random.Range(0f, 10f),
            };

            ApplyJsonOverrides(inst, data.overrides, preset);
            ApplyPresetToLight(inst, preset);
            // Respect the global pointLightsEnabled gate immediately on spawn so a
            // light created during the lights-disable window does not flash on.
            if (!_pointLightsEnabled) go.SetActive(false);

            _activeLights.Add(inst);
            return go;
        }

        // JSON colors are 0–255 ints; per-property -1 is the sentinel for "no override".
        private static void ApplyJsonOverrides(LightInstance inst, LightOverrides ov,
                                              LightPresetDefinition preset)
        {
            if (ov == null) return;
            if (ov.color != null && ov.color.Length >= 3)
            {
                var c = new Color(ov.color[0] / 255f, ov.color[1] / 255f, ov.color[2] / 255f, 1f);

                // An override bit-identical to the preset is not an override — it is a copy the
                // editor wrote on the way past, and every one of the ten shipped records carries
                // one. While they are honoured, retuning a preset's colour changes nothing in the
                // world and nothing says why: the author edits the asset, presses Play, and the
                // frame is pixel-for-pixel the same. Dropping them here costs no pixel today (the
                // values are equal by construction) and hands the presets back their job.
                if (preset != null && SameColour8Bit(c, preset.color))
                    Valkur.Core.VerboseLog.Log(Valkur.Core.VerboseLog.Category.World,
                        () => $"[WorldLightLoader] Light id={inst.id} overrides its colour with its " +
                               "own preset's value; ignoring the override.");
                else
                    inst.overrideColor = c;
            }
            if (ov.intensity     >= 0f) inst.overrideIntensity    = ov.intensity;
            if (ov.radius        >= 0f) inst.overrideRadius       = ov.radius;
            if (ov.falloff       >= 0f) inst.overrideFalloff      = ov.falloff;
            if (ov.flicker_amp   >= 0f) inst.overrideFlickerAmp   = ov.flicker_amp;
            if (ov.flicker_speed >= 0f) inst.overrideFlickerSpeed = ov.flicker_speed;
        }

        /// <summary>
        /// The zone manager, resolved lazily from the scene.
        ///
        /// <c>_zoneManager</c> is a [SerializeField] that NOTHING assigns — the bootstrap builds
        /// this loader with AddComponent and only calls SetCatalog, no scene or prefab carries the
        /// component, and no SetZoneManager exists. It was therefore null 100 % of the time, so
        /// every zone offset resolved to (0,0) and all ten shipped lights spawned 150-200 tiles
        /// from where their record says. That is defect shape #2 of
        /// .github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md, in the subsystem that incident
        /// told the reader to check.
        ///
        /// BuildingLoader, SpawnerInstanceLoader and ParticleInstancesLoader all carry this exact
        /// fallback. This loader was the one that did not.
        /// </summary>
        private ZoneManager ResolveZoneManager()
        {
            if (_zoneManager == null) _zoneManager = FindObjectOfType<ZoneManager>();
            return _zoneManager;
        }

        private Vector2 ComputeWorldPosition(LightInstanceData data)
        {
            Vector2 zoneOffset = Vector2.zero;
            var zones = ResolveZoneManager();
            // Fallback chain for chunk side length: live ZoneManager → injected
            // WorldConfig → the documented legacy default.
            float zoneHeight = _worldConfig != null
                ? _worldConfig.ChunkSize
                : WorldConfig.LegacyChunkSize;

            // string.IsNullOrEmpty first: TryGetZone forwards its argument to Dictionary
            // .TryGetValue, which throws on a null key rather than returning false. A record
            // whose "zone" key is simply absent deserializes to null.
            if (zones != null && !string.IsNullOrEmpty(data.zone) &&
                zones.TryGetZone(data.zone, out var zoneDef))
            {
                zoneOffset = new Vector2(zoneDef.gridOffset.x, zoneDef.gridOffset.y);
                zoneHeight = zones.ZoneHeightTiles;
            }

            // Python stores rel_x, rel_y as pixel offsets (Y-down)
            // Convert to Unity world coords (Y-up):
            float worldX = zoneOffset.x + data.rel_x * PX_TO_WORLD;
            float worldY = zoneOffset.y + (zoneHeight - 1f) - data.rel_y * PX_TO_WORLD;
            return new Vector2(worldX, worldY);
        }

        // Inverse of ComputeWorldPosition: figure out the zone + rel coords for a
        // given world position. The runtime editor needs this so spawned lights
        // get persisted with the same record shape as authored lights.
        private string ResolveZoneAt(Vector3 worldPos, out Vector2 relPx)
        {
            Vector2 zoneOffset = Vector2.zero;
            float zoneHeight = _worldConfig != null ? _worldConfig.ChunkSize : WorldConfig.LegacyChunkSize;
            string zoneId = "";

            var zones = ResolveZoneManager();
            if (zones != null)
            {
                var tilePos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                if (zones.TryGetZoneAtTile(tilePos, out var zoneDef))
                {
                    zoneOffset = new Vector2(zoneDef.gridOffset.x, zoneDef.gridOffset.y);
                    zoneHeight = zones.ZoneHeightTiles;
                    zoneId     = zoneDef.zoneName;
                }
            }

            // Invert the load-side math: rel_x = (worldX - zoneOffset.x) / PX_TO_WORLD,
            // rel_y = (zoneOffset.y + zoneHeight - 1 - worldY) / PX_TO_WORLD.
            float relX = (worldPos.x - zoneOffset.x) / PX_TO_WORLD;
            float relY = (zoneOffset.y + (zoneHeight - 1f) - worldPos.y) / PX_TO_WORLD;
            relPx = new Vector2(relX, relY);
            return zoneId;
        }

        private void ApplyPresetToLight(LightInstance inst, LightPresetDefinition preset)
        {
            // Flicker driver values (cached on the instance for Update()).
            inst.baseIntensity = inst.overrideIntensity ?? preset.intensity;
            inst.flickerAmp    = inst.overrideFlickerAmp ?? preset.flickerAmplitude;
            inst.flickerSpeed  = inst.overrideFlickerSpeed ?? preset.flickerSpeed;
            inst.flickerStyle  = preset.flickerStyle;

            if (inst.light2D == null) return;

            // Point, not Sprite. The reflection this replaced wrote Enum.ToObject(type, 2),
            // and 2 is Sprite in URP 14 — a Sprite light with no cookie clears its mesh, so
            // every torch in the world rasterised nothing at all. See
            // .github/DAY_NIGHT_AUDIT_AND_ROADMAP.md.
            inst.light2D.lightType = Light2D.LightType.Point;

            // Additive: the ambient Multiply light darkens the world, and placed lights add
            // photons back on top of it. On Multiply (the old default, inherited by never
            // setting the index) a torch could only ever darken what it touched, which is
            // the opposite of a torch.
            inst.light2D.blendStyleIndex = PointLightBlendStyleIndex;

            inst.light2D.intensity = inst.baseIntensity;
            inst.light2D.color     = ToRadiance(inst.overrideColor ?? preset.color);

            float radiusPx    = inst.overrideRadius ?? preset.radius;
            float worldRadius = radiusPx * PX_TO_WORLD;
            inst.light2D.pointLightOuterRadius = worldRadius;
            inst.light2D.pointLightInnerRadius = worldRadius * Mathf.Clamp01(preset.centerScale);

            // URP clamps falloffIntensity to [0,1]; LightPresetDefinition used to allow
            // 1.6-2.2, so all three shipped presets collapsed to an identical hard falloff.
            inst.light2D.falloffIntensity = Mathf.Clamp01(inst.overrideFalloff ?? preset.falloff);

            inst.light2D.shadowsEnabled  = preset.castsShadows;
            inst.light2D.shadowIntensity = Mathf.Clamp01(preset.shadowStrength);

            ApplySurfaceMix(inst, preset, worldRadius);
        }

        /// <summary>
        /// True when any preset in the catalog casts shadows.
        ///
        /// Buildings consult this before attaching a <c>ShadowCaster2D</c>: URP's caster runs a
        /// public <c>Update()</c> every frame per instance, so 141 of them would be a standing
        /// cost even in a world where nothing casts. With every preset's shadows off, no caster
        /// is created and the feature is genuinely free.
        /// </summary>
        public bool ShadowsInUse
        {
            get
            {
                if (_shadowsInUse.HasValue) return _shadowsInUse.Value;
                bool any = false;
                if (_catalog != null)
                {
                    foreach (var preset in _catalog.presets)
                    {
                        if (preset == null || !preset.castsShadows) continue;
                        any = true;
                        break;
                    }
                }
                _shadowsInUse = any;
                return any;
            }
        }

        private bool? _shadowsInUse;

        /// <summary>
        /// Give every record a positive id that no other record shares, before anything is spawned.
        ///
        /// The whole editing layer addresses lights by id — undo, redo, delete, move all resolve
        /// through <see cref="FindLightById"/> — so id uniqueness is not a tidiness property, it
        /// is the contract that layer rests on. Nothing on the read path used to enforce it:
        ///
        ///   * an absent "id" key deserializes to 0, which is the sentinel
        ///     <see cref="RegisterDerivedLight"/> uses for "not addressable", so an authored light
        ///     silently joined that class and every command aimed at it did nothing;
        ///   * two records sharing an id resolve to whichever comes first, so deleting the second
        ///     of the pair and pressing redo destroys the first one instead.
        ///
        /// Repaired in memory and reported, rather than refused: the records are real authored
        /// data and the ids are recoverable. The repair reaches disk on the next save.
        /// </summary>
        private void NormaliseRecordIds(LightInstanceData[] items)
        {
            if (items == null) return;

            var used = new HashSet<int>();
            int next = 0;
            foreach (var d in items)
                if (d != null && d.id > next) next = d.id;

            foreach (var d in items)
            {
                if (d == null) continue;
                if (d.id > 0 && used.Add(d.id)) continue;

                int replacement = ++next;
                Debug.LogWarning($"[WorldLightLoader] Light record with preset '{d.preset_id}' had " +
                                  (d.id <= 0
                                      ? $"no usable id ({d.id})"
                                      : $"the duplicate id {d.id}") +
                                  $"; it is now id={replacement}. Ids address lights for undo and " +
                                  "for the editor, so they must be unique and positive. The file is " +
                                  "corrected on the next save.");
                d.id = replacement;
                used.Add(replacement);
            }

            // Hand the allocator the high-water mark this pass reached, so the first runtime spawn
            // continues the sequence instead of recomputing one that could collide with a record
            // this pass just renumbered.
            _nextLightId = next + 1;
        }

        /// <summary>
        /// Mint an id no record in this world is using.
        ///
        /// It has to consider <see cref="_unspawnedRecords"/> as well as the live lights. Those
        /// records are invisible in the scene but are written back verbatim by SaveAll, so an id
        /// taken from the live lights alone can collide with one of them — and the file then
        /// ships two lights with the same id, which the loader resolves by whichever it reads
        /// last.
        /// </summary>
        /// <summary>Next id to hand out. 0 means unseeded; see <see cref="NextLightId"/>.</summary>
        private int _nextLightId;

        private int NextLightId()
        {
            // Monotonic within one world generation, and never recycled. Recomputing max+1 from
            // whatever is currently live looks equivalent and is not: delete the highest-numbered
            // light and the next spawn is handed its number back, so the delete command still
            // sitting in the undo history now names the NEW light — and pressing redo destroys it.
            // The counter is reseeded only when the world itself is replaced.
            if (_nextLightId <= 0)
            {
                int max = 0;
                foreach (var inst in _activeLights)
                    if (inst.id > max) max = inst.id;
                foreach (var rec in _unspawnedRecords)
                    if (rec.id > max) max = rec.id;
                _nextLightId = max + 1;
            }
            return _nextLightId++;
        }

        // ── JSON serialisation (write-side) ──────────────────────────────────

        private void AppendInstance(StringBuilder sb, LightInstance inst)
        {
            sb.Append("  {\n");
            sb.Append("    \"id\": ").Append(inst.id).Append(",\n");
            sb.Append("    \"preset_id\": \"").Append(JsonEscape(inst.presetId)).Append("\",\n");
            sb.Append("    \"zone\": \"").Append(JsonEscape(inst.zone ?? "")).Append("\",\n");
            sb.Append("    \"rel_x\": ").Append(Format(inst.relX)).Append(",\n");
            sb.Append("    \"rel_y\": ").Append(Format(inst.relY));

            if (HasAnyOverride(inst))
            {
                sb.Append(",\n    \"overrides\": {");
                AppendOverridesBody(sb, inst);
                sb.Append("\n    }");
            }
            sb.Append("\n  }");
        }

        /// <summary>
        /// Re-emit a record that came off disk but could not be spawned, field for field.
        ///
        /// It goes through the raw <c>LightInstanceData</c> rather than a <c>LightInstance</c>
        /// precisely because there is no live light to read from: the whole point is to preserve
        /// what the file said about a light this session could not build.
        /// </summary>
        private static void AppendRecordData(StringBuilder sb, LightInstanceData data)
        {
            sb.Append("  {\n");
            sb.Append("    \"id\": ").Append(data.id).Append(",\n");
            sb.Append("    \"preset_id\": \"").Append(JsonEscape(data.preset_id)).Append("\",\n");
            sb.Append("    \"zone\": \"").Append(JsonEscape(data.zone ?? "")).Append("\",\n");
            sb.Append("    \"rel_x\": ").Append(Format(data.rel_x)).Append(",\n");
            sb.Append("    \"rel_y\": ").Append(Format(data.rel_y));

            var ov = data.overrides;
            if (ov != null)
            {
                bool first = true;
                var body = new StringBuilder();
                if (ov.color != null && ov.color.Length >= 3)
                {
                    AppendComma(body, ref first);
                    body.Append("\n      \"color\": [")
                        .Append(Mathf.RoundToInt(ov.color[0])).Append(", ")
                        .Append(Mathf.RoundToInt(ov.color[1])).Append(", ")
                        .Append(Mathf.RoundToInt(ov.color[2]))
                        .Append(']');
                }
                // -1 is the schema's "no override" sentinel on every numeric field.
                AppendNumberOverride(body, ref first, "intensity",     ov.intensity     >= 0f ? ov.intensity     : (float?)null);
                AppendNumberOverride(body, ref first, "radius",        ov.radius        >= 0f ? ov.radius        : (float?)null);
                AppendNumberOverride(body, ref first, "falloff",       ov.falloff       >= 0f ? ov.falloff       : (float?)null);
                AppendNumberOverride(body, ref first, "flicker_amp",   ov.flicker_amp   >= 0f ? ov.flicker_amp   : (float?)null);
                AppendNumberOverride(body, ref first, "flicker_speed", ov.flicker_speed >= 0f ? ov.flicker_speed : (float?)null);

                if (body.Length > 0)
                {
                    sb.Append(",\n    \"overrides\": {");
                    sb.Append(body);
                    sb.Append("\n    }");
                }
            }
            sb.Append("\n  }");
        }

        /// <summary>
        /// Equal once both are quantised to the 0-255 the file stores. Comparing the floats
        /// directly would call 200/255 different from 0.784313738, which is the same colour
        /// having made one round trip through the schema.
        /// </summary>
        private static bool SameColour8Bit(Color a, Color b)
            => Mathf.RoundToInt(a.r * 255f) == Mathf.RoundToInt(b.r * 255f)
            && Mathf.RoundToInt(a.g * 255f) == Mathf.RoundToInt(b.g * 255f)
            && Mathf.RoundToInt(a.b * 255f) == Mathf.RoundToInt(b.b * 255f);

        private static bool HasAnyOverride(LightInstance inst) =>
            inst.overrideColor.HasValue        || inst.overrideIntensity.HasValue ||
            inst.overrideRadius.HasValue       || inst.overrideFalloff.HasValue ||
            inst.overrideFlickerAmp.HasValue   || inst.overrideFlickerSpeed.HasValue;

        private static void AppendOverridesBody(StringBuilder sb, LightInstance inst)
        {
            bool first = true;
            if (inst.overrideColor.HasValue)
            {
                var c = inst.overrideColor.Value;
                AppendComma(sb, ref first);
                sb.Append("\n      \"color\": [")
                  .Append(Mathf.RoundToInt(c.r * 255f)).Append(", ")
                  .Append(Mathf.RoundToInt(c.g * 255f)).Append(", ")
                  .Append(Mathf.RoundToInt(c.b * 255f))
                  .Append(']');
            }
            AppendNumberOverride(sb, ref first, "intensity",     inst.overrideIntensity);
            AppendNumberOverride(sb, ref first, "radius",        inst.overrideRadius);
            // "falloff" was the one key the reader parsed and the writer did not emit, so an
            // authored falloff survived exactly until the next save and then vanished — the
            // quietest shape of data loss there is, because nothing about the file looks wrong
            // afterwards. Keep this list and AppendRecordData's in step with LightOverrides.
            AppendNumberOverride(sb, ref first, "falloff",       inst.overrideFalloff);
            AppendNumberOverride(sb, ref first, "flicker_amp",   inst.overrideFlickerAmp);
            AppendNumberOverride(sb, ref first, "flicker_speed", inst.overrideFlickerSpeed);
        }

        private static void AppendNumberOverride(StringBuilder sb, ref bool first, string key, float? value)
        {
            if (!value.HasValue) return;
            AppendComma(sb, ref first);
            sb.Append("\n      \"").Append(key).Append("\": ").Append(Format(value.Value));
        }

        private static void AppendComma(StringBuilder sb, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;
        }

        private static string Format(float v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private static string JsonEscape(string s) =>
            s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── JSON DTOs ────────────────────────────────────────────────────────

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
