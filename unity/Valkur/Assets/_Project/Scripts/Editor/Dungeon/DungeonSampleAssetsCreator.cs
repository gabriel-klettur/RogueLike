#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Editor.Dungeon
{
    /// <summary>
    /// One-shot menu command that scaffolds the sample ScriptableObject set
    /// the Udemy dungeon system needs to operate end-to-end. Creates:
    /// <list type="bullet">
    /// <item>Seven RoomNodeTypeSO (Entrance, Corridor, CorridorNS, CorridorEW, Room, BossRoom, None).</item>
    /// <item>One RoomNodeTypeListSO containing all of them.</item>
    /// <item>One default DungeonConfigSO.</item>
    /// <item>Five RoomTemplateSO (entrance, corridorNS, corridorEW, chamber, boss)
    ///     with empty prefab slots — the user fills in their own tilemap prefabs later.</item>
    /// <item>One RoomTemplateCatalog populated with the five templates.</item>
    /// <item>One DungeonLevel_Demo + Sample RoomNodeGraphSO referencing them.</item>
    /// </list>
    /// Idempotent: re-running the command skips assets that already exist.
    /// </summary>
    public static class DungeonSampleAssetsCreator
    {
        private const string Root = "Assets/_Project/Data/Dungeon/Samples";

        [MenuItem("Valkur/Dungeon/Create Sample Assets")]
        public static void CreateSampleAssets()
        {
            EnsureFolder(Root);

            // 1) Node types.
            var entrance   = GetOrCreateType("Entrance",   entrance: true);
            var corridor   = GetOrCreateType("Corridor",   corridor: true);
            var corridorNS = GetOrCreateType("CorridorNS", corridor: true, corridorNS: true);
            var corridorEW = GetOrCreateType("CorridorEW", corridor: true, corridorEW: true);
            var room       = GetOrCreateType("Room");
            var bossRoom   = GetOrCreateType("BossRoom",   boss: true);
            var none       = GetOrCreateType("None",       none: true, display: false);

            // 2) Type list.
            var typeListPath = $"{Root}/RoomNodeTypeList.asset";
            var typeList = AssetDatabase.LoadAssetAtPath<RoomNodeTypeListSO>(typeListPath);
            if (typeList == null)
            {
                typeList = ScriptableObject.CreateInstance<RoomNodeTypeListSO>();
                typeList.TestAdd(entrance);
                typeList.TestAdd(corridor);
                typeList.TestAdd(corridorNS);
                typeList.TestAdd(corridorEW);
                typeList.TestAdd(room);
                typeList.TestAdd(bossRoom);
                typeList.TestAdd(none);
                AssetDatabase.CreateAsset(typeList, typeListPath);
            }

            // 3) Default dungeon config.
            var configPath = $"{Root}/DungeonConfig_Default.asset";
            var config = AssetDatabase.LoadAssetAtPath<DungeonConfigSO>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DungeonConfigSO>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            // 4) Five room templates.
            var entranceTpl   = GetOrCreateTemplate("Entrance",   entrance, lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[] { D(Orientation.South, new Vector2Int(2, 0)) });
            var corridorNSTpl = GetOrCreateTemplate("CorridorNS", corridorNS, lower: Vector2Int.zero, upper: new Vector2Int(2, 4),
                doorways: new[] { D(Orientation.North, new Vector2Int(1, 4)), D(Orientation.South, new Vector2Int(1, 0)) });
            var corridorEWTpl = GetOrCreateTemplate("CorridorEW", corridorEW, lower: Vector2Int.zero, upper: new Vector2Int(4, 2),
                doorways: new[] { D(Orientation.East, new Vector2Int(4, 1)), D(Orientation.West, new Vector2Int(0, 1)) });
            var chamberTpl    = GetOrCreateTemplate("Chamber",    room,     lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[] { D(Orientation.North, new Vector2Int(2, 4)), D(Orientation.South, new Vector2Int(2, 0)) });
            var bossTpl       = GetOrCreateTemplate("Boss",       bossRoom, lower: Vector2Int.zero, upper: new Vector2Int(7, 7),
                doorways: new[] { D(Orientation.North, new Vector2Int(3, 7)) });

            // 5) Catalog.
            var catalogPath = $"{Root}/RoomTemplateCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<RoomTemplateCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RoomTemplateCatalog>();
                catalog.UpsertTemplate(entranceTpl);
                catalog.UpsertTemplate(corridorNSTpl);
                catalog.UpsertTemplate(corridorEWTpl);
                catalog.UpsertTemplate(chamberTpl);
                catalog.UpsertTemplate(bossTpl);
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            // 6) Sample graph + level.
            var graphPath = $"{Root}/RoomNodeGraph_Demo.asset";
            var graph = AssetDatabase.LoadAssetAtPath<RoomNodeGraphSO>(graphPath);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();
                AssetDatabase.CreateAsset(graph, graphPath);
                BuildDemoGraph(graph, entrance, corridor, room);
            }

            var levelPath = $"{Root}/DungeonLevel_Demo.asset";
            var level = AssetDatabase.LoadAssetAtPath<DungeonLevelSO>(levelPath);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<DungeonLevelSO>();
                level.levelName = "Demo Dungeon";
                level.roomTemplateList.Add(entranceTpl);
                level.roomTemplateList.Add(corridorNSTpl);
                level.roomTemplateList.Add(corridorEWTpl);
                level.roomTemplateList.Add(chamberTpl);
                level.roomTemplateList.Add(bossTpl);
                level.roomNodeGraphList.Add(graph);
                AssetDatabase.CreateAsset(level, levelPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Valkur] Dungeon sample assets created at " + Root +
                      ". Next: assign tilemap prefabs to each RoomTemplateSO in the inspector.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Asset helpers.
        // ─────────────────────────────────────────────────────────────────

        private static RoomNodeTypeSO GetOrCreateType(string name,
            bool entrance = false, bool corridor = false,
            bool corridorNS = false, bool corridorEW = false,
            bool boss = false, bool none = false, bool display = true)
        {
            var path = $"{Root}/RoomNodeType_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RoomNodeTypeSO>(path);
            if (existing != null) return existing;

            var so = ScriptableObject.CreateInstance<RoomNodeTypeSO>();
            so.TestSetTypeFlags(name, entrance, corridor, corridorNS, corridorEW, boss, none, display);
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static RoomTemplateSO GetOrCreateTemplate(string name, RoomNodeTypeSO type,
            Vector2Int lower, Vector2Int upper, Doorway[] doorways)
        {
            var path = $"{Root}/RoomTemplate_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RoomTemplateSO>(path);
            if (existing != null) return existing;

            var so = ScriptableObject.CreateInstance<RoomTemplateSO>();
            so.roomNodeType = type;
            so.lowerBounds = lower;
            so.upperBounds = upper;
            foreach (var d in doorways) so.doorwayList.Add(d);
            so.TestRegenerateGuid();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static Doorway D(Orientation orient, Vector2Int pos)
            => new Doorway
            {
                orientation = orient,
                position = pos,
                doorwayCopyTileWidth = 1,
                doorwayCopyTileHeight = 1,
            };

        // Demo graph: Entrance → Corridor → Room (chamber). Built directly
        // without sub-asset embedding to keep the script side-effect simple.
        private static void BuildDemoGraph(RoomNodeGraphSO graph,
            RoomNodeTypeSO entranceType, RoomNodeTypeSO corridorType, RoomNodeTypeSO roomType)
        {
            var entranceNode = ScriptableObject.CreateInstance<RoomNodeSO>();
            entranceNode.Initialise(new Rect(50, 80, 200, 80), graph, entranceType);
            entranceNode.name = "Node_Entrance";

            var corridorNode = ScriptableObject.CreateInstance<RoomNodeSO>();
            corridorNode.Initialise(new Rect(50, 200, 200, 80), graph, corridorType);
            corridorNode.name = "Node_Corridor";

            var roomNode = ScriptableObject.CreateInstance<RoomNodeSO>();
            roomNode.Initialise(new Rect(50, 320, 200, 80), graph, roomType);
            roomNode.name = "Node_Room";

            entranceNode.childRoomNodeIDList.Add(corridorNode.id);
            corridorNode.parentRoomNodeIDList.Add(entranceNode.id);
            corridorNode.childRoomNodeIDList.Add(roomNode.id);
            roomNode.parentRoomNodeIDList.Add(corridorNode.id);

            graph.AddRoomNode(entranceNode);
            graph.AddRoomNode(corridorNode);
            graph.AddRoomNode(roomNode);

            AssetDatabase.AddObjectToAsset(entranceNode, graph);
            AssetDatabase.AddObjectToAsset(corridorNode, graph);
            AssetDatabase.AddObjectToAsset(roomNode, graph);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            // Walk down the path creating any missing leg.
            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
