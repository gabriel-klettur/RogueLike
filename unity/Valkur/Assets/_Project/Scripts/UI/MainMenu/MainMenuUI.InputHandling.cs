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
                case "Continuar":    ContinueGame();      break;
                case "Nuevo juego":  OpenClassSelector();  break;
                case "Cargar juego": Debug.Log("[MainMenu] Cargar juego - not yet implemented"); break;
                case "Opciones":     Debug.Log("[MainMenu] Opciones - not yet implemented");     break;
                case "Salir":        QuitGame();           break;
            }
        }

        private void ContinueGame()
        {
            Debug.Log("[MainMenu] Continuing most recent save...");
            SceneTransitionManager.LoadScene(gameplaySceneName);
        }

        private void StartNewGame()
        {
            Debug.Log("[MainMenu] Starting new game...");
            SceneTransitionManager.LoadScene(gameplaySceneName);
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
            _showingClassSelector = true;
            _classSelectionPanel.SetActive(true);
            SetSelectedClassIndex(FindSelectedClassIndex());
        }

        private void CloseClassSelector()
        {
            _showingClassSelector = false;
            if (_classSelectionPanel != null) _classSelectionPanel.SetActive(false);
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
            var hoverColor  = new Color(0.28f, 0.25f, 0.35f, 1f);
            var normalColor = new Color(0.18f, 0.18f, 0.24f, 0.9f);
            for (int i = 0; i < _classButtons.Count; i++)
            {
                bool selected = i == _selectedClassIndex;
                var image = _classButtons[i].GetComponent<Image>();
                if (image != null) image.color = selected ? hoverColor : normalColor;
                if (i < _classMarkerTexts.Count)
                    _classMarkerTexts[i].text = selected
                        ? char.ToUpperInvariant(_classKeys[i][0]).ToString()
                        : string.Empty;
            }
        }

        private void ApplySelectedClassAndStartGame()
        {
            if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count) return;
            PlayerSelectionState.SetSelectedPlayer(_classKeys[_selectedClassIndex]);
            CloseClassSelector();
            StartNewGame();
        }
    }
}
