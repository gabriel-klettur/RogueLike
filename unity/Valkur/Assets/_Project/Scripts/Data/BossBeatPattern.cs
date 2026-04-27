using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Valkur.Data
{
    /// <summary>
    /// Beat-synced choreography pattern for a boss.
    /// Each cue triggers when the music beat clock crosses the configured
    /// (bar, beat) inside a repeating window. The cue carries a string action
    /// tag that the <c>BossBeatChoreographer</c> dispatches via UnityEvent.
    ///
    /// Patterns are independent of any specific song — only the BPM-aligned
    /// "bar / beat-in-bar" matter. Assign the same SO to multiple bosses or
    /// vary patterns per phase.
    /// </summary>
    [CreateAssetMenu(fileName = "BossBeatPattern", menuName = "Valkur/Audio/Boss Beat Pattern")]
    public class BossBeatPattern : ScriptableObject
    {
        [Serializable]
        public class Cue
        {
            [Tooltip("Bar index inside the looped window (0-based). Must be < BarsPerLoop.")]
            [Min(0)] public int bar = 0;

            [Tooltip("Beat-in-bar (0-based). Must be < beats-per-bar of the active track.")]
            [Min(0)] public int beat = 0;

            [Tooltip("Free-form action tag (e.g. 'attack', 'telegraph', 'dash'). Dispatched to OnCue.")]
            public string action = "attack";

            [Tooltip("Optional numeric payload (damage scale, range, etc.).")]
            public float payload = 0f;

            [Tooltip("Optional cosmetic label for the editor.")]
            public string description;
        }

        [Tooltip("Loop length in bars. Cues whose bar >= this value are ignored.")]
        [Min(1)] public int barsPerLoop = 4;

        [Tooltip("List of cues fired each loop.")]
        public List<Cue> cues = new List<Cue>();
    }
}
