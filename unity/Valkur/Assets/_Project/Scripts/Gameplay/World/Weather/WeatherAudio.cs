using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// The looping beds the weather plays under itself, synthesised at runtime.
    ///
    /// <see cref="WeatherEffect"/> has had audio plumbing since it was written — an
    /// AudioSource, a master volume, a fade — and not one subclass ever overrode
    /// <c>ResolveAudioClip</c>, so every weather in the game has always been silent. The
    /// project ships no ambient recordings (<c>Audio/SFX/</c> holds combat, UI and animal
    /// one-shots only) and a rain bed is not something the catalog can be asked for, so
    /// the choice was a silent storm or a generated one.
    ///
    /// Generated is defensible here in a way it would not be for, say, a sword hit: rain and
    /// wind ARE filtered noise. A downpour is broadband hiss with a low-frequency body; wind
    /// is the same noise with everything above a few hundred hertz removed and its amplitude
    /// dragged around by the gust. Both are a few lines of DSP and neither is trying to
    /// imitate a performance.
    ///
    /// Snow is deliberately silent. Falling snow makes no sound, and a bed under it would be
    /// inventing one.
    /// </summary>
    internal static class WeatherAudio
    {
        // Domain Reload is OFF — the managed handle survives a recompile while the native
        // AudioClip does not, so a cached entry would be a destroyed clip on the second Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _rain = null;
            _wind = null;
        }

        /// <summary>
        /// 22.05 kHz mono. These are noise beds: the content above 11 kHz is hiss that is
        /// indistinguishable from the hiss below it, and halving the rate halves a buffer
        /// that is held for the whole session.
        /// </summary>
        private const int SampleRate = 22050;

        /// <summary>
        /// Length of the crossfade that makes the loop seamless. Long enough that the
        /// correlation between the two overlapped noise segments is inaudible, short enough
        /// not to eat a large fraction of a short loop.
        /// </summary>
        private const float CrossfadeSeconds = 0.75f;

        private static AudioClip _rain;
        private static AudioClip _wind;

        /// <summary>Broadband downpour hiss with a low body. ~6 s seamless loop.</summary>
        public static AudioClip Rain()
        {
            if (_rain != null) return _rain;
            _rain = Build("WeatherBed_Rain", 6f, 20260830, RainSample);
            return _rain;
        }

        /// <summary>Low-passed, slowly swelling air. ~9 s seamless loop.</summary>
        public static AudioClip Wind()
        {
            if (_wind != null) return _wind;
            _wind = Build("WeatherBed_Wind", 9f, 991117, WindSample);
            return _wind;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Per-voice filter state, threaded through the sample callbacks. A struct passed by
        /// ref rather than static fields, so building the rain bed cannot leave state behind
        /// that changes what the wind bed sounds like.
        /// </summary>
        private struct VoiceState
        {
            public float LpA, LpB, LpC;   // three cascaded one-pole low-pass stages
            public float HpPrevIn, HpPrevOut;
        }

        private delegate float SampleFunc(float whiteNoise, float timeSeconds, ref VoiceState s);

        /// <summary>
        /// Rain: a bright hiss (noise with the rumble removed) over a soft low body (the same
        /// noise with the hiss removed). Two bands rather than raw white noise because plain
        /// white noise reads as tape hiss — what says "water" is the pairing of a wide top
        /// end with a body that moves under it.
        /// </summary>
        private static float RainSample(float n, float t, ref VoiceState s)
        {
            // One-pole high-pass, corner around 900 Hz — the hiss.
            const float hpA = 0.78f;
            float hp = hpA * (s.HpPrevOut + n - s.HpPrevIn);
            s.HpPrevIn  = n;
            s.HpPrevOut = hp;

            // One-pole low-pass cascade, corner around 350 Hz — the body.
            const float lpK = 0.10f;
            s.LpA += (n - s.LpA) * lpK;
            s.LpB += (s.LpA - s.LpB) * lpK;

            // A slow swell so a long listen does not sit on one static texture. Two detuned
            // terms rather than one, or the swell is perfectly periodic and reads as a pulse
            // once the loop has gone round twice.
            float swell = 0.90f + 0.10f * Mathf.Sin(t * 0.23f) * Mathf.Cos(t * 0.11f + 0.7f);

            return (hp * 0.62f + s.LpB * 1.35f) * swell;
        }

        /// <summary>
        /// Wind: noise through three cascaded low-pass stages, with the cutoff and the
        /// amplitude both dragged by a slow oscillator. The moving cutoff is what separates
        /// wind from a fan — air noise gets BRIGHTER as it gets louder, because the gust is
        /// moving faster past the same edges.
        /// </summary>
        private static float WindSample(float n, float t, ref VoiceState s)
        {
            // Two detuned slow oscillators: a single one is a perfectly periodic swell, which
            // over a 9 s loop is audible as a pulse.
            float slow = 0.5f + 0.5f * Mathf.Sin(t * 0.42f) * Mathf.Cos(t * 0.17f + 1.3f);

            float k = Mathf.Lerp(0.030f, 0.115f, slow);   // cutoff rides the gust
            s.LpA += (n - s.LpA) * k;
            s.LpB += (s.LpA - s.LpB) * k;
            s.LpC += (s.LpB - s.LpC) * k;

            float amp = Mathf.Lerp(0.35f, 1f, slow);
            // The cascade costs a lot of level; 9x brings the bed back to a usable peak
            // before the normalisation pass, rather than leaving it to scale up the noise
            // floor along with the signal.
            return s.LpC * 9f * amp;
        }

        private static AudioClip Build(string name, float seconds, int seed, SampleFunc voice)
        {
            int fade    = Mathf.RoundToInt(CrossfadeSeconds * SampleRate);
            int length  = Mathf.RoundToInt(seconds * SampleRate);
            int total   = length + fade;

            var rng   = new System.Random(seed);
            var buf   = new float[total];
            var state = new VoiceState();

            for (int i = 0; i < total; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                buf[i] = voice(white, i / (float)SampleRate, ref state);
            }

            // Seamless loop: the tail is the same signal continuing past the loop point, so
            // crossfading it back over the head makes sample `length` wrap onto sample 0 with
            // no discontinuity. A hard cut would click once per loop, which is the tell that
            // gives away every badly looped ambience.
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                buf[i] = buf[i] * w + buf[length + i] * (1f - w);
            }

            // Normalise on the looped region only — the tail is discarded below.
            float peak = 0.0001f;
            for (int i = 0; i < length; i++)
            {
                float a = Mathf.Abs(buf[i]);
                if (a > peak) peak = a;
            }
            float gain = 0.82f / peak;

            var samples = new float[length];
            for (int i = 0; i < length; i++)
                samples[i] = Mathf.Clamp(buf[i] * gain, -1f, 1f);

            var clip = AudioClip.Create(name, length, 1, SampleRate, stream: false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }
    }
}
