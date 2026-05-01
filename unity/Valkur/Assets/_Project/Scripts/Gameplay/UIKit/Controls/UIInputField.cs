using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// TMP input fields styled to the kit. Two flavors: <see cref="AddCommit"/>
    /// for editor property rows (commits on Enter / focus loss) and
    /// <see cref="MakeWithPlaceholder"/> for forms with a visible placeholder.
    /// </summary>
    public static class UIInputField
    {
        /// <summary>
        /// Lightweight TMP_InputField used by editor property/entity rows.
        /// Commits via <paramref name="onCommit"/> when the user presses Enter
        /// or the field loses focus (TMP onEndEdit semantics).
        /// </summary>
        public static TMP_InputField AddCommit(Transform parent, string initial,
            Action<string> onCommit, float height = 24f, float fontSize = 11f)
        {
            var go = UIFactory.CreateUI("Input", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.14f, 0.95f);

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;

            var textArea = UIFactory.CreateUI("TextArea", go.transform);
            UIFactory.StretchFill(textArea);
            var taRT = textArea.GetComponent<RectTransform>();
            taRT.offsetMin = new Vector2(4, 2); taRT.offsetMax = new Vector2(-4, -2);
            textArea.AddComponent<RectMask2D>();

            var textGo = UIFactory.CreateUI("Text", textArea.transform);
            UIFactory.StretchFill(textGo);
            var textTMP = textGo.AddComponent<TextMeshProUGUI>();
            textTMP.fontSize = fontSize;
            textTMP.color = UITheme.TEXT_PRIMARY;
            textTMP.alignment = TextAlignmentOptions.MidlineLeft;
            textTMP.enableWordWrapping = false;
            textTMP.overflowMode = TextOverflowModes.Truncate;

            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = textTMP;
            input.text = initial ?? string.Empty;

            if (onCommit != null)
                input.onEndEdit.AddListener(v => onCommit.Invoke(v ?? string.Empty));

            return input;
        }

        /// <summary>Input field with an italic placeholder and a surface-colored bg.</summary>
        public static TMP_InputField MakeWithPlaceholder(Transform parent, string placeholder = "...",
            float height = 30f)
        {
            var go = UIFactory.CreateUI("InputField", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = UITheme.BG_SURFACE;

            var textArea = UIFactory.CreateUI("TextArea", go.transform);
            UIFactory.StretchFill(textArea);

            var phGo = UIFactory.CreateUI("Placeholder", textArea.transform);
            UIFactory.StretchFill(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder; phTmp.fontSize = 12f;
            phTmp.fontStyle = FontStyles.Italic; phTmp.color = UITheme.TEXT_MUTED;

            var txtGo = UIFactory.CreateUI("Text", textArea.transform);
            UIFactory.StretchFill(txtGo);
            var txtTmp = txtGo.AddComponent<TextMeshProUGUI>();
            txtTmp.fontSize = 12f; txtTmp.color = UITheme.TEXT_PRIMARY;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.fontAsset = txtTmp.font;

            return input;
        }
    }
}
