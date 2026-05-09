using UnityEngine;

namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Pure helpers for converting <see cref="DungeonGenerator"/> output (Y-down)
    /// into world-tile coordinates (Y-up). Extracted so it can be unit-tested
    /// without instantiating <see cref="DungeonLoader"/> or any Unity scene.
    /// </summary>
    public static class BspDungeonResultConverter
    {
        /// <summary>
        /// Convert a generator-space (Y-down) room rectangle to world tile coords (Y-up).
        /// </summary>
        public static RectInt ToWorldRect(RectInt genRect, int genHeight, int offX, int offY)
        {
            int worldXMin = offX + genRect.xMin;
            // Y-flip: row 0 in generation is top → highest world Y is offY + (height - 1).
            int worldYMax = offY + (genHeight - 1 - genRect.yMin);
            int worldYMin = offY + (genHeight - 1 - (genRect.yMax - 1));

            return new RectInt(
                worldXMin,
                worldYMin,
                genRect.width,
                worldYMax - worldYMin + 1);
        }
    }
}
