using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// A graph of <see cref="RoomNodeSO"/>s describing dungeon topology.
    /// Children are stored on each node as GUID strings; this asset just owns
    /// the flat node list and a lazy idâ†’node lookup. The runtime NodeGraph
    /// editor (Phase 2) creates/saves these graphs as JSON DTOs; the asset
    /// form is also created for fixture purposes.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoomNodeGraph",
        menuName = "Valkur/Dungeon/Udemy/Room Node Graph")]
    public class RoomNodeGraphSO : ScriptableObject
    {
        [HideInInspector] public RoomNodeTypeListSO roomNodeTypeList;

        [HideInInspector]
        [SerializeField] private List<RoomNodeSO> roomNodeList = new List<RoomNodeSO>();

        // Rebuilt lazily / on validation. Not serialized â€” the list is the source of truth.
        private readonly Dictionary<string, RoomNodeSO> _roomNodeDictionary
            = new Dictionary<string, RoomNodeSO>();
        private bool _dictionaryDirty = true;

        public IReadOnlyList<RoomNodeSO> RoomNodeList => roomNodeList;

        /// <summary>Look up a node by GUID. Builds the dictionary on first call.</summary>
        public RoomNodeSO GetRoomNode(string roomNodeID)
        {
            if (string.IsNullOrEmpty(roomNodeID)) return null;
            EnsureDictionary();
            return _roomNodeDictionary.TryGetValue(roomNodeID, out var node) ? node : null;
        }

        /// <summary>Look up the (first) node with a given type. Used to find the entrance.</summary>
        public RoomNodeSO GetRoomNode(RoomNodeTypeSO type)
        {
            if (type == null) return null;
            for (int i = 0; i < roomNodeList.Count; i++)
            {
                if (roomNodeList[i] != null && roomNodeList[i].roomNodeType == type)
                    return roomNodeList[i];
            }
            return null;
        }

        /// <summary>Iterate the live <see cref="RoomNodeSO"/>s referenced by a parent's child id list.</summary>
        public IEnumerable<RoomNodeSO> GetChildRoomNodes(RoomNodeSO parentRoomNode)
        {
            if (parentRoomNode == null) yield break;
            foreach (var childId in parentRoomNode.childRoomNodeIDList)
            {
                var child = GetRoomNode(childId);
                if (child != null) yield return child;
            }
        }

        /// <summary>Append a node and mark the lookup dirty.</summary>
        public void AddRoomNode(RoomNodeSO node)
        {
            if (node == null) return;
            roomNodeList.Add(node);
            _dictionaryDirty = true;
        }

        /// <summary>Remove a node and mark the lookup dirty.</summary>
        public void RemoveRoomNode(RoomNodeSO node)
        {
            if (node == null) return;
            if (roomNodeList.Remove(node)) _dictionaryDirty = true;
        }

        /// <summary>Force the lookup to rebuild on next access.</summary>
        public void InvalidateDictionary() => _dictionaryDirty = true;

        private void EnsureDictionary()
        {
            if (!_dictionaryDirty) return;
            _roomNodeDictionary.Clear();
            for (int i = 0; i < roomNodeList.Count; i++)
            {
                var node = roomNodeList[i];
                if (node != null && !string.IsNullOrEmpty(node.id))
                    _roomNodeDictionary[node.id] = node;
            }
            _dictionaryDirty = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _dictionaryDirty = true;
        }
#endif
    }
}
