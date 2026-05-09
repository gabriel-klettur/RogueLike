using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// One node in a <see cref="RoomNodeGraphSO"/>. Stored as a sub-asset (offline)
    /// or serialized in JSON DTO (runtime editor). Connections are stored as GUID
    /// strings instead of object references so they survive serialization round-trips.
    ///
    /// All methods here are runtime-safe â€” no <c>#if UNITY_EDITOR</c> guards â€” because
    /// Valkur's NodeGraph editor runs at runtime, not in the Unity editor window.
    /// </summary>
    public class RoomNodeSO : ScriptableObject
    {
        [HideInInspector] public string id;
        [HideInInspector] public string roomNodeName = "RoomNode";
        [HideInInspector] public Rect rect;

        [HideInInspector] public List<string> parentRoomNodeIDList = new List<string>();
        [HideInInspector] public List<string> childRoomNodeIDList = new List<string>();

        [HideInInspector] public RoomNodeGraphSO roomNodeGraph;
        public RoomNodeTypeSO roomNodeType;

        /// <summary>Initialise a freshly created node and assign it a new GUID.</summary>
        public void Initialise(Rect rect, RoomNodeGraphSO nodeGraph, RoomNodeTypeSO nodeType)
        {
            this.rect = rect;
            this.id = Guid.NewGuid().ToString();
            this.roomNodeName = "RoomNode";
            this.roomNodeGraph = nodeGraph;
            this.roomNodeType = nodeType;
        }

        /// <summary>
        /// Add a child id to this node if the connection is valid (10 rules below).
        /// Returns true if added, false if rejected.
        /// </summary>
        public bool AddChildRoomNodeIDToRoomNode(string childID)
        {
            if (!IsChildRoomValid(childID)) return false;
            childRoomNodeIDList.Add(childID);
            return true;
        }

        /// <summary>Add a parent id unconditionally (only valid pairs reach this method).</summary>
        public bool AddParentRoomNodeIDToRoomNode(string parentID)
        {
            parentRoomNodeIDList.Add(parentID);
            return true;
        }

        public bool RemoveChildRoomNodeIDFromRoomNode(string childID)
            => childRoomNodeIDList.Remove(childID);

        public bool RemoveParentRoomNodeIDFromRoomNode(string parentID)
            => parentRoomNodeIDList.Remove(parentID);

        /// <summary>
        /// The 10 connection rules ported verbatim from Udemy's RoomNodeSO.IsChildRoomValid.
        /// Order matters â€” earlier rules short-circuit later ones.
        /// </summary>
        public bool IsChildRoomValid(string childID)
        {
            if (roomNodeGraph == null) return false;
            var child = roomNodeGraph.GetRoomNode(childID);
            if (child == null || child.roomNodeType == null || roomNodeType == null) return false;

            // Rule 1 â€” only one connected boss room is permitted in the entire graph.
            bool bossAlreadyConnected = false;
            foreach (var node in roomNodeGraph.RoomNodeList)
            {
                if (node.roomNodeType != null
                    && node.roomNodeType.IsBossRoom
                    && node.parentRoomNodeIDList.Count > 0)
                {
                    bossAlreadyConnected = true;
                    break;
                }
            }
            if (child.roomNodeType.IsBossRoom && bossAlreadyConnected) return false;

            // Rule 2 â€” None-typed nodes cannot participate in connections.
            if (child.roomNodeType.IsNone) return false;

            // Rule 3 â€” the same child cannot be added twice.
            if (childRoomNodeIDList.Contains(childID)) return false;

            // Rule 4 â€” no self-loops.
            if (id == childID) return false;

            // Rule 5 â€” child must not already be one of this node's parents (cycle).
            if (parentRoomNodeIDList.Contains(childID)) return false;

            // Rule 6 â€” each node may have at most one parent.
            if (child.parentRoomNodeIDList.Count > 0) return false;

            // Rule 7 â€” corridor cannot connect directly to corridor.
            if (child.roomNodeType.IsCorridor && roomNodeType.IsCorridor) return false;

            // Rule 8 â€” non-corridor cannot connect directly to non-corridor.
            if (!child.roomNodeType.IsCorridor && !roomNodeType.IsCorridor) return false;

            // Rule 9 â€” a node may have at most MaxChildCorridors corridor children.
            if (child.roomNodeType.IsCorridor
                && childRoomNodeIDList.Count >= DungeonSettings.MaxChildCorridors)
                return false;

            // Rule 10 â€” entrance is the graph root and cannot become anyone's child.
            if (child.roomNodeType.IsEntrance) return false;

            // Rule 11 â€” a corridor that already has a (room) child rejects further children.
            if (!child.roomNodeType.IsCorridor && childRoomNodeIDList.Count > 0) return false;

            return true;
        }
    }
}
