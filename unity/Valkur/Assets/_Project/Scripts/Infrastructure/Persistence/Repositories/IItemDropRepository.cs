using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for the per-world item drops file.
    ///
    /// Two stores share this contract under different paths:
    ///   • Authoring drops — placed via the Items Editor (F7), shipped with
    ///     the world content under <c>StreamingAssets/Items/item_drops.json</c>.
    ///     Survive across runs, version-controlled.
    ///   • Run drops (Phase B) — gameplay drops (loot, player throws) that
    ///     belong to a single playthrough; written into the per-run save folder.
    ///
    /// As with the buildings repo, the contract works on <b>raw JSON</b> so
    /// <c>JsonUtility</c> parsing stays in <c>Valkur.Gameplay</c> without
    /// inverting the assembly graph.
    ///
    /// Every method takes a <see cref="WorldId"/>, even though the project is
    /// single-world today, so multi-world routing is a free upgrade later.
    /// </summary>
    public interface IItemDropRepository
    {
        /// <summary>True iff a drops file exists for the given world.</summary>
        bool Exists(WorldId worldId);

        /// <summary>Read the raw drops JSON. Returns null when the file is missing.</summary>
        string ReadRawJson(WorldId worldId);

        /// <summary>Persist the raw drops JSON. Implementations write atomically
        /// (tmp + replace) so a crash mid-write cannot truncate the previous content.</summary>
        void WriteRawJson(WorldId worldId, string json);
    }
}
