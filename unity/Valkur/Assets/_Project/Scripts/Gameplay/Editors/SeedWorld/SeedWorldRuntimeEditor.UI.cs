using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// Two panels: the parameters on the left (three tabs), the preview on the right with its
    /// layer switcher, statistics, legend and the point under the pointer.
    /// </summary>
    public partial class SeedWorldRuntimeEditor
    {
        private const float PARAMS_W = 340f;
        private const float PREVIEW_W = 700f;
        private const float PANEL_H = 660f;
        private const float ROW_H = 24f;
        private const float LABEL_W = 150f;

        /// <summary>
        /// Positive on purpose. <c>ApplyPanelDock</c> NEGATES the vertical offset it is given, so
        /// a negative docks the panel above the canvas — what shipped in the Controls editor.
        /// </summary>
        private const float PANEL_TOP = TileEditorUIHelpers.PANEL_TOP_OFFSET;

        private Transform _paramsContent;
        private Transform _previewContent;
        private RectTransform _bodyScrollContent;
        private TextMeshProUGUI _status;

        private DraggablePanel _paramsPanel;
        private DraggablePanel _previewPanel;

        private readonly List<Button> _tabButtons = new List<Button>();

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SeedWorldEditorCanvas", 118);
            // The pre-game menus' look, kept live across every rebuild (Editors/_Shared/Frontend).
            Valkur.Gameplay.Editors.Frontend.FrontendSkinRoot.Attach(_canvas.gameObject);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            // Same top strip as every other editor; PANEL_TOP already leaves its height free.
            DraggablePanel.TopReservedPx = TileEditorUIHelpers.MENUBAR_HEIGHT;

            EditorUIHelpers.MakeDropPanel(
                "SeedWorldParamsPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopLeft, 16f, PANEL_TOP, PARAMS_W, PANEL_H,
                "Seed World", out _paramsContent, out _paramsPanel);

            EditorUIHelpers.MakeDropPanel(
                "SeedWorldPreviewPanel", _root.transform,
                TileEditorUIHelpers.PanelDock.TopRight, 16f, PANEL_TOP, PREVIEW_W, PANEL_H,
                "Vista previa", out _previewContent, out _previewPanel);

            // NEITHER PANEL MAY BE CLOSED. This editor has no toolbar outside them, and the
            // workspace layer persists `open:false` — closing both would open it empty forever,
            // which is what shipped in the Controls editor.
            _paramsPanel.ShowCloseButton = false;
            _previewPanel.ShowCloseButton = false;

            BuildParamsPanel();
            BuildPreviewPanel();
            // Built last so it draws over a panel dragged up against it.
            BuildMenuBar(_root.transform);
        }

        /// <summary>Heal a workspace document that persisted a panel as closed.</summary>
        private void ForcePanelsOpen()
        {
            Reopen(_paramsPanel);
            Reopen(_previewPanel);
            RefreshMenuBar();
        }

        private static void Reopen(DraggablePanel panel)
        {
            if (panel == null) return;
            panel.gameObject.SetActive(true);
            panel.MarkOpened();
        }

        private void BuildParamsPanel()
        {
            var tabs = MakeRow(_paramsContent, "Tabs", 26f);
            _tabButtons.Clear();
            AddTabButton(tabs.transform, "Mundo", Tab.World);
            AddTabButton(tabs.transform, "Clima", Tab.Climate);
            AddTabButton(tabs.transform, "Biomas", Tab.Biomes);

            var built = EditorUIHelpers.MakeScrollView(_paramsContent, "SeedWorldBody", 1f);
            _bodyScrollContent = built.content;
            UIFactory.AddVerticalScrollbar(built.scroll);

            EditorUIHelpers.BuildSeparator(_paramsContent);

            var history = MakeRow(_paramsContent, "HistoryRow", 26f);
            EditorUIHelpers.MakeButton(history.transform, "Deshacer", Undo, 26f, 11f);
            EditorUIHelpers.MakeButton(history.transform, "Rehacer", Redo, 26f, 11f);
            EditorUIHelpers.MakeDangerButton(history.transform, "Por defecto", ResetToDefaults, 26f);

            _status = EditorUIHelpers.AddLabel(_paramsContent, "", 10f);
            _status.color = EditorUIHelpers.TEXT_MUTED;
            _status.enableWordWrapping = true;
            // A fixed three-line box: a build report is long, and unbounded it ran out of the
            // bottom of the panel over the world behind it (first live capture). The full report
            // also goes to the console.
            _status.overflowMode = TextOverflowModes.Ellipsis;
            var statusLe = _status.gameObject.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 40f;
            statusLe.minHeight = 40f;
            statusLe.flexibleHeight = 0f;
        }

        private void AddTabButton(Transform parent, string label, Tab tab)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, () => SetTab(tab), 24f, 11f);
            btn.gameObject.name = "Tab_" + tab;
            _tabButtons.Add(btn);
        }

        private void RefreshTabs()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                if (_tabButtons[i] == null) continue;
                // UIButton.SetTint is the single correct path: writing targetGraphic.color renders
                // colour x normalColor and the chosen tab reads as a gap.
                UIButton.SetTint(_tabButtons[i],
                    (int)_tab == i ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL);
            }
        }

        /// <summary>Rebuilds the parameter body for the active tab.</summary>
        private void RebuildBody()
        {
            if (!_uiBuilt || _bodyScrollContent == null) return;

            RefreshTabs();
            RefreshViewButton();
            RefreshMenuBar();
            _fieldResync.Clear();
            ClearChildren(_bodyScrollContent);

            switch (_tab)
            {
                case Tab.World:   BuildWorldTab(_bodyScrollContent);   break;
                case Tab.Climate: BuildClimateTab(_bodyScrollContent); break;
                case Tab.Biomes:  BuildBiomesTab(_bodyScrollContent);  break;
            }
        }

        internal void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
            if (_menuStatus != null) _menuStatus.text = text;
        }

        internal string StatusText => _status != null ? _status.text : string.Empty;

        private static GameObject MakeRow(Transform parent, string name, float height)
        {
            var row = EditorUIHelpers.CreateUI(name, parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            // Explicit zero: a LayoutElement that sets only preferredHeight still takes
            // flexibleHeight from the layout group on the same object, which reports 1.
            element.flexibleHeight = 0f;
            return row;
        }

        /// <summary>
        /// Empties a rebuilt container. <c>Object.Destroy</c> is an outright ERROR in Edit Mode,
        /// and this editor is reachable from EditMode fixtures.
        /// </summary>
        private static void ClearChildren(Transform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
    }
}
