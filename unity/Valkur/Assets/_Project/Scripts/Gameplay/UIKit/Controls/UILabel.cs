using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// TextMeshPro label builders shared by every editor and HUD. Wraps the
    /// most common patterns: stretched centered text, single-line label with
    /// LayoutElement, accent section header, and the standard editor title
    /// bar (image background + child TMP to avoid the Image+TMP raycast clash).
    /// </summary>
    public static class UILabel
    {
        /// <summary>Stretched (anchor 0,0 → 1,1) centered text. Used as a button label.</summary>
        public static TextMeshProUGUI AddCenteredText(Transform parent, string text,
            float size, FontStyles style, Color color)
        {
            var go = UIFactory.CreateUI("Txt", parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.sizeDelta = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = color;
            return tmp;
        }

        /// <summary>Single-line label with a LayoutElement (height = fontSize + 6f).</summary>
        public static TextMeshProUGUI Add(Transform parent, string text,
            float fontSize = 12f, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = UIFactory.CreateUI("Label", parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 6f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Normal;
            tmp.alignment = align; tmp.color = UITheme.TEXT_SECONDARY;
            return tmp;
        }

        /// <summary>Accent-colored bold section header with letter spacing.</summary>
        public static void BuildSectionHeader(Transform parent, string text, float fontSize = 14f)
        {
            var go = UIFactory.CreateUI("Header_" + text, parent);
            go.AddComponent<LayoutElement>().preferredHeight = fontSize + 8f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = UITheme.ACCENT;
            tmp.characterSpacing = 4f;
        }

        /// <summary>
        /// Standard top-of-panel title bar: dark Image background +
        /// child TMP label. Avoids the Image + TMP-on-the-same-GameObject
        /// raycast collision.
        /// </summary>
        public static TextMeshProUGUI MakeTitleBar(Transform parent, string title, float height = 36f)
        {
            var go = UIFactory.CreateUI("TitleBar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = UITheme.BG_HEADER;

            var labelGo = UIFactory.CreateUI("Label", go.transform);
            UIFactory.StretchFill(labelGo);
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = title;
            tmp.fontSize = 16f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UITheme.ACCENT;
            tmp.characterSpacing = 6f;
            return tmp;
        }

        /// <summary>Italic muted status text used at the bottom of editor panels.</summary>
        public static TextMeshProUGUI MakeStatus(Transform parent)
        {
            var go = UIFactory.CreateUI("Status", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 11f;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UITheme.TEXT_MUTED;
            return tmp;
        }
    }
}
