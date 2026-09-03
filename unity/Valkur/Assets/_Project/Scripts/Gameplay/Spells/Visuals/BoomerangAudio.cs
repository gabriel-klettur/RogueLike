using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The three one-shots a boomerang makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>BoomerangExecutor</c> has always asked the catalog for
    /// <c>spell_boomerang_throw</c> and the palette for <c>spell_boomerang_impact</c>, and
    /// neither id has ever existed in <c>AudioCatalog.asset</c> — which holds no
    /// <c>spell_*</c> entry at all. The spell has been silent since it was written, and the
    /// only thing the two ids produced was one console warning apiece.
    /// <see cref="IceWallAudio"/> and <see cref="ShieldAudio"/> answered the same problem the
    /// same way, and the argument is the same one: a spinning blade IS band-passed noise
    /// chopped at the spin rate, so this is DSP describing the thing rather than DSP imitating
    /// a performance.</para>
    ///
    /// <para>Each clip is built once and cached. If a recorded set is ever authored, the
    /// catalog path is the better answer and these become the fallback.</para>
    /// </summary>
    internal static class BoomerangAudio
    {
        private const int SampleRate = 22050;

        /// <summary>Turns per second of the blade, matching <c>BoomerangProjectile</c>.
        /// The whoosh is amplitude-modulated at twice this, because a flat blade presents its
        /// edge to the air twice per revolution.</summary>
        private const float SpinHz = 2f;

        private static AudioClip _throw;
        private static AudioClip _impact;
        private static AudioClip _catch;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// AudioClips do not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _throw = null;
            _impact = null;
            _catch = null;
        }

        /// <summary>The blade leaving the hand: a wooden release under a chopping whoosh.</summary>
        public static AudioClip Throw()
        {
            if (_throw != null) return _throw;
            _throw = BuildThrow();
            return _throw;
        }

        /// <summary>The edge landing on something: a hard wooden crack with a short body ring.</summary>
        public static AudioClip Impact()
        {
            if (_impact != null) return _impact;
            _impact = BuildImpact();
            return _impact;
        }

        /// <summary>The blade caught: a muted slap that stops dead. No ring — a hand damps it.</summary>
        public static AudioClip Catch()
        {
            if (_catch != null) return _catch;
            _catch = BuildCatch();
            return _catch;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        private static AudioClip BuildThrow()
        {
            var rng = new System.Random(20260902);
            int length = (int)(0.55f * SampleRate);
            var buffer = new float[length];

            var band = new BandPass();
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)length;

                // The blade accelerates away and then settles: centre frequency rises fast and
                // falls back. A single fixed band reads as static, not as something travelling.
                float centre = Mathf.Lerp(420f, 1450f, Mathf.Sin(Mathf.Clamp01(u * 2.2f) * Mathf.PI * 0.5f));
                if (u > 0.45f) centre = Mathf.Lerp(1450f, 680f, (u - 0.45f) / 0.55f);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float voice = band.Process(noise, centre, 0.55f, SampleRate);

                // Two chops per revolution, deepening as the blade gets clear of the hand.
                float chopDepth = Mathf.Lerp(0.15f, 0.55f, Mathf.Clamp01(u * 1.6f));
                float chop = 1f - chopDepth * 0.5f * (1f - Mathf.Cos(t * SpinHz * 2f * 2f * Mathf.PI));

                float envelope = Mathf.Min(1f, u / 0.06f) * Mathf.Exp(-2.6f * u);
                buffer[i] = voice * chop * envelope * 0.9f;
            }

            // The release itself: a short wooden tok so the throw has an onset rather than a fade-in.
            AddWoodTransient(buffer, 0, 0.09f, new[] { 540f, 810f }, 0.55f);
            return Finish("spell_boomerang_throw", buffer);
        }

        private static AudioClip BuildImpact()
        {
            var rng = new System.Random(20260903);
            int length = (int)(0.30f * SampleRate);
            var buffer = new float[length];

            // The crack: a very short burst of bright noise, gone in 25 ms.
            var band = new BandPass();
            for (int i = 0; i < length; i++)
            {
                float u = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float voice = band.Process(noise, 2100f, 0.35f, SampleRate);
                buffer[i] = voice * Mathf.Exp(-90f * u) * 0.8f;
            }

            // The body behind it. Wood, so the partials are inharmonic and decay unevenly —
            // a harmonic stack here would read as a bell.
            AddWoodTransient(buffer, 0, 0.26f, new[] { 620f, 947f, 1490f }, 1f);
            return Finish("spell_boomerang_impact", buffer);
        }

        private static AudioClip BuildCatch()
        {
            var rng = new System.Random(20260904);
            int length = (int)(0.22f * SampleRate);
            var buffer = new float[length];

            var band = new BandPass();
            for (int i = 0; i < length; i++)
            {
                float u = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Darker and shorter than the impact: skin over wood, not wood over stone.
                float voice = band.Process(noise, 760f, 0.5f, SampleRate);
                buffer[i] = voice * Mathf.Exp(-52f * u) * 0.85f;
            }

            AddWoodTransient(buffer, 0, 0.11f, new[] { 210f, 330f }, 0.7f);
            return Finish("spell_boomerang_catch", buffer);
        }

        /// <summary>
        /// A struck-wood body: a few inharmonic partials sharing one exponential decay, with
        /// the higher ones dying faster the way a real body loses its top end first.
        /// </summary>
        private static void AddWoodTransient(float[] buffer, int start, float seconds,
                                             float[] partials, float gain)
        {
            int length = Mathf.Min(buffer.Length - start, (int)(seconds * SampleRate));
            if (length <= 0) return;

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = partials[p];
                float decay = 14f + p * 11f;
                float weight = gain / (p + 1.4f);
                for (int i = 0; i < length; i++)
                {
                    float u = i / (float)SampleRate;
                    buffer[start + i] += Mathf.Sin(2f * Mathf.PI * frequency * u)
                                       * Mathf.Exp(-decay * u) * weight;
                }
            }
        }

        /// <summary>
        /// A one-pole high-pass feeding a one-pole low-pass — enough of a band-pass for noise
        /// shaping, and cheap enough to sweep its centre per sample.
        /// </summary>
        private struct BandPass
        {
            private float _low;
            private float _high;

            public float Process(float input, float centreHz, float width, int sampleRate)
            {
                float lowCut = Mathf.Max(20f, centreHz * (1f - width));
                float highCut = centreHz * (1f + width);

                _high = Mathf.Lerp(_high, input, Coefficient(lowCut, sampleRate));
                float passed = input - _high;
                _low = Mathf.Lerp(_low, passed, Coefficient(highCut, sampleRate));
                return _low;
            }

            private static float Coefficient(float cutoffHz, int sampleRate)
            {
                return Mathf.Clamp01(2f * Mathf.PI * cutoffHz / sampleRate);
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
