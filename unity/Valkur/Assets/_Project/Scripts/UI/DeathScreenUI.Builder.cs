using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.UI
{
    public partial class DeathScreenUI
    {
        private partial void BuildUI()
        {
            // Ensure EventSystem exists (required for mouse clicks on UI)
            InputDiagnostics.EnsureEventSystem();

            // Canvas (overlay, high sort order)
            var canvasGo = new GameObject("DeathScreenCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            // Full-screen dark overlay
            var overlayGo = CreateUI("Overlay", canvasGo.transform);
            var overlayRect = overlayGo.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            var overlayImg = overlayGo.AddComponent<Image>();
            overlayImg.color = overlayColor;

            // "HAS MUERTO" title
            var titleGo = CreateUI("Title", canvasGo.transform);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 100f);
            titleRect.sizeDelta = new Vector2(800f, 100f);

            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "HAS MUERTO";
            titleText.fontSize = 72f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = titleColor;
            titleText.fontStyle = FontStyles.Bold;

            // Subtitle
            var subGo = CreateUI("Subtitle", canvasGo.transform);
            var subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 0.5f);
            subRect.anchorMax = new Vector2(0.5f, 0.5f);
            subRect.pivot = new Vector2(0.5f, 0.5f);
            subRect.anchoredPosition = new Vector2(0f, 40f);
            subRect.sizeDelta = new Vector2(600f, 40f);

            var subText = subGo.AddComponent<TextMeshProUGUI>();
            subText.text = "Tu aventura ha terminado... por ahora.";
            subText.fontSize = 22f;
            subText.alignment = TextAlignmentOptions.Center;
            subText.color = new Color(0.7f, 0.6f, 0.6f, 0.9f);
            subText.fontStyle = FontStyles.Italic;

            // Hint text
            var hintGo = CreateUI("Hint", canvasGo.transform);
            var hintRect = hintGo.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.pivot = new Vector2(0.5f, 0.5f);
            hintRect.anchoredPosition = new Vector2(0f, -140f);
            hintRect.sizeDelta = new Vector2(500f, 30f);

            var hintText = hintGo.AddComponent<TextMeshProUGUI>();
            hintText.text = "W/S o \u2191\u2193 Navegar  |  Enter Seleccionar";
            hintText.fontSize = 14f;
            hintText.alignment = TextAlignmentOptions.Center;
            hintText.color = new Color(0.5f, 0.45f, 0.45f, 0.7f);

            // Button container
            var containerGo = CreateUI("Buttons", canvasGo.transform);
            var containerRect = containerGo.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, -60f);
            containerRect.sizeDelta = new Vector2(340f, 140f);

            var layout = containerGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            // Create buttons
            _buttonCount = 2;
            _buttonImages = new Image[_buttonCount];
            _buttonTexts = new TextMeshProUGUI[_buttonCount];
            _buttonActions = new System.Action[_buttonCount];

            CreateButton(0, "Reiniciar", containerGo.transform, OnRestartClicked);
            CreateButton(1, "Menu Principal", containerGo.transform, OnMainMenuClicked);

            UILayerHelper.SetUILayerRecursive(canvasGo);
        }

        private void CreateButton(int index, string label, Transform parent, System.Action onClick)
        {
            var btnGo = CreateUI($"Btn_{label}", parent);
            var btnLayout = btnGo.AddComponent<LayoutElement>();
            btnLayout.preferredHeight = 50f;

            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = buttonNormalColor;
            btnImg.raycastTarget = true;

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = buttonNormalColor;
            colors.highlightedColor = buttonHoverColor;
            colors.pressedColor = buttonSelectedColor;
            colors.selectedColor = buttonSelectedColor;
            btn.colors = colors;
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(() => onClick());

            var textGo = CreateUI("Text", btnGo.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var btnText = textGo.AddComponent<TextMeshProUGUI>();
            btnText.text = label;
            btnText.fontSize = 26f;
            btnText.alignment = TextAlignmentOptions.Center;
            btnText.color = buttonTextColor;
            btnText.raycastTarget = false;

            _buttonImages[index] = btnImg;
            _buttonTexts[index] = btnText;
            _buttonActions[index] = onClick;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
