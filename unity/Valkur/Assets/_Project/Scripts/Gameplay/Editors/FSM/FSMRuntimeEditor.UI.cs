using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── UI Construction ──────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("FSMEditorCanvas", 112);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = FSMEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => _undo.Undo(),
                onRedo:           () => _undo.Redo(),
                onSave:           () => PersistSets(),
                onReload:         () => { LoadSets(); RefreshSetsList(); RefreshGraph(); RefreshProperties(); },
                onToggleBuiltIn:  ToggleBuiltInEdges,
                onSearchChanged:  v  => { _searchFilter = v ?? ""; RefreshSetsList(); },
                onTabState:       () => SwitchTab(PropsTab.State),
                onTabTransition:  () => SwitchTab(PropsTab.Transition),
                onTabActions:     () => SwitchTab(PropsTab.Actions),
                onTabConditions:  () => SwitchTab(PropsTab.Conditions),
                onTabBlackboard:  () => SwitchTab(PropsTab.Blackboard),
                onToolSelect:     () => SetGraphTool(GraphTool.Select),
                onToolConnect:    () => SetGraphTool(GraphTool.Connect),
                onToolDelete:     () => SetGraphTool(GraphTool.Delete),
                onZoomIn:         () => AdjustZoom(+0.1f),
                onZoomOut:        () => AdjustZoom(-0.1f),
                onToolMarkIni:    () => SetGraphTool(GraphTool.MarkInitial),
                onToolMarkEnd:    () => SetGraphTool(GraphTool.MarkTerminal),
                onToolAddNode:    () => SetGraphTool(GraphTool.AddNode),
                onToolCloneNode:  () => SetGraphTool(GraphTool.CloneNode),
                onToolDisconnect: () => SetGraphTool(GraphTool.Disconnect),
                onToggleTutorial: () => ToggleTutorial(),
                onPerfToggle:     null);

            // The caption has to match the field's initial value, or the very first click
            // reads as a no-op to anyone watching the label rather than the graph.
            RefreshBuiltInButtonLabel();

            // Wire panel close → keep dropdown state in sync (mirrors Buildings Editor)
            if (_uiRefs.ToolsPanelDrag != null)
                _uiRefs.ToolsPanelDrag.OnClose      = () => { _openDropdowns.Remove("tools");      RefreshMenuBtnHighlights(); };
            if (_uiRefs.SetsPanelDrag != null)
                _uiRefs.SetsPanelDrag.OnClose       = () => { _openDropdowns.Remove("sets");       RefreshMenuBtnHighlights(); };
            if (_uiRefs.EntitiesPanelDrag != null)
                _uiRefs.EntitiesPanelDrag.OnClose   = () => { _openDropdowns.Remove("entities");   RefreshMenuBtnHighlights(); };
            if (_uiRefs.AnimationsPanelDrag != null)
                _uiRefs.AnimationsPanelDrag.OnClose = () => { _openDropdowns.Remove("animations"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.PropsPanelDrag != null)
                _uiRefs.PropsPanelDrag.OnClose      = () => { _openDropdowns.Remove("props");      RefreshMenuBtnHighlights(); };

            // Map builder refs to private fields so existing Graph / Selection partials keep working.
            _setsContent  = _uiRefs.SetsContent;
            _graphArea    = _uiRefs.GraphArea;
            _graphContent = _uiRefs.GraphContent;
            _graphInfoTmp = _uiRefs.GraphInfoText;
            _propsTmp     = _uiRefs.PropsText;
            _statusTmp    = _uiRefs.StatusText;
            _searchBox    = _uiRefs.SearchBox;

            // Tutorial overlay (mirrors Python fsm_tutorial_panel content).
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

            OpenAllPanels();
            RefreshTabs();
            RefreshGraphToolHighlights();
        }

        // ── Dropdown / Panel Management ──────────────────────────────────────────

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "tools", "sets", "entities", "animations", "props" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "tools"      => _uiRefs.ToolsDropdown,
                "sets"       => _uiRefs.SetsDropdown,
                "entities"   => _uiRefs.EntitiesDropdown,
                "animations" => _uiRefs.AnimationsDropdown,
                "props"      => _uiRefs.PropsDropdown,
                _            => null
            };
            go?.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            FSMEditorUIBuilder.ApplyMenuBtnStyle(_uiRefs.ToolsMenuBtnImg,      _uiRefs.ToolsMenuBtnTmp,      _openDropdowns.Contains("tools"));
            FSMEditorUIBuilder.ApplyMenuBtnStyle(_uiRefs.SetsMenuBtnImg,       _uiRefs.SetsMenuBtnTmp,       _openDropdowns.Contains("sets"));
            FSMEditorUIBuilder.ApplyMenuBtnStyle(_uiRefs.EntitiesMenuBtnImg,   _uiRefs.EntitiesMenuBtnTmp,   _openDropdowns.Contains("entities"));
            FSMEditorUIBuilder.ApplyMenuBtnStyle(_uiRefs.AnimationsMenuBtnImg, _uiRefs.AnimationsMenuBtnTmp, _openDropdowns.Contains("animations"));
            FSMEditorUIBuilder.ApplyMenuBtnStyle(_uiRefs.PropsMenuBtnImg,      _uiRefs.PropsMenuBtnTmp,      _openDropdowns.Contains("props"));
        }

        // ── Tabs ─────────────────────────────────────────────────────────────────

        private void SwitchTab(PropsTab tab)
        {
            _propsTab = tab;
            RefreshTabs();
            RefreshProperties();
        }

        private void RefreshTabs()
        {
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.StateTabImg,      _uiRefs.StateTabTmp,      _propsTab == PropsTab.State);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.TransitionTabImg, _uiRefs.TransitionTabTmp, _propsTab == PropsTab.Transition);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.ActionsTabImg,    _uiRefs.ActionsTabTmp,    _propsTab == PropsTab.Actions);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.ConditionsTabImg, _uiRefs.ConditionsTabTmp, _propsTab == PropsTab.Conditions);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.BlackboardTabImg, _uiRefs.BlackboardTabTmp, _propsTab == PropsTab.Blackboard);
        }

        // ── Graph Tool Selection (UI only \u2014 functionality pending) ───────────

        private void SetGraphTool(GraphTool tool)
        {
            _graphTool = tool;
            _pendingConnectFrom = null;
            RefreshGraphToolHighlights();
            if (_statusTmp != null)
                _statusTmp.text = $"Tool: {tool}";
        }

        private void RefreshGraphToolHighlights()
        {
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.SelectToolImg,     null, _graphTool == GraphTool.Select);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.AddNodeToolImg,    null, _graphTool == GraphTool.AddNode);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.CloneNodeToolImg,  null, _graphTool == GraphTool.CloneNode);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.ConnectToolImg,    null, _graphTool == GraphTool.Connect);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.DisconnectToolImg, null, _graphTool == GraphTool.Disconnect);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.DeleteToolImg,     null, _graphTool == GraphTool.Delete);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.MarkIniToolImg,    null, _graphTool == GraphTool.MarkInitial);
            FSMEditorUIBuilder.ApplyTabStyle(_uiRefs.MarkEndToolImg,    null, _graphTool == GraphTool.MarkTerminal);
        }

        private void AdjustZoom(float delta)
        {
            _zoom = Mathf.Clamp(_zoom + delta, 0.25f, 3f);
            if (_uiRefs.GraphZoomLabel != null)
                _uiRefs.GraphZoomLabel.text = $"{Mathf.RoundToInt(_zoom * 100f)}%";
            ApplyZoomPan();
        }

        // ── Tutorial / Save Stub ─────────────────────────────────────────────────

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        private void SaveFsmStub()
        {
            // UI-only migration phase: save logic will be ported in the
            // functionality-migration phase (mirrors Python fsm_persistence).
            if (_statusTmp != null)
                _statusTmp.text = "Save: not yet implemented (UI-only phase).";
        }

        // ── Data Loading ─────────────────────────────────────────────────────────

        private void LoadSets()
        {
            // Persistence-driven load: parses raw dict (preserves props/style/blackboard)
            // and rebuilds typed view + applies persisted layouts.
            LoadSetsFromDisk();
            LoadAssignmentsFromDisk();
            LoadAnimationMapFromDisk();
            LoadLayoutsFromDisk();
        }

        [System.Serializable]
        private class FSMSetsWrapper
        {
            public List<FSMSetData> sets;
        }

        // ── Sets List ────────────────────────────────────────────────────────────

        private void RefreshSetsList() => RefreshSetsListInteractive();

        private void SelectSet(FSMSetData set)
        {
            _selectedSet = set;
            _selectedState = null;
            _selectedTransition = null;
            _pan = Vector2.zero;
            _zoom = 1f;
            ApplyLayoutToSelectedSet();
            if (_uiRefs.GraphZoomLabel != null)
                _uiRefs.GraphZoomLabel.text = $"{Mathf.RoundToInt(_zoom * 100f)}%";
            RefreshSetsList();
            RefreshGraph();
            RefreshProperties();
            RefreshEntities();
            RefreshAnimations();
            if (_statusTmp != null)
            {
                string seedNote = IsSeedGeneratedSet(set)
                    ? " — [seed] state list auto-refreshes on regen; transitions/labels are yours"
                    : "";
                _statusTmp.text = $"Set: {set.label ?? set.id} ({set.states.Count} states, {set.transitions.Count} trans){seedNote}";
            }
        }
    }
}
