using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Themed button factory. All editor toolbars, modals and HUD widgets go
    /// through here so colors stay consistent and changes to the press/hover
    /// states are made in one place.
    /// </summary>
    public static class UIButton
    {
        public static Button Make(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f, float fontSize = 13f)
        {
            var go = UIFactory.CreateUI("Btn_" + label, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = UITheme.BTN_NORMAL;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = UITheme.BTN_NORMAL;
            c.highlightedColor = UITheme.BTN_HOVER;
            c.pressedColor     = UITheme.BTN_ACTIVE;
            btn.colors = c;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            UILabel.AddCenteredText(go.transform, label, fontSize, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            return btn;
        }

        public static Button MakeDanger(Transform parent, string label,
            UnityEngine.Events.UnityAction onClick, float height = 30f)
        {
            var btn = Make(parent, label, onClick, height);
            var c = btn.colors;
            c.normalColor      = new Color(0.55f, 0.15f, 0.15f, 1f);
            c.highlightedColor = new Color(0.70f, 0.20f, 0.20f, 1f);
            c.pressedColor     = UITheme.DANGER;
            btn.colors = c;
            return btn;
        }

        /// <summary>
        /// Toggle button — flips its visual state on each click and reports
        /// the new state via <paramref name="onChanged"/>. Used by editor
        /// toolbars (Help, Snap, Grid) and HUD chrome (Mute, Loop).
        /// </summary>
        public static Button MakeToggle(Transform parent, string label, bool initial,
            Action<bool> onChanged, float height = 30f, float fontSize = 13f)
        {
            bool state = initial;
            Button btn = null;
            btn = Make(parent, label, () =>
            {
                state = !state;
                onChanged?.Invoke(state);
                var bg = btn.GetComponent<Image>();
                if (bg != null) bg.color = state ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
            }, height, fontSize);
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = state ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
            return btn;
        }

        /// <summary>
        /// Square slot button (icon + bottom label). Used by inventory grids,
        /// asset pickers and entity catalogs in the editors.
        /// </summary>
        public static (Button button, Image icon, TextMeshProUGUI label) MakeSlot(
            Transform parent, string text, float size = 64f,
            UnityEngine.Events.UnityAction onClick = null)
        {
            var go = UIFactory.CreateUI("Slot", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size;
            var bg = go.AddComponent<Image>();
            bg.color = UITheme.SLOT_BG;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = UITheme.SLOT_BG;
            c.highlightedColor = UITheme.SLOT_HOVER;
            c.pressedColor     = UITheme.SLOT_SELECTED;
            btn.colors = c;
            btn.targetGraphic = bg;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var iconGo = UIFactory.CreateUI("Icon", go.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.1f, 0.2f);
            iconRt.anchorMax = new Vector2(0.9f, 0.9f);
            iconRt.sizeDelta = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.enabled = false;

            var labelTmp = UILabel.Add(go.transform, text, 9f, TextAlignmentOptions.Bottom);
            var labelRt = labelTmp.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 0.25f);
            labelRt.sizeDelta = Vector2.zero;
            labelTmp.alignment = TextAlignmentOptions.Center;

            return (btn, icon, labelTmp);
        }
    }
}
