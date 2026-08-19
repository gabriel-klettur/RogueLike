using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Builds a <see cref="TMP_Dropdown"/> that actually opens.
    ///
    /// <c>AddComponent&lt;TMP_Dropdown&gt;()</c> on its own produces a control that looks
    /// fine closed and throws the moment it is clicked:
    /// <c>"The dropdown template is not assigned. The template needs to be assigned and
    /// must have a child GameObject with a Toggle component serving as the item."</c>
    /// TMP builds its option list by cloning a disabled template hierarchy, and that
    /// hierarchy only exists in the prefab Unity's own menu item creates. Anything built
    /// at runtime has to assemble it by hand — which is what this does:
    ///
    /// <code>
    /// root ─ Image, TMP_Dropdown
    ///  ├ Caption                 the selected option, shown while closed
    ///  ├ Arrow
    ///  └ Template (inactive)     cloned once per option when the list opens
    ///     └ Viewport ─ Mask
    ///        └ Content
    ///           └ Item ─ Toggle
    ///              ├ Item Background
    ///              ├ Item Checkmark
    ///              └ Item Label
    /// </code>
    /// </summary>
    public static class UIDropdown
    {
        private const float ROW_HEIGHT     = 20f;
        private const float TEMPLATE_MAX_H = 150f;
        private const float ARROW_WIDTH    = 14f;

        /// <summary>
        /// IList overload. IList and IReadOnlyList are unrelated interfaces, and callers
        /// hand over one or the other, so both doors lead to the same builder.
        /// </summary>
        public static TMP_Dropdown Add(Transform parent, IList<string> options, int selectedIndex,
                                       float fontSize = 11f)
        {
            var copy = new List<string>();
            if (options != null) copy.AddRange(options);
            return Add(parent, (IReadOnlyList<string>)copy, selectedIndex, fontSize);
        }

        /// <summary>
        /// Adds a working dropdown filling <paramref name="parent"/>.
        /// Returns the control so callers can wire <c>onValueChanged</c>.
        /// </summary>
        public static TMP_Dropdown Add(Transform parent, IReadOnlyList<string> options, int selectedIndex,
                                       float fontSize = 11f)
        {
            var rootGo = UIFactory.CreateUI("Dropdown", parent);
            UIFactory.StretchFill(rootGo);

            var background = rootGo.AddComponent<Image>();
            background.color = UITheme.BG_SURFACE;

            var dropdown = rootGo.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = background;

            dropdown.captionText = BuildCaption(rootGo.transform, fontSize);
            BuildArrow(rootGo.transform);

            BuildTemplate(rootGo.transform, fontSize, out var template, out var itemLabel);
            dropdown.template = template;
            dropdown.itemText = itemLabel;

            dropdown.ClearOptions();
            if (options != null && options.Count > 0)
            {
                var copy = new List<string>(options.Count);
                for (int i = 0; i < options.Count; i++) copy.Add(options[i]);
                dropdown.AddOptions(copy);
                if (selectedIndex >= 0 && selectedIndex < options.Count)
                    dropdown.SetValueWithoutNotify(selectedIndex);
                dropdown.RefreshShownValue();
            }

            return dropdown;
        }

        // ── Closed state ──────────────────────────────────────────────────

        private static TextMeshProUGUI BuildCaption(Transform root, float fontSize)
        {
            var go = UIFactory.CreateUI("Caption", root);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 1f);
            rt.offsetMax = new Vector2(-(ARROW_WIDTH + 4f), -1f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize            = fontSize;
            label.color               = UITheme.TEXT_PRIMARY;
            label.alignment           = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping  = false;
            label.overflowMode        = TextOverflowModes.Truncate;
            label.raycastTarget       = false;
            return label;
        }

        private static void BuildArrow(Transform root)
        {
            var go = UIFactory.CreateUI("Arrow", root);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0.5f);
            rt.anchorMax        = new Vector2(1f, 0.5f);
            rt.pivot            = new Vector2(1f, 0.5f);
            rt.sizeDelta        = new Vector2(ARROW_WIDTH, ARROW_WIDTH);
            rt.anchoredPosition = new Vector2(-4f, 0f);

            var caret = go.AddComponent<TextMeshProUGUI>();
            caret.text          = "▾";                 // ▾
            caret.fontSize      = 10f;
            caret.color         = UITheme.TEXT_SECONDARY;
            caret.alignment     = TextAlignmentOptions.Midline;
            caret.raycastTarget = false;
        }

        // ── The template TMP clones per option ────────────────────────────

        private static void BuildTemplate(Transform root, float fontSize,
                                          out RectTransform template, out TextMeshProUGUI itemLabel)
        {
            var templateGo = UIFactory.CreateUI("Template", root);
            template = templateGo.GetComponent<RectTransform>();
            // Hangs below the closed control and stretches to its width.
            template.anchorMin        = new Vector2(0f, 0f);
            template.anchorMax        = new Vector2(1f, 0f);
            template.pivot            = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, 1f);
            template.sizeDelta        = new Vector2(0f, TEMPLATE_MAX_H);

            var templateBg = templateGo.AddComponent<Image>();
            templateBg.color = UITheme.BG_PANEL;

            // TMP fades the open list through a CanvasGroup it expects to already be
            // there (TMP_Dropdown.AlphaFadeList dereferences it without a null check).
            // Unity's own dropdown prefab ships one; a runtime-built template has to
            // add it or Show() gets past the template check and then NREs.
            templateGo.AddComponent<CanvasGroup>();

            var scroll = templateGo.AddComponent<ScrollRect>();
            scroll.horizontal   = false;
            scroll.vertical     = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = ROW_HEIGHT;

            // Viewport — clips the list.
            var viewportGo = UIFactory.CreateUI("Viewport", templateGo.transform);
            var viewport = viewportGo.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.pivot     = new Vector2(0f, 1f);

            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);   // Mask needs something to draw
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            // Content — TMP resizes this as it adds items.
            var contentGo = UIFactory.CreateUI("Content", viewportGo.transform);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin        = new Vector2(0f, 1f);
            content.anchorMax        = new Vector2(1f, 1f);
            content.pivot            = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta        = new Vector2(0f, ROW_HEIGHT);

            scroll.viewport = viewport;
            scroll.content  = content;

            // Item — the row TMP clones. The Toggle is the part TMP looks for,
            // and its absence is exactly what the runtime error complains about.
            var itemGo = UIFactory.CreateUI("Item", contentGo.transform);
            var item = itemGo.GetComponent<RectTransform>();
            item.anchorMin        = new Vector2(0f, 0.5f);
            item.anchorMax        = new Vector2(1f, 0.5f);
            item.pivot            = new Vector2(0.5f, 0.5f);
            item.sizeDelta        = new Vector2(0f, ROW_HEIGHT);
            item.anchoredPosition = Vector2.zero;

            var toggle = itemGo.AddComponent<Toggle>();

            var itemBgGo = UIFactory.CreateUI("Item Background", itemGo.transform);
            UIFactory.StretchFill(itemBgGo);
            var itemBg = itemBgGo.AddComponent<Image>();
            itemBg.color = UITheme.BG_SURFACE;

            var checkmarkGo = UIFactory.CreateUI("Item Checkmark", itemGo.transform);
            var checkmark = checkmarkGo.GetComponent<RectTransform>();
            checkmark.anchorMin        = new Vector2(0f, 0.5f);
            checkmark.anchorMax        = new Vector2(0f, 0.5f);
            checkmark.pivot            = new Vector2(0f, 0.5f);
            checkmark.sizeDelta        = new Vector2(4f, ROW_HEIGHT - 4f);
            checkmark.anchoredPosition = new Vector2(2f, 0f);
            var checkImage = checkmarkGo.AddComponent<Image>();
            checkImage.color = UITheme.ACCENT;

            var itemLabelGo = UIFactory.CreateUI("Item Label", itemGo.transform);
            var itemLabelRt = itemLabelGo.GetComponent<RectTransform>();
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(10f, 0f);
            itemLabelRt.offsetMax = new Vector2(-4f, 0f);

            itemLabel = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabel.fontSize           = fontSize;
            itemLabel.color              = UITheme.TEXT_PRIMARY;
            itemLabel.alignment          = TextAlignmentOptions.MidlineLeft;
            itemLabel.enableWordWrapping = false;
            itemLabel.overflowMode       = TextOverflowModes.Truncate;
            itemLabel.raycastTarget      = false;

            toggle.targetGraphic = itemBg;
            toggle.graphic       = checkImage;
            toggle.isOn          = true;

            // TMP clones this hierarchy on open; it must stay inactive until then.
            templateGo.SetActive(false);
        }
    }
}
