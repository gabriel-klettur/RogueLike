using Valkur.Core.Coordinates;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Procedural generator for a single chunk. Every biome must be
    /// deterministic: <c>(worldSeed, version, chunkCoord)</c> -> the same
    /// <see cref="ChunkData"/> bit-for-bit, every time. Phase 4 (MMO)
    /// relies on this so the server and a client that just connected can
    /// agree on chunk content without sending the entire tilemap over the
    /// wire — only the diff against the procedural baseline travels.
    ///
    /// Bump <see cref="Version"/> when generation rules change; any
    /// persisted <c>ChunkDelta</c> with the previous version becomes
    /// "stale baseline" and gets re-applied via a migration tool offline.
    /// </summary>
    public interface IBiome
    {
        /// <summary>Stable biome identifier, e.g. "forest", "dungeon".
        /// Used as a key in <see cref="ChunkDelta.BiomeId"/> for the
        /// procedural-vs-modified diff system.</summary>
        string Id { get; }

        /// <summary>Generation-rules version. Increment whenever the output
        /// shape changes for a fixed seed/coord pair. Persisted chunk deltas
        /// reference this so a future load can detect that the baseline has
        /// shifted and decide whether to rebake or carry the diff forward.</summary>
        int Version { get; }

        /// <summary>True for biomes that read pre-authored chunk data from
        /// disk instead of generating it. Hand-crafted regions of the
        /// overworld use this so a deterministic-noise check can short-
        /// circuit. Phase 2 keeps every biome procedural; the flag is
        /// reserved for the Phase-1.5 hand-crafted overworld migration.</summary>
        bool IsHandcrafted { get; }

        /// <summary>Generate the chunk at the given coordinate. The
        /// implementation MUST NOT touch <c>UnityEngine.Random</c>,
        /// <c>DateTime.Now</c>, <c>Time.time</c>, or any other ambient
        /// state — only the supplied <see cref="IBiomeContext"/>.</summary>
        ChunkData GenerateChunk(ChunkCoord coord, long worldSeed, IBiomeContext ctx);
    }

    /// <summary>
    /// Ambient context handed to a biome during generation. Encapsulates
    /// every source of "randomness" the biome is allowed to consume so
    /// determinism can be enforced by code review rather than runtime
    /// luck. Future analyzers can flag any biome that bypasses this.
    /// </summary>
    public interface IBiomeContext
    {
        /// <summary>Side length of the chunk being generated. Saved here so
        /// a biome doesn't have to plumb it as a separate argument.</summary>
        int ChunkSize { get; }

        /// <summary>Number of layers the output should populate. Layer 0 =
        /// Ground; the rest depend on the world's tilemap layout.</summary>
        int LayerCount { get; }

        /// <summary>Tile-id table for the active world. Biomes resolve
        /// names like "grass" to their numeric id once and write only the
        /// numbers into the buffer.</summary>
        ITileIdTable Tiles { get; }

        /// <summary>Get a deterministic noise sampler keyed by
        /// <paramref name="channel"/>. Two calls with the same channel
        /// inside the same generation session return the same sequence of
        /// values for the same coordinates. Different channels are
        /// statistically independent so a biome can safely have one
        /// channel for terrain, one for trees, one for monster spawns,
        /// without correlating their patterns.</summary>
        INoiseSampler Noise(string channel);

        /// <summary>Get a deterministic PRNG keyed by
        /// <paramref name="channel"/>. Same isolation guarantees as
        /// <see cref="Noise"/>.</summary>
        System.Random Random(string channel);
    }

    /// <summary>Mapping between tile names (the form everything outside
    /// chunk data uses) and the compact ids stored inside a
    /// <see cref="ChunkData"/> buffer. Implementations are per-world so
    /// two worlds can ship completely different tile sets without id
    /// collisions.</summary>
    public interface ITileIdTable
    {
        /// <summary>Resolve a tile name to its compact id. Returns 0
        /// (empty) when the name is null/unknown — biomes treat that as
        /// "skip this cell".</summary>
        ushort GetId(string tileName);

        /// <summary>Reverse lookup: id back to name. Returns null for the
        /// empty id (0) and for ids the table doesn't know.</summary>
        string GetName(ushort tileId);
    }

    /// <summary>Deterministic 2D noise sampler. <see cref="Sample"/> with
    /// the same coordinates always returns the same value for the lifetime
    /// of the sampler. Implementations may expose 1D / multi-octave
    /// variants in the future; the minimal contract is a single 2D float
    /// in <c>[0, 1]</c>.</summary>
    public interface INoiseSampler
    {
        float Sample(float x, float y);
    }
}
