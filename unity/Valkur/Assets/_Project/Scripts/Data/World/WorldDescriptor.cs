using System;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Data
{
    /// <summary>
    /// Designer-authored manifest of one world (overworld, dungeon dim,
    /// futuristic dim, etc.). Phase 1 of the multi-world refactor consumes
    /// this to drive <c>WorldManager.LoadWorldAsync</c>: every flat-file
    /// repository, every per-world StreamingAssets directory, and every
    /// in-memory <c>IWorldContext</c> keys off <see cref="Id"/>.
    ///
    /// Today the project ships a single descriptor for <c>"base"</c> that
    /// reproduces the single-world behaviour byte-for-byte. Adding another
    /// world is a designer task: create a new <c>WorldDescriptor</c> asset,
    /// drop new JSONs under <c>StreamingAssets/Worlds/&lt;slug&gt;/</c>, and
    /// register a portal that targets it.
    ///
    /// The descriptor itself does NOT load anything — it is a pure data
    /// asset. <c>WorldManager</c> reads it and orchestrates the load.
    /// </summary>
    [CreateAssetMenu(menuName = "Valkur/World/World Descriptor", fileName = "WorldDescriptor", order = 11)]
    public sealed class WorldDescriptor : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable slug identifying this world (e.g. \"base\", \"the_abyss\"). " +
                 "Persistence paths and WorldId.Slug derive from this. Two descriptors must NOT share a slug.")]
        [SerializeField] private string slug = "base";

        [Tooltip("Human-readable name shown in UI / portals.")]
        [SerializeField] private string displayName = "Overworld";

        [Header("Configuration")]
        [Tooltip("Per-world tunables (chunk size, tile size, seed, dimension slug). " +
                 "Required: WorldManager refuses to load a descriptor with a null config.")]
        [SerializeField] private WorldConfig config;

        [Header("Spawn")]
        [Tooltip("Default spawn position (tile coordinates) when this world is loaded fresh — " +
                 "no save data, no portal-supplied target. Phase 1 keeps this as a tile-coord " +
                 "Vector2Int; Phase 2 chunk streaming swaps to long Tx/Ty WorldPos.")]
        [SerializeField] private Vector2Int defaultSpawnTile = new Vector2Int(75, 75);

        [Header("Generation (Phase 2 chunk streaming)")]
        [Tooltip("When true, GameplaySceneSetup wires a ChunkStreamerBehaviour for this " +
                 "world instead of the legacy WorldLoader. The chunks are produced procedurally " +
                 "from the biome below, gated by the player's active radius. Leave false for " +
                 "hand-crafted worlds (the legacy single-world flow stays byte-compatible).")]
        [SerializeField] private bool useChunkStreaming;

        [Tooltip("Chunks within this Chebyshev distance of the player's chunk stay visible. " +
                 "Higher values = wider view, more memory. 2 means a 5x5 = 25 chunks active.")]
        [SerializeField] private int activeRadius = 2;

        [Tooltip("Procedural-generation kind. 'Uniform' paints every cell of layer 0 with a " +
                 "single tile (smoke-test biome). 'NoiseSplit' threshold-splits two tiles via " +
                 "deterministic value noise. None = no procedural generation; the world is " +
                 "expected to be hand-crafted.")]
        [SerializeField] private ProceduralBiomeKind biomeKind = ProceduralBiomeKind.None;

        [Tooltip("Primary tile name used by the biome (the only tile for Uniform; the 'high' " +
                 "tile for NoiseSplit). Must be registered in the world's tile registry.")]
        [SerializeField] private string primaryTile = "grass";

        [Tooltip("Secondary tile name used by NoiseSplit's 'low' band. Ignored by other biomes.")]
        [SerializeField] private string secondaryTile = "dirt";

        [Tooltip("NoiseSplit threshold in [0, 1]. Cells whose noise sample is >= threshold use " +
                 "the primary tile; otherwise the secondary. 0.5 gives a balanced 50/50 mix.")]
        [SerializeField, Range(0f, 1f)] private float noiseThreshold = 0.5f;

        public string Slug                => string.IsNullOrEmpty(slug) ? "base" : slug;
        public string DisplayName         => string.IsNullOrEmpty(displayName) ? Slug : displayName;
        public WorldConfig Config         => config;
        public Vector2Int DefaultSpawnTile => defaultSpawnTile;
        public bool UseChunkStreaming     => useChunkStreaming;
        public int  ActiveRadius          => Mathf.Max(0, activeRadius);
        public ProceduralBiomeKind BiomeKind => biomeKind;
        public string PrimaryTile         => primaryTile ?? string.Empty;
        public string SecondaryTile       => secondaryTile ?? string.Empty;
        public float  NoiseThreshold      => Mathf.Clamp01(noiseThreshold);

        /// <summary>The <see cref="WorldId"/> this descriptor produces. Always
        /// derived from the descriptor's own <see cref="Slug"/> (not from
        /// the wrapped <see cref="WorldConfig"/>) so two descriptors that
        /// happen to share a config still resolve to distinct identities.
        /// Deterministic: same slug -> same Guid across editor sessions and
        /// across machines, which makes save files portable.</summary>
        public WorldId Id => new WorldId(DeterministicGuid(Slug), Slug);

        // Same MD5-based mapping WorldConfig uses; kept private here so the
        // descriptor's identity is a pure function of its slug, immune to
        // config wiring mistakes.
        private static System.Guid DeterministicGuid(string slug)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("valkur:" + slug));
                return new System.Guid(hash);
            }
        }

        /// <summary>
        /// Build a fallback descriptor in code for the legacy single-world
        /// boot path. Used by <c>WorldManager</c> when no descriptor asset
        /// has been wired yet — preserves Phase 0 behaviour.
        /// </summary>
        public static WorldDescriptor CreateLegacyBase()
        {
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            d.slug = "base";
            d.displayName = "Overworld";
            d.config = WorldConfig.CreateLegacyFallback();
            d.defaultSpawnTile = new Vector2Int(75, 75);
            d.name = "WorldDescriptor (legacy base)";
            return d;
        }
    }
}
