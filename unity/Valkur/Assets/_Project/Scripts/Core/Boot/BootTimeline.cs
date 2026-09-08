using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// The boot's own instrument: what ran, in what order, how long each step took,
    /// and what threw.
    ///
    /// It exists for the reason every other subsystem here has a probe (<c>faces</c>,
    /// <c>journal</c>, <c>spawners</c>, <c>ai</c>, <c>market</c>): "which part of the
    /// arranque costs?" was not answerable without hand-instrumenting, so every
    /// optimisation of it was a guess. It is also what makes the bar honest —
    /// <see cref="LoadWeights"/> feeds the PREVIOUS boot's measured milliseconds back
    /// in as this boot's weights, so the bar is calibrated by the machine it is
    /// running on and a newly added stage self-corrects on the second launch instead
    /// of waiting for somebody to update a constant.
    ///
    /// Weights are per-machine and live in <c>PlayerPrefs</c>, which is exactly the
    /// right storage: they are a measurement of THIS computer, they are worthless on
    /// another one, and losing them costs one uncalibrated boot.
    /// </summary>
    public static class BootTimeline
    {
        private const string PrefsKey = "valkur.boot.weights.v1";

        /// <summary>Below this the label is noise in the report rather than a cost.</summary>
        private const float ReportFloorMs = 0.5f;

        public struct StageTiming
        {
            public string Label;
            public float Milliseconds;
            public bool Failed;

            /// <summary>Rendered frames this step spanned. A synchronous step is 0.</summary>
            public int Frames;

            /// <summary>A pass inside a step, not a step. Never persisted as a weight
            /// (weights are keyed by step label) and indented in the report.</summary>
            public bool IsSubStage;
        }

        private static readonly List<StageTiming> _timings = new List<StageTiming>();
        private static readonly List<string> _failures = new List<string>();
        private static readonly Dictionary<string, float> _measured = new Dictionary<string, float>();

        private static BootProgress _progress = new BootProgress();
        private static Stopwatch _stepWatch = new Stopwatch();
        private static Stopwatch _runWatch = new Stopwatch();
        private static string _currentLabel = string.Empty;
        private static int _stepCount;
        private static bool _ranThisSession;
        private static int _runStartFrame;
        private static int _stepStartFrame;
        private static int _runFrames;
        private static Stopwatch _subWatch = new Stopwatch();
        private static string _subLabel = string.Empty;
        private static float _stepStartFraction;
        private static float _stepShare;
        private static int _stepSubStages;
        private static int _subReported;

        public static float Fraction => _progress.Fraction;
        public static int StepCount => _stepCount;
        public static bool HasRun => _ranThisSession;
        public static float TotalMilliseconds => (float)_runWatch.Elapsed.TotalMilliseconds;
        public static IReadOnlyList<StageTiming> Timings => _timings;
        public static IReadOnlyList<string> Failures => _failures;
        public static bool AnyFailed => _failures.Count > 0;

        // ── Run lifecycle ────────────────────────────────────────────────────

        /// <summary>
        /// Queue the whole sequence. The total is the SUM OF WEIGHTS of the steps
        /// actually queued — including the ones a build gated out — so it can never
        /// disagree with what runs.
        /// </summary>
        public static void BeginRun(IReadOnlyList<BootStep> steps)
        {
            _timings.Clear();
            _failures.Clear();
            _progress = new BootProgress();
            _stepWatch = new Stopwatch();
            _runWatch = new Stopwatch();
            _currentLabel = string.Empty;
            _stepCount = steps != null ? steps.Count : 0;
            _ranThisSession = true;
            _stepStartFraction = 0f;
            _stepShare = 0f;
            _stepSubStages = 0;
            _subReported = 0;

            LoadWeights();

            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                    _progress.Add(ResolveWeight(steps[i]));
            }

            _runStartFrame = Time.frameCount;
            _runFrames = 0;
            _runWatch.Start();
        }

        /// <summary>
        /// The weight this step should carry: its measured cost from the last boot
        /// when we have one, its declared estimate otherwise. A stage measured at
        /// under a millisecond still gets a floor, or a hundred free steps would
        /// collectively weigh nothing and the bar would jump.
        /// </summary>
        private static float ResolveWeight(BootStep step)
        {
            if (step == null) return 1f;
            if (step.IsReported && _measured.TryGetValue(step.Label, out float ms))
                return Mathf.Max(ms, 1f);
            return step.Weight;
        }

        public static void BeginStep(BootStep step)
        {
            _currentLabel = step != null ? step.Label : string.Empty;
            _stepStartFraction = _progress.Fraction;
            _stepShare = 0f;
            _stepSubStages = 0;
            _subReported = 0;
            if (step != null && _progress.TotalWeight > 0f)
            {
                _stepShare = ResolveWeight(step) / _progress.TotalWeight;
                _stepSubStages = step.SubStages;
            }
            _stepStartFrame = Time.frameCount;
            _stepWatch.Reset();
            _stepWatch.Start();
        }

        /// <summary>
        /// Fraction to show for the next sub-stage of the running step. A coroutine
        /// that narrates itself — the world load, the player spawn, the building
        /// load — walks its own share of the bar instead of leaving it frozen while
        /// its label changes four times. Clamped to the step's share, so an extra
        /// sub-stage nobody declared cannot borrow progress from the step after it.
        /// </summary>
        public static float NextSubStageFraction() => NextSubStageFraction(null);

        /// <summary>
        /// Overload that also CLOSES the previous sub-stage's timing and opens one for
        /// <paramref name="label"/>.
        ///
        /// This is what turns "which step costs" into "which part of it". The first
        /// live run answered the first question immediately — 60.1 % of an 11.23 s boot
        /// was one step, "Levantando los edificios" — and then could say nothing more,
        /// because that step is three passes (parse, instantiate 301 objects, wire a
        /// BoxCollider2D per painted cell) behind one stopwatch. Timing the sub-stages
        /// costs one string compare per stage and is the difference between a number
        /// and a lead.
        /// </summary>
        public static float NextSubStageFraction(string label)
        {
            CloseSubStage();
            if (!string.IsNullOrEmpty(label))
            {
                _subLabel = label;
                _subWatch.Reset();
                _subWatch.Start();
            }

            if (_stepSubStages <= 0 || _stepShare <= 0f) return _progress.Fraction;
            _subReported++;
            float t = (float)_subReported / _stepSubStages;
            if (t > 1f) t = 1f;
            float f = _stepStartFraction + t * _stepShare;
            return f > BootProgress.MaxBeforeComplete ? BootProgress.MaxBeforeComplete : f;
        }

        /// <summary>
        /// Bank whatever sub-stage was running. Called when the next one starts and
        /// again when the step ends, so the last sub-stage of a step is never lost.
        /// </summary>
        private static void CloseSubStage()
        {
            if (string.IsNullOrEmpty(_subLabel)) return;
            _subWatch.Stop();
            _timings.Add(new StageTiming
            {
                Label = "  · " + _subLabel,
                Milliseconds = (float)_subWatch.Elapsed.TotalMilliseconds,
                Failed = false,
                IsSubStage = true,
            });
            _subLabel = string.Empty;
        }

        /// <summary>Fraction to show WHILE a step runs — the work already banked.</summary>
        public static float FractionAtStepStart => _stepStartFraction;

        public static void EndStep(int index, bool failed = false)
        {
            CloseSubStage();
            _stepWatch.Stop();
            _timings.Add(new StageTiming
            {
                Label = string.IsNullOrEmpty(_currentLabel) ? "(silencioso)" : _currentLabel,
                Milliseconds = (float)_stepWatch.Elapsed.TotalMilliseconds,
                Failed = failed,
                Frames = Time.frameCount - _stepStartFrame,
            });
            _progress.CompleteStep(index);
        }

        public static void RecordFailure(string label, Exception ex)
        {
            string where = string.IsNullOrEmpty(label) ? "(paso silencioso)" : label;
            string what = ex != null ? ex.Message : "error desconocido";
            _failures.Add($"{where} — {what}");
        }

        /// <summary>
        /// Everything is ready: the bar may finally read 100 %, and this boot's
        /// measurements become the next boot's weights.
        /// </summary>
        public static void CompleteRun()
        {
            _runWatch.Stop();
            _runFrames = Time.frameCount - _runStartFrame;
            _progress.Complete();
            SaveWeights();
        }

        // ── Weight persistence ───────────────────────────────────────────────

        [Serializable]
        private class WeightEntry
        {
            public string label;
            public float ms;
        }

        [Serializable]
        private class WeightDoc
        {
            public List<WeightEntry> entries = new List<WeightEntry>();
        }

        private static void LoadWeights()
        {
            _measured.Clear();
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var doc = JsonUtility.FromJson<WeightDoc>(json);
                if (doc?.entries == null) return;
                foreach (var e in doc.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.label)) continue;
                    _measured[e.label] = e.ms;
                }
            }
            catch (Exception ex)
            {
                // A corrupted profile costs exactly one uncalibrated boot, and the
                // next CompleteRun overwrites it. Never worth failing a launch over.
                Debug.LogWarning($"[BootTimeline] Perfil de pesos ilegible, se ignora: {ex.Message}");
                _measured.Clear();
            }
        }

        private static void SaveWeights()
        {
            if (_timings.Count == 0) return;
            var doc = new WeightDoc();
            foreach (var t in _timings)
            {
                if (t.IsSubStage) continue;
                if (string.IsNullOrEmpty(t.Label) || t.Label == "(silencioso)") continue;
                doc.entries.Add(new WeightEntry { label = t.Label, ms = t.Milliseconds });
            }
            if (doc.entries.Count == 0) return;

            try
            {
                PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(doc));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BootTimeline] No se pudo guardar el perfil de pesos: {ex.Message}");
            }
        }

        /// <summary>Drops the calibration so the next boot runs on declared estimates.</summary>
        public static void ClearWeights()
        {
            _measured.Clear();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        public static bool IsCalibrated => _measured.Count > 0;

        /// <summary>Rendered frames the whole run spanned.</summary>
        public static int RunFrames => _runFrames > 0 ? _runFrames : Time.frameCount - _runStartFrame;

        /// <summary>
        /// Wall clock the run took MINUS the time inside steps.
        ///
        /// This is the number that decides where an optimisation goes next, and it only
        /// became interesting once the steps got cheap: with the building sprites fixed the
        /// steps sum to about a quarter of the boot, so three quarters of it is the runner
        /// waiting for frames plus whatever Unity does in them (deferred collider bakes,
        /// atlas paging, the newly activated scene's own Updates). Optimising a step further
        /// cannot touch it.
        /// </summary>
        public static float MillisecondsOutsideSteps
        {
            get
            {
                float inside = 0f;
                foreach (var t in _timings) if (!t.IsSubStage) inside += t.Milliseconds;
                float outside = TotalMilliseconds - inside;
                return outside > 0f ? outside : 0f;
            }
        }

        // ── The probe ────────────────────────────────────────────────────────

        /// <summary>
        /// Human-readable breakdown, newest run. <paramref name="top"/> caps the
        /// per-stage list; everything under <see cref="ReportFloorMs"/> is folded
        /// into a single "resto" line, because ninety sub-millisecond rows bury the
        /// four that matter.
        /// </summary>
        public static string Report(int top = 12)
        {
            if (!_ranThisSession)
                return "boot: esta sesion no ha arrancado la escena de juego todavia.";

            var sb = new System.Text.StringBuilder();
            sb.Append("Arranque: ").Append(_stepCount).Append(" etapas en ")
              .Append((TotalMilliseconds / 1000f).ToString("F2")).Append(" s")
              .Append(IsCalibrated ? " (barra calibrada)" : " (barra sin calibrar - primer arranque)")
              .AppendLine();

            // Steps and sub-stages are listed apart on purpose: a sub-stage's cost is
            // ALREADY inside its parent, so mixing them into one sorted list double
            // counts the total and reads as if the boot did twice the work it did.
            var steps = new List<StageTiming>();
            var subs  = new List<StageTiming>();
            foreach (var t in _timings) (t.IsSubStage ? subs : steps).Add(t);

            steps.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
            subs.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));

            float rest = 0f;
            int lines = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                var t = steps[i];
                if (lines >= top || t.Milliseconds < ReportFloorMs) { rest += t.Milliseconds; continue; }
                sb.Append(FormatRow(t)).AppendLine();
                lines++;
            }
            if (rest > 0f)
                sb.Append("  ").Append(rest.ToString("F1").PadLeft(8)).Append(" ms         ")
                  .Append("resto (").Append(steps.Count - lines).Append(" etapas)").AppendLine();

            if (subs.Count > 0)
            {
                sb.Append("Desglose de las etapas progresivas:").AppendLine();
                foreach (var t in subs)
                {
                    if (t.Milliseconds < ReportFloorMs) continue;
                    sb.Append(FormatRow(t)).AppendLine();
                }
            }

            float outside = MillisecondsOutsideSteps;
            int frames = RunFrames;
            sb.Append("Fuera de las etapas: ").Append(outside.ToString("F1")).Append(" ms en ")
              .Append(frames).Append(" fotogramas");
            if (frames > 0)
                sb.Append(" (").Append((outside / frames).ToString("F1")).Append(" ms/fotograma)");
            sb.AppendLine();

            if (_failures.Count > 0)
            {
                sb.Append("Fallos (").Append(_failures.Count).Append("):").AppendLine();
                foreach (var f in _failures) sb.Append("  ! ").Append(f).AppendLine();
            }
            else
            {
                sb.Append("Sin fallos.");
            }
            return sb.ToString();
        }

        private static string FormatRow(StageTiming t)
        {
            float pct = TotalMilliseconds > 0f ? t.Milliseconds / TotalMilliseconds * 100f : 0f;
            string frames = t.Frames > 0 ? (t.Frames + "f").PadLeft(5) : "     ";
            return "  " + t.Milliseconds.ToString("F1").PadLeft(8) + " ms  " +
                   pct.ToString("F1").PadLeft(5) + "%  " + frames + "  " +
                   (t.Failed ? "[FALLO] " : string.Empty) + t.Label;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _timings.Clear();
            _failures.Clear();
            _measured.Clear();
            _progress = new BootProgress();
            _stepWatch = new Stopwatch();
            _runWatch = new Stopwatch();
            _currentLabel = string.Empty;
            _stepCount = 0;
            _ranThisSession = false;
            _stepStartFraction = 0f;
            _stepShare = 0f;
            _stepSubStages = 0;
            _subReported = 0;
            _subLabel = string.Empty;
            _subWatch = new Stopwatch();
            _runStartFrame = 0;
            _stepStartFrame = 0;
            _runFrames = 0;
        }
    }
}
