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

        [Tooltip("Minimum global intensity floor — the night never goes darker than this. " +
                  "Default 0.08 keeps deep night nearly black so manually-placed point lights / torches are the dominant light source, matching how the Python original played.")]
        [SerializeField, Range(0f, 1f)] private float minIntensity = 0.08f;

        [Header("Day / Night keyframes (only two, like Python)")]
        // Two real keyframes — Day and Night — that the cycle smoothly lerps
        // between during the Dawn and Dusk windows. There are no separate
        // keyframes for Dawn / Dusk: those windows are pure transitions.
        //
        // Day defaults to **literally white at intensity 1.0**, which is the
        // identity tint for URP 2D Light2D — multiplying any pixel by white·1
        // returns the original color. So during the Day band the world reads
        // at its native texture colors, no filter, no wash.
        //
        // Night defaults to a **dark cool blue at intensity 0.15**, so the
        // ambient is genuinely dim. Manually-placed point lights (torches /
        // lamps via the Lighting Editor Ctrl+F3 / WorldLightLoader) are the
        // dominant light source at night — that's the gameplay loop the
        // Python original was built around.
        [Tooltip("Day keyframe — colour. Pure white = identity (no tint applied to the world).")]
        [SerializeField] private Color dayColor   = new Color(1.00f, 1.00f, 1.00f, 1f);
        [Tooltip("Day keyframe — Light2D intensity. 1.0 = no darkening.")]
        [SerializeField, Range(0f, 1.5f)] private float dayIntensity   = 1.00f;
        [Tooltip("Day keyframe — color-temperature shift. 0 = neutral.")]
        [SerializeField, Range(-1f, 1f)] private float dayWarmth       = 0.00f;
        [Tooltip("Day keyframe — vignette opacity. 0 = no edge tint.")]
        [SerializeField, Range(0f, 1f)] private float dayVignetteAlpha = 0.00f;

        [Tooltip("Night keyframe — colour. Dark cool blue: world reads as moonlit, point lights stand out.")]
        [SerializeField] private Color nightColor   = new Color(0.20f, 0.25f, 0.45f, 1f);
        [Tooltip("Night keyframe — Light2D intensity. Low (≈0.15) so the world is genuinely dim.")]
        [SerializeField, Range(0f, 1.5f)] private float nightIntensity = 0.15f;
        [Tooltip("Night keyframe — color-temperature shift. Slightly cooler.")]
        [SerializeField, Range(-1f, 1f)] private float nightWarmth     = -0.10f;
        [Tooltip("Night keyframe — vignette opacity. Stronger so the screen edges feel enclosed.")]
        [SerializeField, Range(0f, 1f)] private float nightVignetteAlpha = 0.30f;

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

        /// <summary>Snapshot the live look for <paramref name="phase"/>. Only
        /// Day and Night are real keyframes; Dawn/Dusk return the average of
        /// the two (because the cycle's transition windows lerp between them
        /// rather than reading a third keyframe). Legacy GoldenMorning /
        /// GoldenEvening / BlueHour map to their nearest neighbour for
        /// back-compat with old call sites.</summary>
        public PhaseLook GetPhaseLook(DayPhase phase) => phase switch
        {
            DayPhase.Night         => new PhaseLook { color = nightColor, intensity = nightIntensity, warmth = nightWarmth, vignetteAlpha = nightVignetteAlpha },
            DayPhase.Dawn          => AverageLook(),
            DayPhase.Dusk          => AverageLook(),
            DayPhase.GoldenMorning => new PhaseLook { color = dayColor,   intensity = dayIntensity,   warmth = dayWarmth,   vignetteAlpha = dayVignetteAlpha },
            DayPhase.GoldenEvening => AverageLook(),
            DayPhase.BlueHour      => new PhaseLook { color = nightColor, intensity = nightIntensity, warmth = nightWarmth, vignetteAlpha = nightVignetteAlpha },
            _                       => new PhaseLook { color = dayColor,   intensity = dayIntensity,   warmth = dayWarmth,   vignetteAlpha = dayVignetteAlpha },
        };

        // Helper: midpoint between Day and Night, used when callers ask for
        // the "look" of a transition phase. Avoids exposing a third stored
        // keyframe that would only ever be a derived value.
        private PhaseLook AverageLook() => new PhaseLook
        {
            color         = Color.Lerp(nightColor, dayColor, 0.5f),
            intensity     = Mathf.Lerp(nightIntensity, dayIntensity, 0.5f),
            warmth        = Mathf.Lerp(nightWarmth, dayWarmth, 0.5f),
            vignetteAlpha = Mathf.Lerp(nightVignetteAlpha, dayVignetteAlpha, 0.5f),
        };

        /// <summary>Replace the live look for <paramref name="phase"/> and re-apply
        /// the lighting immediately. Only Day and Night are stored keyframes;
        /// writes to Dawn/Dusk route to the nearest neighbour (so a UI that
        /// still targets them produces a visible change). Legacy
        /// GoldenMorning / GoldenEvening / BlueHour map similarly.</summary>
        public void SetPhaseLook(DayPhase phase, PhaseLook look)
        {
            switch (phase)
            {
                case DayPhase.Day:
                case DayPhase.Dawn:               // Dawn writes route to Day (the keyframe Dawn lerps toward)
                case DayPhase.GoldenMorning:
                    dayColor          = look.color;
                    dayIntensity      = Mathf.Clamp(look.intensity, 0f, 1.5f);
                    dayWarmth         = Mathf.Clamp(look.warmth, -1f, 1f);
                    dayVignetteAlpha  = Mathf.Clamp01(look.vignetteAlpha);
                    break;
                case DayPhase.Night:
                case DayPhase.Dusk:               // Dusk writes route to Night (the keyframe Dusk lerps toward)
                case DayPhase.GoldenEvening:
                case DayPhase.BlueHour:
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
                out var newPhase, out var targetColor, out var targetIntensity,
                out _, out var targetVignetteAlpha);

            // Master "tinting off" override: force the Light2D to neutral
            // white at intensity 1 so the world reads at native colors. The
            // computed phase / color values still flow through the OnPhaseChanged
            // event so the HUD clock and ambient atmosphere keep their state in
            // sync — only the Light2D side-effect (and the vignette) are silenced.
            if (!_lightingEnabled)
            {
                targetColor          = Color.white;
                targetIntensity      = 1f;
                targetVignetteAlpha  = 0f;
            }

            // Publish the live values so the vignette / ambient particles can
            // read them without recomputing the same blend.
            CurrentColor         = targetColor;
            CurrentVignetteAlpha = targetVignetteAlpha;

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
        //   Night:  0.84 → 0.18  (wraps midnight)
        //   Dawn:   0.18 → 0.30  ~  04:19 → 07:12   transition night→day
        //   Day:    0.30 → 0.70  ~  07:12 → 16:48   pure white
        //   Dusk:   0.70 → 0.84  ~  16:48 → 20:10   transition day→night
        //
        // Matches Python's effective 4-phase model (only Night and Day are
        // real keyframes; Dawn/Dusk are smoothstep transitions between them).
        // Long stable Night and Day bands keep the world's tint *constant*
        // for most of the cycle, so the player isn't constantly perceiving
        // color drift — that was the "cinematic 6-phase" model's main flaw.
        private const float DAWN_START  = 0.18f;
        private const float DAY_START   = 0.30f;
        private const float DUSK_START  = 0.70f;
        private const float NIGHT_START = 0.84f;

        // Pure function: classify the normalized time t into a DayPhase and
        // pick the matching color + intensity + warmth + vignetteAlpha. No
        // side-effects, no Light2D access — exposed via UpdateLighting and
        // exercised directly by EditMode tests. Uses Mathf.SmoothStep
        // (t²·(3−2t)) between phase keyframes for parity with Python's
        // smoothstep ramp.
        //
        // Two-keyframe model (matches Python's lighting.json original):
        //   • Day band: pure Day values, no transition
        //   • Night band: pure Night values, no transition
        //   • Dawn band: smooth Night → Day lerp
        //   • Dusk band: smooth Day → Night lerp
        // The DayPhase enum's Dawn / Dusk values are returned for HUD label
        // purposes only — the colors are pure interpolations between the
        // two real keyframes. Legacy GoldenMorning / GoldenEvening / BlueHour
        // values are never produced.
        private void ComputePhaseAndColor(float t,
                                          out DayPhase phase,
                                          out Color color,
                                          out float intensity,
                                          out float warmth,
                                          out float vignetteAlpha)
        {
            if (t >= DAWN_START && t < DAY_START)
            {
                float k       = Mathf.SmoothStep(0f, 1f, (t - DAWN_START) / (DAY_START - DAWN_START));
                color         = Color.Lerp(nightColor, dayColor, k);
                intensity     = Mathf.Lerp(nightIntensity, dayIntensity, k);
                warmth        = Mathf.Lerp(nightWarmth, dayWarmth, k);
                vignetteAlpha = Mathf.Lerp(nightVignetteAlpha, dayVignetteAlpha, k);
                phase         = DayPhase.Dawn;
            }
            else if (t >= DAY_START && t < DUSK_START)
            {
                color         = dayColor;
                intensity     = dayIntensity;
                warmth        = dayWarmth;
                vignetteAlpha = dayVignetteAlpha;
                phase         = DayPhase.Day;
            }
            else if (t >= DUSK_START && t < NIGHT_START)
            {
                float k       = Mathf.SmoothStep(0f, 1f, (t - DUSK_START) / (NIGHT_START - DUSK_START));
                color         = Color.Lerp(dayColor, nightColor, k);
                intensity     = Mathf.Lerp(dayIntensity, nightIntensity, k);
                warmth        = Mathf.Lerp(dayWarmth, nightWarmth, k);
                vignetteAlpha = Mathf.Lerp(dayVignetteAlpha, nightVignetteAlpha, k);
                phase         = DayPhase.Dusk;
            }
            else
            {
                // Night wraps midnight — covers [NIGHT_START, 1) ∪ [0, DAWN_START).
                color         = nightColor;
                intensity     = nightIntensity;
                warmth        = nightWarmth;
                vignetteAlpha = nightVignetteAlpha;
                phase         = DayPhase.Night;
            }

            // Apply the warmth temperature shift to the blended color: positive
            // warmth pushes red up + blue down (toward orange), negative warmth
            // does the inverse (toward blue). The 0.18 magnitude keeps the
            // shift visible without overpowering the base hue.
            color = ApplyWarmth(color, warmth);

            // Apply the floor so we never go fully black even at deep night.
            intensity = Mathf.Max(intensity, minIntensity);
        }

        private static Color ApplyWarmth(Color c, float warmth)
        {
            const float STRENGTH = 0.18f;
            return new Color(
                Mathf.Clamp01(c.r + warmth * STRENGTH),
                c.g,
                Mathf.Clamp01(c.b - warmth * STRENGTH),
                c.a);
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
