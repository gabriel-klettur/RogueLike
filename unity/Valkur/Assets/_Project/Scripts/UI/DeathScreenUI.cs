using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI
{
    /// <summary>
    /// Full-screen death overlay shown when the player dies.
    /// Offers Reiniciar (restart gameplay scene) and Menu Principal options.
    /// Supports both mouse clicks and keyboard navigation (W/S/Arrows + Enter).
    /// Works at Time.timeScale = 0 via unscaled input polling.
    /// </summary>
    public class DeathScreenUI : SingletonMonoBehaviour<DeathScreenUI>
    {
        [Header("Scene Names")]
        [SerializeField] private string gameplaySceneName = "MainGameplay";
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        [Header("Style")]
        [SerializeField] private Color overlayColor = new Color(0.05f, 0f, 0f, 0.85f);
        [SerializeField] private Color titleColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color buttonNormalColor = new Color(0.18f, 0.12f, 0.12f, 0.95f);
        [SerializeField] private Color buttonHoverColor = new Color(0.35f, 0.15f, 0.15f, 1f);
        [SerializeField] private Color buttonSelectedColor = new Color(0.45f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color buttonTextColor = new Color(0.95f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color buttonSelectedTextColor = new Color(1f, 0.85f, 0.4f, 1f);

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Health _playerHealth;
        private bool _shown;
        private float _fadeTimer;
        private const float FADE_DURATION = 1.2f;

        private int _selectedIndex;
        private Image[] _buttonImages;
        private TextMeshProUGUI[] _buttonTexts;
        private System.Action[] _buttonActions;
        private int _buttonCount;

        private InputAction _navUpAction;
        private InputAction _navDownAction;
        private InputAction _confirmAction;

        protected override void OnSingletonAwake()
        {
            _navUpAction = new InputAction("DeathNavUp", InputActionType.Button);
            _navUpAction.AddBinding("<Keyboard>/upArrow");
            _navUpAction.AddBinding("<Keyboard>/w");
            _navUpAction.Enable();

            _navDownAction = new InputAction("DeathNavDown", InputActionType.Button);
            _navDownAction.AddBinding("<Keyboard>/downArrow");
            _navDownAction.AddBinding("<Keyboard>/s");
            _navDownAction.Enable();

            _confirmAction = new InputAction("DeathConfirm", InputActionType.Button);
            _confirmAction.AddBinding("<Keyboard>/enter");
            _confirmAction.AddBinding("<Keyboard>/space");
            _confirmAction.Enable();
        }

        private void Start()
        {
            BuildUI();
            SetVisible(false);
            FindAndSubscribePlayer();
        }

        private void Update()
        {
            if (_playerHealth == null)
                FindAndSubscribePlayer();

            if (!_shown) return;

            // Fade in (works at timeScale=0 via unscaledDeltaTime)
            if (_canvasGroup != null)
            {
                if (_canvasGroup.alpha < 1f)
                {
                    _fadeTimer += Time.unscaledDeltaTime;
                    _canvasGroup.alpha = Mathf.Clamp01(_fadeTimer / FADE_DURATION);
                }

                // Enable interaction once fade is past 50%
                bool interactive = _canvasGroup.alpha >= 0.5f;
                _canvasGroup.interactable = interactive;
                _canvasGroup.blocksRaycasts = interactive;
            }

            // Keyboard navigation (uses Input which works at timeScale=0)
            if (_canvasGroup != null && _canvasGroup.alpha >= 0.5f)
                HandleKeyboardNavigation();
        }

        private void HandleKeyboardNavigation()
        {
            if (_buttonActions == null || _buttonCount == 0) return;

            if (_navUpAction != null && _navUpAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex - 1 + _buttonCount) % _buttonCount;
                UpdateButtonHighlights();
            }
            else if (_navDownAction != null && _navDownAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex + 1) % _buttonCount;
                UpdateButtonHighlights();
            }
            else if (_confirmAction != null && _confirmAction.WasPerformedThisFrame())
            {
                _buttonActions[_selectedIndex]?.Invoke();
            }
        }

        private void UpdateButtonHighlights()
        {
            for (int i = 0; i < _buttonCount; i++)
            {
                bool selected = i == _selectedIndex;
                _buttonImages[i].color = selected ? buttonSelectedColor : buttonNormalColor;
                _buttonTexts[i].color = selected ? buttonSelectedTextColor : buttonTextColor;
            }
        }

        private void FindAndSubscribePlayer()
        {
            if (_playerHealth != null) return;

            var player = EntityRegistry.Player;
            if (player == null) return;

            _playerHealth = player.GetComponent<Health>();
            if (_playerHealth != null)
            {
                _playerHealth.OnDeath += OnPlayerDeath;
            }
        }

        private void OnPlayerDeath()
        {
            if (_shown) return;
            _shown = true;
            _fadeTimer = 0f;
            _selectedIndex = 0;
            SetVisible(true);
            Time.timeScale = 0f;
            UpdateButtonHighlights();
            Debug.Log("[DeathScreenUI] Player died — showing death screen.");
        }

        private void SetVisible(bool visible)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(visible);

            if (_canvasGroup == null) return;
            // When showing: start at 0 for fade-in (Update handles the rest)
            // When hiding: set to 0 immediately
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }

        private void OnRestartClicked()
        {
            Time.timeScale = 1f;
            _shown = false;
            Debug.Log("[DeathScreenUI] Restarting gameplay...");
            SceneManager.LoadScene(gameplaySceneName);
        }

        private void OnMainMenuClicked()
        {
            Time.timeScale = 1f;
            _shown = false;
            Debug.Log("[DeathScreenUI] Returning to main menu...");
            SceneManager.LoadScene(mainMenuSceneName);
        }

        private void BuildUI()
        {
            // Ensure EventSystem exists (required for mouse clicks on UI)
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            // Canvas (overlay, high sort order)
            var canvasGo = new GameObject("DeathScreenCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
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

        protected override void OnDestroy()
        {
            _navUpAction?.Disable();
            _navUpAction?.Dispose();
            _navDownAction?.Disable();
            _navDownAction?.Dispose();
            _confirmAction?.Disable();
            _confirmAction?.Dispose();

            if (_playerHealth != null)
                _playerHealth.OnDeath -= OnPlayerDeath;

            base.OnDestroy();
        }
    }
}
