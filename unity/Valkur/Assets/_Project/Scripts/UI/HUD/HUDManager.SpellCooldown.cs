using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private SpellCooldownHUD _spellCooldownHUD;
        private GameObject _spellCooldownPanel;

        public SpellCooldownHUD SpellCooldown => _spellCooldownHUD;

        // Layout constants — anchored top-left, just below the DayNightClockHUD
        // (which is anchored top-left with margin 24, size 110 × ~154 → bottom
        // edge at y ≈ -178). 8 px breathing gap below that edge.
        private const float SpellCooldownPanelX      = 24f;
        private const float SpellCooldownPanelY      = -186f;
        private const float SpellCooldownPanelWidth  = 240f;
        private const float SpellCooldownRowSpacing  = 4f;

        /// <summary>
        /// Build the top-left cooldown countdown stack that sits just below the
        /// DayNightClockHUD. Rows are inserted by <see cref="SpellCooldownHUD"/>
        /// in response to <see cref="GameEvents.OnSpellCast"/>; the panel uses
        /// a <see cref="VerticalLayoutGroup"/> + <see cref="ContentSizeFitter"/>
        /// so it grows downward as more spells go on cooldown and shrinks back
        /// as they expire — the visual "stack" the user asked for.
        /// </summary>
        private void CreateSpellCooldownHUD(GameObject player)
        {
            if (_canvas == null) return;

            var panel = CreateUIObject("SpellCooldownPanel", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            // Pivot top-left so ContentSizeFitter grows the panel downward
            // — the row added most recently appears at the bottom of the stack.
            rect.pivot     = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(SpellCooldownPanelX, SpellCooldownPanelY);
            rect.sizeDelta = new Vector2(SpellCooldownPanelWidth, 0f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SpellCooldownRowSpacing;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childAlignment         = TextAnchor.UpperLeft;

            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            _spellCooldownPanel = panel;
            _spellCooldownHUD   = panel.AddComponent<SpellCooldownHUD>();
            _spellCooldownHUD.Initialize(player, rect);
        }
    }
}
