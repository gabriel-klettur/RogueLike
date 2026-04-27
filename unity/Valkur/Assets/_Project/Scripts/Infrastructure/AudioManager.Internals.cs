using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Infrastructure
{
    public partial class AudioManager
    {
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Ducking
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private void TryAutoDuck(string sfxId)
        {
            if (catalog == null || string.IsNullOrEmpty(sfxId)) return;
            if (catalog.DuckingPrefixes == null) return;

            foreach (var prefix in catalog.DuckingPrefixes)
            {
                if (sfxId.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDucking();
                    return;
                }
            }
        }

        private void ApplyDucking()
        {
            if (_duckingCoroutine != null)
                StopCoroutine(_duckingCoroutine);
            _duckingCoroutine = StartCoroutine(DuckingRoutine());
        }

        private IEnumerator DuckingRoutine()
        {
            float duckDb   = catalog != null ? catalog.DuckingAmountDb : -4f;
            float holdSec  = (catalog != null ? catalog.DuckingHoldMs : 250f) / 1000f;
            float relSec   = (catalog != null ? catalog.DuckingReleaseMs : 200f) / 1000f;

            // Convert dB to linear multiplier
            float duckLinear = Mathf.Pow(10f, duckDb / 20f);
            _duckTarget = duckLinear;
            ApplyMusicVolume();

            // Hold
            yield return new WaitForSeconds(holdSec);

            // Release (ramp back up)
            float elapsed = 0f;
            while (elapsed < relSec)
            {
                elapsed += Time.deltaTime;
                _duckTarget = Mathf.Lerp(duckLinear, 1f, elapsed / relSec);
                ApplyMusicVolume();
                yield return null;
            }

            _duckTarget = 1f;
            ApplyMusicVolume();
            _duckingCoroutine = null;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Internal helpers
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private float EffectiveMusicVolume => _musicVolume * _duckTarget;

        private void ApplyMusicVolume()
        {
            if (_activeMusicSource != null && _crossfadeCoroutine == null)
                _activeMusicSource.volume = EffectiveMusicVolume;
        }

        private float GetGroupVolume(string group)
        {
            if (string.IsNullOrEmpty(group)) return _sfxVolume;
            switch (group.ToLowerInvariant())
            {
                case "ambient": return _ambientVolume;
                case "music":   return _musicVolume;
                default:        return _sfxVolume;
            }
        }

        private void StopCrossfade()
        {
            if (_crossfadeCoroutine != null)
            {
                StopCoroutine(_crossfadeCoroutine);
                _crossfadeCoroutine = null;
            }
        }

        private IEnumerator CrossfadeRoutine(AudioSource from, AudioSource to, float duration)
        {
            float startVol = from.volume;
            float targetVol = EffectiveMusicVolume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                from.volume = Mathf.Lerp(startVol, 0f, t);
                to.volume   = Mathf.Lerp(0f, targetVol, t);
                yield return null;
            }

            from.Stop();
            from.clip   = null;
            from.volume = 0f;
            to.volume   = targetVol;
            _crossfadeCoroutine = null;
        }

        private IEnumerator FadeSource(AudioSource src, float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                src.volume = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            src.volume = to;
            _crossfadeCoroutine = null;
        }

        private IEnumerator FadeAndStop(AudioSource src, float duration)
        {
            float startVol = src.volume;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                src.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
                yield return null;
            }
            src.Stop();
            src.clip   = null;
            src.volume = 0f;
            _crossfadeCoroutine = null;
        }

        private string ResolveTrackTitle(AudioClip clip)
        {
            if (catalog == null || clip == null) return null;
            foreach (var t in catalog.Tracks)
            {
                if (t.clip == clip)
                    return t.title;
            }
            return clip.name;
        }

        /// <summary>
        /// Resolves the catalog entry for the given clip, updates current-track state
        /// and raises <see cref="OnTrackChanged"/> if the track actually changed.
        /// </summary>
        private void SetCurrentTrack(AudioClip clip)
        {
            MusicTrackEntry entry = null;
            if (catalog != null && clip != null)
            {
                foreach (var t in catalog.Tracks)
                {
                    if (t.clip == clip) { entry = t; break; }
                }
            }

            string newId    = entry?.id ?? (clip != null ? clip.name : string.Empty);
            string newTitle = entry?.title ?? (clip != null ? clip.name : null);

            _currentTrack      = entry;
            _currentTrackId    = newId;
            _currentTrackTitle = newTitle;

            float bpm        = entry != null ? entry.bpm         : 0f;
            int   beatsPerBar = entry != null ? Mathf.Max(1, entry.beatsPerBar) : 4;
            try
            {
                OnTrackChanged?.Invoke(newId, newTitle, bpm, beatsPerBar);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AudioManager] OnTrackChanged subscriber threw: {ex.Message}");
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Public convenience: apply settings from GameSettings
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        /// <summary>
        /// Re-read volumes from GameSettings and apply them.
        /// Called after PauseMenu saves settings.
        /// </summary>
        public void ApplySettings()
        {
            var gs = GameSettings.Instance;
            SetMusicVolume(gs.musicVolume);
            SetSFXVolume(gs.sfxVolume);
            SetAmbientVolume(gs.ambientVolume);
        }

        /// <summary>
        /// Convenience: begin in-game audio from catalog defaults.
        /// Plays the default playlist and enables default ambient.
        /// </summary>
        public void EnterGameAudio()
        {
            if (catalog == null) return;

            var clips = catalog.BuildPlaylistClips();
            if (clips.Length > 0)
                StartPlaylist(clips, catalog.PlaylistIntervalSec, catalog.PlaylistShuffle);
            else
            {
                var defaultClip = catalog.GetTrackClip(catalog.IngameTrackId);
                if (defaultClip != null) CrossfadeTo(defaultClip, catalog.CrossfadeSec);
            }

            EnableAmbient(catalog.DefaultAmbientChoices,
                catalog.DefaultAmbientMinInterval,
                catalog.DefaultAmbientMaxInterval);
        }

        /// <summary>
        /// Convenience: play menu startup music.
        /// </summary>
        public void PlayMenuMusic()
        {
            if (catalog == null) return;
            DisableAmbient();
            StopPlaylistInternal();
            var clip = catalog.GetTrackClip(catalog.StartupTrackId);
            if (clip != null) PlayMusic(clip, catalog.CrossfadeSec);
        }

        /// <summary>
        /// Convenience: transition from menu to in-game audio.
        /// Fades out menu music, then starts in-game playlist + ambient.
        /// </summary>
        public void TransitionMenuToGame()
        {
            if (catalog == null) return;
            float fadeOut = catalog.MenuFadeOutSec;
            StopMusic(fadeOut);
            StartCoroutine(DelayedEnterGame(fadeOut + 0.1f));
        }

        private IEnumerator DelayedEnterGame(float delay)
        {
            yield return new WaitForSeconds(delay);
            EnterGameAudio();
        }

        /// <summary>
        /// Called by ZoneManager when zone changes.
        /// Resolves the correct track and ambient for the new zone/biome/level.
        /// </summary>
        public void OnZoneChanged(string zoneName, string levelName = null, string biomeName = null)
        {
            if (catalog == null) return;

            // Resolve music
            string trackId = catalog.ResolveTrackId(zoneName, levelName, biomeName);
            var clip = catalog.GetTrackClip(trackId);
            if (clip != null && clip != CurrentMusicClip)
            {
                // Only crossfade if not already in playlist mode OR track is different scope
                StopPlaylistInternal();
                CrossfadeTo(clip, catalog.CrossfadeSec);
            }

            // Resolve ambient
            catalog.ResolveAmbient(zoneName, out var choices, out float minI, out float maxI);
            if (choices != null && choices.Length > 0)
                EnableAmbient(choices, minI, maxI);
            else
                DisableAmbient();
        }
    }
}
