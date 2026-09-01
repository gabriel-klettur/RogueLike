namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// How hard a weather is falling. The old API was a bool, which forced every
    /// effect to be authored at one density — so "rain" had to be simultaneously
    /// light enough to see the game through and heavy enough to read as weather,
    /// and landed on neither. A level is a density multiplier, NOT an on/off:
    /// <see cref="WeatherEffect"/> keeps activation (the fade) and density (this)
    /// as separate scalars so raising the level of a live weather ramps it instead
    /// of restarting it.
    ///
    /// <see cref="Off"/> exists so the UI can cycle through one array
    /// (Off → Light → Medium → Heavy → Off) rather than pairing an enum with a bool.
    /// </summary>
    public enum WeatherIntensity
    {
        Off,
        Light,
        Medium,
        Heavy,
    }

    public static class WeatherIntensityExtensions
    {
        /// <summary>
        /// The density multiplier a level applies to every layer's authored emission rate.
        ///
        /// Not linear: perceived precipitation density follows the number of streaks that
        /// cross the eye per second, and the step from "you can tell it is raining" to
        /// "you cannot see" is short. 0.30 / 0.62 / 1.00 keeps Light readable as weather
        /// while leaving Heavy room to feel genuinely oppressive.
        /// </summary>
        public static float ToScalar(this WeatherIntensity level) => level switch
        {
            WeatherIntensity.Light  => 0.30f,
            WeatherIntensity.Medium => 0.62f,
            WeatherIntensity.Heavy  => 1.00f,
            _                       => 0f,
        };

        /// <summary>Short uppercase tag for the Time &amp; Weather row label.</summary>
        public static string ToLabel(this WeatherIntensity level) => level switch
        {
            WeatherIntensity.Light  => "LIGHT",
            WeatherIntensity.Medium => "MEDIUM",
            WeatherIntensity.Heavy  => "HEAVY",
            _                       => "OFF",
        };

        /// <summary>Off → Light → Medium → Heavy → Off.</summary>
        public static WeatherIntensity Next(this WeatherIntensity level) => level switch
        {
            WeatherIntensity.Off    => WeatherIntensity.Light,
            WeatherIntensity.Light  => WeatherIntensity.Medium,
            WeatherIntensity.Medium => WeatherIntensity.Heavy,
            _                       => WeatherIntensity.Off,
        };
    }
}
