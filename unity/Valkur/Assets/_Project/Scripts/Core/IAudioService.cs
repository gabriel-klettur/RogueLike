using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Audio service interface defined in Core so any layer can request audio
    /// without cross-asmdef reflection. Implemented by AudioManager in Infrastructure.
    /// Mirrors Python AudioBus public API.
    /// </summary>
    public interface IAudioService
    {
        // ── Music ───────────────────────────────────────────────────────────
        void PlayMusic(AudioClip clip);
        void PlayMusic(AudioClip clip, float fadeInSec);
        void CrossfadeTo(AudioClip clip, float durationSec = 0.6f);
        void StopMusic();
        void StopMusic(float fadeOutSec);
        void SetMusicVolume(float vol);

        // ── SFX ─────────────────────────────────────────────────────────────
        void PlaySFX(AudioClip clip, float volumeScale = 1f);
        void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f);

        /// <summary>Play SFX by catalog ID (resolved via AudioCatalogSO).</summary>
        void PlaySfxById(string sfxId, float volumeScale = 1f);

        /// <summary>Play random clip from a list of catalog IDs.</summary>
        void PlaySfxRandom(string[] sfxIds, float volumeScale = 1f);

        void SetSFXVolume(float vol);

        // ── Ambient ─────────────────────────────────────────────────────────
        void SetAmbientVolume(float vol);
        void EnableAmbient(string[] choiceIds, float minInterval, float maxInterval);
        void DisableAmbient();

        // ── Playlist ────────────────────────────────────────────────────────
        void StartPlaylist(AudioClip[] tracks, float intervalSec = 120f, bool shuffle = true);
        void StopPlaylist();

        // ── Queries ─────────────────────────────────────────────────────────
        bool IsMusicPlaying { get; }
        AudioClip CurrentMusicClip { get; }
        string CurrentTrackTitle { get; }

        // ── Zone-aware transitions ──────────────────────────────────────────
        /// <summary>
        /// Notify the audio system of a zone change so it can resolve the
        /// correct music track and ambient sounds from the catalog.
        /// </summary>
        void OnZoneChanged(string zoneName, string levelName = null, string biomeName = null);

        /// <summary>Begin in-game audio (playlist + ambient) from catalog defaults.</summary>
        void EnterGameAudio();

        /// <summary>Play menu startup music from catalog.</summary>
        void PlayMenuMusic();

        /// <summary>Fade out menu music, then start in-game audio.</summary>
        void TransitionMenuToGame();

        /// <summary>Re-apply volumes from GameSettings.</summary>
        void ApplySettings();
    }
}
