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
            SetStatus(_showBuiltInEdges
                ? $"Showing {FSMBuiltInTransitions.All.Count} built-in edges — the transitions the state classes take on their own."
                : "Built-in edges hidden — the graph now shows ONLY what this editor owns.");
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

            // Built-in edges are also directed.
            CreateArrowhead(fromCenter, toCenter, toRect, angle, BUILTIN_EDGE_COLOR);
        }

        /// <summary>
        /// A built-in edge cannot be selected, retargeted or deleted, so a click explains it
        /// instead of pretending to act. Saying WHERE it lives is the point: the next
        /// question after "why did my monster do that" is "where do I change it".
        /// </summary>
        private void OnBuiltInEdgeClicked(FSMBuiltInEdge edge)
        {
            SetStatus(
                $"BUILT-IN {edge.From} → {edge.To}: {edge.Label}. Not editable here — " +
                $"owned by {edge.SourceFile}.");
        }

        /// <summary>Height of a node's coloured header band, in graph-content units. The
        /// label reserves exactly this much at the top, so the two cannot drift apart.</summary>
        private const float NODE_HEADER_H = 14f;

        /// <summary>Gap between the header band and the first line of the label.</summary>
        private const float NODE_LABEL_PAD = 2f;

        /// <summary>
        /// Shared chrome for every node in the graph: body panel, coloured header band and a
        /// 1 px border. Both node kinds (real states and the wildcard "*" marker) build on
        /// this so their silhouettes stay identical and only the palette differs — the header
        /// is what separates two adjacent nodes at a glance when the graph is zoomed out.
        /// The border is a uGUI <see cref="Outline"/>, i.e. an offset copy of the quad; the
        /// body is an untextured <see cref="Image"/>, so the corners are square.
        /// Callers add the label, the port dots and the click handler on top.
        /// </summary>
        private GameObject BuildNodeBase(string nodeName, Transform parent,
            float w, float h, Vector2 anchoredPos, Color bodyColor, Color headerColor,
            Color outlineColor)
        {
            var go = new GameObject(nodeName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = anchoredPos;

            // Body — the only raycast target on the node; the header, label and ports are
            // all transparent to clicks so the Button below receives every hit.
            go.GetComponent<Image>().color = bodyColor;

            var ol = go.AddComponent<Outline>();
            ol.effectColor = outlineColor;
            ol.effectDistance = new Vector2(1.2f, -1.2f);

            // Header band, pinned to the top edge and stretched across the full width.
            var hdrGo = new GameObject("Header", typeof(RectTransform), typeof(Image));
            hdrGo.transform.SetParent(go.transform, false);
            var hrt = hdrGo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = Vector2.zero;
            hrt.sizeDelta = new Vector2(0f, NODE_HEADER_H);
            var hdrImg = hdrGo.GetComponent<Image>();
            hdrImg.color = headerColor;
            hdrImg.raycastTarget = false;

            return go;
        }

        /// <summary>
        /// Fixed, non-deletable marker for the wildcard transition source ("*"). Pinned to
        /// the left of the normal auto-layout grid (which starts at x≈40) so it never
        /// overlaps a real node; pans/zooms with the rest of the content but does not drag.
        /// Click dispatch is <see cref="OnAnyStateNodeClicked"/>, not
        /// <see cref="OnNodeClicked"/> — there is no backing <see cref="FSMStateNode"/>.
        /// It carries an OUTPUT port only: "*" is a transition source and nothing can ever
        /// target it.
        /// </summary>
        private RectTransform CreateAnyStateNodeVisual()
        {
            const float w = 120f, h = 48f;
            var anchoredPos = new Vector2(-200f, 40f);

            var go = BuildNodeBase("Node_AnyState", _graphContent,
                w, h, anchoredPos,
                new Color(0.35f, 0.26f, 0.06f, 0.95f),  // body: dark amber — no real state uses it
                new Color(0.55f, 0.42f, 0.10f, 1f),     // header: the same amber, lifted
                new Color(0.70f, 0.55f, 0.20f, 0.6f));

            var rt = go.GetComponent<RectTransform>();

            AddNodeLabel(go, "<b>Any State</b>\n<size=8>(*) — Connect tool</size>", 10f);
            AddPortIndicator(go.transform, isInput: false);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(OnAnyStateNodeClicked);

            return rt;
        }

        private RectTransform CreateNodeVisual(FSMStateNode state)
        {
            float w = state.w > 0 ? state.w : 120f;
            float h = state.h > 0 ? state.h : 52f;

            bool isSelected = _selectedState != null && _selectedState.id == state.id;
            bool isInitial = state.isInitial || (_selectedSet != null && _selectedSet.initial == state.id);
            bool isTerminal = state.isTerminal;

            // Palette by role — selected wins over initial wins over terminal, the same
            // precedence the flat-colour version used.
            Color bodyColor, headerColor, outlineColor;

            if (isSelected)
            {
                bodyColor = EditorUIHelpers.BTN_ACTIVE;
                headerColor = new Color(0.35f, 0.55f, 0.75f, 1f);
                outlineColor = EditorUIHelpers.ACCENT;
            }
            else if (isInitial)
            {
                bodyColor = new Color(0.18f, 0.35f, 0.18f, 0.92f);
                headerColor = new Color(0.25f, 0.55f, 0.25f, 1f);
                outlineColor = new Color(0.3f, 0.7f, 0.3f, 0.6f);
            }
            else if (isTerminal)
            {
                bodyColor = new Color(0.40f, 0.12f, 0.12f, 0.92f);
                headerColor = new Color(0.60f, 0.18f, 0.18f, 1f);
                outlineColor = new Color(0.7f, 0.3f, 0.3f, 0.6f);
            }
            else
            {
                bodyColor = new Color(0.12f, 0.14f, 0.17f, 0.92f);
                headerColor = new Color(0.18f, 0.22f, 0.28f, 1f);
                outlineColor = new Color(0.3f, 0.35f, 0.4f, 0.5f);
            }

            var anchoredPos = new Vector2(state.x, -state.y);
            var go = BuildNodeBase($"Node_{state.id}", _graphContent,
                w, h, anchoredPos, bodyColor, headerColor, outlineColor);

            var rt = go.GetComponent<RectTransform>();

            AddNodeLabel(go, $"<b>{state.label ?? state.id}</b>\n<size=9>{state.stateClass}</size>", 11f);

            // A real state is both a target and a source, so it shows both dots.
            AddPortIndicator(go.transform, isInput: true);
            AddPortIndicator(go.transform, isInput: false);

            // Click handler — dispatched via current GraphTool
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => OnNodeClicked(state));

            return rt;
        }

        /// <summary>
        /// Node caption, stretched over the body minus the header band so a long state name
        /// truncates instead of running under the coloured stripe. Never a raycast target:
        /// the body Image is the click surface for the Button the caller adds.
        /// </summary>
        private static void AddNodeLabel(GameObject node, string richText, float fontSize)
        {
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(node.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(6f, 4f);
            lrt.offsetMax = new Vector2(-6f, -(NODE_HEADER_H + NODE_LABEL_PAD));

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = richText;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// Small dot on the left (input) or right (output) edge, marking where the edge
        /// lines meet the node. PURELY decorative: connections are still made by clicking
        /// the node body with the Connect tool, and the dot is not a raycast target, so it
        /// never steals that click. <see cref="CreateEdgeVisual"/> anchors its lines on the
        /// node rects, not on these — they are a reading aid, not geometry.
        /// </summary>
        private void AddPortIndicator(Transform nodeTransform, bool isInput)
        {
            const float PORT_SIZE = 8f;
            var portGo = new GameObject(isInput ? "InputPort" : "OutputPort",
                typeof(RectTransform), typeof(Image));
            portGo.transform.SetParent(nodeTransform, false);
            var prt = portGo.GetComponent<RectTransform>();
            var edge = new Vector2(isInput ? 0f : 1f, 0.5f);
            prt.anchorMin = prt.anchorMax = prt.pivot = edge;
            // Half the dot hangs outside the body, so it reads as a port and not as a
            // rectangle painted on the panel.
            prt.anchoredPosition = new Vector2(isInput ? -PORT_SIZE * 0.5f : PORT_SIZE * 0.5f, 0f);
            prt.sizeDelta = new Vector2(PORT_SIZE, PORT_SIZE);

            var pImg = portGo.GetComponent<Image>();
            pImg.color = new Color(0.5f, 0.6f, 0.7f, 0.6f);
            pImg.raycastTarget = false;
        }

        /// <summary>
        /// Directional arrowhead drawn at the &quot;to&quot; end of a directed edge.
        /// Uses two short Image segments (same technique as the edge line itself)
        /// angled ±30° from the edge direction to form a V. No TextMeshProUGUI,
        /// no font glyphs, no runtime-surprise dependencies — just rotated rects
        /// like the edge lines that already work.
        /// </summary>
        private void CreateArrowhead(Vector2 fromCenter, Vector2 toCenter,
            RectTransform toRect, float edgeAngleDeg, Color color)
        {
            var dir = (toCenter - fromCenter).normalized;
            if (dir.sqrMagnitude < 0.0001f) return;

            float halfW = toRect.sizeDelta.x * 0.5f;
            float halfH = toRect.sizeDelta.y * 0.5f;

            // Distance from toCenter to the node boundary going backwards (-dir).
            float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            float distToEdge = Mathf.Min(tx, ty);

            // The tip sits just outside the node's visual boundary.
            Vector2 tipPos = toCenter - dir * (distToEdge + 2f);

            const float WING_LEN   = 14f;   // px — wing length
            const float WING_WIDTH = 3f;   // px — wing thickness
            const float WING_ANGLE = 30f;  // degrees — half-opening of the V

            // Two wings, symmetric around the edge direction.
            float[] wingAngles = { edgeAngleDeg + 180f - WING_ANGLE,
                                   edgeAngleDeg + 180f + WING_ANGLE };

            for (int i = 0; i < 2; i++)
            {
                var wingGo = new GameObject("ArrowWing_" + i,
                    typeof(RectTransform), typeof(Image), typeof(CanvasRenderer));
                wingGo.transform.SetParent(_graphContent, false);

                var rt = wingGo.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = tipPos;
                rt.sizeDelta = new Vector2(WING_LEN, WING_WIDTH);
                rt.localRotation = Quaternion.Euler(0, 0, wingAngles[i]);

                var img = wingGo.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;

                _edgeObjects.Add(wingGo);
            }
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

            // Directional arrowhead — transitions are not bidirectional.
            Color arrowColor = lineGo.GetComponent<Image>().color;
            CreateArrowhead(fromCenter, toCenter, toRect, angle, arrowColor);
        }

    }
}
