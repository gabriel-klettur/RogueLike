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

        /// <summary>
        /// Build the bottom-center XP bar (Python parity: 50% screen width,
        /// 10 px tall, level + xp/next label centered above the bar).
        /// </summary>
        private void CreateXpBarHUD(Experience playerXp)
        {
            if (_canvas == null) return;

            const float panelHeight = 28f;
            const float barHeight   = 10f;

            var panel = CreateUIObject("XpBarPanel", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot     = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 14f);
            // Width is 50% of canvas reference width (Python: screen_w * 0.5).
            rect.sizeDelta = new Vector2(800f, panelHeight);

            // Label (Lvl N    xp/next) above the bar.
            var labelGo = CreateUIObject("Label", panel.transform);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot     = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, 0f);
            labelRect.sizeDelta = new Vector2(0f, panelHeight - barHeight - 2f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "Lvl 1   0/0";
            label.fontSize = 14f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.92f, 0.92f, 0.92f, 1f);

            // Bar background.
            var barBg = CreateUIObject("BarBG", panel.transform);
            var barBgRect = barBg.GetComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0f, 0f);
            barBgRect.anchorMax = new Vector2(1f, 0f);
            barBgRect.pivot = new Vector2(0.5f, 0f);
            barBgRect.anchoredPosition = new Vector2(0f, 0f);
            barBgRect.sizeDelta = new Vector2(0f, barHeight);
            var bg = barBg.AddComponent<Image>();
            bg.sprite = GetWhitePixelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.16f, 0.16f, 0.16f, 0.85f);

            // Bar fill.
            var fillGo = CreateUIObject("BarFill", barBg.transform);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = GetWhitePixelSprite();
            fill.type   = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color  = new Color(0.31f, 0.55f, 1f, 1f);
            fill.fillAmount = 0f;

            _xpBarPanel = panel;
            _xpBarHUD = panel.AddComponent<XpBarHUD>();
            _xpBarHUD.SetUIReferences(fill, bg, label);
            _xpBarHUD.Bind(playerXp);
        }
    }
}
