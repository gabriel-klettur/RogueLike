namespace Valkur.Core.Diagnostics
{
    /// <summary>One frame that took far longer than the frames around it.</summary>
    public readonly struct FrameHitch
    {
        /// <summary>Real time the frame ended at (<c>Time.realtimeSinceStartup</c>).</summary>
        public readonly float Time;
        public readonly float Ms;

        /// <summary>A gen-0 garbage collection ran during this frame.</summary>
        public readonly bool DuringGc;

        public FrameHitch(float time, float ms, bool duringGc)
        {
            Time = time;
            Ms = ms;
            DuringGc = duringGc;
        }
    }

    /// <summary>
    /// The last few hitches, newest first, and the rule that decides what counts as one.
    ///
    /// <para><b>Why a log at all.</b> A hitch lasts forty milliseconds. A graph shows it for as
    /// long as its column is on screen and a percentile smooths it into a number, so the one
    /// event a performance overlay exists to catch was, on the old panel, gone by the time
    /// anybody looked. The log is what makes "it stuttered a moment ago" answerable.</para>
    /// </summary>
    public sealed class FrameHitchLog
    {
        private readonly FrameHitch[] _ring;
        private int _head;
        private int _count;

        /// <summary>Hitches recorded since the log was created, including ones that fell off.</summary>
        public int TotalRecorded { get; private set; }

        public FrameHitchLog(int capacity = 8)
        {
            _ring = new FrameHitch[capacity < 1 ? 1 : capacity];
        }

        public int Count => _count;
        public int Capacity => _ring.Length;

        public void Record(FrameHitch hitch)
        {
            _ring[_head] = hitch;
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
            TotalRecorded++;
        }

        /// <summary>A recorded hitch, <paramref name="age"/> 0 being the newest.</summary>
        public FrameHitch Get(int age)
        {
            if (age < 0 || age >= _count) return default;
            int i = _head - 1 - age;
            if (i < 0) i += _ring.Length;
            return _ring[i];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            TotalRecorded = 0;
        }

        /// <summary>
        /// A frame is a hitch when it is BOTH far longer than the typical frame and long in
        /// absolute terms. Both halves are needed: a ratio alone flags a 4 ms frame at 400 FPS,
        /// which nobody can see; a floor alone flags every frame on a machine running at 30.
        /// </summary>
        public static bool IsHitch(float frameMs, float typicalMs, float factor, float floorMs)
        {
            if (frameMs < floorMs) return false;
            if (typicalMs <= 0f) return true;
            return frameMs >= typicalMs * factor;
        }
    }
}
