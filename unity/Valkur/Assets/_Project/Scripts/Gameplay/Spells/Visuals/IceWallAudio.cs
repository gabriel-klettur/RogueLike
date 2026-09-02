using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The four one-shots an ice wall makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>WallController</c> has always asked the catalog for
    /// <c>spell_wall_ice_create</c> and <c>spell_wall_ice_destroy</c>, and neither id has
    /// ever existed in <c>AudioCatalog.asset</c> — the spell has been silent since it was
    /// written. <see cref="Valkur.Gameplay.World.Weather.WeatherAudio"/> answered the same
    /// problem the same way, and the argument is the same one: breaking ice IS a cluster of
    /// short noise transients with a ringing partial, so this is DSP describing the thing
    /// rather than DSP imitating a performance.</para>
    ///
    /// <para>Each clip is built once and cached. If a recorded set is ever authored, the
    /// catalog path is the better answer and these become the fallback.</para>
    /// </summary>
    internal static class IceWallAudio
    {
        private const int SampleRate = 22050;

        private static AudioClip _create;
        private static AudioClip _hit;
        private static AudioClip _shatter;
        private static AudioClip _melt;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// AudioClips do not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _create = null;
            _hit = null;
            _shatter = null;
            _melt = null;
        }

        /// <summary>The wall erupting: a low swell under a rising cascade of cracks.</summary>
        public static AudioClip Create()
        {
            if (_create != null) return _create;
            _create = BuildCreate();
            return _create;
        }

        /// <summary>A single blow landing on the crystals.</summary>
        public static AudioClip Hit()
        {
            if (_hit != null) return _hit;
            _hit = BuildHit();
            return _hit;
        }

        /// <summary>The whole barrier letting go at once.</summary>
        public static AudioClip Shatter()
        {
            if (_shatter != null) return _shatter;
            _shatter = BuildShatter();
            return _shatter;
        }

        /// <summary>The wall sublimating away when its timer runs out. Airy, no impact.</summary>
        public static AudioClip Melt()
        {
            if (_melt != null) return _melt;
            _melt = BuildMelt();
            return _melt;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        private static AudioClip BuildCreate()
        {
            var rng = new System.Random(20260901);
            var buffer = new float[(int)(1.15f * SampleRate)];

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / 1.15f;

                // Low swell: the ground heaving. Rises in pitch as the wall pushes up.
                float swell = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(52f, 104f, progress) * t);
                float swellEnvelope = Mathf.Pow(Mathf.Clamp01(progress / 0.35f), 1.2f) *
                                      Mathf.Pow(1f - progress, 1.4f);

                // Crystalline shimmer over the top, sweeping upward.
                float shimmer = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(880f, 2350f, progress) * t) +
                                0.5f * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1320f, 3520f, progress) * t);
                float shimmerEnvelope = Mathf.Pow(Mathf.Clamp01(progress / 0.5f), 2f) *
                                        Mathf.Pow(1f - progress, 2.2f);

                buffer[i] = swell * swellEnvelope * 0.55f + shimmer * shimmerEnvelope * 0.10f;
            }

            // The cracks: dense in the middle, where the crystals are actually erupting.
            for (int g = 0; g < 34; g++)
            {
                float when = Mathf.Pow((float)rng.NextDouble(), 0.7f) * 0.85f + 0.05f;
                AddGrain(buffer, (int)(when * SampleRate),
                    amplitude: 0.30f + 0.35f * (float)rng.NextDouble(),
                    decaySeconds: 0.018f + 0.05f * (float)rng.NextDouble(),
                    frequency: 900f + 2600f * (float)rng.NextDouble(), rng);
            }

            return Finish("IceWall_Create", buffer);
        }

        private static AudioClip BuildHit()
        {
            var rng = new System.Random(4711);
            var buffer = new float[(int)(0.32f * SampleRate)];

            for (int g = 0; g < 4; g++)
            {
                AddGrain(buffer, (int)((float)rng.NextDouble() * 0.03f * SampleRate),
                    amplitude: 0.55f + 0.35f * (float)rng.NextDouble(),
                    decaySeconds: 0.02f + 0.045f * (float)rng.NextDouble(),
                    frequency: 1400f + 2400f * (float)rng.NextDouble(), rng);
            }

            // A short ring so the hit has pitch — pure noise reads as a footstep.
            AddGrain(buffer, 0, amplitude: 0.35f, decaySeconds: 0.11f, frequency: 1720f, rng, noiseMix: 0.15f);

            return Finish("IceWall_Hit", buffer);
        }

        private static AudioClip BuildShatter()
        {
            var rng = new System.Random(90210);
            var buffer = new float[(int)(0.95f * SampleRate)];

            // The body of the collapse.
            AddGrain(buffer, 0, amplitude: 0.75f, decaySeconds: 0.09f, frequency: 130f, rng, noiseMix: 0.7f);

            // Forty-odd pieces, thinning out as they come to rest.
            for (int g = 0; g < 46; g++)
            {
                float when = Mathf.Pow((float)rng.NextDouble(), 1.9f) * 0.62f;
                AddGrain(buffer, (int)(when * SampleRate),
                    amplitude: (0.22f + 0.45f * (float)rng.NextDouble()) * (1f - when * 0.8f),
                    decaySeconds: 0.015f + 0.06f * (float)rng.NextDouble(),
                    frequency: 700f + 3800f * (float)rng.NextDouble(), rng);
            }

            return Finish("IceWall_Shatter", buffer);
        }

        private static AudioClip BuildMelt()
        {
            var rng = new System.Random(31337);
            var buffer = new float[(int)(0.75f * SampleRate)];
            float lowPass = 0f;

            for (int i = 0; i < buffer.Length; i++)
            {
                float progress = i / (float)buffer.Length;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // The filter closes as the wall goes: the hiss gets duller rather than
                // merely quieter, which is what "losing substance" sounds like.
                float cutoff = Mathf.Lerp(0.42f, 0.05f, progress);
                lowPass += (noise - lowPass) * cutoff;

                float envelope = Mathf.Pow(Mathf.Clamp01(progress / 0.12f), 1.5f) *
                                 Mathf.Pow(1f - progress, 1.6f);
                buffer[i] = lowPass * envelope * 0.55f;
            }

            return Finish("IceWall_Melt", buffer);
        }

        /// <summary>
        /// One crack: an exponentially decaying burst of noise with a ringing partial. The
        /// ring is what makes it ICE — the same grain with <paramref name="noiseMix"/> at 1
        /// is gravel.
        /// </summary>
        private static void AddGrain(float[] buffer, int start, float amplitude, float decaySeconds,
            float frequency, System.Random rng, float noiseMix = 0.55f)
        {
            if (start < 0) start = 0;
            int length = Mathf.Min(buffer.Length - start, (int)(decaySeconds * 5f * SampleRate));
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t / decaySeconds);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float ring = Mathf.Sin(2f * Mathf.PI * frequency * t);
                buffer[start + i] += amplitude * envelope *
                                     (noiseMix * noise + (1f - noiseMix) * ring);
            }
        }

        /// <summary>Normalise to just under full scale and wrap in a clip.</summary>
        private static AudioClip Finish(string name, float[] buffer)
        {
            float peak = 0f;
            for (int i = 0; i < buffer.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buffer[i]));
            if (peak > 1e-4f)
            {
                float gain = 0.92f / peak;
                for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
            }

            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
