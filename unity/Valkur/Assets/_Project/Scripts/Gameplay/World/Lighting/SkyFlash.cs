using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// A transient burst of light in the SKY, folded into the Global Light 2D.
    ///
    /// <para>WHY THIS EXISTS. An effect that is supposed to light the world cannot do it
    /// with its own <c>Light2D</c>: a point light has a radius, and something detonating
    /// eight units above the ground has to touch the tilemap, the buildings and every
    /// entity on screen at once. <see cref="Weather.WeatherGrade"/> already learned this —
    /// its strike boosts the global light rather than only the screen grade, because a
    /// grade-only flash brightens every pixel by the same amount and reads as an exposure
    /// change instead of as something happening in the sky.</para>
    ///
    /// <para>That hook was hardcoded to lightning. This is the same mechanism with the
    /// weather taken out of it, so a firework — or anything else that goes off overhead —
    /// composes into the same one place rather than growing a second, differently-tuned
    /// path through <see cref="DayNightCycle"/>. The two ADD: a firework during a storm
    /// should be brighter than either alone, and the sum is clamped by the ceiling
    /// <c>DayNightCycle</c> already applies.</para>
    ///
    /// <para>Ticked once per frame by <see cref="DayNightCycle"/>'s <c>Update</c>, never by
    /// <c>UpdateLighting</c> — that method is also called from property setters, so a timer
    /// advanced there would run several times in a frame whenever anything scrubbed the
    /// clock.</para>
    /// </summary>
    public static class SkyFlash
    {
        /// <summary>
        /// Peak added intensity for a full-strength pulse. Deliberately below the storm
        /// strike's 0.85: lightning is the sky itself letting go, a firework is a shell.
        /// </summary>
        public const float MaxLightBoost = 0.55f;

        private static float _age;
        private static float _duration;
        private static float _strength;
        private static Color _color = Color.white;

        /// <summary>How much light the pulse is adding right now, 0 when nothing is lit.</summary>
        public static float Flash01 { get; private set; }

        /// <summary>The colour the sky is being pushed toward while <see cref="Flash01"/> is non-zero.</summary>
        public static Color FlashColor => _color;

        public static bool IsFlashing => _duration > 0f;

        /// <summary>
        /// Domain Reload is OFF, so a pulse left mid-flight by a Play-mode exit would be
        /// restored on the next entry and tint the very first frame of the new session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _age = 0f;
            _duration = 0f;
            _strength = 0f;
            _color = Color.white;
            Flash01 = 0f;
        }

        /// <summary>
        /// Light the sky. A second call while one is already running keeps whichever pulse
        /// is BRIGHTER at this instant rather than restarting — a volley of fireworks should
        /// build to a glow, not chop each other off mid-decay.
        /// </summary>
        public static void Pulse(Color color, float strength, float duration)
        {
            strength = Mathf.Clamp01(strength);
            duration = Mathf.Max(0.02f, duration);
            if (strength <= 0f) return;

            // Compare at the current instant, not on the authored peaks: a fading bright
            // pulse can legitimately be dimmer right now than a new faint one.
            if (Flash01 >= strength) return;

            _color = color;
            _strength = strength;
            _duration = duration;
            _age = 0f;
            Flash01 = strength;
        }

        /// <summary>
        /// Advance the envelope. Fast attack, slow release — the shape of a detonation.
        /// A symmetric fade reads as a lamp being turned up and down.
        /// </summary>
        public static void Tick(float deltaTime)
        {
            if (_duration <= 0f) return;

            _age += Mathf.Max(0f, deltaTime);
            if (_age >= _duration)
            {
                _duration = 0f;
                _strength = 0f;
                Flash01 = 0f;
                return;
            }

            float t = _age / _duration;
            const float attack = 0.12f;
            float env = t < attack
                ? t / attack
                : 1f - Mathf.Pow((t - attack) / (1f - attack), 0.65f);
            Flash01 = _strength * Mathf.Clamp01(env);
        }
    }
}
