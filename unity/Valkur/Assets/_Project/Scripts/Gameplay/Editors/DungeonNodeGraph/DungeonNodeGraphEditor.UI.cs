using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    /// <summary>
    /// uGUI implementation of the editor. Phase 2 MVP: linear list of nodes
    /// (not a free-form canvas), Add-Node dropdown, two-click connect flow,
    /// JSON save/load. Pan/zoom and bezier wiring are deferred — the
    /// per-node display surfaces enough info (id + type + parents + children)
    /// to author useful graphs for the dungeon builder while staying small.
    /// </summary>
    public partial class DungeonNodeGraphEditor
    {
        // Generated UI handles. Built once on first Activate, hidden between visits.
        private Canvas _canvas;
        private GameObject _root;
        private Transform _nodesContainer;
        private Transform _filesContainer;
        private InputField _graphNameField;
        private Dropdown _addNodeTypeDropdown;
        private Text _toast;
        private float _toastUntil;

        private static readonly Color BgPanel = new Color(0.10f, 0.10f, 0.12f, 0.95f);
        private static readonly Color BgRow = new Color(0.18f, 0.18f, 0.22f, 1f);
        private static readonly Color BgRowSource = new Color(0.40f, 0.30f, 0.10f, 1f);
        private static readonly Color BgButton = new Color(0.25f, 0.30f, 0.40f, 1f);
        private static readonly Color FgText = Color.white;

        private void EnsureUI()
        {
            if (_root != null) return;

            // Root canvas — full-screen overlay so we sit above gameplay UI
            // when the editor is active.
            var canvasGo = new GameObject("DungeonNodeGraphCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = (RectTransform)_root.transform;
            rootRt.anchorMin = new Vector2(0.05f, 0.05f);
            rootRt.anchorMax = new Vector2(0.95f, 0.95f);
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = BgPanel;

            BuildHeader(rootRt);
            BuildLeftFilesPanel(rootRt);
            BuildCenterNodesPanel(rootRt);
            BuildToast(rootRt);
        }

        private void BuildHeader(RectTransform parent)
        {
            var bar = NewPanel("Header", parent, new Vector2(0, 0.92f), new Vector2(1, 1f), BgRow);

            // Title.
            NewText("Title", bar, "DUNGEON NODEGRAPH EDITOR",
                new Vector2(0.005f, 0f), new Vector2(0.30f, 1f), 26, TextAnchor.MiddleLeft);

            // Graph name input.
            _graphNameField = NewInputField("GraphName", bar, _activeGraphName,
                new Vector2(0.30f, 0.20f), new Vector2(0.55f, 0.80f));
            _graphNameField.onEndEdit.AddListener(name =>
            {
                _activeGraphName = string.IsNullOrEmpty(name) ? "untitled" : name;
            });

            NewButton("New", bar, new Vector2(0.56f, 0.20f), new Vector2(0.64f, 0.80f),
                () => NewGraph(_graphNameField != null ? _graphNameField.text : "untitled"));
            NewButton("Save", bar, new Vector2(0.65f, 0.20f), new Vector2(0.73f, 0.80f), Save);
            NewButton("Load", bar, new Vector2(0.74f, 0.20f), new Vector2(0.82f, 0.80f),
                () => Load(_graphNameField != null ? _graphNameField.text : "untitled"));
            NewButton("Delete", bar, new Vector2(0.83f, 0.20f), new Vector2(0.91f, 0.80f), DeleteCurrent);
            NewButton("Close", bar, new Vector2(0.92f, 0.20f), new Vector2(0.99f, 0.80f),
                () =>
                {
                    var mgr = Core.GameEditorManager.Instance;
                    if (mgr != null) mgr.CloseAll();
                });
        }

        private void BuildLeftFilesPanel(RectTransform parent)
        {
            var panel = NewPanel("FilesPanel", parent,
                new Vector2(0, 0), new Vector2(0.20f, 0.92f), BgRow);

            NewText("FilesTitle", panel, "Saved Graphs",
                new Vector2(0, 0.95f), new Vector2(1, 1f), 18, TextAnchor.MiddleCenter);

            var content = new GameObject("FilesContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(panel, false);
            var rt = (RectTransform)content.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0.95f);
            rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -4);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.spacing = 2;
            _filesContainer = content.transform;
        }

        private void BuildCenterNodesPanel(RectTransform parent)
        {
            var panel = NewPanel("NodesPanel", parent,
                new Vector2(0.20f, 0), new Vector2(1f, 0.92f), BgPanel);

            // Add-node row.
            var addBar = NewPanel("AddBar", (RectTransform)panel,
                new Vector2(0, 0.92f), new Vector2(1, 1f), BgRow);

            NewText("AddLabel", addBar, "Add node:",
                new Vector2(0.01f, 0f), new Vector2(0.12f, 1f), 16, TextAnchor.MiddleLeft);

            _addNodeTypeDropdown = NewDropdown("TypeDropdown", addBar,
                new Vector2(0.13f, 0.20f), new Vector2(0.40f, 0.80f), GetTypeOptions());

            NewButton("+ Add Node", addBar, new Vector2(0.41f, 0.20f), new Vector2(0.55f, 0.80f),
                () =>
                {
                    var types = GetVisibleTypes();
                    if (_addNodeTypeDropdown == null || types.Count == 0)
                    {
                        ShowToast("No room node types available.");
                        return;
                    }
                    int sel = Mathf.Clamp(_addNodeTypeDropdown.value, 0, types.Count - 1);
                    AddNode(types[sel], position: new Vector2(0, 0));
                });

            NewText("ConnectHelp", addBar, "Click a node, then click another to connect.",
                new Vector2(0.56f, 0f), new Vector2(0.99f, 1f), 14, TextAnchor.MiddleRight);

            // Node list scroll.
            var content = new GameObject("NodesContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(panel, false);
            var rt = (RectTransform)content.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0.92f);
            rt.offsetMin = new Vector2(8, 8); rt.offsetMax = new Vector2(-8, -8);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.spacing = 4;
            _nodesContainer = content.transform;
        }

        private void BuildToast(RectTransform parent)
        {
            var go = new GameObject("Toast", typeof(RectTransform), typeof(Image), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.20f, 0.92f);
            rt.anchorMax = new Vector2(1f, 0.96f);
            rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, 0);
            go.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
            _toast = go.GetComponent<Text>();
            _toast.color = FgText;
            _toast.fontSize = 14;
            _toast.alignment = TextAnchor.MiddleCenter;
            _toast.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.SetActive(false);
        }

        public void ShowToast(string message)
        {
            if (_toast == null) return;
            _toast.text = message;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.gameObject.SetActive(false);
        }

        private void SetUIVisible(bool visible)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
        }

        // ─────────────────────────────────────────────────────────────────
        // Refresh (rebuilds the dynamic content panels each call).
        // ─────────────────────────────────────────────────────────────────

        public void RefreshUI()
        {
            if (_filesContainer == null || _nodesContainer == null) return;

            if (_graphNameField != null && _graphNameField.text != _activeGraphName)
                _graphNameField.text = _activeGraphName;

            // Files panel.
            ClearChildren(_filesContainer);
            foreach (var name in ListGraphFiles())
            {
                NewButton(name, _filesContainer, Vector2.zero, Vector2.one,
                    () => Load(name), preferredHeight: 32);
            }

            // Nodes panel.
            ClearChildren(_nodesContainer);
            foreach (var node in _nodes)
            {
                BuildNodeRow(_nodesContainer, node);
            }

            // Add-node dropdown options.
            if (_addNodeTypeDropdown != null)
            {
                _addNodeTypeDropdown.ClearOptions();
                _addNodeTypeDropdown.AddOptions(GetTypeOptions());
            }
        }

        private void BuildNodeRow(Transform parent, DungeonGraphNodeData node)
        {
            var go = new GameObject("Node_" + node.Id, typeof(RectTransform), typeof(Image),
                typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = node.Id == _connectingFromId ? BgRowSource : BgRow;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 56;
            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(8, 8, 4, 4); hl.spacing = 6;

            var label =
                $"<b>{node.RoomNodeName}</b> ({(node.NodeType != null ? node.NodeType.RoomNodeTypeName : "?")})\n" +
                $"id={node.Id.Substring(0, System.Math.Min(8, node.Id.Length))} " +
                $"parents={node.ParentIds.Count} children={node.ChildIds.Count}";
            var labelText = NewText("Label", go.transform, label,
                Vector2.zero, Vector2.one, 14, TextAnchor.MiddleLeft);
            labelText.supportRichText = true;

            // Whole row is clickable as the connect-flow source/target.
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnNodeClicked(node.Id));

            // Trailing delete button (smaller).
            var del = new GameObject("Del", typeof(RectTransform), typeof(LayoutElement));
            del.transform.SetParent(go.transform, false);
            del.GetComponent<LayoutElement>().preferredWidth = 64;
            NewButton("Del", del.transform, Vector2.zero, Vector2.one, () => RemoveNode(node.Id));
        }

        // ─────────────────────────────────────────────────────────────────
        // uGUI factory helpers.
        // ─────────────────────────────────────────────────────────────────

        private static RectTransform NewPanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        private static Text NewText(string name, Transform parent, string text,
            Vector2 anchorMin, Vector2 anchorMax, int fontSize, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(4, 0); rt.offsetMax = new Vector2(-4, 0);
            var t = go.GetComponent<Text>();
            t.text = text; t.fontSize = fontSize; t.color = FgText;
            t.alignment = align;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return t;
        }

        private static void NewButton(string label, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, System.Action onClick, float preferredHeight = 0f)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = BgButton;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            NewText("Label", go.transform, label, Vector2.zero, Vector2.one, 14, TextAnchor.MiddleCenter);
            if (preferredHeight > 0)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = preferredHeight;
            }
        }

        private static InputField NewInputField(string name, Transform parent, string initial,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f, 1f);

            var textGo = NewText("Text", go.transform, "", Vector2.zero, Vector2.one, 14, TextAnchor.MiddleLeft);
            textGo.color = Color.black;
            textGo.supportRichText = false;
            var placeholderGo = NewText("Placeholder", go.transform, "name...", Vector2.zero, Vector2.one, 14, TextAnchor.MiddleLeft);
            placeholderGo.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            placeholderGo.fontStyle = FontStyle.Italic;

            var inputField = go.GetComponent<InputField>();
            inputField.textComponent = textGo;
            inputField.placeholder = placeholderGo;
            inputField.text = initial;
            return inputField;
        }

        private static Dropdown NewDropdown(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, List<Dropdown.OptionData> options)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = BgButton;
            var label = NewText("Label", go.transform, "—",
                new Vector2(0, 0), new Vector2(0.8f, 1f), 14, TextAnchor.MiddleLeft);
            var dd = go.GetComponent<Dropdown>();
            dd.captionText = label;
            dd.options = options ?? new List<Dropdown.OptionData>();
            return dd;
        }

        private List<Dropdown.OptionData> GetTypeOptions()
        {
            var options = new List<Dropdown.OptionData>();
            foreach (var t in GetVisibleTypes())
                options.Add(new Dropdown.OptionData(t.RoomNodeTypeName));
            return options;
        }

        private List<RoomNodeTypeSO> GetVisibleTypes()
        {
            var visible = new List<RoomNodeTypeSO>();
            if (roomNodeTypeList == null) return visible;
            foreach (var t in roomNodeTypeList.List)
                if (t != null && t.DisplayInNodeGraphEditor) visible.Add(t);
            return visible;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
