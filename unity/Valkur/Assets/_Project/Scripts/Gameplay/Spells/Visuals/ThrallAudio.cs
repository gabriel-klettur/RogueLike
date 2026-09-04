using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sound of a thrall rising, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>AudioCatalog.asset</c> contains no <c>spell_*</c> id at all, so
    /// every <c>PlaySfxById("spell_…")</c> in the project is a miss that produces one warning
    /// and no sound. <see cref="IceWallAudio"/>, <see cref="ShieldAudio"/> and
    /// <see cref="BoomerangAudio"/> all answered this the same way; the catalog stays the
    /// better answer the day a recorded set is authored, and these become the fallback.</para>
    ///
    /// <para>THE SILENCE IS COMPOSED. The clip opens with 0.15 s of ACTUAL SILENCE — samples
    /// written as zero, not an envelope that happens to be quiet — because the rising's first
    /// beat is the pause after the kill. Starting the swell immediately would fill exactly the
    /// gap the visual sequence needs, and the two would fight.</para>
    /// </summary>
    internal static class ThrallAudio
    {
        private const int SampleRate = 22050;

        /// <summary>Seconds of true silence at the head of the clip. Matches
        /// <c>ThrallRaiseFX.T_SILENCE</c>; the two are the same beat heard and seen.</summary>
        private const float SilenceSeconds = 0.15f;

        private const float TotalSeconds = 1.6f;

        private static AudioClip _raise;

        /// <summary>
        /// Domain Reload is OFF: the managed handle survives a recompile while the native
        /// AudioClip does not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _raise = null;

        public static AudioClip Raise()
        {
            if (_raise == null) _raise = BuildRaise();
            return _raise;
        }

        /// <summary>
        /// One-shot at a world position. <c>AudioSource.PlayClipAtPoint</c> builds a
        /// temporary GameObject and cleans it up itself, which is what every other
        /// synthesised spell sound in this folder uses.
        /// </summary>
        public static void PlayRaise(Vector3 worldPos)
        {
            var clip = Raise();
            if (clip != null) AudioSource.PlayClipAtPoint(clip, worldPos, 0.8f);
        }

        private static AudioClip BuildRaise()
        {
            int total = Mathf.RoundToInt(SampleRate * TotalSeconds);
            int silent = Mathf.RoundToInt(SampleRate * SilenceSeconds);
            var data = new float[total];

            // A low swell that climbs, then a dry crack at the moment the body comes up.
            int crackAt = Mathf.RoundToInt(SampleRate * 0.85f);
            var rng = new System.Random(0x7472616C);   // fixed seed: the sound is content

            for (int i = silent; i < total; i++)
            {
                float t = (i - silent) / (float)SampleRate;
                float span = (total - silent) / (float)SampleRate;
                float k = t / span;

                // Sub-bass swell. The pitch RISES, which is what makes it read as something
                // being drawn up out of the ground rather than settling into it.
                float freq = Mathf.Lerp(38f, 96f, k * k);
                float swell = Mathf.Sin(2f * Mathf.PI * freq * t);

                // A fifth above, entering late and quiet, so the swell gains harmonic weight
                // as it climbs instead of being one tone the whole way.
                float fifth = Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t) * Mathf.Clamp01((k - 0.35f) * 1.6f);

                float envelope = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                float sample = (swell * 0.7f + fifth * 0.25f) * envelope;

                // The crack: a short burst of filtered noise decaying fast. Dry on purpose —
                // it is the one dead, physical sound in an otherwise tonal effect, which is
                // what makes the body land rather than float.
                if (i >= crackAt)
                {
                    float ct = (i - crackAt) / (float)SampleRate;
                    float decay = Mathf.Exp(-ct * 26f);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    sample += noise * decay * 0.45f;
                }

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create("thrall_raise", total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
