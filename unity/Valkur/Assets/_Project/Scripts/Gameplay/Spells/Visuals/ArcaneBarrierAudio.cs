using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The four one-shots a woven barrier makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>AudioCatalog.asset</c> contains no <c>spell_*</c> id at all, so
    /// every <c>PlaySfxById("spell_…")</c> in the project is a miss that produces one warning
    /// and no sound. <see cref="IceWallAudio"/> answered that the same way and the argument is
    /// the same one: the catalog path stays the better answer the day a recorded set is
    /// authored, and these become the fallback.</para>
    ///
    /// <para>WHY NOT REUSE THE ICE CLIPS. They are DSP describing breaking ice — a cluster of
    /// noise transients with a ringing partial, and that file says in as many words that the
    /// ring "is what makes it ICE". A ward is the opposite material: no transient, no grain, a
    /// PITCHED body. What is being described here is a chord being struck and held, then
    /// detuning as the weave fails, which is why every clip below is built from harmonics and
    /// the only noise in the file is the shimmer riding on top of them.</para>
    /// </summary>
    internal static class ArcaneBarrierAudio
    {
        private const int SampleRate = 22050;

        /// <summary>
        /// The chord the barrier is tuned to: a fifth and an octave over the root. A ward that
        /// rings a MAJOR triad sounds benevolent and a minor one sounds ominous; a bare
        /// fifth-and-octave is neither, which is what an arcane construct should be.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Four immutable frequencies. Holds no Unity objects and is " +
            "never written after init, so it cannot carry a destroyed object or a stale " +
            "registration into the next Play session. The AudioClips built FROM it are the " +
            "part that goes stale, and those are nulled by ResetStatics below.")]
        private static readonly float[] Chord = { 220f, 330f, 440f, 660f };

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

        /// <summary>The barrier being woven: anchors struck, then the chord rising into place.</summary>
        public static AudioClip Create()
        {
            if (_create == null) _create = BuildCreate();
            return _create;
        }

        /// <summary>A blow turned away. Bright, short, and it RINGS rather than cracks.</summary>
        public static AudioClip Hit()
        {
            if (_hit == null) _hit = BuildHit();
            return _hit;
        }

        /// <summary>The weave torn apart: the chord collapsing through a downward sweep.</summary>
        public static AudioClip Shatter()
        {
            if (_shatter == null) _shatter = BuildShatter();
            return _shatter;
        }

        /// <summary>The barrier unravelling on its own clock. Detunes and thins out.</summary>
        public static AudioClip Melt()
        {
            if (_melt == null) _melt = BuildMelt();
            return _melt;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Two events in one clip, because the effect is two events: the anchors landing (a
        /// short pitched knock at the root, twice) and the weave locking in (the full chord
        /// swelling in behind them). Playing them as one undifferentiated swell loses the
        /// causality the animation is built around.
        /// </summary>
        private static AudioClip BuildCreate()
        {
            var buffer = new float[(int)(1.20f * SampleRate)];

            AddKnock(buffer, 0, Chord[0], 0.07f, 0.55f);
            AddKnock(buffer, (int)(0.06f * SampleRate), Chord[1], 0.06f, 0.40f);

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                // The swell starts under the knocks and peaks where the weave meets in the
                // middle of the bays, ~0.5 s in, which is what the rig's KnitDelay resolves to.
                float envelope = Mathf.Pow(Mathf.Clamp01(t / 0.50f), 1.35f) *
                                 Mathf.Exp(-Mathf.Max(0f, t - 0.55f) / 0.28f);

                float sum = 0f;
                for (int h = 0; h < Chord.Length; h++)
                {
                    // Each partial arrives slightly after the one below it, so the chord
                    // assembles from the root upward instead of switching on complete.
                    float delay = h * 0.055f;
                    if (t < delay) continue;
                    sum += Mathf.Sin(2f * Mathf.PI * Chord[h] * (t - delay)) / (h + 1.4f);
                }

                buffer[i] += sum * envelope * 0.60f + Shimmer(t, envelope * 0.16f);
            }

            return Finish("ArcaneBarrier_Create", buffer);
        }

        private static AudioClip BuildHit()
        {
            var buffer = new float[(int)(0.34f * SampleRate)];

            // An octave above the barrier's root: a blow lands ON the surface, and a surface
            // struck answers above its own fundamental.
            AddKnock(buffer, 0, Chord[2], 0.10f, 0.85f);
            AddKnock(buffer, (int)(0.012f * SampleRate), Chord[3] * 1.5f, 0.055f, 0.45f);

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] += Shimmer(i / (float)SampleRate,
                    Mathf.Exp(-i / (float)SampleRate / 0.06f) * 0.22f);

            return Finish("ArcaneBarrier_Hit", buffer);
        }

        /// <summary>
        /// The chord losing its tuning and falling. A downward pitch sweep is what a structure
        /// coming apart sounds like; a burst of noise is what RUBBLE sounds like, and there is
        /// no rubble here.
        /// </summary>
        private static AudioClip BuildShatter()
        {
            var buffer = new float[(int)(0.85f * SampleRate)];
            var phases = new float[Chord.Length];

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / (buffer.Length / (float)SampleRate);
                float envelope = Mathf.Pow(Mathf.Clamp01(t / 0.015f), 0.7f) *
                                 Mathf.Pow(1f - progress, 1.7f);

                float sum = 0f;
                for (int h = 0; h < Chord.Length; h++)
                {
                    // Integrating the phase rather than writing sin(2*pi*f(t)*t) is what makes
                    // the sweep continuous: the closed form jumps whenever f changes, because
                    // the whole elapsed time is multiplied by the NEW frequency.
                    float frequency = Chord[h] * Mathf.Lerp(1f, 0.34f, Mathf.Pow(progress, 0.8f));
                    phases[h] += 2f * Mathf.PI * frequency / SampleRate;
                    sum += Mathf.Sin(phases[h]) / (h + 1.2f);
                }

                buffer[i] = sum * envelope * 0.75f + Shimmer(t, envelope * 0.30f);
            }

            return Finish("ArcaneBarrier_Shatter", buffer);
        }

        /// <summary>
        /// Expiry: the partials drift apart and thin out. Detuning rather than sweeping, so a
        /// barrier that runs out of time does not sound like one that was broken.
        /// </summary>
        private static AudioClip BuildMelt()
        {
            var buffer = new float[(int)(0.90f * SampleRate)];
            var phases = new float[Chord.Length];

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / (buffer.Length / (float)SampleRate);
                float envelope = Mathf.Pow(Mathf.Clamp01(t / 0.10f), 1.2f) *
                                 Mathf.Pow(1f - progress, 1.4f);

                float sum = 0f;
                for (int h = 0; h < Chord.Length; h++)
                {
                    // Each partial drifts by a different amount and in a different direction:
                    // one shared detune is a pitch bend, several disagreeing ones is a chord
                    // coming untied, which is the thing being described.
                    float drift = 1f + (h % 2 == 0 ? 1f : -1f) * progress * (0.03f + h * 0.012f);
                    phases[h] += 2f * Mathf.PI * Chord[h] * drift / SampleRate;
                    // The upper partials leave first, so the sound HOLLOWS rather than fading.
                    sum += Mathf.Sin(phases[h]) / (h + 1.4f) * Mathf.Pow(1f - progress, h * 0.9f);
                }

                buffer[i] = sum * envelope * 0.55f + Shimmer(t, envelope * 0.12f);
            }

            return Finish("ArcaneBarrier_Melt", buffer);
        }

        /// <summary>
        /// A pitched knock: a fast-decaying sine with its own octave over it. No noise at all —
        /// noise is what makes a hit read as MATTER, and none of this is matter.
        /// </summary>
        private static void AddKnock(float[] buffer, int start, float frequency,
            float decaySeconds, float amplitude)
        {
            if (start < 0) start = 0;
            int length = Mathf.Min(buffer.Length - start, (int)(decaySeconds * 6f * SampleRate));
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t / decaySeconds);
                buffer[start + i] += amplitude * envelope *
                    (Mathf.Sin(2f * Mathf.PI * frequency * t) +
                     0.45f * Mathf.Sin(4f * Mathf.PI * frequency * t));
            }
        }

        /// <summary>
        /// The one non-pitched element: a fast tremolo high above the chord. It is what stops
        /// the clips sounding like a synth pad — an arcane surface is ACTIVE, and activity at
        /// this register is heard as air moving over something rather than as a note.
        /// </summary>
        private static float Shimmer(float t, float amplitude)
            => amplitude * Mathf.Sin(2f * Mathf.PI * 2640f * t) *
               (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 17f * t));

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
