using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Runtime
{
    /// <summary>
    /// Copies a room prefab's per-child tilemaps into the global
    /// <see cref="WorldGridBuilder"/> layers and seals unconnected doorways
    /// with the tile-copy procedure ported from Udemy's <c>InstantiatedRoom</c>.
    ///
    /// Why a stamper instead of leaving the prefab's local tilemaps in place:
    /// Valkur uses one global Tilemap per <see cref="TilemapLayerSetup.TilemapLayer"/>
    /// shared by every zone (driven by URP 2D lighting + composite collider
    /// caching). Each Udemy room prefab brings its own tilemaps; this stamper
    /// is the bridge between the two models.
    /// </summary>
    public static class RoomTilemapStamper
    {
        /// <summary>
        /// Result returned by <see cref="Stamp"/>. The penalty matrix uses
        /// template-local indices (0..width-1, 0..height-1); pass it to
        /// <see cref="RoomPathfindingBridge.RegisterRoom"/> together with
        /// <paramref name="defaultPenalty"/> to project it into world space.
        /// </summary>
        public sealed class StampResult
        {
            public int[,] PenaltyMatrix;
            public int DefaultPenalty;
            public int LayersTransferred;
            public int TilesStamped;
            public int DoorwaysSealed;
        }

        /// <summary>
        /// Stamp a room into the global tilemaps. Caller must have already
        /// called <c>DungeonBuilder</c> so <see cref="Room.lowerBounds"/> +
        /// <see cref="Room.templateLowerBounds"/> are filled.
        /// </summary>
        public static StampResult Stamp(
            Room room,
            GameObject roomGameObject,
            WorldGridBuilder gridBuilder,
            DungeonConfigSO config)
        {
            if (room == null || roomGameObject == null || gridBuilder == null)
            {
                Debug.LogError("[RoomTilemapStamper] Null argument; skipping stamp.");
                return new StampResult();
            }

            var result = new StampResult { DefaultPenalty = config != null ? config.defaultMovementPenalty : 40 };
            var sourceTilemaps = roomGameObject.GetComponentsInChildren<Tilemap>(includeInactive: true);

            // World offset to translate template-local cells into world cells.
            var offset = room.lowerBounds - room.templateLowerBounds;

            // We need the source Collision tilemap to compute the A* penalty matrix below.
            Tilemap sourceCollision = null;

            foreach (var src in sourceTilemaps)
            {
                if (src == null) continue;
                if (!RoomTilemapLayerMapping.TryResolve(src.gameObject.name, out var layer))
                    continue;

                var dst = gridBuilder.GetTilemap(layer);
                if (dst == null)
                {
                    Debug.LogWarning(
                        $"[RoomTilemapStamper] Destination tilemap missing for layer '{layer}'.");
                    continue;
                }

                int copied = TransferAllTiles(src, dst, offset);
                result.TilesStamped += copied;
                result.LayersTransferred++;

                if (layer == TilemapLayerSetup.TilemapLayer.Collision)
                    sourceCollision = src;
            }

            // Visual fallback: if the prefab brought no tilemaps, paint the
            // room's bounding box on the global Ground layer with the config's
            // defaultFloorTile. Lets the dungeon be VISIBLE even when authored
            // template prefabs are still empty — useful while iterating.
            if (result.LayersTransferred == 0 && config != null && config.defaultFloorTile != null)
            {
                result.TilesStamped += PaintFallbackFloor(room, gridBuilder, config.defaultFloorTile);
            }

            // Build the A* penalty matrix from the source collision tilemap.
            // We use the SOURCE (template-local) tilemap so coordinates match
            // template-local indexing, which RoomPathfindingBridge expects.
            result.PenaltyMatrix = ComputePenaltyMatrix(room, sourceCollision, config);

            // Seal doorways whose other side never got connected by the builder.
            result.DoorwaysSealed = BlockOffUnusedDoorways(room, gridBuilder, offset);

            // Hide the destination Collision renderer so the global collision
            // layer stays invisible (matches Valkur's convention for the
            // existing "dungeon" zone — see DungeonLoader).
            var collisionDst = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            if (collisionDst != null)
            {
                var renderer = collisionDst.GetComponent<TilemapRenderer>();
                if (renderer != null) renderer.enabled = false;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Tile transfer.
        // ─────────────────────────────────────────────────────────────────

        private static int PaintFallbackFloor(Room room, WorldGridBuilder gridBuilder, TileBase fallback)
        {
            var ground = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            if (ground == null) return 0;

            int painted = 0;
            for (int x = room.lowerBounds.x; x <= room.upperBounds.x; x++)
            for (int y = room.lowerBounds.y; y <= room.upperBounds.y; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), fallback);
                painted++;
            }
            return painted;
        }

        private static int TransferAllTiles(Tilemap src, Tilemap dst, Vector2Int worldOffset)
        {
            int count = 0;
            var bounds = src.cellBounds;
            for (int x = bounds.xMin; x <= bounds.xMax; x++)
            for (int y = bounds.yMin; y <= bounds.yMax; y++)
            {
                var srcCell = new Vector3Int(x, y, 0);
                var tile = src.GetTile(srcCell);
                if (tile == null) continue;

                var dstCell = new Vector3Int(x + worldOffset.x, y + worldOffset.y, 0);
                dst.SetTile(dstCell, tile);
                dst.SetTransformMatrix(dstCell, src.GetTransformMatrix(srcCell));
                count++;
            }
            return count;
        }

        // ─────────────────────────────────────────────────────────────────
        // A* penalty matrix.
        // Mirrors Udemy's AddObstaclesAndPreferredPaths but reads from the
        // (still in-prefab) collision tilemap so indices stay template-local.
        // ─────────────────────────────────────────────────────────────────

        private static int[,] ComputePenaltyMatrix(Room room, Tilemap sourceCollision, DungeonConfigSO config)
        {
            int width = room.templateUpperBounds.x - room.templateLowerBounds.x + 1;
            int height = room.templateUpperBounds.y - room.templateLowerBounds.y + 1;
            int defaultPenalty = config != null ? config.defaultMovementPenalty : 40;
            var matrix = new int[width, height];

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                matrix[x, y] = defaultPenalty;

            if (sourceCollision == null || config == null) return matrix;

            var unwalkable = config.enemyUnwalkableCollisionTiles;
            var preferred = config.preferredEnemyPathTile;

            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var cell = new Vector3Int(
                    x + room.templateLowerBounds.x,
                    y + room.templateLowerBounds.y, 0);
                var tile = sourceCollision.GetTile(cell);
                if (tile == null) continue;

                if (preferred != null && tile == preferred)
                {
                    matrix[x, y] = 1;
                    continue;
                }
                if (unwalkable != null && unwalkable.Contains(tile))
                {
                    matrix[x, y] = 0;
                }
            }

            return matrix;
        }

        // ─────────────────────────────────────────────────────────────────
        // Doorway sealing — copies a small rectangle of tiles from the
        // adjacent wall over the gap, mirroring Udemy's
        // BlockDoorwayHorizontally / BlockDoorwayVertically.
        // ─────────────────────────────────────────────────────────────────

        private static int BlockOffUnusedDoorways(
            Room room, WorldGridBuilder gridBuilder, Vector2Int worldOffset)
        {
            int sealed_ = 0;
            for (int i = 0; i < room.doorWayList.Count; i++)
            {
                var doorway = room.doorWayList[i];
                if (doorway == null || doorway.isConnected) continue;

                foreach (TilemapLayerSetup.TilemapLayer layer in System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer)))
                {
                    var tilemap = gridBuilder.GetTilemap(layer);
                    if (tilemap == null) continue;
                    sealed_ += BlockADoorwayOnTilemapLayer(tilemap, doorway, worldOffset);
                }
            }
            return sealed_;
        }

        private static int BlockADoorwayOnTilemapLayer(Tilemap tilemap, Doorway doorway, Vector2Int worldOffset)
        {
            switch (doorway.orientation)
            {
                case Orientation.North:
                case Orientation.South:
                    return BlockDoorwayHorizontally(tilemap, doorway, worldOffset);
                case Orientation.East:
                case Orientation.West:
                    return BlockDoorwayVertically(tilemap, doorway, worldOffset);
                default:
                    return 0;
            }
        }

        // North/South doorways: copy a column of tiles one step to the right.
        private static int BlockDoorwayHorizontally(Tilemap tilemap, Doorway doorway, Vector2Int worldOffset)
        {
            int copied = 0;
            var start = doorway.doorwayStartCopyPosition + worldOffset;
            for (int xPos = 0; xPos < doorway.doorwayCopyTileWidth; xPos++)
            for (int yPos = 0; yPos < doorway.doorwayCopyTileHeight; yPos++)
            {
                var srcCell = new Vector3Int(start.x + xPos, start.y - yPos, 0);
                var dstCell = new Vector3Int(start.x + 1 + xPos, start.y - yPos, 0);
                var tile = tilemap.GetTile(srcCell);
                if (tile == null) continue;
                var matrix = tilemap.GetTransformMatrix(srcCell);
                tilemap.SetTile(dstCell, tile);
                tilemap.SetTransformMatrix(dstCell, matrix);
                copied++;
            }
            return copied;
        }

        // East/West doorways: copy a row of tiles one step down.
        private static int BlockDoorwayVertically(Tilemap tilemap, Doorway doorway, Vector2Int worldOffset)
        {
            int copied = 0;
            var start = doorway.doorwayStartCopyPosition + worldOffset;
            for (int yPos = 0; yPos < doorway.doorwayCopyTileHeight; yPos++)
            for (int xPos = 0; xPos < doorway.doorwayCopyTileWidth; xPos++)
            {
                var srcCell = new Vector3Int(start.x + xPos, start.y - yPos, 0);
                var dstCell = new Vector3Int(start.x + xPos, start.y - 1 - yPos, 0);
                var tile = tilemap.GetTile(srcCell);
                if (tile == null) continue;
                var matrix = tilemap.GetTransformMatrix(srcCell);
                tilemap.SetTile(dstCell, tile);
                tilemap.SetTransformMatrix(dstCell, matrix);
                copied++;
            }
            return copied;
        }
    }
}
