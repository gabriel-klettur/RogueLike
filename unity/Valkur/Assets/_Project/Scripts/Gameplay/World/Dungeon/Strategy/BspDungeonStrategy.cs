using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World.Dungeon.Strategy
{
    /// <summary>
    /// Adapter that exposes the legacy procedural BSP-style dungeon
    /// (<see cref="DungeonGenerator"/> + <see cref="DungeonLoader"/>) as an
    /// <see cref="IDungeonStrategy"/>. Behavior is byte-identical to the previous
    /// inline call in <c>GameplaySceneSetup.GenerateDungeon</c>.
    /// </summary>
    public sealed class BspDungeonStrategy : IDungeonStrategy
    {
        public const string StrategyId = "bsp";

        private readonly DungeonGeneratorConfig _config;
        private DungeonLoader _spawnedLoader;

        public BspDungeonStrategy(DungeonGeneratorConfig config)
        {
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

            var config = _config != null
                ? _config
                : ScriptableObject.CreateInstance<DungeonGeneratorConfig>();

            var loaderGo = new GameObject("DungeonLoader");
            if (ctx.SceneContainer != null)
                loaderGo.transform.SetParent(ctx.SceneContainer, false);

            _spawnedLoader = loaderGo.AddComponent<DungeonLoader>();
            _spawnedLoader.SetConfig(config);
            _spawnedLoader.GenerateAndPaint(
                ctx.GridBuilder,
                ctx.DungeonOffsetX, ctx.DungeonOffsetY,
                ctx.LobbyOffsetX, ctx.LobbyOffsetY,
                ctx.ZoneHeight,
                ctx.Seed);

            result = BuildResult(_spawnedLoader.LastResult, ctx);
            return result.Success;
        }

        public void Cleanup()
        {
            if (_spawnedLoader == null) return;

            if (Application.isPlaying)
                Object.Destroy(_spawnedLoader.gameObject);
            else
                Object.DestroyImmediate(_spawnedLoader.gameObject);

            _spawnedLoader = null;
        }

        private static DungeonGenerationResult BuildResult(
            DungeonGenerator.Result genResult, DungeonGenerationContext ctx)
        {
            if (genResult.Rooms == null)
            {
                return DungeonGenerationResult.Failed("DungeonGenerator returned no rooms.");
            }

            var rooms = new List<RectInt>(genResult.Rooms.Count);
            for (int i = 0; i < genResult.Rooms.Count; i++)
            {
                rooms.Add(BspDungeonResultConverter.ToWorldRect(
                    genResult.Rooms[i], genResult.Height,
                    ctx.DungeonOffsetX, ctx.DungeonOffsetY));
            }

            Vector2Int entrance = Vector2Int.zero;
            if (genResult.Rooms.Count > 0)
            {
                var center = DungeonGenerator.CenterOf(genResult.Rooms[0]);
                int flippedY = genResult.Height - 1 - center.y;
                entrance = new Vector2Int(ctx.DungeonOffsetX + center.x,
                                          ctx.DungeonOffsetY + flippedY);
            }

            return new DungeonGenerationResult
            {
                Success = true,
                RoomBounds = rooms,
                EntrancePosition = entrance,
            };
        }
    }
}
