using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>The inventory events that make a sound.</summary>
    public enum InventorySound
    {
        Open = 0,
        Pickup = 1,
        PickupRare = 2,
        Equip = 3,
        Unequip = 4,
        Refuse = 5,
        Coin = 6,
        Drop = 7,
        Consume = 8,
        Sort = 9,
        Settle = 10,
    }

    /// <summary>
    /// The window's sounds. A catalogue id from <see cref="InventoryHudStyle"/> is tried first,
    /// gated on <c>HasSfx</c> (an unresolved id warns once by design, and a speculative id must
    /// not push a warning into a console this project requires to be clean). Every event without
    /// a recorded clip falls back to a sound SYNTHESISED here — the catalogue holds exactly one
    /// inventory id (<c>inv_open</c>), the same situation <c>IceWallAudio</c> and
    /// <c>ShieldAudio</c> answer the same way.
    ///
    /// <para>Played through <see cref="IAudioService.PlaySFX"/> so the player's effects volume
    /// applies. Short and soft on purpose: these are confirmation, not reward, and a window the
    /// player opens a hundred times a session must not tire the ear.</para>
    /// </summary>
    internal static class InventoryAudio
    {
        private const int SampleRate = 22050;

        private static Dictionary<InventorySound, AudioClip> s_clips = new Dictionary<InventorySound, AudioClip>();

        /// <summary>
        /// Domain Reload is OFF: the managed handles survive a recompile while the native clips do
        /// not, so a cached entry would be a destroyed clip on the second Play.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_clips = new Dictionary<InventorySound, AudioClip>();

        public static void Play(InventorySound sound, InventoryHudStyle style, float volumeScale = 1f)
        {
            if (!Application.isPlaying || style == null) return;
            if (!ServiceLocator.TryGet<IAudioService>(out var audio) || audio == null) return;
            float v = style.volume * volumeScale;

            string id = IdFor(sound, style);
            if (!string.IsNullOrEmpty(id) && audio.HasSfx(id))
            {
                audio.PlaySfxById(id, v);
                return;
            }
            var clip = Clip(sound);
            if (clip != null) audio.PlaySFX(clip, v);
        }

        private static string IdFor(InventorySound s, InventoryHudStyle style)
        {
            switch (s)
            {
                case InventorySound.Open: return style.sfxOpen;
                case InventorySound.Pickup:
                case InventorySound.PickupRare: return style.sfxPickup;
                case InventorySound.Equip: return style.sfxEquip;
                case InventorySound.Unequip: return style.sfxUnequip;
                case InventorySound.Refuse: return style.sfxRefuse;
                case InventorySound.Coin: return style.sfxCoin;
                case InventorySound.Drop: return style.sfxDrop;
                case InventorySound.Consume: return style.sfxConsume;
                case InventorySound.Sort: return style.sfxSort;
                default: return "";
            }
        }

        /// <summary>The synthesised clip for an event, built once.</summary>
        public static AudioClip Clip(InventorySound s)
        {
            if (s_clips.TryGetValue(s, out var c) && c != null) return c;
            c = Build(s);
            s_clips[s] = c;
            return c;
        }

        private static AudioClip Build(InventorySound s)
        {
            switch (s)
            {
                case InventorySound.Open:
                    // Leather and a buckle: a short noise swell under a soft click.
                    return Finish("inv_open_synth", Mix(0.16f, (t, i) =>
                        Noise(i) * Env(t, 0.01f, 0.12f) * 0.35f + Tone(t, 1900f) * Env(t, 0.001f, 0.03f) * 0.5f));
                case InventorySound.Pickup:
                    // Two quick rising blips.
                    return Finish("inv_pickup", Mix(0.14f, (t, i) =>
                        Tone(t, t < 0.05f ? 880f : 1320f) * Env(t < 0.05f ? t : t - 0.05f, 0.002f, 0.05f) * 0.6f));
                case InventorySound.PickupRare:
                    // A little arpeggio with a bell tail.
                    return Finish("inv_pickup_rare", Mix(0.5f, (t, i) =>
                    {
                        float f = t < 0.06f ? 784f : t < 0.12f ? 988f : 1319f;
                        float local = t < 0.06f ? t : t < 0.12f ? t - 0.06f : t - 0.12f;
                        return (Tone(t, f) + 0.4f * Tone(t, f * 2.01f)) * Env(local, 0.002f, t < 0.12f ? 0.06f : 0.35f) * 0.5f;
                    }));
                case InventorySound.Equip:
                    // Metal settling: a bright inharmonic clink over a short scrape.
                    return Finish("inv_equip", Mix(0.28f, (t, i) =>
                        (Tone(t, 1560f) + 0.6f * Tone(t, 2437f) + 0.35f * Tone(t, 3310f)) * Env(t, 0.001f, 0.16f) * 0.45f
                        + Noise(i) * Env(t, 0.002f, 0.04f) * 0.25f));
                case InventorySound.Unequip:
                    return Finish("inv_unequip", Mix(0.2f, (t, i) =>
                        (Tone(t, 1040f) + 0.5f * Tone(t, 1630f)) * Env(t, 0.001f, 0.11f) * 0.4f
                        + Noise(i) * Env(t, 0.002f, 0.03f) * 0.2f));
                case InventorySound.Refuse:
                    // A low, dull double knock — "no", without a buzzer's rudeness.
                    return Finish("inv_refuse", Mix(0.2f, (t, i) =>
                    {
                        float local = t < 0.09f ? t : t - 0.09f;
                        return Tone(t, 150f - local * 300f) * Env(local, 0.002f, 0.06f) * 0.8f;
                    }));
                case InventorySound.Coin:
                    return Finish("inv_coin", Mix(0.32f, (t, i) =>
                    {
                        float local = t < 0.07f ? t : t - 0.07f;
                        float f = t < 0.07f ? 2093f : 2637f;
                        return (Tone(t, f) + 0.3f * Tone(t, f * 2.76f)) * Env(local, 0.001f, t < 0.07f ? 0.06f : 0.22f) * 0.45f;
                    }));
                case InventorySound.Drop:
                    // A soft thud: a falling low sine with a puff of noise.
                    return Finish("inv_drop", Mix(0.18f, (t, i) =>
                        Tone(t, 120f + 180f * Mathf.Exp(-t * 30f)) * Env(t, 0.002f, 0.1f) * 0.8f
                        + Noise(i) * Env(t, 0.001f, 0.03f) * 0.2f));
                case InventorySound.Consume:
                    // A gulp: a bubble that rises in pitch, twice.
                    return Finish("inv_consume", Mix(0.3f, (t, i) =>
                    {
                        float local = t < 0.13f ? t : t - 0.13f;
                        return Tone(t, 280f + local * 1600f) * Env(local, 0.004f, 0.09f) * 0.6f;
                    }));
                case InventorySound.Sort:
                    // Things being shuffled: four short noise ticks.
                    return Finish("inv_sort", Mix(0.26f, (t, i) =>
                    {
                        float local = t % 0.06f;
                        return Noise(i) * Env(local, 0.001f, 0.025f) * 0.5f + Tone(t, 700f + 300f * Mathf.Floor(t / 0.06f)) * Env(local, 0.001f, 0.02f) * 0.2f;
                    }));
                default: // Settle
                    return Finish("inv_settle", Mix(0.07f, (t, i) =>
                        Tone(t, 1200f) * Env(t, 0.001f, 0.025f) * 0.35f + Noise(i) * Env(t, 0.001f, 0.012f) * 0.2f));
            }
        }

        private delegate float Sampler(float t, int i);

        private static float[] Mix(float seconds, Sampler s)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var buf = new float[n];
            for (int i = 0; i < n; i++) buf[i] = s(i / (float)SampleRate, i);
            return buf;
        }

        private static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

        /// <summary>A linear attack and an exponential decay to ~1 % at <paramref name="decay"/>.</summary>
        private static float Env(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / Mathf.Max(1e-5f, attack);
            return Mathf.Exp(-(t - attack) * 4.6f / Mathf.Max(1e-4f, decay));
        }

        private static float Noise(int i)
        {
            unchecked
            {
                uint h = (uint)(i * 747796405 + 2891336453);
                h = ((h >> (int)((h >> 28) + 4)) ^ h) * 277803737u;
                h = (h >> 22) ^ h;
                return h / (float)uint.MaxValue * 2f - 1f;
            }
        }

        private static AudioClip Finish(string name, float[] buffer)
        {
            float peak = 0f;
            for (int i = 0; i < buffer.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buffer[i]));
            if (peak > 1e-4f)
            {
                float gain = 0.8f / peak;
                for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
            }
            // A 3 ms fade at both ends: a clip that starts or stops on a non-zero sample clicks.
            int fade = Mathf.Min(buffer.Length / 2, SampleRate * 3 / 1000);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                buffer[i] *= k;
                buffer[buffer.Length - 1 - i] *= k;
            }
            var clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
