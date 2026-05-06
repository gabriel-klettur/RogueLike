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

        /// <summary>
        /// Resolve a track by its catalog id and crossfade to it. Pass
        /// <paramref name="fadeSec"/> = -1 (default) to use the catalog's
        /// configured crossfade duration. No-op when the id, catalog, or
        /// resolved clip is missing.
        /// </summary>
        void PlayMusicByTrackId(string trackId, float fadeSec = -1f);

        void StopMusic();
        void StopMusic(float fadeOutSec);
        void SetMusicVolume(float vol);

        // ── Transport (pause / resume / skip) ───────────────────────────────
        /// <summary>Pause the current music track, keeping its position.</summary>
        void PauseMusic();
        /// <summary>Resume a previously paused track.</summary>
        void ResumeMusic();
        /// <summary>True if the music source is currently paused.</summary>
        bool IsMusicPaused { get; }
        /// <summary>Current music master volume (0..1, before ducking).</summary>
        float MusicVolume { get; }
        /// <summary>
        /// Advance to the next track in the active playlist (wraps).
        /// No-op when no playlist is running.
        /// </summary>
        void SkipToNextTrack();
        /// <summary>Go back to the previous track in the active playlist (wraps).</summary>
        void SkipToPreviousTrack();
        /// <summary>True if a playlist is currently controlling music.</summary>
        bool HasActivePlaylist { get; }

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

        /// <summary>Catalog ID of the currently playing track (empty if none).</summary>
        string CurrentTrackId { get; }
        /// <summary>Tempo of the current track in BPM. 0 if unset.</summary>
        float CurrentTrackBpm { get; }
        /// <summary>Beats per bar (time-signature numerator) for the current track.</summary>
        int CurrentTrackBeatsPerBar { get; }
        /// <summary>Offset in seconds from clip start to the first downbeat.</summary>
        float CurrentTrackBeatOffsetSec { get; }
        /// <summary>
        /// Per-beat onset timestamps (seconds from clip start) for the current track,
        /// or <c>null</c> if no beat-map was imported. Populated by
        /// <c>tools/audio/analyze_music.py</c> + <c>tools/audio/patch_audio_catalog_bpm.py</c>
        /// and consumed by <see cref="Valkur.Infrastructure.MusicBeatClock"/> for
        /// sample-accurate sync.
        /// </summary>
        float[] CurrentTrackBeatTimes { get; }
        /// <summary>Estimated musical key (e.g. "C major"). Empty if unknown.</summary>
        string CurrentTrackKey { get; }
        /// <summary>Playback time of the active music source (seconds since clip start).</summary>
        float CurrentMusicTime { get; }

        /// <summary>Seek the active music source to <paramref name="seconds"/> (clamped to clip length).</summary>
        void SeekMusic(float seconds);

        /// <summary>
        /// Fill <paramref name="buffer"/> with the spectrum (FFT magnitudes) of the
        /// active music source. Buffer length must be a power of two between 64 and 8192.
        /// Returns false if no music is currently playing (buffer left untouched).
        /// </summary>
        bool GetMusicSpectrumData(float[] buffer, int channel = 0, FFTWindow window = FFTWindow.BlackmanHarris);

        /// <summary>
        /// Fill <paramref name="buffer"/> with the raw output samples of the active
        /// music source. Returns false if no music is currently playing.
        /// Used by visualizers that need waveform peaks (e.g. progressive overview
        /// for streaming clips that don't support AudioClip.GetData).
        /// </summary>
        bool GetMusicOutputData(float[] buffer, int channel = 0);

        /// <summary>
        /// Raised whenever the active music track changes. Args: trackId, displayTitle, bpm, beatsPerBar.
        /// MusicBeatClock and MusicPlayerHUD subscribe to this.
        /// </summary>
        event System.Action<string, string, float, int> OnTrackChanged;

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
