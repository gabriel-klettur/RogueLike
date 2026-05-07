using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.MapEditor.Backups
{
    public partial class MapBackupBrowserUI
    {
        // ── Tiny UI factory helpers ──────────────────────────────────────────────

        private static GameObject MakeStretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static Button AddButton(Transform parent, string label, float w, float h,
            Color normal, Color hover, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = normal;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = normal;
            c.highlightedColor = hover;
            c.pressedColor = hover;
            c.disabledColor = new Color(0.18f, 0.20f, 0.24f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var labelTmp = AddText(go.transform, label, 13f, TextPrimary,
                                   TextAlignmentOptions.Center, FontStyles.Bold);
            var lRt = labelTmp.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
