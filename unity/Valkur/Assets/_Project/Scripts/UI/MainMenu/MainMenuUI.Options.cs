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
        private enum MenuScreen { Main, Options, Sounds, Inputs, LoadGame, ClassSelector }
        private MenuScreen _menuScreen = MenuScreen.Main;

        // ── Options overlay & panels ─────────────────────────────────────────
        private GameObject _optOverlay;
        private GameObject _optPanel;
        private GameObject _optSoundsPanel;
        private GameObject _optInputsPanel;

        // ── Options list ─────────────────────────────────────────────────────
        private readonly string[] _optMenuOptions = { "Inputs", "Sound", "Back" };
        private int      _optMenuSel;
        private Image[]  _optMenuPills;
        private Image[]  _optMenuBars;
        private TextMeshProUGUI[] _optMenuTexts;

        // ── Sounds panel ─────────────────────────────────────────────────────
        private struct SoundRow
        {
            public TextMeshProUGUI valueText;
            public UnityEngine.UI.Slider slider;
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
        // Selected editor sub-tab when the "Editors" main tab is active (0–11).
        private int _optEditorSubTabSel;
        private TextMeshProUGUI[] _optEditorSubTabLabels;

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Single source of truth for which menu screen is visible.
        ///
        /// Every screen has its own root container (<c>_menuPanelGo</c> for
        /// Main, <c>_optOverlay</c> for Options/Sounds/Inputs, <c>_mmLoadOverlay</c>
        /// for LoadGame). Exactly one root is kept active at a time so panels
        /// can never overlap and intercept each other's mouse events.
        ///
        /// Calling this method also bumps the active overlay to the last sibling
        /// so it's drawn (and raycast) on top of any always-on layers (footer,
        /// title, etc.) regardless of when those siblings were created.
        /// </summary>
        private void ShowMenuScreen(MenuScreen screen)
        {
            _menuScreen = screen;
            bool showMain  = screen == MenuScreen.Main;
            bool showOpt   = screen == MenuScreen.Options || screen == MenuScreen.Sounds || screen == MenuScreen.Inputs;
            bool showLoad  = screen == MenuScreen.LoadGame;
            bool showClass = screen == MenuScreen.ClassSelector;

            // Sync legacy input-routing flag with screen state so Update()
            // dispatches keyboard/gamepad input to the correct handler.
            _showingClassSelector = showClass;

            // Main menu panel is hidden whenever a sub-screen is open.
            if (_menuPanelGo        != null) _menuPanelGo.SetActive(showMain);
            if (_optOverlay         != null) _optOverlay.SetActive(showOpt);
            if (_optPanel           != null) _optPanel.SetActive(screen == MenuScreen.Options);
            if (_optSoundsPanel     != null) _optSoundsPanel.SetActive(screen == MenuScreen.Sounds);
            if (_optInputsPanel     != null) _optInputsPanel.SetActive(screen == MenuScreen.Inputs);
            if (_mmLoadOverlay      != null) _mmLoadOverlay.SetActive(showLoad);
            if (_classSelectionPanel != null) _classSelectionPanel.SetActive(showClass);

            // Defensive z-order: the active root is moved to the last sibling so
            // it's always drawn on top of anything created after BuildUI() (e.g.
            // a rebuilt _menuPanelGo after a save was deleted).
            if      (showClass && _classSelectionPanel != null) _classSelectionPanel.transform.SetAsLastSibling();
            else if (showLoad  && _mmLoadOverlay       != null) _mmLoadOverlay.transform.SetAsLastSibling();
            else if (showOpt   && _optOverlay          != null) _optOverlay.transform.SetAsLastSibling();
            else if (showMain  && _menuPanelGo         != null) _menuPanelGo.transform.SetAsLastSibling();

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
                case MenuScreen.LoadGame:
                    // Saves may have been deleted while in the load panel.
                    // Rebuild first (so _menuPanelGo is fresh) then switch screens —
                    // ShowMenuScreen will activate the rebuilt panel and put it on top.
                    RebuildMenuPanel();
                    ShowMenuScreen(MenuScreen.Main);
                    break;
                default: break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Input handlers
        // ════════════════════════════════════════════════════════════════════

        private void HandleOptionsListInput()
        {
            // InputCompat already ORs the new InputSystem with the legacy backend.
            if (Valkur.Core.Input.InputCompat.NavUpPressed())
            { _optMenuSel = (_optMenuSel - 1 + _optMenuOptions.Length) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (Valkur.Core.Input.InputCompat.NavDownPressed())
            { _optMenuSel = (_optMenuSel + 1) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (Valkur.Core.Input.InputCompat.ConfirmPressed())
            { ExecuteOptionsItem(_optMenuSel); }
            else if (Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); }
        }

        private void HandleOptionsSoundsInput()
        {
            if (Valkur.Core.Input.InputCompat.NavUpPressed())
            { _optSoundSel = (_optSoundSel - 1 + _optSoundRows.Count) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (Valkur.Core.Input.InputCompat.NavDownPressed())
            { _optSoundSel = (_optSoundSel + 1) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (Valkur.Core.Input.InputCompat.NavLeftPressed())
            { ChangeOptSound(_optSoundSel, -1); }
            else if (Valkur.Core.Input.InputCompat.NavRightPressed())
            { ChangeOptSound(_optSoundSel, +1); }
            else if (Valkur.Core.Input.InputCompat.ConfirmPressed())
            { GameSettings.Instance?.Save(); ServiceLocator.Get<IAudioService>()?.ApplySettings(); OptionsGoBack(); }
            else if (Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); }
        }

        private void HandleOptionsInputsInput()
        {
            int tabCount = _optTabLabels != null ? _optTabLabels.Length : 0;
            bool tabLeft  = Valkur.Core.Input.KeyboardInputManager.WasQPressedThisFrame();
            bool tabRight = Valkur.Core.Input.KeyboardInputManager.WasEPressedThisFrame();

            if (tabLeft && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel - 1 + tabCount) % tabCount; UpdateOptInputsPanel(); }
            else if (tabRight && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel + 1) % tabCount; UpdateOptInputsPanel(); }
            else if (Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); }
        }

        private void ExecuteOptionsItem(int idx)
        {
            switch (_optMenuOptions[idx])
            {
                case "Inputs": ShowMenuScreen(MenuScreen.Inputs); break;
                case "Sound":  ShowMenuScreen(MenuScreen.Sounds); break;
                case "Back":   ShowMenuScreen(MenuScreen.Main);   break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Panel builders (called from BuildUI)
        // ════════════════════════════════════════════════════════════════════

    }
}