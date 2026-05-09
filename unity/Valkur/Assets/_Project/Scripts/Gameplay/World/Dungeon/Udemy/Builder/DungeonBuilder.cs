using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Pure (no-MonoBehaviour) port of Udemy's <c>DungeonBuilder.GenerateDungeon</c>.
    /// Picks a random graph from the dungeon level, BFS-walks it from the entrance,
    /// and tries to place each child room by aligning a doorway with one of its
    /// parent's available doorways. Falls back through the double-retry loop when
    /// rooms overlap or no compatible doorway remains.
    ///
    /// Determinism: the constructor takes a <see cref="System.Random"/>; tests
    /// pass a seeded instance, runtime callers can pass <c>new System.Random()</c>
    /// or wrap the seed via <see cref="FromSeed"/>.
    ///
    /// This phase intentionally does NOT instantiate prefabs; it only positions
    /// rooms in world tile space. Phase 4's <c>InstantiatedRoom</c> + tilemap
    /// transfer code will consume <see cref="DungeonBuildResult.RoomsByNodeId"/>
    /// to spawn the actual GameObjects.
    /// </summary>
    public sealed class DungeonBuilder
    {
        private readonly System.Random _rng;
        private readonly DungeonConfigSO _config;
        private readonly RoomNodeTypeListSO _nodeTypeList;
        private readonly Dictionary<string, RoomTemplateSO> _roomTemplateDictionary
            = new Dictionary<string, RoomTemplateSO>();
        private readonly Dictionary<string, Room> _roomsByNodeId
            = new Dictionary<string, Room>();
        private List<RoomTemplateSO> _roomTemplateList;

        /// <summary>Default ctor uses Unity's <c>UnityEngine.Random</c> seed bridge.</summary>
        public DungeonBuilder(DungeonConfigSO config, RoomNodeTypeListSO nodeTypeList)
            : this(config, nodeTypeList, new System.Random()) { }

        public DungeonBuilder(DungeonConfigSO config, RoomNodeTypeListSO nodeTypeList, System.Random rng)
        {
            _config = config;
            _nodeTypeList = nodeTypeList;
            _rng = rng ?? new System.Random();
        }

        /// <summary>Convenience factory for seeded builds (deterministic tests + replay).</summary>
        public static DungeonBuilder FromSeed(DungeonConfigSO config, RoomNodeTypeListSO nodeTypeList, int seed)
            => new DungeonBuilder(config, nodeTypeList, new System.Random(seed));

        /// <summary>
        /// Run the double retry loop and return the placed rooms. Mirrors Udemy's
        /// <c>DungeonBuilder.GenerateDungeon</c> closely so the algorithm stays
        /// auditable against the original.
        /// </summary>
        public DungeonBuildResult GenerateDungeon(DungeonBuildRequest request)
        {
            if (request == null) return DungeonBuildResult.Failed("Null request.");
            if (request.Level == null) return DungeonBuildResult.Failed("Null DungeonLevelSO.");
            if (request.NodeTypeList == null && _nodeTypeList == null)
                return DungeonBuildResult.Failed("Null RoomNodeTypeListSO.");

            var nodeTypes = request.NodeTypeList ?? _nodeTypeList;
            var config = request.Config ?? _config;
            int maxOuter = config != null ? config.maxDungeonBuildAttempts : 10;
            int maxInner = config != null ? config.maxDungeonRebuildAttemptsForRoomGraph : 1000;

            _roomTemplateList = request.Level.roomTemplateList;
            LoadRoomTemplatesIntoDictionary();

            var result = new DungeonBuildResult();
            bool successful = false;
            int outerAttempts = 0;
            int totalInnerAttempts = 0;

            while (!successful && outerAttempts < maxOuter)
            {
                outerAttempts++;
                var graph = SelectRandomRoomNodeGraph(request.Level.roomNodeGraphList);
                if (graph == null)
                {
                    return DungeonBuildResult.Failed("DungeonLevel has no node graphs.");
                }

                int innerAttempts = 0;
                successful = false;
                while (!successful && innerAttempts <= maxInner)
                {
                    ClearDungeon();
                    innerAttempts++;
                    totalInnerAttempts++;
                    successful = AttemptToBuildRandomDungeon(graph, nodeTypes);
                }
            }

            result.Success = successful;
            result.OuterAttempts = outerAttempts;
            result.InnerAttempts = totalInnerAttempts;
            if (successful)
            {
                result.RoomsByNodeId = new Dictionary<string, Room>(_roomsByNodeId);
            }
            else
            {
                result.FailureReason =
                    $"Exhausted retries (outer={outerAttempts}/{maxOuter}, inner={totalInnerAttempts}).";
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Top-level BFS over the graph from the entrance node.
        // ─────────────────────────────────────────────────────────────────

        private bool AttemptToBuildRandomDungeon(RoomNodeGraphSO graph, RoomNodeTypeListSO nodeTypes)
        {
            var entrance = FindEntranceNode(graph, nodeTypes);
            if (entrance == null) return false;

            var openQueue = new Queue<RoomNodeSO>();
            openQueue.Enqueue(entrance);

            bool noOverlaps = ProcessRoomsInOpenRoomNodeQueue(graph, openQueue);

            return openQueue.Count == 0 && noOverlaps;
        }

        private bool ProcessRoomsInOpenRoomNodeQueue(RoomNodeGraphSO graph, Queue<RoomNodeSO> queue)
        {
            bool noOverlaps = true;

            while (queue.Count > 0 && noOverlaps)
            {
                var node = queue.Dequeue();

                foreach (var child in graph.GetChildRoomNodes(node))
                    queue.Enqueue(child);

                if (node.roomNodeType != null && node.roomNodeType.IsEntrance)
                {
                    var template = GetRandomRoomTemplate(node.roomNodeType);
                    if (template == null) return false;
                    var room = CreateRoomFromRoomTemplate(template, node);
                    room.isPositioned = true;
                    _roomsByNodeId[room.id] = room;
                }
                else
                {
                    if (node.parentRoomNodeIDList.Count == 0) return false;
                    if (!_roomsByNodeId.TryGetValue(node.parentRoomNodeIDList[0], out var parent))
                        return false;

                    noOverlaps = CanPlaceRoomWithNoOverlaps(node, parent);
                }
            }

            return noOverlaps;
        }

        private RoomNodeSO FindEntranceNode(RoomNodeGraphSO graph, RoomNodeTypeListSO nodeTypes)
        {
            if (graph == null || nodeTypes == null) return null;
            for (int i = 0; i < nodeTypes.List.Count; i++)
            {
                var t = nodeTypes.List[i];
                if (t != null && t.IsEntrance)
                    return graph.GetRoomNode(t);
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Placement + overlap detection.
        // ─────────────────────────────────────────────────────────────────

        private bool CanPlaceRoomWithNoOverlaps(RoomNodeSO node, Room parent)
        {
            bool overlaps = true;
            while (overlaps)
            {
                var available = GetUnconnectedAvailableDoorways(parent.doorWayList);
                if (available.Count == 0) return false;

                var parentDoorway = available[_rng.Next(available.Count)];
                var template = GetRandomTemplateForRoomConsistentWithParent(node, parentDoorway);
                if (template == null) return false;

                var room = CreateRoomFromRoomTemplate(template, node);

                if (PlaceTheRoom(parent, parentDoorway, room))
                {
                    overlaps = false;
                    room.isPositioned = true;
                    _roomsByNodeId[room.id] = room;
                }
            }
            return true;
        }

        private bool PlaceTheRoom(Room parent, Doorway parentDoorway, Room room)
        {
            var childDoorway = DoorwayMatcher.GetOppositeDoorway(parentDoorway, room.doorWayList);
            if (childDoorway == null)
            {
                parentDoorway.isUnavailable = true;
                return false;
            }

            room.lowerBounds = DoorwayMatcher.ComputeChildLowerBounds(
                parent.lowerBounds, parent.templateLowerBounds, parentDoorway,
                room.templateLowerBounds, childDoorway);
            room.upperBounds = room.lowerBounds + room.templateUpperBounds - room.templateLowerBounds;

            var overlapping = CheckForRoomOverlap(room);
            if (overlapping == null)
            {
                parentDoorway.isConnected = true;
                parentDoorway.isUnavailable = true;
                childDoorway.isConnected = true;
                childDoorway.isUnavailable = true;
                return true;
            }

            // The candidate parent doorway can't host this child without colliding —
            // mark it unavailable so the next loop iteration picks a different one.
            parentDoorway.isUnavailable = true;
            return false;
        }

        private Room CheckForRoomOverlap(Room candidate)
        {
            foreach (var kvp in _roomsByNodeId)
            {
                var other = kvp.Value;
                if (other.id == candidate.id || !other.isPositioned) continue;
                if (DoorwayMatcher.RoomsOverlap(
                    candidate.lowerBounds, candidate.upperBounds,
                    other.lowerBounds, other.upperBounds))
                {
                    return other;
                }
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Template selection helpers.
        // ─────────────────────────────────────────────────────────────────

        private RoomTemplateSO GetRandomTemplateForRoomConsistentWithParent(
            RoomNodeSO node, Doorway parentDoorway)
        {
            if (node.roomNodeType == null) return null;
            var nodeTypes = _nodeTypeList;

            if (node.roomNodeType.IsCorridor)
            {
                switch (parentDoorway.orientation)
                {
                    case Orientation.North:
                    case Orientation.South:
                        return GetRandomRoomTemplate(FindFirstType(nodeTypes, t => t.IsCorridorNS));
                    case Orientation.East:
                    case Orientation.West:
                        return GetRandomRoomTemplate(FindFirstType(nodeTypes, t => t.IsCorridorEW));
                    default: return null;
                }
            }

            return GetRandomRoomTemplate(node.roomNodeType);
        }

        private RoomTemplateSO GetRandomRoomTemplate(RoomNodeTypeSO nodeType)
        {
            if (nodeType == null || _roomTemplateList == null) return null;
            var matches = new List<RoomTemplateSO>();
            for (int i = 0; i < _roomTemplateList.Count; i++)
            {
                var t = _roomTemplateList[i];
                if (t != null && t.roomNodeType == nodeType) matches.Add(t);
            }
            if (matches.Count == 0) return null;
            return matches[_rng.Next(matches.Count)];
        }

        private RoomNodeGraphSO SelectRandomRoomNodeGraph(List<RoomNodeGraphSO> graphs)
        {
            if (graphs == null || graphs.Count == 0) return null;
            return graphs[_rng.Next(graphs.Count)];
        }

        private static RoomNodeTypeSO FindFirstType(
            RoomNodeTypeListSO nodeTypes, System.Func<RoomNodeTypeSO, bool> predicate)
        {
            if (nodeTypes == null) return null;
            for (int i = 0; i < nodeTypes.List.Count; i++)
            {
                var t = nodeTypes.List[i];
                if (t != null && predicate(t)) return t;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Room construction + cleanup.
        // ─────────────────────────────────────────────────────────────────

        private Room CreateRoomFromRoomTemplate(RoomTemplateSO template, RoomNodeSO node)
        {
            var room = new Room
            {
                templateID = template.guid,
                id = node.id,
                prefab = template.prefab,
                battleMusicId = template.battleMusicId,
                ambientMusicId = template.ambientMusicId,
                roomNodeType = template.roomNodeType,
                lowerBounds = template.lowerBounds,
                upperBounds = template.upperBounds,
                spawnPositionArray = template.spawnPositionArray,
                enemiesByLevelList = template.enemiesByLevelList,
                roomLevelEnemySpawnParametersList = template.roomEnemySpawnParametersList,
                templateLowerBounds = template.lowerBounds,
                templateUpperBounds = template.upperBounds,
                childRoomIDList = new List<string>(node.childRoomNodeIDList),
                doorWayList = CopyDoorwayList(template.doorwayList),
            };
            if (node.parentRoomNodeIDList.Count == 0)
            {
                room.parentRoomID = string.Empty;
                room.isPreviouslyVisited = true;
            }
            else
            {
                room.parentRoomID = node.parentRoomNodeIDList[0];
            }
            return room;
        }

        private static List<Doorway> CopyDoorwayList(List<Doorway> source)
        {
            var copy = new List<Doorway>(source != null ? source.Count : 0);
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++)
            {
                var d = source[i];
                if (d == null) continue;
                copy.Add(new Doorway
                {
                    position = d.position,
                    orientation = d.orientation,
                    doorPrefab = d.doorPrefab,
                    doorwayStartCopyPosition = d.doorwayStartCopyPosition,
                    doorwayCopyTileWidth = d.doorwayCopyTileWidth,
                    doorwayCopyTileHeight = d.doorwayCopyTileHeight,
                    isConnected = d.isConnected,
                    isUnavailable = d.isUnavailable,
                });
            }
            return copy;
        }

        private void LoadRoomTemplatesIntoDictionary()
        {
            _roomTemplateDictionary.Clear();
            if (_roomTemplateList == null) return;
            for (int i = 0; i < _roomTemplateList.Count; i++)
            {
                var t = _roomTemplateList[i];
                if (t == null || string.IsNullOrEmpty(t.guid)) continue;
                if (!_roomTemplateDictionary.ContainsKey(t.guid))
                    _roomTemplateDictionary[t.guid] = t;
            }
        }

        private void ClearDungeon() => _roomsByNodeId.Clear();

        private static List<Doorway> GetUnconnectedAvailableDoorways(IList<Doorway> doorways)
        {
            var result = new List<Doorway>();
            if (doorways == null) return result;
            for (int i = 0; i < doorways.Count; i++)
            {
                var d = doorways[i];
                if (d == null) continue;
                if (!d.isConnected && !d.isUnavailable) result.Add(d);
            }
            return result;
        }
    }
}
