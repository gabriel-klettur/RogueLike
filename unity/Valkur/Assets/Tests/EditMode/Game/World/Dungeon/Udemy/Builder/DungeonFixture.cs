using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Shared fixture builder for DungeonBuilder tests. Constructs the smallest
    /// possible end-to-end scenario: 4 node types, 3 templates, 1 graph
    /// (entrance → corridorNS → chamber). Caller disposes via <see cref="Dispose"/>.
    /// </summary>
    internal sealed class DungeonFixture : IDisposable
    {
        public RoomNodeTypeSO EntranceType;
        public RoomNodeTypeSO CorridorType;
        public RoomNodeTypeSO CorridorNSType;
        public RoomNodeTypeSO RoomType;
        public RoomNodeTypeSO NoneType;
        public RoomNodeTypeListSO NodeTypeList;

        public RoomTemplateSO EntranceTemplate;
        public RoomTemplateSO CorridorNSTemplate;
        public RoomTemplateSO ChamberTemplate;
        public RoomTemplateSO CorridorEWTemplate; // unused but required for type-coverage
        public RoomNodeTypeSO CorridorEWType;

        public RoomNodeGraphSO Graph;
        public DungeonLevelSO Level;
        public DungeonConfigSO Config;

        private readonly List<UnityEngine.Object> _toDispose = new List<UnityEngine.Object>();

        /// <summary>Linear graph: entrance → corridor (NS) → room.</summary>
        public static DungeonFixture MakeLinearEntranceCorridorChamber()
        {
            var f = new DungeonFixture();

            f.EntranceType = f.MakeType("Entrance", entrance: true);
            f.CorridorType = f.MakeType("Corridor", corridor: true);
            f.CorridorNSType = f.MakeType("CorridorNS", corridor: true, corridorNS: true);
            f.CorridorEWType = f.MakeType("CorridorEW", corridor: true, corridorEW: true);
            f.RoomType = f.MakeType("Room");
            f.NoneType = f.MakeType("None", none: true);

            f.NodeTypeList = ScriptableObject.CreateInstance<RoomNodeTypeListSO>();
            f._toDispose.Add(f.NodeTypeList);
            f.NodeTypeList.TestAdd(f.EntranceType);
            f.NodeTypeList.TestAdd(f.CorridorType);
            f.NodeTypeList.TestAdd(f.CorridorNSType);
            f.NodeTypeList.TestAdd(f.CorridorEWType);
            f.NodeTypeList.TestAdd(f.RoomType);
            f.NodeTypeList.TestAdd(f.NoneType);

            // Templates with explicit doorways so the matcher has work to do.
            // Entrance: 5×5, south doorway at (2, 0).
            f.EntranceTemplate = f.MakeTemplate(f.EntranceType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[]
                {
                    new Doorway { orientation = Orientation.South, position = new Vector2Int(2, 0) },
                });

            // Corridor NS: 3×5, north doorway at (1,4) and south doorway at (1,0).
            f.CorridorNSTemplate = f.MakeTemplate(f.CorridorNSType,
                lower: Vector2Int.zero, upper: new Vector2Int(2, 4),
                doorways: new[]
                {
                    new Doorway { orientation = Orientation.North, position = new Vector2Int(1, 4) },
                    new Doorway { orientation = Orientation.South, position = new Vector2Int(1, 0) },
                });

            // Corridor EW present but unused — the BuildLevel coverage check requires NS+EW.
            f.CorridorEWTemplate = f.MakeTemplate(f.CorridorEWType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 2),
                doorways: new[]
                {
                    new Doorway { orientation = Orientation.East, position = new Vector2Int(4, 1) },
                    new Doorway { orientation = Orientation.West, position = new Vector2Int(0, 1) },
                });

            // Chamber: 5×5, north doorway at (2,4).
            f.ChamberTemplate = f.MakeTemplate(f.RoomType,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[]
                {
                    new Doorway { orientation = Orientation.North, position = new Vector2Int(2, 4) },
                });

            // Graph: entrance → corridor → chamber.
            f.Graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();
            f._toDispose.Add(f.Graph);

            var entranceNode = f.MakeNode(f.Graph, f.EntranceType);
            var corridorNode = f.MakeNode(f.Graph, f.CorridorType);
            var chamberNode = f.MakeNode(f.Graph, f.RoomType);

            entranceNode.childRoomNodeIDList.Add(corridorNode.id);
            corridorNode.parentRoomNodeIDList.Add(entranceNode.id);
            corridorNode.childRoomNodeIDList.Add(chamberNode.id);
            chamberNode.parentRoomNodeIDList.Add(corridorNode.id);

            f.Graph.AddRoomNode(entranceNode);
            f.Graph.AddRoomNode(corridorNode);
            f.Graph.AddRoomNode(chamberNode);

            // Level + config.
            f.Level = ScriptableObject.CreateInstance<DungeonLevelSO>();
            f._toDispose.Add(f.Level);
            f.Level.levelName = "Test Level";
            f.Level.roomTemplateList.Add(f.EntranceTemplate);
            f.Level.roomTemplateList.Add(f.CorridorNSTemplate);
            f.Level.roomTemplateList.Add(f.CorridorEWTemplate);
            f.Level.roomTemplateList.Add(f.ChamberTemplate);
            f.Level.roomNodeGraphList.Add(f.Graph);

            f.Config = ScriptableObject.CreateInstance<DungeonConfigSO>();
            f._toDispose.Add(f.Config);
            f.Config.maxDungeonBuildAttempts = 5;
            f.Config.maxDungeonRebuildAttemptsForRoomGraph = 50;

            return f;
        }

        public void Dispose()
        {
            foreach (var obj in _toDispose)
            {
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
            _toDispose.Clear();
        }

        private RoomNodeTypeSO MakeType(string name,
            bool entrance = false, bool corridor = false,
            bool corridorNS = false, bool corridorEW = false,
            bool boss = false, bool none = false)
        {
            var t = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            t.TestSetTypeFlags(name, entrance, corridor, corridorNS, corridorEW, boss, none);
            _toDispose.Add(t);
            return t;
        }

        private RoomTemplateSO MakeTemplate(
            RoomNodeTypeSO type, Vector2Int lower, Vector2Int upper, Doorway[] doorways)
        {
            var t = ScriptableObject.CreateInstance<RoomTemplateSO>();
            t.roomNodeType = type;
            t.lowerBounds = lower;
            t.upperBounds = upper;
            foreach (var d in doorways) t.doorwayList.Add(d);
            t.TestRegenerateGuid();
            _toDispose.Add(t);
            return t;
        }

        private RoomNodeSO MakeNode(RoomNodeGraphSO graph, RoomNodeTypeSO type)
        {
            var n = ScriptableObject.CreateInstance<RoomNodeSO>();
            n.Initialise(new Rect(0, 0, 100, 60), graph, type);
            _toDispose.Add(n);
            return n;
        }
    }
}
