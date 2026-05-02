using System.Collections.Generic;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Storage abstraction for player-driven chunk edits (a
    /// <see cref="ChunkDelta"/> per modified chunk). The full chunk's
    /// <see cref="ChunkData"/> baseline is regenerated on demand from the
    /// biome — only the diff against that baseline ever lives on disk
    /// (or, in Phase 4, in the network packet on AOI enter).
    ///
    /// Path layout for the JSON-file backend:
    ///   - WorldId.Base  -> persistentDataPath/Chunks/&lt;cx&gt;_&lt;cy&gt;.delta.json
    ///   - other worlds  -> persistentDataPath/Chunks/&lt;slug&gt;/&lt;cx&gt;_&lt;cy&gt;.delta.json
    ///
    /// Empty deltas are intentionally NOT persisted — a virgin chunk that
    /// the player has never touched costs zero bytes on disk. That keeps
    /// save size proportional to "chunks edited" instead of "chunks
    /// visited", which is the whole point of the diff-on-procedural model.
    /// </summary>
    public interface IChunkDeltaRepository
    {
        bool Exists(WorldId worldId, ChunkCoord coord);

        /// <summary>Read the persisted delta. Returns null when no file
        /// exists for this chunk (treat as empty delta).</summary>
        ChunkDelta Read(WorldId worldId, ChunkCoord coord);

        /// <summary>Persist the delta. Empty deltas are skipped (no file
        /// written) — caller can rely on "Exists -> non-empty diff".</summary>
        void Write(WorldId worldId, ChunkCoord coord, ChunkDelta delta);

        /// <summary>Delete a delta. Returns true iff a file was removed.
        /// No-op (returns false) when nothing was persisted.</summary>
        bool Delete(WorldId worldId, ChunkCoord coord);

        /// <summary>Enumerate the coordinates of every chunk that has a
        /// persisted delta in the given world. Order is not guaranteed.</summary>
        IEnumerable<ChunkCoord> ListEdited(WorldId worldId);
    }
}
