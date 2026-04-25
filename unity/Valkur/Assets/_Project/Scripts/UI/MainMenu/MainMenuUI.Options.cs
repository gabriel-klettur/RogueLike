using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options sub-menu for the main menu.
    /// Mirrors Python: Opciones → Inputs / Sonido / Volver.
    /// Each sub-screen uses the same visual style as PauseMenuUI.
    /// </summary>
    public partial class MainMenuUI
    {
        // ── Screen state ─────────────────────────────────────────────────────
        private enum MenuScreen { Main, Options, Sounds, Inputs, LoadGame }
        private MenuScreen _menuScreen = MenuScreen.Main;

        // ── Options overlay & panels ─────────────────────────────────────────
        private GameObject _optOverlay;
        private GameObject _optPanel;
        private GameObject _optSoundsPanel;
        private GameObject _optInputsPanel;

        // ── Options list ─────────────────────────────────────────────────────
        private readonly string[] _optMenuOptions = { "Inputs", "Sonido", "Volver" };
        private int      _optMenuSel;
        private Image[]  _optMenuPills;
        private Image[]  _optMenuBars;
        private TextMeshProUGUI[] _optMenuTexts;

        // ── Sounds panel ─────────────────────────────────────────────────────
        private struct SoundRow
        {
            public TextMeshProUGUI valueText;
            public float min, max, step;
            public System.Func<float> get;
            public System.Action<float> set;
        }
        private readonly List<SoundRow> _optSoundRows = new List<SoundRow>();
        private int      _optSoundSel;
        private Image[]  _optSoundPills;
        private Image[]  _optSoundBars;
        private TextMeshProUGUI[] _optSoundLabels;

        // ── Inputs panel ─────────────────────────────────────────────────────
        private int _optInputsTabSel;
        private TextMeshProUGUI[] _optTabLabels;

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        private void ShowMenuScreen(MenuScreen screen)
        {
            _menuScreen = screen;
            bool showOpt = screen == MenuScreen.Options || screen == MenuScreen.Sounds || screen == MenuScreen.Inputs;
            bool showLoad = screen == MenuScreen.LoadGame;
            if (_optOverlay != null) _optOverlay.SetActive(showOpt);
            if (_optPanel != null) _optPanel.SetActive(screen == MenuScreen.Options);
            if (_optSoundsPanel != null) _optSoundsPanel.SetActive(screen == MenuScreen.Sounds);
            if (_optInputsPanel != null) _optInputsPanel.SetActive(screen == MenuScreen.Inputs);
            if (_mmLoadOverlay != null) _mmLoadOverlay.SetActive(showLoad);

            if (screen == MenuScreen.Options)
            { _optMenuSel = 0; UpdateOptListVisuals(); }
            if (screen == MenuScreen.Sounds)
            { _optSoundSel = 0; UpdateOptSoundsVisuals(); }
            if (screen == MenuScreen.Inputs)
            { _optInputsTabSel = 0; UpdateOptInputsPanel(); }
            if (screen == MenuScreen.LoadGame)
            { RefreshMMLoadPanel(); }
        }

        private void OptionsGoBack()
        {
            switch (_menuScreen)
            {
                case MenuScreen.Options:  ShowMenuScreen(MenuScreen.Main); break;
                case MenuScreen.Sounds:   ShowMenuScreen(MenuScreen.Options); break;
                case MenuScreen.Inputs:   ShowMenuScreen(MenuScreen.Options); break;
                case MenuScreen.LoadGame: ShowMenuScreen(MenuScreen.Main); break;
                default: break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Input handlers
        // ════════════════════════════════════════════════════════════════════

        private void HandleOptionsListInput()
        {
            if (_navUpAction.WasPerformedThisFrame())
            { _optMenuSel = (_optMenuSel - 1 + _optMenuOptions.Length) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (_navDownAction.WasPerformedThisFrame())
            { _optMenuSel = (_optMenuSel + 1) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (_confirmAction.WasPerformedThisFrame())
            { ExecuteOptionsItem(_optMenuSel); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void HandleOptionsSoundsInput()
        {
            if (_navUpAction.WasPerformedThisFrame())
            { _optSoundSel = (_optSoundSel - 1 + _optSoundRows.Count) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (_navDownAction.WasPerformedThisFrame())
            { _optSoundSel = (_optSoundSel + 1) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (_navLeftAction.WasPerformedThisFrame())
            { ChangeOptSound(_optSoundSel, -1); }
            else if (_navRightAction.WasPerformedThisFrame())
            { ChangeOptSound(_optSoundSel, +1); }
            else if (_confirmAction.WasPerformedThisFrame())
            { GameSettings.Instance?.Save(); ServiceLocator.Get<IAudioService>()?.ApplySettings(); OptionsGoBack(); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void HandleOptionsInputsInput()
        {
            int tabCount = _optTabLabels != null ? _optTabLabels.Length : 0;
            bool tabLeft  = UnityEngine.InputSystem.Keyboard.current != null &&
                            UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame;
            bool tabRight = UnityEngine.InputSystem.Keyboard.current != null &&
                            UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame;

            if (tabLeft && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel - 1 + tabCount) % tabCount; UpdateOptInputsPanel(); }
            else if (tabRight && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel + 1) % tabCount; UpdateOptInputsPanel(); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void ExecuteOptionsItem(int idx)
        {
            switch (_optMenuOptions[idx])
            {
                case "Inputs": ShowMenuScreen(MenuScreen.Inputs); break;
                case "Sonido": ShowMenuScreen(MenuScreen.Sounds); break;
                case "Volver": ShowMenuScreen(MenuScreen.Main);   break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Panel builders (called from BuildUI)
        // ════════════════════════════════════════════════════════════════════

    }
}