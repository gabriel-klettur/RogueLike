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

        // ── Precise mode (beat-map driven) ──────────────────────────────────
        // When the active track was analysed by analyze_music.py we get the
        // exact onset of every beat in seconds. Driving OnBeat from that array
        // (instead of a constant-BPM model) keeps the clock locked to the song
        // even when its tempo drifts. Falls back to BPM mode when null/empty
        // or after a tap-tempo OverrideTempo() call.
        private float[] _beatTimes;       // copy of MusicTrackEntry.beatTimes
        private bool    _preciseMode;     // true when _beatTimes is usable
        private int     _searchHint;      // last index searched (forward-scan optimisation)

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
                if (_preciseMode && _audio != null)
                {
                    // Phase = position within the [prev, next] beat window.
                    int k = Mathf.Max(0, _lastBeatIndex);
                    if (k + 1 >= _beatTimes.Length) return 0f;
                    float t  = _audio.CurrentMusicTime;
                    float t0 = _beatTimes[k];
                    float t1 = _beatTimes[k + 1];
                    if (t1 <= t0) return 0f;
                    return Mathf.Clamp01((t - t0) / (t1 - t0));
                }
                if (_bpm <= 0f) return 0f;
                float frac = _beatTime - Mathf.Floor(_beatTime);
                return Mathf.Clamp01(frac);
            }
        }

        /// <summary>Total seconds per beat (60 / bpm). 0 if no BPM.</summary>
        public float SecondsPerBeat
        {
            get
            {
                if (_preciseMode)
                {
                    int k = Mathf.Max(0, _lastBeatIndex);
                    if (k + 1 < _beatTimes.Length) return Mathf.Max(0.001f, _beatTimes[k + 1] - _beatTimes[k]);
                }
                return _bpm > 0f ? 60f / _bpm : 0f;
            }
        }

        /// <summary>True if the active track has a per-beat onset map (sample-accurate sync).</summary>
        public bool HasBeatMap => _preciseMode;

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
            _searchHint    = 0;

            // Adopt the beat-map if the catalog provides one (precise mode).
            float[] beats = _audio != null ? _audio.CurrentTrackBeatTimes : null;
            _beatTimes  = beats;
            _preciseMode = beats != null && beats.Length >= 2;
        }

        private void Update()
        {
            if (!_subscribed) TrySubscribe();
            if (_audio == null) return;
            if (!_audio.IsMusicPlaying) return;

            float musicTime = _audio.CurrentMusicTime;

            if (_preciseMode)
            {
                // Forward-scan from the last index; if we somehow seek backwards,
                // re-anchor by binary search.
                if (_lastBeatIndex >= 0 && _lastBeatIndex < _beatTimes.Length &&
                    musicTime + 0.0005f < _beatTimes[_lastBeatIndex])
                {
                    _lastBeatIndex = -1;
                    _searchHint    = 0;
                }
                int target = _lastBeatIndex;
                int i = Mathf.Max(_searchHint, _lastBeatIndex + 1);
                while (i < _beatTimes.Length && _beatTimes[i] <= musicTime)
                {
                    target = i;
                    i++;
                }
                _searchHint = i;
                if (target > _lastBeatIndex)
                {
                    for (int b = _lastBeatIndex + 1; b <= target; b++)
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
                    _lastBeatIndex = target;
                }
                return;
            }

            // ── Constant-BPM fallback ───────────────────────────────────────
            if (_bpm <= 0f) return;
            float t = musicTime - _offsetSec;
            if (t < 0f)
            {
                _beatTime = 0f;
                return;
            }
            _beatTime = t * (_bpm / 60f);

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

        /// <summary>
        /// Live tempo override: lets the HUD (or a tap-tempo tool) re-tune the
        /// running clock without going through <see cref="IAudioService"/>.
        /// Pass a <paramref name="bpm"/> &lt;= 0 to keep the current BPM.
        /// Pass a negative <paramref name="offsetSec"/> to keep the current offset.
        /// Resets the beat counter so the next downbeat fires cleanly.
        /// </summary>
        public void OverrideTempo(float bpm, float offsetSec)
        {
            // Manual tap-tempo override always drops out of precise mode — the user
            // is intentionally retuning, so the imported beat-map no longer applies.
            _preciseMode = false;
            _beatTimes   = null;
            if (bpm > 0f) _bpm = bpm;
            if (offsetSec >= 0f) _offsetSec = offsetSec;
            // Re-anchor: don't replay missed beats from time 0 after a live retune.
            if (_audio != null && _audio.IsMusicPlaying && _bpm > 0f)
            {
                float musicTime = Mathf.Max(0f, _audio.CurrentMusicTime - _offsetSec);
                _beatTime = musicTime * (_bpm / 60f);
                _lastBeatIndex = Mathf.FloorToInt(_beatTime);
            }
            else
            {
                _beatTime = 0f;
                _lastBeatIndex = -1;
            }
        }

        /// <summary>Current first-beat offset in seconds (downbeat 0 lives here).</summary>
        public float FirstBeatOffsetSec => _offsetSec;

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
