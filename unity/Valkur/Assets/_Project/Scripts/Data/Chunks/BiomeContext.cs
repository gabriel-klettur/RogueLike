using System.Collections.Generic;

namespace Valkur.Data.Chunks
{
    /// <summary>
    /// Default <see cref="IBiomeContext"/> implementation. Mixes the world
    /// seed, the channel name, and the chunk coordinate into independent
    /// streams so two channels in the same biome do not correlate and two
    /// chunks at different coordinates produce independent results.
    ///
    /// All randomness goes through this type so a future static-analysis
    /// pass can flag any biome that calls <c>UnityEngine.Random</c>,
    /// <c>DateTime.Now</c>, or <c>System.Random()</c> directly — those
    /// would break determinism the moment Phase 4 networking lands.
    /// </summary>
    public sealed class BiomeContext : IBiomeContext
    {
        private readonly long _worldSeed;
        private readonly int  _cx;
        private readonly int  _cy;
        private readonly Dictionary<string, INoiseSampler> _noiseCache = new Dictionary<string, INoiseSampler>();
        private readonly Dictionary<string, System.Random> _rngCache   = new Dictionary<string, System.Random>();

        public int ChunkSize  { get; }
        public int LayerCount { get; }
        public ITileIdTable Tiles { get; }

        public BiomeContext(long worldSeed, Valkur.Core.Coordinates.ChunkCoord coord,
                            int chunkSize, int layerCount, ITileIdTable tiles)
        {
            _worldSeed = worldSeed;
            _cx = coord.Cx;
            _cy = coord.Cy;
            ChunkSize  = chunkSize;
            LayerCount = layerCount;
            Tiles = tiles ?? new EmptyTileIdTable();
        }

        public INoiseSampler Noise(string channel)
        {
            if (string.IsNullOrEmpty(channel)) channel = string.Empty;
            if (_noiseCache.TryGetValue(channel, out var s)) return s;
            int seed = MixSeed(channel);
            s = new ValueNoise2D(seed);
            _noiseCache[channel] = s;
            return s;
        }

        public System.Random Random(string channel)
        {
            if (string.IsNullOrEmpty(channel)) channel = string.Empty;
            if (_rngCache.TryGetValue(channel, out var r)) return r;
            int seed = MixSeed(channel);
            r = new System.Random(seed);
            _rngCache[channel] = r;
            return r;
        }

        // FNV-1a hash mixed with the world seed and chunk coordinates so the
        // resulting per-channel seed is unique to (worldSeed, coord, channel)
        // but still cheap to derive. Two channels at the same coord get
        // different streams; the same channel at different coords also gets
        // different streams — both are required for visual variety.
        private int MixSeed(string channel)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL; // FNV offset basis
                for (int i = 0; i < channel.Length; i++)
                {
                    h ^= channel[i];
                    h *= 1099511628211UL;
                }
                h ^= (ulong)_worldSeed;
                h *= 1099511628211UL;
                h ^= (ulong)((long)_cx << 32 | (uint)_cy);
                h *= 1099511628211UL;
                return (int)(h ^ (h >> 32));
            }
        }
    }

    /// <summary>Stateless tile table that returns 0 for every name. Used
    /// as a fallback when a context is constructed without a real table —
    /// keeps tests that don't care about tile resolution succinct.</summary>
    public sealed class EmptyTileIdTable : ITileIdTable
    {
        public ushort GetId(string tileName)   => 0;
        public string GetName(ushort tileId)   => null;
    }
}
