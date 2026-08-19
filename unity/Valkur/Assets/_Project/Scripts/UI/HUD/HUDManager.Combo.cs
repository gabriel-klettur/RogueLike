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
        private void CreateComboHUD(GameObject playerGo)
        {
            var panel = CreateUIObject("ComboHUDPanel", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot     = Vector2.zero;
            rect.sizeDelta = new Vector2(_playerPanelWidth, ComboHUD.PreferredHeight);
            rect.anchoredPosition = new Vector2(
                HudPanelMargin,
                HudPanelMargin + _playerPanelHeight + ComboPanelGap);

            _comboPanel = panel;
            _comboHUD   = panel.AddComponent<ComboHUD>();
            // Null is fine — the badge re-resolves the player's counter itself,
            // which also covers respawns replacing the component.
            _comboHUD.Bind(playerGo != null ? playerGo.GetComponent<ComboCounter>() : null);
        }
    }
}
