using System.Collections.Generic;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    public partial class DungeonNodeGraphEditor
    {
        /// <summary>
        /// Apply the 11 connection rules from <c>RoomNodeSO.IsChildRoomValid</c>
        /// to a candidate (parent, child) pair on the in-memory editor graph.
        /// We can't reuse <c>RoomNodeSO.IsChildRoomValid</c> directly because
        /// it requires a live <c>RoomNodeGraphSO</c> SO; the editor works on
        /// <see cref="DungeonGraphNodeData"/>. The rules are kept in lockstep
        /// (both implementations must change together).
        /// </summary>
        public static bool IsChildRoomValid(
            IList<DungeonGraphNodeData> graph,
            DungeonGraphNodeData parent,
            DungeonGraphNodeData child,
            out string rejectReason)
        {
            rejectReason = null;
            if (parent == null || child == null
                || parent.NodeType == null || child.NodeType == null)
            {
                rejectReason = "Missing node or node type.";
                return false;
            }

            // Rule 1 — only one connected boss room is allowed.
            bool bossAlreadyConnected = false;
            foreach (var n in graph)
            {
                if (n != null && n.NodeType != null
                    && n.NodeType.IsBossRoom
                    && n.ParentIds.Count > 0)
                {
                    bossAlreadyConnected = true;
                    break;
                }
            }
            if (child.NodeType.IsBossRoom && bossAlreadyConnected)
            {
                rejectReason = "Only one connected boss room allowed per graph.";
                return false;
            }

            if (child.NodeType.IsNone) { rejectReason = "Cannot connect to a 'None' node."; return false; }
            if (parent.ChildIds.Contains(child.Id)) { rejectReason = "Already connected."; return false; }
            if (parent.Id == child.Id) { rejectReason = "A node cannot connect to itself."; return false; }
            if (parent.ParentIds.Contains(child.Id)) { rejectReason = "Would create a cycle."; return false; }
            if (child.ParentIds.Count > 0) { rejectReason = "Child already has a parent."; return false; }
            if (child.NodeType.IsCorridor && parent.NodeType.IsCorridor)
            { rejectReason = "Corridors cannot connect directly."; return false; }
            if (!child.NodeType.IsCorridor && !parent.NodeType.IsCorridor)
            { rejectReason = "Rooms must connect through a corridor."; return false; }
            if (child.NodeType.IsCorridor
                && parent.ChildIds.Count >= DungeonSettings.MaxChildCorridors)
            { rejectReason = $"Max {DungeonSettings.MaxChildCorridors} corridor children per node."; return false; }
            if (child.NodeType.IsEntrance) { rejectReason = "Entrance cannot be a child."; return false; }
            if (!child.NodeType.IsCorridor && parent.ChildIds.Count > 0)
            { rejectReason = "Corridor with a room child cannot accept another."; return false; }

            return true;
        }
    }
}
