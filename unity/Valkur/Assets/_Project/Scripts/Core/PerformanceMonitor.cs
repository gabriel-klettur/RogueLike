using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core
{
    /// <summary>
    /// Runtime performance monitor that tracks FPS, frame time, and GC allocations.
    /// Maps to Python's benchmark decorator and perf_log system.
    /// 
    /// Provides:
    /// - Rolling average FPS and frame time (p50, p95, p99).
    /// - GC allocation tracking per interval.
    /// - Optional on-screen debug overlay (toggle with F3).
    /// - Log dump on demand for profiling sessions.
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour
    {
        private const int SAMPLE_COUNT = 300;
        private const float LOG_INTERVAL = 10f;

        [Header("Settings")]
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logToConsole;

        private float[] _frameTimes;
        private int _frameIndex;
        private int _frameCount;
        private float _logTimer;

        private float _avgFps;
        private float _avgFrameTime;
        private float _p95FrameTime;
        private float _p99FrameTime;
        private float _minFps;
        private float _maxFrameTime;
        private long _lastGcCount;
        private int _gcCollections;

        private static PerformanceMonitor _instance;
        public static PerformanceMonitor Instance => _instance;

        public float AvgFps => _avgFps;
        public float AvgFrameTimeMs => _avgFrameTime * 1000f;
        public float P95FrameTimeMs => _p95FrameTime * 1000f;
        public float P99FrameTimeMs => _p99FrameTime * 1000f;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _frameTimes = new float[SAMPLE_COUNT];
            _lastGcCount = System.GC.CollectionCount(0);

            _toggleAction = new InputAction("TogglePerfMon", InputActionType.Button, "<Keyboard>/f3");
            _toggleAction.Enable();
        }

        private InputAction _toggleAction;

        private void Update()
        {
            // Toggle overlay
            if (_toggleAction != null && _toggleAction.WasPerformedThisFrame())
                showOverlay = !showOverlay;

            // Record frame time
            float dt = Time.unscaledDeltaTime;
            _frameTimes[_frameIndex] = dt;
            _frameIndex = (_frameIndex + 1) % SAMPLE_COUNT;
            if (_frameCount < SAMPLE_COUNT) _frameCount++;

            // Track GC
            long gcNow = System.GC.CollectionCount(0);
            if (gcNow > _lastGcCount)
            {
                _gcCollections += (int)(gcNow - _lastGcCount);
                _lastGcCount = gcNow;
            }

            // Compute stats periodically
            _logTimer += dt;
            if (_logTimer >= LOG_INTERVAL)
            {
                ComputeStats();
                _logTimer = 0f;

                if (logToConsole)
                    LogStats();
            }
        }

        private void ComputeStats()
        {
            if (_frameCount == 0) return;

            // Copy and sort for percentile calculation
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
                      $"GC collections={_gcCollections}");
            _gcCollections = 0;
        }

        /// <summary>
        /// Force a stats dump to console.
        /// </summary>
        public void DumpStats()
        {
            ComputeStats();
            LogStats();
        }

        private void OnGUI()
        {
            if (!showOverlay) return;

            // Compute live FPS each frame for display
            float liveFps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float liveMs = Time.unscaledDeltaTime * 1000f;

            int w = 280;
            int h = 90;
            int x = Screen.width - w - 10;
            int y = 10;

            GUI.Box(new Rect(x, y, w, h), "");

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            Color fpsColor = liveFps >= 55 ? Color.green : liveFps >= 30 ? Color.yellow : Color.red;
            style.normal.textColor = fpsColor;

            GUI.Label(new Rect(x + 5, y + 5, w - 10, 20), $"FPS: {liveFps:F0}  ({liveMs:F1}ms)", style);

            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Normal;
            style.fontSize = 11;

            GUI.Label(new Rect(x + 5, y + 25, w - 10, 20),
                $"Avg: {_avgFps:F0} FPS | p95: {_p95FrameTime * 1000f:F1}ms", style);
            GUI.Label(new Rect(x + 5, y + 45, w - 10, 20),
                $"p99: {_p99FrameTime * 1000f:F1}ms | GC: {_gcCollections}", style);
            int entityCount = EntityRegistry.MonsterCount + (EntityRegistry.HasPlayer ? 1 : 0);
            GUI.Label(new Rect(x + 5, y + 65, w - 10, 20),
                $"Entities: {entityCount} | F3 toggle", style);
        }

        private void OnDestroy()
        {
            _toggleAction?.Disable();
            _toggleAction?.Dispose();

            if (_instance == this)
                _instance = null;
        }
    }
}
