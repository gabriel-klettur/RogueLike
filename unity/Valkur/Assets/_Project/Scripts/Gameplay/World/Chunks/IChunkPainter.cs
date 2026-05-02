using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Abstraction over "show this chunk on screen" / "hide this chunk
    /// from screen". Production uses a Tilemap-based painter; tests can
    /// supply an in-memory recorder that just stores which chunks were
    /// painted/cleared. Keeping the streamer behind this interface lets
    /// us validate streaming logic in EditMode without instantiating
    /// real Tilemaps.
    /// </summary>
    public interface IChunkPainter
    {
        /// <summary>Make the chunk visible. Called once per chunk when it
        /// enters the active set.</summary>
        void Show(ChunkData chunk);

        /// <summary>Hide the chunk and release any rendering resources
        /// held for it. Called once per chunk when it leaves the active
        /// set.</summary>
        void Hide(Valkur.Core.Coordinates.ChunkCoord coord);
    }
}
