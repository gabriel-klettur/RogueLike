using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-zone/biome/level music override.
    /// Maps to Python audio.json zones / biomes / levels blocks.
    /// Resolution priority: zone > level > biome > defaults (same as Python).
    /// </summary>
    [Serializable]
    public class MusicScopeOverride
    {
        public enum ScopeType { Zone, Level, Biome }

        [Tooltip("Whether this override is for a zone, level, or biome")]
        public ScopeType scope;

        [Tooltip("Name of the zone/level/biome")]
        public string scopeName;

        [Tooltip("Music track ID to play in this scope")]
        public string trackId;
    }
}
