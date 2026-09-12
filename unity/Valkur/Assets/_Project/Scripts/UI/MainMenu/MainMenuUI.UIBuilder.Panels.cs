using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Gameplay.Save;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The main menu's own panel, the footer and the credits sheet.
    ///
    /// <para><b>The rows are dispatched by an ENUM, never by their label.</b> The shipped menu
    /// did <c>switch (_menuOptions[index]) { case "New Game": ... }</c> and the pause menu did
    /// the same — so the text on screen was also the key to the logic, translating the menu
    /// broke the navigation, and <c>MainMenuUITests</c> pinned the English with
    /// <c>Assert.Contains("New Game")</c>. A label that is also a key is a label nobody can
    /// change.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        /// <summary>What a main-menu row MEANS. The label is just how it is spelled today.</summary>
        private enum MainMenuItem
        {
            Continue,
            LoadGame,
            NewGame,
            Options,
            Credits,
            Exit,
        }

        private readonly List<MainMenuItem> _menuItems = new List<MainMenuItem>();
        private MenuList _menuList;
        private MenuPanelView _creditsPanel;
        private TextMeshProUGUI _footerHint;
        private TextMeshProUGUI _footerVersion;

        private void BuildMenuOptions()
        {
            _menuItems.Clear();
            bool hasSaves = SaveFileManager.ListSaves().Count > 0;

            // "Continue" CONTINUES — it loads the newest save. The shipped menu used that word
            // for a row that opened a file browser, which is a different promise: a player who
            // wants to get back into their run should not have to pick their run out of a list.
            if (hasSaves)
            {
                _menuItems.Add(MainMenuItem.Continue);
                _menuItems.Add(MainMenuItem.LoadGame);
            }
            _menuItems.Add(MainMenuItem.NewGame);
            _menuItems.Add(MainMenuItem.Options);
            _menuItems.Add(MainMenuItem.Credits);
            _menuItems.Add(MainMenuItem.Exit);

            _menuOptions = new string[_menuItems.Count];
            for (int i = 0; i < _menuItems.Count; i++) _menuOptions[i] = LabelFor(_menuItems[i]);
        }

        private static string LabelFor(MainMenuItem item)
        {
            switch (item)
            {
                case MainMenuItem.Continue: return MenuText.Continue;
                case MainMenuItem.LoadGame: return MenuText.LoadGame;
                case MainMenuItem.NewGame: return MenuText.NewGame;
                case MainMenuItem.Options: return MenuText.Options;
                case MainMenuItem.Credits: return MenuText.Credits;
                default: return MenuText.Exit;
            }
        }

        private void BuildMenuPanel(Transform canvas)
        {
            var style = Style;
            var panelGo = MenuUIKit.Rect("MenuPanel", canvas);
            _menuPanelGo = panelGo.gameObject;

            panelGo.anchorMin = panelGo.anchorMax = new Vector2(0.5f, 0.5f);
            panelGo.pivot = new Vector2(0.5f, 0.5f);
            panelGo.anchoredPosition = new Vector2(0f, -138f);

            float bodyHeight = _menuItems.Count * style.rowHeight
                             + Mathf.Max(0, _menuItems.Count - 1) * style.rowGap;
            float pad = style.panelPadding;
            panelGo.sizeDelta = new Vector2(style.menuPanelWidth, bodyHeight + pad * 2f);

            var frame = MenuUIKit.Panel("Frame", panelGo, _art, style);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var body = MenuUIKit.Rect("Body", panelGo);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(pad, pad);
            body.offsetMax = new Vector2(-pad, -pad);

            _menuList = new MenuList(body, _art, style, ReduceMotion);
            for (int i = 0; i < _menuItems.Count; i++)
                _menuList.Add(_art, _menuOptions[i]);

            _menuList.Changed += OnMenuSelectionChanged;
            _menuList.Chosen += ExecuteOption;
            _menuList.Index = 0;
        }

        private void OnMenuSelectionChanged(int index)
        {
            _selectedIndex = index;
            _sfx?.Move();
            // Two motes off the leading edge of the highlight, in the direction it travelled.
            // The cheapest possible acknowledgement that a key press did something.
            if (_fx != null && !ReduceMotion && _menuList != null)
            {
                var centre = MoteSpaceOf(_menuList.Rows[index].Root);
                _fx.Burst(centre + new Vector2(-Style.menuPanelWidth * 0.42f, 0f), 2,
                          Style.Gold, 42f, 0.45f, MenuMoteShape.Dot, spreadDegrees: 70f, direction: 0f);
            }
        }

        private void BuildFooter(Transform canvas)
        {
            var style = Style;

            var verRt = MenuUIKit.Rect("Version", canvas);
            verRt.anchorMin = verRt.anchorMax = new Vector2(1f, 0f);
            verRt.pivot = new Vector2(1f, 0f);
            verRt.anchoredPosition = new Vector2(-18f, 12f);
            verRt.sizeDelta = new Vector2(400f, 26f);
            // The engine version is gone. It told the player nothing, and a build number is the
            // only part of that line a bug report can use.
            _footerVersion = MenuTypography.Label(verRt.gameObject, style, $"v{Application.version}",
                                                  style.hintFontSize - 1f, style.TextMuted,
                                                  TextAlignmentOptions.Right);

            var hintRt = MenuUIKit.Rect("ControlsHint", canvas);
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0f, 0f);
            hintRt.pivot = new Vector2(0f, 0f);
            hintRt.anchoredPosition = new Vector2(18f, 12f);
            hintRt.sizeDelta = new Vector2(620f, 26f);
            _footerHint = MenuTypography.Label(hintRt.gameObject, style, MenuText.MainMenuHint,
                                               style.hintFontSize - 1f, style.TextMuted);
            ApplyHintVisibility();
        }

        /// <summary>The hint line is a setting, and it is ON by default for a reason: this menu
        /// is navigable by keyboard and nothing else on screen says so.</summary>
        private void ApplyHintVisibility()
        {
            bool show = GameSettings.Instance == null || GameSettings.Instance.showHints;
            if (_footerHint != null) _footerHint.enabled = show;
        }

        private void BuildCreditsPanel(Transform canvas)
        {
            var style = Style;
            _creditsPanel = new MenuPanelView(canvas, _art, style, MenuText.CreditsTitle,
                                              style.panelWidth, ReduceMotion);

            var bodyRt = MenuUIKit.Stretch("Text", _creditsPanel.Body);
            var tmp = MenuTypography.Label(bodyRt.gameObject, style, MenuText.CreditsBody,
                                           style.rowFontSize - 3f, style.TextPrimary,
                                           TextAlignmentOptions.Top);
            tmp.enableWordWrapping = true;
            tmp.lineSpacing = 6f;

            _creditsPanel.FitToContent(300f);
            _creditsPanel.SetHint(MenuText.Back + "  ·  Esc");
            _creditsPanel.Close();
        }

        /// <summary>Advances every panel's open animation. Called from the shell's one tick.</summary>
        private void TickPanels(float dt)
        {
            _creditsPanel?.Tick(dt);
            _optionsPanel?.Tick(dt);
            _audioPanel?.Tick(dt);
            _videoPanel?.Tick(dt);
            _gameplayPanel?.Tick(dt);
            _controlsPanel?.Tick(dt);
            _optionsList?.Tick(dt);
            _audioList?.Tick(dt);
            _videoList?.Tick(dt);
            _gameplayList?.Tick(dt);
            _controlsList?.Tick(dt);
        }

        /// <summary>
        /// Rebuilds the main panel — after deleting the last save, so "Continue" and "Load game"
        /// disappear. The panel is destroyed through the kit's helper because
        /// <c>Object.Destroy</c> is an outright ERROR in Edit Mode and this path is reached by
        /// the EditMode fixtures.
        /// </summary>
        private void RebuildMenuPanel()
        {
            if (_canvasTransform == null) return;
            BuildMenuOptions();
            if (_menuPanelGo != null) MenuUIKit.Destroy(_menuPanelGo);
            _menuList = null;

            BuildMenuPanel(_canvasTransform);
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _menuOptions.Length - 1));
            if (_menuList != null) _menuList.Index = _selectedIndex;
            UpdateSelection();

            if (_menuPanelGo != null)
                _menuPanelGo.SetActive(_menuScreen == MenuScreen.Main);
        }
    }
}
