using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        // Gap between the top of the unified player panel and the combo badge.
        private const float ComboPanelGap = 8f;

        private ComboHUD   _comboHUD;
        private GameObject _comboPanel;

        public ComboHUD Combo => _comboHUD;

        /// <summary>
        /// Builds the combo badge as the next widget up the bottom-left column:
        /// same left margin and same width as the player panel, stacked directly
        /// above it. Sizing happens before the component is added so
        /// <c>ComboHUD.Awake</c> builds its hierarchy against the final rect.
        /// </summary>
        /// <summary>
        /// Stacks the badge on the player panel's LIVE footprint. The panel sizes itself in whole
        /// screen pixels per texel, so its height in canvas units moves with the resolution; a
        /// placement computed once at build would drift onto the panel after a resize.
        /// </summary>
        private void ApplyComboPlacement()
        {
            if (_comboPanel == null) return;
            var rect = _comboPanel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(_playerPanelWidth, ComboHUD.PreferredHeight);
            rect.anchoredPosition = new Vector2(
                _playerPanelOrigin.x,
                _playerPanelOrigin.y + _playerPanelHeight + ComboPanelGap);
        }

        private void CreateComboHUD(GameObject playerGo)
        {
            var panel = CreateUIObject("ComboHUDPanel", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot     = Vector2.zero;
            _comboPanel = panel;
            ApplyComboPlacement();

            _comboHUD   = panel.AddComponent<ComboHUD>();
            // Null is fine — the badge re-resolves the player's counter itself,
            // which also covers respawns replacing the component.
            _comboHUD.Bind(playerGo != null ? playerGo.GetComponent<ComboCounter>() : null);
        }
    }
}
