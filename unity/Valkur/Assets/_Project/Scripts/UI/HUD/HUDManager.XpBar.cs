using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private XpBarHUD _xpBarHUD;
        private GameObject _xpBarPanel;

        public XpBarHUD XpBar => _xpBarHUD;

        // Yellow XP fill — matches the user's reference HUD (per request).
        private static readonly Color XpFillColor = new Color(1.0f, 0.82f, 0.20f, 1f);

        /// <summary>
        /// Build the XP bar inline inside the unified player HUD panel
        /// (parent supplied by <c>CreatePlayerHUD</c>). The bar grows with
        /// the parent's HorizontalLayoutGroup width and uses a yellow fill;
        /// a tiny "Lvl N  xp/next" label is overlaid on top.
        /// </summary>
        private void CreateXpBarHUD(Experience playerXp, Transform parent)
        {
            const float panelHeight = 18f;

            var panel = CreateUIObject("XpBar", parent);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot     = new Vector2(0.5f, 0f);
            var le = panel.AddComponent<LayoutElement>();
            le.preferredHeight = panelHeight;
            le.flexibleWidth   = 1f;

            // Bar background (fills the panel).
            var bg = panel.AddComponent<Image>();
            bg.sprite = GetWhitePixelSprite();
            bg.type   = Image.Type.Sliced;
            bg.color  = new Color(0.12f, 0.12f, 0.14f, 0.85f);
            bg.raycastTarget = false;

            // Bar fill (anchored stretch). The XP bar carries no overlay text
            // by design — the player's level number lives in its own HUD widget
            // in the bottom-right corner of the screen.
            var fillGo = CreateUIObject("BarFill", panel.transform);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = GetWhitePixelSprite();
            fill.type   = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color  = XpFillColor;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            _xpBarPanel = panel;
            _xpBarHUD = panel.AddComponent<XpBarHUD>();
            _xpBarHUD.SetUIReferences(fill, bg, null);
            _xpBarHUD.Bind(playerXp);
        }
    }
}
