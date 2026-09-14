using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Turns river centre lines into the set of tiles that are water, at the profile's width.
    /// The ONE place width is applied, so the preview and the build cannot disagree about how
    /// wide a river is.
    /// </summary>
    public static class WorldRiverRaster
    {
        public static HashSet<Vector2Int> Tiles(IReadOnlyList<WorldRiver> rivers, int width, int mapWidth, int mapHeight)
        {
            var set = new HashSet<Vector2Int>();
            if (rivers == null) return set;

            // Width w covers w tiles centred on the line: 1 -> [0], 2 -> [0,1], 3 -> [-1,0,1].
            int lo = -(Mathf.Max(1, width) - 1) / 2;
            int hi = lo + Mathf.Max(1, width) - 1;

            for (int r = 0; r < rivers.Count; r++)
            {
                var tiles = rivers[r].Tiles;
                for (int i = 0; i < tiles.Count; i++)
                    for (int dy = lo; dy <= hi; dy++)
                        for (int dx = lo; dx <= hi; dx++)
                        {
                            int x = tiles[i].x + dx, y = tiles[i].y + dy;
                            if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight) continue;
                            set.Add(new Vector2Int(x, y));
                        }
            }
            return set;
        }
    }
}
