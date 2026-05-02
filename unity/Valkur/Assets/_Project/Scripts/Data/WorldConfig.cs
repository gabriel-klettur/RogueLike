using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Data
{
    /// <summary>
    /// Per-world tunable parameters: chunk side, tile size, dimension id, seed.
    /// Lives as a <see cref="ScriptableObject"/> so designers can author one
    /// asset per world (overworld, dungeon dim, futuristic dim, …) without
    /// touching code.
    ///
    /// Phase 0 introduces this type to begin removing the hardcoded "50" tile
    /// constant scattered across the loaders. The default asset wires
    /// <see cref="chunkSize"/> to 50 to match the legacy zone size; Phase 2
    /// will lower the canonical default to 32 and slice legacy zones.
    ///
    /// Read this asset at <c>Awake</c> and cache the resulting values; never
    /// resolve per-frame.
    /// </summary>
    [CreateAssetMenu(menuName = "Valkur/World/World Config", fileName = "WorldConfig", order = 10)]
    public sealed class WorldConfig : ScriptableObject
    {
        /// <summary>
        /// The historical zone side length used by every loader before
        /// <see cref="WorldConfig"/> existed. Loaders that have not yet been
        /// taught about an injected <see cref="WorldConfig"/> reference this
        /// constant explicitly instead of carrying a magic <c>50</c> literal,
        /// so the migration to runtime-tunable chunk size is grep-able.
        /// </summary>
        public const int LegacyChunkSize = 50;

        [Header("Identity")]
        [Tooltip("Stable slug identifying this world (e.g. \"base\", \"the_abyss\"). " +
                 "Persistence paths key off this string. Two configs must not share a slug.")]
        [SerializeField] private string dimensionSlug = "base";

        [Header("Chunk geometry")]
        [Tooltip("Chunk side length in tiles. Phase 0 keeps the legacy 50; Phase 2 will lower this to 32. " +
                 "Loaders read this instead of hardcoding the value.")]
        [SerializeField] private int chunkSize = 50;

        [Tooltip("Unity world units per tile. Keep at 1 for the canonical Valkur grid; only diverge if " +
                 "you genuinely want a different tilemap scale for this dimension.")]
        [SerializeField] private float tileSize = 1f;

        [Header("Generation")]
        [Tooltip("Deterministic seed used by procedural biome generators. -1 = random per-run.")]
        [SerializeField] private long seed = -1L;

        public string DimensionSlug => string.IsNullOrEmpty(dimensionSlug) ? "base" : dimensionSlug;
        public int ChunkSize         => Mathf.Max(1, chunkSize);
        public float TileSize        => Mathf.Max(0.0001f, tileSize);
        public long Seed             => seed;

        /// <summary>The <see cref="WorldId"/> backing this config. Slug is the asset's slug; GUID
        /// is derived deterministically from the slug so the same slug always produces the same id
        /// across editor sessions and across machines (no hidden GUID rolling).</summary>
        public WorldId Id => new WorldId(DeterministicGuid(DimensionSlug), DimensionSlug);

        // Stable, deterministic mapping slug → Guid via MD5 (NOT for crypto — for identity).
        // Same slug on two machines yields the same Guid, so save files port between machines.
        private static System.Guid DeterministicGuid(string slug)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("valkur:" + slug));
                return new System.Guid(hash);
            }
        }

        /// <summary>
        /// Hardcoded fallback used during the transition while not every code path has been
        /// taught about <see cref="WorldConfig"/>. Returns chunkSize = 50, tileSize = 1f.
        /// REMOVE THIS HELPER once all loaders inject their config explicitly (mid Phase 1).
        /// </summary>
        public static WorldConfig CreateLegacyFallback()
        {
            var cfg = ScriptableObject.CreateInstance<WorldConfig>();
            cfg.dimensionSlug = "base";
            cfg.chunkSize = 50;
            cfg.tileSize = 1f;
            cfg.seed = -1L;
            cfg.name = "WorldConfig (legacy fallback)";
            return cfg;
        }
    }
}
