using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {

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

            _tutorial = TutorialOverlay.Build(_root.transform, "FSM HOTKEYS", new[]
            {
                ("F12",    "Toggle FSM Editor"),
                ("Click",  "Select set / state / transition"),
                ("Drag",   "Move state node"),
                ("MMB",    "Pan graph"),
                ("Wheel",  "Zoom graph"),
                ("Type",   "Filter sets"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        private void BuildSetsPanel()
        {
            var left = EditorUIHelpers.MakeSidebar("SetsPanel", _root.transform, 220f);
            EditorUIHelpers.AddVLG(left, 6, 4f);
            EditorUIHelpers.MakeTitleBar(left.transform, "FSM SETS");

            var toolRow = EditorUIHelpers.CreateUI("ToolRow", left.transform);
            toolRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var thlg = toolRow.AddComponent<HorizontalLayoutGroup>();
            thlg.spacing = 4f; thlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(toolRow.transform, "Undo", () => _undo.Undo(), 28f, 11f);
            EditorUIHelpers.MakeButton(toolRow.transform, "Redo", () => _undo.Redo(), 28f, 11f);

            _searchBox = SearchBox.Create(left.transform, "Search sets\u2026",
                v => { _searchFilter = v ?? ""; RefreshSetsList(); });

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

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var set in _fsmSets)
            {
                var s = set;
                if (filter.Length > 0)
                {
                    string n = ((set.label ?? set.id) ?? "").ToLowerInvariant();
                    if (!n.Contains(filter) && !(set.id ?? "").ToLowerInvariant().Contains(filter)) continue;
                }
                shown++;
                var btn = EditorUIHelpers.MakeButton(_setsContent, set.label ?? set.id,
                    () => SelectSet(s), 26f, 11f);
                if (s == _selectedSet)
                    btn.GetComponent<Image>().color = EditorUIHelpers.BTN_ACTIVE;
            }

            if (shown == 0)
            {
                EditorUIHelpers.AddLabel(_setsContent,
                    _fsmSets.Count == 0 ? "No FSM sets loaded." : $"No sets match '{_searchFilter}'.", 11f);
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

    }
}