using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sounds a firework makes, synthesised at runtime.
    ///
    /// <para>WHY GENERATED. <c>FireworkLaunchExecutor</c> has always asked the catalog for
    /// <c>spell_firework_launch</c>, and <c>AudioCatalog.asset</c> contains no <c>spell_*</c>
    /// id at all — so the only thing that call ever produced was one warning per session, in a
    /// console this project requires to be clean. <see cref="ShieldAudio"/> and
    /// <see cref="IceWallAudio"/> answered the same problem the same way; the catalog stays the
    /// better answer the day a recorded set is authored, and these become the fallback.</para>
    ///
    /// <para>THE WHISTLE IS NOT DECORATION. A firework is the one effect whose sound arrives
    /// BEFORE its picture — the rising shriek is what makes a player look up, so the burst lands
    /// on an eye that is already pointed at it. Without it the detonation is the first thing
    /// they hear and half of it is over before they find it.</para>
    /// </summary>
    internal static class FireworkAudio
    {
        private const int SampleRate = 22050;

        private static AudioClip _launch;
        private static AudioClip _whistle;
        private static AudioClip _burst;
        private static AudioClip _companion;

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// AudioClips do not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _launch = null;
            _whistle = null;
            _burst = null;
            _companion = null;
        }

        /// <summary>The mortar: a body-felt thump with a short hiss of propellant on top.</summary>
        public static AudioClip Launch()
        {
            if (_launch == null) _launch = BuildLaunch();
            return _launch;
        }

        /// <summary>The shell climbing. A rising sweep with the wobble of something spinning.</summary>
        public static AudioClip Whistle()
        {
            if (_whistle == null) _whistle = BuildWhistle();
            return _whistle;
        }

        /// <summary>The primary detonation: a boom that decays into a full second of crackle.</summary>
        public static AudioClip Burst()
        {
            if (_burst == null) _burst = BuildBurst();
            return _burst;
        }

        /// <summary>A companion shell. Higher, drier, and over quickly.</summary>
        public static AudioClip Companion()
        {
            if (_companion == null) _companion = BuildCompanion();
            return _companion;
        }

        // ── synthesis ────────────────────────────────────────────────────────────────

        private static AudioClip BuildLaunch()
        {
            var rng = new System.Random(90412);
            const float seconds = 0.40f;
            var buffer = new float[(int)(seconds * SampleRate)];

            // The launch is mostly BELOW the crackle band. A firework going up is felt more than
            // heard, and pitching it up would make it compete with its own burst a second later.
            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;
                float thumpHz = Mathf.Lerp(150f, 58f, Mathf.Pow(progress, 0.5f));
                buffer[i] = Mathf.Sin(2f * Mathf.PI * thumpHz * t) *
                            Mathf.Exp(-progress * 5.5f) * 0.75f;
            }

            // Propellant. A short filtered hiss, brightest at the very start.
            float previous = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float progress = i / (float)buffer.Length;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                previous = Mathf.Lerp(previous, noise, 0.55f);   // one-pole low pass
                buffer[i] += previous * Mathf.Exp(-progress * 9f) * 0.30f;
            }

            return Finish("Firework_Launch", buffer);
        }

        private static AudioClip BuildWhistle()
        {
            const float seconds = 0.80f;
            var buffer = new float[(int)(seconds * SampleRate)];

            // Phase is INTEGRATED rather than computed as frequency x t. Writing sin(2 pi f(t) t)
            // for a swept f is the classic mistake: it is not the sweep it looks like, and the
            // result glides through frequencies nobody asked for and clicks where the two
            // disagree. Accumulating phase per sample is exact at any sweep shape.
            float phase = 0f;
            float vibratoPhase = 0f;

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;

                // Up, then just over the top. The shell is decelerating as it reaches its apex
                // and a whistle that only rises never sounds like it arrived anywhere.
                float baseHz = Mathf.Lerp(700f, 1900f, Mathf.Pow(Mathf.Min(progress / 0.82f, 1f), 0.75f));
                if (progress > 0.82f) baseHz = Mathf.Lerp(1900f, 1560f, (progress - 0.82f) / 0.18f);

                // The wobble of a shell tumbling as it climbs. Without it the sweep is a test tone.
                vibratoPhase += 2f * Mathf.PI * 17f / SampleRate;
                float hz = baseHz * (1f + 0.035f * Mathf.Sin(vibratoPhase));

                phase += 2f * Mathf.PI * hz / SampleRate;

                // A touch of the second harmonic keeps it reedy rather than pure.
                float envelope = Mathf.Clamp01(progress / 0.09f) * Mathf.Pow(1f - progress, 0.55f);
                buffer[i] = (Mathf.Sin(phase) + 0.22f * Mathf.Sin(phase * 2f)) * envelope * 0.42f;
            }

            return Finish("Firework_Whistle", buffer, peakTarget: 0.62f);
        }

        private static AudioClip BuildBurst() => BuildDetonation(
            "Firework_Burst", seed: 27714, seconds: 1.35f,
            boomHzStart: 210f, boomHzEnd: 42f, boomAmplitude: 0.95f,
            crackleGrains: 54, crackleSpread: 0.95f, crackleHzLow: 1500f, crackleHzHigh: 6200f);

        private static AudioClip BuildCompanion() => BuildDetonation(
            "Firework_Companion", seed: 61885, seconds: 0.72f,
            boomHzStart: 340f, boomHzEnd: 96f, boomAmplitude: 0.62f,
            crackleGrains: 24, crackleSpread: 0.45f, crackleHzLow: 2400f, crackleHzHigh: 7400f);

        /// <summary>
        /// A detonation is two sounds with different clocks: the BOOM is one event that decays,
        /// the CRACKLE is a shower of hundreds spread over the second after it. Sharing one
        /// envelope for both is what makes a synthesised explosion sound like a door slamming.
        /// </summary>
        private static AudioClip BuildDetonation(string name, int seed, float seconds,
            float boomHzStart, float boomHzEnd, float boomAmplitude,
            int crackleGrains, float crackleSpread, float crackleHzLow, float crackleHzHigh)
        {
            var rng = new System.Random(seed);
            var buffer = new float[(int)(seconds * SampleRate)];

            float phase = 0f;
            float lowPass = 0f;

            for (int i = 0; i < buffer.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / seconds;

                // The pressure wave: a fast downward sweep, integrated the same way the whistle
                // is, under an envelope that is essentially over in a quarter of a second.
                float hz = Mathf.Lerp(boomHzStart, boomHzEnd, Mathf.Pow(Mathf.Min(progress * 4.5f, 1f), 0.5f));
                phase += 2f * Mathf.PI * hz / SampleRate;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-progress * 13f) * boomAmplitude;

                // Rumble: heavily low-passed noise that outlasts the sweep and gives the boom a
                // tail instead of a cliff.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lowPass = Mathf.Lerp(lowPass, noise, 0.10f);
                buffer[i] = boom + lowPass * Mathf.Exp(-progress * 6.5f) * boomAmplitude * 0.55f;
            }

            // The crackle. Densest right after the bang and thinning out, which is what
            // Pow(random, 0.55) buys over a uniform spread — a uniform one sounds like applause.
            for (int g = 0; g < crackleGrains; g++)
            {
                float when = Mathf.Pow((float)rng.NextDouble(), 0.55f) * crackleSpread;
                float remaining = 1f - (when / seconds);
                AddGrain(buffer, (int)(when * SampleRate),
                    amplitude: (0.10f + 0.20f * (float)rng.NextDouble()) * remaining,
                    decaySeconds: 0.006f + 0.020f * (float)rng.NextDouble(),
                    frequency: Mathf.Lerp(crackleHzLow, crackleHzHigh, (float)rng.NextDouble()),
                    rng, noiseMix: 0.65f);
            }

            return Finish(name, buffer);
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
