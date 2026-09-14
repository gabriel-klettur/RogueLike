using System;

namespace Valkur.Core.Boot
{
    /// <summary>One etapa of a <see cref="BootRunRecord"/>.</summary>
    [Serializable]
    public sealed class BootPhaseRecord
    {
        public string name;
        public int steps;
        public float predictedMs;
        public float actualMs;
    }
}
