using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Runtime in-game FSM Editor (F12).
    /// Mirrors Python's fsm_editor: sets list, visual graph canvas of states/transitions,
    /// properties panel with state and transition tabbed editing.
    /// Displays the FSM for any selected entity with a StateMachine / FSMMonsterBrain.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Inspector ──

        [SerializeField, Tooltip("Monster catalog asset — drives the Entities panel's " +
            "by_archetype key picker. Same Editor-only self-resolution limitation as " +
            "EntitiesRuntimeEditor/F3's spawner catalog (see FSMRuntimeEditor.Entities.cs " +
            "ResolveMonsterCatalogIfNeeded): empty in a standalone build until this gets a " +
            "real injection seam.")]
        private Valkur.Data.MonsterCatalog _monsterCatalog;

        // ── State ──

        private bool _active;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

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

        // UI refs
        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _setsContent;
        private RectTransform _graphArea;
        private RectTransform _graphContent;
        private TextMeshProUGUI _propsTmp;
        private TextMeshProUGUI _statusTmp;
        private TextMeshProUGUI _graphInfoTmp;

        // Tab tracking — mirrors Python fsm_properties_panel tab set.
        private enum PropsTab { State, Transition, Actions, Conditions, Blackboard }
        private PropsTab _propsTab = PropsTab.State;

        // Graph editing tools — mirrors Python fsm_graph_panel toolbar tools.
        private enum GraphTool { Select, AddNode, CloneNode, Connect, Disconnect, Delete, MarkInitial, MarkTerminal }
        private GraphTool _graphTool = GraphTool.Select;

        // Connect/disconnect tool: id of the first state clicked (waiting for second).
        private string _pendingConnectFrom;

        // UI builder refs (replaces individual sidebar fields; kept aliases below).
        private FSMEditorUIBuilder.UIRefs _uiRefs;
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // Graph node visuals
        private readonly Dictionary<string, RectTransform> _nodeRects = new Dictionary<string, RectTransform>();
        private readonly List<GameObject> _edgeObjects = new List<GameObject>();

        // EditorKit extras
        private string _searchFilter = "";
        private TMP_InputField _searchBox;
        private GameObject _tutorial;
        private readonly UndoStack _undo = new UndoStack(64);

        // IGameEditor
        public string EditorName => "FSM Editor";
        public bool IsActive => _active;

        // Raw root dict (parity with Python sets.json — preserves all fields).
        private Dictionary<string, object> _setsRoot;
        // Per-set assignments + animation_map (raw dict round-trip).
        private Dictionary<string, object> _assignmentsRoot;
        private Dictionary<string, object> _animationMapRoot;
        // Per-set layouts (positions + viewport).
        private Dictionary<string, object> _layoutsRoot;

        // ── Data Classes ──
        // Each typed view holds a back-pointer to its raw dict so edits are
        // round-trippable via MiniJsonRuntime.Serialize.

        public class FSMSetData
        {
            public string id;
            public string label;
            public string initial;
            public List<FSMStateNode> states = new List<FSMStateNode>();
            public List<FSMTransitionData> transitions = new List<FSMTransitionData>();
            public Dictionary<string, object> raw;   // pointer into _setsRoot["sets"][i]
        }

        public class FSMStateNode
        {
            public string id;
            public string label;
            public string stateClass;
            public bool isInitial;
            public bool isTerminal;
            public float x, y, w, h;
            public Dictionary<string, object> raw;   // pointer into set.raw["states"][i]
        }

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
            public Dictionary<string, object> raw;   // pointer into set.raw["transitions"][i]
        }

        // ── Lifecycle ──

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleFSM, out _ownsToggleAction);
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleFSM))
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }
            if (!_active) return;
            HandleGraphInput();
            HandleUndoRedoShortcuts();
        }

        /// <summary>
        /// Ctrl+Z / Ctrl+Y / Esc, matching what the tutorial overlay advertises
        /// (<c>("Ctrl+Z", "Undo")</c> / <c>("Ctrl+Y", "Redo")</c> / <c>("Esc", …)</c> in
        /// <c>BuildUI</c>'s <c>TutorialOverlay.Build</c> call) — previously Ctrl+Z/Y were
        /// true only of the two toolbar buttons, and Esc did nothing at all. Mirrors
        /// <c>ItemsRuntimeEditor.Modes.HandleKeyboardShortcuts</c>: routed through
        /// <c>KeyboardInputManager</c>, never <c>Keyboard.current</c> directly, so the
        /// legacy backend still supplies these reads when the new InputSystem package
        /// drops OS events. Esc unwinds the same escalation Items uses — cancel the
        /// in-progress Connect/Disconnect, then the tutorial, then the editor. The
        /// method keeps its historical name because <c>FSMEditorGraphToolsTests</c>
        /// reflects on it.
        /// </summary>
        private void HandleUndoRedoShortcuts()
        {
            bool ctrl = KeyboardInputManager.IsCtrlHeld();
            if (EditorInput.UndoPressed()) _undo.Undo();
            if (EditorInput.RedoPressed()) _undo.Redo();

            if (EditorInput.ClosePressed())
            {
                if (_pendingConnectFrom != null)
                {
                    _pendingConnectFrom = null;
                    SetStatus("Cancelled.");
                }
                else if (_tutorial != null && _tutorial.activeSelf)
                {
                    _tutorial.SetActive(false);
                }
                else
                {
                    if (GameEditorManager.HasInstance)
                        GameEditorManager.Instance.ToggleExclusive(this);
                    else
                        ToggleActive();
                }
            }
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            LoadSets();
            RefreshSetsList();
            // Canonical parity (Items F7 / Buildings F10): detach the Cinemachine
            // follow while authoring so the camera holds still behind the overlay;
            // reattached in Deactivate.
            CameraSetup.Instance?.DetachFollow();
            SetStatus("FSM Editor active. F12 to close.");
            Debug.Log("[FSMEditor] Activated (F12)");
        }

        public void Deactivate()
        {
            _active = false;
            _root.SetActive(false);
            _pendingConnectFrom = null;   // cancel any half-wired Connect/Disconnect
            _panning = false;
            _selectedSet = null;
            _selectedState = null;
            _selectedTransition = null;
            CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[FSMEditor] Deactivated (F12)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── Status feedback (canonical Items F7 pattern) ──────────────────────────

        /// <summary>Writes the shared status line. Null-safe: several refresh paths
        /// also run from EditMode tests where BuildUI may not have run.</summary>
        private void SetStatus(string msg)
        {
            if (_statusTmp != null) _statusTmp.text = msg;
        }

        /// <summary>SetStatus + console log — reserved for actions worth a paper
        /// trail; routine selections stay status-line-only to avoid log spam.</summary>
        private void Toast(string msg)
        {
            SetStatus(msg);
            Debug.Log($"[FSMEditor] {msg}");
        }

        // ── UI Construction ──

    }
}