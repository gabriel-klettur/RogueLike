using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Runtime diagnostics for tilemap collision setup.
    /// Logs, for every CompositeCollider2D in the scene, the number of painted
    /// cells on its Tilemap and the resulting <c>pathCount</c> after baking.
    ///
    /// A pathCount of 0 with a non-zero tile count means the composite was never
    /// regenerated; a pathCount of 0 with a zero tile count means the layer was
    /// never authored. Both surface as a warning so missing collision data is
    /// visible the moment the player can't be blocked by a wall.
    /// </summary>
    public static class TileCollisionDiagnostics
    {
        /// <summary>
        /// Scan every CompositeCollider2D in the scene and report tile + pathCount.
        /// Call from <see cref="Bootstrap.GameplaySceneSetup"/> right after
        /// RebakeTilemapColliders so the report reflects the final baked state.
        /// </summary>
        public static void Report()
        {
            var composites = Object.FindObjectsOfType<CompositeCollider2D>();
            int total = composites.Length;
            int empty = 0;
            int healthy = 0;
            int unbaked = 0;

            for (int i = 0; i < total; i++)
            {
                var cc = composites[i];
                var tm = cc.GetComponent<Tilemap>();
                int tiles = tm != null ? CountTiles(tm) : -1;
                int paths = cc.pathCount;

                string layerName = tm != null
                    ? cc.gameObject.name
                    : cc.gameObject.name + " (no Tilemap)";

                if (tiles == 0 && paths == 0)
                {
                    empty++;
                    Debug.Log($"[TileCollisionDiagnostics] {layerName}: 0 tiles → no collision (empty layer).");
                }
                else if (tiles > 0 && paths == 0)
                {
                    unbaked++;
                    Debug.LogWarning(
                        $"[TileCollisionDiagnostics] {layerName}: {tiles} tiles but pathCount=0 — " +
                        "CompositeCollider2D was not baked. Player will pass through these tiles. " +
                        "Call CompositeCollider2D.GenerateGeometry() after painting.");
                }
                else
                {
                    healthy++;
                    Debug.Log($"[TileCollisionDiagnostics] {layerName}: {tiles} tiles, pathCount={paths}.");
                }
            }

            Debug.Log($"[TileCollisionDiagnostics] {total} composite collider(s): " +
                      $"{healthy} healthy, {empty} empty, {unbaked} UNBAKED.");
        }

        private static int CountTiles(Tilemap tm)
        {
            var bounds = tm.cellBounds;
            if (bounds.size.x <= 0 || bounds.size.y <= 0) return 0;
            int count = 0;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    if (tm.HasTile(new Vector3Int(x, y, 0))) count++;
                }
            }
            return count;
        }
    }
}
