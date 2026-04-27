using System;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Infrastructure
{
    /// <summary>
    /// Drives a real-time beat/bar clock from the active <see cref="IAudioService"/>.
    /// Subscribes to <see cref="IAudioService.OnTrackChanged"/> and reads
    /// <see cref="IAudioService.CurrentMusicTime"/> every frame.
    ///
    /// Fires <see cref="OnBeat"/>(beatIndex,beatInBar,bar) every musical beat and
    /// <see cref="OnBar"/>(barIndex) every downbeat. <see cref="BeatPhase01"/> is the
    /// continuous 0–1 phase inside the current beat (useful for HUD pulse animation).
    ///
    /// Inactive when no track is playing or BPM &lt;= 0.
    /// Lives in Infrastructure so both UI (HUD) and Gameplay (boss choreography)
    /// can reference it without crossing assembly boundaries.
    /// </summary>
    public sealed class MusicBeatClock : MonoBehaviour
    {
        public static MusicBeatClock Instance { get; private set; }

        // ── Track state ─────────────────────────────────────────────────────
        private IAudioService _audio;
        private string _trackId;
        private string _trackTitle;
        private float  _bpm;
        private int    _beatsPerBar = 4;
        private float  _offsetSec;
        private bool   _subscribed;

        // ── Beat state ──────────────────────────────────────────────────────
        private int   _lastBeatIndex = -1;
        private float _beatTime;          // continuous beats since downbeat 0

        // ── Public API ──────────────────────────────────────────────────────
        public string TrackId       => _trackId ?? string.Empty;
        public string TrackTitle    => _trackTitle ?? string.Empty;
        public float  Bpm           => _bpm;
        public int    BeatsPerBar   => _beatsPerBar;
        public bool   IsActive      => _bpm > 0f && _audio != null && _audio.IsMusicPlaying;
        public int    CurrentBeat   => Mathf.Max(0, _lastBeatIndex);
        public int    CurrentBar    => _beatsPerBar > 0 ? CurrentBeat / _beatsPerBar : 0;
        public int    CurrentBeatInBar => _beatsPerBar > 0 ? CurrentBeat % _beatsPerBar : 0;

        /// <summary>0–1 phase within the current beat (0 = just hit, ~1 = next beat imminent).</summary>
        public float BeatPhase01
        {
            get
            {
                if (_bpm <= 0f) return 0f;
                float frac = _beatTime - Mathf.Floor(_beatTime);
                return Mathf.Clamp01(frac);
            }
        }

        /// <summary>Total seconds per beat (60 / bpm). 0 if no BPM.</summary>
        public float SecondsPerBeat => _bpm > 0f ? 60f / _bpm : 0f;

        /// <summary>(beatIndex, beatInBar, bar). Fired exactly once per beat crossing.</summary>
        public event Action<int, int, int> OnBeat;
        /// <summary>(barIndex). Fired on every downbeat (beatInBar == 0).</summary>
        public event Action<int> OnBar;

        // ── Lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            TrySubscribe();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDestroy()
        {
            UnsubscribeAudio();
            if (Instance == this) Instance = null;
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            _audio = ServiceLocator.Get<IAudioService>();
            if (_audio == null) return;
            _audio.OnTrackChanged += HandleTrackChanged;
            _subscribed = true;

            // Initialize from whatever is already playing
            if (!string.IsNullOrEmpty(_audio.CurrentTrackId) || _audio.IsMusicPlaying)
            {
                HandleTrackChanged(_audio.CurrentTrackId, _audio.CurrentTrackTitle,
                                   _audio.CurrentTrackBpm, _audio.CurrentTrackBeatsPerBar);
            }
        }

        private void UnsubscribeAudio()
        {
            if (_audio != null && _subscribed)
                _audio.OnTrackChanged -= HandleTrackChanged;
            _subscribed = false;
            _audio = null;
        }

        private void HandleTrackChanged(string id, string title, float bpm, int beatsPerBar)
        {
            _trackId       = id;
            _trackTitle    = title;
            _bpm           = Mathf.Max(0f, bpm);
            _beatsPerBar   = Mathf.Max(1, beatsPerBar);
            _offsetSec     = _audio != null ? _audio.CurrentTrackBeatOffsetSec : 0f;
            _lastBeatIndex = -1;
            _beatTime      = 0f;
        }

        private void Update()
        {
            if (!_subscribed) TrySubscribe();
            if (_audio == null || _bpm <= 0f) return;
            if (!_audio.IsMusicPlaying) return;

            float musicTime = _audio.CurrentMusicTime - _offsetSec;
            if (musicTime < 0f)
            {
                _beatTime = 0f;
                return;
            }
            _beatTime = musicTime * (_bpm / 60f);

            int beatIndex = Mathf.FloorToInt(_beatTime);
            if (beatIndex > _lastBeatIndex)
            {
                // It's possible to advance multiple beats in one frame (low FPS) —
                // fire each one in order so beat-synced cues never get skipped.
                for (int b = _lastBeatIndex + 1; b <= beatIndex; b++)
                {
                    int beatInBar = b % _beatsPerBar;
                    int bar       = b / _beatsPerBar;
                    try { OnBeat?.Invoke(b, beatInBar, bar); }
                    catch (Exception ex) { Debug.LogWarning($"[MusicBeatClock] OnBeat handler error: {ex.Message}"); }
                    if (beatInBar == 0)
                    {
                        try { OnBar?.Invoke(bar); }
                        catch (Exception ex) { Debug.LogWarning($"[MusicBeatClock] OnBar handler error: {ex.Message}"); }
                    }
                }
                _lastBeatIndex = beatIndex;
            }
        }

        // ── Test helpers ────────────────────────────────────────────────────
        /// <summary>Editor / unit-test only: feed a synthetic music time and emit beats.</summary>
        internal void DebugTick(float musicTime)
        {
            if (_bpm <= 0f) return;
            float t = musicTime - _offsetSec;
            if (t < 0f) { _beatTime = 0f; return; }
            _beatTime = t * (_bpm / 60f);
            int beatIndex = Mathf.FloorToInt(_beatTime);
            if (beatIndex > _lastBeatIndex)
            {
                for (int b = _lastBeatIndex + 1; b <= beatIndex; b++)
                {
                    int beatInBar = b % _beatsPerBar;
                    int bar       = b / _beatsPerBar;
                    OnBeat?.Invoke(b, beatInBar, bar);
                    if (beatInBar == 0) OnBar?.Invoke(bar);
                }
                _lastBeatIndex = beatIndex;
            }
        }

        /// <summary>Editor / unit-test only: configure track without going through IAudioService.</summary>
        internal void DebugSetTrack(string id, string title, float bpm, int beatsPerBar, float offsetSec = 0f)
        {
            _trackId = id; _trackTitle = title;
            _bpm = Mathf.Max(0f, bpm);
            _beatsPerBar = Mathf.Max(1, beatsPerBar);
            _offsetSec = offsetSec;
            _lastBeatIndex = -1;
            _beatTime = 0f;
        }
    }
}
