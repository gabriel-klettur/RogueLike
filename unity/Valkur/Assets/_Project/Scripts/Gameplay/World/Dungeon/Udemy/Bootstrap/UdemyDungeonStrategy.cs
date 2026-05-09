using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Strategy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;
using Valkur.Gameplay.World.Dungeon.Udemy.Doors;
using Valkur.Gameplay.World.Dungeon.Udemy.Runtime;
using Valkur.Gameplay.World.Dungeon.Udemy.Spawning;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Bootstrap
{
    /// <summary>
    /// Orchestrates a full Udemy-style dungeon build end-to-end. Used as the
    /// "udemy" strategy in <see cref="DungeonStrategyResolver"/>:
    ///
    /// 1. Run <see cref="DungeonBuilder"/> to compute room placements.
    /// 2. For each room: instantiate prefab, stamp tilemaps onto the global
    ///    <see cref="WorldGridBuilder"/> layers, attach <see cref="InstantiatedRoom"/>,
    ///    register the room with <see cref="RoomRegistry"/>, register A* penalties.
    /// 3. Wire <see cref="RoomPathfindingBridge"/> to <c>PathFinder</c> so the
    ///    matrix is consulted during NPC pathing.
    ///
    /// Cleanup tears every step back down so a Map slot reload starts fresh.
    /// </summary>
    public sealed class UdemyDungeonStrategy : IDungeonStrategy
    {
        public const string StrategyId = "udemy";

        private readonly DungeonLevelSO _level;
        private readonly RoomNodeTypeListSO _nodeTypeList;
        private readonly DungeonConfigSO _config;
        private readonly RoomPathfindingBridge _bridge = new RoomPathfindingBridge();
        private readonly System.Collections.Generic.List<GameObject> _spawnedRoots
            = new System.Collections.Generic.List<GameObject>();

        public UdemyDungeonStrategy(
            DungeonLevelSO level,
            RoomNodeTypeListSO nodeTypeList,
            DungeonConfigSO config)
        {
            _level = level;
            _nodeTypeList = nodeTypeList;
            _config = config;
        }

        public string Id => StrategyId;

        public bool TryGenerate(DungeonGenerationContext ctx, out DungeonGenerationResult result)
        {
            if (ctx == null || ctx.GridBuilder == null)
            {
                result = DungeonGenerationResult.Failed("Missing context or grid builder.");
                return false;
            }
            if (_level == null)
            {
                result = DungeonGenerationResult.Failed("UdemyDungeonStrategy needs a DungeonLevelSO.");
                return false;
            }

            // 1) Compute room placements.
            var builder = ctx.Seed != -1
                ? DungeonBuilder.FromSeed(_config, _nodeTypeList, ctx.Seed)
                : new DungeonBuilder(_config, _nodeTypeList);
            var buildResult = builder.GenerateDungeon(new DungeonBuildRequest
            {
                Level = _level,
                NodeTypeList = _nodeTypeList,
                Config = _config,
                Seed = ctx.Seed,
            });

            if (!buildResult.Success)
            {
                result = DungeonGenerationResult.Failed(buildResult.FailureReason);
                return false;
            }

            // 2) Spawn prefabs + stamp tilemaps + register rooms.
            var roomBounds = new System.Collections.Generic.List<RectInt>();
            Vector2Int entrance = Vector2Int.zero;
            var dungeonOrigin = new Vector2Int(ctx.DungeonOffsetX, ctx.DungeonOffsetY);

            foreach (var room in buildResult.RoomsByNodeId.Values)
            {
                if (room.prefab == null) continue;

                // Translate template-local bounds into world space + apply
                // the requested dungeon offset so the whole dungeon sits
                // inside the target map slot.
                room.lowerBounds += dungeonOrigin;
                room.upperBounds += dungeonOrigin;

                Vector3 worldPos = new Vector3(
                    room.lowerBounds.x - room.templateLowerBounds.x,
                    room.lowerBounds.y - room.templateLowerBounds.y,
                    0f);

                var roomGo = Object.Instantiate(room.prefab, worldPos, Quaternion.identity);
                if (ctx.SceneContainer != null) roomGo.transform.SetParent(ctx.SceneContainer, true);
                _spawnedRoots.Add(roomGo);

                // 2a) Stamp tilemaps and compute A* penalty matrix.
                var stamp = RoomTilemapStamper.Stamp(room, roomGo, ctx.GridBuilder, _config);
                _bridge.RegisterRoom(room, stamp.PenaltyMatrix, stamp.DefaultPenalty);

                // 2b) Attach InstantiatedRoom + register doors.
                var instantiated = roomGo.GetComponent<InstantiatedRoom>()
                                   ?? roomGo.AddComponent<InstantiatedRoom>();
                instantiated.Initialise(room, _config != null ? _config.playerLayer : (int?)null);
                foreach (var door in roomGo.GetComponentsInChildren<Door>())
                    instantiated.RegisterDoor(door);

                // 2c) Make Room available to spawner / audio subscribers.
                RoomRegistry.Register(room);

                roomBounds.Add(new RectInt(
                    room.lowerBounds.x, room.lowerBounds.y,
                    room.upperBounds.x - room.lowerBounds.x + 1,
                    room.upperBounds.y - room.lowerBounds.y + 1));

                if (room.roomNodeType != null && room.roomNodeType.IsEntrance)
                {
                    entrance = new Vector2Int(
                        (room.lowerBounds.x + room.upperBounds.x) / 2,
                        (room.lowerBounds.y + room.upperBounds.y) / 2);
                }
            }

            // 3) Wire A* bridge.
            _bridge.AttachToPathFinder();

            result = new DungeonGenerationResult
            {
                Success = true,
                RoomBounds = roomBounds,
                EntrancePosition = entrance,
            };
            return true;
        }

        public void Cleanup()
        {
            _bridge.DetachFromPathFinder();
            _bridge.Clear();

            foreach (var root in _spawnedRoots)
            {
                if (root == null) continue;
                if (Application.isPlaying) Object.Destroy(root);
                else Object.DestroyImmediate(root);
            }
            _spawnedRoots.Clear();

            RoomRegistry.Clear();
        }
    }
}
