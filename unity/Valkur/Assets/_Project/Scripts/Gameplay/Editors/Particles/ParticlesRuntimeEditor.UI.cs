using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── UI Construction ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ParticlesEditorCanvas", 110);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = ParticlesEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => { _undo.Undo(); RefreshUndoRedoLabels(); SetStatus("Undo"); },
                onRedo:           () => { _undo.Redo(); RefreshUndoRedoLabels(); SetStatus("Redo"); },
                onSave:           () => { SaveInstancesToJson(); },
                onReload:         () => { ReloadFromJson(); RefreshPicker(); },
                onModeSelect:     () => SetMode(EditorMode.Select),
                onModePlace:      () => SetMode(EditorMode.Place),
                onModeDelete:     () => SetMode(EditorMode.Delete),
                onAddSystem:      OnAddSystemClicked,
                onRemoveSystem:   OnRemoveClicked,
                onSearchChanged:  v  => { _searchFilter = v ?? ""; RefreshPicker(); },
                onToggleGroup:    () => ToggleGroupByKind(),
                onToggleSpells:    () => ToggleSpellsExpanded(),
                onToggleTutorial:  ToggleTutorial,
                onDeleteInZone:    RequestDeleteAllInZoneWithConfirm,
                onDeleteInstance:  RequestDeleteSelectedInstanceWithConfirm,
                onLoopsToggled:    OnLoopsToggled);

            // Wire panel close callbacks to keep dropdown state in sync (Buildings parity).
            if (_ui.ToolsPanelDrag    != null) _ui.ToolsPanelDrag.OnClose    = () => { _openDropdowns.Remove("tools");    RefreshMenuBtnHighlights(); };
            if (_ui.PresetsPanelDrag  != null) _ui.PresetsPanelDrag.OnClose  = () => { _openDropdowns.Remove("presets");  RefreshMenuBtnHighlights(); };
            if (_ui.PropsPanelDrag    != null) _ui.PropsPanelDrag.OnClose    = () => { _openDropdowns.Remove("props");    RefreshMenuBtnHighlights(); };
            if (_ui.SpellsPanelDrag   != null) _ui.SpellsPanelDrag.OnClose   = () => { _openDropdowns.Remove("spells");   RefreshMenuBtnHighlights(); };

            BuildTutorial();
            BuildConfirmModal();
        }

        // ── Dropdown management (mirrors EntitiesRuntimeEditor) ─────────────────

        private void OpenDefaultDropdowns()
        {
            _openDropdowns.Clear();
            SetDropdownOpen("tools",   true);
            SetDropdownOpen("presets", true);
            SetDropdownOpen("props",   true);
            SetDropdownOpen("spells",  true);
            RefreshMenuBtnHighlights();
        }

        private void ToggleDropdown(string name)
        {
            bool willOpen = !_openDropdowns.Contains(name);
            SetDropdownOpen(name, willOpen);
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = GetDropdown(name);
            if (go == null) return;

            if (open) _openDropdowns.Add(name);
            else      _openDropdowns.Remove(name);
            go.SetActive(open);
        }

        private GameObject GetDropdown(string name) => name switch
        {
            "tools"   => _ui.ToolsDropdown,
            "presets" => _ui.PresetsDropdown,
            "props"   => _ui.PropsDropdown,
            "spells"  => _ui.SpellsDropdown,
            _         => null
        };

        private void RefreshMenuBtnHighlights()
        {
            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,   _ui.ToolsMenuBtnTmp,   _openDropdowns.Contains("tools"));
            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PresetsMenuBtnImg, _ui.PresetsMenuBtnTmp, _openDropdowns.Contains("presets"));
            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PropsMenuBtnImg,   _ui.PropsMenuBtnTmp,   _openDropdowns.Contains("props"));
            ParticlesEditorUIBuilder.ApplyMenuBtnStyle(_ui.SpellsMenuBtnImg,  _ui.SpellsMenuBtnTmp,  _openDropdowns.Contains("spells"));
        }
    }
}
