using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// One town's layout: where it is, its plaza and its streets, in world TILES.
    ///
    /// <para>What is NOT here is which building stands where. Streets depend only on the seed and
    /// the ground, so the Seed World preview can draw them; lots depend on the sizes of the
    /// building templates in the catalogue, which is Gameplay data, so they are placed at build
    /// time by <see cref="WorldTownLots"/> against this same layout.</para>
    /// </summary>
    public sealed class WorldTown
    {
        public readonly int Index;
        public readonly Vector2Int Center;
        public readonly int Radius;
        public readonly bool IsStart;
        public readonly RectInt Plaza;

        /// <summary>Street segments as rectangles, main arms first. Clipped tiles are absent from <see cref="StreetTiles"/>.</summary>
        public readonly List<RectInt> Streets = new List<RectInt>();

        /// <summary>Every tile that is street or plaza after clipping to walkable ground.</summary>
        public readonly HashSet<Vector2Int> StreetTiles = new HashSet<Vector2Int>();

        public WorldTown(int index, Vector2Int center, int radius, bool isStart, RectInt plaza)
        {
            Index = index;
            Center = center;
            Radius = radius;
            IsStart = isStart;
            Plaza = plaza;
        }

        public bool Contains(Vector2Int tile, int margin = 0)
            => (tile - Center).sqrMagnitude <= (Radius + margin) * (Radius + margin);
    }
}
