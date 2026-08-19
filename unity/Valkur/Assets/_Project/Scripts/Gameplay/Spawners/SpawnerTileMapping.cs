using UnityEngine;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// The one definition of how a spawner's <c>tile</c> field in
    /// <c>spawners_instances.json</c> relates to its world position.
    ///
    /// The file stores tiles ZONE-RELATIVE with the row axis flipped: column 0 is the zone's
    /// left edge, row 0 is its TOP. World space is absolute and y grows upward. Those are two
    /// different coordinate systems, and for a long time only one side of the round trip knew
    /// it — <c>SpawnerInstanceLoader</c> converted on the way in while the F3 editor wrote
    /// <c>RoundToInt(transform.position)</c> straight out.
    ///
    /// The result was a save that looked like it worked: the file grew, the entries were
    /// well-formed, and every reload moved every spawner by the zone's origin. Lobby sits at
    /// (150, 50), so a spawner drifted 150 tiles right per restart until it was off the map
    /// entirely — reported as "I place spawners and after a restart they are gone".
    ///
    /// Both directions live here so they cannot disagree again. Pure and static, so the round
    /// trip is provable without a scene.
    /// </summary>
    public static class SpawnerTileMapping
    {
        /// <summary>Zone-relative tile (row 0 = top) to absolute world position.</summary>
        public static Vector2 TileToWorld(int tileCol, int tileRow, Vector2 gridOffset, int zoneHeightTiles)
        {
            return new Vector2(
                gridOffset.x + tileCol,
                gridOffset.y + (zoneHeightTiles - 1) - tileRow);
        }

        /// <summary>
        /// Absolute world position to zone-relative tile (row 0 = top).
        ///
        /// Exactly the inverse of <see cref="TileToWorld"/>. Rounding happens here rather than
        /// at the callsite so both ends agree on which tile a position between two tiles
        /// belongs to.
        /// </summary>
        public static Vector2Int WorldToTile(Vector2 world, Vector2 gridOffset, int zoneHeightTiles)
        {
            return new Vector2Int(
                Mathf.RoundToInt(world.x - gridOffset.x),
                Mathf.RoundToInt(gridOffset.y + (zoneHeightTiles - 1) - world.y));
        }

        /// <summary>
        /// Whether a tile is inside the zone it claims to belong to.
        ///
        /// A file written in the wrong space produces coordinates far outside these bounds —
        /// a Lobby tile of 262 against a 50-tile-tall zone — so this is what distinguishes
        /// authored data from data that has already drifted.
        /// </summary>
        public static bool IsInsideZone(int tileCol, int tileRow, int zoneWidthTiles, int zoneHeightTiles)
        {
            return tileCol >= 0 && tileCol < zoneWidthTiles
                && tileRow >= 0 && tileRow < zoneHeightTiles;
        }
    }
}
