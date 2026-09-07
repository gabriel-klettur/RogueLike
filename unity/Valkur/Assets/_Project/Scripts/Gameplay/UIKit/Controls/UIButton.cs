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
            if (onClick != null) btn.onClick.AddListener(onClick);
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

        /// <summary>
        /// Repaint a button built by this factory — the ONLY correct way to show selection
        /// state on one.
        ///
        /// <para>WHY <c>btn.targetGraphic.color = x</c> DOES NOT WORK, measured rather than
        /// assumed. Every button here ships with <c>Selectable</c> on its default ColorTint
        /// transition, which drives the target graphic's CanvasRenderer to
        /// <c>colors.normalColor</c> — and the CanvasRenderer colour MULTIPLIES with
        /// <c>Graphic.color</c> rather than replacing it. So a caller who writes the graphic
        /// gets <c>written × normalColor</c>, which is darker than either and is not the
        /// colour they asked for.</para>
        ///
        /// <para>The arithmetic, off a captured frame of the Skills editor: an "active" tab
        /// written as ACCENT-over-BTN_NORMAL rendered <c>(32,31,29)</c> against a panel
        /// background of <c>(31,33,42)</c> — invisible — while untouched buttons rendered
        /// <c>(6,6,11)</c>, i.e. <c>BTN_NORMAL squared</c>, near-black. The selection read
        /// BACKWARDS: the chosen row looked like a gap and the unchosen ones looked solid.
        /// Nothing failed, because uGUI performs no layout in EditMode and a structural probe
        /// reads back the value the caller wrote, not the one on screen.</para>
        ///
        /// <para>Setting the ColorBlock instead makes the tint exact: the graphic is held at
        /// white so the product is the requested colour, and hover/press stay proportional to
        /// it rather than to the old palette.</para>
        /// </summary>
        public static void SetTint(Button button, Color tint)
        {
            if (button == null) return;

            if (button.targetGraphic != null) button.targetGraphic.color = Color.white;

            var c = button.colors;
            c.normalColor = tint;
            c.selectedColor = tint;
            // Hover and press are derived so a caller only ever names one colour; a fixed pair
            // would fight whatever tint was just applied.
            c.highlightedColor = Color.Lerp(tint, Color.white, 0.18f);
            c.pressedColor = Color.Lerp(tint, Color.black, 0.22f);
            c.disabledColor = new Color(tint.r, tint.g, tint.b, tint.a * 0.4f);
            button.colors = c;

            // ASSIGNING colors DOES NOT REPAINT. Selectable pushes its ColorBlock to the
            // graphic's CanvasRenderer only when it evaluates a state transition — on enable,
            // or on a pointer/selection event — so a button whose block is changed while it
            // sits idle keeps whatever the CanvasRenderer last held. With the graphic pinned to
            // white above, that is WHITE, and the whole strip renders as pale cream.
            //
            // Measured on the first pass of this method: every button in the Skills editor came
            // back at ~(202,202,205) against a (33,32,39) panel — the exact inverse of the bug
            // it was written to fix, and just as unreadable. Toggling `enabled` re-enters
            // OnEnable, which transitions to the current state INSTANTLY, so the tint lands in
            // the same frame. `interactable` is a separate flag and is not disturbed.
            if (button.gameObject.activeInHierarchy)
            {
                button.enabled = false;
                button.enabled = true;
            }
        }
    }
}
