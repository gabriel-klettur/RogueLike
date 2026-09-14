using System;
using System.Collections.Generic;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// One boot, as written to disk by <see cref="BootRunLog"/>. Plain serializable data so
    /// <c>JsonUtility</c> can write the history line and a test can build one by hand.
    ///
    /// <para><b>Every duration carries its PREDICTION beside it.</b> A timing alone answers
    /// "how long"; a timing next to what the previous boots predicted answers "did this get
    /// slower", which is the question a log is kept for.</para>
    /// </summary>
    [Serializable]
    public sealed class BootRunRecord
    {
        public int schema = 1;
        public string runId;
        public string utc;
        public string unity;
        public string platform;
        public string device;
        public string cpu;
        public int cpuCores;
        public int ramMb;
        public string gpu;
        public bool editor;
        public bool calibrated;

        public int steps;
        public int frames;
        public int failures;

        /// <summary>LoadSceneAsync until activation was permitted. -1 when no screen measured it.</summary>
        public float sceneLoadMs = -1f;
        /// <summary>Activation permitted until the boot sequence began. -1 when unmeasured.</summary>
        public float activationMs = -1f;
        /// <summary>First step to ready, wall clock.</summary>
        public float bootWallMs;
        public float insideStepsMs;
        public float outsideStepsMs;
        public float totalMs;
        public float predictedTotalMs;

        public List<BootPhaseRecord> phases = new List<BootPhaseRecord>();
        public List<BootStepRecord> stepRows = new List<BootStepRecord>();
        public List<string> failureMessages = new List<string>();
    }
}
