using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    public partial class MapEditorUI
    {
        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
            return go;
        }

        private static GameObject CreateRow(string name, Transform parent, float height)
        {
            var row = CreatePanel(name, parent, new Color(0.09f, 0.09f, 0.1f, 1f));
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.CeilToInt(size + 8f);
            return text;
        }

        private static Button CreateActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGo = CreatePanel($"Btn_{label}", parent, new Color(0.16f, 0.18f, 0.22f, 1f));
            var button = buttonGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.22f, 0.28f, 1f);
            colors.highlightedColor = new Color(0.26f, 0.31f, 0.4f, 1f);
            colors.pressedColor = new Color(0.34f, 0.4f, 0.5f, 1f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.targetGraphic = buttonGo.GetComponent<Image>();
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", buttonGo.transform, label, 12f, Color.white, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            return button;
        }

        private static Button CreateMiniActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var button = CreateActionButton(parent, label, onClick);
            var rect = button.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(42f, 24f);
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.fontSize = 10f;
            return button;
        }

        private static TMP_InputField CreateInputField(Transform parent, string placeholder)
        {
            var root = CreatePanel("NameInput", parent, new Color(0.15f, 0.16f, 0.2f, 1f));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(6f, 6f);
            rootRect.offsetMax = new Vector2(-6f, -6f);

            var textViewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            textViewport.transform.SetParent(root.transform, false);
            var viewportRect = textViewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8f, 4f);
            viewportRect.offsetMax = new Vector2(-8f, -4f);

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(textViewport.transform, false);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 14f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(textViewport.transform, false);
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 14f;
            placeholderText.color = new Color(0.68f, 0.72f, 0.8f, 0.75f);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            var input = root.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 48;

            return input;
        }

        private static Toggle CreateToggle(Transform parent)
        {
            var root = new GameObject("Toggle", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 28f);

            var bg = CreatePanel("Background", root.transform, new Color(0.13f, 0.14f, 0.18f, 1f));
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.1f, 0.1f);
            bgRect.anchorMax = new Vector2(0.9f, 0.9f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var check = CreatePanel("Checkmark", bg.transform, new Color(0.5f, 0.9f, 0.55f, 1f));
            var checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            return toggle;
        }

        private static ScrollRect CreateScrollView(string name, Transform parent, out Transform content)
        {
            var root = CreatePanel(name, parent, new Color(0.08f, 0.08f, 0.09f, 1f));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(0f, 360f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -4f);
            viewport.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 1f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            content = contentGo.transform;
            return scroll;
        }

        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            return _whiteSprite;
        }
    }
}
