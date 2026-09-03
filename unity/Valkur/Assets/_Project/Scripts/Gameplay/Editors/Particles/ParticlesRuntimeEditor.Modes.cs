using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            switch (mode)
            {
                case EditorMode.Select: SetStatus("Select: click to select, RMB-drag to move."); break;
                case EditorMode.Place:
                    SetStatus(string.IsNullOrEmpty(_selectedPresetId)
                        ? "Place: pick a preset first."
                        : $"Place: click on the map to spawn '{_selectedPresetId}'.");
                    break;
                case EditorMode.Delete: SetStatus("Delete: click an instance to remove it."); break;
            }
        }

        private void RefreshModeButtons()
        {
            if (_ui.SelectBtnImg != null)
                _ui.SelectBtnImg.color = _mode == EditorMode.Select ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            if (_ui.PlaceBtnImg != null)
                _ui.PlaceBtnImg.color = _mode == EditorMode.Place ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
            if (_ui.DeleteBtnImg != null)
                _ui.DeleteBtnImg.color = _mode == EditorMode.Delete ? UITheme.DANGER : UITheme.DANGER_IDLE;
        }

        // ── Add / Remove (Python particles_add_remove_panel parity) ──

        private void OnAddSystemClicked()
        {
            if (string.IsNullOrEmpty(_selectedPresetId))
            {
                SetStatus("Pick a preset first, then click Add System.");
                return;
            }
            SetMode(EditorMode.Place);
        }

        private void OnRemoveClicked()
        {
            SetMode(EditorMode.Delete);
        }

        private void RefreshUndoRedoLabels()
        {
            // Undo/Redo button labels mirror the stack peek labels (Buildings parity).
            if (_ui.UndoBtnLabel != null)
            {
                var lbl = _undo.PeekUndoLabel();
                _ui.UndoBtnLabel.text = string.IsNullOrEmpty(lbl) ? "Undo" : $"Undo: {lbl}";
            }
            if (_ui.RedoBtnLabel != null)
            {
                var lbl = _undo.PeekRedoLabel();
                _ui.RedoBtnLabel.text = string.IsNullOrEmpty(lbl) ? "Redo" : $"Redo: {lbl}";
            }
        }
    }
}
