#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Editor.Dungeon
{
    /// <summary>
    /// One-shot menu command that scaffolds a complete sample asset set
    /// for the Udemy dungeon system: tile assets, room templates with
    /// tilemap-prefab "authored content", a 5-room demo graph
    /// (entrance → corridor → chamber → corridor → boss), and the
    /// supporting RoomNodeTypeListSO + DungeonConfigSO + RoomTemplateCatalog.
    /// Idempotent — re-running rebuilds the prefabs (since their tilemap
    /// contents are baked) but preserves the GUIDs of the SOs so existing
    /// slot files keep resolving.
    /// </summary>
    public static class DungeonSampleAssetsCreator
    {
        private const string Root = "Assets/_Project/Resources/Dungeon/Samples";

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

            // 3) Tile assets — floor (interior) and wall (perimeter ring).
            var floorTile = GetOrCreateTile("DungeonFloor_Tile", "Tiles/dungeon_floor");
            var wallTile  = GetOrCreateTile("DungeonWall_Tile",  "Tiles/wall");

            // 4) DungeonConfigSO with both tiles assigned.
            var configPath = $"{Root}/DungeonConfig_Default.asset";
            var config = AssetDatabase.LoadAssetAtPath<DungeonConfigSO>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<DungeonConfigSO>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            config.defaultFloorTile = floorTile;
            config.defaultWallTile = wallTile;
            EditorUtility.SetDirty(config);

            // 5) Room templates with authored prefabs.
            //    Geometry chosen so the 5-node demo graph below resolves
            //    without overlaps:
            //      Entrance (5×5) — south doorway
            //      CorridorNS (3×5) — north + south doorways
            //      Chamber (7×7) — north + east doorways  (branches east)
            //      CorridorEW (5×3) — west + east doorways
            //      Boss (7×7) — west doorway
            var entranceTpl = GetOrCreateTemplate("Entrance", entrance,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 4),
                doorways: new[] { D(Orientation.South, new Vector2Int(2, 0)) });
            var corridorNSTpl = GetOrCreateTemplate("CorridorNS", corridorNS,
                lower: Vector2Int.zero, upper: new Vector2Int(2, 4),
                doorways: new[]
                {
                    D(Orientation.North, new Vector2Int(1, 4)),
                    D(Orientation.South, new Vector2Int(1, 0)),
                });
            var corridorEWTpl = GetOrCreateTemplate("CorridorEW", corridorEW,
                lower: Vector2Int.zero, upper: new Vector2Int(4, 2),
                doorways: new[]
                {
                    D(Orientation.East, new Vector2Int(4, 1)),
                    D(Orientation.West, new Vector2Int(0, 1)),
                });
            var chamberTpl = GetOrCreateTemplate("Chamber", room,
                lower: Vector2Int.zero, upper: new Vector2Int(6, 6),
                doorways: new[]
                {
                    D(Orientation.North, new Vector2Int(3, 6)),
                    D(Orientation.East,  new Vector2Int(6, 3)),
                });
            var bossTpl = GetOrCreateTemplate("Boss", bossRoom,
                lower: Vector2Int.zero, upper: new Vector2Int(6, 6),
                doorways: new[] { D(Orientation.West, new Vector2Int(0, 3)) });

            // 6) Bake authored Tilemap prefabs and assign them to each template.
            //    Prefabs always rebuild because the baked tile content needs to
            //    track template geometry; their .prefab path stays stable.
            entranceTpl.prefab    = BuildAndSaveRoomPrefab(entranceTpl,  floorTile, wallTile);
            corridorNSTpl.prefab  = BuildAndSaveRoomPrefab(corridorNSTpl, floorTile, wallTile);
            corridorEWTpl.prefab  = BuildAndSaveRoomPrefab(corridorEWTpl, floorTile, wallTile);
            chamberTpl.prefab     = BuildAndSaveRoomPrefab(chamberTpl,    floorTile, wallTile);
            bossTpl.prefab        = BuildAndSaveRoomPrefab(bossTpl,       floorTile, wallTile);
            EditorUtility.SetDirty(entranceTpl);
            EditorUtility.SetDirty(corridorNSTpl);
            EditorUtility.SetDirty(corridorEWTpl);
            EditorUtility.SetDirty(chamberTpl);
            EditorUtility.SetDirty(bossTpl);

            // 7) Catalog.
            var catalogPath = $"{Root}/RoomTemplateCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<RoomTemplateCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RoomTemplateCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            catalog.UpsertTemplate(entranceTpl);
            catalog.UpsertTemplate(corridorNSTpl);
            catalog.UpsertTemplate(corridorEWTpl);
            catalog.UpsertTemplate(chamberTpl);
            catalog.UpsertTemplate(bossTpl);
            EditorUtility.SetDirty(catalog);

            // 8) Rebuild the 5-node demo graph from scratch every time so it
            //    stays in sync with this script's intent (previous runs may have
            //    left a 3-node version behind).
            var graphPath = $"{Root}/RoomNodeGraph_Demo.asset";
            var oldGraph = AssetDatabase.LoadAssetAtPath<RoomNodeGraphSO>(graphPath);
            if (oldGraph != null) AssetDatabase.DeleteAsset(graphPath);
            var graph = ScriptableObject.CreateInstance<RoomNodeGraphSO>();
            AssetDatabase.CreateAsset(graph, graphPath);
            BuildDemoGraph5Rooms(graph, entrance, corridor, room, bossRoom);

            // 9) Dungeon level.
            var levelPath = $"{Root}/DungeonLevel_Demo.asset";
            var level = AssetDatabase.LoadAssetAtPath<DungeonLevelSO>(levelPath);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<DungeonLevelSO>();
                AssetDatabase.CreateAsset(level, levelPath);
            }
            level.levelName = "Demo Dungeon";
            level.roomTemplateList.Clear();
            level.roomTemplateList.Add(entranceTpl);
            level.roomTemplateList.Add(corridorNSTpl);
            level.roomTemplateList.Add(corridorEWTpl);
            level.roomTemplateList.Add(chamberTpl);
            level.roomTemplateList.Add(bossTpl);
            level.roomNodeGraphList.Clear();
            level.roomNodeGraphList.Add(graph);
            EditorUtility.SetDirty(level);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Valkur] Dungeon sample assets rebuilt at " + Root +
                      ". Reload Dungeon v1 (F11 → Maps → Load) to see the 5-room dungeon.");
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

        private static Tile GetOrCreateTile(string assetName, string spriteResourcePath)
        {
            var path = $"{Root}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (existing != null) return existing;

            var tile = ScriptableObject.CreateInstance<Tile>();
            var sprite = Resources.Load<Sprite>(spriteResourcePath);
            if (sprite != null) tile.sprite = sprite;
            AssetDatabase.CreateAsset(tile, path);
            return tile;
        }

        private static RoomTemplateSO GetOrCreateTemplate(string name, RoomNodeTypeSO type,
            Vector2Int lower, Vector2Int upper, Doorway[] doorways)
        {
            var path = $"{Root}/RoomTemplate_{name}.asset";
            var so = AssetDatabase.LoadAssetAtPath<RoomTemplateSO>(path);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<RoomTemplateSO>();
                AssetDatabase.CreateAsset(so, path);
                so.TestRegenerateGuid();
            }
            so.roomNodeType = type;
            so.lowerBounds = lower;
            so.upperBounds = upper;
            so.doorwayList.Clear();
            foreach (var d in doorways) so.doorwayList.Add(d);
            EditorUtility.SetDirty(so);
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

        // ─────────────────────────────────────────────────────────────────
        // Tilemap prefab baking. Builds an authored prefab per template:
        //   root: GameObject (with Grid)
        //     └── "Ground" Tilemap pre-painted with floor (interior) +
        //         wall (perimeter, doorway cells punched through).
        // The prefab path stays stable so the RoomTemplateSO.prefab
        // reference in scene/SO files remains valid across rebuilds.
        // ─────────────────────────────────────────────────────────────────

        private static GameObject BuildAndSaveRoomPrefab(
            RoomTemplateSO template, TileBase floorTile, TileBase wallTile)
        {
            var prefabPath = $"{Root}/RoomPrefab_{template.name.Replace("RoomTemplate_", "")}.prefab";
            // Delete the old prefab so the next SaveAsPrefabAsset write is clean.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);

            var rootGo = new GameObject(template.name);
            rootGo.AddComponent<Grid>();

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(rootGo.transform, worldPositionStays: false);
            var tilemap = groundGo.AddComponent<Tilemap>();
            groundGo.AddComponent<TilemapRenderer>();

            PaintTemplateOntoTilemap(template, tilemap, floorTile, wallTile);

            var saved = PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
            Object.DestroyImmediate(rootGo);
            return saved;
        }

        private static void PaintTemplateOntoTilemap(
            RoomTemplateSO template, Tilemap tilemap, TileBase floorTile, TileBase wallTile)
        {
            // Pre-compute doorway hole cells (5-tile cross around each doorway
            // anchor — same widening logic as the runtime fallback so the
            // authored prefab matches what the strategy would have painted).
            var holes = new HashSet<Vector2Int>();
            foreach (var d in template.doorwayList)
            {
                if (d == null) continue;
                holes.Add(d.position);
                holes.Add(d.position + Vector2Int.right);
                holes.Add(d.position + Vector2Int.left);
                holes.Add(d.position + Vector2Int.up);
                holes.Add(d.position + Vector2Int.down);
            }

            for (int x = template.lowerBounds.x; x <= template.upperBounds.x; x++)
            for (int y = template.lowerBounds.y; y <= template.upperBounds.y; y++)
            {
                bool isPerimeter =
                    x == template.lowerBounds.x || x == template.upperBounds.x ||
                    y == template.lowerBounds.y || y == template.upperBounds.y;
                bool isHole = holes.Contains(new Vector2Int(x, y));
                TileBase chosen = (isPerimeter && !isHole)
                    ? wallTile
                    : floorTile;
                tilemap.SetTile(new Vector3Int(x, y, 0), chosen);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 5-room demo graph: Entrance → Corridor → Chamber → Corridor → Boss.
        // The two corridor nodes use the generic "Corridor" type; the builder
        // resolves to NS or EW at runtime based on the parent doorway's
        // orientation (entrance.S → CorridorNS; chamber.E → CorridorEW).
        // ─────────────────────────────────────────────────────────────────

        private static void BuildDemoGraph5Rooms(RoomNodeGraphSO graph,
            RoomNodeTypeSO entranceType, RoomNodeTypeSO corridorType,
            RoomNodeTypeSO roomType, RoomNodeTypeSO bossType)
        {
            var entranceNode = MakeNode(graph, entranceType, "Node_Entrance",  new Rect(220, 60, 200, 70));
            var corridor1   = MakeNode(graph, corridorType, "Node_Corridor1", new Rect(220, 170, 200, 70));
            var chamberNode = MakeNode(graph, roomType,    "Node_Chamber",   new Rect(220, 280, 200, 70));
            var corridor2   = MakeNode(graph, corridorType, "Node_Corridor2", new Rect(450, 280, 200, 70));
            var bossNode    = MakeNode(graph, bossType,    "Node_Boss",      new Rect(680, 280, 200, 70));

            // entrance → corridor1 → chamber
            entranceNode.childRoomNodeIDList.Add(corridor1.id);
            corridor1.parentRoomNodeIDList.Add(entranceNode.id);
            corridor1.childRoomNodeIDList.Add(chamberNode.id);
            chamberNode.parentRoomNodeIDList.Add(corridor1.id);

            // chamber → corridor2 → boss
            chamberNode.childRoomNodeIDList.Add(corridor2.id);
            corridor2.parentRoomNodeIDList.Add(chamberNode.id);
            corridor2.childRoomNodeIDList.Add(bossNode.id);
            bossNode.parentRoomNodeIDList.Add(corridor2.id);

            graph.AddRoomNode(entranceNode);
            graph.AddRoomNode(corridor1);
            graph.AddRoomNode(chamberNode);
            graph.AddRoomNode(corridor2);
            graph.AddRoomNode(bossNode);

            AssetDatabase.AddObjectToAsset(entranceNode, graph);
            AssetDatabase.AddObjectToAsset(corridor1, graph);
            AssetDatabase.AddObjectToAsset(chamberNode, graph);
            AssetDatabase.AddObjectToAsset(corridor2, graph);
            AssetDatabase.AddObjectToAsset(bossNode, graph);
        }

        private static RoomNodeSO MakeNode(RoomNodeGraphSO graph, RoomNodeTypeSO type, string name, Rect rect)
        {
            var n = ScriptableObject.CreateInstance<RoomNodeSO>();
            n.Initialise(rect, graph, type);
            n.name = name;
            return n;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
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
