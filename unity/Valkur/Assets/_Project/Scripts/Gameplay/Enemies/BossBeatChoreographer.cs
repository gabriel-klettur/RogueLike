using System;
using UnityEngine;
using UnityEngine.Events;
using Valkur.Data;
using Valkur.Infrastructure;

namespace Valkur.Gameplay.Enemies
{
    /// <summary>
    /// Drives a boss's actions in lock-step with the music's beat clock.
    /// Subscribes to <see cref="MusicBeatClock.OnBeat"/> and emits matching
    /// cues from a <see cref="BossBeatPattern"/> through <see cref="OnCue"/>.
    ///
    /// Designers can wire <see cref="OnCue"/> to any FSM/spell/dash method on
    /// the boss in the inspector — no code changes needed per boss.
    ///
    /// Cue (bar, beat) is matched against the loop window:
    ///   barInLoop  = bar % pattern.barsPerLoop
    ///   beatInBar  = clock-provided beatInBar
    /// </summary>
    public sealed class BossBeatChoreographer : MonoBehaviour
    {
        [Tooltip("Beat pattern to play. Can be swapped at runtime per boss phase.")]
        [SerializeField] private BossBeatPattern pattern;

        [Tooltip("If false, the choreographer does nothing (use to gate by HP%, distance, etc.).")]
        [SerializeField] private bool active = true;

        [Tooltip("Delay (seconds) after a beat fires before OnCue is invoked. " +
                 "Useful for pre-telegraph timing, e.g. 0.1s wind-up.")]
        [Min(0f)] [SerializeField] private float cueDelay = 0f;

        /// <summary>Raised each time a cue fires. Args: action, payload, beatInBar, bar.</summary>
        [Serializable] public class CueEvent : UnityEvent<string, float, int, int> { }

        [SerializeField] private CueEvent onCue = new CueEvent();
        public CueEvent OnCue => onCue;

        public BossBeatPattern Pattern
        {
            get => pattern;
            set => pattern = value;
        }
        public bool Active { get => active; set => active = value; }

        private MusicBeatClock _clock;
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
            if (!active || pattern == null || pattern.cues == null) return;
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
                    StartCoroutine(InvokeDelayed(cue, beatInBar, bar, cueDelay));
                else
                    InvokeCue(cue, beatInBar, bar);
            }
        }

        private System.Collections.IEnumerator InvokeDelayed(BossBeatPattern.Cue cue, int beatInBar, int bar, float delay)
        {
            yield return new WaitForSeconds(delay);
            InvokeCue(cue, beatInBar, bar);
        }

        private void InvokeCue(BossBeatPattern.Cue cue, int beatInBar, int bar)
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
    }
}
