using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Search/filter input widget used by editor pickers and asset grids.
    /// Mirrors Python editors' header filter fields.
    /// </summary>
    public static class SearchBox
    {
        public static TMP_InputField Create(Transform parent, string placeholder,
            Action<string> onChanged, float height = 28f)
        {
            var go = EditorUIHelpers.CreateUI("Search", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = EditorUIHelpers.BG_SURFACE;
            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;

            // Text component
            var textGo = EditorUIHelpers.CreateUI("Text", go.transform);
            EditorUIHelpers.StretchFill(textGo);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 13f;
            tmp.color = EditorUIHelpers.TEXT_PRIMARY;
            tmp.margin = new Vector4(8, 3, 8, 3);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            input.textComponent = tmp;

            // Placeholder
            var phGo = EditorUIHelpers.CreateUI("Placeholder", go.transform);
            EditorUIHelpers.StretchFill(phGo);
            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.fontSize = 13f;
            ph.color = EditorUIHelpers.TEXT_MUTED;
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
