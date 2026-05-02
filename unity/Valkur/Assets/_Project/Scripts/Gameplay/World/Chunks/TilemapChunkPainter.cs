using System.Collections.Generic;
using UnityEngine.Tilemaps;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Production <see cref="IChunkPainter"/> backed by Unity Tilemaps.
    /// Holds one <see cref="Tilemap"/> per chunk-data layer (Ground,
    /// Walls, Decoration, …); Show/Hide blits the chunk's
    /// <see cref="ChunkData"/> into / out of the matching tilemaps via
    /// <see cref="ChunkTilemapPainter"/>.
    ///
    /// Stateful: the painter remembers which chunks are currently
    /// visible so a Hide call knows what region to clear without the
    /// caller having to thread the chunk size through.
    /// </summary>
    public sealed class TilemapChunkPainter : IChunkPainter
    {
        private readonly Tilemap[] _layerTilemaps;
        private readonly IChunkTileResolver _resolver;
        private readonly int _chunkSize;
        private readonly Dictionary<ChunkCoord, int> _visible = new Dictionary<ChunkCoord, int>();

        public TilemapChunkPainter(Tilemap[] layerTilemaps,
                                   IChunkTileResolver resolver,
                                   int chunkSize)
        {
            _layerTilemaps = layerTilemaps ?? new Tilemap[0];
            _resolver      = resolver;
            _chunkSize     = chunkSize > 0 ? chunkSize : ChunkData.DefaultChunkSize;
        }

        public IReadOnlyDictionary<ChunkCoord, int> Visible => _visible;

        public void Show(ChunkData chunk)
        {
            if (chunk == null) return;
            ChunkTilemapPainter.Paint(chunk, _layerTilemaps, _resolver);
            _visible[chunk.Coord] = chunk.Size;
        }

        public void Hide(ChunkCoord coord)
        {
            // Use the stored size if we know it (matches what was painted);
            // fall back to the configured _chunkSize for chunks the painter
            // never showed but the streamer asks to clear (defensive).
            int size = _visible.TryGetValue(coord, out var s) ? s : _chunkSize;
            ChunkTilemapPainter.Clear(coord, size, _layerTilemaps);
            _visible.Remove(coord);
        }
    }
}
