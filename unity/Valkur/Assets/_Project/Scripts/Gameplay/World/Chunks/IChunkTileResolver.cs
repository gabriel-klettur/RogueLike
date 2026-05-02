using UnityEngine.Tilemaps;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Resolves a numeric tile id (the form stored inside <see cref="ChunkData"/>)
    /// to a real <see cref="TileBase"/> that <see cref="Tilemap"/> can paint.
    /// Phase 2.5 plumbing: chunks travel through Sim/Net as ushort buffers,
    /// presentation translates them at the very last moment.
    ///
    /// Two indirections happen behind this interface:
    ///   id  →  string name      (per-world <see cref="ITileIdTable"/>)
    ///   name → TileBase asset   (Resources / Addressables / Tile registry)
    ///
    /// Keeping both behind a single resolver lets the painter remain
    /// agnostic to where tile assets live; Phase 3 modding can swap the
    /// asset side to Addressables without touching painter or streamer.
    /// </summary>
    public interface IChunkTileResolver
    {
        /// <summary>Look up the asset for the given numeric id. Returns null
        /// for the empty id (0) and for any id whose name has no asset
        /// registered — caller treats null as "leave the cell empty".</summary>
        TileBase Resolve(ushort tileId);
    }
}
