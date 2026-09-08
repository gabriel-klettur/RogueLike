using System.Collections.Generic;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// The bar's arithmetic, as a pure class so an EditMode test can walk a whole
    /// boot without Unity.
    ///
    /// Two properties are the entire point, and the shipped implementation had
    /// neither:
    ///
    /// <list type="bullet">
    ///   <item><b>The total is derived, never declared.</b> It is the sum of the
    ///   weights of the steps actually queued, so it cannot go stale the way
    ///   <c>SetupStepTotal = 53</c> did against a real 70.</item>
    ///   <item><b>100 % means READY.</b> <see cref="Fraction"/> is capped below 1
    ///   until <see cref="Complete"/> is called, so the bar physically cannot
    ///   claim the game is loaded while seventeen stages are still running. The
    ///   old code clamped the overflow instead, which hid the drift rather than
    ///   preventing it.</item>
    /// </list>
    /// </summary>
    public sealed class BootProgress
    {
        /// <summary>
        /// The ceiling the bar may reach on stage reports alone. Deliberately not
        /// 1: the last visible percent belongs to <see cref="Complete"/>, so a
        /// player looking at 100 % is looking at a game that is ready.
        /// </summary>
        public const float MaxBeforeComplete = 0.99f;

        private readonly List<float> _weights = new List<float>();
        private float _total;
        private float _done;
        private bool _complete;

        public int StepCount => _weights.Count;
        public float TotalWeight => _total;
        public bool IsComplete => _complete;

        /// <summary>Completed weight over total, capped below 1 until <see cref="Complete"/>.</summary>
        public float Fraction
        {
            get
            {
                if (_complete) return 1f;
                if (_total <= 0f) return 0f;
                float f = _done / _total;
                if (f < 0f) f = 0f;
                return f > MaxBeforeComplete ? MaxBeforeComplete : f;
            }
        }

        /// <summary>Queue a step's weight. Call once per step, before the run starts.</summary>
        public void Add(float weight)
        {
            if (weight <= 0f) weight = 0.0001f;
            _weights.Add(weight);
            _total += weight;
        }

        public void AddRange(IEnumerable<float> weights)
        {
            if (weights == null) return;
            foreach (var w in weights) Add(w);
        }

        /// <summary>
        /// Mark the step at <paramref name="index"/> finished. Out-of-range indices
        /// are ignored rather than thrown: a runner that grew a step mid-run is a
        /// bug to be seen in the timeline, not a reason to abort a boot.
        /// </summary>
        public void CompleteStep(int index)
        {
            if (_complete) return;
            if (index < 0 || index >= _weights.Count) return;
            _done += _weights[index];
            if (_done > _total) _done = _total;
        }

        /// <summary>Everything is ready. This is the ONLY way to reach 1.</summary>
        public void Complete() => _complete = true;

        public void Reset()
        {
            _weights.Clear();
            _total = 0f;
            _done = 0f;
            _complete = false;
        }
    }
}
