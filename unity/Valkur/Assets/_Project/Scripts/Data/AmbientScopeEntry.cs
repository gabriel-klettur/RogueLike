using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Ambient sound scope: defines which ambient sounds to play
    /// and at what interval for a given biome/zone.
    /// Maps to Python audio.json biomes/zones ambient blocks.
    /// </summary>
    [Serializable]
    public class AmbientScopeEntry
    {
        [Tooltip("Scope identifier (biome or zone name)")]
        public string scopeName;

        [Tooltip("SFX IDs to randomly choose from")]
        public string[] choices;

        [Tooltip("Minimum seconds between ambient sounds (Python default 6.0)")]
        public float minInterval = 6f;

        [Tooltip("Maximum seconds between ambient sounds (Python default 18.0)")]
        public float maxInterval = 18f;
    }
}
