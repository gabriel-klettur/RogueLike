using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI
{
    public partial class DeathBannerUI
    {
        private partial void BuildUI()
        {
            // Canvas (overlay, high sort order so the banner sits above HUD)
            var canvasGo = new GameObject("DeathBannerCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 480;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            BuildFlash(canvasGo.transform);
            BuildBanner(canvasGo.transform);

            UILayerHelper.SetUILayerRecursive(canvasGo);
        }

        private void BuildFlash(Transform parent)
        {
            var flashGo = new GameObject("DeathFlash", typeof(RectTransform));
            flashGo.transform.SetParent(parent, false);
            var rt = (RectTransform)flashGo.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(900f, 200f);

            _flashGroup = flashGo.AddComponent<CanvasGroup>();
            _flashGroup.alpha = 0f;
            _flashGroup.interactable = false;
            _flashGroup.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(flashGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            _flashText = textGo.AddComponent<TextMeshProUGUI>();
            _flashText.text = "HAS MUERTO";
            _flashText.fontSize = 96f;
            _flashText.alignment = TextAlignmentOptions.Center;
            _flashText.color = flashColor;
            _flashText.fontStyle = FontStyles.Bold;
            _flashText.raycastTarget = false;
        }

        private void BuildBanner(Transform parent)
        {
            // Strip pinned to the top of the screen
            var stripGo = new GameObject("Banner", typeof(RectTransform));
            stripGo.transform.SetParent(parent, false);
            var rt = (RectTransform)stripGo.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -8f);
            rt.sizeDelta = new Vector2(0f, 56f);

            _bannerGroup = stripGo.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.interactable = false;
            _bannerGroup.blocksRaycasts = false;

            var bg = stripGo.AddComponent<Image>();
            bg.color = bannerStripBg;
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(stripGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            _bannerText = textGo.AddComponent<TextMeshProUGUI>();
            _bannerText.text = "Encuentra el altar para revivir";
            _bannerText.fontSize = 24f;
            _bannerText.alignment = TextAlignmentOptions.Center;
            _bannerText.color = bannerColor;
            _bannerText.fontStyle = FontStyles.Italic | FontStyles.Bold;
            _bannerText.raycastTarget = false;
        }
    }
}
