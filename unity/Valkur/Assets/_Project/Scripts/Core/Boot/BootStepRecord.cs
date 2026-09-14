using System;

namespace Valkur.Core.Boot
{
    /// <summary>One step (or sub-stage) of a <see cref="BootRunRecord"/>.</summary>
    [Serializable]
    public sealed class BootStepRecord
    {
        public int index;
        public string phase;
        public string label;
        public bool substage;
        public bool failed;
        public int frames;
        public float predictedMs;
        /// <summary>Time inside the step's own body.</summary>
        public float cpuMs;
        /// <summary>From this step's start to the next step's start: body plus the frames it yielded.</summary>
        public float wallMs;
    }
}
