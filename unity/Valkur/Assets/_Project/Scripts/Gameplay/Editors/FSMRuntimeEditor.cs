using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Runtime in-game FSM Editor (F12).
    /// Mirrors Python's fsm_editor: sets list, visual graph canvas of states/transitions,
    /// properties panel with state and transition tabbed editing.
    /// Displays the FSM for any selected entity with a StateMachine / FSMMonsterBrain.
    /// </summary>
    public class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Inspector ──

        [SerializeField, Tooltip("FSM sets JSON (StreamingAssets or TextAsset)")]
        private TextAsset _setsJsonAsset;

        // ── State ──

        private bool _active;
        private InputAction _toggleAction;

        // Loaded data
        private List<FSMSetData> _fsmSets = new List<FSMSetData>();
        private FSMSetData _selectedSet;
        private FSMStateNode _selectedState;
        private FSMTransitionData _selectedTransition;

        // Graph viewport
        private Vector2 _pan;
        private float _zoom = 1f;
        private bool _panning;
        private Vector2 _panStart;
        private bool _draggingNode;
        private Vector2 _dragOffset;

        // UI refs
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _setsContent;
        private RectTransform _graphArea;
        private RectTransform _graphContent;
        private TextMeshProUGUI _propsTmp;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _graphInfoTmp;

        // Tab tracking
        private enum PropsTab { State, Transition }
        private PropsTab _propsTab = PropsTab.State;
        private Image _stateTabImg, _transTabImg;

        // Graph node visuals
        private readonly Dictionary<string, RectTransform> _nodeRects = new Dictionary<string, RectTransform>();
        private readonly List<GameObject> _edgeObjects = new List<GameObject>();

        // IGameEditor
        public string EditorName => "FSM Editor";
        public bool IsActive => _active;

        // ── Data Classes ──

        [System.Serializable]
        public class FSMSetData
        {
            public string id;
            public string label;
            public string initial;
            public List<FSMStateNode> states = new List<FSMStateNode>();
            public List<FSMTransitionData> transitions = new List<FSMTransitionData>();
        }

        [System.Serializable]
        public class FSMStateNode
        {
            public string id;
            public string label;
            public string stateClass;
            public bool isInitial;
            public bool isTerminal;
            public float x, y, w, h;
        }

        [System.Serializable]
        public class FSMTransitionData
        {
            public string id;
            public string from;
            public string to;
            public string label;
            public string whenEvent;
            public string condition;
            public int priority;
            public int cooldownFrames;
        }

        // ── Lifecycle ──

        protected override void OnSingletonAwake()
        {
            _toggleAction = new InputAction("ToggleFSMEditor", InputActionType.Button, "<Keyboard>/f12");
            _toggleAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        private void OnDestroy()
        {
            _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
        }

        private void Update()
        {
            if (_toggleAction.WasPerformedThisFrame())
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;
            HandleGraphInput();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            LoadSets();
            RefreshSetsList();
            _statusTmp.text = "FSM Editor active. F12 to close.";
            Debug.Log("[FSMEditor] Activated (F12)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _selectedSet = null;
            _selectedState = null;
            _selectedTransition = null;
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[FSMEditor] Deactivated (F12)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI Construction ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("FSMEditorCanvas", 112);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            BuildSetsPanel();
            BuildGraphPanel();
            BuildPropsPanel();
        }

        private void BuildSetsPanel()
        {
            var left = EditorUIHelpers.MakeSidebar("SetsPanel", _root.transform, 220f);
            EditorUIHelpers.AddVLG(left, 6, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "FSM SETS");

            var (scroll, content) = EditorUIHelpers.MakeScrollView(left.transform, "SetsScroll");
            _setsContent = content;

            _statusTmp = EditorUIHelpers.MakeStatusText(left.transform);
        }

        private void BuildGraphPanel()
        {
            // Centre panel for the graph
            var graphPanel = new GameObject("GraphPanel", typeof(RectTransform), typeof(Image));
            graphPanel.transform.SetParent(_root.transform, false);
            var grt = graphPanel.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0f, 0f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.offsetMin = new Vector2(224f, 4f);
            grt.offsetMax = new Vector2(-324f, -4f);
            graphPanel.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.95f);

            // Clip mask
            var mask = graphPanel.AddComponent<RectMask2D>();

            // Scrollable content inside
            _graphArea = grt;
            var contentGo = new GameObject("GraphContent", typeof(RectTransform));
            contentGo.transform.SetParent(graphPanel.transform, false);
            _graphContent = contentGo.GetComponent<RectTransform>();
            _graphContent.anchorMin = Vector2.zero;
            _graphContent.anchorMax = Vector2.one;
            _graphContent.offsetMin = Vector2.zero;
            _graphContent.offsetMax = Vector2.zero;
            _graphContent.pivot = new Vector2(0.5f, 0.5f);

            // Info label
            _graphInfoTmp = EditorUIHelpers.AddLabel(contentGo.transform, "Select an FSM Set to view graph.", 11f);
            _graphInfoTmp.alignment = TextAlignmentOptions.Center;
            _graphInfoTmp.color = EditorUIHelpers.TEXT_SECONDARY;
            var irt = _graphInfoTmp.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.3f, 0.45f);
            irt.anchorMax = new Vector2(0.7f, 0.55f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
        }

        private void BuildPropsPanel()
        {
            var right = EditorUIHelpers.MakeRightPanel("PropsPanel", _root.transform, 320f);
            EditorUIHelpers.AddVLG(right, 6, 4f);

            // Tabs bar
            var tabBar = EditorUIHelpers.CreateUI("TabBar", right.transform);
            tabBar.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true;

            var stateTab = EditorUIHelpers.MakeButton(tabBar.transform, "State", () => SwitchTab(PropsTab.State), 28f, 11f);
            _stateTabImg = stateTab.GetComponent<Image>();
            var transTab = EditorUIHelpers.MakeButton(tabBar.transform, "Transition", () => SwitchTab(PropsTab.Transition), 28f, 11f);
            _transTabImg = transTab.GetComponent<Image>();

            EditorUIHelpers.BuildSeparator(right.transform);

            var (scroll, content) = EditorUIHelpers.MakeScrollView(right.transform, "PropsScroll");
            _propsTmp = EditorUIHelpers.AddLabel(content, "Select a state or transition.", 11f);
            _propsTmp.color = EditorUIHelpers.TEXT_SECONDARY;
            _propsTmp.richText = true;

            RefreshTabs();
        }

        // ── Tabs ──

        private void SwitchTab(PropsTab tab)
        {
            _propsTab = tab;
            RefreshTabs();
            RefreshProperties();
        }

        private void RefreshTabs()
        {
            if (_stateTabImg) _stateTabImg.color = _propsTab == PropsTab.State ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_transTabImg) _transTabImg.color = _propsTab == PropsTab.Transition ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
        }

        // ── Data Loading ──

        private void LoadSets()
        {
            _fsmSets.Clear();
            if (_setsJsonAsset == null)
            {
                var path = System.IO.Path.Combine(Application.streamingAssetsPath, "FSM", "sets.json");
                if (System.IO.File.Exists(path))
                {
                    ParseSetsJson(System.IO.File.ReadAllText(path));
                }
                else
                {
                    Debug.LogWarning("[FSMEditor] No sets JSON found.");
                }
            }
            else
            {
                ParseSetsJson(_setsJsonAsset.text);
            }
        }

        private void ParseSetsJson(string json)
        {
            // Unity JsonUtility doesn't handle nested arrays of custom objects well;
            // use a wrapper for the top-level "sets" array.
            var wrapper = JsonUtility.FromJson<FSMSetsWrapper>("{\"sets\":" + json + "}");
            if (wrapper?.sets != null)
                _fsmSets = wrapper.sets;

            // Fallback: try direct wrapper if JSON has {"sets": [...]}
            if (_fsmSets.Count == 0)
            {
                wrapper = JsonUtility.FromJson<FSMSetsWrapper>(json);
                if (wrapper?.sets != null)
                    _fsmSets = wrapper.sets;
            }
        }

        [System.Serializable]
        private class FSMSetsWrapper
        {
            public List<FSMSetData> sets;
        }

        // ── Sets List ──

        private void RefreshSetsList()
        {
            for (int i = _setsContent.childCount - 1; i >= 0; i--)
                Destroy(_setsContent.GetChild(i).gameObject);

            foreach (var set in _fsmSets)
            {
                var s = set;
                var btn = EditorUIHelpers.MakeButton(_setsContent, set.label ?? set.id,
                    () => SelectSet(s), 26f, 11f);
                if (s == _selectedSet)
                    btn.GetComponent<Image>().color = EditorUIHelpers.BTN_ACTIVE;
            }

            if (_fsmSets.Count == 0)
            {
                EditorUIHelpers.AddLabel(_setsContent, "No FSM sets loaded.", 11f);
            }
        }

        private void SelectSet(FSMSetData set)
        {
            _selectedSet = set;
            _selectedState = null;
            _selectedTransition = null;
            _pan = Vector2.zero;
            _zoom = 1f;
            RefreshSetsList();
            RefreshGraph();
            RefreshProperties();
            _statusTmp.text = $"Set: {set.label ?? set.id} ({set.states.Count} states, {set.transitions.Count} trans)";
        }

        // ── Graph Rendering ──

        private void RefreshGraph()
        {
            // Clear old nodes/edges
            foreach (var kv in _nodeRects)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _nodeRects.Clear();
            foreach (var e in _edgeObjects)
                if (e != null) Destroy(e);
            _edgeObjects.Clear();

            if (_selectedSet == null)
            {
                _graphInfoTmp.gameObject.SetActive(true);
                return;
            }
            _graphInfoTmp.gameObject.SetActive(false);

            // Draw nodes
            foreach (var state in _selectedSet.states)
            {
                var node = CreateNodeVisual(state);
                _nodeRects[state.id] = node;
            }

            // Draw edges
            foreach (var trans in _selectedSet.transitions)
            {
                CreateEdgeVisual(trans);
            }

            ApplyZoomPan();
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

            // Click handler
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => SelectState(state));

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

            // Click handler on edge label
            var edgeBtnGo = labelGo;
            var edgeBtn = edgeBtnGo.AddComponent<Button>();
            edgeBtn.onClick.AddListener(() => SelectTransition(trans));

            _edgeObjects.Add(lineGo);
            _edgeObjects.Add(labelGo);
        }

        private void ApplyZoomPan()
        {
            if (_graphContent == null) return;
            _graphContent.localScale = Vector3.one * _zoom;
            _graphContent.anchoredPosition = _pan;
        }

        // ── Graph Input (Pan/Zoom) ──

        private void HandleGraphInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Zoom with scroll wheel (when pointer is over graph area)
            var scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                _zoom = Mathf.Clamp(_zoom + scrollDelta * 0.001f, 0.3f, 2f);
                ApplyZoomPan();
            }

            // Pan with MMB
            if (mouse.middleButton.wasPressedThisFrame)
            {
                _panning = true;
                _panStart = mouse.position.ReadValue() - _pan;
            }
            if (mouse.middleButton.wasReleasedThisFrame)
                _panning = false;

            if (_panning)
            {
                _pan = (Vector2)mouse.position.ReadValue() - _panStart;
                ApplyZoomPan();
            }

            // Drag selected node with LMB
            if (_selectedState != null && _draggingNode)
            {
                if (mouse.leftButton.isPressed && _nodeRects.TryGetValue(_selectedState.id, out var nrt))
                {
                    var delta = (Vector2)mouse.delta.ReadValue() / _zoom;
                    _selectedState.x += delta.x;
                    _selectedState.y -= delta.y;
                    nrt.anchoredPosition = new Vector2(_selectedState.x, -_selectedState.y);
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    _draggingNode = false;
                    RefreshGraph(); // Redraw edges
                }
            }
        }

        // ── Selection ──

        private void SelectState(FSMStateNode state)
        {
            _selectedState = state;
            _selectedTransition = null;
            _propsTab = PropsTab.State;
            _draggingNode = true;
            RefreshTabs();
            RefreshProperties();
            RefreshGraph();
        }

        private void SelectTransition(FSMTransitionData trans)
        {
            _selectedTransition = trans;
            _selectedState = null;
            _propsTab = PropsTab.Transition;
            RefreshTabs();
            RefreshProperties();
            RefreshGraph();
        }

        // ── Properties ──

        private void RefreshProperties()
        {
            if (_propsTab == PropsTab.State)
                ShowStateProperties();
            else
                ShowTransitionProperties();
        }

        private void ShowStateProperties()
        {
            if (_selectedState == null)
            {
                _propsTmp.text = "Click a state node to view properties.";
                return;
            }
            var s = _selectedState;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>STATE PROPERTIES</b>");
            sb.AppendLine();
            sb.AppendLine($"<b>ID:</b> {s.id}");
            sb.AppendLine($"<b>Label:</b> {s.label}");
            sb.AppendLine($"<b>Class:</b> {s.stateClass}");
            sb.AppendLine($"<b>Initial:</b> {s.isInitial || (_selectedSet?.initial == s.id)}");
            sb.AppendLine($"<b>Terminal:</b> {s.isTerminal}");
            sb.AppendLine($"<b>Position:</b> ({s.x:F0}, {s.y:F0})");
            sb.AppendLine($"<b>Size:</b> {s.w:F0} × {s.h:F0}");

            // Show outgoing transitions
            if (_selectedSet != null)
            {
                var outgoing = _selectedSet.transitions.Where(t => t.from == s.id).ToList();
                if (outgoing.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<b>Outgoing Transitions ({outgoing.Count}):</b>");
                    foreach (var t in outgoing)
                        sb.AppendLine($"  → {t.to} [{t.label ?? t.whenEvent ?? "?"}]");
                }
                var incoming = _selectedSet.transitions.Where(t => t.to == s.id).ToList();
                if (incoming.Count > 0)
                {
                    sb.AppendLine($"<b>Incoming Transitions ({incoming.Count}):</b>");
                    foreach (var t in incoming)
                        sb.AppendLine($"  ← {t.from} [{t.label ?? t.whenEvent ?? "?"}]");
                }
            }

            _propsTmp.text = sb.ToString();
        }

        private void ShowTransitionProperties()
        {
            if (_selectedTransition == null)
            {
                _propsTmp.text = "Click a transition label to view properties.";
                return;
            }
            var t = _selectedTransition;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>TRANSITION PROPERTIES</b>");
            sb.AppendLine();
            sb.AppendLine($"<b>ID:</b> {t.id}");
            sb.AppendLine($"<b>From:</b> {t.from}");
            sb.AppendLine($"<b>To:</b> {t.to}");
            sb.AppendLine($"<b>Label:</b> {t.label}");
            sb.AppendLine($"<b>Event:</b> {t.whenEvent}");
            sb.AppendLine($"<b>Condition:</b> {(string.IsNullOrEmpty(t.condition) ? "–" : t.condition)}");
            sb.AppendLine($"<b>Priority:</b> {t.priority}");
            sb.AppendLine($"<b>Cooldown:</b> {t.cooldownFrames} frames");
            _propsTmp.text = sb.ToString();
        }

        // ── Live Entity Inspection ──

        /// <summary>
        /// Called by external systems to inspect the FSM of a specific entity at runtime.
        /// </summary>
        public void InspectEntity(GameObject entity)
        {
            var brain = entity.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
            if (brain == null) return;

            _statusTmp.text = $"Inspecting: {entity.name} — State: {brain.CurrentStateName}";
        }
    }
}
