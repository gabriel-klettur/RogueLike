using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Input setup ──────────────────────────────────────────────────────

        partial void SetupInputActions()
        {
            _pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pauseAction.Enable();

            _navUp    = new InputAction("PauseNavUp",    InputActionType.Button);
            _navDown  = new InputAction("PauseNavDown",  InputActionType.Button);
            _navLeft  = new InputAction("PauseNavLeft",  InputActionType.Button);
            _navRight = new InputAction("PauseNavRight", InputActionType.Button);
            _confirm  = new InputAction("PauseConfirm",  InputActionType.Button);
            _cancel   = new InputAction("PauseCancel",   InputActionType.Button);

            _navUp.AddBinding("<Keyboard>/upArrow");     _navUp.AddBinding("<Keyboard>/w");
            _navDown.AddBinding("<Keyboard>/downArrow"); _navDown.AddBinding("<Keyboard>/s");
            _navLeft.AddBinding("<Keyboard>/leftArrow"); _navLeft.AddBinding("<Keyboard>/a");
            _navRight.AddBinding("<Keyboard>/rightArrow"); _navRight.AddBinding("<Keyboard>/d");
            _confirm.AddBinding("<Keyboard>/enter");     _confirm.AddBinding("<Keyboard>/space");
            _cancel.AddBinding("<Keyboard>/escape");

            _navUp.Enable(); _navDown.Enable(); _navLeft.Enable();
            _navRight.Enable(); _confirm.Enable(); _cancel.Enable();
        }

        // ── Sounds panel input ───────────────────────────────────────────────

        private void HandleSoundsInput()
        {
            if (_navUp != null && _navUp.WasPerformedThisFrame())
            { _soundSel = (_soundSel - 1 + _soundRows.Count) % _soundRows.Count; UpdateSoundsPanel(); }
            else if (_navDown != null && _navDown.WasPerformedThisFrame())
            { _soundSel = (_soundSel + 1) % _soundRows.Count; UpdateSoundsPanel(); }
            else if (_navLeft != null && _navLeft.WasPerformedThisFrame())
            { ChangeSound(_soundSel, -1); }
            else if (_navRight != null && _navRight.WasPerformedThisFrame())
            { ChangeSound(_soundSel, +1); }
            else if (_confirm != null && _confirm.WasPerformedThisFrame())
            { SaveAndBack(); }
            else if (_cancel != null && _cancel.WasPerformedThisFrame())
            { GoBack(); }
        }

        private void ChangeSound(int i, int dir)
        {
            if (i < 0 || i >= _soundRows.Count) return;
            var row = _soundRows[i];
            float v = Mathf.Clamp(row.get() + dir * row.step, row.min, row.max);
            row.set(v);
            RefreshSoundRowText(i);
        }

        private void SaveAndBack()
        {
            Valkur.Core.GameSettings.Instance?.Save();
            GoBack();
        }

        private void UpdateSoundsPanel()
        {
            if (_soundPills == null || _soundBars == null) return;
            for (int i = 0; i < _soundPills.Length; i++)
            {
                bool s = i == _soundSel;
                if (i < _soundPills.Length) _soundPills[i].color         = s ? PillColor    : Color.clear;
                if (i < _soundBars.Length)  _soundBars[i].color          = s ? AccentGold   : Color.clear;
                if (_soundRowLabels != null && i < _soundRowLabels.Length)
                    _soundRowLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void RefreshSoundRowText(int i)
        {
            if (i < 0 || i >= _soundRows.Count) return;
            var row = _soundRows[i];
            float v = row.get();
            row.valueText.text = row.max <= 1f
                ? Mathf.RoundToInt(v * 100f).ToString()
                : v.ToString("F1");
        }

        // ── Inputs panel input ───────────────────────────────────────────────

        private void HandleInputsTabInput()
        {
            int tabCount = _tabLabels != null ? _tabLabels.Length : 0;
            bool tabLeft  = Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
            bool tabRight = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

            if (tabLeft && tabCount > 0)
            { _inputsTabSel = (_inputsTabSel - 1 + tabCount) % tabCount; UpdateInputsPanel(); }
            else if (tabRight && tabCount > 0)
            { _inputsTabSel = (_inputsTabSel + 1) % tabCount; UpdateInputsPanel(); }
            else if (_cancel != null && _cancel.WasPerformedThisFrame())
            { GoBack(); }
        }

        private void UpdateInputsPanel()
        {
            if (_tabLabels == null || _inputsPanel == null) return;
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = i == _inputsTabSel ? TextSelected : TextNormal;

                var container = _inputsPanel.transform.Find($"TabContent_{i}");
                if (container != null) container.gameObject.SetActive(i == _inputsTabSel);
            }
        }
    }
}
