using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — UI lifecycle, dropdown management, panel-toggle wiring.
    /// Mirrors ItemsRuntimeEditor.UI / BuildingsRuntimeEditor.UI patterns.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── UI build ──

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SpellsEditorCanvas", 105);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = SpellsEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onAdd:            OnAddSpell,
                onRemove:         OnRemoveSpell,
                onReload:         OnReload,
                onUndo:           OnUndo,
                onRedo:           OnRedo,
                onSave:           OnSave,
                onSearchChanged:  OnSearchChanged,
                onTutorialPrev:   () => StepTutorial(-1),
                onTutorialNext:   () => StepTutorial(+1),
                onTutorialClose:  CloseTutorial,
                onPerfToggle:     () => Toast("PERF overlay — not yet wired."));

            // Wire close-X on each panel header → keep dropdown state in sync.
            WireOnClose(_uiRefs.ModesPanelDrag,    "modes");
            WireOnClose(_uiRefs.SpellsPanelDrag,   "spells");
            WireOnClose(_uiRefs.PropsPanelDrag,    "props");
            WireOnClose(_uiRefs.ViewPanelDrag,     "view");
            WireOnClose(_uiRefs.TutorialPanelDrag, "tutorial");

            // ── Table view wiring ────────────────────────────────────────────
            // Load hidden columns from PlayerPrefs so the user's previous choice
            // is restored immediately (before the table is built the first time).
            LoadColumnPrefs();

            // Hand off the two table ScrollRects so the Table partial can build
            // and refresh without needing access to the UIRefs struct directly.
            SetTableScrollRects(
                _uiRefs.SpellsTableHeaderScroll,
                _uiRefs.SpellsTableBodyScroll,
                _uiRefs.SpellsTableHeaderContent,
                _uiRefs.SpellsTableBodyContent);

            // Wire the "Columns" button → column-visibility popup.
            if (_uiRefs.SpellsColumnsCfgBtn != null)
                _uiRefs.SpellsColumnsCfgBtn.onClick.AddListener(OpenColumnsConfigPopup);
            RefreshColumnsCountLabel();

            // Wire the View panel's direction-selector callbacks.
            WireViewPanel();

            RefreshMenuBtnHighlights();
        }

        private void WireOnClose(DraggablePanel drag, string key)
        {
            if (drag == null) return;
            drag.OnClose = () =>
            {
                _openDropdowns.Remove(key);
                if (key == "view") OnViewPanelClosed();
                RefreshMenuBtnHighlights();
            };
        }

        // ── Search ──

        private void OnSearchChanged(string v)
        {
            _searchFilter = v ?? "";
            RefreshActivePicker();
        }

        // ── Dropdown management ──

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
                if (name == "view") OnViewPanelClosed();
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
                if (name == "tutorial") RefreshTutorial();
                if (name == "view")     OnViewPanelOpened();
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "modes", "spells", "props" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            // Tutorial stays closed by default — user opens it explicitly.
            RefreshMenuBtnHighlights();
        }

        private void CloseAllPanels()
        {
            bool viewWasOpen = _openDropdowns.Contains("view");
            foreach (var n in new[] { "modes", "spells", "props", "view", "tutorial" })
                SetDropdownOpen(n, false);
            _openDropdowns.Clear();
            if (viewWasOpen) OnViewPanelClosed();
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "modes"    => _uiRefs.ModesDropdown,
                "spells"   => _uiRefs.SpellsDropdown,
                "props"    => _uiRefs.PropsDropdown,
                "view"     => _uiRefs.ViewDropdown,
                "tutorial" => _uiRefs.TutorialDropdown,
                _          => null
            };
            if (go != null) go.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            SpellsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ModesMenuBtnImg,    _uiRefs.ModesMenuBtnTmp,    _openDropdowns.Contains("modes"));
            SpellsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.SpellsMenuBtnImg,   _uiRefs.SpellsMenuBtnTmp,   _openDropdowns.Contains("spells"));
            SpellsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PropsMenuBtnImg,    _uiRefs.PropsMenuBtnTmp,    _openDropdowns.Contains("props"));
            SpellsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ViewMenuBtnImg,     _uiRefs.ViewMenuBtnTmp,     _openDropdowns.Contains("view"));
            SpellsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.TutorialMenuBtnImg, _uiRefs.TutorialMenuBtnTmp, _openDropdowns.Contains("tutorial"));
        }

        // ── Style helpers (kept for parity with ItemsRuntimeEditor surface) ──

        private void ApplyMenuBtnStyle(Image img, TMPro.TextMeshProUGUI tmp, bool isOpen)
            => SpellsEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen);

        private void ApplyToolBtnStyle(Image img, bool active, bool danger = false)
            => SpellsEditorUIBuilder.ApplyToolBtnStyle(img, active, danger);
    }
}