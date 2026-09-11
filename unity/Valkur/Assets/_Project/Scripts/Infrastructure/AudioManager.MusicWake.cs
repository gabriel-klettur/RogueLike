using UnityEngine;

namespace Valkur.Infrastructure
{
    public partial class AudioManager
    {
        /// <summary>
        /// The volume a starved music voice is lifted to for a few frames: -54 dBFS, for about
        /// fifty milliseconds. Inaudible, and above the level Unity starts a voice real at.
        /// </summary>
        public const float MusicWakeVolume = 2e-3f;

        private const int MusicWakeFrames = 3;
        private const float MusicWakeCooldown = 2f;

        private int _musicWakeFramesLeft;
        private float _musicWakeReadyAt;

        /// <summary>
        /// Keeps the active music voice REAL at the silence floor, so the music panel's
        /// resonance still sees the song while the player has the music muted.
        ///
        /// <para>Measured on Unity 2022.3: a source that starts quieter than somewhere between
        /// 3e-4 and 1e-3 is started VIRTUAL — its <c>OnAudioFilterRead</c> receives exact zeros
        /// — and stays virtual at <see cref="MusicSilenceFloor"/>. Lifted once above 1e-3 it goes
        /// real, and it STAYS real when brought back down to 1e-4 (filter peak 7e-5). So a
        /// muted player's track fades in to 1e-4 and is never fed, unless it is woken.</para>
        ///
        /// <para>A wake only happens when it is needed (the tap has seen nothing but zeros for
        /// 0.3 s while the source plays below the wake level), at most every two seconds, and
        /// never during a crossfade, which owns the volume while it runs.</para>
        /// </summary>
        private void Update()
        {
            var src = _activeMusicSource;
            if (src == null) return;

            if (_musicWakeFramesLeft > 0)
            {
                if (--_musicWakeFramesLeft == 0 && _crossfadeCoroutine == null)
                    src.volume = EffectiveMusicVolume;
                return;
            }

            if (_crossfadeCoroutine != null || !src.isPlaying || _isPaused) return;
            if (EffectiveMusicVolume >= MusicWakeVolume || Time.unscaledTime < _musicWakeReadyAt) return;
            var tap = src.GetComponent<MusicSignalTap>();
            if (tap == null || !tap.Starved) return;

            _musicWakeReadyAt = Time.unscaledTime + MusicWakeCooldown;
            _musicWakeFramesLeft = MusicWakeFrames;
            src.volume = MusicWakeVolume;
        }
    }
}
