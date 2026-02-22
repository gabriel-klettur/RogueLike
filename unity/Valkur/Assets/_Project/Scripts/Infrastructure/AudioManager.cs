using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Infrastructure
{
    /// <summary>
    /// Centralized audio manager for music and SFX.
    /// Maps to Python's audio system with zone-based music and pooled SFX sources.
    /// Singleton pattern matching Python's global audio manager.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Music")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private float musicFadeDuration = 1f;
        [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.5f;

        [Header("SFX")]
        [SerializeField] private int sfxPoolSize = 8;
        [SerializeField] [Range(0f, 1f)] private float sfxVolume = 0.7f;

        private readonly List<AudioSource> _sfxPool = new List<AudioSource>();
        private int _sfxIndex;
        private AudioClip _pendingMusic;
        private float _fadeTimer;
        private bool _fadingOut;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
            }

            for (int i = 0; i < sfxPoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                _sfxPool.Add(src);
            }
        }

        private void Update()
        {
            if (_fadingOut)
            {
                _fadeTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_fadeTimer / musicFadeDuration);
                musicSource.volume = t * musicVolume;

                if (_fadeTimer <= 0f)
                {
                    _fadingOut = false;
                    musicSource.Stop();
                    if (_pendingMusic != null)
                    {
                        musicSource.clip = _pendingMusic;
                        musicSource.volume = musicVolume;
                        musicSource.Play();
                        _pendingMusic = null;
                    }
                }
            }
        }

        /// <summary>
        /// Play background music with crossfade.
        /// Maps to Python's zone-based music switching.
        /// </summary>
        public void PlayMusic(AudioClip clip)
        {
            if (clip == null) return;
            if (musicSource.clip == clip && musicSource.isPlaying) return;

            if (musicSource.isPlaying)
            {
                _pendingMusic = clip;
                _fadingOut = true;
                _fadeTimer = musicFadeDuration;
            }
            else
            {
                musicSource.clip = clip;
                musicSource.volume = musicVolume;
                musicSource.Play();
            }
        }

        public void StopMusic()
        {
            _fadingOut = true;
            _fadeTimer = musicFadeDuration;
            _pendingMusic = null;
        }

        /// <summary>
        /// Play a one-shot SFX from the pool.
        /// </summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;

            var src = _sfxPool[_sfxIndex];
            src.volume = sfxVolume * volumeScale;
            src.PlayOneShot(clip);
            _sfxIndex = (_sfxIndex + 1) % _sfxPool.Count;
        }

        /// <summary>
        /// Play SFX at a world position (3D spatialized).
        /// </summary>
        public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, position, sfxVolume * volumeScale);
        }

        public void SetMusicVolume(float vol)
        {
            musicVolume = Mathf.Clamp01(vol);
            if (!_fadingOut)
                musicSource.volume = musicVolume;
        }

        public void SetSFXVolume(float vol)
        {
            sfxVolume = Mathf.Clamp01(vol);
        }
    }
}
