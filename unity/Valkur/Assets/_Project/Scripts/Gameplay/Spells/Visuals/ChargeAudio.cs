using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The tone a charge builds on, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>AudioCatalog.asset</c> contains no <c>spell_*</c> id at all, so
    /// every <c>PlaySfxById("spell_…")</c> in this project is a miss that produces one warning
    /// and no sound. <see cref="IceWallAudio"/>, <see cref="ShieldAudio"/>,
    /// <see cref="BoomerangAudio"/> and <see cref="ThrallAudio"/> all answered it the same
    /// way; the catalog stays the better answer the day a recorded set is authored.</para>
    ///
    /// <para>WHY IT IS PITCHED RATHER THAN CROSSFADED. The clip is one steady tone and the
    /// charge level is expressed entirely by <c>AudioSource.pitch</c>. Pitch is the one channel
    /// that can report a continuous quantity without occupying any screen space, so a player
    /// can charge while watching the fight instead of watching their own hand — which is the
    /// difference between a mechanic they can use under pressure and one they cannot.</para>
    ///
    /// <para>Every partial closes on a whole number of cycles across the clip, which is what
    /// makes the loop seamless. A partial that does not close leaves a discontinuity at the
    /// wrap, and it is audible as a tick once per period, forever — the same constraint
    /// <see cref="ShieldAudio"/>'s hum records.</para>
    /// </summary>
    internal static class ChargeAudio
    {
        private const int SampleRate = 22050;
        private const float LoopSeconds = 1f;

        /// <summary>Base frequency at pitch 1. Low enough that the top of the ramp
        /// (pitch ~1.85) still sits under the range voices and impacts occupy.</summary>
        private const float BaseHz = 180f;

        private static AudioClip _tone;

        /// <summary>
        /// Domain Reload is OFF: the managed handle survives a recompile while the native
        /// AudioClip does not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _tone = null;

        public static AudioClip Tone()
        {
            if (_tone == null) _tone = BuildTone();
            return _tone;
        }

        private static AudioClip BuildTone()
        {
            int total = Mathf.RoundToInt(SampleRate * LoopSeconds);
            var data = new float[total];

            // Snap each partial to a whole number of cycles across the buffer, so the loop
            // closes exactly. Rounding the CYCLE COUNT rather than the frequency is what
            // guarantees it -- rounding the frequency leaves a fractional cycle behind.
            int f1 = Mathf.RoundToInt(BaseHz * LoopSeconds);
            int f2 = Mathf.RoundToInt(BaseHz * 2f * LoopSeconds);
            int f3 = Mathf.RoundToInt(BaseHz * 3f * LoopSeconds);
            int wobble = Mathf.RoundToInt(6f * LoopSeconds);

            for (int i = 0; i < total; i++)
            {
                float t = i / (float)total;             // 0..1 across exactly one loop
                float phase = 2f * Mathf.PI * t;

                // A fundamental with two quiet harmonics. The harmonics are what stop it
                // reading as a test tone: a pure sine at a rising pitch sounds like equipment,
                // not like something being charged.
                float sample = Mathf.Sin(phase * f1) * 0.60f
                             + Mathf.Sin(phase * f2) * 0.22f
                             + Mathf.Sin(phase * f3) * 0.10f;

                // A slow amplitude wobble so the tone breathes instead of sitting perfectly
                // still. Also snapped to whole cycles, for the same loop reason.
                sample *= 0.88f + 0.12f * Mathf.Sin(phase * wobble);

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create("charge_tone", total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
