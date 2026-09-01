using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// How much snow is lying on the world, and the one global the shaders read it from.
    ///
    /// Snow that falls through the frame and vanishes is the tell that a weather effect is a
    /// screen overlay: nothing it lands on ever changes, so after ten seconds the eye stops
    /// believing it. Accumulation is what makes it weather.
    ///
    /// This is the GLOBAL half of it — how much snow the world has taken overall, on a clock
    /// that thaws with the time of day. WHERE that snow lies is <see cref="SnowSplatMap"/>,
    /// a world-space buffer stamped by the individual flakes as they land, and the shader
    /// multiplies the two: this scalar decides how deep a full drift can get, the map decides
    /// which ground has one. Neither is meaningful alone, which is why
    /// <see cref="SetAmount"/> writes both.
    ///
    /// Nothing is written to disk and nothing is written per tile. That is deliberate: the
    /// alternative — painting a snow tilemap, or stamping accumulation into
    /// <c>StreamingAssets/</c> — would need a snow variant authored for every terrain pack,
    /// would not cover the 969 building templates at all, and would put a rendering effect
    /// into the same files the map editors own.
    ///
    /// Ticked once per frame by <see cref="WeatherManager"/>.
    /// </summary>
    public static class SnowAccumulation
    {
        // Domain Reload is OFF, and a Shader global outlives the Play session that set it —
        // so without this the editor would still be rendering the last run's snowdrift over
        // the first frames of the next one, with no snow falling and nothing to explain it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Amount     = 0f;
            _published = -1f;
            PublishIfChanged(force: true);
        }

        private static readonly int AmountId = Shader.PropertyToID("_ValkurSnowAmount");
        private static readonly int ColorId  = Shader.PropertyToID("_ValkurSnowColor");

        /// <summary>
        /// Seconds of Heavy snow to go from bare ground to full cover. Long on purpose: the
        /// player must never watch the world turn white, they must notice that it has. Anything
        /// under a minute reads as a slider being dragged.
        /// </summary>
        private const float SecondsToFullCover = 95f;

        /// <summary>Seconds to melt from full cover to bare, at the base (dawn/dusk) rate.</summary>
        private const float SecondsToFullMelt = 300f;

        /// <summary>
        /// The colour snow is blended toward. Very slightly blue rather than pure white: a
        /// pure-white blend against warm pixel art reads as a blown-out highlight, and snow in
        /// daylight is lit by the sky, which is not white.
        /// </summary>
        public static Color SnowColor => new Color(0.94f, 0.96f, 1.00f, 1f);

        /// <summary>Live cover, 0..1. 0 is bare ground, 1 is fully blanketed.</summary>
        public static float Amount { get; private set; }

        private static float _published = -1f;

        /// <summary>
        /// Advance the cover and publish it.
        ///
        /// <paramref name="snowDensity01"/> is the live snow density (level times fade), so
        /// accumulation follows what is actually falling rather than what a toggle was set to —
        /// snow turned off keeps accumulating for as long as the last flakes are still landing.
        /// </summary>
        public static void Tick(float deltaTime, float snowDensity01, bool enabled)
        {
            if (!enabled)
            {
                // The feature being switched off must clear the world, not freeze it mid-drift.
                Amount = Mathf.MoveTowards(Amount, 0f, deltaTime / SecondsToFullMelt * MeltRate());
                PublishIfChanged(force: false);
                return;
            }

            float density = Mathf.Clamp01(snowDensity01);
            if (density > 0.001f)
            {
                // Falling snow always wins over melting: a heavy fall settles even at noon,
                // which is exactly the situation the melt rate would otherwise cancel out.
                Amount = Mathf.MoveTowards(Amount, density, deltaTime * density / SecondsToFullCover);
            }
            else
            {
                Amount = Mathf.MoveTowards(Amount, 0f, deltaTime / SecondsToFullMelt * MeltRate());
            }

            PublishIfChanged(force: false);
        }

        /// <summary>
        /// Set the cover directly. For the <c>snow</c> console command and for tests — an
        /// author checking how a roof line reads at half cover should not have to stand in a
        /// blizzard for fifty seconds first.
        ///
        /// It fills <see cref="SnowSplatMap"/> to match. The two multiply in the shader, so
        /// raising this alone over an empty map would change nothing on screen: "pretend it
        /// has been snowing" has to be said in both places or in neither.
        /// </summary>
        public static void SetAmount(float amount)
        {
            Amount = Mathf.Clamp01(amount);
            PublishIfChanged(force: true);

            var map = SnowSplatMap.Instance;
            if (map != null) map.Fill(Amount);
        }

        /// <summary>
        /// Fraction of the lying snow that melts per second right now. Read by
        /// <see cref="WeatherManager"/> to fade <see cref="SnowSplatMap"/> on the same clock,
        /// so the drift and the global scalar never disagree about the thaw.
        /// </summary>
        public static float MeltPerSecond => MeltRate() / SecondsToFullMelt;

        /// <summary>
        /// How fast the lying snow melts right now, as a multiplier on the base rate.
        ///
        /// The sun does the melting, so this is the one place weather and the clock are
        /// genuinely coupled: a snowfall that ends at dusk is still on the ground at dawn,
        /// while one that ends at noon is gone within the hour. Night is not zero — a scene
        /// that never clears would strand the world white until the player happens to be
        /// looking at it in daylight.
        /// </summary>
        private static float MeltRate()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return 1f;

            switch (cycle.CurrentPhase)
            {
                case DayNightCycle.DayPhase.Day:            return 3.2f;
                case DayNightCycle.DayPhase.GoldenMorning:  return 1.9f;
                case DayNightCycle.DayPhase.GoldenEvening:  return 1.5f;
                case DayNightCycle.DayPhase.Dawn:           return 1.2f;
                case DayNightCycle.DayPhase.Dusk:           return 0.8f;
                case DayNightCycle.DayPhase.BlueHour:       return 0.5f;
                default:                                    return 0.25f;   // Night
            }
        }

        /// <summary>
        /// Push the value into the shaders. Gated on a real change so a bare world costs one
        /// float compare per frame rather than two global writes — <c>SetGlobalFloat</c>
        /// touches every shader in the pipeline, not just the two that read it.
        /// </summary>
        private static void PublishIfChanged(bool force)
        {
            if (!force && Mathf.Abs(Amount - _published) < 0.002f) return;
            _published = Amount;
            Shader.SetGlobalFloat(AmountId, Amount);
            Shader.SetGlobalColor(ColorId, SnowColor);
        }
    }
}
