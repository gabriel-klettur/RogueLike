using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The five sounds of the pre-game menus: move, confirm, cancel, refuse, start.
    ///
    /// <para><b>What it replaces: nothing.</b> A grep for <c>PlaySFX</c>, <c>PlaySfx</c> and
    /// <c>HasSfx</c> across <c>UI/MainMenu</c>, <c>UI/Loading</c> and <c>UI/PauseMenu</c>
    /// returned zero results. The menu played music and was otherwise silent — every key press,
    /// every confirmation and every refusal produced no sound at all.</para>
    ///
    /// <para><b>Catalogue first, synthesis second.</b> <c>AudioCatalog.asset</c> holds no
    /// <c>ui_*</c> id, and <c>PlaySfxById</c> warns once per unresolved id BY DESIGN — an
    /// explicit id that fails to resolve is a data bug. So each sound is tried through
    /// <c>HasSfx</c> and synthesised when absent, which is the shape <c>InventoryAudio</c>,
    /// <c>IceWallAudio</c>, <c>ShieldAudio</c> and <c>BoomerangAudio</c> already use. The day
    /// somebody records a menu set, the ids resolve and the synthesis stops being reached.</para>
    ///
    /// <para><b>Everything goes through <c>IAudioService.PlaySFX</c></b> so the effects volume
    /// applies. A clip played on a bare <c>AudioSource</c> would ignore the one slider the
    /// player set two screens ago.</para>
    ///
    /// <para><b>The five are DISTINCT in pitch direction, not just in pitch.</b> Confirm rises,
    /// cancel falls, refuse is a flat dull thud. A player learns "that did not work" from the
    /// shape of the sound long before they read the row that says so.</para>
    /// </summary>
    public sealed class MenuSfx
    {
        private const int SampleRate = 44100;

        private readonly MenuStyle _style;
        private AudioClip _move, _confirm, _cancel, _refuse, _start;

        public MenuSfx(MenuStyle style)
        {
            _style = style != null ? style : MenuStyle.Active;
        }

        public void Move() => Play(_style.sfxMove, ref _move, BuildMove, 0.5f);
        public void Confirm() => Play(_style.sfxConfirm, ref _confirm, BuildConfirm, 1f);
        public void Cancel() => Play(_style.sfxCancel, ref _cancel, BuildCancel, 0.8f);
        public void Refuse() => Play(_style.sfxRefuse, ref _refuse, BuildRefuse, 0.9f);
        public void Start() => Play(_style.sfxStart, ref _start, BuildStart, 1f);

        private void Play(string id, ref AudioClip cache, System.Func<AudioClip> build, float gain)
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;

            if (!string.IsNullOrEmpty(id) && audio.HasSfx(id))
            {
                audio.PlaySfxById(id);
                return;
            }

            if (cache == null) cache = build();
            if (cache == null) return;
            audio.PlaySFX(cache, _style.sfxVolume * gain);
        }

        // ── Synthesis ────────────────────────────────────────────────────────
        //
        // Short, quiet and deliberately unmusical. A menu click that carries a pitch belongs to
        // a key; these are wood and felt, which is what a menu sounds like when it is not trying
        // to be an instrument.

        /// <summary>A soft click. 40 ms, so holding a direction does not turn into a buzz.</summary>
        private static AudioClip BuildMove()
            => Build("ui_move", 0.040f, (t, n) =>
            {
                float env = Mathf.Exp(-t * 90f);
                float body = Mathf.Sin(2f * Mathf.PI * 880f * t);
                float click = Noise(n) * Mathf.Exp(-t * 420f);
                return (body * 0.35f + click * 0.5f) * env;
            });

        /// <summary>Two notes a fifth apart, the second above the first. Rising means yes.</summary>
        private static AudioClip BuildConfirm()
            => Build("ui_confirm", 0.170f, (t, n) =>
            {
                float env = Mathf.Exp(-t * 16f);
                float f = t < 0.055f ? 523.25f : 783.99f;   // C5 then G5
                float tone = Mathf.Sin(2f * Mathf.PI * f * t)
                           + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t);
                return tone * env * 0.5f;
            });

        /// <summary>The same interval, downward. Falling means no, and it is the same voice.</summary>
        private static AudioClip BuildCancel()
            => Build("ui_cancel", 0.160f, (t, n) =>
            {
                float env = Mathf.Exp(-t * 18f);
                float f = t < 0.055f ? 659.25f : 392.00f;   // E5 then G4
                return Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.45f;
            });

        /// <summary>
        /// A dull thud with no pitch movement at all. A refusal that used a musical interval
        /// would be heard as a different confirmation; what says "nothing happened" is the
        /// absence of any direction.
        /// </summary>
        private static AudioClip BuildRefuse()
            => Build("ui_refuse", 0.140f, (t, n) =>
            {
                float env = Mathf.Exp(-t * 26f);
                float body = Mathf.Sin(2f * Mathf.PI * 138f * t);
                float grit = Noise(n) * Mathf.Exp(-t * 60f) * 0.35f;
                return (body * 0.7f + grit) * env * 0.55f;
            });

        /// <summary>The one long sound: a swell under the transition into the game.</summary>
        private static AudioClip BuildStart()
            => Build("ui_start", 0.85f, (t, n) =>
            {
                float attack = Mathf.Clamp01(t / 0.18f);
                float env = attack * Mathf.Exp(-Mathf.Max(0f, t - 0.18f) * 3.4f);
                float f = Mathf.Lerp(196f, 293.66f, Mathf.Clamp01(t / 0.6f));   // G3 up to D4
                float tone = Mathf.Sin(2f * Mathf.PI * f * t)
                           + 0.45f * Mathf.Sin(2f * Mathf.PI * f * 1.5f * t)
                           + 0.2f * Mathf.Sin(2f * Mathf.PI * f * 3f * t);
                return tone * env * 0.34f;
            });

        private static AudioClip Build(string name, float seconds, System.Func<float, int, float> shape)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(shape(t, i), -1f, 1f);
            }
            // A two-millisecond fade at the tail, or the clip ends on a non-zero sample and every
            // one of these clicks at the end as well as the start.
            int tail = Mathf.Min(count, SampleRate / 500);
            for (int i = 0; i < tail; i++)
                data[count - 1 - i] *= i / (float)tail;

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        /// <summary>A deterministic hash rather than <c>Random</c>: the same click every time,
        /// and no draw from a sequence a spell cast elsewhere is also pulling from.</summary>
        private static float Noise(int n)
        {
            uint h = (uint)n * 1664525u + 1013904223u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }
    }
}
