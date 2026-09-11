using Unity.Profiling;
using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The engine's own counters, read through <see cref="ProfilerRecorder"/>: main-thread time,
    /// GPU time, batches, SetPass calls, bytes allocated this frame, memory in use.
    ///
    /// <para><b>Only while the panel is open.</b> A recorder costs a little every frame it is
    /// alive, so they are started when the full panel is shown and disposed when it closes —
    /// a tool that measures while nobody is looking pays for a readout nobody reads.</para>
    ///
    /// <para><b>A counter the platform does not have is "-", never 0.</b> Several of these exist
    /// only in the Editor and in Development builds; a release player reports them invalid, and
    /// printing 0 there would read as "no batches", which is a claim, not an absence.</para>
    ///
    /// <para>GPU time comes from <see cref="FrameTimingManager"/>, which needs "Frame Timing
    /// Stats" in the Player settings; without it the row says "-" rather than inventing a number.</para>
    /// </summary>
    public sealed class DebugHudCounters : System.IDisposable
    {
        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _usedMemory;
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private bool _running;

        public bool Running => _running;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _mainThread.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _gcAlloc.Dispose();
            _usedMemory.Dispose();
        }

        public void Dispose() => Stop();

        /// <summary>Main-thread milliseconds averaged over the recorder's samples, or -1.</summary>
        public float MainThreadMs
        {
            get
            {
                if (!_running || !_mainThread.Valid || _mainThread.Count == 0) return -1f;
                double sum = 0;
                int n = _mainThread.Count;
                for (int i = 0; i < n; i++) sum += _mainThread.GetSample(i).Value;
                return (float)(sum / n * 1e-6);
            }
        }

        public long Batches => Read(_batches);
        public long SetPassCalls => Read(_setPass);
        public long GcAllocatedInFrame => Read(_gcAlloc);
        public long UsedMemory => Read(_usedMemory);

        /// <summary>GPU milliseconds of the latest timed frame, or -1 when frame timing is unavailable.</summary>
        public float GpuMs
        {
            get
            {
                if (!_running) return -1f;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) == 0) return -1f;
                double gpu = _timings[0].gpuFrameTime;
                return gpu > 0.0 ? (float)gpu : -1f;
            }
        }

        private long Read(ProfilerRecorder r) => _running && r.Valid ? r.LastValue : -1;
    }
}
