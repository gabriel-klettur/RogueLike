namespace Valkur.Data
{
    /// <summary>
    /// Phase-2 procedural-biome selector consumed by
    /// <see cref="WorldDescriptor"/>. Kept in <c>Valkur.Data</c> so the
    /// descriptor (also Data) doesn't have to reach into Gameplay to
    /// describe the world's generation rules.
    ///
    /// New procedural biomes register a value here and an instantiator
    /// in the bootstrap step that builds <c>IBiome</c> from a
    /// descriptor; the enum keeps inspector wiring trivial for designers.
    /// </summary>
    public enum ProceduralBiomeKind
    {
        /// <summary>No procedural generation — world is hand-crafted.</summary>
        None = 0,
        /// <summary>Every cell of layer 0 painted with the primary tile.</summary>
        Uniform = 1,
        /// <summary>Two tiles split by a deterministic noise threshold.</summary>
        NoiseSplit = 2,
    }
}
