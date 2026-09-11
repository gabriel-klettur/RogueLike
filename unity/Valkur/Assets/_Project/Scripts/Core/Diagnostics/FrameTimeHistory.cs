using System;

namespace Valkur.Core.Diagnostics
{
    /// <summary>What a window of frames looked like. Every value is in milliseconds.</summary>
    public readonly struct FrameStats
    {
        public readonly int Frames;
        public readonly float AvgMs;
        public readonly float P50Ms;
        public readonly float P95Ms;
        public readonly float P99Ms;
        public readonly float MaxMs;

        public FrameStats(int frames, float avgMs, float p50Ms, float p95Ms, float p99Ms, float maxMs)
        {
            Frames = frames;
            AvgMs = avgMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            MaxMs = maxMs;
        }

        /// <summary>Frames per second implied by the average frame, 0 when there is no data.</summary>
        public float Fps => AvgMs > 0.0001f ? 1000f / AvgMs : 0f;

        public bool IsEmpty => Frames == 0;
    }

    /// <summary>
    /// A ring of recent frame durations, each stamped with the real time it ended at, and the
    /// statistics over a window of them.
    ///
    /// <para><b>The window is in SECONDS, not in frames.</b> The monitor this replaced sorted
    /// the last 300 frames, which is 2.6 s at 114 FPS and 10 s at 30 — so the p95 printed next
    /// to a 0.5-second FPS average described a different stretch of time on every machine, and
    /// on the same machine whenever the frame rate moved. A window in seconds is the same
    /// question at any frame rate.</para>
    ///
    /// <para><b>No allocation after construction.</b> The sort runs on a scratch array held
    /// here; the old code did <c>new float[n]</c> every two seconds, in the one class whose job
    /// is to report on garbage.</para>
    ///
    /// <para>Pure and scene-free, so the percentile maths is provable in EditMode.</para>
    /// </summary>
    public sealed class FrameTimeHistory
    {
        public const int DefaultCapacity = 1024;

        private readonly float[] _ms;
        private readonly float[] _stamp;
        private readonly float[] _scratch;
        private int _head;      // index the NEXT push writes to
        private int _count;

        public FrameTimeHistory(int capacity = DefaultCapacity)
        {
            if (capacity < 2) capacity = 2;
            _ms = new float[capacity];
            _stamp = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Capacity => _ms.Length;
        public int Count => _count;

        /// <summary>Records one frame of <paramref name="ms"/> that ended at <paramref name="realtime"/>.</summary>
        public void Push(float ms, float realtime)
        {
            _ms[_head] = ms < 0f ? 0f : ms;
            _stamp[_head] = realtime;
            _head = (_head + 1) % _ms.Length;
            if (_count < _ms.Length) _count++;
        }

        /// <summary>Duration of a recorded frame, <paramref name="age"/> 0 being the newest.</summary>
        public float MsAt(int age)
        {
            if (age < 0 || age >= _count) return 0f;
            return _ms[IndexOfAge(age)];
        }

        /// <summary>When a recorded frame ended, <paramref name="age"/> 0 being the newest.</summary>
        public float StampAt(int age)
        {
            if (age < 0 || age >= _count) return 0f;
            return _stamp[IndexOfAge(age)];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Statistics over every frame that ended within <paramref name="windowSeconds"/> of
        /// <paramref name="now"/>. At least one frame is always included when any exist, so a
        /// single enormous hitch that is longer than the window is not reported as "no data".
        /// </summary>
        public FrameStats Compute(float windowSeconds, float now)
        {
            if (_count == 0) return default;

            int n = 0;
            float sum = 0f;
            for (int age = 0; age < _count; age++)
            {
                int i = IndexOfAge(age);
                if (n > 0 && now - _stamp[i] > windowSeconds) break;
                float v = _ms[i];
                _scratch[n++] = v;
                sum += v;
            }

            Array.Sort(_scratch, 0, n);
            return new FrameStats(
                n,
                sum / n,
                Percentile(_scratch, n, 0.50f),
                Percentile(_scratch, n, 0.95f),
                Percentile(_scratch, n, 0.99f),
                _scratch[n - 1]);
        }

        /// <summary>
        /// Nearest-rank percentile of the first <paramref name="n"/> values of an ASCENDING
        /// array: the smallest value at or below which <paramref name="p"/> of the samples fall.
        /// Nearest rank rather than interpolation because every value it returns is a frame
        /// that actually happened.
        /// </summary>
        public static float Percentile(float[] sortedAscending, int n, float p)
        {
            if (sortedAscending == null || n <= 0) return 0f;
            if (p <= 0f) return sortedAscending[0];
            // The epsilon is load-bearing: 0.99f * 100 is 99.0000x in float, and a bare Ceiling
            // turns the p99 of 1..100 into 100.
            int rank = (int)Math.Ceiling(p * n - 1e-4);
            if (rank < 1) rank = 1;
            if (rank > n) rank = n;
            return sortedAscending[rank - 1];
        }

        private int IndexOfAge(int age)
        {
            int i = _head - 1 - age;
            if (i < 0) i += _ms.Length;
            return i;
        }
    }
}
