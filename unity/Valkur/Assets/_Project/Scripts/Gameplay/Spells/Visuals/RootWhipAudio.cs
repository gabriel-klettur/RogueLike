using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two one-shots a root field makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>AudioCatalog.asset</c> holds no <c>spell_*</c> id at all, so
    /// every <c>PlaySfxById("spell_…")</c> in the project is a miss that produces one
    /// warning and no sound. <see cref="IceWallAudio"/>, <c>ShieldAudio</c> and
    /// <c>BoomerangAudio</c> answered that the same way and for the same reason: earth
    /// tearing open IS a low rumble under a cluster of fibrous cracks, and a whip IS a
    /// swept hiss ending in a transient, so this is DSP describing the thing rather than
    /// DSP imitating a performance. If a recorded set is ever authored, the catalog path is
    /// the better answer and these become the fallback.</para>
    /// </summary>
    internal static class RootWhipAudio
    {
        private const int SampleRate = 22050;

        /// <summary>Shortest gap between two lash cracks anywhere. Four stems answer every
        /// damage tick and a field can hold several victims, so without this a busy tick
        /// fires a dozen overlapping transients and the whole thing reads as static.</summary>
        private const float LashCooldown = 0.12f;

        private static AudioClip _sprout;
        private static AudioClip _lash;
        private static float _nextLashTime;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// AudioClips do not, so a cached entry would be a destroyed clip on the second
        /// Play. Assigning the fields directly is also the only reset shape
        /// <c>DomainReloadStaticResetTests</c> recognises.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sprout = null;
            _lash = null;
            _nextLashTime = 0f;
        }

        /// <summary>The ground opening: a low swell under a cluster of fibrous cracks.</summary>
        public static AudioClip Sprout()
        {
            if (_sprout == null) _sprout = BuildSprout();
            return _sprout;
        }

        /// <summary>One stem striking: a swept hiss ending in a hard transient.</summary>
        public static AudioClip Lash()
        {
            if (_lash == null) _lash = BuildLash();
            return _lash;
        }

        public static void PlaySproutAt(Vector3 worldPos)
        {
            // AudioSource.PlayClipAtPoint builds a temporary GameObject and schedules
            // Object.Destroy on it, which is an outright ERROR in Edit Mode — so a test or
            // an editor probe that builds the rig would fail on a sound it never asked for.
            if (!Application.isPlaying) return;
            var clip = Sprout();
            if (clip != null) AudioSource.PlayClipAtPoint(clip, worldPos, 0.75f);
        }

        /// <summary>Rate-limited: see <see cref="LashCooldown"/>.</summary>
        public static void PlayLashAt(Vector3 worldPos)
        {
            if (!Application.isPlaying) return;
            if (Time.time < _nextLashTime) return;
            _nextLashTime = Time.time + LashCooldown;
            var clip = Lash();
            if (clip != null) AudioSource.PlayClipAtPoint(clip, worldPos, 0.55f);
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        private static AudioClip BuildSprout()
        {
            const float seconds = 0.85f;
            int n = Mathf.RoundToInt(SampleRate * seconds);
            var buf = new float[n];
            var rng = new System.Random(20260903);

            // Layer 1 — the swell. Noise through a one-pole low-pass whose cutoff opens as
            // the ground gives way, which is what turns a rumble into a rumble that is
            // GOING somewhere.
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float k = Mathf.Lerp(0.010f, 0.075f, Mathf.Pow(t, 0.6f));
                lp += k * (white - lp);
                float env = Mathf.Exp(-t * 3.4f) * Mathf.Clamp01(t * 22f);
                buf[i] += lp * env * 1.5f;
            }

            // Layer 2 — the cracks. Seven short band-limited bursts, front-loaded, each a
            // decaying sine at a woody partial with noise riding it. Fibres letting go one
            // after another, not all at once.
            for (int c = 0; c < 7; c++)
            {
                float at = Mathf.Pow((float)rng.NextDouble(), 1.6f) * 0.42f;
                int start = Mathf.RoundToInt(at * SampleRate);
                float freq = Mathf.Lerp(180f, 620f, (float)rng.NextDouble());
                float decay = Mathf.Lerp(38f, 90f, (float)rng.NextDouble());
                float amp = Mathf.Lerp(0.35f, 0.8f, (float)rng.NextDouble());
                int len = Mathf.Min(n - start, SampleRate / 8);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-t * decay);
                    float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                    float grit = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.55f;
                    buf[start + i] += (tone * 0.7f + grit * 0.3f) * env * amp;
                }
            }

            return Finish("root_sprout", buf);
        }

        private static AudioClip BuildLash()
        {
            const float seconds = 0.26f;
            int n = Mathf.RoundToInt(SampleRate * seconds);
            var buf = new float[n];
            var rng = new System.Random(77015);

            // The hiss sweeps DOWN in cutoff while the amplitude sweeps UP into the crack:
            // the sound of something accelerating past you, then arriving.
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                float k = Mathf.Lerp(0.55f, 0.14f, t);
                lp += k * (white - lp);
                // One-pole high-pass on the low-passed noise: a band, which is what a whip
                // actually is. Full-band noise reads as a hi-hat.
                hp = 0.86f * (hp + lp - prev);
                prev = lp;

                // Ramp into the crack at 62%, then a hard decay.
                float env = t < 0.62f
                    ? Mathf.Pow(t / 0.62f, 2.4f) * 0.55f
                    : Mathf.Exp(-(t - 0.62f) * 26f);
                buf[i] += hp * env;
            }

            // The crack itself: one short, low, hard transient so the ear places the strike
            // on the ground rather than in the air.
            int crack = Mathf.RoundToInt(n * 0.62f);
            for (int i = 0; crack + i < n && i < SampleRate / 12; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 52f);
                buf[crack + i] += Mathf.Sin(2f * Mathf.PI * 145f * t) * env * 0.85f;
            }

            return Finish("root_lash", buf);
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
