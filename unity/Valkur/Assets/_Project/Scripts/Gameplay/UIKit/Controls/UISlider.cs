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
            Action<float> onValueChanged = null, float height = 20f, float thumbSize = 14f,
            Color? trackColor = null, Color? fillColor = null, Color? handleColor = null)
        {
            var go = UIFactory.CreateUI("Slider", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var bg = go.AddComponent<Image>();
            bg.color = trackColor ?? new Color(0.10f, 0.12f, 0.14f, 0.95f);

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
            fillImg.color = fillColor ?? UITheme.ACCENT_DIM;
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
            hImg.color = handleColor ?? UITheme.ACCENT;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;

            slider.value = Mathf.Clamp(initial, min, max);
            if (onValueChanged != null)
                slider.onValueChanged.AddListener(v => onValueChanged(v));
            return slider;
        }

        /// <summary>
        /// Variant where the slider's visible track is slim but the click /
        /// drag area spans the whole <paramref name="hitHeight"/>. The host
        /// GameObject holds a transparent raycast-only Image plus the
        /// <see cref="Slider"/> component, so a click anywhere in the
        /// hit-area band registers as a slider press at that x-position. The
        /// visible track + fill live as children of the host, sized to
        /// <paramref name="trackHeight"/>; the handle sits in its own child
        /// and can be larger than the track without distorting the hit
        /// detection. Use this when the slider must stay visually slim (e.g.
        /// inside a Sound Options row) without forcing pixel-perfect aim.
        /// </summary>
        public static Slider MakeSlimTrack(Transform parent, string name,
            float min, float max, float initial,
            Action<float> onValueChanged,
            float hitHeight, float trackHeight, float thumbSize,
            Color trackColor, Color fillColor, Color handleColor)
        {
            var go = UIFactory.CreateUI(name, parent);

            // Transparent Image so EventSystem raycasts hit the entire
            // hitHeight band, not just the slim visible track.
            var hostImg = go.AddComponent<Image>();
            hostImg.color = new Color(0f, 0f, 0f, 0f);

            var slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;

            // Visible track (centered, slim)
            var trackGo = UIFactory.CreateUI("Track", go.transform);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot     = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, trackHeight);
            trackRt.anchoredPosition = Vector2.zero;
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.color = trackColor;
            trackImg.raycastTarget = false;

            // Fill area lives inside the track so the cyan stripe never
            // bleeds outside the visible track edges.
            var fillArea = UIFactory.CreateUI("FillArea", trackGo.transform);
            var faRt = fillArea.GetComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
            faRt.offsetMin = Vector2.zero; faRt.offsetMax = Vector2.zero;

            var fillGo = UIFactory.CreateUI("Fill", fillArea.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = fillColor;
            fillImg.raycastTarget = false;
            slider.fillRect = fillRt;

            // Handle area sits on the host (not the track) so the handle
            // can be visually taller than the track without altering the
            // slider's drag bounds.
            var handleArea = UIFactory.CreateUI("HandleArea", go.transform);
            var haRt = handleArea.GetComponent<RectTransform>();
            haRt.anchorMin = new Vector2(0f, 0.5f);
            haRt.anchorMax = new Vector2(1f, 0.5f);
            haRt.pivot     = new Vector2(0.5f, 0.5f);
            haRt.sizeDelta = new Vector2(-thumbSize, thumbSize);
            haRt.anchoredPosition = Vector2.zero;

            var handleGo = UIFactory.CreateUI("Handle", handleArea.transform);
            var hRt = handleGo.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 0.5f);
            hRt.anchorMax = new Vector2(0f, 0.5f);
            hRt.pivot     = new Vector2(0.5f, 0.5f);
            hRt.sizeDelta = new Vector2(thumbSize, thumbSize);
            var hImg = handleGo.AddComponent<Image>();
            hImg.color = handleColor;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;

            slider.value = Mathf.Clamp(initial, min, max);
            if (onValueChanged != null)
                slider.onValueChanged.AddListener(v => onValueChanged(v));
            return slider;
        }
    }
}
