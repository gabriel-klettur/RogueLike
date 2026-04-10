using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Orchestrates the entire HUD system. Creates the screen-space Canvas,
    /// PlayerHUD, and TargetHUD at runtime. Wires to player Health events.
    /// Attach to a persistent GameObject in the gameplay scene.
    /// </summary>
    public class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private PlayerHUD _playerHUD;
        private TargetHUD _targetHUD;
        private Canvas _canvas;
        private Mana _playerMana;

        public TargetHUD TargetHUD => _targetHUD;

        /// <summary>
        /// Called by GameplaySceneSetup after the player is spawned.
        /// </summary>
        public void InitializeForPlayer(Health playerHealth, Mana playerMana = null)
        {
            if (_canvas == null)
                CreateCanvas();

            CreatePlayerHUD(playerHealth);
            CreateTargetHUD();

            UILayerHelper.SetUILayerRecursive(_canvas.gameObject);

            // Wire mana to PlayerHUD
            if (playerMana != null)
            {
                _playerMana = playerMana;
                _playerMana.OnManaChanged += OnPlayerManaChanged;
                OnPlayerManaChanged(_playerMana.CurrentMana, _playerMana.MaxMana);
            }

            Debug.Log("[HUDManager] HUD initialized for player.");
        }

        private void OnPlayerManaChanged(int current, int max)
        {
            if (_playerHUD != null)
                _playerHUD.SetMana(current, max);
        }

        private void CreateCanvas()
        {
            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
        }

        private void CreatePlayerHUD(Health playerHealth)
        {
            // --- Container panel (bottom-left) ---
            var panel = CreateUIObject("PlayerHUDPanel", _canvas.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(20f, 20f);
            panelRect.sizeDelta = new Vector2(260f, 80f);

            // Semi-transparent background
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.55f);

            // Vertical layout
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // HP Row
            var hpRow = CreateBarRow(panel.transform, "HP", out var hpFill, out var hpBg, out var hpText,
                new Color(0.2f, 0.85f, 0.2f, 1f));

            // MP Row
            var mpRow = CreateBarRow(panel.transform, "MP", out var mpFill, out var mpBg, out var mpText,
                new Color(0.31f, 0.47f, 1f, 1f));

            // Attach PlayerHUD component
            _playerHUD = panel.AddComponent<PlayerHUD>();

            _playerHUD.SetUIReferences(hpFill, hpBg, hpText, mpFill, mpBg, mpText);
            _playerHUD.Initialize(playerHealth);
        }

        private void CreateTargetHUD()
        {
            // --- Container panel (top-center) ---
            var panel = CreateUIObject("TargetHUDPanel", _canvas.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -15f);
            panelRect.sizeDelta = new Vector2(320f, 90f);

            // Semi-transparent background
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.6f);

            var canvasGroup = panel.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            // Vertical layout
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            // Name text
            var nameGo = CreateUIObject("NameText", panel.transform);
            var nameText = nameGo.AddComponent<TextMeshProUGUI>();
            nameText.text = "";
            nameText.fontSize = 22f;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = Color.white;
            var nameLayout = nameGo.AddComponent<LayoutElement>();
            nameLayout.preferredHeight = 28f;

            // State text
            var stateGo = CreateUIObject("StateText", panel.transform);
            var stateText = stateGo.AddComponent<TextMeshProUGUI>();
            stateText.text = "";
            stateText.fontSize = 16f;
            stateText.alignment = TextAlignmentOptions.Center;
            stateText.color = new Color(1f, 0.78f, 0.47f, 1f);
            var stateLayout = stateGo.AddComponent<LayoutElement>();
            stateLayout.preferredHeight = 20f;

            // HP bar
            var barContainer = CreateUIObject("HPBarContainer", panel.transform);
            var barContainerLayout = barContainer.AddComponent<LayoutElement>();
            barContainerLayout.preferredHeight = 14f;

            var barBg = CreateUIObject("BarBG", barContainer.transform);
            var barBgRect = barBg.GetComponent<RectTransform>();
            barBgRect.anchorMin = Vector2.zero;
            barBgRect.anchorMax = Vector2.one;
            barBgRect.sizeDelta = Vector2.zero;
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.sprite = GetWhitePixelSprite();
            barBgImg.type = Image.Type.Sliced;
            barBgImg.color = new Color(0.24f, 0.24f, 0.24f, 1f);

            var barFill = CreateUIObject("BarFill", barContainer.transform);
            var barFillRect = barFill.GetComponent<RectTransform>();
            barFillRect.anchorMin = Vector2.zero;
            barFillRect.anchorMax = Vector2.one;
            barFillRect.sizeDelta = Vector2.zero;
            var barFillImg = barFill.AddComponent<Image>();
            barFillImg.sprite = GetWhitePixelSprite();
            barFillImg.color = new Color(0.86f, 0.24f, 0.24f, 1f);
            barFillImg.type = Image.Type.Filled;
            barFillImg.fillMethod = Image.FillMethod.Horizontal;

            // HP text below bar
            var hpTextGo = CreateUIObject("HPText", panel.transform);
            var hpText = hpTextGo.AddComponent<TextMeshProUGUI>();
            hpText.text = "";
            hpText.fontSize = 14f;
            hpText.alignment = TextAlignmentOptions.Center;
            hpText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            var hpTextLayout = hpTextGo.AddComponent<LayoutElement>();
            hpTextLayout.preferredHeight = 18f;

            // Attach TargetHUD component
            _targetHUD = panel.AddComponent<TargetHUD>();
            _targetHUD.SetUIReferences(canvasGroup, nameText, stateText, barFillImg, hpText);
        }

        private GameObject CreateBarRow(Transform parent, string label,
            out Image fill, out Image bg, out TextMeshProUGUI text, Color fillColor)
        {
            var row = CreateUIObject($"{label}Row", parent);
            var rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 26f;

            // Horizontal layout: label + bar
            var hLayout = row.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing = 8f;
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;

            // Label text
            var labelGo = CreateUIObject($"{label}Label", row.transform);
            text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = $"{label}: 0/0";
            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = Color.white;
            var labelLayout = labelGo.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 110f;

            // Bar container
            var barContainer = CreateUIObject($"{label}Bar", row.transform);
            var barContainerLayout = barContainer.AddComponent<LayoutElement>();
            barContainerLayout.flexibleWidth = 1f;

            // Bar background
            var bgGo = CreateUIObject("BG", barContainer.transform);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            bg = bgGo.AddComponent<Image>();
            bg.sprite = GetWhitePixelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // Bar fill
            var fillGo = CreateUIObject("Fill", barContainer.transform);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            fill = fillGo.AddComponent<Image>();
            fill.sprite = GetWhitePixelSprite();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            return row;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Sprite _whitePixelSprite;

        private static Sprite GetWhitePixelSprite()
        {
            if (_whitePixelSprite != null) return _whitePixelSprite;
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            _whitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _whitePixelSprite;
        }

        protected override void OnDestroy()
        {
            if (_playerMana != null)
                _playerMana.OnManaChanged -= OnPlayerManaChanged;

            base.OnDestroy();
        }
    }
}
