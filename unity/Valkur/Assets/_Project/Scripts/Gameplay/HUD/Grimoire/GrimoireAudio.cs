using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>The grimoire events that make a sound.</summary>
    public enum GrimoireSound
    {
        Open = 0,
        Learn = 1,
        Capstone = 2,
        Refuse = 3,
        School = 4,
        Select = 5,
    }

    /// <summary>
    /// The window's sounds. A catalogue id from <see cref="GrimoireStyle"/> is tried first,
    /// gated on <c>HasSfx</c> — an unresolved id warns once BY DESIGN, and a speculative id
    /// must not push a warning into a console this project requires to be clean. Everything
    /// without a recorded clip falls back to a tone synthesised here, which is the answer
    /// <c>IceWallAudio</c>, <c>ShieldAudio</c> and <c>InventoryAudio</c> all reached:
    /// <c>AudioCatalog.asset</c> contains no grimoire id at all.
    ///
    /// <para>Played through <see cref="IAudioService.PlaySFX"/> so the player's effects volume
    /// applies. Soft and short: these are confirmation, not reward. The one that is allowed to
    /// be a moment is <see cref="GrimoireSound.Capstone"/>, because finishing a school is the
    /// only thing in this window that happens once.</para>
    /// </summary>
    internal static class GrimoireAudio
    {
        private const int SampleRate = 22050;

        private static Dictionary<GrimoireSound, AudioClip> s_clips =
            new Dictionary<GrimoireSound, AudioClip>();

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native
        /// clips do not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() =>
            s_clips = new Dictionary<GrimoireSound, AudioClip>();

        public static void Play(GrimoireSound sound, GrimoireStyle style, float volumeScale = 1f)
        {
            if (!Application.isPlaying || style == null) return;
            if (!ServiceLocator.TryGet<IAudioService>(out var audio) || audio == null) return;

            float volume = style.volume * volumeScale;

            string id = IdFor(sound, style);
            if (!string.IsNullOrEmpty(id) && audio.HasSfx(id))
            {
                audio.PlaySfxById(id, volume);
                return;
            }

            var clip = Clip(sound);
            if (clip != null) audio.PlaySFX(clip, volume);
        }

        private static string IdFor(GrimoireSound sound, GrimoireStyle style)
        {
            switch (sound)
            {
                case GrimoireSound.Open:     return style.openSfxId;
                case GrimoireSound.Learn:    return style.learnSfxId;
                case GrimoireSound.Capstone: return style.learnSfxId;
                case GrimoireSound.Refuse:   return style.refuseSfxId;
                case GrimoireSound.School:   return style.schoolSfxId;
                default:                     return string.Empty;
            }
        }

        /// <summary>Public so a probe can hear one without a service.</summary>
        internal static AudioClip Clip(GrimoireSound sound)
        {
            if (s_clips.TryGetValue(sound, out var cached) && cached != null) return cached;
            var built = Build(sound);
            s_clips[sound] = built;
            return built;
        }

        private static AudioClip Build(GrimoireSound sound)
        {
            switch (sound)
            {
                // A page settling. Low, brief, no pitch to speak of.
                case GrimoireSound.Open:
                    return Finish("grimoire_open", Mix(0.16f, (t, i) =>
                        (Tone(t, 190f) * 0.35f + Noise(i) * 0.20f) * Env(t, 0.004f, 0.15f)));

                // The purchase. A rising pair a fifth apart — the only sound here that goes UP,
                // because it is the only event that means the player gained something.
                case GrimoireSound.Learn:
                    return Finish("grimoire_learn", Mix(0.30f, (t, i) =>
                        (Tone(t, 523f) * 0.30f + Tone(t, 784f) * 0.22f * Mathf.Clamp01(t * 9f))
                        * Env(t, 0.006f, 0.29f)));

                // Finishing a school. The same interval, held, with an octave over it.
                case GrimoireSound.Capstone:
                    return Finish("grimoire_capstone", Mix(0.70f, (t, i) =>
                        (Tone(t, 523f) * 0.26f
                         + Tone(t, 784f) * 0.20f * Mathf.Clamp01(t * 5f)
                         + Tone(t, 1046f) * 0.16f * Mathf.Clamp01((t - 0.12f) * 5f))
                        * Env(t, 0.01f, 0.68f)));

                // A refusal. Flat and downward, and quiet: it must not be worth triggering.
                case GrimoireSound.Refuse:
                    return Finish("grimoire_refuse", Mix(0.13f, (t, i) =>
                        Tone(t, Mathf.Lerp(230f, 170f, t / 0.13f)) * 0.22f * Env(t, 0.003f, 0.12f)));

                // Turning to another school. A soft edge, no pitch: a page, not a note.
                case GrimoireSound.School:
                    return Finish("grimoire_school", Mix(0.12f, (t, i) =>
                        Noise(i) * 0.16f * Env(t, 0.002f, 0.11f)));

                // Picking a node. Barely there — it happens on every hover-and-click.
                case GrimoireSound.Select:
                    return Finish("grimoire_select", Mix(0.06f, (t, i) =>
                        Tone(t, 660f) * 0.12f * Env(t, 0.002f, 0.055f)));
            }
            return null;
        }

        private delegate float Sampler(float t, int i);

        private static float[] Mix(float seconds, Sampler sampler)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var buffer = new float[count];
            for (int i = 0; i < count; i++)
                buffer[i] = Mathf.Clamp(sampler(i / (float)SampleRate, i), -1f, 1f);
            return buffer;
        }

        private static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

        private static float Env(float t, float attack, float decay)
        {
            if (t < attack) return attack <= 0f ? 1f : t / attack;
            float k = (t - attack) / Mathf.Max(0.0001f, decay);
            return Mathf.Clamp01(1f - k);
        }

        /// <summary>Deterministic noise: a hash, not <c>Random</c>, so the same event sounds
        /// the same every time and a test can assert on the buffer.</summary>
        private static float Noise(int i)
        {
            unchecked
            {
                int h = i * 1103515245 + 12345;
                h = (h >> 16) ^ h;
                return (h % 2000) / 1000f - 1f;
            }
        }

        private static AudioClip Finish(string name, float[] buffer)
        {
            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
