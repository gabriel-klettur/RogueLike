using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Search/filter input widget used by editor pickers, asset grids and
    /// HUD lists. Mirrors Python editors' header filter fields.
    /// </summary>
    public static class SearchBox
    {
        public static TMP_InputField Create(Transform parent, string placeholder,
            Action<string> onChanged, float height = 28f)
        {
            var go = UIFactory.CreateUI("Search", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = UITheme.BG_SURFACE;
            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;

            var textGo = UIFactory.CreateUI("Text", go.transform);
            UIFactory.StretchFill(textGo);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 13f;
            tmp.color = UITheme.TEXT_PRIMARY;
            tmp.margin = new Vector4(8, 3, 8, 3);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent = tmp;

            var phGo = UIFactory.CreateUI("Placeholder", go.transform);
            UIFactory.StretchFill(phGo);
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.fontSize = 13f;
            ph.color = UITheme.TEXT_MUTED;
            ph.fontStyle = FontStyles.Italic;
            ph.margin = new Vector4(8, 3, 8, 3);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.text = placeholder ?? "Search...";
            input.placeholder = ph;

            if (onChanged != null) input.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(onChanged));
            return input;
        }
    }
}
