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

        // Layout constants — tuned so the stack sits directly above the XP bar
        // (XP panel: anchoredPosition.y = 14, height = 28 → top edge at y = 42).
        // The cooldown panel adds an 8 px breathing gap above that edge.
        private const float SpellCooldownPanelY      = 50f;
        private const float SpellCooldownPanelWidth  = 360f;
        private const float SpellCooldownRowSpacing  = 2f;

        /// <summary>
        /// Build the bottom-center cooldown countdown stack that floats just
        /// above the XP bar. Rows are inserted by <see cref="SpellCooldownHUD"/>
        /// in response to <see cref="GameEvents.OnSpellCast"/>; the panel uses
        /// a <see cref="VerticalLayoutGroup"/> + <see cref="ContentSizeFitter"/>
        /// so it grows upward as more spells go on cooldown and shrinks back
        /// as they expire — the visual "stack" the user asked for.
        /// </summary>
        private void CreateSpellCooldownHUD(GameObject player)
        {
            if (_canvas == null) return;

            var panel = CreateUIObject("SpellCooldownPanel", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            // Pivot at the bottom so ContentSizeFitter grows the panel upward
            // — the row added most recently appears at the top of the stack.
            rect.pivot     = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, SpellCooldownPanelY);
            rect.sizeDelta = new Vector2(SpellCooldownPanelWidth, 0f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SpellCooldownRowSpacing;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childAlignment         = TextAnchor.LowerCenter;

            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            _spellCooldownPanel = panel;
            _spellCooldownHUD   = panel.AddComponent<SpellCooldownHUD>();
            _spellCooldownHUD.Initialize(player, rect);
        }
    }
}
