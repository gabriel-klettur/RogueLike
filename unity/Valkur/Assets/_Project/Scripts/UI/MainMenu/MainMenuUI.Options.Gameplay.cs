using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options → Gameplay: language, reduce motion, screen shake, text size, hints.
    ///
    /// <para><b>It did not exist.</b> The shipped Options had three rows — Inputs, Sound, Video
    /// — so the game had no language control outside the chat panel (where an EN/ES button had
    /// been persisting a per-NPC preference that only a remote model ever read), no
    /// accessibility settings of any kind, and no way to turn the camera shake off.</para>
    ///
    /// <para><b>Reduce motion is honoured immediately, not on the next launch.</b> A setting
    /// whose effect the player cannot see while they are looking at the switch is one they
    /// cannot evaluate — so the title stops assembling, the motes stop and the panels stop
    /// animating on the frame it is toggled.</para>
    ///
    /// <para><b>Language rebuilds the menu.</b> Every label in it is a property read once at
    /// build time; re-reading them all in place would mean a setter per label, which is the
    /// parallel model this whole pass exists to remove. One rebuild, on a screen with no state
    /// worth preserving beyond which row was selected.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private enum GameplayRow { Language, ReduceMotion, ScreenShake, TextSize, ShowHints, Back }

        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly GameplayRow[] GameplayRows =
        {
            GameplayRow.Language, GameplayRow.ReduceMotion, GameplayRow.ScreenShake,
            GameplayRow.TextSize, GameplayRow.ShowHints, GameplayRow.Back,
        };

        private MenuPanelView _gameplayPanel;
        private MenuList _gameplayList;
        private TextMeshProUGUI _gameplayNote;

        private void BuildGameplayPanel(Transform canvas)
        {
            var style = Style;
            _gameplayPanel = new MenuPanelView(canvas, _art, style, MenuText.GameplayTitle,
                                               style.panelWidth, ReduceMotion);
            _gameplayList = new MenuList(_gameplayPanel.Body, _art, style, ReduceMotion);

            foreach (var row in GameplayRows)
            {
                var r = _gameplayList.Add(_art, GameplayLabel(row));
                if (row != GameplayRow.Back) r.Value.text = string.Empty;
            }

            _gameplayList.Changed += OnGameplaySelectionChanged;
            _gameplayList.Chosen += OnGameplayChosen;

            _gameplayPanel.FitToContent(_gameplayList.ContentHeight, 40f);
            _gameplayList.SetViewport(_gameplayPanel.BodyHeight);

            var noteRt = MenuUIKit.Rect("Note", _gameplayPanel.Root);
            noteRt.anchorMin = new Vector2(0f, 0f);
            noteRt.anchorMax = new Vector2(1f, 0f);
            noteRt.pivot = new Vector2(0.5f, 0f);
            noteRt.anchoredPosition = new Vector2(0f, style.hintBarHeight + 2f);
            noteRt.sizeDelta = new Vector2(-24f, 34f);
            _gameplayNote = MenuTypography.Label(noteRt.gameObject, style, string.Empty,
                                                 style.detailFontSize, style.TextMuted,
                                                 TextAlignmentOptions.Center);
            _gameplayNote.enableWordWrapping = true;

            _gameplayPanel.SetHint(MenuText.AudioHint);
            _gameplayPanel.Close();
            RefreshGameplayRows();
        }

        private static string GameplayLabel(GameplayRow row)
        {
            switch (row)
            {
                case GameplayRow.Language: return MenuText.GameplayLanguage;
                case GameplayRow.ReduceMotion: return MenuText.GameplayReduceMotion;
                case GameplayRow.ScreenShake: return MenuText.GameplayScreenShake;
                case GameplayRow.TextSize: return MenuText.GameplayTextSize;
                case GameplayRow.ShowHints: return MenuText.GameplayShowHints;
                default: return MenuText.OptionsBack;
            }
        }

        private void RefreshGameplayRows()
        {
            if (_gameplayList == null) return;   // not built yet: nothing to repaint
            var gs = GameSettings.Instance;
            var rows = _gameplayList.Rows;
            for (int i = 0; i < GameplayRows.Length && i < rows.Count; i++)
            {
                string text;
                bool? toggle = null;
                switch (GameplayRows[i])
                {
                    case GameplayRow.Language:
                        text = GameLanguage.IsEnglish ? MenuText.LanguageEnglish : MenuText.LanguageSpanish;
                        break;
                    case GameplayRow.ReduceMotion:
                        toggle = gs != null && gs.reduceMotion;
                        text = toggle.Value ? MenuText.VideoOn : MenuText.VideoOff;
                        break;
                    case GameplayRow.ScreenShake:
                        toggle = gs == null || gs.screenShake;
                        text = toggle.Value ? MenuText.VideoOn : MenuText.VideoOff;
                        break;
                    case GameplayRow.TextSize:
                        text = gs == null || gs.textSize == 0 ? MenuText.TextSizeNormal
                             : gs.textSize < 0 ? MenuText.TextSizeSmall : MenuText.TextSizeLarge;
                        break;
                    case GameplayRow.ShowHints:
                        toggle = gs == null || gs.showHints;
                        text = toggle.Value ? MenuText.VideoOn : MenuText.VideoOff;
                        break;
                    default:
                        text = string.Empty;
                        break;
                }
                rows[i].Value.text = text;
                if (toggle.HasValue) rows[i].SetToggle(toggle.Value);
            }
            OnGameplaySelectionChanged(_gameplayList.Index);
        }

        /// <summary>The note under the list explains the row being looked at, not all of them.</summary>
        private void OnGameplaySelectionChanged(int index)
        {
            _sfx?.Move();
            if (_gameplayNote == null) return;
            _gameplayNote.text = index >= 0 && index < GameplayRows.Length
                                 && GameplayRows[index] == GameplayRow.ReduceMotion
                ? MenuText.GameplayReduceMotionNote
                : string.Empty;
        }

        private void OnGameplayChosen(int index) => ChangeGameplay(index, +1);

        private void ChangeGameplay(int index, int dir)
        {
            if (index < 0 || index >= GameplayRows.Length) return;
            var gs = GameSettings.Instance;

            switch (GameplayRows[index])
            {
                case GameplayRow.Language:
                    GameLanguage.Toggle();
                    _sfx?.Confirm();
                    RebuildForLanguage();
                    return;

                case GameplayRow.ReduceMotion:
                    if (gs != null) { gs.reduceMotion = !gs.reduceMotion; gs.Save(); }
                    ApplyReduceMotion();
                    break;

                case GameplayRow.ScreenShake:
                    if (gs != null) { gs.screenShake = !gs.screenShake; gs.Save(); }
                    break;

                case GameplayRow.TextSize:
                    if (gs != null)
                    {
                        gs.textSize = Mathf.Clamp(gs.textSize + dir, -1, 1);
                        if (gs.textSize > 1) gs.textSize = -1;
                        if (gs.textSize < -1) gs.textSize = 1;
                        gs.Save();
                    }
                    break;

                case GameplayRow.ShowHints:
                    if (gs != null) { gs.showHints = !gs.showHints; gs.Save(); }
                    ApplyHintVisibility();
                    break;

                default:
                    ShowMenuScreen(MenuScreen.Options);
                    return;
            }

            _sfx?.Confirm();
            RefreshGameplayRows();
        }

        /// <summary>Pushes the accessibility switch into everything that moves, right now.</summary>
        private void ApplyReduceMotion()
        {
            bool reduce = ReduceMotion;
            _title?.SetReduceMotion(reduce);
            _menuList?.SetReduceMotion(reduce);
            _optionsList?.SetReduceMotion(reduce);
            _audioList?.SetReduceMotion(reduce);
            _videoList?.SetReduceMotion(reduce);
            _gameplayList?.SetReduceMotion(reduce);
            _controlsList?.SetReduceMotion(reduce);
            _optionsPanel?.SetReduceMotion(reduce);
            _audioPanel?.SetReduceMotion(reduce);
            _videoPanel?.SetReduceMotion(reduce);
            _gameplayPanel?.SetReduceMotion(reduce);
            _controlsPanel?.SetReduceMotion(reduce);
            _creditsPanel?.SetReduceMotion(reduce);
            if (reduce) _fx?.Clear();
        }

        /// <summary>
        /// Rebuilds the whole canvas in the new language and comes back to the same screen.
        /// Blunt on purpose: the alternative is a setter beside every label, i.e. a second copy
        /// of the layout that has to be kept in step with the first.
        /// </summary>
        private void RebuildForLanguage()
        {
            var screen = _menuScreen;
            if (_canvasTransform != null) MenuUIKit.Destroy(_canvasTransform.gameObject);
            _canvasTransform = null;
            _menuList = null;
            _optionsList = _audioList = _videoList = _gameplayList = _controlsList = null;
            _optionsPanel = _audioPanel = _videoPanel = _gameplayPanel = _controlsPanel = _creditsPanel = null;

            BuildMenuOptions();
            BuildUI();
            // The press-to-start overlay is only for the first arrival; a language change is not
            // a new session and putting the player back behind it would be a punishment.
            DismissPressToStart(silent: true);
            ShowMenuScreen(screen == MenuScreen.Main ? MenuScreen.Gameplay : screen);
        }

        private void HandleGameplayInput()
        {
            if (InputCompat.NavLeftPressed()) { ChangeGameplay(_gameplayList.Index, -1); return; }
            if (InputCompat.NavRightPressed()) { ChangeGameplay(_gameplayList.Index, +1); return; }
            HandleListInput(_gameplayList, OptionsGoBack);
        }
    }
}
