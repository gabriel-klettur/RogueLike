using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Themed horizontal slider used for volumes, seek bars and any 0..1
    /// range input across the editors and the MusicPlayer HUD. Styled with
    /// the kit's accent palette: dark track, gold fill, gold thumb.
    /// </summary>
    public static class UISlider
    {
        public static Slider Make(Transform parent,
            float min = 0f, float max = 1f, float initial = 0.5f,
            Action<float> onValueChanged = null, float height = 20f, float thumbSize = 14f)
        {
            var go = UIFactory.CreateUI("Slider", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.14f, 0.95f);

            var slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;

            // Fill area
            var fillArea = UIFactory.CreateUI("FillArea", go.transform);
            UIFactory.StretchFill(fillArea);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.offsetMin = new Vector2(2f, 4f);
            faRt.offsetMax = new Vector2(-2f, -4f);

            var fillGo = UIFactory.CreateUI("Fill", fillArea.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = UITheme.ACCENT_DIM;
            slider.fillRect = fillRt;

            // Handle
            var handleArea = UIFactory.CreateUI("HandleArea", go.transform);
            UIFactory.StretchFill(handleArea);
            var haRt = handleArea.GetComponent<RectTransform>();
            haRt.offsetMin = new Vector2(thumbSize * 0.5f, 0f);
            haRt.offsetMax = new Vector2(-thumbSize * 0.5f, 0f);

            var handleGo = UIFactory.CreateUI("Handle", handleArea.transform);
            var hRt = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0, 0.5f);
            hRt.anchorMax = new Vector2(0, 0.5f);
            hRt.pivot     = new Vector2(0.5f, 0.5f);
            hRt.sizeDelta = new Vector2(thumbSize, thumbSize);
            var hImg = handleGo.AddComponent<Image>();
            hImg.color = UITheme.ACCENT;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;

            slider.value = Mathf.Clamp(initial, min, max);
            if (onValueChanged != null)
                slider.onValueChanged.AddListener(v => onValueChanged(v));
            return slider;
        }
    }
}
