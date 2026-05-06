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
        // IAudioService â€” Music
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void PlayMusic(AudioClip clip)
        {
            CrossfadeTo(clip, catalog != null ? catalog.CrossfadeSec : defaultCrossfadeSec);
        }

        public void PlayMusic(AudioClip clip, float fadeInSec)
        {
            if (clip == null) return;
            if (_activeMusicSource.clip == clip && _activeMusicSource.isPlaying) return;

            StopCrossfade();
            _activeMusicSource.clip   = clip;
            _activeMusicSource.volume = 0f;
            _activeMusicSource.Play();
            _isPaused = false;
            SetCurrentTrack(clip);
            _crossfadeCoroutine = StartCoroutine(FadeSource(_activeMusicSource, 0f, EffectiveMusicVolume, fadeInSec));
        }

        public void CrossfadeTo(AudioClip clip, float durationSec = 0.6f)
        {
            if (clip == null) return;
            if (_activeMusicSource.clip == clip && _activeMusicSource.isPlaying) return;

            StopCrossfade();
            var from = _activeMusicSource;
            var to   = _activeMusicSource == _musicA ? _musicB : _musicA;

            to.clip   = clip;
            to.volume = 0f;
            to.Play();

            _activeMusicSource = to;
            _isPaused = false;
            SetCurrentTrack(clip);
            _crossfadeCoroutine = StartCoroutine(CrossfadeRoutine(from, to, durationSec));
        }

        /// <summary>
        /// Resolve a track id via the catalog and crossfade to it. No-op when
        /// the id is empty, the catalog is missing, or the track has no clip.
        /// Use this when the caller knows the catalog id (e.g. boss phase
        /// audio, scripted music switches) instead of holding the AudioClip
        /// directly.
        /// </summary>
        public void PlayMusicByTrackId(string trackId, float fadeSec = -1f)
        {
            if (string.IsNullOrEmpty(trackId)) return;
            if (catalog == null) return;
            var clip = catalog.GetTrackClip(trackId);
            if (clip == null) return;
            float dur = fadeSec >= 0f ? fadeSec : catalog.CrossfadeSec;
            CrossfadeTo(clip, dur);
        }

        public void StopMusic()
        {
            StopMusic(catalog != null ? catalog.MenuFadeOutSec : 0.5f);
        }

        public void StopMusic(float fadeOutSec)
        {
            StopCrossfade();
            StopPlaylistInternal();
            _isPaused = false;
            if (_activeMusicSource.isPlaying)
                _crossfadeCoroutine = StartCoroutine(FadeAndStop(_activeMusicSource, fadeOutSec));
        }

        public void SetMusicVolume(float vol)
        {
            _musicVolume = Mathf.Clamp01(vol);
            if (_crossfadeCoroutine == null && _activeMusicSource != null)
                _activeMusicSource.volume = EffectiveMusicVolume;
        }

        public void PauseMusic()
        {
            if (_activeMusicSource != null && _activeMusicSource.isPlaying)
            {
                _activeMusicSource.Pause();
                _isPaused = true;
            }
        }

        public void ResumeMusic()
        {
            if (_activeMusicSource != null && _isPaused)
            {
                _activeMusicSource.UnPause();
                _isPaused = false;
            }
        }

        public bool  IsMusicPaused    => _isPaused;
        public float MusicVolume      => _musicVolume;
        public bool  HasActivePlaylist => _playlistTracks != null && _playlistTracks.Length > 0;

        public void SkipToNextTrack()
        {
            if (_playlistTracks == null || _playlistTracks.Length == 0) return;
            _playlistIndex = (_playlistIndex + 1) % _playlistTracks.Length;
            CrossfadeTo(_playlistTracks[_playlistIndex]);
        }

        public void SkipToPreviousTrack()
        {
            if (_playlistTracks == null || _playlistTracks.Length == 0) return;
            _playlistIndex = (_playlistIndex - 1 + _playlistTracks.Length) % _playlistTracks.Length;
            CrossfadeTo(_playlistTracks[_playlistIndex]);
        }

        public bool IsMusicPlaying => _activeMusicSource != null && _activeMusicSource.isPlaying;
        public AudioClip CurrentMusicClip => _activeMusicSource != null ? _activeMusicSource.clip : null;
        public string CurrentTrackTitle => _currentTrackTitle;

        public string CurrentTrackId            => _currentTrackId ?? string.Empty;
        public float  CurrentTrackBpm           => _currentTrack != null ? _currentTrack.bpm : 0f;
        public int    CurrentTrackBeatsPerBar   => _currentTrack != null ? Mathf.Max(1, _currentTrack.beatsPerBar) : 4;
        public float  CurrentTrackBeatOffsetSec => _currentTrack != null ? _currentTrack.firstBeatOffsetSec : 0f;
        public float[] CurrentTrackBeatTimes    => _currentTrack != null ? _currentTrack.beatTimes : null;
        public string CurrentTrackKey           => _currentTrack != null ? (_currentTrack.key ?? string.Empty) : string.Empty;
        public float  CurrentMusicTime          => _activeMusicSource != null ? _activeMusicSource.time : 0f;

        public void SeekMusic(float seconds)
        {
            if (_activeMusicSource == null || _activeMusicSource.clip == null) return;
            float len = _activeMusicSource.clip.length;
            if (len <= 0f) return;
            // AudioSource.time must be strictly less than clip.length, otherwise it throws.
            float t = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, len - 0.05f));
            _activeMusicSource.time = t;
        }

        public bool GetMusicSpectrumData(float[] buffer, int channel = 0, FFTWindow window = FFTWindow.BlackmanHarris)
        {
            if (buffer == null || buffer.Length == 0) return false;
            if (_activeMusicSource == null || !_activeMusicSource.isPlaying) return false;
            _activeMusicSource.GetSpectrumData(buffer, Mathf.Max(0, channel), window);
            return true;
        }

        public bool GetMusicOutputData(float[] buffer, int channel = 0)
        {
            if (buffer == null || buffer.Length == 0) return false;
            if (_activeMusicSource == null || !_activeMusicSource.isPlaying) return false;
            _activeMusicSource.GetOutputData(buffer, Mathf.Max(0, channel));
            return true;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // IAudioService â€” SFX
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            var src = _sfxPool[_sfxIndex];
            src.volume = _sfxVolume * volumeScale;
            src.PlayOneShot(clip);
            _sfxIndex = (_sfxIndex + 1) % _sfxPool.Count;

            TryAutoDuck(null); // duck check deferred to ID-based calls
        }

        public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, position, _sfxVolume * volumeScale);
        }

        public void PlaySfxById(string sfxId, float volumeScale = 1f)
        {
            if (catalog == null || string.IsNullOrEmpty(sfxId)) return;
            var entry = catalog.GetSfx(sfxId);
            if (entry == null || entry.clip == null)
            {
                if (_warnedMissingSfxIds.Add(sfxId))
                    Debug.LogWarning($"[AudioManager] SFX not found in catalog: '{sfxId}' — assign an AudioClip to this ID in the AudioCatalog asset. (This warning fires once per ID.)");
                return;
            }

            float groupVol = GetGroupVolume(entry.group);
            PlaySFX(entry.clip, volumeScale * (groupVol / Mathf.Max(_sfxVolume, 0.001f)));
            TryAutoDuck(sfxId);
        }

        public void PlaySfxRandom(string[] sfxIds, float volumeScale = 1f)
        {
            if (sfxIds == null || sfxIds.Length == 0) return;
            PlaySfxById(sfxIds[Random.Range(0, sfxIds.Length)], volumeScale);
        }

        public void SetSFXVolume(float vol)
        {
            _sfxVolume = Mathf.Clamp01(vol);
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // IAudioService â€” Ambient
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void SetAmbientVolume(float vol)
        {
            _ambientVolume = Mathf.Clamp01(vol);
        }

        public void EnableAmbient(string[] choiceIds, float minInterval, float maxInterval)
        {
            DisableAmbient();
            if (choiceIds == null || choiceIds.Length == 0) return;
            _ambientChoices = choiceIds;
            _ambientCoroutine = StartCoroutine(AmbientLoop(minInterval, maxInterval));
        }

        public void DisableAmbient()
        {
            if (_ambientCoroutine != null)
            {
                StopCoroutine(_ambientCoroutine);
                _ambientCoroutine = null;
            }
            _ambientChoices = null;
        }

        private IEnumerator AmbientLoop(float minInterval, float maxInterval)
        {
            while (true)
            {
                float wait = Random.Range(minInterval, maxInterval);
                yield return new WaitForSeconds(wait);

                if (_ambientChoices != null && _ambientChoices.Length > 0 && catalog != null)
                {
                    string id = _ambientChoices[Random.Range(0, _ambientChoices.Length)];
                    var clip = catalog.GetSfxClip(id);
                    if (clip != null)
                    {
                        var src = _sfxPool[_sfxIndex];
                        src.volume = _ambientVolume;
                        src.PlayOneShot(clip);
                        _sfxIndex = (_sfxIndex + 1) % _sfxPool.Count;
                    }
                }
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // IAudioService â€” Playlist
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        public void StartPlaylist(AudioClip[] tracks, float intervalSec = 120f, bool shuffle = true)
        {
            StopPlaylistInternal();
            if (tracks == null || tracks.Length == 0) return;

            _playlistTracks  = tracks;
            _playlistShuffle = shuffle;
            _playlistIndex   = 0;

            if (shuffle) ShufflePlaylist();

            _playlistCoroutine = StartCoroutine(PlaylistLoop(intervalSec));
        }

        public void StopPlaylist()
        {
            StopPlaylistInternal();
        }

        private void StopPlaylistInternal()
        {
            if (_playlistCoroutine != null)
            {
                StopCoroutine(_playlistCoroutine);
                _playlistCoroutine = null;
            }
        }

        private IEnumerator PlaylistLoop(float intervalSec)
        {
            // Play first track immediately
            CrossfadeTo(_playlistTracks[_playlistIndex]);

            while (true)
            {
                yield return new WaitForSeconds(intervalSec);

                _playlistIndex = (_playlistIndex + 1) % _playlistTracks.Length;
                if (_playlistIndex == 0 && _playlistShuffle)
                    ShufflePlaylist();

                CrossfadeTo(_playlistTracks[_playlistIndex]);
            }
        }

        private void ShufflePlaylist()
        {
            for (int i = _playlistTracks.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_playlistTracks[i], _playlistTracks[j]) = (_playlistTracks[j], _playlistTracks[i]);
            }
        }

    }
}
