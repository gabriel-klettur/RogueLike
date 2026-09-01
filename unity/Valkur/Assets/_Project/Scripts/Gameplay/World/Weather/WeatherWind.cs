using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// The one wind vector every weather effect reads.
    ///
    /// Before this, each effect carried its own hardcoded horizontal drift: rain slanted
    /// left at a constant 1–2 u/s, snow wobbled inside a fixed ±0.4 band, and the wind
    /// effect blew at a flat 8–12 u/s. Three constants, three independent authors — so
    /// "Wind + Rain = a wind-driven rainstorm", the composition <see cref="WeatherManager"/>
    /// exists to allow, was a claim the pixels never made: turning the wind on changed
    /// nothing about how the rain fell.
    ///
    /// Now there is one field. <see cref="WindEffect"/> raises <see cref="WeatherSpeed"/>
    /// while it is active; rain and snow read <see cref="Velocity"/> and are pushed by it.
    /// A weak ambient breeze runs even with every weather off, because falling precipitation
    /// with a mathematically vertical trajectory reads as a screen-space overlay rather than
    /// as something happening in the world.
    ///
    /// Ticked once per frame by <see cref="WeatherManager"/>, never by the effects — the
    /// gust envelope has to be the SAME sample for every reader within a frame, or rain and
    /// snow would slant by different amounts in the same gust.
    /// </summary>
    public static class WeatherWind
    {
        // Domain Reload is OFF: a gust phase and a stale WeatherSpeed from the previous
        // Play session would otherwise start the next one mid-storm with no weather active.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DirectionX    = -1f;
            WeatherSpeed  = 0f;
            Gust01        = 0.5f;
            _phase        = 0f;
            _seed         = 0f;
            _seeded       = false;
        }

        /// <summary>
        /// Speed of the breeze that blows with no weather active, in world units/second.
        /// Small enough that a still scene is still, large enough that a snowflake never
        /// falls in a perfectly straight line.
        /// </summary>
        public const float AmbientSpeed = 0.9f;

        /// <summary>Gust envelope floor/ceiling as a multiplier on the base speed.</summary>
        private const float GustFloor = 0.45f;
        private const float GustCeil  = 1.85f;

        /// <summary>
        /// Perlin scroll rate of the slow gust body, in cycles/second. A real gust front
        /// takes several seconds to build and several more to die; anything above ~0.2 Hz
        /// reads as flicker rather than wind.
        /// </summary>
        private const float SlowGustHz = 0.085f;

        /// <summary>Faster ripple layered on top so the envelope is not a clean sine.</summary>
        private const float FastGustHz = 0.47f;

        private static float _phase;
        private static float _seed;
        private static bool  _seeded;

        /// <summary>
        /// Sign of the blow: -1 blows toward screen-left, +1 toward screen-right.
        /// Kept as a signed scalar rather than a Vector2 because every weather in the game
        /// is a vertical fall pushed sideways — a wind with a vertical component would make
        /// rain fall UP, which is a different feature.
        /// </summary>
        public static float DirectionX { get; private set; } = -1f;

        /// <summary>
        /// What <see cref="WindEffect"/> contributes, in world units/second, before the gust
        /// envelope. Written only by that effect (smoothed by its own fade), so the field is
        /// zero exactly when no wind weather is running.
        /// </summary>
        public static float WeatherSpeed { get; set; }

        /// <summary>Live gust envelope, 0..1. Same sample for every reader within a frame.</summary>
        public static float Gust01 { get; private set; } = 0.5f;

        /// <summary>Base speed before the gust envelope (ambient breeze + weather contribution).</summary>
        public static float BaseSpeed => AmbientSpeed + WeatherSpeed;

        /// <summary>Live speed, gust included, in world units/second. Always positive.</summary>
        public static float Speed => BaseSpeed * Mathf.Lerp(GustFloor, GustCeil, Gust01);

        /// <summary>Live wind as a signed horizontal velocity.</summary>
        public static float VelocityX => DirectionX * Speed;

        /// <summary>Live wind as a vector, for callers that want to add it to a velocity.</summary>
        public static Vector2 Velocity => new Vector2(VelocityX, 0f);

        /// <summary>
        /// Flip the blow direction. Anything non-negative blows right, negative blows left;
        /// zero is treated as left so a caller passing a raw axis at rest does not stall the wind.
        /// </summary>
        public static void SetDirection(float sign) => DirectionX = sign > 0f ? 1f : -1f;

        /// <summary>Reverse the current blow direction. Returns the new sign.</summary>
        public static float FlipDirection()
        {
            DirectionX = -DirectionX;
            return DirectionX;
        }

        /// <summary>
        /// Advance the gust envelope. Called once per frame by <see cref="WeatherManager"/>.
        ///
        /// Two Perlin octaves rather than one: a single octave at gust rate is smooth enough
        /// to read as a slider being dragged, and a single octave fast enough to feel alive
        /// changes direction faster than air does. The slow one carries the gust front, the
        /// fast one roughens its surface at a quarter of the amplitude.
        /// </summary>
        public static void Tick(float deltaTime)
        {
            if (!_seeded)
            {
                // One draw per session. A fixed seed would make every run's first gust land
                // on the same frame of the same shape, which is visible when the player
                // restarts to compare a weather setting.
                _seed   = Random.value * 512f;
                _seeded = true;
            }

            _phase += Mathf.Max(0f, deltaTime);

            float slow = Mathf.PerlinNoise(_seed + _phase * SlowGustHz, 0.37f);
            float fast = Mathf.PerlinNoise(0.71f, _seed + _phase * FastGustHz);

            // Perlin's practical range is roughly 0.15..0.85 rather than the full 0..1, so a
            // raw sample never reaches either end of the envelope. Re-normalise before mixing,
            // or Heavy wind never actually reaches its ceiling.
            slow = Mathf.Clamp01((slow - 0.15f) / 0.70f);
            fast = Mathf.Clamp01((fast - 0.15f) / 0.70f);

            Gust01 = Mathf.Clamp01(slow * 0.78f + fast * 0.22f);
        }
    }
}
