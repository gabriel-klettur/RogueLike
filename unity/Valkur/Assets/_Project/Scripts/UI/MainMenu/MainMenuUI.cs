using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Builds and manages the main menu UI programmatically.
    /// Mirrors Python's MenuManager: start mode with title, background, and menu options.
    /// Options: Nuevo Juego, Opciones, Salir (+ Continuar/Cargar if saves exist).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        private const string MENU_SCENE_NAME = "MainMenu";

        [Header("Scene")]
        [SerializeField] private string gameplaySceneName = "MainGameplay";

        [Header("Style")]
        [SerializeField] private Color bgColor = new Color(0.08f, 0.08f, 0.12f, 1f);
        [SerializeField] private Color titleColor = new Color(0.85f, 0.75f, 0.45f, 1f);
        [SerializeField] private Color buttonNormalColor = new Color(0.18f, 0.18f, 0.24f, 0.9f);
        [SerializeField] private Color buttonHoverColor = new Color(0.28f, 0.25f, 0.35f, 1f);
        [SerializeField] private Color buttonTextColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color versionColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

        private Canvas _canvas;
        private int _selectedIndex;
        private Button[] _buttons;
        private TextMeshProUGUI[] _buttonTexts;
        private Image _selectionIndicator;

        private InputAction _navUpAction;
        private InputAction _navDownAction;
        private InputAction _confirmAction;

        private readonly string[] _menuOptions = {
            "Nuevo Juego",
            "Opciones",
            "Salir"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            // Also check the current scene in case we're already in MainMenu
            if (SceneManager.GetActiveScene().name == MENU_SCENE_NAME)
                CreateInstance();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MENU_SCENE_NAME)
                CreateInstance();
        }

        private static void CreateInstance()
        {
            if (FindObjectOfType<MainMenuUI>() != null) return;
            var go = new GameObject("MainMenuUI");
            go.AddComponent<MainMenuUI>();
            Debug.Log("[MainMenuUI] Auto-bootstrapped in MainMenu scene.");
        }

        private void Start()
        {
            _navUpAction = new InputAction("MenuNavUp", InputActionType.Button);
            _navUpAction.AddBinding("<Keyboard>/upArrow");
            _navUpAction.AddBinding("<Keyboard>/w");
            _navUpAction.Enable();

            _navDownAction = new InputAction("MenuNavDown", InputActionType.Button);
            _navDownAction.AddBinding("<Keyboard>/downArrow");
            _navDownAction.AddBinding("<Keyboard>/s");
            _navDownAction.Enable();

            _confirmAction = new InputAction("MenuConfirm", InputActionType.Button);
            _confirmAction.AddBinding("<Keyboard>/enter");
            _confirmAction.AddBinding("<Keyboard>/space");
            _confirmAction.Enable();

            EnsureCamera();
            BuildUI();
        }

        /// <summary>
        /// Ensures a Camera exists in the scene to suppress
        /// "Display 1 No cameras rendering" message.
        /// </summary>
        private void EnsureCamera()
        {
            if (Camera.main != null) return;

            var camGo = new GameObject("MainMenuCamera");
            camGo.transform.SetParent(transform);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bgColor;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.depth = -1f;
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        private void Update()
        {
            HandleKeyboardNavigation();
        }

        private void HandleKeyboardNavigation()
        {
            if (_buttons == null || _buttons.Length == 0) return;

            if (_navUpAction != null && _navUpAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex - 1 + _buttons.Length) % _buttons.Length;
                UpdateSelection();
            }
            else if (_navDownAction != null && _navDownAction.WasPerformedThisFrame())
            {
                _selectedIndex = (_selectedIndex + 1) % _buttons.Length;
                UpdateSelection();
            }
            else if (_confirmAction != null && _confirmAction.WasPerformedThisFrame())
            {
                ExecuteOption(_selectedIndex);
            }
        }

        private void UpdateSelection()
        {
            if (_selectionIndicator == null || _buttons == null) return;

            var targetRect = _buttons[_selectedIndex].GetComponent<RectTransform>();
            var indicatorRect = _selectionIndicator.GetComponent<RectTransform>();
            indicatorRect.position = targetRect.position;
            indicatorRect.sizeDelta = targetRect.sizeDelta + new Vector2(8f, 8f);

            for (int i = 0; i < _buttonTexts.Length; i++)
            {
                _buttonTexts[i].color = i == _selectedIndex
                    ? titleColor
                    : buttonTextColor;
            }
        }

        private void ExecuteOption(int index)
        {
            if (index < 0 || index >= _menuOptions.Length) return;

            string option = _menuOptions[index];
            switch (option)
            {
                case "Nuevo Juego":
                    StartNewGame();
                    break;
                case "Opciones":
                    Debug.Log("[MainMenu] Opciones (not yet implemented)");
                    break;
                case "Salir":
                    QuitGame();
                    break;
            }
        }

        private void StartNewGame()
        {
            Debug.Log("[MainMenu] Starting new game...");
            SceneTransitionManager.LoadScene(gameplaySceneName);
        }

        private void QuitGame()
        {
            Debug.Log("[MainMenu] Quitting...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildUI()
        {
            // Canvas
            var canvasGo = new GameObject("MainMenuCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Full-screen background
            var bgGo = CreateUIObject("Background", canvasGo.transform);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = bgColor;

            // Title
            var titleGo = CreateUIObject("Title", canvasGo.transform);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -80f);
            titleRect.sizeDelta = new Vector2(800f, 120f);

            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "VALKUR";
            titleText.fontSize = 72f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = titleColor;
            titleText.fontStyle = FontStyles.Bold;

            // Subtitle
            var subGo = CreateUIObject("Subtitle", canvasGo.transform);
            var subRect = subGo.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 1f);
            subRect.anchorMax = new Vector2(0.5f, 1f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -195f);
            subRect.sizeDelta = new Vector2(600f, 40f);

            var subText = subGo.AddComponent<TextMeshProUGUI>();
            subText.text = "A Roguelike Adventure";
            subText.fontSize = 24f;
            subText.alignment = TextAlignmentOptions.Center;
            subText.color = new Color(0.6f, 0.6f, 0.65f, 0.8f);
            subText.fontStyle = FontStyles.Italic;

            // Button container (centered)
            var containerGo = CreateUIObject("ButtonContainer", canvasGo.transform);
            var containerRect = containerGo.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(0f, -30f);
            containerRect.sizeDelta = new Vector2(340f, 300f);

            var layout = containerGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleCenter;

            // Selection indicator (behind selected button)
            var indicatorGo = CreateUIObject("SelectionIndicator", canvasGo.transform);
            _selectionIndicator = indicatorGo.AddComponent<Image>();
            _selectionIndicator.color = new Color(0.85f, 0.75f, 0.45f, 0.15f);
            var indicatorRect2 = indicatorGo.GetComponent<RectTransform>();
            indicatorRect2.sizeDelta = new Vector2(348f, 58f);

            // Buttons
            _buttons = new Button[_menuOptions.Length];
            _buttonTexts = new TextMeshProUGUI[_menuOptions.Length];

            for (int i = 0; i < _menuOptions.Length; i++)
            {
                var btnGo = CreateUIObject($"Btn_{_menuOptions[i]}", containerGo.transform);
                var btnLayout = btnGo.AddComponent<LayoutElement>();
                btnLayout.preferredHeight = 50f;

                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = buttonNormalColor;

                var btn = btnGo.AddComponent<Button>();
                var colors = btn.colors;
                colors.normalColor = buttonNormalColor;
                colors.highlightedColor = buttonHoverColor;
                colors.pressedColor = buttonHoverColor;
                colors.selectedColor = buttonHoverColor;
                btn.colors = colors;
                btn.targetGraphic = btnImg;

                int capturedIndex = i;
                btn.onClick.AddListener(() =>
                {
                    _selectedIndex = capturedIndex;
                    UpdateSelection();
                    ExecuteOption(capturedIndex);
                });

                // Mouse hover: sync selection indicator with hovered button
                var trigger = btnGo.AddComponent<EventTrigger>();
                var pointerEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                pointerEnter.callback.AddListener(_ =>
                {
                    _selectedIndex = capturedIndex;
                    UpdateSelection();
                });
                trigger.triggers.Add(pointerEnter);

                // Button text
                var textGo = CreateUIObject("Text", btnGo.transform);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;

                var btnText = textGo.AddComponent<TextMeshProUGUI>();
                btnText.text = _menuOptions[i];
                btnText.fontSize = 26f;
                btnText.alignment = TextAlignmentOptions.Center;
                btnText.color = buttonTextColor;

                _buttons[i] = btn;
                _buttonTexts[i] = btnText;
            }

            // Version text (bottom-right)
            var verGo = CreateUIObject("Version", canvasGo.transform);
            var verRect = verGo.GetComponent<RectTransform>();
            verRect.anchorMin = new Vector2(1f, 0f);
            verRect.anchorMax = new Vector2(1f, 0f);
            verRect.pivot = new Vector2(1f, 0f);
            verRect.anchoredPosition = new Vector2(-15f, 10f);
            verRect.sizeDelta = new Vector2(300f, 30f);

            var verText = verGo.AddComponent<TextMeshProUGUI>();
            verText.text = $"v{Application.version} | Unity {Application.unityVersion}";
            verText.fontSize = 14f;
            verText.alignment = TextAlignmentOptions.Right;
            verText.color = versionColor;

            // Controls hint (bottom-left)
            var hintGo = CreateUIObject("Hint", canvasGo.transform);
            var hintRect = hintGo.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(0f, 0f);
            hintRect.pivot = new Vector2(0f, 0f);
            hintRect.anchoredPosition = new Vector2(15f, 10f);
            hintRect.sizeDelta = new Vector2(400f, 30f);

            var hintText = hintGo.AddComponent<TextMeshProUGUI>();
            hintText.text = "Mouse or W/S \u2191\u2193 Navigate  |  Click or Enter Select";
            hintText.fontSize = 14f;
            hintText.alignment = TextAlignmentOptions.Left;
            hintText.color = versionColor;

            // Initial selection (deferred one frame so layout has resolved)
            _selectedIndex = 0;
            StartCoroutine(DeferredUpdateSelection());
        }

        private System.Collections.IEnumerator DeferredUpdateSelection()
        {
            yield return null;
            UpdateSelection();
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private void OnDestroy()
        {
            _navUpAction?.Disable();
            _navUpAction?.Dispose();
            _navDownAction?.Disable();
            _navDownAction?.Dispose();
            _confirmAction?.Disable();
            _confirmAction?.Dispose();
        }
    }
}
