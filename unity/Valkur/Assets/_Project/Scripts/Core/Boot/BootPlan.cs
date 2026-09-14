using System.Collections.Generic;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// The boot divided into ETAPAS, each with its share of the bar and the time it is
    /// predicted to take. Pure, so the arithmetic is testable without a scene.
    ///
    /// <para><b>A segment's share is the sum of its steps' WEIGHTS</b>, the same numbers
    /// <see cref="BootProgress"/> advances by — so a divider drawn at a segment's end is
    /// exactly where the bar stands when that segment's last step finishes. Computing the
    /// share from any other quantity would put the divider somewhere the fill never stops.
    /// Once the machine has booted once, the weights ARE predicted milliseconds, so share and
    /// prediction agree; on a first boot the weights are declared estimates and
    /// <see cref="IsTimed"/> is false, which is what stops the screen promising a time it
    /// has never measured.</para>
    /// </summary>
    public sealed class BootPlan
    {
        /// <summary>The phase of a step nobody placed in one.</summary>
        public const string DefaultPhase = "Arranque";

        /// <summary>The segment in front of the boot: loading and activating the scene.</summary>
        public const string ScenePhase = "Cargando la escena";

        public readonly struct Segment
        {
            public readonly string Name;
            public readonly int FirstStep;
            public readonly int StepCount;
            public readonly float Weight;
            public readonly float PredictedMs;
            /// <summary>Start and end in plan space, 0..1.</summary>
            public readonly float Start;
            public readonly float End;

            public Segment(string name, int firstStep, int stepCount, float weight,
                           float predictedMs, float start, float end)
            {
                Name = name;
                FirstStep = firstStep;
                StepCount = stepCount;
                Weight = weight;
                PredictedMs = predictedMs;
                Start = start;
                End = end;
            }
        }

        private readonly List<Segment> _segments = new List<Segment>();

        public IReadOnlyList<Segment> Segments => _segments;
        public int Count => _segments.Count;
        public float TotalWeight { get; private set; }
        public float TotalPredictedMs { get; private set; }

        /// <summary>True when the prediction covers the sequence (all but a new step or two).</summary>
        public bool IsTimed { get; private set; }

        public static readonly BootPlan Empty = new BootPlan();

        private BootPlan() { }

        /// <summary>
        /// Groups consecutive steps of one phase into a segment. <paramref name="predictedMs"/>
        /// holds 0 for a step with no measurement; more than a few such steps make the plan untimed.
        /// </summary>
        public static BootPlan FromSteps(IReadOnlyList<BootStep> steps,
                                         IReadOnlyList<float> weights,
                                         IReadOnlyList<float> predictedMs)
        {
            var plan = new BootPlan();
            if (steps == null || steps.Count == 0) return plan;

            var names = new List<string>();
            var w = new List<float>();
            var ms = new List<float>();
            var first = new List<int>();
            var count = new List<int>();
            int reported = 0, unmeasured = 0;

            for (int i = 0; i < steps.Count; i++)
            {
                string phase = steps[i]?.Phase ?? DefaultPhase;
                float weight = weights != null && i < weights.Count ? weights[i] : 1f;
                if (weight <= 0f) weight = 0.0001f;
                float pred = predictedMs != null && i < predictedMs.Count ? predictedMs[i] : 0f;
                // A silent step is never measured on its own (weights are keyed by label), so its
                // lack of a prediction must not make the whole plan untimed.
                if (steps[i] != null && steps[i].IsReported)
                {
                    reported++;
                    if (pred <= 0f) unmeasured++;
                }

                int last = names.Count - 1;
                if (last < 0 || names[last] != phase)
                {
                    names.Add(phase); w.Add(0f); ms.Add(0f); first.Add(i); count.Add(0);
                    last++;
                }
                w[last] += weight;
                ms[last] += pred > 0f ? pred : 0f;
                count[last]++;
            }

            // One brand-new step should not cost the screen its time estimate for a whole boot;
            // a sequence that is mostly unmeasured (a first boot, a wiped profile) should.
            int tolerance = reported / 20 > 1 ? reported / 20 : 1;
            bool timed = reported > 0 && unmeasured <= tolerance && unmeasured < reported;
            plan.Fill(names, first, count, w, ms, timed);
            return plan;
        }

        /// <summary>A plan from phase totals alone — what a PREVIOUS boot saved.</summary>
        public static BootPlan FromPhases(IReadOnlyList<string> names, IReadOnlyList<float> predictedMs)
        {
            var plan = new BootPlan();
            if (names == null || predictedMs == null || names.Count == 0) return plan;
            int n = names.Count < predictedMs.Count ? names.Count : predictedMs.Count;

            var nm = new List<string>(n);
            var w = new List<float>(n);
            var first = new List<int>(n);
            var count = new List<int>(n);
            bool timed = true;
            for (int i = 0; i < n; i++)
            {
                float ms = predictedMs[i];
                if (ms <= 0f) timed = false;
                nm.Add(string.IsNullOrWhiteSpace(names[i]) ? DefaultPhase : names[i]);
                w.Add(ms > 0f ? ms : 1f);
                first.Add(i);
                count.Add(1);
            }
            plan.Fill(nm, first, count, w, w, timed);
            return plan;
        }

        private void Fill(List<string> names, List<int> first, List<int> count,
                          List<float> weights, List<float> ms, bool timed)
        {
            float total = 0f, totalMs = 0f;
            for (int i = 0; i < weights.Count; i++) { total += weights[i]; totalMs += ms[i]; }
            TotalWeight = total;
            TotalPredictedMs = totalMs;
            IsTimed = timed && names.Count > 0;

            float acc = 0f;
            for (int i = 0; i < names.Count; i++)
            {
                float start = total > 0f ? acc / total : 0f;
                acc += weights[i];
                float end = total > 0f ? acc / total : 1f;
                if (i == names.Count - 1) end = 1f;
                _segments.Add(new Segment(names[i], first[i], count[i], weights[i], ms[i], start, end));
            }
        }

        /// <summary>The segment a plan-space fraction falls in. A boundary belongs to the NEXT one.</summary>
        public int IndexAt(float fraction)
        {
            if (_segments.Count == 0) return -1;
            for (int i = 0; i < _segments.Count; i++)
                if (fraction < _segments[i].End) return i;
            return _segments.Count - 1;
        }

        /// <summary>The segment holding step <paramref name="stepIndex"/>, or -1.</summary>
        public int IndexOfStep(int stepIndex)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (stepIndex >= s.FirstStep && stepIndex < s.FirstStep + s.StepCount) return i;
            }
            return -1;
        }
    }
}
