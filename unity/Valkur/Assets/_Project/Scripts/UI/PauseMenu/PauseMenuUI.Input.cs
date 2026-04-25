using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI : MonoBehaviour
    {

        private void Update()
        {
            // ESC when menu is closed → open pause; don't fall through to
            // sub-screen input this frame (_cancel also binds ESC and would
            // immediately close the menu again).
            if (_screen == PauseScreen.None)
            {
                if (_pauseAction != null && _pauseAction.WasPerformedThisFrame())
                    OpenPause();
                return;
            }

            // Menu is open – sub-screen input handles ESC via _cancel → GoBack()
            switch (_screen)
            {
                case PauseScreen.Pause:   HandlePauseListInput(); break;
                case PauseScreen.Options: HandleListInput(_optOptions.Length,   ref _optSel,   _optPills,   _optBars,   _optTexts,   ExecuteOption); break;
                case PauseScreen.Sounds:  HandleSoundsInput();  break;
                case PauseScreen.Inputs:  HandleInputsTabInput(); break;
                case PauseScreen.LoadGame: HandleLoadGameInput(); break;
            }
        }

        /// <summary>
        /// Pause screen handles ESC directly via _pauseAction (not _cancel)
        /// so that ESC closes the menu cleanly from the top-level list.
        /// </summary>
        private void HandlePauseListInput()
        {
            if (_pauseAction != null && _pauseAction.WasPerformedThisFrame())
            { ClosePause(); return; }
            if (_navUp != null && _navUp.WasPerformedThisFrame())
            { _pauseSel = (_pauseSel - 1 + _pauseOptions.Length) % _pauseOptions.Length; UpdateListVisuals(_pauseSel, _pausePills, _pauseBars, _pauseTexts); }
            else if (_navDown != null && _navDown.WasPerformedThisFrame())
            { _pauseSel = (_pauseSel + 1) % _pauseOptions.Length; UpdateListVisuals(_pauseSel, _pausePills, _pauseBars, _pauseTexts); }
            else if (_confirm != null && _confirm.WasPerformedThisFrame())
            { ExecutePause(_pauseSel); }
        }

        private void OnDestroy()
        {
            _pauseAction?.Disable(); _pauseAction?.Dispose();
            _navUp?.Disable();   _navUp?.Dispose();
            _navDown?.Disable(); _navDown?.Dispose();
            _navLeft?.Disable(); _navLeft?.Dispose();
            _navRight?.Disable(); _navRight?.Dispose();
            _confirm?.Disable(); _confirm?.Dispose();
            _cancel?.Disable();  _cancel?.Dispose();
            _rebinder?.Dispose(); _rebinder = null;
        }

        // ════════════════════════════════════════════════════════════════════
        // Public API
        // ════════════════════════════════════════════════════════════════════

        public void TogglePause()
        {
            if (_screen == PauseScreen.None)
                OpenPause();
            else
                ClosePause();
        }

        public void OpenPause()
        {
            RebuildPauseOptions();
            ShowScreen(PauseScreen.Pause);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(true);
        }

        public void ClosePause()
        {
            ShowScreen(PauseScreen.None);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(false);
        }

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        private void ShowScreen(PauseScreen s)
        {
            _screen = s;
            _overlayRoot?.SetActive(s != PauseScreen.None);
            _pausePanel?.SetActive(s == PauseScreen.Pause);
            _optionsPanel?.SetActive(s == PauseScreen.Options);
            _soundsPanel?.SetActive(s == PauseScreen.Sounds);
            _inputsPanel?.SetActive(s == PauseScreen.Inputs);
            _loadGamePanel?.SetActive(s == PauseScreen.LoadGame);

            if (s == PauseScreen.Pause)   { _pauseSel = 0;  UpdateListVisuals(_pauseSel,  _pausePills,  _pauseBars,  _pauseTexts); }
            if (s == PauseScreen.Options) { _optSel = 0;    UpdateListVisuals(_optSel,    _optPills,    _optBars,    _optTexts);   }
            if (s == PauseScreen.Sounds)  { _soundSel = 0;  UpdateSoundsPanel(); }
            if (s == PauseScreen.Inputs)  { _inputsTabSel = 0; _inputsRowSel = 0; UpdateInputsPanel(); }
            if (s == PauseScreen.LoadGame) { RefreshLoadGamePanel(); }
        }

        private void HideAll() => ShowScreen(PauseScreen.None);

        // ════════════════════════════════════════════════════════════════════
        // Pause menu execution
        // ════════════════════════════════════════════════════════════════════

        private void RebuildPauseOptions()
        {
            bool hasSaves = SaveFileManager.ListSaves().Count > 0;
            var opts = new List<string> { "Continuar", "Nueva Partida", "Guardar partida" };
            if (hasSaves) opts.Add("Cargar juego");
            opts.Add("Opciones");
            opts.Add("Salir");
            _pauseOptions = opts.ToArray();
            // Rebuild panel rows to match new count
            RebuildPausePanelRows();
        }


        // ════════════════════════════════════════════════════════════════════
        // Generic list input

        // ════════════════════════════════════════════════════════════════════

        private void HandleListInput(int count, ref int sel,
            Image[] pills, Image[] bars, TextMeshProUGUI[] texts,
            System.Action<int> execute)
        {
            if (_navUp != null && _navUp.WasPerformedThisFrame())
            { sel = (sel - 1 + count) % count; UpdateListVisuals(sel, pills, bars, texts); }
            else if (_navDown != null && _navDown.WasPerformedThisFrame())
            { sel = (sel + 1) % count; UpdateListVisuals(sel, pills, bars, texts); }
            else if (_confirm != null && _confirm.WasPerformedThisFrame())
            { execute(sel); }
            else if (_cancel != null && _cancel.WasPerformedThisFrame())
            { GoBack(); }
        }

        private void UpdateListVisuals(int sel, Image[] pills, Image[] bars, TextMeshProUGUI[] texts)
        {
            if (pills == null) return;
            for (int i = 0; i < pills.Length; i++)
            {
                bool s = i == sel;
                if (pills != null && i < pills.Length) pills[i].color = s ? PillColor  : Color.clear;
                if (bars  != null && i < bars.Length)  bars[i].color  = s ? AccentGold : Color.clear;
                if (texts != null && i < texts.Length) texts[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void GoBack()
        {
            switch (_screen)
            {
                case PauseScreen.Options:  ShowScreen(PauseScreen.Pause);   break;
                case PauseScreen.Sounds:   ShowScreen(PauseScreen.Options); break;
                case PauseScreen.Inputs:   ShowScreen(PauseScreen.Options); break;
                case PauseScreen.LoadGame: ShowScreen(PauseScreen.Pause);   break;
                default:                   ClosePause();                     break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Sounds input
        // ════════════════════════════════════════════════════════════════════

        // Input setup implemented in PauseMenuUI.InputSetup.cs
        partial void SetupInputActions();

        // ════════════════════════════════════════════════════════════════════
        // UI Construction
        // ════════════════════════════════════════════════════════════════════

        // UI builder methods extracted to PauseMenuUI.Builder.cs
        partial void BuildCanvas();

        // Builder helpers extracted to PauseMenuUI.Builder.cs
        partial void RebuildPausePanelRows();
    }
}