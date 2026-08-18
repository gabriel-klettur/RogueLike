using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    /// <summary>
    /// Runtime editor for <see cref="RoomNodeGraphSO"/>-style dungeon graphs.
    /// Accessible from the General Editor (ESC) → "Dungeon NodeGraph" button.
    /// Stores graphs as JSON DTOs under
    /// <see cref="GraphsDirectory"/> instead of as ScriptableObject sub-assets,
    /// because asset-database writes don't work in shipped builds.
    ///
    /// Phase 2 MVP: no pan/zoom, no bezier connections, no drag — node
    /// placement uses the click coordinate inside the graph area; connections
    /// use a "select source → select target" two-click flow with toast
    /// validation feedback.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class DungeonNodeGraphEditor : SingletonMonoBehaviour<DungeonNodeGraphEditor>,
        GameEditorManager.IGameEditor
    {
        [Tooltip("Master list of room node types — populates the Add-Node picker.")]
        [SerializeField] private RoomNodeTypeListSO roomNodeTypeList;

        // ── Editor state ─────────────────────────────────────────────────
        private string _activeGraphName = "untitled";
        private readonly List<DungeonGraphNodeData> _nodes = new List<DungeonGraphNodeData>();
        private string _connectingFromId; // id of the source node during a 2-click connect

        // ── IGameEditor contract ─────────────────────────────────────────
        public string EditorName => "Dungeon NodeGraph";
        public bool IsActive { get; private set; }

        public IReadOnlyList<DungeonGraphNodeData> Nodes => _nodes;
        public string ActiveGraphName => _activeGraphName;
        public RoomNodeTypeListSO RoomNodeTypeList => roomNodeTypeList;

        // ── Lifecycle ────────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();
            GameEditorManager.EnsureInstance().Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        public void Activate()
        {
            IsActive = true;
            EnsureUI();
            SetUIVisible(true);
            RefreshUI();
        }

        public void Deactivate()
        {
            IsActive = false;
            SetUIVisible(false);
        }

        // ── Graph mutation API (called by the UI) ────────────────────────

        public void NewGraph(string name = "untitled")
        {
            _activeGraphName = string.IsNullOrEmpty(name) ? "untitled" : name;
            _nodes.Clear();
            _connectingFromId = null;
            ShowToast($"New graph '{_activeGraphName}'.");
            RefreshUI();
        }

        public DungeonGraphNodeData AddNode(RoomNodeTypeSO type, Vector2 position)
        {
            var node = new DungeonGraphNodeData
            {
                NodeType = type,
                Position = position,
                RoomNodeName = type != null ? type.RoomNodeTypeName : "Node",
            };
            _nodes.Add(node);
            RefreshUI();
            return node;
        }

        public void RemoveNode(string id)
        {
            var index = FindNodeIndex(id);
            if (index < 0) return;

            // Detach from neighbours so dangling refs don't break the file.
            foreach (var other in _nodes)
            {
                other.ParentIds.Remove(id);
                other.ChildIds.Remove(id);
            }
            _nodes.RemoveAt(index);
            if (_connectingFromId == id) _connectingFromId = null;
            RefreshUI();
        }

        /// <summary>Two-click connect flow: first click picks the source, second click connects.</summary>
        public void OnNodeClicked(string id)
        {
            if (string.IsNullOrEmpty(_connectingFromId))
            {
                _connectingFromId = id;
                ShowToast($"Source: {GetNodeLabel(id)} — click another node to connect.");
                RefreshUI();
                return;
            }

            if (_connectingFromId == id)
            {
                _connectingFromId = null;
                ShowToast("Connect cancelled.");
                RefreshUI();
                return;
            }

            var parent = GetNode(_connectingFromId);
            var child = GetNode(id);
            string reason = null;
            bool valid = parent != null && child != null
                && IsChildRoomValid(_nodes, parent, child, out reason);
            if (valid)
            {
                parent.ChildIds.Add(child.Id);
                child.ParentIds.Add(parent.Id);
                ShowToast($"Connected {parent.RoomNodeName} → {child.RoomNodeName}.");
            }
            else
            {
                ShowToast($"Reject: {reason ?? "invalid connection."}");
            }
            _connectingFromId = null;
            RefreshUI();
        }

        public void Save()
        {
            var dto = ToDto(_activeGraphName, _nodes);
            if (SaveToFile(_activeGraphName, dto))
                ShowToast($"Saved '{_activeGraphName}'.");
        }

        public void Load(string fileName)
        {
            var dto = LoadFromFile(fileName);
            if (dto == null) { ShowToast($"Load failed: '{fileName}'."); return; }
            _activeGraphName = dto.graphName;
            _nodes.Clear();
            _nodes.AddRange(FromDto(dto, roomNodeTypeList));
            _connectingFromId = null;
            ShowToast($"Loaded '{_activeGraphName}'.");
            RefreshUI();
        }

        public void DeleteCurrent()
        {
            if (DeleteFile(_activeGraphName)) ShowToast($"Deleted '{_activeGraphName}'.");
            RefreshUI();
        }

        // ── Helpers ──────────────────────────────────────────────────────

        public DungeonGraphNodeData GetNode(string id)
        {
            int idx = FindNodeIndex(id);
            return idx >= 0 ? _nodes[idx] : null;
        }

        public string GetConnectingFromId() => _connectingFromId;

        // Test hook so EditMode tests can mutate the active graph by name.
        public void TestSetActiveGraphName(string name)
        {
            _activeGraphName = name;
        }

        private int FindNodeIndex(string id)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i] != null && _nodes[i].Id == id) return i;
            return -1;
        }

        private string GetNodeLabel(string id)
        {
            var n = GetNode(id);
            return n != null ? n.RoomNodeName : id;
        }
    }
}
