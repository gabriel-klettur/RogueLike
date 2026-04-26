using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;
using Valkur.UI.Loading;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        private void HandleKeyboardNavigation()
        {
            if (_menuOptions == null || _menuOptions.Length == 0) return;
            if (_navUpAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex - 1 + _menuOptions.Length) % _menuOptions.Length;
                UpdateSelection();
            }
            else if (_navDownAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex + 1) % _menuOptions.Length;
                UpdateSelection();
            }
            else if (_confirmAction.WasPerformedThisFrame())
            {
                ExecuteOption(_selectedIndex);
            }
        }

        private void UpdateSelection()
        {
            if (_menuOptions == null) return;
            for (int i = 0; i < _menuOptions.Length; i++)
            {
                bool sel = i == _selectedIndex;
                if (_menuTexts  != null && i < _menuTexts.Length)
                    _menuTexts[i].color  = sel ? TextSelected : TextNormal;
                if (_pillImages != null && i < _pillImages.Length)
                    _pillImages[i].color = sel ? PillColor    : Color.clear;
                if (_accentBars != null && i < _accentBars.Length)
                    _accentBars[i].color = sel ? AccentGold   : Color.clear;
            }
        }

        private void ExecuteOption(int index)
        {
            if (index < 0 || _menuOptions == null || index >= _menuOptions.Length) return;
            switch (_menuOptions[index])
            {
                case "Continuar":    ShowMenuScreen(MenuScreen.LoadGame); break;
                case "Nuevo juego":  OpenClassSelector();  break;
                case "Opciones":     ShowMenuScreen(MenuScreen.Options); break;
                case "Salir":        QuitGame();           break;
            }
        }

        private void StartNewGame()
        {
            Debug.Log("[MainMenu] Starting new game...");
            // Clear any stale position checkpoint so the new character spawns at the
            // default spawn point rather than the previous session's last position.
            Valkur.Gameplay.Save.SaveFileManager.DeletePositionCheckpoint();
            TransitionAudioToGame();
            LoadingScreenController.Show(gameplaySceneName);
        }

        private void QuitGame()
        {
            Debug.Log("[MainMenu] Quitting...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenClassSelector()
        {
            if (_classSelectionPanel == null) return;
            // Route through ShowMenuScreen so the main-menu panel and any other
            // sub-screen are hidden, and the class-selector is promoted to the
            // last sibling (drawn on top regardless of canvas creation order).
            ShowMenuScreen(MenuScreen.ClassSelector);
            SetSelectedClassIndex(FindSelectedClassIndex());
        }

        private void CloseClassSelector()
        {
            // Restore the main menu via the single-source-of-truth screen switch.
            ShowMenuScreen(MenuScreen.Main);
        }

        private void HandleClassSelectorInput()
        {
            if (_cancelAction.WasPerformedThisFrame()) { CloseClassSelector(); return; }
            if (_classButtons.Count == 0) return;
            if (_navLeftAction.WasPerformedThisFrame())
                SetSelectedClassIndex(_selectedClassIndex - 1);
            else if (_navRightAction.WasPerformedThisFrame())
                SetSelectedClassIndex(_selectedClassIndex + 1);
            else if (_confirmAction.WasPerformedThisFrame())
                ApplySelectedClassAndStartGame();
        }

        private int FindSelectedClassIndex()
        {
            if (_classKeys.Count == 0) return 0;
            string selectedKey = PlayerSelectionState.SelectedPlayerKey;
            for (int i = 0; i < _classKeys.Count; i++)
                if (string.Equals(_classKeys[i], selectedKey, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        private void SetSelectedClassIndex(int index)
        {
            if (_classButtons.Count == 0) return;
            if (index < 0) index = _classButtons.Count - 1;
            else if (index >= _classButtons.Count) index = 0;
            _selectedClassIndex = index;
            UpdateClassSelectionUI();
        }

        private void OnClassCardClicked(int index)
        {
            SetSelectedClassIndex(index);
            ApplySelectedClassAndStartGame();
        }

        private void UpdateClassSelectionUI()
        {
            // Update card border colors and thickness
            for (int i = 0; i < _classButtons.Count; i++)
            {
                bool selected = i == _selectedClassIndex;

                // Border color: per-class color when selected, gray otherwise
                if (i < _classCardBorderImages.Count)
                {
                    if (selected && i < _classKeys.Count
                        && ClassBorderColors.TryGetValue(_classKeys[i], out var borderCol))
                        _classCardBorderImages[i].color = borderCol;
                    else
                        _classCardBorderImages[i].color = CellBorderUnselected;
                }

                // Border thickness: 4px selected (Python width=4), 2px unselected (Python width=2)
                if (i < _classCardBgRects.Count)
                {
                    float border = selected ? 4f : 2f;
                    _classCardBgRects[i].offsetMin = new Vector2(border, border);
                    _classCardBgRects[i].offsetMax = new Vector2(-border, -border);
                }
            }

            // Update header portrait to match selected class
            if (_classHeaderPortrait != null
                && _selectedClassIndex >= 0
                && _selectedClassIndex < _classKeys.Count)
            {
                var sprite = GetCachedPortraitSprite(_classKeys[_selectedClassIndex]);
                _classHeaderPortrait.sprite = sprite;
                _classHeaderPortrait.color = sprite != null ? Color.white : Color.clear;
            }
        }

        private void ApplySelectedClassAndStartGame()
        {
            if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count) return;
            PlayerSelectionState.SetSelectedPlayer(_classKeys[_selectedClassIndex]);
            CloseClassSelector();
            StartNewGame();
        }

        private void TransitionAudioToGame()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            audio.TransitionMenuToGame();
        }
    }
}
