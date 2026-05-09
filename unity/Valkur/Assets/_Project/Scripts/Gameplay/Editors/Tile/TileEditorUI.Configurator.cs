using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Wires the "CONFIGURE TILESET" button (in the Tiles panel) and the
    /// <see cref="TilesetConfiguratorPanel"/> wizard. The wizard is built
    /// lazily as a sibling of the menu UI on the same canvas, so it overlays
    /// the editor when opened.
    /// </summary>
    public partial class TileEditorUI
    {
        private TilesetConfiguratorPanel _configuratorPanel;

        /// <summary>
        /// Hooked from <see cref="BuildUI"/> after the UI builder returns. Wires
        /// the Configure button click and primes the enabled-state tracking.
        /// </summary>
        private void WireConfiguratorButton()
        {
            if (_refs.ConfigureTilesetBtn == null) return;
            _refs.ConfigureTilesetBtn.onClick.RemoveAllListeners();
            _refs.ConfigureTilesetBtn.onClick.AddListener(OpenConfiguratorForCurrentCategory);
            RefreshConfiguratorButtonState();
        }

        /// <summary>
        /// Updates whether the Configure button is interactable, based on whether
        /// the currently-selected category has a <c>ruleset.asset</c> on disk.
        /// Called after every category selection.
        /// </summary>
        private void RefreshConfiguratorButtonState()
        {
            if (_refs.ConfigureTilesetBtn == null) return;
            bool hasCategory = !string.IsNullOrEmpty(_currentCategory);
            bool hasRuleset = hasCategory && LoadRulesetForCategory(_currentCategory) != null;
            _refs.ConfigureTilesetBtn.interactable = hasRuleset;

            if (_refs.ConfigureTilesetBtnLabel != null)
            {
                _refs.ConfigureTilesetBtnLabel.color = hasRuleset ? ACCENT : TEXT_MUTED;
                _refs.ConfigureTilesetBtnLabel.text = hasCategory
                    ? (hasRuleset ? $"CONFIGURE: {_currentCategory.ToUpperInvariant()}" : "NO RULESET FOR CATEGORY")
                    : "PICK A CATEGORY FIRST";
            }
        }

        private void OpenConfiguratorForCurrentCategory()
        {
            if (string.IsNullOrEmpty(_currentCategory)) return;
            var ruleset = LoadRulesetForCategory(_currentCategory);
            if (ruleset == null) return;
            EnsureConfiguratorPanel();
            _configuratorPanel.Open(ruleset, _currentCategory);
        }

        private void EnsureConfiguratorPanel()
        {
            if (_configuratorPanel != null) return;
            // Place the panel as a sibling of the menu UI on the same canvas so
            // it inherits the same scaler / sortingOrder.
            var canvas = GetComponentInChildren<Canvas>(includeInactive: true);
            var parent = canvas != null ? canvas.transform : transform;
            var go = new GameObject("TilesetConfiguratorPanel", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _configuratorPanel = go.AddComponent<TilesetConfiguratorPanel>();
        }

        private static TilesetRuleset LoadRulesetForCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return null;
            return Resources.Load<TilesetRuleset>($"Tiles/{category}/ruleset");
        }
    }
}
