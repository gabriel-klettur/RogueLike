using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// The boot's own instrument: what ran, in what order, how long each step took,
    /// what the previous boots PREDICTED it would take, and what threw.
    ///
    /// It exists for the reason every other subsystem here has a probe (<c>faces</c>,
    /// <c>journal</c>, <c>spawners</c>, <c>ai</c>, <c>market</c>): "which part of the
    /// arranque costs?" was not answerable without hand-instrumenting, so every
    /// optimisation of it was a guess. It is also what makes the bar honest —
    /// the previous boots' measured milliseconds are this boot's weights, so the bar is
    /// calibrated by the machine it is running on and a newly added stage self-corrects
    /// on the second launch instead of waiting for somebody to update a constant.
    ///
    /// <para><b>The prediction is a moving average, not the last boot.</b> The first boot
    /// after launching the Editor pages every asset in and runs seconds slower than the one
    /// after it; weighting the bar by that single boot made the second launch's bar crawl
    /// through the world load and then leap. An exponential average with
    /// <see cref="PredictionAlpha"/> follows a real change within three or four boots and
    /// shrugs off one outlier.</para>
    ///
    /// <para><b>The weight is WALL time, from a step's start to the next step's start</b> —
    /// its body plus the frames it yielded. Weighting by body time alone described a boot in
    /// which three quarters of the clock happened between steps, so the bar filled at the
    /// speed of the CPU and waited at the speed of the frames. The body time is still
    /// recorded, and the difference between the two is the column that says whether a step
    /// is slow or is merely waiting.</para>
    ///
    /// Weights are per-machine and live in <c>PlayerPrefs</c>, which is exactly the
    /// right storage: they are a measurement of THIS computer, they are worthless on
    /// another one, and losing them costs one uncalibrated boot. The full history is
    /// written by <see cref="BootRunLog"/>.
    /// </summary>
    public static class BootTimeline
    {
        private const string PrefsKey = "valkur.boot.weights.v1";

        /// <summary>How much of a new measurement enters the prediction.</summary>
        public const float PredictionAlpha = 0.35f;

        /// <summary>An entry not seen for this many boots is a renamed or deleted step and is dropped.</summary>
        private const int ForgetAfterRuns = 20;

        /// <summary>How far into a step's share the clock alone may carry the bar.</summary>
        private const float CreepCeiling = 0.92f;

        /// <summary>Below this the label is noise in the report rather than a cost.</summary>
        private const float ReportFloorMs = 0.5f;

        public struct StageTiming
        {
            public string Label;
            /// <summary>Time inside the step's body.</summary>
            public float Milliseconds;
            public bool Failed;

            /// <summary>Rendered frames this step spanned. A synchronous step is 0.</summary>
            public int Frames;

            /// <summary>A pass inside a step, not a step. Never persisted as a weight
            /// (weights are keyed by step label) and indented in the report.</summary>
            public bool IsSubStage;

            /// <summary>Start of this step to start of the next. 0 for a sub-stage.</summary>
            public float WallMilliseconds;

            /// <summary>What the history predicted for this step. 0 when unmeasured.</summary>
            public float PredictedMilliseconds;

            public string Phase;
            public int StepIndex;
        }

        private static readonly List<StageTiming> _timings = new List<StageTiming>();
        private static readonly List<string> _failures = new List<string>();
        private static readonly Dictionary<string, float> _measured = new Dictionary<string, float>();
        private static readonly List<float> _stepWeights = new List<float>();
        private static readonly List<float> _stepPredicted = new List<float>();
        private static readonly List<string> _stepLabels = new List<string>();

        private static BootProgress _progress = new BootProgress();
        private static Stopwatch _stepWatch = new Stopwatch();
        private static Stopwatch _runWatch = new Stopwatch();
        private static string _currentLabel = string.Empty;
        private static string _currentPhase = string.Empty;
        private static int _stepCount;
        private static bool _ranThisSession;
        private static bool _runComplete;
        private static int _runStartFrame;
        private static int _stepStartFrame;
        private static int _runFrames;
        private static Stopwatch _subWatch = new Stopwatch();
        private static string _subLabel = string.Empty;
        private static float _stepStartFraction;
        private static float _stepShare;
        private static int _stepSubStages;
        private static int _subReported;
        private static float _lastSubFraction;
        private static int _currentIndex = -1;
        private static double _stepWallStartMs;
        private static int _openWallTiming = -1;
        private static BootPlan _plan = BootPlan.Empty;
        private static WeightDoc _doc;
        private static string _lastLogPath;

        // Scene load, measured by the loading screen before the sequence exists.
        private static Stopwatch _sceneWatch = new Stopwatch();
        private static float _sceneLoadMs = -1f;
        private static float _activationMs = -1f;
        private static bool _activationOpen;

        public static float Fraction => _progress.Fraction;
        public static int StepCount => _stepCount;
        public static bool HasRun => _ranThisSession;
        public static bool IsRunning => _ranThisSession && !_runComplete;
        public static float TotalMilliseconds => (float)_runWatch.Elapsed.TotalMilliseconds;
        public static IReadOnlyList<StageTiming> Timings => _timings;
        public static IReadOnlyList<string> Failures => _failures;
        public static bool AnyFailed => _failures.Count > 0;

        /// <summary>The running boot's etapas. Empty until <see cref="BeginRun"/>.</summary>
        public static BootPlan Plan => _plan;

        /// <summary>Index of the step running now, or -1.</summary>
        public static int CurrentStepIndex => _currentIndex;

        /// <summary>Etapa of the step running now.</summary>
        public static string CurrentPhase => _currentPhase;

        /// <summary>Where the last boot report was written, or null.</summary>
        public static string LastLogPath => _lastLogPath;

        // ── Scene load (reported by the loading screen) ──────────────────────

        /// <summary>The screen is about to call LoadSceneAsync.</summary>
        public static void BeginSceneLoad()
        {
            _sceneLoadMs = -1f;
            _activationMs = -1f;
            _activationOpen = false;
            _sceneWatch = Stopwatch.StartNew();
        }

        /// <summary>The scene's assets are loaded and activation has been permitted.</summary>
        public static void MarkSceneActivation()
        {
            if (!_sceneWatch.IsRunning) return;
            _sceneLoadMs = (float)_sceneWatch.Elapsed.TotalMilliseconds;
            _sceneWatch = Stopwatch.StartNew();
            _activationOpen = true;
        }

        public static float SceneLoadMilliseconds => _sceneLoadMs;
        public static float ActivationMilliseconds => _activationMs;

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
            _stepWeights.Clear();
            _stepPredicted.Clear();
            _stepLabels.Clear();
            _progress = new BootProgress();
            _stepWatch = new Stopwatch();
            _runWatch = new Stopwatch();
            _currentLabel = string.Empty;
            _currentPhase = string.Empty;
            _stepCount = steps != null ? steps.Count : 0;
            _ranThisSession = true;
            _runComplete = false;
            _stepStartFraction = 0f;
            _stepShare = 0f;
            _stepSubStages = 0;
            _subReported = 0;
            _lastSubFraction = 0f;
            _currentIndex = -1;
            _openWallTiming = -1;
            _stepWallStartMs = 0d;

            if (_activationOpen)
            {
                _activationMs = (float)_sceneWatch.Elapsed.TotalMilliseconds;
                _activationOpen = false;
                _sceneWatch.Stop();
            }

            LoadWeights();

            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    float w = ResolveWeight(steps[i]);
                    _progress.Add(w);
                    _stepWeights.Add(w);
                    _stepPredicted.Add(PredictedFor(steps[i]));
                    _stepLabels.Add(steps[i]?.Label ?? string.Empty);
                }
            }
            _plan = BootPlan.FromSteps(steps, _stepWeights, _stepPredicted);

            _runStartFrame = Time.frameCount;
            _runFrames = 0;
            _runWatch.Start();
        }

        /// <summary>
        /// The weight this step should carry: its predicted cost when we have one, its
        /// declared estimate otherwise. A stage measured at under a millisecond still gets
        /// a floor, or a hundred free steps would collectively weigh nothing and the bar
        /// would jump.
        /// </summary>
        private static float ResolveWeight(BootStep step)
        {
            if (step == null) return 1f;
            if (step.IsReported && _measured.TryGetValue(step.Label, out float ms))
                return Mathf.Max(ms, 1f);
            return step.Weight;
        }

        private static float PredictedFor(BootStep step)
        {
            if (step == null || !step.IsReported) return 0f;
            return _measured.TryGetValue(step.Label, out float ms) ? Mathf.Max(ms, 0.01f) : 0f;
        }

        public static void BeginStep(BootStep step)
        {
            double now = _runWatch.Elapsed.TotalMilliseconds;
            CloseWall(now);
            _stepWallStartMs = now;

            _currentIndex++;
            _currentLabel = step != null ? step.Label : string.Empty;
            _currentPhase = step != null ? step.Phase : string.Empty;
            _stepStartFraction = _progress.Fraction;
            _lastSubFraction = _stepStartFraction;
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
        /// BoxCollider2D per painted cell) behind one stopwatch.
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
            f = f > BootProgress.MaxBeforeComplete ? BootProgress.MaxBeforeComplete : f;
            if (f > _lastSubFraction) _lastSubFraction = f;
            return f;
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
                Phase = _currentPhase,
                StepIndex = _currentIndex,
            });
            _subLabel = string.Empty;
        }

        /// <summary>Fraction to show WHILE a step runs — the work already banked.</summary>
        public static float FractionAtStepStart => _stepStartFraction;

        /// <summary>
        /// The fraction the bar should show THIS FRAME: the work banked, the running step's
        /// reported sub-stages, and — when the history has a prediction for the running step —
        /// the clock carrying the bar through the step's share. The clock stops short of the
        /// share (<see cref="CreepCeiling"/>), so a step that overruns its prediction holds the
        /// bar just before its end rather than borrowing from the next etapa.
        /// </summary>
        public static float LiveFraction
        {
            get
            {
                float f = _progress.Fraction;
                if (!IsRunning || _currentIndex < 0 || _currentIndex >= _stepPredicted.Count) return f;
                if (_lastSubFraction > f) f = _lastSubFraction;

                float predicted = _stepPredicted[_currentIndex];
                if (predicted > 0f && _stepShare > 0f)
                {
                    float elapsed = (float)(_runWatch.Elapsed.TotalMilliseconds - _stepWallStartMs);
                    float t = elapsed / predicted;
                    if (t > CreepCeiling) t = CreepCeiling;
                    float creep = _stepStartFraction + t * _stepShare;
                    if (creep > f) f = creep;
                }
                return f > BootProgress.MaxBeforeComplete ? BootProgress.MaxBeforeComplete : f;
            }
        }

        /// <summary>
        /// Predicted milliseconds left in the boot, or -1 when any step still to run has
        /// never been measured — the screen shows no time rather than a wrong one.
        /// </summary>
        public static float PredictedRemainingMs
        {
            get
            {
                if (!IsRunning) return 0f;
                if (!_plan.IsTimed) return -1f;
                float left = 0f;
                int from = _currentIndex < 0 ? 0 : _currentIndex;
                for (int i = from; i < _stepPredicted.Count; i++) left += _stepPredicted[i];
                if (_currentIndex >= 0 && _currentIndex < _stepPredicted.Count)
                {
                    float elapsed = (float)(_runWatch.Elapsed.TotalMilliseconds - _stepWallStartMs);
                    float current = _stepPredicted[_currentIndex];
                    left -= elapsed < current ? elapsed : current;
                }
                return left > 0f ? left : 0f;
            }
        }

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
                PredictedMilliseconds = index >= 0 && index < _stepPredicted.Count ? _stepPredicted[index] : 0f,
                Phase = _currentPhase,
                StepIndex = index,
            });
            _openWallTiming = _timings.Count - 1;
            _progress.CompleteStep(index);
        }

        /// <summary>Wall time of the last finished step runs until the next one starts.</summary>
        private static void CloseWall(double nowMs)
        {
            if (_openWallTiming < 0 || _openWallTiming >= _timings.Count) return;
            var t = _timings[_openWallTiming];
            t.WallMilliseconds = (float)(nowMs - _stepWallStartMs);
            if (t.WallMilliseconds < t.Milliseconds) t.WallMilliseconds = t.Milliseconds;
            _timings[_openWallTiming] = t;
            _openWallTiming = -1;
        }

        public static void RecordFailure(string label, Exception ex)
        {
            string where = string.IsNullOrEmpty(label) ? "(paso silencioso)" : label;
            string what = ex != null ? ex.Message : "error desconocido";
            _failures.Add($"{where} — {what}");
        }

        /// <summary>
        /// Everything is ready: the bar may finally read 100 %, this boot's measurements
        /// enter the prediction, and the run is written to the boot log.
        /// </summary>
        public static void CompleteRun()
        {
            CloseWall(_runWatch.Elapsed.TotalMilliseconds);
            _runWatch.Stop();
            _runFrames = Time.frameCount - _runStartFrame;
            _progress.Complete();
            _runComplete = true;
            _currentIndex = -1;

            var record = BuildRecord();
            SaveWeights();
            _lastLogPath = BootRunLog.TryWrite(record, Report(int.MaxValue));
            if (_lastLogPath != null)
                Debug.Log($"[BootTimeline] Arranque registrado: {record.totalMs / 1000f:F2} s " +
                          $"(prevision {record.predictedTotalMs / 1000f:F2} s) en {BootRunLog.Directory}");

            // Consumed: a later boot that no screen measured must not inherit these.
            _sceneLoadMs = -1f;
            _activationMs = -1f;
        }

        // ── The record ───────────────────────────────────────────────────────

        public static BootRunRecord BuildRecord()
        {
            var r = new BootRunRecord
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"),
                utc = DateTime.UtcNow.ToString("o"),
                unity = Application.unityVersion,
                platform = Application.platform.ToString(),
                device = SystemInfo.deviceModel,
                cpu = SystemInfo.processorType,
                cpuCores = SystemInfo.processorCount,
                ramMb = SystemInfo.systemMemorySize,
                gpu = SystemInfo.graphicsDeviceName,
                editor = Application.isEditor,
                calibrated = IsCalibrated,
                steps = _stepCount,
                frames = RunFrames,
                failures = _failures.Count,
                sceneLoadMs = _sceneLoadMs,
                activationMs = _activationMs,
                bootWallMs = TotalMilliseconds,
                outsideStepsMs = MillisecondsOutsideSteps,
            };
            r.failureMessages.AddRange(_failures);

            float inside = 0f;
            foreach (var t in _timings)
            {
                if (!t.IsSubStage) inside += t.Milliseconds;
                r.stepRows.Add(new BootStepRecord
                {
                    index = t.StepIndex,
                    phase = t.Phase,
                    label = t.IsSubStage ? t.Label.Trim().TrimStart('·').Trim() : t.Label,
                    substage = t.IsSubStage,
                    failed = t.Failed,
                    frames = t.Frames,
                    predictedMs = t.PredictedMilliseconds,
                    cpuMs = t.Milliseconds,
                    wallMs = t.WallMilliseconds,
                });
            }
            r.insideStepsMs = inside;

            // The scene segment first, predicted from the doc as it stood BEFORE this boot.
            float predScene = (_doc != null && _doc.sceneLoadMs > 0f ? _doc.sceneLoadMs : 0f)
                            + (_doc != null && _doc.activationMs > 0f ? _doc.activationMs : 0f);
            float actualScene = (_sceneLoadMs > 0f ? _sceneLoadMs : 0f) + (_activationMs > 0f ? _activationMs : 0f);
            if (_sceneLoadMs > 0f)
                r.phases.Add(new BootPhaseRecord { name = BootPlan.ScenePhase, steps = 0, predictedMs = predScene, actualMs = actualScene });

            foreach (var seg in _plan.Segments)
            {
                float actual = 0f, predicted = 0f;
                foreach (var t in _timings)
                {
                    if (t.IsSubStage || t.StepIndex < seg.FirstStep || t.StepIndex >= seg.FirstStep + seg.StepCount) continue;
                    actual += t.WallMilliseconds > 0f ? t.WallMilliseconds : t.Milliseconds;
                    predicted += t.PredictedMilliseconds;
                }
                r.phases.Add(new BootPhaseRecord { name = seg.Name, steps = seg.StepCount, predictedMs = predicted, actualMs = actual });
            }

            r.totalMs = actualScene + r.bootWallMs;
            float predictedBoot = 0f;
            foreach (var p in _stepPredicted) predictedBoot += p;
            r.predictedTotalMs = (_plan.IsTimed ? predictedBoot : 0f) + (_plan.IsTimed ? predScene : 0f);
            return r;
        }

        // ── Prediction persistence ───────────────────────────────────────────

        [Serializable]
        private class WeightEntry
        {
            public string label;
            public float ms;
            public int samples;
            public float last;
            public int lastRun;
        }

        [Serializable]
        private class PhaseEntry
        {
            public string name;
            public float ms;
        }

        [Serializable]
        private class WeightDoc
        {
            public int version = 2;
            public int runs;
            public float sceneLoadMs = -1f;
            public float activationMs = -1f;
            public List<WeightEntry> entries = new List<WeightEntry>();
            public List<PhaseEntry> phases = new List<PhaseEntry>();
        }

        private static void LoadWeights()
        {
            _measured.Clear();
            _doc = ReadDoc();
            if (_doc == null) return;
            foreach (var e in _doc.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.label)) continue;
                _measured[e.label] = e.ms;
            }
        }

        private static WeightDoc ReadDoc()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var doc = JsonUtility.FromJson<WeightDoc>(json);
                if (doc == null) return null;
                if (doc.entries == null) doc.entries = new List<WeightEntry>();
                if (doc.phases == null) doc.phases = new List<PhaseEntry>();
                return doc;
            }
            catch (Exception ex)
            {
                // A corrupted profile costs exactly one uncalibrated boot, and the
                // next CompleteRun overwrites it. Never worth failing a launch over.
                Debug.LogWarning($"[BootTimeline] Perfil de pesos ilegible, se ignora: {ex.Message}");
                return null;
            }
        }

        /// <summary>One step of the moving average. A first sample is taken as it is.</summary>
        public static float Blend(float previous, int samples, float sample)
            => samples <= 0 || previous <= 0f ? sample : previous + (sample - previous) * PredictionAlpha;

        private static void SaveWeights()
        {
            if (_timings.Count == 0) return;
            var doc = ReadDoc() ?? new WeightDoc();
            doc.version = 2;
            doc.runs++;

            var byLabel = new Dictionary<string, WeightEntry>();
            foreach (var e in doc.entries) if (e != null && !string.IsNullOrEmpty(e.label)) byLabel[e.label] = e;

            bool any = false;
            foreach (var t in _timings)
            {
                if (t.IsSubStage || t.Failed) continue;
                if (string.IsNullOrEmpty(t.Label) || t.Label == "(silencioso)") continue;
                float sample = t.WallMilliseconds > 0f ? t.WallMilliseconds : t.Milliseconds;
                if (!byLabel.TryGetValue(t.Label, out var e))
                    byLabel[t.Label] = e = new WeightEntry { label = t.Label };
                e.ms = Blend(e.ms, e.samples, sample);
                e.samples++;
                e.last = sample;
                e.lastRun = doc.runs;
                any = true;
            }
            if (!any) return;

            doc.entries.Clear();
            foreach (var e in byLabel.Values)
                if (doc.runs - e.lastRun <= ForgetAfterRuns) doc.entries.Add(e);

            if (_sceneLoadMs > 0f)
            {
                doc.sceneLoadMs = Blend(doc.sceneLoadMs, doc.sceneLoadMs > 0f ? 1 : 0, _sceneLoadMs);
                if (_activationMs >= 0f)
                    doc.activationMs = Blend(doc.activationMs, doc.activationMs > 0f ? 1 : 0, _activationMs);
            }

            // The etapas the NEXT screen draws before its own sequence exists.
            doc.phases.Clear();
            foreach (var seg in _plan.Segments)
            {
                float ms = 0f;
                for (int i = seg.FirstStep; i < seg.FirstStep + seg.StepCount && i < _stepLabels.Count; i++)
                    if (!string.IsNullOrEmpty(_stepLabels[i]) && byLabel.TryGetValue(_stepLabels[i], out var e)) ms += e.ms;
                doc.phases.Add(new PhaseEntry { name = seg.Name, ms = ms });
            }

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

        /// <summary>
        /// The bar the loading screen draws BEFORE the boot sequence has been built: the
        /// previous boots' etapas and scene timings. Untimed on a machine with no history.
        /// </summary>
        public static BootScreenPlan PredictScreenPlan()
        {
            var doc = ReadDoc();
            if (doc == null || doc.phases.Count == 0)
                return BootScreenPlan.Compose(-1f, -1f, BootPlan.Empty);

            var names = new List<string>(doc.phases.Count);
            var ms = new List<float>(doc.phases.Count);
            foreach (var p in doc.phases) { names.Add(p?.name); ms.Add(p != null ? p.ms : 0f); }
            return BootScreenPlan.Compose(doc.sceneLoadMs, doc.activationMs, BootPlan.FromPhases(names, ms));
        }

        /// <summary>Drops the calibration so the next boot runs on declared estimates.</summary>
        public static void ClearWeights()
        {
            _measured.Clear();
            _doc = null;
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
            float scene = (_sceneLoadMs > 0f ? _sceneLoadMs : 0f) + (_activationMs > 0f ? _activationMs : 0f);
            sb.Append("Arranque: ").Append(_stepCount).Append(" etapas en ")
              .Append((TotalMilliseconds / 1000f).ToString("F2")).Append(" s")
              .Append(IsCalibrated ? " (barra calibrada)" : " (barra sin calibrar - primer arranque)")
              .AppendLine();
            if (_sceneLoadMs > 0f)
                sb.Append("Escena: carga ").Append(_sceneLoadMs.ToString("F0")).Append(" ms, activacion ")
                  .Append((_activationMs > 0f ? _activationMs : 0f).ToString("F0")).Append(" ms (")
                  .Append(((scene + TotalMilliseconds) / 1000f).ToString("F2")).Append(" s en total)").AppendLine();

            if (_plan.Count > 0)
            {
                sb.Append("Etapas (pared, prevision):").AppendLine();
                foreach (var seg in _plan.Segments)
                {
                    float actual = 0f, predicted = 0f;
                    foreach (var t in _timings)
                    {
                        if (t.IsSubStage || t.StepIndex < seg.FirstStep || t.StepIndex >= seg.FirstStep + seg.StepCount) continue;
                        actual += t.WallMilliseconds > 0f ? t.WallMilliseconds : t.Milliseconds;
                        predicted += t.PredictedMilliseconds;
                    }
                    sb.Append("  ").Append(actual.ToString("F0").PadLeft(7)).Append(" ms  ");
                    if (predicted > 0f)
                    {
                        float err = (actual - predicted) / predicted * 100f;
                        sb.Append(("prev " + predicted.ToString("F0")).PadLeft(10)).Append(" ms ")
                          .Append(((err >= 0f ? "+" : "") + err.ToString("F0") + "%").PadLeft(6));
                    }
                    else sb.Append("   sin prevision   ");
                    sb.Append("  ").Append(seg.Name).Append(" (").Append(seg.StepCount).Append(')').AppendLine();
                }
            }

            // Steps and sub-stages are listed apart on purpose: a sub-stage's cost is
            // ALREADY inside its parent, so mixing them into one sorted list double
            // counts the total and reads as if the boot did twice the work it did.
            var steps = new List<StageTiming>();
            var subs  = new List<StageTiming>();
            foreach (var t in _timings) (t.IsSubStage ? subs : steps).Add(t);

            steps.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
            subs.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));

            sb.Append("Pasos (cpu, pared):").AppendLine();
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
            string wall = t.WallMilliseconds > 0f ? (t.WallMilliseconds.ToString("F0") + " ms").PadLeft(9) : "         ";
            return "  " + t.Milliseconds.ToString("F1").PadLeft(8) + " ms  " +
                   pct.ToString("F1").PadLeft(5) + "%  " + frames + wall + "  " +
                   (t.Failed ? "[FALLO] " : string.Empty) + t.Label;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _timings.Clear();
            _failures.Clear();
            _measured.Clear();
            _stepWeights.Clear();
            _stepPredicted.Clear();
            _stepLabels.Clear();
            _progress = new BootProgress();
            _stepWatch = new Stopwatch();
            _runWatch = new Stopwatch();
            _currentLabel = string.Empty;
            _currentPhase = string.Empty;
            _stepCount = 0;
            _ranThisSession = false;
            _runComplete = false;
            _stepStartFraction = 0f;
            _stepShare = 0f;
            _stepSubStages = 0;
            _subReported = 0;
            _lastSubFraction = 0f;
            _subLabel = string.Empty;
            _subWatch = new Stopwatch();
            _runStartFrame = 0;
            _stepStartFrame = 0;
            _runFrames = 0;
            _currentIndex = -1;
            _stepWallStartMs = 0d;
            _openWallTiming = -1;
            _plan = BootPlan.Empty;
            _doc = null;
            _lastLogPath = null;
            _sceneWatch = new Stopwatch();
            _sceneLoadMs = -1f;
            _activationMs = -1f;
            _activationOpen = false;
        }
    }
}
