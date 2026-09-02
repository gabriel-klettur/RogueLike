using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sounds a shield sphere makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>ShieldController</c> has always asked the catalog for
    /// <c>spell_shield_create</c>, and that id has never existed in
    /// <c>AudioCatalog.asset</c> — the spell has been silent since it was written, exactly like
    /// the ice wall was. <see cref="IceWallAudio"/> and
    /// <see cref="Valkur.Gameplay.World.Weather.WeatherAudio"/> answered the same problem the
    /// same way.</para>
    ///
    /// <para>The HUM is the one that matters. A barrier is a SUSTAINED thing, and a sustained
    /// visual with no sustained sound reads as a picture of a shield rather than as a shield
    /// being up — the one-shots alone leave five seconds of silence in the middle of the
    /// effect.</para>
    /// </summary>
    internal static class ShieldAudio
    {
        private const int SampleRate = 22050;

        /// <summary>
        /// Hum length in seconds. Every partial is snapped to a whole number of cycles across
        /// exactly this many samples, which is what makes the loop seamless — a partial that
        /// does not close leaves a discontinuity at the wrap and it is audible as a tick once
        /// per period, forever.
        /// </summary>
        private const float HumSeconds = 2f;

        private static AudioClip _create;
        private static AudioClip _hum;
        private static AudioClip _impact;
        private static AudioClip _break;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// AudioClips do not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _create = null;
            _hum = null;
            _impact = null;
            _break = null;
        }

        /// <summary>The sphere closing: a rising sweep that resolves into a struck bell.</summary>
        public static AudioClip Create()
        {
            if (_create == null) _create = BuildCreate();
            return _create;
        }

        /// <summary>The steady tone of a barrier holding. Loops seamlessly.</summary>
        public static AudioClip Hum()
        {
            if (_hum == null) _hum = BuildHum();
            return _hum;
        }

        /// <summary>A blow turned away — bright, glassy, gone quickly.</summary>
        public static AudioClip Impact()
        {
            if (_impact == null) _impact = BuildImpact();
            return _impact;
        }

        /// <summary>The shell letting go when its time runs out.</summary>
        public static AudioClip Break()
        {
            if (_break == null) _break = BuildBreak();
            return _break;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        private static AudioClip BuildCreate()
        {
            var rng = new System.Random(51204);
            const float seconds = 0.85f;
            var buffer = new float[(int)(seconds * SampleRate)];

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;

                // A sweep UP into the moment it closes, rather than a decay away from it: the
                // shell is being assembled, and the ear reads a rising pitch as gathering.
                float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 720f, Mathf.Pow(progress, 0.6f)) * t);
                float sweepEnvelope = Mathf.Pow(Mathf.Clamp01(progress / 0.55f), 1.5f) *
                                      Mathf.Pow(1f - progress, 0.9f);

                // The bell it lands on, only after the sweep has arrived.
                float strike = Mathf.Clamp01((progress - 0.45f) / 0.1f);
                float bell = Mathf.Sin(2f * Mathf.PI * 784f * t) +
                             0.45f * Mathf.Sin(2f * Mathf.PI * 1176f * t) +
                             0.22f * Mathf.Sin(2f * Mathf.PI * 2352f * t);

                buffer[i] = sweep * sweepEnvelope * 0.42f +
                            bell * strike * Mathf.Pow(1f - progress, 2.4f) * 0.24f;
            }

            // A handful of glassy specks as the facets snap in.
            for (int g = 0; g < 12; g++)
            {
                float when = 0.42f + (float)rng.NextDouble() * 0.30f;
                AddGrain(buffer, (int)(when * SampleRate),
                    amplitude: 0.14f + 0.16f * (float)rng.NextDouble(),
                    decaySeconds: 0.02f + 0.04f * (float)rng.NextDouble(),
                    frequency: 1800f + 2800f * (float)rng.NextDouble(), rng, noiseMix: 0.2f);
            }

            return Finish("Shield_Create", buffer);
        }

        /// <summary>
        /// The sustained tone. Three detuned partials plus a slow beat, all snapped to whole
        /// cycles over the buffer. It is deliberately QUIET and low — a bright drone under a
        /// five-second effect becomes maddening within one cast.
        /// </summary>
        private static AudioClip BuildHum()
        {
            int length = (int)(HumSeconds * SampleRate);
            var buffer = new float[length];

            // Snap a frequency to the nearest whole number of cycles across the buffer.
            float Snap(float hz) => Mathf.Max(1f, Mathf.Round(hz * HumSeconds)) / HumSeconds;

            float f1 = Snap(98f);      // the body
            float f2 = Snap(147f);     // a fifth above it
            float f3 = Snap(294f);     // an airy octave, kept faint
            float beat = Snap(3.5f);   // the slow swell that stops it being a test tone

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.82f + 0.18f * Mathf.Sin(2f * Mathf.PI * beat * t);

                buffer[i] = (Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.55f +
                             Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.30f +
                             Mathf.Sin(2f * Mathf.PI * f3 * t) * 0.12f) * swell;
            }

            return Finish("Shield_Hum", buffer, peakTarget: 0.55f);
        }

        private static AudioClip BuildImpact()
        {
            var rng = new System.Random(8823);
            var buffer = new float[(int)(0.38f * SampleRate)];

            // The blow itself: a short thump so the hit has weight under the glass.
            AddGrain(buffer, 0, amplitude: 0.55f, decaySeconds: 0.035f, frequency: 165f, rng,
                noiseMix: 0.45f);

            // The shell answering. Two partials a fifth apart ring longer than the thump, which
            // is what says the energy went INTO something rather than through it.
            AddGrain(buffer, 0, amplitude: 0.42f, decaySeconds: 0.16f, frequency: 1046f, rng);
            AddGrain(buffer, 0, amplitude: 0.24f, decaySeconds: 0.13f, frequency: 1568f, rng);

            for (int g = 0; g < 6; g++)
            {
                AddGrain(buffer, (int)((float)rng.NextDouble() * 0.05f * SampleRate),
                    amplitude: 0.16f + 0.18f * (float)rng.NextDouble(),
                    decaySeconds: 0.02f + 0.03f * (float)rng.NextDouble(),
                    frequency: 2200f + 2600f * (float)rng.NextDouble(), rng, noiseMix: 0.25f);
            }

            return Finish("Shield_Impact", buffer);
        }

        private static AudioClip BuildBreak()
        {
            var rng = new System.Random(30117);
            const float seconds = 0.7f;
            var buffer = new float[(int)(seconds * SampleRate)];

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;

                // The mirror image of Create: the tone falls away as the shell opens.
                float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(620f, 140f, Mathf.Pow(progress, 0.7f)) * t);
                buffer[i] = sweep * Mathf.Pow(1f - progress, 1.6f) * 0.34f;
            }

            // The facets going their separate ways.
            for (int g = 0; g < 26; g++)
            {
                float when = Mathf.Pow((float)rng.NextDouble(), 0.6f) * 0.5f;
                AddGrain(buffer, (int)(when * SampleRate),
                    amplitude: 0.18f + 0.28f * (float)rng.NextDouble(),
                    decaySeconds: 0.03f + 0.07f * (float)rng.NextDouble(),
                    frequency: 1200f + 3000f * (float)rng.NextDouble(), rng, noiseMix: 0.3f);
            }

            return Finish("Shield_Break", buffer);
        }

        // ── shared ───────────────────────────────────────────────────────────────────

        private static void AddGrain(float[] buffer, int start, float amplitude,
            float decaySeconds, float frequency, System.Random rng, float noiseMix = 0f)
        {
            if (start < 0 || start >= buffer.Length) return;

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

        private static AudioClip Finish(string name, float[] buffer, float peakTarget = 0.92f)
        {
            float peak = 0f;
            for (int i = 0; i < buffer.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buffer[i]));
            if (peak > 1e-4f)
            {
                float gain = peakTarget / peak;
                for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
            }

            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
