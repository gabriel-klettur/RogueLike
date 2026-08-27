using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        /// <summary>
        /// Whether the code-owned edges are drawn. ON by default: hiding them by default
        /// would reproduce exactly the state this feature exists to end, where the panel
        /// showed three edges of a machine that has more than twenty. The toggle is for
        /// decluttering while authoring, not for choosing which truth to believe.
        /// </summary>
        private bool _showBuiltInEdges = true;

        /// <summary>Desaturated blue, thinner and dimmer than an authored edge's grey.
        /// The distinction has to survive being glanced at, not just read.</summary>
        private static readonly Color BUILTIN_EDGE_COLOR       = new Color(0.35f, 0.45f, 0.62f, 0.55f);
        private static readonly Color BUILTIN_EDGE_LABEL_COLOR = new Color(0.45f, 0.58f, 0.75f, 0.75f);


        private void RefreshGraph()
        {
            // Clear old nodes/edges. SafeDestroy, not a raw Destroy: this refresh runs from
            // EditMode tests too (round-trip / undo coverage), and plain Object.Destroy is a
            // silent no-op outside Play Mode that logs "Destroy may not be called from edit
            // mode!" — the same class of bug CLAUDE.md documents for the F5 picker refresh
            // and Projectile's un-pooled expire.
            foreach (var kv in _nodeRects)
                if (kv.Value != null) SafeDestroy.GameObjectOf(kv.Value);
            _nodeRects.Clear();
            foreach (var e in _edgeObjects)
                SafeDestroy.Of(e);
            _edgeObjects.Clear();

            if (_selectedSet == null)
            {
                _graphInfoTmp.gameObject.SetActive(true);
                return;
            }
            _graphInfoTmp.gameObject.SetActive(false);

            // Permanent "Any State" pseudo-node — mirrors Unity's own Animator window,
            // which has exactly this: a fixed, always-present anchor you drag transitions
            // FROM. Registering it in _nodeRects under GLOBAL_NODE_ID ("*") is enough for
            // CreateEdgeVisual below to draw any global edge without special-casing —
            // before this, `NormalizeSets`'s wildcard "from": "*" (used by Monster_Default's
            // authored retaliation transition) resolved to no node and the edge was silently
            // never drawn.
            _nodeRects[GLOBAL_NODE_ID] = CreateAnyStateNodeVisual();

            // Draw nodes
            foreach (var state in _selectedSet.states)
            {
                var node = CreateNodeVisual(state);
                _nodeRects[state.id] = node;
            }

            // Built-in edges FIRST, so an authored edge that shadows one draws on top of it.
            // These are the transitions the state classes take on their own — see
            // FSMBuiltInTransitions for why they are drawn at all. They are read-only: the
            // graph is a picture of the whole machine, but only the authored half answers to
            // this editor, and the styling is what says which is which.
            if (_showBuiltInEdges)
            {
                var stateIds = new HashSet<string>();
                foreach (var st in _selectedSet.states) stateIds.Add(st.id);
                foreach (var edge in FSMBuiltInTransitions.ForStates(stateIds))
                    CreateBuiltInEdgeVisual(edge);
            }

            // Draw edges
            foreach (var trans in _selectedSet.transitions)
            {
                CreateEdgeVisual(trans);
            }

            ApplyZoomPan();
        }

        /// <summary>
        /// Flips the code-owned edges on or off and repaints. Also refreshes the button
        /// caption, because a toggle whose label never changes is indistinguishable from a
        /// toggle that did nothing.
        /// </summary>
        private void ToggleBuiltInEdges()
        {
            _showBuiltInEdges = !_showBuiltInEdges;
            RefreshBuiltInButtonLabel();
            RefreshGraph();
            if (_statusTmp != null)
            {
                _statusTmp.text = _showBuiltInEdges
                    ? $"Showing {FSMBuiltInTransitions.All.Count} built-in edges — the transitions the state classes take on their own."
                    : "Built-in edges hidden — the graph now shows ONLY what this editor owns.";
            }
        }

        /// <summary>Caption states what the button will do next, not what is true now.</summary>
        private void RefreshBuiltInButtonLabel()
        {
            if (_uiRefs.BuiltInBtnTmp == null) return;
            _uiRefs.BuiltInBtnTmp.text = _showBuiltInEdges ? "Hide Built-in" : "Show Built-in";
        }

        /// <summary>
        /// Draws one code-owned edge. Deliberately quieter than an authored edge — thinner
        /// line, desaturated blue, italic label — because the two are not the same kind of
        /// thing and a graph that drew them identically would trade one lie for another.
        /// Clicking reports the condition in the status line; it never selects, because
        /// there is nothing here to edit or delete.
        /// </summary>
        private void CreateBuiltInEdgeVisual(FSMBuiltInEdge edge)
        {
            if (!_nodeRects.TryGetValue(edge.From, out var fromRect)) return;
            if (!_nodeRects.TryGetValue(edge.To, out var toRect)) return;

            var lineGo = new GameObject($"BuiltInEdge_{edge.From}_{edge.To}",
                                        typeof(RectTransform), typeof(Image));
            lineGo.transform.SetParent(_graphContent, false);
            lineGo.transform.SetAsFirstSibling(); // behind nodes AND behind authored edges

            var fromCenter = fromRect.anchoredPosition + fromRect.sizeDelta * new Vector2(0.5f, -0.5f);
            var toCenter   = toRect.anchoredPosition   + toRect.sizeDelta   * new Vector2(0.5f, -0.5f);

            var diff  = toCenter - fromCenter;
            float dist = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            var rt = lineGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = fromCenter;
            rt.sizeDelta = new Vector2(dist, 1f);
            rt.localRotation = Quaternion.Euler(0, 0, angle);
            lineGo.GetComponent<Image>().color = BUILTIN_EDGE_COLOR;

            var labelGo = new GameObject("BuiltInEdgeLabel",
                                         typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(_graphContent, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            // Offset opposite the authored labels' +10 so a built-in edge and an authored
            // edge between the same pair do not print their captions on top of each other.
            lrt.anchoredPosition = (fromCenter + toCenter) * 0.5f + new Vector2(0f, -10f);
            lrt.sizeDelta = new Vector2(120, 16);

            var lbl = labelGo.GetComponent<TextMeshProUGUI>();
            lbl.text = edge.Label;
            lbl.fontSize = 8f;
            lbl.fontStyle = FontStyles.Italic;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.color = BUILTIN_EDGE_LABEL_COLOR;
            lbl.enableWordWrapping = false;
            lbl.overflowMode = TextOverflowModes.Truncate;

            var btn = labelGo.AddComponent<Button>();
            btn.onClick.AddListener(() => OnBuiltInEdgeClicked(edge));

            _edgeObjects.Add(lineGo);
            _edgeObjects.Add(labelGo);
        }

        /// <summary>
        /// A built-in edge cannot be selected, retargeted or deleted, so a click explains it
        /// instead of pretending to act. Saying WHERE it lives is the point: the next
        /// question after "why did my monster do that" is "where do I change it".
        /// </summary>
        private void OnBuiltInEdgeClicked(FSMBuiltInEdge edge)
        {
            if (_statusTmp == null) return;
            _statusTmp.text =
                $"BUILT-IN {edge.From} → {edge.To}: {edge.Label}. Not editable here — " +
                $"owned by {edge.SourceFile}.";
        }

        /// <summary>
        /// Fixed, non-deletable marker for the wildcard transition source ("*"). Pinned to
        /// the left of the normal auto-layout grid (which starts at x≈40) so it never
        /// overlaps a real node; pans/zooms with the rest of the content but does not drag.
        /// Click dispatch is <see cref="OnAnyStateNodeClicked"/>, not
        /// <see cref="OnNodeClicked"/> — there is no backing <see cref="FSMStateNode"/>.
        /// </summary>
        private RectTransform CreateAnyStateNodeVisual()
        {
            const float w = 110f, h = 44f;
            var go = new GameObject("Node_AnyState", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_graphContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-200f, 40f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.42f, 0.32f, 0.08f, 0.95f); // amber — distinct from any real state colour

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4, 2); lrt.offsetMax = new Vector2(-4, -2);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "<b>Any State</b>\n<size=8>(*) — Connect tool</size>";
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(OnAnyStateNodeClicked);

            return rt;
        }

        private RectTransform CreateNodeVisual(FSMStateNode state)
        {
            float w = state.w > 0 ? state.w : 100f;
            float h = state.h > 0 ? state.h : 50f;

            var go = new GameObject($"Node_{state.id}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_graphContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); // top-left anchor
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(state.x, -state.y);

            var img = go.GetComponent<Image>();
            bool isSelected = _selectedState != null && _selectedState.id == state.id;
            bool isInitial = state.isInitial || (_selectedSet != null && _selectedSet.initial == state.id);
            bool isTerminal = state.isTerminal;

            if (isSelected)
                img.color = EditorUIHelpers.BTN_ACTIVE;
            else if (isInitial)
                img.color = new Color(0.2f, 0.5f, 0.2f, 0.9f);
            else if (isTerminal)
                img.color = new Color(0.55f, 0.15f, 0.15f, 0.9f);
            else
                img.color = new Color(0.15f, 0.18f, 0.22f, 0.9f);

            // Label
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4, 2); lrt.offsetMax = new Vector2(-4, -2);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = $"<b>{state.label ?? state.id}</b>\n<size=9>{state.stateClass}</size>";
            tmp.fontSize = 11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;

            // Click handler — dispatched via current GraphTool
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => OnNodeClicked(state));

            return rt;
        }

        private void CreateEdgeVisual(FSMTransitionData trans)
        {
            if (!_nodeRects.TryGetValue(trans.from, out var fromRect)) return;
            if (!_nodeRects.TryGetValue(trans.to, out var toRect)) return;

            // Simple line between node centres using a stretched image
            var lineGo = new GameObject($"Edge_{trans.id}", typeof(RectTransform), typeof(Image));
            lineGo.transform.SetParent(_graphContent, false);
            lineGo.transform.SetAsFirstSibling(); // Behind nodes

            var fromCenter = fromRect.anchoredPosition + fromRect.sizeDelta * new Vector2(0.5f, -0.5f);
            var toCenter = toRect.anchoredPosition + toRect.sizeDelta * new Vector2(0.5f, -0.5f);

            var diff = toCenter - fromCenter;
            float dist = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            var rt = lineGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = fromCenter;
            rt.sizeDelta = new Vector2(dist, 2f);
            rt.localRotation = Quaternion.Euler(0, 0, angle);

            bool isSelected = _selectedTransition != null && _selectedTransition.id == trans.id;
            lineGo.GetComponent<Image>().color = isSelected
                ? EditorUIHelpers.ACCENT
                : new Color(0.5f, 0.5f, 0.5f, 0.7f);

            // Edge label
            var labelGo = new GameObject("EdgeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(_graphContent, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            var midpoint = (fromCenter + toCenter) * 0.5f + new Vector2(0, 10f);
            lrt.anchoredPosition = midpoint;
            lrt.sizeDelta = new Vector2(100, 18);
            var lbl = labelGo.GetComponent<TextMeshProUGUI>();
            lbl.text = trans.label ?? trans.whenEvent ?? "";
            lbl.fontSize = 9f;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.color = new Color(0.7f, 0.7f, 0.5f, 0.9f);
            lbl.enableWordWrapping = false;
            lbl.overflowMode = TextOverflowModes.Truncate;

            // Click handler on edge label — dispatched via current GraphTool
            var edgeBtnGo = labelGo;
            var edgeBtn = edgeBtnGo.AddComponent<Button>();
            edgeBtn.onClick.AddListener(() => OnEdgeClicked(trans));

            _edgeObjects.Add(lineGo);
            _edgeObjects.Add(labelGo);
        }

    }
}