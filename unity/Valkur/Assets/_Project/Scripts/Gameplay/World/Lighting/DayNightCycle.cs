using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Drives a 24-hour day/night cycle by animating the scene's URP 2D Global Light 2D.
    ///
    /// Python parity (lighting.json):
    ///   • <c>time_scale = 0.4 min/s</c> ⇒ 1 in-game day (1440 min) takes 3600 s (60 min real).
    ///   • <c>min_intensity = 0.2</c>     ⇒ even night never goes fully dark.
    ///   • <c>lights_disable_window</c>  ⇒ point lights are disabled while it is daytime
    ///                                     (Python window: 08:45 → 20:45, normalized 0.365 → 0.865).
    ///   • Smoothstep interpolation between phase keyframes (Python uses t²·(3−2t)).
    ///
    /// Uses reflection to write Light2D.color and Light2D.intensity so that
    /// Valkur.Gameplay.asmdef does NOT need a direct reference to the URP runtime assembly
    /// (consistent with GameplaySceneSetup.EnsureGlobalLight2D approach).
    ///
    /// Phases (normalized day time 0..1):
    ///   Dawn:  0.20 - 0.30  (warm pinkish light, intensity rising)
    ///   Day:   0.30 - 0.70  (full white light, max intensity)
    ///   Dusk:  0.70 - 0.80  (orange tint, intensity falling)
    ///   Night: 0.80 - 0.20  (deep blue, low intensity)
    ///
    /// Fires <see cref="OnPhaseChanged"/> for AudioManager ambient updates and
    /// <see cref="OnLightsEnabledChanged"/> for <see cref="WorldLightLoader"/> when the
    /// daytime "lights off" window is entered or exited.
    /// </summary>
    public class DayNightCycle : SingletonMonoBehaviour<DayNightCycle>
    {
        // 1440 = minutes per 24h day. Used to map normalized time ↔ minute-of-day.
        public const float MinutesPerDay                    = 1440f;
        // Python parity: lights-disable window at 08:45 → 20:45 (525 / 1245 minutes).
        public const float DefaultLightsOffStartNormalized  = 525f  / MinutesPerDay;
        public const float DefaultLightsOffEndNormalized    = 1245f / MinutesPerDay;

        // ── Inspector ─────────────────────────────────────────────────

        [Header("Timing")]
        [Tooltip("Real-world seconds per full in-game day. Python parity: 3600 (60 real min). Set to 0 to pause.")]
        [SerializeField] private float realSecondsPerDay = 3600f;

        [Tooltip("Starting time of day (0=midnight, 0.25=dawn, 0.5=noon, 0.75=dusk).")]
        [SerializeField, Range(0f, 1f)] private float startTimeNormalized = 0.35f;

        [Tooltip("Pause the cycle (useful in dungeons, etc.).")]
        [SerializeField] private bool paused;

        [Tooltip("Minimum global intensity floor — the night phase never goes darker than this. Higher floor keeps gameplay readable; lower floor sells deeper nights. Default 0.30 keeps texture detail visible.")]
        [SerializeField, Range(0f, 1f)] private float minIntensity = 0.30f;

        [Header("Phase palette (cinematic 6-phase model)")]
        // Pro-game color theory: most of the day is **near-neutral white** so
        // the world's actual colors read true. Saturated tints are reserved for
        // *brief* transitions — Golden Hour (warm low sun) and Blue Hour (cool
        // post-sunset). Civil Dawn / Dusk are mostly desaturated (the dramatic
        // sky colors people imagine actually only happen for ~10 in-game min).
        // This avoids the "uniform sepia wash" that screams amateur.
        [Tooltip("Mid-day color. Should sit near pure white with only a hint of warmth so world textures read accurately.")]
        [SerializeField] private Color dayColor            = new Color(0.97f, 0.97f, 0.95f, 1f);
        [Tooltip("Civil-dawn color (cool pre-sunrise). Soft lavender / cool grey — NOT warm orange; orange belongs to Golden Hour.")]
        [SerializeField] private Color dawnColor           = new Color(0.74f, 0.76f, 0.86f, 1f);
        [Tooltip("Golden Hour Morning. Warm honey light when the sun is low above the horizon — the magic photographic light.")]
        [SerializeField] private Color goldenMorningColor  = new Color(1.00f, 0.86f, 0.70f, 1f);
        [Tooltip("Golden Hour Evening. Slightly cooler / coppier than morning — the world is warmer, sky is closer to sunset.")]
        [SerializeField] private Color goldenEveningColor  = new Color(1.00f, 0.78f, 0.58f, 1f);
        [Tooltip("Civil-dusk color. Subtle red-orange after the sun dips below the horizon. Briefer than dramatic skies suggest.")]
        [SerializeField] private Color duskColor           = new Color(0.86f, 0.62f, 0.55f, 1f);
        [Tooltip("Blue Hour. Deep cool indigo right after dusk — the post-sunset photographic 'blue moment'.")]
        [SerializeField] private Color blueHourColor       = new Color(0.45f, 0.52f, 0.78f, 1f);
        [Tooltip("Deep night color. Desaturated navy — keeps texture detail readable instead of the pure-black trap.")]
        [SerializeField] private Color nightColor          = new Color(0.28f, 0.34f, 0.55f, 1f);

        [Tooltip("Mid-day intensity. 1.0 = full Light2D output.")]
        [SerializeField, Range(0f, 1.5f)] private float dayIntensity            = 1.00f;
        [SerializeField, Range(0f, 1.5f)] private float dawnIntensity           = 0.55f;
        [SerializeField, Range(0f, 1.5f)] private float goldenMorningIntensity = 0.85f;
        [SerializeField, Range(0f, 1.5f)] private float goldenEveningIntensity = 0.85f;
        [SerializeField, Range(0f, 1.5f)] private float duskIntensity           = 0.60f;
        [SerializeField, Range(0f, 1.5f)] private float blueHourIntensity      = 0.45f;
        [SerializeField, Range(0f, 1.5f)] private float nightIntensity         = 0.35f;

        [Header("Per-phase warmth (-1 cooler / +1 warmer)")]
        [Tooltip("Color-temperature shift on top of the base color. -1 pulls toward cool blue, +1 toward warm orange. " +
                  "Lets a designer dial the same hue between morning-cold and evening-cold without rotating the base color.")]
        [SerializeField, Range(-1f, 1f)] private float dayWarmth            = 0.05f;
        [SerializeField, Range(-1f, 1f)] private float dawnWarmth           = -0.20f;
        [SerializeField, Range(-1f, 1f)] private float goldenMorningWarmth  = 0.45f;
        [SerializeField, Range(-1f, 1f)] private float goldenEveningWarmth  = 0.55f;
        [SerializeField, Range(-1f, 1f)] private float duskWarmth           = 0.30f;
        [SerializeField, Range(-1f, 1f)] private float blueHourWarmth       = -0.55f;
        [SerializeField, Range(-1f, 1f)] private float nightWarmth          = -0.40f;

        [Header("Per-phase vignette opacity (0..1)")]
        [Tooltip("Per-phase strength of the screen-edge vignette overlay. 0 = no edge tint, 1 = full edge wash. " +
                  "DayNightVignetteOverlay uses this to know how strong its border darkening should be at each phase.")]
        [SerializeField, Range(0f, 1f)] private float dayVignetteAlpha            = 0.05f;
        [SerializeField, Range(0f, 1f)] private float dawnVignetteAlpha           = 0.22f;
        [SerializeField, Range(0f, 1f)] private float goldenMorningVignetteAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float goldenEveningVignetteAlpha = 0.40f;
        [SerializeField, Range(0f, 1f)] private float duskVignetteAlpha           = 0.42f;
        [SerializeField, Range(0f, 1f)] private float blueHourVignetteAlpha      = 0.36f;
        [SerializeField, Range(0f, 1f)] private float nightVignetteAlpha         = 0.30f;

        [Header("Point-light disable window (Python parity)")]
        [Tooltip("When ON, point lights spawned by WorldLightLoader are deactivated during the lights-disable window (FPS optimisation while it is bright outside).")]
        [SerializeField] private bool lightsDisableWindowEnabled = true;

        [Tooltip("Normalized start of the daytime window where point lights stay OFF. Python: 08:45 ⇒ 0.3646.")]
        [SerializeField, Range(0f, 1f)] private float lightsDisableStartNormalized = DefaultLightsOffStartNormalized;

        [Tooltip("Normalized end of the daytime window where point lights stay OFF. Python: 20:45 ⇒ 0.8646.")]
        [SerializeField, Range(0f, 1f)] private float lightsDisableEndNormalized = DefaultLightsOffEndNormalized;

        // ── Public state ──────────────────────────────────────────────

        // Phase enum extended with the cinematic Golden / Blue hour beats.
        // Order kept stable so existing call-sites that only switch on the
        // original four (Day/Dawn/Dusk/Night) continue to compile and behave
        // as before — they fall through to the original semantics via the
        // `_ => Day` defaults sprinkled across consumers.
        public enum DayPhase { Day, Dawn, Dusk, Night, GoldenMorning, GoldenEvening, BlueHour }

        /// <summary>Current normalized day time [0, 1).</summary>
        public float TimeNormalized { get; private set; }

        /// <summary>Current phase.</summary>
        public DayPhase CurrentPhase { get; private set; } = DayPhase.Day;

        /// <summary>The fully-blended Light2D color that <see cref="ComputePhaseAndColor"/>
        /// produced for the current frame, including warmth shift and the
        /// LightingEnabled override. Other systems (vignette, particles) read
        /// this so they don't have to recompute the same lerp twice.</summary>
        public Color CurrentColor { get; private set; } = Color.white;

        /// <summary>Per-phase vignette overlay strength, blended between the two
        /// active phase keyframes the same way <see cref="CurrentColor"/> is.</summary>
        public float CurrentVignetteAlpha { get; private set; } = 0.05f;

        /// <summary>Approximate in-game hour 0-23.</summary>
        public int HourOfDay => Mathf.FloorToInt(TimeNormalized * 24f);

        /// <summary>Approximate minute-of-day 0-1439, useful for HH:MM displays.</summary>
        public int MinuteOfDay => Mathf.FloorToInt(TimeNormalized * MinutesPerDay);

        /// <summary>True iff the lights-disable window is currently ACTIVE (i.e. point lights should be off).</summary>
        public bool LightsDisabledNow { get; private set; }

        /// <summary>True iff point lights should be ON right now (inverse of <see cref="LightsDisabledNow"/>).</summary>
        public bool LightsEnabledNow => !LightsDisabledNow;

        // ── Editor-control surface (used by LightingRuntimeEditor / tests) ──

        public bool  Paused                       { get => paused; set => paused = value; }
        public float RealSecondsPerDay            { get => realSecondsPerDay; set => realSecondsPerDay = Mathf.Max(0f, value); }
        public float MinIntensity                 { get => minIntensity; set { minIntensity = Mathf.Clamp01(value); UpdateLighting(); } }
        public bool  LightsDisableWindowEnabled   { get => lightsDisableWindowEnabled; set { lightsDisableWindowEnabled = value; RecomputeLightsDisabled(); } }
        public float LightsDisableStartNormalized { get => lightsDisableStartNormalized; set { lightsDisableStartNormalized = Mathf.Clamp01(value); RecomputeLightsDisabled(); } }
        public float LightsDisableEndNormalized   { get => lightsDisableEndNormalized; set { lightsDisableEndNormalized = Mathf.Clamp01(value); RecomputeLightsDisabled(); } }

        // ── Per-phase look (5 properties exposed to runtime editors / HUD) ──
        //
        // The HUD's "AJUSTES DE FASE" panel reads/writes these via
        // GetPhaseLook / SetPhaseLook so a player can tweak the cinematic
        // look at runtime. All properties land directly back into the
        // SerializeField storage above so a designer's inspector edits and a
        // player's runtime edits share the same source of truth.

        /// <summary>Live-editable look definition for one phase. The HUD panel
        /// exposes 5 sliders that drive these fields:
        ///   • Color: hue + saturation (Value/Brightness lives in <see cref="intensity"/>).
        ///   • intensity: Light2D output multiplier 0..1.5.
        ///   • warmth: -1 cool / +1 warm color-temperature shift.
        ///   • vignetteAlpha: per-phase screen-edge vignette strength 0..1.
        /// </summary>
        public struct PhaseLook
        {
            public Color color;
            public float intensity;
            public float warmth;
            public float vignetteAlpha;
        }

        /// <summary>Snapshot the live look for <paramref name="phase"/>. Returns
        /// the Day defaults for the catch-all branch so callers get a usable
        /// value even if a brand-new enum entry is added without an update here.</summary>
        public PhaseLook GetPhaseLook(DayPhase phase) => phase switch
        {
            DayPhase.Dawn          => new PhaseLook { color = dawnColor,           intensity = dawnIntensity,           warmth = dawnWarmth,           vignetteAlpha = dawnVignetteAlpha },
            DayPhase.GoldenMorning => new PhaseLook { color = goldenMorningColor,  intensity = goldenMorningIntensity,  warmth = goldenMorningWarmth,  vignetteAlpha = goldenMorningVignetteAlpha },
            DayPhase.GoldenEvening => new PhaseLook { color = goldenEveningColor,  intensity = goldenEveningIntensity,  warmth = goldenEveningWarmth,  vignetteAlpha = goldenEveningVignetteAlpha },
            DayPhase.Dusk          => new PhaseLook { color = duskColor,           intensity = duskIntensity,           warmth = duskWarmth,           vignetteAlpha = duskVignetteAlpha },
            DayPhase.BlueHour      => new PhaseLook { color = blueHourColor,       intensity = blueHourIntensity,       warmth = blueHourWarmth,       vignetteAlpha = blueHourVignetteAlpha },
            DayPhase.Night         => new PhaseLook { color = nightColor,          intensity = nightIntensity,          warmth = nightWarmth,          vignetteAlpha = nightVignetteAlpha },
            _                       => new PhaseLook { color = dayColor,            intensity = dayIntensity,            warmth = dayWarmth,            vignetteAlpha = dayVignetteAlpha },
        };

        /// <summary>Replace the live look for <paramref name="phase"/> and re-apply
        /// the lighting immediately so the user sees the change without waiting
        /// for the next phase boundary.</summary>
        public void SetPhaseLook(DayPhase phase, PhaseLook look)
        {
            switch (phase)
            {
                case DayPhase.Day:
                    dayColor          = look.color;
                    dayIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    dayWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    dayVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.Dawn:
                    dawnColor          = look.color;
                    dawnIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    dawnWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    dawnVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.GoldenMorning:
                    goldenMorningColor          = look.color;
                    goldenMorningIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    goldenMorningWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    goldenMorningVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.GoldenEvening:
                    goldenEveningColor          = look.color;
                    goldenEveningIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    goldenEveningWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    goldenEveningVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.Dusk:
                    duskColor          = look.color;
                    duskIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    duskWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    duskVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.BlueHour:
                    blueHourColor          = look.color;
                    blueHourIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    blueHourWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    blueHourVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.Night:
                    nightColor          = look.color;
                    nightIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    nightWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    nightVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
            }
            UpdateLighting();
        }

        // ── Events ────────────────────────────────────────────────────
        public static System.Action<DayPhase> OnPhaseChanged;
        public static System.Action<bool>     OnLightsEnabledChanged;
        public static System.Action<bool>     OnLightingEnabledChanged;

        // ── Master "tinting on/off" switch ────────────────────────────
        // When OFF the cycle keeps ticking but the Light2D is forced to
        // neutral (white, intensity 1) so the world reads at its native
        // texture colors. The vignette + ambient atmosphere subscribe to
        // this so the player sees a perfectly clean view as if the
        // day/night system wasn't running. Turning it ON re-applies the
        // current phase's tint immediately.
        private bool _lightingEnabled = true;
        public bool LightingEnabled
        {
            get => _lightingEnabled;
            set
            {
                if (_lightingEnabled == value) return;
                _lightingEnabled = value;
                UpdateLighting();
                OnLightingEnabledChanged?.Invoke(value);
            }
        }

        // Domain Reload is OFF in Valkur — static delegates would otherwise
        // carry zombie subscriptions from the previous Play Mode session
        // (typical victim: a destroyed WorldLightLoader whose OnDestroy ran
        // but a third-party subscriber leaked, or a test fixture that
        // forgot to clear). Resetting on SubsystemRegistration guarantees
        // every fresh Play starts with a clean dispatch list.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEventsOnPlayModeEnter()
        {
            OnPhaseChanged           = null;
            OnLightsEnabledChanged   = null;
            OnLightingEnabledChanged = null;
        }

        protected override bool Persist => false;

        // ── Reflection cache ──────────────────────────────────────────

        private Component    _globalLight;
        private PropertyInfo _colorProp;
        private PropertyInfo _intensityProp;
        private bool         _lightResolved;

        // ── Lifecycle ─────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            TimeNormalized = startTimeNormalized;
            // NOTE: Do NOT call ResolveGlobalLight() here — the Global Light 2D may not
            // exist yet at Awake time (GameplaySceneSetup creates it in Start).
            // Resolved lazily on first Update frame instead.
        }

        private void Update()
        {
            // Lazy-resolve the Global Light 2D — by first Update() all Start() calls have run.
            if (!_lightResolved)
            {
                ResolveGlobalLight();
                _lightResolved = true;
            }

            if (!paused && realSecondsPerDay > 0f)
            {
                TimeNormalized = (TimeNormalized + Time.deltaTime / realSecondsPerDay) % 1f;
            }

            // UpdateLighting is cheap enough to call every frame — keeps pause-then-scrub
            // and Inspector edits in sync without needing dirty flags.
            UpdateLighting();
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Set the time of day directly. 0=midnight, 0.5=noon.</summary>
        public void SetTimeNormalized(float t)
        {
            TimeNormalized = Mathf.Repeat(t, 1f);
            UpdateLighting();
        }

        /// <summary>Set the time of day from a 0-1439 minute-of-day value.</summary>
        public void SetMinuteOfDay(int minute)
            => SetTimeNormalized(Mathf.Repeat(minute, MinutesPerDay) / MinutesPerDay);

        /// <summary>Advance / rewind the clock by a delta in normalized units.</summary>
        public void AdvanceNormalized(float delta) => SetTimeNormalized(TimeNormalized + delta);

        /// <summary>Advance / rewind the clock by a number of in-game minutes.</summary>
        public void AdvanceMinutes(float minutes) => AdvanceNormalized(minutes / MinutesPerDay);

        public void Pause()  => paused = true;
        public void Resume() => paused = false;

        // ── Internal ──────────────────────────────────────────────────

        private void ResolveGlobalLight()
        {
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType == null)
            {
                Debug.LogWarning("[DayNightCycle] Light2D type not found — URP 2D Renderer missing.");
                return;
            }

            // Find all Light2D components; pick the first with lightType == Global (1)
            var all = FindObjectsOfType(light2DType);
            var ltProp = light2DType.GetProperty("lightType",
                BindingFlags.Public | BindingFlags.Instance);

            foreach (Component c in all)
            {
                if (ltProp == null) { _globalLight = c; break; }
                try
                {
                    int val = System.Convert.ToInt32(ltProp.GetValue(c));
                    if (val == 1) { _globalLight = c; break; } // 1 = Global
                }
                catch { }
            }

            if (_globalLight == null && all.Length > 0)
                _globalLight = all[0] as Component;

            if (_globalLight == null)
            {
                Debug.LogWarning("[DayNightCycle] No Light2D found in scene.");
                return;
            }

            _colorProp     = light2DType.GetProperty("color",     BindingFlags.Public | BindingFlags.Instance);
            _intensityProp = light2DType.GetProperty("intensity", BindingFlags.Public | BindingFlags.Instance);
        }

        private void UpdateLighting()
        {
            // Compute phase + visuals from time alone — independent of the
            // Light2D so EditMode tests (and any system observing CurrentPhase
            // without rendering, e.g. spawners that key on day/night) get a
            // consistent state machine even when no URP renderer is wired.
            ComputePhaseAndColor(TimeNormalized,
                out var newPhase, out var targetColor, out var targetIntensity);

            // Master "tinting off" override: force the Light2D to neutral
            // white at intensity 1 so the world reads at native colors. The
            // computed phase / color values still flow through the OnPhaseChanged
            // event so the HUD clock and ambient atmosphere keep their state in
            // sync — only the Light2D side-effect is suppressed.
            if (!_lightingEnabled)
            {
                targetColor     = Color.white;
                targetIntensity = 1f;
            }

            // Apply the visual side-effect only when a Light2D is reachable.
            if (_globalLight != null)
            {
                try
                {
                    _colorProp?.SetValue(_globalLight, targetColor);
                    _intensityProp?.SetValue(_globalLight, targetIntensity);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[DayNightCycle] Failed to set Light2D properties: {ex.Message}");
                }
            }

            if (newPhase != CurrentPhase)
            {
                CurrentPhase = newPhase;
                OnPhaseChanged?.Invoke(newPhase);
            }

            RecomputeLightsDisabled();
        }

        // Phase boundaries (normalized 0..1, mapped from a 24h day):
        //   Night:           0.84 → 0.18  (wraps midnight)
        //   Dawn (civil):    0.18 → 0.23  ~  04:19 → 05:31
        //   Golden Morning:  0.23 → 0.30  ~  05:31 → 07:12
        //   Day:             0.30 → 0.66  ~  07:12 → 15:50
        //   Golden Evening:  0.66 → 0.74  ~  15:50 → 17:46
        //   Dusk (civil):    0.74 → 0.79  ~  17:46 → 18:58
        //   Blue Hour:       0.79 → 0.84  ~  18:58 → 20:10
        // The two "Golden" windows are intentionally short — that is the
        // photographic reality and reading the warm tint as *transient* is
        // what makes the world feel cinematic rather than uniformly tinted.
        private const float DAWN_START   = 0.18f;
        private const float GOLD_M_START = 0.23f;
        private const float DAY_START    = 0.30f;
        private const float GOLD_E_START = 0.66f;
        private const float DUSK_START   = 0.74f;
        private const float BLUE_START   = 0.79f;
        private const float NIGHT_START  = 0.84f;

        // Pure function: classify the normalized time t into a DayPhase and
        // pick the matching color + intensity. No side-effects, no Light2D
        // access — exposed via UpdateLighting and exercised directly by
        // EditMode tests. Uses Mathf.SmoothStep (t²·(3−2t)) between phase
        // keyframes for parity with Python's smoothstep ramp.
        private void ComputePhaseAndColor(float t,
                                          out DayPhase phase,
                                          out Color color,
                                          out float intensity)
        {
            if (t >= DAWN_START && t < GOLD_M_START)
            {
                float k   = Mathf.SmoothStep(0f, 1f, (t - DAWN_START) / (GOLD_M_START - DAWN_START));
                color     = Color.Lerp(dawnColor, goldenMorningColor, k);
                intensity = Mathf.Lerp(dawnIntensity, goldenMorningIntensity, k);
                phase     = k < 0.5f ? DayPhase.Dawn : DayPhase.GoldenMorning;
            }
            else if (t >= GOLD_M_START && t < DAY_START)
            {
                float k   = Mathf.SmoothStep(0f, 1f, (t - GOLD_M_START) / (DAY_START - GOLD_M_START));
                color     = Color.Lerp(goldenMorningColor, dayColor, k);
                intensity = Mathf.Lerp(goldenMorningIntensity, dayIntensity, k);
                phase     = k < 0.5f ? DayPhase.GoldenMorning : DayPhase.Day;
            }
            else if (t >= DAY_START && t < GOLD_E_START)
            {
                color     = dayColor;
                intensity = dayIntensity;
                phase     = DayPhase.Day;
            }
            else if (t >= GOLD_E_START && t < DUSK_START)
            {
                float k   = Mathf.SmoothStep(0f, 1f, (t - GOLD_E_START) / (DUSK_START - GOLD_E_START));
                color     = Color.Lerp(dayColor, goldenEveningColor, k);
                intensity = Mathf.Lerp(dayIntensity, goldenEveningIntensity, k);
                phase     = k < 0.5f ? DayPhase.Day : DayPhase.GoldenEvening;
            }
            else if (t >= DUSK_START && t < BLUE_START)
            {
                float k   = Mathf.SmoothStep(0f, 1f, (t - DUSK_START) / (BLUE_START - DUSK_START));
                color     = Color.Lerp(goldenEveningColor, duskColor, k);
                intensity = Mathf.Lerp(goldenEveningIntensity, duskIntensity, k);
                phase     = k < 0.5f ? DayPhase.GoldenEvening : DayPhase.Dusk;
            }
            else if (t >= BLUE_START && t < NIGHT_START)
            {
                float k   = Mathf.SmoothStep(0f, 1f, (t - BLUE_START) / (NIGHT_START - BLUE_START));
                color     = Color.Lerp(duskColor, blueHourColor, k);
                intensity = Mathf.Lerp(duskIntensity, blueHourIntensity, k);
                phase     = k < 0.5f ? DayPhase.Dusk : DayPhase.BlueHour;
            }
            else
            {
                // Night wraps midnight — covers [NIGHT_START, 1) ∪ [0, DAWN_START).
                // Inside this window we ease from BlueHour → Night → BlueHour again
                // so the wrap doesn't snap to a hard color edge.
                color     = nightColor;
                intensity = nightIntensity;
                phase     = DayPhase.Night;
            }

            // Apply the Python-parity floor so we never go fully black.
            intensity = Mathf.Max(intensity, minIntensity);
        }

        // Recompute LightsDisabledNow and fire the change event when it flips.
        // Handles both wrap-around and non-wrap windows — start may exceed end if
        // a designer ever flips the window into the night side.
        private void RecomputeLightsDisabled()
        {
            bool disabledNow = false;
            if (lightsDisableWindowEnabled)
            {
                float a = lightsDisableStartNormalized;
                float b = lightsDisableEndNormalized;
                if (Mathf.Approximately(a, b))
                {
                    disabledNow = false;
                }
                else if (a < b)
                {
                    disabledNow = TimeNormalized >= a && TimeNormalized < b;
                }
                else
                {
                    // Wraparound: e.g. 0.9 → 0.1 means active in [0.9,1) ∪ [0,0.1).
                    disabledNow = TimeNormalized >= a || TimeNormalized < b;
                }
            }
            if (disabledNow != LightsDisabledNow)
            {
                LightsDisabledNow = disabledNow;
                OnLightsEnabledChanged?.Invoke(!disabledNow);
            }
        }
    }
}
