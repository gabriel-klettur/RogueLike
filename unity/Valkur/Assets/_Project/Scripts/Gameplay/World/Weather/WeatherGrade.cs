using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// What the weather asks the full-screen grade and the global light for.
    ///
    /// Particles alone cannot make weather felt. A heavy downpour that leaves the world at
    /// full saturation reads as confetti in front of a sunny day: real precipitation is a
    /// volume of scattering water between the eye and everything else, so it drains colour,
    /// lifts the blacks and closes the frame in. That half of the look belongs to
    /// <c>ScreenGradeSettings</c>, and lightning belongs to the Global Light 2D.
    ///
    /// Ownership is split rather than shared, because two writers to one field is the bug
    /// this class exists to avoid:
    ///   • <see cref="SaturationMultiplier"/> and <see cref="VignetteAdd"/> are MODIFIERS —
    ///     <c>DayNightCycle.PublishScreenGrade</c> composes them onto the values it owns.
    ///   • <see cref="Gain"/> and <see cref="Lift"/> are handed to the grade verbatim.
    ///     Nothing else in the project writes them (the day/night look is expressed as
    ///     saturation, contrast and vignette), so the weather owns them outright.
    ///   • <see cref="LightFlash01"/> is read by the cycle's light path.
    ///
    /// Written once per frame by <see cref="WeatherManager"/> — never by the individual
    /// effects, which would let rain and snow fight over the same multiplier.
    /// </summary>
    public static class WeatherGrade
    {
        // Domain Reload is OFF: a storm's desaturation left over from the previous Play
        // session would grade the first frames of the next one with no weather running.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SaturationMultiplier = 1f;
            VignetteAdd          = 0f;
            Gain                 = Vector3.one;
            Lift                 = Vector3.zero;
            LightFlash01         = 0f;
            _flashTimer          = 0f;
            _flashDuration       = 0f;
            _nextStrikeIn        = Unarmed;
        }

        /// <summary>Multiplies the day/night grade's saturation. 1 = untouched.</summary>
        public static float SaturationMultiplier { get; private set; } = 1f;

        /// <summary>Added to the day/night grade's vignette intensity. 0 = untouched.</summary>
        public static float VignetteAdd { get; private set; }

        /// <summary>Handed to the grade verbatim; the weather is the only writer.</summary>
        public static Vector3 Gain { get; private set; } = Vector3.one;

        /// <inheritdoc cref="Gain"/>
        public static Vector3 Lift { get; private set; } = Vector3.zero;

        /// <summary>
        /// Live lightning envelope, 0..1. The day/night cycle folds it into the Global
        /// Light 2D so a strike lights the WORLD — buildings, entities, the tilemap — and
        /// not only the post-process. A grade-only flash brightens the frame uniformly,
        /// which reads as an exposure change rather than as something happening in the sky.
        /// </summary>
        public static float LightFlash01 { get; private set; }

        /// <summary>
        /// The colour the global light is pushed toward at the peak of a strike. A computed
        /// property rather than a <c>static readonly</c> field so it stays off the
        /// Domain-Reload static ledger — there is nothing here to reset, and a field would
        /// have to justify itself to <c>DomainReloadStaticResetTests</c> anyway.
        /// </summary>
        public static Color FlashColor => new Color(0.86f, 0.90f, 1.00f, 1f);

        /// <summary>How much intensity a full-strength strike adds to the global light.</summary>
        public const float FlashLightBoost = 0.85f;

        // ── grade composition ────────────────────────────────────────────────────────

        /// <summary>
        /// Recompute the grade modifiers from how hard each weather is currently falling.
        /// Every argument is the effect's live density (level x fade), so the grade ramps
        /// with the particles rather than snapping when a toggle is clicked.
        /// </summary>
        /// <param name="rain01">Live rain density, 0..1.</param>
        /// <param name="snow01">Live snow density, 0..1.</param>
        /// <param name="wind01">Live wind density, 0..1.</param>
        public static void Compose(float rain01, float snow01, float wind01)
        {
            rain01 = Mathf.Clamp01(rain01);
            snow01 = Mathf.Clamp01(snow01);
            wind01 = Mathf.Clamp01(wind01);

            // Rain drains the most colour: a wet world is a grey world. Snow drains less and
            // instead lifts the blacks, which is what an overcast sky over a bright ground
            // actually does to a frame. Wind carries dust, so it desaturates a little too.
            float desaturate = rain01 * 0.26f + snow01 * 0.14f + wind01 * 0.06f;
            SaturationMultiplier = Mathf.Clamp(1f - desaturate, 0.55f, 1f);

            // Precipitation closes the frame in — the far edges are seen through more water
            // than the centre is. Small: the day/night vignette already owns most of this.
            VignetteAdd = rain01 * 0.20f + snow01 * 0.10f;

            // Snow's overcast lift, plus whatever the lightning envelope is contributing.
            float lift = snow01 * 0.020f + rain01 * 0.006f;
            Lift = new Vector3(lift, lift, lift * 1.35f) + Vector3.one * (LightFlash01 * 0.05f);

            // Rain cools the frame; snow cools it further and slightly flattens the highlights.
            float coolTop = 1f - rain01 * 0.030f - snow01 * 0.015f;
            Gain = new Vector3(coolTop, coolTop + rain01 * 0.008f, 1f)
                 + Vector3.one * (LightFlash01 * 0.55f);
        }

        // ── lightning ────────────────────────────────────────────────────────────────

        /// <summary>Sentinel for "the storm scheduler is not armed".</summary>
        private const float Unarmed = -1f;

        private static float _flashTimer;
        private static float _flashDuration;
        private static float _nextStrikeIn = Unarmed;

        /// <summary>Density below which no strike is ever scheduled.</summary>
        public const float StormDensityThreshold = 0.70f;

        /// <summary>True while a strike envelope is playing.</summary>
        public static bool IsFlashing => _flashDuration > 0f;

        /// <summary>
        /// Advance the lightning state machine. Called once per frame by
        /// <see cref="WeatherManager"/> with the live rain density; a strike is only ever
        /// scheduled while the rain is at or past <see cref="StormDensityThreshold"/>,
        /// because lightning over a drizzle reads as a rendering bug rather than as weather.
        ///
        /// Arming and firing are separate steps. Entering a storm sets a first gap rather
        /// than striking — otherwise the flash would land on the same frame as the click that
        /// turned the rain up, and read as a side effect of the UI rather than of the sky.
        ///
        /// The envelope itself is a double flash — a short leader, a gap, then the brighter
        /// return stroke — because a single symmetric pulse is the one shape real lightning
        /// never has, and the eye knows it.
        /// </summary>
        public static void TickLightning(float deltaTime, float rain01, bool enabled)
        {
            // A strike already in flight always finishes, even if the rain is being turned
            // off underneath it: cutting the envelope mid-stroke is a visible hard edge.
            if (_flashDuration > 0f)
            {
                _flashTimer += deltaTime;
                LightFlash01 = SampleFlash(_flashTimer / _flashDuration);
                if (_flashTimer >= _flashDuration)
                {
                    _flashDuration = 0f;
                    _flashTimer    = 0f;
                    LightFlash01   = 0f;
                }
                return;
            }

            LightFlash01 = 0f;

            if (!enabled || rain01 < StormDensityThreshold)
            {
                _nextStrikeIn = Unarmed;
                return;
            }

            if (_nextStrikeIn < 0f)
            {
                _nextStrikeIn = Random.Range(4f, 13f);
                return;
            }

            _nextStrikeIn -= deltaTime;
            if (_nextStrikeIn > 0f) return;

            Strike(Random.Range(0.55f, 1f));

            // Heavier rain strikes more often, but never so often that the world spends its
            // time lit by lightning: even at full density the mean gap is around 13 s.
            float lean = Mathf.InverseLerp(StormDensityThreshold, 1f, rain01);
            _nextStrikeIn = Random.Range(Mathf.Lerp(14f, 6f, lean), Mathf.Lerp(34f, 20f, lean));
        }

        /// <summary>Fire a strike right now. Exposed for the dev console and for tests.</summary>
        public static void Strike(float strength = 1f)
        {
            _flashTimer    = 0f;
            _flashDuration = Mathf.Lerp(0.28f, 0.52f, Mathf.Clamp01(strength));
            LightFlash01   = 0f;
        }

        /// <summary>
        /// The double-flash envelope, sampled on normalized strike time.
        /// Leader at ~12% of the strike, dark gap, return stroke at ~38%, long decay.
        /// </summary>
        private static float SampleFlash(float t)
        {
            t = Mathf.Clamp01(t);
            float leader = Mathf.Exp(-Mathf.Pow((t - 0.06f) / 0.045f, 2f)) * 0.45f;
            float ret    = Mathf.Exp(-Mathf.Pow((t - 0.24f) / 0.075f, 2f));
            float decay  = Mathf.Exp(-Mathf.Pow((t - 0.24f) / 0.42f, 2f)) * 0.22f;
            return Mathf.Clamp01(Mathf.Max(leader, ret) + decay * (t > 0.24f ? 1f : 0f));
        }
    }
}
