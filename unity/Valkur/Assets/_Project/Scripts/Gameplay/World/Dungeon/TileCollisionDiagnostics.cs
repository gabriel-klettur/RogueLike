using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;

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
    ///
    /// One exception is NOT a fault: the visual <c>Collision</c> tilemap keeps its
    /// painted cells as the authoring source of truth, but
    /// <see cref="Layering.WorldCollisionBaker.Initialize"/> DISABLES its
    /// <see cref="TilemapCollider2D"/> and redistributes every cell to the ten
    /// <c>CollisionPhysics_*</c> sub-tilemaps that own physics. A disabled (or
    /// absent) TilemapCollider2D therefore means "delegated", not "unbaked" —
    /// warning on it reported a wall-less world on every single boot while the
    /// player was in fact blocked correctly by the sub-tilemaps.
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
            int delegated = 0;

            for (int i = 0; i < total; i++)
            {
                var cc = composites[i];
                var tm = cc.GetComponent<Tilemap>();
                int tiles = tm != null ? CountTiles(tm) : -1;
                int paths = cc.pathCount;

                string layerName = tm != null
                    ? cc.gameObject.name
                    : cc.gameObject.name + " (no Tilemap)";

                // A composite whose TilemapCollider2D is disabled feeds nothing into
                // the composite by design — WorldCollisionBaker owns those cells now.
                var tmCollider = cc.GetComponent<TilemapCollider2D>();
                if (tiles > 0 && paths == 0 && (tmCollider == null || !tmCollider.enabled))
                {
                    delegated++;
                    VerboseLog.Log(VerboseLog.Category.Collision,
                        () => $"[TileCollisionDiagnostics] {layerName}: {tiles} tiles, collision " +
                              "delegated to the WorldCollisionBaker sub-tilemaps (source collider disabled).");
                    continue;
                }

                if (tiles == 0 && paths == 0)
                {
                    empty++;
                    // An empty layer is the normal case for most of the 11 visual
                    // layers — reported in the summary line below, detail on demand.
                    VerboseLog.Log(VerboseLog.Category.Collision,
                        () => $"[TileCollisionDiagnostics] {layerName}: 0 tiles → no collision (empty layer).");
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
                    // Healthy layers need no attention; the summary counts them.
                    VerboseLog.Log(VerboseLog.Category.Collision,
                        () => $"[TileCollisionDiagnostics] {layerName}: {tiles} tiles, pathCount={paths}.");
                }
            }

            Debug.Log($"[TileCollisionDiagnostics] {total} composite collider(s): " +
                      $"{healthy} healthy, {empty} empty, {delegated} delegated, {unbaked} UNBAKED.");
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
