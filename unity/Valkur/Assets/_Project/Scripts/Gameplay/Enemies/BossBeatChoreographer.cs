using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Valkur.Core;
using Valkur.Data;
using Valkur.Infrastructure;

namespace Valkur.Gameplay.Enemies
{
    /// <summary>
    /// Drives a boss's actions in lock-step with the music's beat clock.
    /// Subscribes to <see cref="MusicBeatClock.OnBeat"/> and emits matching
    /// cues from the active source (a <see cref="BossChart"/>, or — for
    /// legacy assets — a <see cref="BossBeatPattern"/>) through
    /// <see cref="OnTypedCue"/> (preferred) or the legacy <see cref="OnCue"/>
    /// string-based UnityEvent.
    ///
    /// Cue (bar, beat) is matched against the loop window:
    ///   barInLoop  = bar % chart.barsPerLoop
    ///   beatInBar  = clock-provided beatInBar
    ///
    /// Charts also filter on the active <c>MusicTrackEntry.id</c> — a chart
    /// only fires when its target song is playing. Legacy patterns (no track
    /// id) fire regardless of song.
    /// </summary>
    public sealed class BossBeatChoreographer : MonoBehaviour
    {
        [Header("Source (chart preferred; pattern is legacy)")]
        [Tooltip("Preferred source. Beat-anchored cues with typed actions " +
                 "(spell / sfx / phase / spawn / anim). When non-null, takes " +
                 "precedence over the legacy pattern below.")]
        [SerializeField] private BossChart chart;

        [Tooltip("Legacy free-form pattern. Kept so existing assets keep " +
                 "working — new content should use BossChart instead.")]
        [SerializeField] private BossBeatPattern pattern;

        [Tooltip("If false, the choreographer does nothing (gate by HP%, distance, …).")]
        [SerializeField] private bool active = true;

        [Tooltip("Delay (seconds) after a beat fires before the cue is invoked. " +
                 "For a chart this is layered ON TOP of the per-spell prepareDuration " +
                 "auto-offset that the dispatcher applies.")]
        [Min(0f)] [SerializeField] private float cueDelay = 0f;

        // ── Legacy event surface (kept for already-wired bosses) ────────────
        [Serializable] public class CueEvent : UnityEvent<string, float, int, int> { }
        [SerializeField] private CueEvent onCue = new CueEvent();
        public CueEvent OnCue => onCue;

        // ── Typed event surface (preferred — subscribed by BossCueDispatcher) ───
        /// <summary>(cue, beatInBar, bar). Fires once per matching cue.</summary>
        public event Action<BossCue, int, int> OnTypedCue;

        public BossChart Chart
        {
            get => chart;
            set => chart = value;
        }
        public BossBeatPattern Pattern
        {
            get => pattern;
            set => pattern = value;
        }
        public bool Active { get => active; set => active = value; }

        private MusicBeatClock _clock;
        private IAudioService  _audio;
        private bool _subscribed;

        private void OnEnable()  => TrySubscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Update()
        {
            if (!_subscribed) TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            _clock = MusicBeatClock.Instance;
            if (_clock == null) return;
            _clock.OnBeat += HandleBeat;
            _audio = ServiceLocator.Get<IAudioService>();
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_subscribed && _clock != null)
                _clock.OnBeat -= HandleBeat;
            _subscribed = false;
        }

        private void HandleBeat(int beatIndex, int beatInBar, int bar)
        {
            if (!active) return;

            // Chart path (preferred). Only fires for the matching song.
            if (chart != null && IsChartTrackActive(chart))
            {
                FireChartCues(beatInBar, bar);
            }

            // Legacy pattern path — independent of song id.
            if (pattern != null && pattern.cues != null)
            {
                FirePatternCues(beatInBar, bar);
            }
        }

        private bool IsChartTrackActive(BossChart c)
        {
            if (string.IsNullOrEmpty(c.musicTrackId)) return true; // unbound chart fires anywhere
            string current = _audio != null ? _audio.CurrentTrackId : string.Empty;
            return string.Equals(current, c.musicTrackId, StringComparison.OrdinalIgnoreCase);
        }

        private void FireChartCues(int beatInBar, int bar)
        {
            int loop = Mathf.Max(1, chart.barsPerLoop);
            int barInLoop = bar % loop;
            List<BossCue> cues = chart.cues;
            if (cues == null) return;

            for (int i = 0; i < cues.Count; i++)
            {
                BossCue cue = cues[i];
                if (cue.bar < 0 || cue.bar >= loop) continue;
                if (cue.bar != barInLoop) continue;
                if (cue.beat != beatInBar) continue;

                // Sub-beat fraction is honoured by adding a coroutine delay
                // proportional to SecondsPerBeat. fraction == 0 → fires now.
                float frac = Mathf.Clamp01(cue.beatFraction);
                float delay = cueDelay + frac * Mathf.Max(0f, _clock != null ? _clock.SecondsPerBeat : 0f);
                if (delay > 0f)
                    StartCoroutine(InvokeChartCueDelayed(cue, beatInBar, bar, delay));
                else
                    InvokeChartCue(cue, beatInBar, bar);
            }
        }

        private void FirePatternCues(int beatInBar, int bar)
        {
            int loop = Mathf.Max(1, pattern.barsPerLoop);
            int barInLoop = bar % loop;

            for (int i = 0; i < pattern.cues.Count; i++)
            {
                var cue = pattern.cues[i];
                if (cue == null) continue;
                if (cue.bar >= loop) continue;
                if (cue.bar != barInLoop) continue;
                if (cue.beat != beatInBar) continue;

                if (cueDelay > 0f)
                    StartCoroutine(InvokeLegacyDelayed(cue, beatInBar, bar, cueDelay));
                else
                    InvokeLegacyCue(cue, beatInBar, bar);
            }
        }

        private System.Collections.IEnumerator InvokeChartCueDelayed(BossCue cue, int beatInBar, int bar, float delay)
        {
            yield return new WaitForSeconds(delay);
            InvokeChartCue(cue, beatInBar, bar);
        }

        private System.Collections.IEnumerator InvokeLegacyDelayed(BossBeatPattern.Cue cue, int beatInBar, int bar, float delay)
        {
            yield return new WaitForSeconds(delay);
            InvokeLegacyCue(cue, beatInBar, bar);
        }

        private void InvokeChartCue(BossCue cue, int beatInBar, int bar)
        {
            try { OnTypedCue?.Invoke(cue, beatInBar, bar); }
            catch (Exception ex) { Debug.LogWarning($"[BossBeatChoreographer] OnTypedCue handler error: {ex.Message}"); }
        }

        private void InvokeLegacyCue(BossBeatPattern.Cue cue, int beatInBar, int bar)
        {
            try { onCue?.Invoke(cue.action ?? string.Empty, cue.payload, beatInBar, bar); }
            catch (Exception ex) { Debug.LogWarning($"[BossBeatChoreographer] OnCue handler error: {ex.Message}"); }
        }

        // ── Test helpers ────────────────────────────────────────────────────
        /// <summary>Editor / unit-test only: fire as if the clock emitted this beat.</summary>
        internal void DebugForceBeat(int beatIndex, int beatInBar, int bar)
        {
            HandleBeat(beatIndex, beatInBar, bar);
        }

        /// <summary>Editor / unit-test only: inject the clock + audio service references.</summary>
        internal void DebugInitForTest(MusicBeatClock clock, IAudioService audio)
        {
            _clock = clock;
            _audio = audio;
            _subscribed = true;
        }
    }
}
