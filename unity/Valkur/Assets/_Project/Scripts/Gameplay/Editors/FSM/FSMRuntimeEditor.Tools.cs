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
        /// <summary>
        /// Wildcard "from" — the value <c>NormalizeSets</c> folds into
        /// <c>tr["global"] = true</c> and <c>FSMTransition.IsGlobal</c> already honours at
        /// runtime. Also the id of the permanent "Any State" pseudo-node the graph always
        /// renders (mirrors Unity's own Animator window "Any State" node) so a global edge
        /// is both authorable and visible.
        /// </summary>
        private const string GLOBAL_NODE_ID = "*";

        // ── Per-element click dispatch (called from CreateNodeVisual / CreateEdgeVisual) ──

        private void OnNodeClicked(FSMStateNode state)
        {
            switch (_graphTool)
            {
                case GraphTool.Select:        SelectState(state); break;
                case GraphTool.AddNode:       /* AddNode acts on empty space */ SelectState(state); break;
                case GraphTool.CloneNode:     CloneNode(state); break;
                case GraphTool.Delete:        DeleteNode(state); break;
                case GraphTool.Connect:       HandleConnectClickFrom(state.id, isConnect: true); break;
                case GraphTool.Disconnect:    HandleConnectClickFrom(state.id, isConnect: false); break;
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

        /// <summary>
        /// Click dispatch for the permanent "Any State" pseudo-node (see
        /// <see cref="CreateAnyStateNodeVisual"/>). It is not a real <see cref="FSMStateNode"/>
        /// — it cannot be selected, cloned, deleted or marked initial/terminal — so only the
        /// tools that make sense for a wildcard source are wired.
        /// </summary>
        private void OnAnyStateNodeClicked()
        {
            switch (_graphTool)
            {
                case GraphTool.Connect:    HandleConnectClickFrom(GLOBAL_NODE_ID, isConnect: true);  break;
                case GraphTool.Disconnect: HandleConnectClickFrom(GLOBAL_NODE_ID, isConnect: false); break;
                default:
                    if (_statusTmp != null)
                        _statusTmp.text = "Any State (*) — the wildcard source FSMTransition.IsGlobal " +
                                           "reads. Switch to Connect/Disconnect to wire an edge from it.";
                    break;
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

        // ── Tool implementations ────────────────────────────────────────────────

        private void AddNodeAt(Vector2 contentLocalPos)
        {
            if (_selectedSet == null) return;

            // Convert from _graphContent's own local frame (pivot (0.5,0.5), origin at the
            // canvas CENTRE — what ScreenPointToLocalPointInRectangle returns) into the
            // frame every node visual is actually positioned in (anchored top-left, Y+
            // down — CreateNodeVisual's `anchoredPosition = (state.x, -state.y)`). Without
            // this conversion a click at the middle of the canvas stored its centre-relative
            // coordinates straight into state.x/y and a top-left-anchored node rendered them
            // pinned to the corner.
            var nodeCoords = ContentLocalToNodeCoords(contentLocalPos);

            var ids = CollectAllStateIds(_selectedSet);
            string newId = NewId("state", ids);
            var raw = new Dictionary<string, object>
            {
                { "id", newId }, { "label", newId }, { "class", "" },
                { "props", new Dictionary<string, object>() }, { "terminal", false },
                { "x", (long)Mathf.RoundToInt(nodeCoords.x) },
                { "y", (long)Mathf.RoundToInt(nodeCoords.y) },
            };
            var node = new FSMStateNode
            {
                raw = raw, id = newId, label = newId, stateClass = "",
                x = nodeCoords.x, y = nodeCoords.y, w = 120f, h = 60f,
            };
            var set = _selectedSet;

            _undo.Do($"Add node '{newId}'",
                doAction: () =>
                {
                    set.states.Add(node);
                    PersistSets();
                    _selectedState = node;
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    set.states.Remove(node);
                    if (_selectedState == node) _selectedState = null;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null)
                _statusTmp.text = $"Added node '{newId}' at ({nodeCoords.x:F0}, {nodeCoords.y:F0})";
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
            var set = _selectedSet;

            _undo.Do($"Clone node → '{newId}'",
                doAction: () =>
                {
                    set.states.Add(node);
                    PersistSets();
                    _selectedState = node;
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    set.states.Remove(node);
                    if (_selectedState == node) _selectedState = null;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null) _statusTmp.text = $"Cloned → '{newId}'";
        }

        private void DeleteNode(FSMStateNode node)
        {
            if (_selectedSet == null || node == null) return;
            var set = _selectedSet;

            // Cascade: capture every incoming/outgoing transition so Undo can restore them
            // exactly, not just the node.
            var removedTransitions = set.transitions.Where(t => t.from == node.id || t.to == node.id).ToList();
            int nodeIndex = set.states.IndexOf(node);
            string prevInitial = set.initial;
            bool wasSelected = ReferenceEquals(_selectedState, node);

            _undo.Do($"Delete node '{node.id}'",
                doAction: () =>
                {
                    foreach (var t in removedTransitions) set.transitions.Remove(t);
                    set.states.Remove(node);
                    if (ReferenceEquals(_selectedState, node)) _selectedState = null;
                    if (set.initial == node.id)
                        set.initial = set.states.Count > 0 ? set.states[0].id : null;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    if (nodeIndex >= 0 && nodeIndex <= set.states.Count) set.states.Insert(nodeIndex, node);
                    else set.states.Add(node);
                    foreach (var t in removedTransitions)
                        if (!set.transitions.Contains(t)) set.transitions.Add(t);
                    set.initial = prevInitial;
                    if (wasSelected) _selectedState = node;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null) _statusTmp.text = $"Deleted node '{node.id}'";
        }

        private void DeleteEdge(FSMTransitionData tr)
        {
            if (_selectedSet == null || tr == null) return;
            var set = _selectedSet;
            int idx = set.transitions.IndexOf(tr);
            bool wasSelected = ReferenceEquals(_selectedTransition, tr);

            _undo.Do($"Delete edge {tr.from}→{tr.to}",
                doAction: () =>
                {
                    set.transitions.Remove(tr);
                    if (ReferenceEquals(_selectedTransition, tr)) _selectedTransition = null;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    if (idx >= 0 && idx <= set.transitions.Count) set.transitions.Insert(idx, tr);
                    else set.transitions.Add(tr);
                    if (wasSelected) _selectedTransition = tr;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null) _statusTmp.text = $"Deleted edge {tr.from}→{tr.to}";
        }

        /// <summary>
        /// Second half of the Connect/Disconnect tools. <paramref name="id"/> is either a
        /// real state id (from <see cref="OnNodeClicked"/>) or <see cref="GLOBAL_NODE_ID"/>
        /// (from <see cref="OnAnyStateNodeClicked"/>) — both are handled identically since a
        /// global edge is just a transition whose "from" happens to be "*"
        /// (<c>NormalizeSets</c> already folds that into <c>tr["global"] = true</c> and
        /// <c>FSMTransition.IsGlobal</c> already reads it at runtime).
        /// </summary>
        private void HandleConnectClickFrom(string id, bool isConnect)
        {
            if (_selectedSet == null) return;
            if (_pendingConnectFrom == null)
            {
                _pendingConnectFrom = id;
                string fromLabel = id == GLOBAL_NODE_ID ? "ANY STATE (*)" : $"'{id}'";
                if (_statusTmp != null)
                    _statusTmp.text = (isConnect ? "Connect: pick TARGET node" : "Disconnect: pick TARGET node")
                                       + $" (from {fromLabel})";
                return;
            }
            string from = _pendingConnectFrom;
            string to   = id;
            _pendingConnectFrom = null;

            if (from == to)
            {
                // A self-transition can never fire: StateMachine.TryTakeAuthoredTransition
                // skips any edge whose To equals the CURRENT state unconditionally — being
                // "applicable" already requires being IN that exact state, so the edge is
                // dead by construction, not merely low-value. The Any State node is the
                // supported way to wire a transition that fires from every OTHER state.
                if (_statusTmp != null)
                    _statusTmp.text = $"Cancelled — a self-transition ('{from}' → '{from}') can never fire " +
                                       "(StateMachine.TryTakeAuthoredTransition skips To == current state). " +
                                       "Use the Any State (*) node instead.";
                return;
            }

            var set = _selectedSet;

            if (isConnect)
            {
                var trIds = CollectAllTransitionIds(set);
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

                _undo.Do($"Connect {from}→{to}",
                    doAction: () =>
                    {
                        set.transitions.Add(tr);
                        _selectedTransition = tr;
                        PersistSets();
                        RefreshGraph();
                        RefreshProperties();
                    },
                    undoAction: () =>
                    {
                        set.transitions.Remove(tr);
                        if (ReferenceEquals(_selectedTransition, tr)) _selectedTransition = null;
                        PersistSets();
                        RefreshGraph();
                        RefreshProperties();
                    });
                // A fresh edge carries no guard key: FSMCondition.Parse("") returns null
                // and StateMachine treats a null condition as PASS, so the natural gesture
                // "draw the arrow, then type the guard" would otherwise ship an edge that
                // fires on its first eligible frame with no hint that it did. Creation is
                // deliberately NOT blocked — an unconditional edge is legitimate — only the
                // surprise is removed.
                if (_statusTmp != null)
                    _statusTmp.text = $"Connected {from}→{to} — UNCONDITIONAL until a condition is " +
                                       "typed: an edge with no guard fires on its first eligible " +
                                       $"frame in '{from}'. Add one in the Transition tab, or leave " +
                                       "it empty on purpose.";
            }
            else
            {
                var removed = set.transitions.Where(t => t.from == from && t.to == to).ToList();

                _undo.Do($"Disconnect {from}→{to}",
                    doAction: () =>
                    {
                        foreach (var t in removed) set.transitions.Remove(t);
                        if (_selectedTransition != null && removed.Contains(_selectedTransition))
                            _selectedTransition = null;
                        PersistSets();
                        RefreshGraph();
                        RefreshProperties();
                    },
                    undoAction: () =>
                    {
                        foreach (var t in removed)
                            if (!set.transitions.Contains(t)) set.transitions.Add(t);
                        PersistSets();
                        RefreshGraph();
                        RefreshProperties();
                    });
                if (_statusTmp != null) _statusTmp.text = $"Disconnected {from}→{to} ({removed.Count} edges)";
            }
        }

        private void MarkInitial(FSMStateNode node)
        {
            if (_selectedSet == null || node == null) return;
            var set = _selectedSet;
            string prevInitial = set.initial;
            string newInitial = node.id;
            if (prevInitial == newInitial) return;

            _undo.Do($"Mark initial = '{node.id}'",
                doAction: () =>
                {
                    set.initial = newInitial;
                    foreach (var s in set.states) s.isInitial = (s.id == newInitial);
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    set.initial = prevInitial;
                    foreach (var s in set.states) s.isInitial = (s.id == prevInitial);
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null) _statusTmp.text = $"Initial = '{node.id}'";
        }

        private void ToggleTerminal(FSMStateNode node)
        {
            if (node == null) return;
            bool newValue = !node.isTerminal;

            _undo.Do($"{(newValue ? "Mark" : "Unmark")} terminal '{node.id}'",
                doAction: () =>
                {
                    node.isTerminal = newValue;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    node.isTerminal = !newValue;
                    PersistSets();
                    RefreshGraph();
                    RefreshProperties();
                });
            if (_statusTmp != null) _statusTmp.text = $"Terminal '{node.id}' = {newValue}";
        }

        // ── Coordinate-frame conversion ─────────────────────────────────────────

        /// <summary>
        /// Converts a point from <c>_graphContent</c>'s own local space — pivot (0.5,0.5),
        /// origin at the canvas CENTRE, Y+ up (what
        /// <c>RectTransformUtility.ScreenPointToLocalPointInRectangle(_graphContent, …)</c>
        /// returns, see <see cref="TryGetEmptyCanvasContentPos"/>) — into the frame every
        /// node visual is actually positioned in: anchored at the content rect's TOP-LEFT
        /// corner with Y+ DOWN (<see cref="FSMStateNode.x"/>/<see cref="FSMStateNode.y"/>,
        /// consumed as <c>anchoredPosition = (state.x, -state.y)</c> in
        /// <c>CreateNodeVisual</c>). The two frames disagree both in ORIGIN (centre vs
        /// corner) and in Y SIGN (up vs down) — <c>AddNodeAt</c> used to store the raw
        /// centre-relative click straight into state.x/y, so clicking the middle of the
        /// canvas created a node pinned to the corner.
        /// </summary>
        private Vector2 ContentLocalToNodeCoords(Vector2 contentLocal)
        {
            float w = _graphContent != null ? _graphContent.rect.width  : 0f;
            float h = _graphContent != null ? _graphContent.rect.height : 0f;
            return new Vector2(contentLocal.x + w * 0.5f, h * 0.5f - contentLocal.y);
        }

        // ── Empty-canvas hit testing ─────────────────────────────────────────────

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
