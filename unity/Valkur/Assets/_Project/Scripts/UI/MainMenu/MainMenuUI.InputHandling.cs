using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Save;
using Valkur.UI.Loading;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        private void HandleKeyboardNavigation()
        {
            if (_menuList == null) return;
            if (InputCompat.NavUpPressed()) { _menuList.MoveBy(-1); return; }
            if (InputCompat.NavDownPressed()) { _menuList.MoveBy(1); return; }
            if (InputCompat.ConfirmPressed())
            {
                if (!_menuList.ChooseCurrent()) _sfx?.Refuse();
            }
        }

        /// <summary>Kept for the fixtures and the rebuild path; the list owns the visuals now.</summary>
        private void UpdateSelection()
        {
            if (_menuList == null) return;
            _menuList.Index = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _menuList.Count - 1));
        }

        /// <summary>
        /// Runs the row the player chose. Dispatch is on the <see cref="MainMenuItem"/> the row
        /// carries — never on its label, which is what made the shipped menu untranslatable.
        /// </summary>
        private void ExecuteOption(int index)
        {
            if (index < 0 || index >= _menuItems.Count) return;
            switch (_menuItems[index])
            {
                case MainMenuItem.Continue: ContinueMostRecentRun(); break;
                case MainMenuItem.LoadGame: ShowMenuScreen(MenuScreen.LoadGame); break;
                case MainMenuItem.NewGame: ChooseNewGame(seeded: false); break;
                case MainMenuItem.SeededNewGame: ChooseNewGame(seeded: true); break;
                case MainMenuItem.Options: ShowMenuScreen(MenuScreen.Options); break;
                case MainMenuItem.Credits: ShowMenuScreen(MenuScreen.Credits); break;
                case MainMenuItem.Exit: QuitGame(); break;
            }
        }

        /// <summary>
        /// Loads the newest save outright. "Continue" used to open the load browser, which is a
        /// different promise: a player who wants back into their run should not have to pick it
        /// out of a list of its own autosaves.
        /// </summary>
        private void ContinueMostRecentRun()
        {
            WithdrawSeededWorld();
            var runs = SaveFileManager.ListSavesByRun();
            foreach (var run in runs)
            {
                if (run?.saves == null) continue;
                foreach (var save in run.saves)
                {
                    if (save.isCorrupted) continue;
                    PendingSaveLoad.Path = save.path;
                    PendingSaveLoad.PlayerClass = save.playerClass;
                    BeginTransitionToGame();
                    return;
                }
            }
            // Every save unreadable. Falling through to the browser is the honest answer: it is
            // the one screen that can say WHICH of them is damaged and offer to delete it.
            _sfx?.Refuse();
            ShowMenuScreen(MenuScreen.LoadGame);
        }

        private void StartNewGame()
        {
            // Clear any stale position checkpoint so the new character spawns at the default
            // spawn point rather than the previous session's last position.
            SaveFileManager.DeletePositionCheckpoint();
            // And reset the Map Editor's active-slot pointer, so a previous session's custom
            // slot does not leak into the fresh playthrough.
            Valkur.Gameplay.MapEditor.MapEditorManager.ResetActiveSlotToDefaultOnDisk();
            // The seeded row's generated world, or nothing: the request waits for the next boot.
            ArmOrWithdrawSeededWorld();
            BeginTransitionToGame();
        }

        /// <summary>
        /// The one door into the game. The title sweeps, the start sound plays under it, and the
        /// loading screen takes over — so the last thing the player sees in the menu is the name
        /// of the game lighting up rather than a cut.
        /// </summary>
        private void BeginTransitionToGame()
        {
            _title?.Sweep();
            _sfx?.Start();
            TransitionAudioToGame();
            LoadingScreenController.Show(gameplaySceneName);
        }

        private void QuitGame()
        {
            _sfx?.Cancel();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenClassSelector()
        {
            // Built on demand: ShowMenuScreen does it, so the early return this used to have —
            // "no panel, do nothing" — would now refuse the one path that creates it.
            ShowMenuScreen(MenuScreen.ClassSelector);
            if (_classSelectionPanel == null) return;
            SetSelectedClassIndex(FindSelectedClassIndex());
        }

        private void CloseClassSelector() => ShowMenuScreen(MenuScreen.Main);

        private void HandleClassSelectorInput()
        {
            if (InputCompat.CancelPressed()) { CloseClassSelector(); return; }
            if (_classButtons.Count == 0) return;
            if (InputCompat.NavLeftPressed()) SetSelectedClassIndex(_selectedClassIndex - 1);
            else if (InputCompat.NavRightPressed()) SetSelectedClassIndex(_selectedClassIndex + 1);
            else if (InputCompat.ConfirmPressed()) ApplySelectedClassAndStartGame();
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
            if (index == _selectedClassIndex) return;
            _selectedClassIndex = index;
            _sfx?.Move();
            UpdateClassSelectionUI();
        }

        private void OnClassCardClicked(int index)
        {
            if (index != _selectedClassIndex) { SetSelectedClassIndex(index); return; }
            // A second click on the card that is already chosen is the confirmation. One click
            // that both selects and STARTS the game is how a player loses a run to a mis-click.
            ApplySelectedClassAndStartGame();
        }

        private void UpdateClassSelectionUI()
        {
            var style = Style;
            for (int i = 0; i < _classCardFrames.Count; i++)
            {
                bool selected = i == _selectedClassIndex;
                // ONE selection colour for all six cards. The shipped selector tinted the border
                // with the class's own colour, so "chosen" was red on the barbarian, green on the
                // elf and pink on the valkyrie — a state the player could never learn to read,
                // and red in particular reads as an error.
                _classCardFrames[i].color = selected ? style.Gold : new Color(1f, 1f, 1f, 0.55f);
                if (i < _classCardAccents.Count && _classCardAccents[i] != null)
                {
                    var c = _classCardAccents[i].color;
                    c.a = selected ? 1f : 0.5f;
                    _classCardAccents[i].color = c;
                }
                var rt = _classCardFrames[i].rectTransform;
                rt.localScale = selected ? new Vector3(1.03f, 1.03f, 1f) : Vector3.one;
            }

            if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count) return;
            string key = _classKeys[_selectedClassIndex];

            if (_classHeaderPortrait != null)
            {
                var sprite = GetCachedPortraitSprite(key);
                _classHeaderPortrait.sprite = sprite;
                _classHeaderPortrait.color = sprite != null ? Color.white : Color.clear;
            }

            if (_classChosenName != null)
            {
                PlayerClassCatalog.TryGetPreset(key, out var preset);
                _classChosenName.text = string.IsNullOrEmpty(preset.DisplayName)
                    ? key : preset.DisplayName;
            }

            if (_classDescription != null)
                _classDescription.text = MenuClassCopy.Describe(key);
        }

        private void ApplySelectedClassAndStartGame()
        {
            if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count) return;
            PlayerSelectionState.SetSelectedPlayer(_classKeys[_selectedClassIndex]);

            // A burst in the class's own colour at the card that was chosen — the one moment in
            // the menu where the identity colour means something, because the player just acted.
            if (_fx != null && !ReduceMotion && _selectedClassIndex < _classCardFrames.Count)
            {
                var accent = ClassAccent.TryGetValue(_classKeys[_selectedClassIndex], out var c)
                    ? c : Style.Gold;
                BurstAtCanvasRect(_classCardFrames[_selectedClassIndex].rectTransform, 18, accent);
            }

            _showingClassSelector = false;
            if (_classSelectionPanel != null) _classSelectionPanel.SetActive(false);
            StartNewGame();
        }

        private void BurstAtCanvasRect(RectTransform rt, int count, Color colour)
        {
            if (_fx == null) return;
            _fx.Burst(MoteSpaceOf(rt), count, colour, 120f, 0.75f, MenuMoteShape.Spark);
        }

        private void TransitionAudioToGame()
            => ServiceLocator.Get<IAudioService>()?.TransitionMenuToGame();
    }
}
