using System.Collections.Generic;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// Builds the boot sequence one ETAPA at a time.
    ///
    /// <para>A phase is declared once with <see cref="Phase"/> and every step added after it
    /// belongs to it, so the sequence reads the way it is grouped and no step can be left out
    /// of a phase by forgetting an argument. The loading bar is divided by these phases and the
    /// boot log sums its timings by them — so a phase name is a key in a CSV that outlives this
    /// build, and renaming one starts a new series in the trend report.</para>
    ///
    /// <para><b>A phase must be one contiguous run.</b> Declaring the same name twice with other
    /// steps in between would draw two segments with one name and split one series into two
    /// halves; <c>BootSequenceTests</c> refuses it.</para>
    /// </summary>
    public sealed class BootSequenceBuilder
    {
        private readonly List<BootStep> _steps;
        private string _phase = BootPlan.DefaultPhase;

        public BootSequenceBuilder(int capacity = 80)
        {
            _steps = new List<BootStep>(capacity > 0 ? capacity : 16);
        }

        /// <summary>Every step added from now on belongs to <paramref name="name"/>.</summary>
        public void Phase(string name)
            => _phase = string.IsNullOrWhiteSpace(name) ? BootPlan.DefaultPhase : name;

        public void Add(BootStep step)
        {
            if (step == null) return;
            step.AssignPhase(_phase);
            _steps.Add(step);
        }

        public int Count => _steps.Count;

        public List<BootStep> Steps => _steps;
    }
}
