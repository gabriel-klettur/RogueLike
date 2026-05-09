using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Dungeon.Strategy;
using Valkur.Gameplay.World.Dungeon.Udemy.Bootstrap;
using Valkur.Gameplay.World.Dungeon.Udemy.Spawning;

namespace Valkur.Tests.PlayMode.Game.World.Dungeon.Udemy
{
    /// <summary>
    /// PlayMode E2E coverage for the full Udemy dungeon strategy: build →
    /// instantiate prefabs → stamp tilemaps → register rooms → publish events.
    /// Each test wires up the bare-minimum scene (a single
    /// <see cref="WorldGridBuilder"/>) and runs a tiny linear graph through
    /// <see cref="UdemyDungeonStrategy"/> using prefabs generated on the fly,
    /// so we don't depend on any pre-existing project asset.
    /// </summary>
    public class UdemyDungeonStrategyE2ETests
    {
        private readonly List<Object> _spawnedAssets = new List<Object>();
        private GameObject _gridGo;
        private WorldGridBuilder _gridBuilder;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("WorldGridBuilder");
            _gridBuilder = _gridGo.AddComponent<WorldGridBuilder>();
            _gridBuilder.BuildGrid();
            RoomRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.Destroy(_gridGo);
            foreach (var asset in _spawnedAssets)
                if (asset != null) Object.Destroy(asset);
            _spawnedAssets.Clear();
            RoomRegistry.Clear();
            DungeonStrategyResolver.ClearForTests();
        }

        [UnityTest]
        public IEnumerator TryGenerate_LinearGraph_Succeeds_AndRegistersRooms()
        {
            var fixture = MakeLinearFixture();
            var strategy = new UdemyDungeonStrategy(fixture.Level, fixture.NodeTypes, fixture.Config);

            var ctx = new DungeonGenerationContext
            {
                GridBuilder = _gridBuilder,
                DungeonOffsetX = 100,
                DungeonOffsetY = 100,
                ZoneHeight = 50,
                Seed = 42,
            };

            bool ok = strategy.TryGenerate(ctx, out var result);
            yield return null; // let one frame elapse so spawned objects settle

            Assert.IsTrue(ok, result.FailureReason);
            Assert.IsTrue(result.Success);
            Assert.GreaterOrEqual(result.RoomBounds.Count, 3);
            Assert.AreEqual(result.RoomBounds.Count, RoomRegistry.Count);

            // No two rooms overlap.
            for (int i = 0; i < result.RoomBounds.Count; i++)
            for (int j = i + 1; j < result.RoomBounds.Count; j++)
                Assert.IsFalse(BoundsOverlap(result.RoomBounds[i], result.RoomBounds[j]),
                    $"Rooms {i} and {j} overlap.");

            strategy.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Cleanup_RemovesSpawnedRooms_AndClearsRegistry()
        {
            var fixture = MakeLinearFixture();
            var strategy = new UdemyDungeonStrategy(fixture.Level, fixture.NodeTypes, fixture.Config);

            strategy.TryGenerate(new DungeonGenerationContext
            {
                GridBuilder = _gridBuilder,
                Seed = 1,
            }, out _);
            yield return null;

            Assert.Greater(RoomRegistry.Count, 0);

            strategy.Cleanup();
            yield return null;

            Assert.AreEqual(0, RoomRegistry.Count);
        }

        // ─────────────────────────────────────────────────────────────────
        // Tiny end-to-end fixture. Generates everything (types, templates,
        // prefabs, graph, level) in code so the test is hermetic.
        // ─────────────────────────────────────────────────────────────────

        private LinearFixture MakeLinearFixture()
        {
            var entranceType = MakeType("Entrance", entrance: true);
            var corridorType = MakeType("Corridor", corridor: true);
            var corridorNSType = MakeType("CorridorNS", corridor: true, corridorNS: true);
            var corridorEWType = MakeType("CorridorEW", corridor: true, corridorEW: true);
            var roomType = MakeType("Room");

            var typeList = ScriptableObject.CreateInstance<RoomNodeTypeListSO>();
            typeList.TestAdd(entranceType);
            typeList.TestAdd(corridorType);
            typeList.TestAdd(corridorNSType);
            typeList.TestAdd(corridorEWType);
            typeList.TestAdd(roomType);
            _spawnedAssets.Add(typeList);

            var entranceTpl = MakeTemplate("Entrance", entranceType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[] { D(Orientation.South, new Vector2Int(2, 0)) });
            var corridorNSTpl = MakeTemplate("CorridorNS", corridorNSType,
                lower: Vector2Int.zero, upper: new Vector2Int(2, 4),
                doorways: new[] { D(Orientation.North, new Vector2Int(1, 4)), D(Orientation.South, new Vector2Int(1, 0)) });
            var corridorEWTpl = MakeTemplate("CorridorEW", corridorEWType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 2),
                doorways: new[] { D(Orientation.East, new Vector2Int(4, 1)), D(Orientation.West, new Vector2Int(0, 1)) });
            var chamberTpl = MakeTemplate("Chamber", roomType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[] { D(Orientation.North, new Vector2Int(2, 4)) });

            var graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();
            _spawnedAssets.Add(graph);

            var entranceNode = MakeNode(graph, entranceType);
            var corridorNode = MakeNode(graph, corridorType);
            var roomNode = MakeNode(graph, roomType);

            entranceNode.childRoomNodeIDList.Add(corridorNode.id);
            corridorNode.parentRoomNodeIDList.Add(entranceNode.id);
            corridorNode.childRoomNodeIDList.Add(roomNode.id);
            roomNode.parentRoomNodeIDList.Add(corridorNode.id);

            graph.AddRoomNode(entranceNode);
            graph.AddRoomNode(corridorNode);
            graph.AddRoomNode(roomNode);

            var level = ScriptableObject.CreateInstance<DungeonLevelSO>();
            level.roomTemplateList.AddRange(new[] { entranceTpl, corridorNSTpl, corridorEWTpl, chamberTpl });
            level.roomNodeGraphList.Add(graph);
            _spawnedAssets.Add(level);

            var config = ScriptableObject.CreateInstance<DungeonConfigSO>();
            config.maxDungeonBuildAttempts = 5;
            config.maxDungeonRebuildAttemptsForRoomGraph = 50;
            _spawnedAssets.Add(config);

            return new LinearFixture
            {
                NodeTypes = typeList,
                Level = level,
                Config = config,
            };
        }

