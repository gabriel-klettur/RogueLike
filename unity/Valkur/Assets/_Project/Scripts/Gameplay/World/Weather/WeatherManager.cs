using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Singleton orchestrator for the weather effects, and the owner of WHICH ZONE has which
    /// weather.
    ///
    /// Weather is stored per zone and rendered once. Those are two different facts, and
    /// conflating them is the mistake this class exists to avoid: there is one camera and one
    /// sky, so there is exactly one <see cref="RainEffect"/> no matter how many zones are
    /// raining — what varies per zone is the LEVEL that effect targets. Walking from a
    /// heavy-rain zone into a light-rain one retargets the same effect, and because
    /// <see cref="WeatherEffect"/> already keeps activation and density as separate scalars,
    /// that arrives as a ramp rather than a cut, for free.
    ///
    /// A zone is the right unit because it is the world's SEMANTIC unit here — it has a name,
    /// and the music and ambience already resolve against it (<c>ZoneManager.Update</c> calls
    /// <c>IAudioService.OnZoneChanged</c>). A 50x50 map tile is a unit of file, not a unit of
    /// place: "it snows in the forest" is a sentence, "it snows in section (3,2)" is not.
    ///
    /// It also drives the two shared fields the effects read but none of them may own:
    ///
    ///   • <see cref="WeatherWind"/> is ticked here, ONCE per frame, so every effect reading
    ///     the gust within a frame reads the same sample. Ticking it from the effects would
    ///     advance the envelope once per active weather, making a gust arrive faster the more
    ///     weathers are running and slanting rain and snow by different amounts in the same gust.
    ///   • <see cref="WeatherGrade"/> is composed here from all three live densities, because
    ///     a grade is a property of the SCENE, not of one effect — two effects each writing
    ///     "my" saturation would be two owners of one field.
    ///
    /// Effects stack freely within a zone: Wind + Rain is a wind-driven rainstorm, and at
    /// Heavy rain it gets lightning.
    /// </summary>
    public sealed class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance            = null;
            OnWeatherChanged    = null;
            AudioEnabled        = true;
            LightningEnabled    = true;
            AccumulationEnabled = true;
        }

        /// <summary>
        /// Master switch for the procedural audio beds (see <see cref="WeatherAudio"/>).
        /// Exposed because the beds are synthesised rather than authored, so a player or a
        /// designer may reasonably want the visuals without them.
        /// </summary>
        public static bool AudioEnabled { get; set; } = true;

        /// <summary>Master switch for storm lightning. Photosensitivity, and dev sanity.</summary>
        public static bool LightningEnabled { get; set; } = true;

        /// <summary>
        /// Master switch for lying snow (<see cref="SnowAccumulation"/>). Off makes the world
        /// melt back to bare rather than freezing the drift where it stood — a half-covered
        /// world with the feature disabled is worse than either end of the range.
        /// </summary>
        public static bool AccumulationEnabled { get; set; } = true;

        /// <summary>Raised whenever a zone's weather changes. Args: zone, type, new level.</summary>
        public static event System.Action<string, WeatherType, WeatherIntensity> OnWeatherChanged;

        /// <summary>Number of weather types — the width of one zone's level row.</summary>
        private const int TypeCount = 3;

        /// <summary>
        /// Authored weather, per zone. Zone names are compared with OrdinalIgnoreCase because
        /// <c>ZoneManager</c>'s own lookup is, and a weather keyed by a differently-cased name
        /// would be silently unreachable rather than visibly wrong.
        /// </summary>
        private readonly Dictionary<string, WeatherIntensity[]> _byZone =
            new Dictionary<string, WeatherIntensity[]>(System.StringComparer.OrdinalIgnoreCase);

        // Live effect instances, created lazily. One set for the whole world.
        private readonly Dictionary<WeatherType, WeatherEffect> _effects =
            new Dictionary<WeatherType, WeatherEffect>();

        private SnowSplatMap _splatMap;
        private ZoneManager  _zones;

        /// <summary>
        /// The zone the player is standing in, and therefore the zone the editor authors and
        /// the world renders. Empty before the first detection.
        /// </summary>
        public string ActiveZone { get; private set; } = string.Empty;

        /// <summary>
        /// True while the player is inside an interior or any overlay that is not in the zone
        /// database. Weather fades out there — you are under a roof — and comes back on the way
        /// out, which the effects' own fade makes a ramp rather than a pop.
        ///
        /// Read from <c>ZoneManager.IsDetectionSuspended</c> rather than inferred: that flag is
        /// set by <c>WorldTransitionService</c> and by nothing else in the project, so it means
        /// exactly "the base-world zones do not describe where the player is".
        /// </summary>
        public bool IsIndoors { get; private set; }

        /// <summary>True once a zone has been resolved, i.e. authoring has somewhere to go.</summary>
        public bool HasActiveZone => !string.IsNullOrEmpty(ActiveZone);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // The accumulation buffer lives on its own child so its RenderTextures are released
            // with it. Created eagerly rather than on first snowfall: it publishes the shader
            // globals the world materials sample every frame, snow or not.
            var mapGo = new GameObject("SnowSplatMap");
            mapGo.transform.SetParent(transform, false);
            _splatMap = mapGo.AddComponent<SnowSplatMap>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            // Hand back everything the effects were driving. A scene unload that left the
            // wind field and the grade where the last storm put them would grade the next
            // scene's first frames with weather that is not running.
            WeatherWind.WeatherSpeed = 0f;
            WeatherGrade.Compose(0f, 0f, 0f);
            // A Shader global outlives the object that set it: leaving the drift behind would
            // snow the next scene with nothing falling in it.
            SnowAccumulation.SetAmount(0f);
        }

        /// <summary>
        /// The wind field and the grade advance every frame regardless of whether anything is
        /// falling: the ambient breeze runs with all weather off (see
        /// <see cref="WeatherWind.AmbientSpeed"/>), and the grade has to be able to RELAX back
        /// to neutral after a storm rather than freezing at the last value it was given.
        /// </summary>
        private void Update()
        {
            float dt = Time.deltaTime;

            RefreshActiveZone();

            WeatherWind.Tick(dt);

            float rain = DensityOf(WeatherType.Rain);
            float snow = DensityOf(WeatherType.Snow);
            float wind = DensityOf(WeatherType.Wind);

            // Order matters: the flash envelope is one of the terms Compose folds into Gain
            // and Lift, so it has to be advanced first or the grade is always one frame behind
            // the strike — visible, because the strike's whole leading edge is two frames long.
            WeatherGrade.TickLightning(dt, rain, LightningEnabled);
            WeatherGrade.Compose(rain, snow, wind);

            // Snow LYING on the world, as opposed to snow falling through it. Driven by the
            // live density for the same reason the grade is: snow switched off keeps settling
            // for as long as the last flakes are still in the air.
            SnowAccumulation.Tick(dt, snow, AccumulationEnabled);

            // The buffer thaws on the same clock the scalar does, and follows the camera. It is
            // ticked even with the feature off, so a world that was left snowed still melts.
            if (_splatMap != null)
            {
                float melt = AccumulationEnabled
                    ? SnowAccumulation.MeltPerSecond
                    : 1f / 12f;   // switched off: clear in about a dozen seconds, do not freeze
                _splatMap.Tick(dt, melt);
            }
        }

        // ── zone tracking ────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-read where the player is. Cheap: two field reads off a cached component, and it
        /// only touches the effects when the answer actually changed.
        ///
        /// <c>ZoneManager</c> is found rather than injected because that is how every other
        /// caller in the project reaches it — it is a plain MonoBehaviour with no
        /// ServiceLocator registration. The lookup is cached and retried only while it is null,
        /// since the bootstrap creates it in Start and this may run first.
        /// </summary>
        private void RefreshActiveZone()
        {
            if (_zones == null) _zones = FindObjectOfType<ZoneManager>();
            if (_zones == null) return;   // no zone database: whatever was set by hand stands

            SetActiveZone(_zones.CurrentZone, _zones.IsDetectionSuspended);
        }

        /// <summary>
        /// Point the live effects at a zone.
        ///
        /// Public because the per-frame refresh is not the only legitimate driver: the dev
        /// console drives it, tests drive it with no <c>ZoneManager</c> in the scene, and the
        /// climate system this is the first step of will drive it too. When no ZoneManager
        /// exists the refresh returns early, so a value set here is not clobbered next frame.
        /// </summary>
        public void SetActiveZone(string zoneName, bool indoors = false)
        {
            if (zoneName == null) zoneName = string.Empty;

            if (string.Equals(zoneName, ActiveZone, System.StringComparison.OrdinalIgnoreCase)
                && indoors == IsIndoors)
                return;

            ActiveZone = zoneName;
            IsIndoors  = indoors;
            ApplyActiveZone();
        }

        /// <summary>
        /// Retarget every live effect at what the active zone asks for.
        ///
        /// Note what this does NOT do: it never creates an effect in order to turn it off. An
        /// effect builds four or five ParticleSystems and, for rain and wind, a synthesised
        /// audio clip; walking through a hundred clear zones must not allocate any of that.
        /// </summary>
        private void ApplyActiveZone()
        {
            for (int i = 0; i < TypeCount; i++)
            {
                var type    = (WeatherType)i;
                var desired = IsIndoors ? WeatherIntensity.Off : LevelOf(type);

                if (desired == WeatherIntensity.Off && !_effects.ContainsKey(type)) continue;

                var fx = ResolveOrCreate(type);
                if (fx != null) fx.SetIntensity(desired);
            }
        }

        // ── queries ──────────────────────────────────────────────────────────────────

        /// <summary>The level <paramref name="type"/> is set to in the ACTIVE zone.</summary>
        public WeatherIntensity LevelOf(WeatherType type) => LevelOfZone(ActiveZone, type);

        /// <summary>The level <paramref name="type"/> is set to in a named zone.</summary>
        public WeatherIntensity LevelOfZone(string zoneName, WeatherType type)
        {
            if (string.IsNullOrEmpty(zoneName)) return WeatherIntensity.Off;
            return _byZone.TryGetValue(zoneName, out var row) ? row[(int)type] : WeatherIntensity.Off;
        }

        /// <summary>True iff <paramref name="type"/> is above Off in the active zone.</summary>
        public bool IsActive(WeatherType type) => LevelOf(type) != WeatherIntensity.Off;

        /// <summary>
        /// Live density of <paramref name="type"/>, 0..1 — what is actually being rendered,
        /// level times activation fade. This is what the grade, the wind field and the snow
        /// accumulation are driven from, so all three ramp with the particles instead of
        /// snapping when the player crosses a zone boundary.
        /// </summary>
        public float DensityOf(WeatherType type)
            => _effects.TryGetValue(type, out var fx) && fx != null ? fx.Density : 0f;

        /// <summary>Every zone that has any weather authored, for the console and diagnostics.</summary>
        public List<string> ZonesWithWeather()
        {
            var list = new List<string>();
            foreach (var kv in _byZone)
            {
                for (int i = 0; i < TypeCount; i++)
                {
                    if (kv.Value[i] == WeatherIntensity.Off) continue;
                    list.Add(kv.Key);
                    break;
                }
            }
            list.Sort(System.StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // ── mutation ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Set a weather's level in the ACTIVE zone. This is what the F2 panel and the console
        /// call: turning rain on means turning it on where the player is standing.
        ///
        /// Returns false and changes nothing when no zone has been resolved — writing to an
        /// empty key would author weather into a zone that does not exist and could never be
        /// reached again, and the caller needs to be able to say so rather than report success.
        /// </summary>
        public bool Set(WeatherType type, WeatherIntensity level)
        {
            if (!HasActiveZone) return false;
            SetInZone(ActiveZone, type, level);
            return true;
        }

        /// <summary>Set a weather's level in a named zone, whether or not the player is in it.</summary>
        public void SetInZone(string zoneName, WeatherType type, WeatherIntensity level)
        {
            if (string.IsNullOrEmpty(zoneName)) return;

            if (!_byZone.TryGetValue(zoneName, out var row))
            {
                // Off on a zone with no row is already true; do not allocate one to say so.
                if (level == WeatherIntensity.Off)
                {
                    OnWeatherChanged?.Invoke(zoneName, type, WeatherIntensity.Off);
                    return;
                }
                row = new WeatherIntensity[TypeCount];
                _byZone[zoneName] = row;
            }

            row[(int)type] = level;

            // Only the zone being rendered may move the live effects.
            if (string.Equals(zoneName, ActiveZone, System.StringComparison.OrdinalIgnoreCase))
                ApplyActiveZone();

            OnWeatherChanged?.Invoke(zoneName, type, level);
        }

        /// <summary>
        /// Advance the active zone's weather one step round Off → Light → Medium → Heavy → Off,
        /// and return the new level. This is what a Time &amp; Weather row click does: the panel
        /// used to be a plain toggle, which could only ever ask for one density.
        /// </summary>
        public WeatherIntensity Cycle(WeatherType type)
        {
            var next = LevelOf(type).Next();
            return Set(type, next) ? next : LevelOf(type);
        }

        /// <summary>
        /// Fade out every weather in the ACTIVE zone. Deliberately scoped: the OFF row means
        /// "clear the weather here", which is the only result an author standing in a zone can
        /// actually see. <see cref="ClearEveryZone"/> is the global one.
        /// </summary>
        public void ClearAll()
        {
            if (HasActiveZone) ClearZone(ActiveZone);
        }

        /// <summary>Fade out every weather in a named zone.</summary>
        public void ClearZone(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName) || !_byZone.TryGetValue(zoneName, out var row)) return;

            for (int i = 0; i < TypeCount; i++)
            {
                if (row[i] == WeatherIntensity.Off) continue;
                row[i] = WeatherIntensity.Off;
                OnWeatherChanged?.Invoke(zoneName, (WeatherType)i, WeatherIntensity.Off);
            }

            if (string.Equals(zoneName, ActiveZone, System.StringComparison.OrdinalIgnoreCase))
                ApplyActiveZone();
        }

        /// <summary>Wipe the authored weather of every zone in the world.</summary>
        public void ClearEveryZone()
        {
            var zones = new List<string>(_byZone.Keys);
            for (int i = 0; i < zones.Count; i++) ClearZone(zones[i]);
        }

        // ── construction ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Lazy creation. Each effect owns a bare child GameObject and builds its own depth
        /// slices underneath it — note the object is created with NO ParticleSystem, unlike
        /// the single-system version this replaced: an effect is now a stack of child systems
        /// and a system on the root would be an idle one nothing ever emits from.
        /// </summary>
        private WeatherEffect ResolveOrCreate(WeatherType type)
        {
            if (_effects.TryGetValue(type, out var fx) && fx != null) return fx;

            var go = new GameObject($"Weather_{type}");
            go.transform.SetParent(transform, false);
            switch (type)
            {
                case WeatherType.Wind: fx = go.AddComponent<WindEffect>(); break;
                case WeatherType.Rain: fx = go.AddComponent<RainEffect>(); break;
                case WeatherType.Snow: fx = go.AddComponent<SnowEffect>(); break;
                default:
                    Debug.LogWarning($"[WeatherManager] Unknown weather type {type}.");
                    Destroy(go);
                    return null;
            }
            // AddComponent already ran Awake in Play Mode; this is the belt for anything that
            // reaches the manager outside it, where Unity does not call Awake at all.
            fx.EnsureBuilt();
            _effects[type] = fx;
            return fx;
        }
    }
}
