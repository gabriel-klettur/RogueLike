using UnityEngine;
using Valkur.Core.Diagnostics;

namespace Valkur.Core
{
    /// <summary>
    /// The single owner of frame-time MEASUREMENT: every frame's duration, the statistics over a
    /// window of them, the hitches, the garbage collections and the console's error count. The
    /// debug HUD (F1) only READS this; nothing else keeps a second FPS counter.
    ///
    /// <para><b>Everything is sampled every frame, whether or not anything is showing it.</b>
    /// The overlay is usually closed when a hitch happens, and a log that only filled while it
    /// was open would be empty exactly when somebody opens it to ask what just went wrong.
    /// The per-frame cost is a store into a ring and one <c>GC.CollectionCount</c> call.</para>
    ///
    /// <para><b>One window, in seconds.</b> Every statistic here describes the same stretch of
    /// time (<see cref="StatsWindowSeconds"/>). The old monitor reported p95 over the last 300
    /// FRAMES beside a debug HUD that averaged its own FPS over half a second, so two numbers
    /// printed side by side described different stretches of time.</para>
    /// </summary>
    public class PerformanceMonitor : SingletonMonoBehaviour<PerformanceMonitor>
    {
        /// <summary>Seconds every statistic is computed over.</summary>
        public const float StatsWindowSeconds = 3f;

        /// <summary>How often the statistics are recomputed. The ring is sampled every frame.</summary>
        public const float ComputeIntervalSeconds = 0.25f;

        /// <summary>A hitch is at least this many times the median frame…</summary>
        public const float HitchFactor = 2f;

        /// <summary>…and at least this long: 25 ms is a dropped frame at 40 Hz, visible to anyone.</summary>
        public const float HitchFloorMs = 25f;

        [Header("Settings")]
        [Tooltip("Print a summary line to the console every Log Interval seconds.")]
        [SerializeField] private bool logToConsole;
        [Tooltip("Seconds between console summaries when Log To Console is on.")]
        [SerializeField] private float logInterval = 10f;

        private FrameTimeHistory _history;
        private FrameHitchLog _hitches;
        private ConsoleLogTally _console;
        private FrameStats _stats;
        private float _computeTimer;
        private float _logTimer;
        private int _lastGcCount = -1;
        private int _sessionGcStart = -1;
        private float _sessionStart = -1f;
        private bool _logHooked;

        public float AvgFps => _stats.Fps;
        public float AvgFrameTimeMs => _stats.AvgMs;
        public float P50FrameTimeMs => _stats.P50Ms;
        public float P95FrameTimeMs => _stats.P95Ms;
        public float P99FrameTimeMs => _stats.P99Ms;
        public float MaxFrameTimeMs => _stats.MaxMs;
        public float MinFps => _stats.MaxMs > 0.0001f ? 1000f / _stats.MaxMs : 0f;

        /// <summary>The last computed window. Recomputed every <see cref="ComputeIntervalSeconds"/>.</summary>
        public FrameStats Stats => _stats;

        public FrameTimeHistory History => Ensure()._history;
        public FrameHitchLog Hitches => Ensure()._hitches;
        public ConsoleLogTally Console => Ensure()._console;

        /// <summary>Gen-0 collections since this monitor started — not since the PROCESS did,
        /// which in the Editor includes everything the Editor did since it was opened.</summary>
        public int SessionGcCollections =>
            _sessionGcStart < 0 || _lastGcCount < 0 ? 0 : Mathf.Max(0, _lastGcCount - _sessionGcStart);

        /// <summary>Real seconds since this monitor started sampling.</summary>
        public float SessionSeconds => _sessionStart < 0f ? 0f : Time.realtimeSinceStartup - _sessionStart;

        /// <summary>Collections per minute over the session, the number a GC readout should be.</summary>
        public float GcPerMinute
        {
            get
            {
                float minutes = SessionSeconds / 60f;
                return minutes > 0.05f ? SessionGcCollections / minutes : 0f;
            }
        }

