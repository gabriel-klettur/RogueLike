using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Graph-tool semantics: dispatches LMB clicks on nodes, edges and empty
    /// canvas to the action implied by <c>_graphTool</c>. Mirrors Python
    /// <c>fsm_graph_panel/toolbar_graph_panel/tools/*</c>.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Per-element click dispatch (called from CreateNodeVisual / CreateEdgeVisual) â”€â”€

        private void OnNodeClicked(FSMStateNode state)
        {
            switch (_graphTool)
            {
                case GraphTool.Select:        SelectState(state); break;
                case GraphTool.AddNode:       /* AddNode acts on empty space */ SelectState(state); break;
                case GraphTool.CloneNode:     CloneNode(state); break;
                case GraphTool.Delete:        DeleteNode(state); break;
                case GraphTool.Connect:       HandleConnectClick(state, isConnect: true); break;
                case GraphTool.Disconnect:    HandleConnectClick(state, isConnect: false); break;
                case GraphTool.MarkInitial:   MarkInitial(state); break;
                case GraphTool.MarkTerminal:  ToggleTerminal(state); break;
            }
        }

        private void OnEdgeClicked(FSMTransitionData tr)
        {
            switch (_graphTool)
            {
                case GraphTool.Delete:    DeleteEdge(tr); break;
                default:                  SelectTransition(tr); break;
            }
        }

        // Called when LMB pressed in empty canvas space (handled in HandleGraphInput).
        private void OnEmptyCanvasClicked(Vector2 contentLocalPos)
        {
            if (_graphTool == GraphTool.AddNode)
                AddNodeAt(contentLocalPos);
            else if (_pendingConnectFrom != null)
            {
                _pendingConnectFrom = null;
                if (_statusTmp != null) _statusTmp.text = "Cancelled.";
            }
        }

        // â”€â”€ Tool implementations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void AddNodeAt(Vector2 contentLocalPos)
        {
            if (_selectedSet == null) return;
            var ids = CollectAllStateIds(_selectedSet);
            string newId = NewId("state", ids);
            var raw = new Dictionary<string, object>
            {
                { "id", newId }, { "label", newId }, { "class", "" },
                { "props", new Dictionary<string, object>() }, { "terminal", false },
                { "x", (long)Mathf.RoundToInt(contentLocalPos.x) },
                { "y", (long)Mathf.RoundToInt(-contentLocalPos.y) },
            };
            var node = new FSMStateNode
            {
                raw = raw, id = newId, label = newId, stateClass = "",
                x = contentLocalPos.x, y = -contentLocalPos.y, w = 120f, h = 60f,
            };
            _selectedSet.states.Add(node);
            PersistSets();
            _selectedState = node;
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Added node '{newId}'";
        }

        private void CloneNode(FSMStateNode src)
        {
            if (_selectedSet == null || src == null) return;
            string newId = NewId("state", CollectAllStateIds(_selectedSet));
            var rawCopy = MiniJsonHelpersWrap.Deserialize(MiniJsonHelpersWrap.Serialize(src.raw))
                          as Dictionary<string, object>;
            if (rawCopy == null) return;
            rawCopy["id"]    = newId;
            rawCopy["label"] = src.label + " (copy)";
            rawCopy["x"]     = (long)Mathf.RoundToInt(src.x + 20f);
            rawCopy["y"]     = (long)Mathf.RoundToInt(src.y + 20f);
            var node = new FSMStateNode
            {
                raw = rawCopy, id = newId, label = src.label + " (copy)",
                stateClass = src.stateClass, x = src.x + 20f, y = src.y + 20f,
                w = src.w, h = src.h,
            };
            _selectedSet.states.Add(node);
            PersistSets();
            _selectedState = node;
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Cloned â†’ '{newId}'";
        }

        private void DeleteNode(FSMStateNode node)
        {
            if (_selectedSet == null || node == null) return;
            // Cascade: remove all incoming/outgoing transitions.
            _selectedSet.transitions.RemoveAll(t => t.from == node.id || t.to == node.id);
            _selectedSet.states.Remove(node);
            if (_selectedState == node) _selectedState = null;
            if (_selectedSet.initial == node.id)
                _selectedSet.initial = _selectedSet.states.Count > 0 ? _selectedSet.states[0].id : null;
            PersistSets();
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Deleted node '{node.id}'";
        }

        private void DeleteEdge(FSMTransitionData tr)
        {
            if (_selectedSet == null || tr == null) return;
            _selectedSet.transitions.Remove(tr);
            if (_selectedTransition == tr) _selectedTransition = null;
            PersistSets();
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Deleted edge {tr.from}â†’{tr.to}";
        }

        private void HandleConnectClick(FSMStateNode node, bool isConnect)
        {
            if (_selectedSet == null) return;
            if (_pendingConnectFrom == null)
            {
                _pendingConnectFrom = node.id;
                if (_statusTmp != null)
                    _statusTmp.text = (isConnect ? "Connect: pick TARGET node" : "Disconnect: pick TARGET node") + $" (from '{node.id}')";
                return;
            }
            string from = _pendingConnectFrom;
            string to   = node.id;
            _pendingConnectFrom = null;
            if (from == to)
            {
                if (_statusTmp != null) _statusTmp.text = "Cancelled (same node).";
                return;
            }

            if (isConnect)
            {
                var trIds = CollectAllTransitionIds(_selectedSet);
                var newTrId = NewTrId(trIds);
                var raw = new Dictionary<string, object>
                {
                    { "id", newTrId }, { "from", from }, { "to", to },
                    { "when", "" }, { "event", "" },
                    { "priority", 0L }, { "cooldown_frames", 0L },
                    { "actions", new List<object>() },
                };
                var tr = new FSMTransitionData
                {
                    raw = raw, id = newTrId, from = from, to = to,
                    whenEvent = "", label = "",
                };
                _selectedSet.transitions.Add(tr);
                _selectedTransition = tr;
                if (_statusTmp != null) _statusTmp.text = $"Connected {from}â†’{to}";
            }
            else
            {
                int n = _selectedSet.transitions.RemoveAll(t => t.from == from && t.to == to);
                if (_statusTmp != null) _statusTmp.text = $"Disconnected {from}â†’{to} ({n} edges)";
            }
            PersistSets();
            RefreshGraph();
            RefreshProperties();
        }

        private void MarkInitial(FSMStateNode node)
        {
            if (_selectedSet == null || node == null) return;
            _selectedSet.initial = node.id;
            foreach (var s in _selectedSet.states)
                s.isInitial = (s.id == node.id);
            PersistSets();
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Initial = '{node.id}'";
        }

        private void ToggleTerminal(FSMStateNode node)
        {
            if (node == null) return;
            node.isTerminal = !node.isTerminal;
            PersistSets();
            RefreshGraph();
            RefreshProperties();
            if (_statusTmp != null) _statusTmp.text = $"Terminal '{node.id}' = {node.isTerminal}";
        }

        // â”€â”€ Empty-canvas hit testing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Returns true if the screen-space mouse position currently lies inside
        /// the graph canvas but not over any node visual.
        /// </summary>
        private bool TryGetEmptyCanvasContentPos(out Vector2 contentLocal)
        {
            contentLocal = Vector2.zero;
            if (_graphContent == null) return false;
            var canvasArea = _graphContent.parent as RectTransform;
            if (canvasArea == null) return false;
            var mouse = Mouse.current; if (mouse == null) return false;
            Vector2 screen = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            // Mouse over scrollable canvas area (not toolbar)?
            if (!RectTransformUtility.RectangleContainsScreenPoint(canvasArea, screen, _canvas.worldCamera))
                return false;
            // Skip if over a node
            foreach (var kv in _nodeRects)
            {
                if (kv.Value == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(kv.Value, screen, _canvas.worldCamera))
                    return false;
            }
            // Convert to graph-content local coords
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _graphContent, screen, _canvas.worldCamera, out contentLocal);
        }
    }
}
