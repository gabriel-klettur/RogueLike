using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Which screen is on, and how the keyboard reaches it.
    ///
    /// <para><b>One root per screen and exactly one active</b>, so two panels can never overlap
    /// and intercept each other's clicks. That part the shipped menu had right and it is kept
    /// verbatim.</para>
    ///
    /// <para><b>What changed is the dispatch.</b> Choosing a row used to compare its English
    /// label; it now carries an <see cref="OptionsItem"/>. Same fix as the main menu's, same
    /// reason: a label that is also a key cannot be translated.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private enum MenuScreen
        {
            Main,
            Options,
            Audio,
            Video,
            Gameplay,
            Controls,
            LoadGame,
            Credits,
            ClassSelector,
        }

        private enum OptionsItem { Audio, Video, Controls, Gameplay, Back }

        private MenuScreen _menuScreen = MenuScreen.Main;

        private MenuPanelView _optionsPanel;
        private MenuList _optionsList;
        private readonly OptionsItem[] _optionsItems =
        {
            OptionsItem.Audio, OptionsItem.Video, OptionsItem.Controls,
            OptionsItem.Gameplay, OptionsItem.Back,
        };

        /// <summary>The panel a screen is drawn on, or null for the two that own no panel.</summary>
        private MenuPanelView PanelFor(MenuScreen screen)
        {
            switch (screen)
            {
                case MenuScreen.Options: return _optionsPanel;
                case MenuScreen.Audio: return _audioPanel;
                case MenuScreen.Video: return _videoPanel;
                case MenuScreen.Gameplay: return _gameplayPanel;
                case MenuScreen.Controls: return _controlsPanel;
                case MenuScreen.Credits: return _creditsPanel;
                default: return null;
            }
        }

        private static bool IsSubScreen(MenuScreen screen) => screen != MenuScreen.Main;

        /// <summary>
        /// The single source of truth for which screen is visible. Also deepens the veil and
        /// dims the title, so an open panel sits on art that has been darkened ONCE — the
        /// shipped menu stacked a second 55 % black inside every sub-screen's overlay.
        /// </summary>
        private void ShowMenuScreen(MenuScreen screen)
        {
            var previous = _menuScreen;
            EnsureScreenBuilt(screen);
            _menuScreen = screen;

            // Clear EventSystem focus so a Selectable left over from the previous screen (a
            // slider clicked in Audio, say) cannot intercept keyboard navigation in the new one
            // through OnMove / OnCancel dispatch.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);

            _showingClassSelector = screen == MenuScreen.ClassSelector;

            if (_menuPanelGo != null) _menuPanelGo.SetActive(screen == MenuScreen.Main);
            if (_mmLoadOverlay != null) _mmLoadOverlay.SetActive(screen == MenuScreen.LoadGame);
            if (_classSelectionPanel != null) _classSelectionPanel.SetActive(_showingClassSelector);

            // Only panels that EXIST. A screen nobody has opened has no panel, and building one
            // here in order to close it would undo the whole point of building on demand.
            foreach (MenuScreen s in System.Enum.GetValues(typeof(MenuScreen)))
            {
                var panel = PanelFor(s);
                if (panel == null) continue;
                if (s == screen) panel.Open();
                else panel.Close();
            }

            SetScrimForScreen(IsSubScreen(screen));

            // Defensive z-order: whatever is on goes last, so it is drawn and raycast above any
            // sibling created after BuildUI (a rebuilt main panel, for one).
            var active = PanelFor(screen);
            if (active != null) active.Root.SetAsLastSibling();
            else if (screen == MenuScreen.LoadGame && _mmLoadOverlay != null)
                _mmLoadOverlay.transform.SetAsLastSibling();
            else if (_showingClassSelector && _classSelectionPanel != null)
                _classSelectionPanel.transform.SetAsLastSibling();
            else if (screen == MenuScreen.Main && _menuPanelGo != null)
                _menuPanelGo.transform.SetAsLastSibling();

            switch (screen)
            {
                case MenuScreen.Options: if (_optionsList != null) _optionsList.Index = 0; break;
                case MenuScreen.Audio:
                    RefreshAudioRows(); if (_audioList != null) _audioList.Index = 0; break;
                case MenuScreen.Video:
                    LoadVideoFromSettings(); RefreshVideoRows();
                    if (_videoList != null) _videoList.Index = 0; break;
                case MenuScreen.Gameplay:
                    RefreshGameplayRows(); if (_gameplayList != null) _gameplayList.Index = 0; break;
                case MenuScreen.Controls: RefreshControlsRows(); break;
                case MenuScreen.LoadGame: RefreshMMLoadPanel(); break;
            }

            if (previous != screen && _sfx != null)
            {
                if (screen == MenuScreen.Main && IsSubScreen(previous)) _sfx.Cancel();
                else if (IsSubScreen(screen)) _sfx.Confirm();
            }
        }

        private void OptionsGoBack()
        {
            switch (_menuScreen)
            {
                case MenuScreen.Options:
                case MenuScreen.Credits:
                    ShowMenuScreen(MenuScreen.Main);
                    break;
                case MenuScreen.Audio:
                case MenuScreen.Video:
                case MenuScreen.Gameplay:
                    ShowMenuScreen(MenuScreen.Options);
                    break;
                case MenuScreen.Controls:
                    if (CancelControlsCapture()) return;   // Esc first cancels a rebind
                    ShowMenuScreen(MenuScreen.Options);
                    break;
                case MenuScreen.LoadGame:
                    // Saves may have been deleted in there, so the main panel is rebuilt BEFORE
                    // the switch: ShowMenuScreen then activates the fresh one and puts it on top.
                    RebuildMenuPanel();
                    ShowMenuScreen(MenuScreen.Main);
                    break;
            }
        }

        /// <summary>
        /// Builds a screen the first time it is asked for.
        ///
        /// <para>Everything used to be built in <c>Start</c>, whether or not the player ever
        /// opened it. The four option panels alone carry a slider, a value column and a hit
        /// target per row; the class selector carries six cards of six bars each. None of it is
        /// reachable until somebody chooses a row, and uGUI charges for it at <c>Start</c>
        /// either way.</para>
        ///
        /// <para><b>The panels are grouped the way they are REACHED, not one by one.</b> Opening
        /// Options and then finding Audio unbuilt would cost a hitch in the middle of a
        /// navigation the player is already performing; the four are built together the moment
        /// the Options screen is first shown, which is one hitch behind a keypress that already
        /// changed the screen.</para>
        /// </summary>
        private void EnsureScreenBuilt(MenuScreen screen)
        {
            if (_canvasTransform == null) return;
            switch (screen)
            {
                case MenuScreen.Options:
                case MenuScreen.Audio:
                case MenuScreen.Video:
                case MenuScreen.Gameplay:
                case MenuScreen.Controls:
                    if (_optionsPanel == null) BuildOptionsSubmenu(_canvasTransform);
                    break;
                case MenuScreen.LoadGame:
                    if (_mmLoadOverlay == null) BuildLoadGameSubmenu(_canvasTransform);
                    break;
                case MenuScreen.Credits:
                    if (_creditsPanel == null) BuildCreditsPanel(_canvasTransform);
                    break;
                case MenuScreen.ClassSelector:
                    if (_classSelectionPanel == null) BuildClassSelectorPanel(_canvasTransform);
                    break;
            }
        }

        /// <summary>
        /// Builds every screen at once. The fixtures use it so a test about visibility does not
        /// also have to be a test about lazy construction, and nothing in production calls it.
        /// </summary>
        internal void BuildAllScreensForTests()
        {
            foreach (MenuScreen s in System.Enum.GetValues(typeof(MenuScreen)))
                EnsureScreenBuilt(s);
        }

        // ── Options list ─────────────────────────────────────────────────────

        private void BuildOptionsSubmenu(Transform canvas)
        {
            var style = Style;
            _optionsPanel = new MenuPanelView(canvas, _art, style, MenuText.OptionsTitle,
                                              style.menuPanelWidth + 90f, ReduceMotion);
            _optionsList = new MenuList(_optionsPanel.Body, _art, style, ReduceMotion);
            foreach (var item in _optionsItems)
                _optionsList.Add(_art, OptionsLabel(item));

            _optionsList.Changed += _ => _sfx?.Move();
            _optionsList.Chosen += ExecuteOptionsItem;
            _optionsPanel.FitToContent(_optionsList.ContentHeight);
            _optionsList.SetViewport(_optionsPanel.BodyHeight);
            _optionsPanel.SetHint(MenuText.MainMenuHint);
            _optionsPanel.Close();

            BuildAudioPanel(canvas);
            BuildVideoPanel(canvas);
            BuildGameplayPanel(canvas);
            BuildControlsPanel(canvas);
        }

        private static string OptionsLabel(OptionsItem item)
        {
            switch (item)
            {
                case OptionsItem.Audio: return MenuText.OptionsAudio;
                case OptionsItem.Video: return MenuText.OptionsVideo;
                case OptionsItem.Controls: return MenuText.OptionsControls;
                case OptionsItem.Gameplay: return MenuText.OptionsGameplay;
                default: return MenuText.OptionsBack;
            }
        }

        private void ExecuteOptionsItem(int index)
        {
            if (index < 0 || index >= _optionsItems.Length) return;
            switch (_optionsItems[index])
            {
                case OptionsItem.Audio: ShowMenuScreen(MenuScreen.Audio); break;
                case OptionsItem.Video: ShowMenuScreen(MenuScreen.Video); break;
                case OptionsItem.Controls: ShowMenuScreen(MenuScreen.Controls); break;
                case OptionsItem.Gameplay: ShowMenuScreen(MenuScreen.Gameplay); break;
                default: ShowMenuScreen(MenuScreen.Main); break;
            }
        }

        // ── Input ────────────────────────────────────────────────────────────

        /// <summary>
        /// Up / down / confirm / cancel for any list-shaped screen. One implementation, so every
        /// list in the menu wraps at the same edge and plays the same sound — the five shipped
        /// screens each had their own copy of this and they had already drifted.
        /// </summary>
        private bool HandleListInput(MenuList list, System.Action onCancel)
        {
            if (list == null) return false;
            if (InputCompat.NavUpPressed()) { list.MoveBy(-1); return true; }
            if (InputCompat.NavDownPressed()) { list.MoveBy(1); return true; }
            if (InputCompat.ConfirmPressed())
            {
                if (!list.ChooseCurrent()) _sfx?.Refuse();
                return true;
            }
            if (InputCompat.CancelPressed()) { onCancel?.Invoke(); return true; }
            return false;
        }

        private void HandleOptionsListInput() => HandleListInput(_optionsList, OptionsGoBack);
    }
}
