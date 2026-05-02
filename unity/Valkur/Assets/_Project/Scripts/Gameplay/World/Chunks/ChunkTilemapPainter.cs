using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Pure utility that paints a single chunk's <see cref="ChunkData"/>
    /// into one or more <see cref="Tilemap"/>s. Stateless and callable
    /// from EditMode tests — no MonoBehaviour, no scene assumptions, no
    /// hidden global state.
    ///
    /// Coordinate convention:
    ///   Chunk-local cells (x, y) in [0, Size) translate to world tile
    ///   coordinates (Cx*Size + x, Cy*Size + y). Layer 0 in the chunk maps
    ///   to the first tilemap supplied; layer N to the (N)-th.
    ///
    /// <see cref="Paint"/> uses <see cref="Tilemap.SetTilesBlock"/> for the
    /// whole chunk in a single call — orders of magnitude faster than
    /// per-cell SetTile when the chunk side is 32+. <see cref="Clear"/>
    /// is the inverse: blank out the chunk-shaped region so a streamer
    /// can drop a chunk without leaving phantom tiles behind.
    /// </summary>
    public static class ChunkTilemapPainter
    {
        /// <summary>Paint a chunk into the provided per-layer tilemaps.
        /// Tilemap entries that are null are silently skipped — the
        /// painter does not require every chunk layer to have a target.</summary>
        public static void Paint(ChunkData data, Tilemap[] layerTilemaps, IChunkTileResolver resolver)
        {
            if (data == null || layerTilemaps == null || resolver == null) return;
            int size = data.Size;
            int layers = Mathf.Min(data.Layers.Length, layerTilemaps.Length);
            int worldOriginX = data.Coord.Cx * size;
            int worldOriginY = data.Coord.Cy * size;

            for (int l = 0; l < layers; l++)
            {
                var tilemap = layerTilemaps[l];
                if (tilemap == null) continue;

                var bounds = new BoundsInt(worldOriginX, worldOriginY, 0, size, size, 1);
                var buffer = new TileBase[size * size];
                var src = data.Layers[l];
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = resolver.Resolve(src[i]);

                tilemap.SetTilesBlock(bounds, buffer);
            }
        }

        /// <summary>Clear every cell inside the chunk-shaped region on
        /// every supplied tilemap. Used by the streamer when a chunk
        /// leaves the active set.</summary>
        public static void Clear(Valkur.Core.Coordinates.ChunkCoord coord, int size, Tilemap[] layerTilemaps)
        {
            if (layerTilemaps == null || size <= 0) return;
            int worldOriginX = coord.Cx * size;
            int worldOriginY = coord.Cy * size;
            var bounds = new BoundsInt(worldOriginX, worldOriginY, 0, size, size, 1);
            var emptyBuffer = new TileBase[size * size];

            for (int l = 0; l < layerTilemaps.Length; l++)
            {
                var tilemap = layerTilemaps[l];
                if (tilemap == null) continue;
                tilemap.SetTilesBlock(bounds, emptyBuffer);
            }
        }
    }
}
