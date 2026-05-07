using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Input handling ────────────────────────────────────────────────────────

        private void HandleMMLoadInput()
        {
            switch (_mmLoadMode)
            {
                case LoadPanelMode.Rename:        HandleRenameInput();        return;
                case LoadPanelMode.ConfirmDelete: HandleConfirmDeleteInput(); return;
            }

            // OR new-InputSystem actions with legacy fallback (InputCompat) so the
            // panel still navigates when the new pipeline drops OS event delivery.
            if (InputCompat.CancelPressed() || Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); return; }

            if (_mmLoadRuns.Count == 0) return;

            // W/S navigate runs (left column)
            if (InputCompat.NavUpPressed() || Valkur.Core.Input.InputCompat.NavUpPressed())
            {
                _mmLoadRunSel = Mathf.Max(0, _mmLoadRunSel - 1);
                _mmLoadSaveSel = 0;
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            else if (InputCompat.NavDownPressed() || Valkur.Core.Input.InputCompat.NavDownPressed())
            {
                _mmLoadRunSel = Mathf.Min(_mmLoadRuns.Count - 1, _mmLoadRunSel + 1);
                _mmLoadSaveSel = 0;
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            // A/D navigate saves within selected run (right column)
            else if (InputCompat.NavLeftPressed() || Valkur.Core.Input.InputCompat.NavLeftPressed())
            {
                if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                {
                    int saves = _mmLoadRuns[_mmLoadRunSel].saves.Count;
                    if (saves > 0) { _mmLoadSaveSel = Mathf.Max(0, _mmLoadSaveSel - 1); UpdateMMLoadVisuals(); }
                }
            }
            else if (InputCompat.NavRightPressed() || Valkur.Core.Input.InputCompat.NavRightPressed())
            {
                if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                {
                    int saves = _mmLoadRuns[_mmLoadRunSel].saves.Count;
                    if (saves > 0) { _mmLoadSaveSel = Mathf.Min(saves - 1, _mmLoadSaveSel + 1); UpdateMMLoadVisuals(); }
                }
            }
            else if (InputCompat.ConfirmPressed() || Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                MMLoadSelectedSave();
            }
            else if (Valkur.Core.Input.KeyboardInputManager.WasDeletePressedThisFrame())
            {
                RequestDeleteSelectedSave();
            }
            else if (Valkur.Core.Input.KeyboardInputManager.WasF2PressedThisFrame())
            {
                BeginRenameSelectedSave();
            }
        }

        private void EnsureMMLoadScroll()
        {
            if (_mmLoadRunSel < _mmLoadRunScroll) _mmLoadRunScroll = _mmLoadRunSel;
            if (_mmLoadRunSel >= _mmLoadRunScroll + MM_RUN_ROWS)
                _mmLoadRunScroll = _mmLoadRunSel - MM_RUN_ROWS + 1;
        }

        private void HandleRenameInput()
        {
            if (Valkur.Core.Input.InputCompat.CancelPressed())
            {
                CancelRename();
                return;
            }
            if (Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                CommitRename();
            }
        }

        private void HandleConfirmDeleteInput()
        {
            if (InputCompat.CancelPressed() || Valkur.Core.Input.InputCompat.CancelPressed())
            { SetLoadMode(LoadPanelMode.List); return; }

            if (InputCompat.NavLeftPressed() || InputCompat.NavRightPressed()
                || Valkur.Core.Input.InputCompat.NavLeftPressed() || Valkur.Core.Input.InputCompat.NavRightPressed())
            { _mmConfirmSel = 1 - _mmConfirmSel; UpdateConfirmVisuals(); }

            if (InputCompat.ConfirmPressed() || Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                if (_mmConfirmSel == 1) MMDeleteSelectedSave();
                else SetLoadMode(LoadPanelMode.List);
            }
        }
    }
}
