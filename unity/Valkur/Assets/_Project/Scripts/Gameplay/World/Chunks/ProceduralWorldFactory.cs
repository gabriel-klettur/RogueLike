using System;
using Valkur.Data;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Phase 2.6 wiring helper: converts a designer-authored
    /// <see cref="WorldDescriptor"/> (which holds a <see cref="ProceduralBiomeKind"/>
    /// and a couple of tile names) into a ready-to-stream
    /// <see cref="IChunkProvider"/>. Hides the bookkeeping that
    /// every chunk-streamed world needs:
    ///
    ///   - Registers the descriptor's tile names in a fresh
    ///     <see cref="DictionaryTileIdTable"/> so the biome can
    ///     resolve them to numeric ids.
    ///   - Instantiates the matching <see cref="IBiome"/> for the
    ///     descriptor's <see cref="ProceduralBiomeKind"/>.
    ///   - Wraps the biome in a <see cref="SingleBiomeRouter"/> (Phase 2
    ///     ships only single-biome worlds; Phase 3 swaps the router).
    ///   - Pulls the seed and chunk-size from the descriptor's
    ///     <see cref="WorldConfig"/>.
    ///
    /// The returned tile table is exposed alongside the provider because
    /// the renderer needs the same table to translate numeric ids back
    /// to names when painting.
    /// </summary>
    public static class ProceduralWorldFactory
    {
        public readonly struct ProceduralWorld
        {
            public readonly IChunkProvider Provider;
            public readonly DictionaryTileIdTable Tiles;
            public readonly int ChunkSize;
            public readonly int LayerCount;

            public ProceduralWorld(IChunkProvider p, DictionaryTileIdTable t, int chunkSize, int layerCount)
            {
                Provider = p; Tiles = t; ChunkSize = chunkSize; LayerCount = layerCount;
            }
        }

        public const int DefaultLayerCount = 1;

        public static ProceduralWorld Build(WorldDescriptor descriptor,
                                            IChunkDeltaSource deltaSource = null,
                                            int layerCount = DefaultLayerCount)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.Config == null)
                throw new InvalidOperationException(
                    $"WorldDescriptor '{descriptor.name}' has no WorldConfig.");
            if (descriptor.BiomeKind == ProceduralBiomeKind.None)
                throw new InvalidOperationException(
                    $"WorldDescriptor '{descriptor.name}' has BiomeKind=None — " +
                    "ProceduralWorldFactory only knows how to build streamed worlds.");

            var tiles = new DictionaryTileIdTable();
            // Register both tile names eagerly so even an under-used
            // biome resolves cleanly (e.g. Uniform with the primary
            // name, NoiseSplit with both).
            if (!string.IsNullOrEmpty(descriptor.PrimaryTile))   tiles.Register(descriptor.PrimaryTile);
            if (!string.IsNullOrEmpty(descriptor.SecondaryTile)) tiles.Register(descriptor.SecondaryTile);

            IBiome biome = BuildBiome(descriptor);
            var router = new SingleBiomeRouter(biome);

            int chunkSize = descriptor.Config.ChunkSize;
            long seed     = descriptor.Config.Seed;

            IChunkProvider provider = new DiffOverlayChunkProvider(
                router,
                deltaSource ?? new EmptyDeltaSource(),
                worldSeed: seed,
                chunkSize: chunkSize,
                layerCount: layerCount,
                tiles: tiles);

            return new ProceduralWorld(provider, tiles, chunkSize, layerCount);
        }

        private static IBiome BuildBiome(WorldDescriptor d)
        {
            switch (d.BiomeKind)
            {
                case ProceduralBiomeKind.Uniform:
                    return new UniformFillBiome(
                        id: d.Slug + ".uniform",
                        tileName: d.PrimaryTile);

                case ProceduralBiomeKind.NoiseSplit:
                    return new NoiseSplitBiome(
                        id: d.Slug + ".noise_split",
                        highTile: d.PrimaryTile,
                        lowTile:  d.SecondaryTile,
                        threshold: d.NoiseThreshold);

                case ProceduralBiomeKind.Checkerboard:
                    return new CheckerboardBiome(
                        id: d.Slug + ".checkerboard",
                        primaryTile:   d.PrimaryTile,
                        secondaryTile: d.SecondaryTile);

                case ProceduralBiomeKind.Ring:
                    // No dedicated ring-width field on WorldDescriptor (yet);
                    // the biome's own default is enough for the demo asset.
                    // Designers who want custom ring widths construct the
                    // biome directly rather than going through the descriptor.
                    return new RingBiome(
                        id: d.Slug + ".ring",
                        primaryTile:   d.PrimaryTile,
                        secondaryTile: d.SecondaryTile);

                default:
                    throw new InvalidOperationException(
                        $"ProceduralWorldFactory: unsupported BiomeKind '{d.BiomeKind}'. " +
                        "Add the case here when introducing a new biome.");
            }
        }
    }
}
