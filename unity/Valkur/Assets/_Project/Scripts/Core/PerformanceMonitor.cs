using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Runtime performance monitor that tracks FPS, frame time, and GC allocations.
    /// Collects rolling statistics (p95, p99) and exposes them via public API.
    /// The DebugHUD (F1) reads from this singleton to display performance data.
    /// </summary>
    public class PerformanceMonitor : SingletonMonoBehaviour<PerformanceMonitor>
    {
        private const int SAMPLE_COUNT = 300;
        private const float COMPUTE_INTERVAL = 2f;

        [Header("Settings")]
        [SerializeField] private bool logToConsole;
        [SerializeField] private float logInterval = 10f;

        private float[] _frameTimes;
        private int _frameIndex;
        private int _frameCount;
        private float _computeTimer;
        private float _logTimer;

        private float _avgFps;
        private float _avgFrameTime;
        private float _p95FrameTime;
        private float _p99FrameTime;
        private float _minFps;
        private float _maxFrameTime;

        public float AvgFps => _avgFps;
        public float AvgFrameTimeMs => _avgFrameTime * 1000f;
        public float P95FrameTimeMs => _p95FrameTime * 1000f;
        public float P99FrameTimeMs => _p99FrameTime * 1000f;
        public float MinFps => _minFps;

        protected override void OnSingletonAwake()
        {
            _frameTimes = new float[SAMPLE_COUNT];
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Record frame time
            _frameTimes[_frameIndex] = dt;
            _frameIndex = (_frameIndex + 1) % SAMPLE_COUNT;
            if (_frameCount < SAMPLE_COUNT) _frameCount++;

            // Compute stats periodically (not every frame)
            _computeTimer += dt;
            if (_computeTimer >= COMPUTE_INTERVAL)
            {
                ComputeStats();
                _computeTimer = 0f;
            }

            // Optional console logging
            if (logToConsole)
            {
                _logTimer += dt;
                if (_logTimer >= logInterval)
                {
                    LogStats();
                    _logTimer = 0f;
                }
            }
        }

        private void ComputeStats()
        {
            if (_frameCount == 0) return;

            float[] sorted = new float[_frameCount];
            System.Array.Copy(_frameTimes, sorted, _frameCount);
            System.Array.Sort(sorted);

            float sum = 0f;
            for (int i = 0; i < _frameCount; i++)
                sum += sorted[i];

            _avgFrameTime = sum / _frameCount;
            _avgFps = 1f / Mathf.Max(_avgFrameTime, 0.0001f);
            _maxFrameTime = sorted[_frameCount - 1];
            _minFps = 1f / Mathf.Max(_maxFrameTime, 0.0001f);

            int p95Index = Mathf.FloorToInt(_frameCount * 0.95f);
            int p99Index = Mathf.FloorToInt(_frameCount * 0.99f);
            _p95FrameTime = sorted[Mathf.Clamp(p95Index, 0, _frameCount - 1)];
            _p99FrameTime = sorted[Mathf.Clamp(p99Index, 0, _frameCount - 1)];
        }

        private void LogStats()
        {
            Debug.Log($"[PerfMon] FPS avg={_avgFps:F1} min={_minFps:F1} | " +
                      $"Frame avg={_avgFrameTime * 1000f:F2}ms p95={_p95FrameTime * 1000f:F2}ms p99={_p99FrameTime * 1000f:F2}ms | " +
                      $"GC={System.GC.CollectionCount(0)}");
        }

        /// <summary>
        /// Force a stats dump to console.
        /// </summary>
        public void DumpStats()
        {
            ComputeStats();
            LogStats();
        }
    }
}
