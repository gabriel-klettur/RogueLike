using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for the per-run record of what the player has broken or worked:
    /// trees felled, seams mined, crates smashed, and how much of each is left.
    ///
    /// <para>WHY THIS IS NOT PART OF THE BUILDINGS FILE. A building's PLACEMENT is authored
    /// world data — it lives in <c>StreamingAssets/Buildings/buildings_instances.json</c>,
    /// the F10 editor owns it, and it is version-controlled. Its WORKED STATE belongs to one
    /// playthrough. Folding the second into the first means one player chopping one tree
    /// edits the file the editor rewrites, so the felling ships to every future player and to
    /// every other save — and it does it through the exact path the
    /// <c>BUILDINGS_SAVE_POSITION_COLLAPSE</c> incident already made fragile.</para>
    ///
    /// <para>Like the other repositories here the contract works on RAW JSON, so
    /// <c>JsonUtility</c> parsing stays in <c>Valkur.Gameplay</c> rather than inverting the
    /// assembly graph.</para>
    /// </summary>
    public interface IWorldDamageRepository
    {
        /// <summary>True iff a damage file exists for the given world.</summary>
        bool Exists(WorldId worldId);

        /// <summary>Read the raw damage JSON. Returns null when the file is missing.</summary>
        string ReadRawJson(WorldId worldId);

        /// <summary>
        /// Persist the raw damage JSON. Implementations write atomically (tmp + replace) so a
        /// crash mid-write cannot truncate the previous content.
        /// </summary>
        void WriteRawJson(WorldId worldId, string json);
    }
}
