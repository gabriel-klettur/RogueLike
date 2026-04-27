using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Infrastructure
{
    /// <summary>
    /// Full audio manager: music crossfade, SFX pool, ambient scheduler, ducking, playlists.
    /// Mirrors Python AudioService + AudioBus + AudioSystem combined.
    /// Registers as IAudioService via ServiceLocator.
    /// </summary>
    public partial class AudioManager : SingletonMonoBehaviour<AudioManager>, IAudioService
    {
        // â”€â”€ Inspector â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [Header("Audio Catalog")]
        [Tooltip("Reference to the AudioCatalog ScriptableObject")]
        [SerializeField] private AudioCatalogSO catalog;

        [Header("Music")]
        [Tooltip("Default crossfade duration in seconds (Python: 0.6)")]
        [SerializeField] private float defaultCrossfadeSec = 0.6f;

        [Header("SFX")]
        [Tooltip("Number of pooled AudioSources for SFX (Python max channels: 32)")]
        [SerializeField] private int sfxPoolSize = 16;

        // â”€â”€ Music state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private AudioSource _musicA;
        private AudioSource _musicB;
        private AudioSource _activeMusicSource;
        private Coroutine _crossfadeCoroutine;
        private float _musicVolume;
        private string _currentTrackTitle;
        private MusicTrackEntry _currentTrack;
        private string _currentTrackId;
        private bool _isPaused;

        /// <inheritdoc cref="IAudioService.OnTrackChanged"/>
        public event System.Action<string, string, float, int> OnTrackChanged;

        // â”€â”€ SFX state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly List<AudioSource> _sfxPool = new List<AudioSource>();
        private int _sfxIndex;
        private float _sfxVolume;

        // â”€â”€ Ambient state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private float _ambientVolume;
        private Coroutine _ambientCoroutine;
        private string[] _ambientChoices;

        // â”€â”€ Playlist state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private Coroutine _playlistCoroutine;
        private AudioClip[] _playlistTracks;
        private int _playlistIndex;
        private bool _playlistShuffle;

        // â”€â”€ Ducking state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private Coroutine _duckingCoroutine;
        private float _duckTarget = 1f; // 1 = no duck

        // Deduplicate missing-SFX warnings: only log once per ID so active spells
        // (e.g. laser_beam called every frame in AdvancePhase) don't spam the console.
        private readonly HashSet<string> _warnedMissingSfxIds = new HashSet<string>();

        protected override bool Persist => true;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Lifecycle
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        protected override void OnSingletonAwake()
        {
            // Read saved settings
            var gs = GameSettings.Instance;
            _musicVolume   = gs.musicVolume;
            _sfxVolume     = gs.sfxVolume;
            _ambientVolume = gs.ambientVolume;

            // Create two music sources for crossfade
            _musicA = CreateAudioSource("MusicA", true);
            _musicB = CreateAudioSource("MusicB", true);
            _activeMusicSource = _musicA;

            // Create SFX pool
            for (int i = 0; i < sfxPoolSize; i++)
            {
                var src = CreateAudioSource($"SFX_{i}", false);
                _sfxPool.Add(src);
            }

            ServiceLocator.Register<IAudioService>(this);
            Debug.Log($"[AudioManager] Initialized. Music={_musicVolume:F2}, SFX={_sfxVolume:F2}, Ambient={_ambientVolume:F2}");
        }

        protected override void OnDestroy()
        {
            ServiceLocator.Unregister<IAudioService>();
            base.OnDestroy();
        }

        private AudioSource CreateAudioSource(string label, bool loop)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            return src;
        }

        /// <summary>
        /// Assigns the catalog at runtime (used when AudioManager is created via code
        /// rather than placed in a scene with the inspector reference pre-wired).
        /// </summary>
        public void SetCatalog(AudioCatalogSO newCatalog)
        {
            catalog = newCatalog;
        }

    }
}