        private RoomNodeTypeSO MakeType(string name,
            bool entrance = false, bool corridor = false,
            bool corridorNS = false, bool corridorEW = false,
            bool boss = false, bool none = false)
        {
            var t = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            t.TestSetTypeFlags(name, entrance, corridor, corridorNS, corridorEW, boss, none);
            _spawnedAssets.Add(t);
            return t;
        }

        private RoomTemplateSO MakeTemplate(string name, RoomNodeTypeSO type,
            Vector2Int lower, Vector2Int upper, Doorway[] doorways)
        {
            var t = ScriptableObject.CreateInstance<RoomTemplateSO>();
            t.roomNodeType = type;
            t.lowerBounds = lower;
            t.upperBounds = upper;
            foreach (var d in doorways) t.doorwayList.Add(d);

            // Generate a minimal prefab on the fly: an empty GameObject with a
            // Grid + BoxCollider2D so InstantiatedRoom has the trigger collider
            // it requires. We don't add child Tilemaps — the stamper handles
            // missing layers gracefully (logs warning, skips); the penalty
            // matrix falls back to default values for this lightweight smoke test.
            //
            // The "prefab" must stay active so its clones (Object.Instantiate)
            // also start active, otherwise their MonoBehaviour Awake never
            // fires and InstantiatedRoom._trigger stays null.
            var prefab = new GameObject("RoomPrefab_" + name);
            prefab.AddComponent<Grid>();
            prefab.AddComponent<BoxCollider2D>();
            t.prefab = prefab;

            t.TestRegenerateGuid();
            _spawnedAssets.Add(t);
            _spawnedAssets.Add(prefab);
            return t;
        }

        private RoomNodeSO MakeNode(RoomNodeGraphSO graph, RoomNodeTypeSO type)
        {
            var n = ScriptableObject.CreateInstance<RoomNodeSO>();
            n.Initialise(new Rect(0, 0, 100, 60), graph, type);
            _spawnedAssets.Add(n);
            return n;
        }

        private Doorway D(Orientation orient, Vector2Int pos)
            => new Doorway
            {
                orientation = orient,
                position = pos,
                doorwayCopyTileWidth = 1,
                doorwayCopyTileHeight = 1,
            };

        private static bool BoundsOverlap(RectInt a, RectInt b)
        {
            // Exclusive overlap (RectInt convention) — adjacent rooms are fine.
            return Mathf.Max(a.xMin, b.xMin) < Mathf.Min(a.xMax, b.xMax)
                && Mathf.Max(a.yMin, b.yMin) < Mathf.Min(a.yMax, b.yMax);
        }

        private sealed class LinearFixture
        {
            public RoomNodeTypeListSO NodeTypes;
            public DungeonLevelSO Level;
            public DungeonConfigSO Config;
        }
    }
}