        /// <summary>Raised on the main thread for every hitch, as it is recorded.</summary>
        public event System.Action<FrameHitch> HitchRecorded;

        protected override void OnSingletonAwake()
        {
            Ensure();
        }

        private void OnEnable()
        {
            Ensure();
            if (_logHooked) return;
            Application.logMessageReceivedThreaded += OnLogMessage;
            _logHooked = true;
        }

        private void OnDisable()
        {
            if (!_logHooked) return;
            Application.logMessageReceivedThreaded -= OnLogMessage;
            _logHooked = false;
        }

        private void Update()
        {
            Sample(Time.unscaledDeltaTime * 1000f, System.GC.CollectionCount(0), Time.realtimeSinceStartup);

            if (!logToConsole) return;
            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer < logInterval) return;
            _logTimer = 0f;
            LogStats();
        }

        /// <summary>
        /// Records one frame. Public so an EditMode test can drive the monitor without a clock —
        /// Unity calls no Awake or Update on a component added in Edit Mode.
        /// </summary>
        public void Sample(float frameMs, int gcCount, float realtime)
        {
            Ensure();
            if (_sessionStart < 0f) _sessionStart = realtime;
            if (_sessionGcStart < 0) _sessionGcStart = gcCount;

            bool gcThisFrame = _lastGcCount >= 0 && gcCount > _lastGcCount;
            _lastGcCount = gcCount;

            // The hitch test compares against the median BEFORE this frame joins the window, so
            // a long run of slow frames cannot make itself look typical in one step.
            float typical = _stats.IsEmpty ? 0f : _stats.P50Ms;
            _history.Push(frameMs, realtime);
            if (!_stats.IsEmpty && FrameHitchLog.IsHitch(frameMs, typical, HitchFactor, HitchFloorMs))
            {
                var hitch = new FrameHitch(realtime, frameMs, gcThisFrame);
                _hitches.Record(hitch);
                HitchRecorded?.Invoke(hitch);
            }

            _computeTimer += frameMs * 0.001f;
            if (_stats.IsEmpty || _computeTimer >= ComputeIntervalSeconds)
            {
                _computeTimer = 0f;
                _stats = _history.Compute(StatsWindowSeconds, realtime);
            }
        }

        /// <summary>Forgets every sample, hitch and console count — the "start measuring now" button.</summary>
        public void ResetSession()
        {
            Ensure();
            _history.Clear();
            _hitches.Clear();
            _console.Clear();
            _stats = default;
            _computeTimer = 0f;
            _sessionStart = -1f;
            _sessionGcStart = -1;
            _lastGcCount = -1;
        }

        private PerformanceMonitor Ensure()
        {
            if (_history == null) _history = new FrameTimeHistory();
            if (_hitches == null) _hitches = new FrameHitchLog();
            if (_console == null) _console = new ConsoleLogTally();
            return this;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            // Off the main thread: Time may not be read here. The stamp is only used to order
            // the last error against the session, so the thread-safe clock is enough.
            _console?.Record(type, condition, (float)System.Diagnostics.Stopwatch.GetTimestamp()
                                               / System.Diagnostics.Stopwatch.Frequency);
        }

        private void LogStats()
        {
            Debug.Log($"[PerfMon] FPS avg={_stats.Fps:F1} min={MinFps:F1} | " +
                      $"Frame avg={_stats.AvgMs:F2}ms p95={_stats.P95Ms:F2}ms p99={_stats.P99Ms:F2}ms | " +
                      $"GC={SessionGcCollections} ({GcPerMinute:F1}/min) hitches={_hitches.TotalRecorded}");
        }

        /// <summary>Force a stats dump to console.</summary>
        public void DumpStats()
        {
            Ensure();
            _stats = _history.Compute(StatsWindowSeconds, Time.realtimeSinceStartup);
            LogStats();
        }
    }
}
