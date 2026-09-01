using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Data;

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
    /// Writes Light2D.color and Light2D.intensity through the typed URP API.
    /// Valkur.Gameplay.asmdef already references Unity.RenderPipelines.Universal.Runtime,
    /// so the reflection this class used to carry bought nothing and cost correctness
    /// (it bound to the wrong lightType constant). See
    /// .github/DAY_NIGHT_AUDIT_AND_ROADMAP.md.
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
        /// <summary>
        /// Window during which placed point lights (torches, lamps) stay OFF.
        ///
        /// Derived from the phase bands rather than from Python's 08:45 → 20:45 literals.
        /// Those literals did not line up with the bands: night started at NIGHT_START
        /// (20:10) while the lights only came on at 20:45, so for ~35 in-game minutes —
        /// 88 real seconds at the default day length — the world sat at full night ambient
        /// with every torch deactivated. Tying the window to DAY_START / DUSK_START also
        /// means torches light up as dusk begins, which is when a torch is for.
        /// </summary>
        public const float DefaultLightsOffStartNormalized  = DAY_START;
        public const float DefaultLightsOffEndNormalized    = DUSK_START;

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

        [Header("Look")]
        [Tooltip("Authored 24-hour ramp. When assigned (or found at Resources/DayNightProfile), " +
                  "it is the source of truth for colour, intensity and vignette; the two keyframes " +
                  "below are only the fallback for a scene that has no profile.")]
        [SerializeField] private DayNightProfile profile;

        [Header("Fallback keyframes (used only when no profile is available)")]
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
        [Tooltip("Night keyframe — color-temperature shift. Neutral by default: with a profile " +
                  "assigned the gradient already carries the colour, and a non-zero warmth here " +
                  "would tint it a second time.")]
        [SerializeField, Range(-1f, 1f)] private float nightWarmth     = 0f;
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
        public PhaseLook GetPhaseLook(DayPhase phase)
        {
            var look = ActiveProfile;
            if (look != null)
            {
                bool night = phase is DayPhase.Night or DayPhase.Dusk or DayPhase.GoldenEvening or DayPhase.BlueHour;
                look.ReadPlateau(night, out var c, out var i, out var v);
                return new PhaseLook
                {
                    color         = c,
                    intensity     = i,
                    warmth        = night ? nightWarmth : dayWarmth,
                    vignetteAlpha = v,
                };
            }
            return GetFallbackPhaseLook(phase);
        }

        private PhaseLook GetFallbackPhaseLook(DayPhase phase) => phase switch
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
            var profileLook = ActiveProfile;
            if (profileLook != null)
            {
                // The profile is the source of truth, so the sliders edit IT — otherwise they
                // would write to fields nothing reads any more, which is exactly the kind of
                // control that silently does nothing. Note this mutates the asset in memory:
                // in the Editor that survives a domain reload but is not written to disk until
                // the asset is saved (persisting authoring is Phase 6 of the roadmap).
                bool night = phase is DayPhase.Night or DayPhase.Dusk or DayPhase.GoldenEvening or DayPhase.BlueHour;
                profileLook.WritePlateau(night,
                                          look.color,
                                          Mathf.Clamp(look.intensity, 0f, 1.5f),
                                          Mathf.Clamp01(look.vignetteAlpha));
                if (night) nightWarmth = Mathf.Clamp(look.warmth, -1f, 1f);
                else       dayWarmth   = Mathf.Clamp(look.warmth, -1f, 1f);
                UpdateLighting();
                return;
            }

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

        /// <summary>Fires when the clock rolls past midnight, carrying the new <see cref="DayCount"/>.</summary>
        public static System.Action<int>      OnDayChanged;

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
            OnDayChanged             = null;
        }

        protected override bool Persist => false;

        // ── Light binding ─────────────────────────────────────────────

        private Light2D _globalLight;
        private bool    _lightResolved;

        private DayNightProfile _resolvedProfile;
        private bool            _profileResolved;

        /// <summary>
        /// The authored ramp, or null when none is available and the two fallback keyframes
        /// should drive the cycle instead. Resolved once: an inspector reference wins, then
        /// <c>Resources/DayNightProfile</c>.
        /// </summary>
        private DayNightProfile ActiveProfile
        {
            get
            {
                if (_profileResolved) return _resolvedProfile;
                _profileResolved = true;

                var candidate = profile != null ? profile : Resources.Load<DayNightProfile>("DayNightProfile");
                if (candidate == null)
                {
                    Debug.LogWarning("[DayNightCycle] No DayNightProfile found — falling back to the two " +
                                      "built-in keyframes, which cannot produce a warm dawn or dusk.");
                }
                else if (!candidate.IsUsable)
                {
                    Debug.LogWarning($"[DayNightCycle] DayNightProfile '{candidate.name}' has no usable ramp " +
                                      "(needs at least 2 gradient keys and 2 intensity keys) — using the fallback keyframes.");
                    candidate = null;
                }
                _resolvedProfile = candidate;
                return _resolvedProfile;
            }
        }

        // Last values pushed to the Light2D. Day and Night are flat bands — together 74 %
        // of the cycle — so most frames write a value identical to the previous one. The
        // guard skips those; without it the cycle wrote a Color and a float every frame
        // forever, including while paused.
        private Color _lastAppliedColor     = Color.clear;
        private float _lastAppliedIntensity = float.NaN;

        /// <summary>
        /// Elapsed days since the run started, fractional. THE clock — <see cref="TimeNormalized"/>
        /// and <see cref="DayCount"/> are both projections of it.
        ///
        /// A double, and accumulated in NORMALIZED units rather than seconds, for two separate
        /// reasons. Double because a float nudged by ~0.0000046 per frame loses its low bits within
        /// an hour of play, so the clock slowly stops being the same clock. Normalized because
        /// accumulating raw seconds and dividing by realSecondsPerDay at read time would make the
        /// speed slider TELEPORT the sun: changing the divisor would rewrite the whole history.
        /// Accumulating the rate instead means a speed change only affects what comes after it.
        /// </summary>
        private double _elapsedDays;

        // ── Lifecycle ─────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            _elapsedDays = Mathf.Repeat(startTimeNormalized, 1f);
            SyncClockFromElapsed();
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
                _elapsedDays += (double)Time.deltaTime / realSecondsPerDay;
                SyncClockFromElapsed();
            }

            // UpdateLighting is cheap enough to call every frame — keeps pause-then-scrub
            // and Inspector edits in sync without needing dirty flags.
            UpdateLighting();
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Set the time of day directly. 0=midnight, 0.5=noon.</summary>
        public void SetTimeNormalized(float t)
        {
            // Rewrite the accumulator, not just its projection: setting only TimeNormalized would
            // be undone by the very next frame, which re-derives it from _elapsedDays.
            _elapsedDays = System.Math.Floor(_elapsedDays) + Mathf.Repeat(t, 1f);
            SyncClockFromElapsed();
            UpdateLighting();
        }

        /// <summary>Set the time of day from a 0-1439 minute-of-day value.</summary>
        public void SetMinuteOfDay(int minute)
            => SetTimeNormalized(Mathf.Repeat(minute, MinutesPerDay) / MinutesPerDay);

        /// <summary>
        /// Advance / rewind the clock by a delta in normalized units. Unlike
        /// <see cref="SetTimeNormalized"/> this crosses midnight properly, so advancing past the end
        /// of a day increments <see cref="DayCount"/>.
        /// </summary>
        public void AdvanceNormalized(float delta)
        {
            _elapsedDays += delta;
            if (_elapsedDays < 0d) _elapsedDays = 0d;
            SyncClockFromElapsed();
            UpdateLighting();
        }

        /// <summary>Advance / rewind the clock by a number of in-game minutes.</summary>
        public void AdvanceMinutes(float minutes) => AdvanceNormalized(minutes / MinutesPerDay);

        public void Pause()  => paused = true;
        public void Resume() => paused = false;

        /// <summary>Whole in-game days elapsed since the run began. Day 0 is the first.</summary>
        public int DayCount { get; private set; }

        /// <summary>
        /// Fractional days elapsed — the raw clock. Persisted by the save system so a reloaded run
        /// resumes at the hour AND the day it left off instead of restarting at 08:24.
        /// </summary>
        public double ElapsedDays => _elapsedDays;

        /// <summary>Restore the clock wholesale, e.g. from a save.</summary>
        public void SetElapsedDays(double elapsedDays)
        {
            _elapsedDays = elapsedDays < 0d ? 0d : elapsedDays;
            SyncClockFromElapsed();
            UpdateLighting();
        }

        /// <summary>Project <see cref="_elapsedDays"/> onto the time of day and the day counter.</summary>
        private void SyncClockFromElapsed()
        {
            double whole = System.Math.Floor(_elapsedDays);
            TimeNormalized = (float)(_elapsedDays - whole);

            int day = (int)whole;
            if (day == DayCount) return;
            DayCount = day;
            OnDayChanged?.Invoke(day);
        }

        // ── Internal ──────────────────────────────────────────────────

        /// <summary>
        /// Binds to the scene's Global Light2D — and ONLY to a Global one.
        ///
        /// The previous version matched <c>lightType == 1</c> commented as "1 = Global".
        /// URP 14's enum is <c>Parametric=0, Freeform=1, Sprite=2, Point=3, Global=4</c>,
        /// so it never matched and fell through to <c>all[0]</c> — an arbitrary light, in
        /// practice a torch. There is no fallback here on purpose: binding to a point light
        /// makes the cycle silently drive the wrong object, which is exactly the failure
        /// that hid this bug for months. A missing Global light is a loud warning instead.
        /// </summary>
        private void ResolveGlobalLight()
        {
            foreach (var l in FindObjectsOfType<Light2D>())
            {
                if (l.lightType != Light2D.LightType.Global) continue;
                _globalLight = l;
                return;
            }

            Debug.LogWarning(
                "[DayNightCycle] No Global Light2D in the scene — the day/night tint will not " +
                "reach any pixel. GameplaySceneSetup.EnsureGlobalLight2D should have created one.");
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

            // Storm lightning. Folded into the GLOBAL LIGHT, not only into the screen grade,
            // because a strike has to light the world — buildings, entities, the tilemap —
            // and a grade-only flash brightens every pixel by the same amount, which reads as
            // an exposure change rather than as something happening in the sky. It survives
            // the "no filter" override above on purpose: the switch silences the day/night
            // TINT, and a strike is an event, not a tint.
            float flash = Weather.WeatherGrade.LightFlash01;
            if (flash > 0.0001f)
            {
                targetColor     = Color.Lerp(targetColor, Weather.WeatherGrade.FlashColor, flash * 0.85f);
                targetIntensity = Mathf.Min(2f, targetIntensity + flash * Weather.WeatherGrade.FlashLightBoost);
            }

            PublishScreenGrade(targetVignetteAlpha);

            // Publish the live values so the vignette / ambient particles can
            // read them without recomputing the same blend.
            CurrentColor         = targetColor;
            CurrentVignetteAlpha = targetVignetteAlpha;

            // Apply the visual side-effect only when a Light2D is reachable.
            if (_globalLight != null &&
                (targetColor != _lastAppliedColor || !Mathf.Approximately(targetIntensity, _lastAppliedIntensity)))
            {
                _globalLight.color     = targetColor;
                _globalLight.intensity = targetIntensity;
                _lastAppliedColor      = targetColor;
                _lastAppliedIntensity  = targetIntensity;
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
        /// <summary>Start of the dawn ramp — night begins turning into day (~04:19).</summary>
        public const float DAWN_START  = 0.18f;
        /// <summary>Start of the flat day band (~07:12).</summary>
        public const float DAY_START   = 0.30f;
        /// <summary>Start of the dusk ramp — day begins turning into night (~16:48).</summary>
        public const float DUSK_START  = 0.70f;
        /// <summary>Start of the flat night band (~20:10), wraps midnight.</summary>
        public const float NIGHT_START = 0.84f;

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
            var look = ActiveProfile;
            if (look != null)
            {
                // Profile path: the gradient IS the colour. Every beat the two-keyframe model
                // could not express — the warm dawn, the golden hour, the mauve sunset — lives
                // in its 8 keys. Bands come from the profile too, so an author who moves dusk
                // moves the label and the ramp together.
                phase = ClassifyPhase(t, look.DawnStart, look.DayStart, look.DuskStart, look.NightStart);
                look.Sample(t, out color, out intensity, out vignetteAlpha);
                warmth = phase == DayPhase.Night ? nightWarmth : dayWarmth;
            }
            else if (t >= DAWN_START && t < DAY_START)
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

        /// <summary>
        /// Hand the full-screen grade its live values.
        ///
        /// This is the half of the day/night look a Multiply Light2D structurally cannot do:
        /// draining saturation, recontrasting what the darkening flattened, and dithering a dim
        /// frame that has few code values left to land on. The light gets the colour; the grade
        /// gets everything the colour cannot express.
        ///
        /// Writes to a static in Valkur.Core because the renderer feature must live there —
        /// Gameplay may reference Core, never the other way round.
        /// </summary>
        private void PublishScreenGrade(float vignetteAlpha)
        {
            // Lift / gamma / gain are written here and nowhere else, and the day/night look
            // does not use them — it is expressed as saturation, contrast and vignette. The
            // weather owns them outright (overcast lift, the cool cast of rain, the lightning
            // punch), so they are handed over verbatim, before the early returns: leaving a
            // storm's gain behind when the grade is switched off would be a stale value that
            // reappears the moment it is switched back on.
            Valkur.Core.Rendering.ScreenGradeSettings.Gain         = Weather.WeatherGrade.Gain;
            Valkur.Core.Rendering.ScreenGradeSettings.Lift         = Weather.WeatherGrade.Lift;
            Valkur.Core.Rendering.ScreenGradeSettings.InverseGamma = Vector3.one;

            var look = ActiveProfile;
            if (look == null)
            {
                // No profile: no grade. The fallback keyframes describe a light, not a look.
                Valkur.Core.Rendering.ScreenGradeSettings.Enabled = false;
                return;
            }

            if (!_lightingEnabled)
            {
                // The F2 "no filter" switch has to silence the grade too, or turning the tint off
                // would leave the world desaturated with no visible cause.
                Valkur.Core.Rendering.ScreenGradeSettings.Enabled = false;
                return;
            }

            look.SampleGrade(TimeNormalized, out float saturation, out float contrast);

            // Weather MODIFIES what the cycle computed rather than replacing it — a downpour
            // at noon and a downpour at dusk are both desaturated, each relative to its own
            // hour. Two writers to one absolute value would let whichever ran last win.
            saturation *= Weather.WeatherGrade.SaturationMultiplier;

            Valkur.Core.Rendering.ScreenGradeSettings.Enabled            = true;
            Valkur.Core.Rendering.ScreenGradeSettings.Saturation         = saturation;
            Valkur.Core.Rendering.ScreenGradeSettings.Contrast           = contrast;
            Valkur.Core.Rendering.ScreenGradeSettings.VignetteIntensity  =
                vignetteAlpha * VignetteIntensityScale + Weather.WeatherGrade.VignetteAdd;
            Valkur.Core.Rendering.ScreenGradeSettings.VignetteSmoothness = VignetteSmoothness;
            Valkur.Core.Rendering.ScreenGradeSettings.VignetteColor      = look.VignetteTint;
        }

        /// <summary>
        /// Maps the authored 0..1 vignette alpha onto the shader's falloff term. The overlay it
        /// replaces was an alpha over a fixed sprite; the shader's intensity is a radius scale, so
        /// the two are not the same number.
        /// </summary>
        private const float VignetteIntensityScale = 2.2f;

        /// <summary>Falloff exponent of the screen vignette. Higher is a tighter edge.</summary>
        private const float VignetteSmoothness = 1.6f;

        /// <summary>
        /// Pure band classification. Night wraps midnight, so it is the "everything else" case
        /// rather than a range test.
        /// </summary>
        private static DayPhase ClassifyPhase(float t, float dawn, float day, float dusk, float night)
        {
            if (t >= dawn && t < day)   return DayPhase.Dawn;
            if (t >= day  && t < dusk)  return DayPhase.Day;
            if (t >= dusk && t < night) return DayPhase.Dusk;
            return DayPhase.Night;
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
