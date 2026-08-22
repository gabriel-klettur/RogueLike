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
                case PauseScreen.Video:   HandleVideoInput();   break;
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
            // Both `P` (the configured pause toggle) and `Esc` close the menu
            // from the top-level list — Esc mirrors clicking "Continue". We
            // dual-read via _cancel.InputAction AND InputCompat (device-level)
            // because the InputAction state can desync when the EventSystem
            // holds a Selectable focus from a previous mouse click.
            if ((_pauseAction != null && _pauseAction.WasPerformedThisFrame())
             || (_cancel != null && _cancel.WasPerformedThisFrame())
             || Valkur.Core.Input.InputCompat.CancelPressed())
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
            if (Instance == this)
            {
                Valkur.Core.ServiceLocator.Unregister<Valkur.Core.Services.IPauseMenuService>();
                Instance = null;
            }
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

        /// <summary>
        /// Opens the pause overlay and jumps directly to the Load Game sub-screen.
        /// Invoked by the General Editor launcher's "Cargar" button so the user
        /// lands on the save list with one click instead of two.
        /// </summary>
        public void OpenLoadGame()
        {
            RebuildPauseOptions();
            ShowScreen(PauseScreen.LoadGame);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(true);
        }

        /// <summary>
        /// Opens the pause overlay and jumps directly to the Options sub-screen
        /// (Inputs / Sounds / Volver). Mirrors <see cref="OpenLoadGame"/>.
        /// </summary>
        public void OpenOptions()
        {
            RebuildPauseOptions();
            ShowScreen(PauseScreen.Options);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(true);
        }

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        private void ShowScreen(PauseScreen s)
        {
            _screen = s;

            // Clear EventSystem focus so a Selectable left over from the
            // previous screen (e.g. a slider clicked in Sound Options) can't
            // intercept keyboard navigation in the new screen via OnMove /
            // OnCancel dispatch.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);

            _overlayRoot?.SetActive(s != PauseScreen.None);
            _pausePanel?.SetActive(s == PauseScreen.Pause);
            _optionsPanel?.SetActive(s == PauseScreen.Options);
            _soundsPanel?.SetActive(s == PauseScreen.Sounds);
            _videoPanel?.SetActive(s == PauseScreen.Video);
            _inputsPanel?.SetActive(s == PauseScreen.Inputs);
            _loadGamePanel?.SetActive(s == PauseScreen.LoadGame);

            if (s == PauseScreen.Pause)   { _pauseSel = 0;  UpdateListVisuals(_pauseSel,  _pausePills,  _pauseBars,  _pauseTexts); }
            if (s == PauseScreen.Options) { _optSel = 0;    UpdateListVisuals(_optSel,    _optPills,    _optBars,    _optTexts);   }
            if (s == PauseScreen.Sounds)  { _soundSel = 0;  UpdateSoundsPanel(); }
            if (s == PauseScreen.Video)   { _videoSel = 0;  LoadVideoFromSettings(); RefreshVideoRows(); UpdateVideoPanel(); }
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
            var opts = new List<string> { "Continue", "New Game", "Save Game" };
            if (hasSaves) opts.Add("Load Game");
            opts.Add("Options");
            opts.Add("Exit");
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
            else if ((_cancel != null && _cancel.WasPerformedThisFrame())
                  || Valkur.Core.Input.InputCompat.CancelPressed())
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
                case PauseScreen.Video:    ShowScreen(PauseScreen.Options); break;
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