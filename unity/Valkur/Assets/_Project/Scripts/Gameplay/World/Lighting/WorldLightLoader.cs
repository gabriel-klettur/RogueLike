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
            public float?    overrideFlickerAmp;
            public float?    overrideFlickerSpeed;

            public Light2D   light2D;     // Spawned Light2D (URP).
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

        public int ActiveLightCount => _activeLights.Count;

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

                inst.light2D.intensity = inst.baseIntensity * FlickerFactor(inst, time);
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
            if (_catalog == null) return;

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

            int spawned = 0;
            foreach (var data in wrapper.items)
            {
                if (SpawnFromData(data) != null) spawned++;
            }

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

        /// <summary>Destroy every spawned light without touching disk.</summary>
        public void ClearSpawnedLights()
        {
            for (int i = _activeLights.Count - 1; i >= 0; i--)
            {
                if (_activeLights[i].go != null) Destroy(_activeLights[i].go);
            }
            _activeLights.Clear();
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

        /// <summary>Move a previously-spawned light to <paramref name="worldPos"/>; updates the persisted record's zone + rel coords.</summary>
        public void MoveLight(GameObject lightGo, Vector3 worldPos)
        {
            if (lightGo == null) return;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.go != lightGo) continue;
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
                Destroy(_activeLights[i].go);
                _activeLights.RemoveAt(i);
                return;
            }
        }

        /// <summary>Apply a per-instance override on top of the catalog preset and re-configure the live Light2D.</summary>
        public void OverrideLight(GameObject lightGo,
            Color? color = null, float? intensity = null, float? radius = null,
            float? flickerAmp = null, float? flickerSpeed = null)
        {
            if (lightGo == null) return;
            for (int i = 0; i < _activeLights.Count; i++)
            {
                var inst = _activeLights[i];
                if (inst.go != lightGo) continue;
                if (color.HasValue)        inst.overrideColor        = color;
                if (intensity.HasValue)    inst.overrideIntensity    = intensity;
                if (radius.HasValue)       inst.overrideRadius       = radius;
                if (flickerAmp.HasValue)   inst.overrideFlickerAmp   = flickerAmp;
                if (flickerSpeed.HasValue) inst.overrideFlickerSpeed = flickerSpeed;

                // Re-apply the cached preset + new overrides to the live Light2D.
                var preset = _catalog?.GetByKey(inst.presetId);
                if (preset != null) ApplyPresetToLight(inst, preset);
                return;
            }
        }

        /// <summary>Persist all currently-active lights back to <c>light_instances.json</c>.</summary>
        public int SaveAll()
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
            sb.Append("\n]\n");

            ResolveRepository().WriteRawJson(WorldId.Base, sb.ToString());
            Debug.Log($"[WorldLightLoader] Saved {written} light instance(s) to repository.");
            return written;
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

            ApplyJsonOverrides(inst, data.overrides);
            ApplyPresetToLight(inst, preset);
            // Respect the global pointLightsEnabled gate immediately on spawn so a
            // light created during the lights-disable window does not flash on.
            if (!_pointLightsEnabled) go.SetActive(false);

            _activeLights.Add(inst);
            return go;
        }

        // JSON colors are 0–255 ints; per-property -1 is the sentinel for "no override".
        private static void ApplyJsonOverrides(LightInstance inst, LightOverrides ov)
        {
            if (ov == null) return;
            if (ov.color != null && ov.color.Length >= 3)
                inst.overrideColor = new Color(
                    ov.color[0] / 255f,
                    ov.color[1] / 255f,
                    ov.color[2] / 255f,
                    1f);
            if (ov.intensity     >= 0f) inst.overrideIntensity    = ov.intensity;
            if (ov.radius        >= 0f) inst.overrideRadius       = ov.radius;
            if (ov.flicker_amp   >= 0f) inst.overrideFlickerAmp   = ov.flicker_amp;
            if (ov.flicker_speed >= 0f) inst.overrideFlickerSpeed = ov.flicker_speed;
        }

        private Vector2 ComputeWorldPosition(LightInstanceData data)
        {
            Vector2 zoneOffset = Vector2.zero;
            // Fallback chain for chunk side length: live ZoneManager → injected
            // WorldConfig → the documented legacy default.
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

            if (_zoneManager != null)
            {
                var tilePos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
                if (_zoneManager.TryGetZoneAtTile(tilePos, out var zoneDef))
                {
                    zoneOffset = new Vector2(zoneDef.gridOffset.x, zoneDef.gridOffset.y);
                    zoneHeight = _zoneManager.ZoneHeightTiles;
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
            inst.light2D.color     = inst.overrideColor ?? preset.color;

            float radiusPx    = inst.overrideRadius ?? preset.radius;
            float worldRadius = radiusPx * PX_TO_WORLD;
            inst.light2D.pointLightOuterRadius = worldRadius;
            inst.light2D.pointLightInnerRadius = worldRadius * Mathf.Clamp01(preset.centerScale);

            // URP clamps falloffIntensity to [0,1]; LightPresetDefinition used to allow
            // 1.6-2.2, so all three shipped presets collapsed to an identical hard falloff.
            inst.light2D.falloffIntensity = Mathf.Clamp01(preset.falloff);

            inst.light2D.shadowsEnabled  = preset.castsShadows;
            inst.light2D.shadowIntensity = Mathf.Clamp01(preset.shadowStrength);
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

        private int NextLightId()
        {
            int max = 0;
            foreach (var inst in _activeLights)
                if (inst.id > max) max = inst.id;
            return max + 1;
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

        private static bool HasAnyOverride(LightInstance inst) =>
            inst.overrideColor.HasValue        || inst.overrideIntensity.HasValue ||
            inst.overrideRadius.HasValue       || inst.overrideFlickerAmp.HasValue ||
            inst.overrideFlickerSpeed.HasValue;

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
