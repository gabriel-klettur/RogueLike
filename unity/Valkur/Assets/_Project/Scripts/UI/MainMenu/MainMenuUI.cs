using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Data;

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
        private GameObject _classSelectionPanel;
        private readonly System.Collections.Generic.List<Button> _classButtons = new System.Collections.Generic.List<Button>();
        private readonly System.Collections.Generic.List<TextMeshProUGUI> _classMarkerTexts = new System.Collections.Generic.List<TextMeshProUGUI>();
        private readonly System.Collections.Generic.List<string> _classKeys = new System.Collections.Generic.List<string>();
        private int _selectedClassIndex;
        private bool _showingClassSelector;

        private InputAction _navUpAction;
        private InputAction _navDownAction;
        private InputAction _navLeftAction;
        private InputAction _navRightAction;
        private InputAction _confirmAction;
        private InputAction _cancelAction;

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

            _navLeftAction = new InputAction("MenuNavLeft", InputActionType.Button);
            _navLeftAction.AddBinding("<Keyboard>/leftArrow");
            _navLeftAction.AddBinding("<Keyboard>/a");
            _navLeftAction.Enable();

            _navRightAction = new InputAction("MenuNavRight", InputActionType.Button);
            _navRightAction.AddBinding("<Keyboard>/rightArrow");
            _navRightAction.AddBinding("<Keyboard>/d");
            _navRightAction.Enable();

            _confirmAction = new InputAction("MenuConfirm", InputActionType.Button);
            _confirmAction.AddBinding("<Keyboard>/enter");
            _confirmAction.AddBinding("<Keyboard>/space");
            _confirmAction.Enable();

            _cancelAction = new InputAction("MenuCancel", InputActionType.Button);
            _cancelAction.AddBinding("<Keyboard>/escape");
            _cancelAction.Enable();

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
            if (_showingClassSelector)
            {
                HandleClassSelectorInput();
                return;
            }

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
                    OpenClassSelector();
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

        private void OpenClassSelector()
        {
            if (_classSelectionPanel == null)
                return;

            _showingClassSelector = true;
            _classSelectionPanel.SetActive(true);
            SetSelectedClassIndex(FindSelectedClassIndex());
        }

        private void CloseClassSelector()
        {
            _showingClassSelector = false;
            if (_classSelectionPanel != null)
                _classSelectionPanel.SetActive(false);
        }

        private void HandleClassSelectorInput()
        {
            if (_cancelAction != null && _cancelAction.WasPerformedThisFrame())
            {
                CloseClassSelector();
                return;
            }

            if (_classButtons.Count == 0)
                return;

            if (_navLeftAction != null && _navLeftAction.WasPerformedThisFrame())
            {
                SetSelectedClassIndex(_selectedClassIndex - 1);
            }
            else if (_navRightAction != null && _navRightAction.WasPerformedThisFrame())
            {
                SetSelectedClassIndex(_selectedClassIndex + 1);
            }
            else if (_confirmAction != null && _confirmAction.WasPerformedThisFrame())
            {
                ApplySelectedClassAndStartGame();
            }
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

            BuildClassSelectorPanel(canvasGo.transform);

            // Initial selection (deferred one frame so layout has resolved)
            _selectedIndex = 0;
            StartCoroutine(DeferredUpdateSelection());
        }

        private void BuildClassSelectorPanel(Transform canvasTransform)
        {
            _classSelectionPanel = CreateUIObject("ClassSelectionOverlay", canvasTransform);
            var overlayRect = _classSelectionPanel.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            var overlayImage = _classSelectionPanel.AddComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.72f);

            var panel = CreateUIObject("ClassSelectionPanel", _classSelectionPanel.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1120f, 560f);
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.11f, 0.11f, 0.16f, 0.97f);

            var titleGo = CreateUIObject("ClassSelectorTitle", panel.transform);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);
            titleRect.sizeDelta = new Vector2(900f, 48f);
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "Selecciona Personaje";
            titleText.fontSize = 40f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = titleColor;
            titleText.fontStyle = FontStyles.Bold;

            var rowGo = CreateUIObject("ClassCardsRow", panel.transform);
            var rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, 30f);
            rowRect.sizeDelta = new Vector2(1040f, 320f);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;

            _classButtons.Clear();
            _classMarkerTexts.Clear();
            _classKeys.Clear();

            var presets = PlayerClassCatalog.AllPresets;
            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var key = preset.PlayerKey;

                var cardGo = CreateUIObject($"Class_{key}", rowGo.transform);
                var cardLayout = cardGo.AddComponent<LayoutElement>();
                cardLayout.preferredWidth = 196f;
                cardLayout.preferredHeight = 300f;

                var cardImage = cardGo.AddComponent<Image>();
                cardImage.color = buttonNormalColor;

                var cardButton = cardGo.AddComponent<Button>();
                cardButton.targetGraphic = cardImage;
                int captured = i;
                cardButton.onClick.AddListener(() => SetSelectedClassIndex(captured));

                var nameGo = CreateUIObject("Name", cardGo.transform);
                var nameRect = nameGo.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(1f, 1f);
                nameRect.pivot = new Vector2(0.5f, 1f);
                nameRect.anchoredPosition = new Vector2(0f, -12f);
                nameRect.sizeDelta = new Vector2(-12f, 40f);
                var nameText = nameGo.AddComponent<TextMeshProUGUI>();
                nameText.text = preset.DisplayName;
                nameText.fontSize = 24f;
                nameText.alignment = TextAlignmentOptions.Center;
                nameText.color = buttonTextColor;
                nameText.fontStyle = FontStyles.Bold;

                var markerGo = CreateUIObject("Marker", cardGo.transform);
                var markerRect = markerGo.GetComponent<RectTransform>();
                markerRect.anchorMin = new Vector2(0.5f, 0.5f);
                markerRect.anchorMax = new Vector2(0.5f, 0.5f);
                markerRect.pivot = new Vector2(0.5f, 0.5f);
                markerRect.anchoredPosition = new Vector2(0f, 20f);
                markerRect.sizeDelta = new Vector2(90f, 90f);
                var markerText = markerGo.AddComponent<TextMeshProUGUI>();
                markerText.text = string.Empty;
                markerText.fontSize = 68f;
                markerText.alignment = TextAlignmentOptions.Center;
                markerText.color = titleColor;
                markerText.fontStyle = FontStyles.Bold;

                var statsGo = CreateUIObject("Stats", cardGo.transform);
                var statsRect = statsGo.GetComponent<RectTransform>();
                statsRect.anchorMin = new Vector2(0f, 0f);
                statsRect.anchorMax = new Vector2(1f, 0f);
                statsRect.pivot = new Vector2(0.5f, 0f);
                statsRect.anchoredPosition = new Vector2(0f, 10f);
                statsRect.sizeDelta = new Vector2(-18f, 130f);
                var statsText = statsGo.AddComponent<TextMeshProUGUI>();
                statsText.text = $"HP {preset.MaxStrength}\nMP {preset.MaxIntelligence}\nSPD {preset.BasicSpeed:0.#}\nATK {preset.BasicAttack}";
                statsText.fontSize = 19f;
                statsText.alignment = TextAlignmentOptions.TopLeft;
                statsText.color = new Color(0.84f, 0.84f, 0.9f, 1f);

                _classButtons.Add(cardButton);
                _classMarkerTexts.Add(markerText);
                _classKeys.Add(key);
            }

            var actionsGo = CreateUIObject("ClassActions", panel.transform);
            var actionsRect = actionsGo.GetComponent<RectTransform>();
            actionsRect.anchorMin = new Vector2(0.5f, 0f);
            actionsRect.anchorMax = new Vector2(0.5f, 0f);
            actionsRect.pivot = new Vector2(0.5f, 0f);
            actionsRect.anchoredPosition = new Vector2(0f, 20f);
            actionsRect.sizeDelta = new Vector2(520f, 56f);
            var actionsLayout = actionsGo.AddComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 16f;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childForceExpandHeight = true;

            var confirmGo = CreateUIObject("ConfirmButton", actionsGo.transform);
            confirmGo.AddComponent<LayoutElement>().preferredHeight = 56f;
            var confirmImage = confirmGo.AddComponent<Image>();
            confirmImage.color = new Color(0.24f, 0.47f, 0.2f, 1f);
            var confirmButton = confirmGo.AddComponent<Button>();
            confirmButton.targetGraphic = confirmImage;
            confirmButton.onClick.AddListener(ApplySelectedClassAndStartGame);
            var confirmText = CreateUIObject("Text", confirmGo.transform).AddComponent<TextMeshProUGUI>();
            var confirmTextRect = confirmText.GetComponent<RectTransform>();
            confirmTextRect.anchorMin = Vector2.zero;
            confirmTextRect.anchorMax = Vector2.one;
            confirmTextRect.sizeDelta = Vector2.zero;
            confirmText.text = "Confirmar";
            confirmText.fontSize = 24f;
            confirmText.alignment = TextAlignmentOptions.Center;
            confirmText.color = Color.white;
            confirmText.fontStyle = FontStyles.Bold;

            var cancelGo = CreateUIObject("CancelButton", actionsGo.transform);
            cancelGo.AddComponent<LayoutElement>().preferredHeight = 56f;
            var cancelImage = cancelGo.AddComponent<Image>();
            cancelImage.color = new Color(0.34f, 0.2f, 0.2f, 1f);
            var cancelButton = cancelGo.AddComponent<Button>();
            cancelButton.targetGraphic = cancelImage;
            cancelButton.onClick.AddListener(CloseClassSelector);
            var cancelText = CreateUIObject("Text", cancelGo.transform).AddComponent<TextMeshProUGUI>();
            var cancelTextRect = cancelText.GetComponent<RectTransform>();
            cancelTextRect.anchorMin = Vector2.zero;
            cancelTextRect.anchorMax = Vector2.one;
            cancelTextRect.sizeDelta = Vector2.zero;
            cancelText.text = "Cancelar";
            cancelText.fontSize = 24f;
            cancelText.alignment = TextAlignmentOptions.Center;
            cancelText.color = Color.white;
            cancelText.fontStyle = FontStyles.Bold;

            var helpGo = CreateUIObject("ClassSelectorHint", panel.transform);
            var helpRect = helpGo.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0.5f, 0f);
            helpRect.anchorMax = new Vector2(0.5f, 0f);
            helpRect.pivot = new Vector2(0.5f, 0f);
            helpRect.anchoredPosition = new Vector2(0f, 84f);
            helpRect.sizeDelta = new Vector2(900f, 28f);
            var helpText = helpGo.AddComponent<TextMeshProUGUI>();
            helpText.text = "A/D o \u2190\u2192 para elegir clase, Enter para confirmar";
            helpText.fontSize = 18f;
            helpText.alignment = TextAlignmentOptions.Center;
            helpText.color = versionColor;

            _classSelectionPanel.SetActive(false);
            _selectedClassIndex = FindSelectedClassIndex();
            UpdateClassSelectionUI();
        }

        private int FindSelectedClassIndex()
        {
            if (_classKeys.Count == 0)
                return 0;

            string selectedKey = PlayerSelectionState.SelectedPlayerKey;
            for (int i = 0; i < _classKeys.Count; i++)
            {
                if (string.Equals(_classKeys[i], selectedKey, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        private void SetSelectedClassIndex(int index)
        {
            if (_classButtons.Count == 0)
                return;

            if (index < 0)
                index = _classButtons.Count - 1;
            else if (index >= _classButtons.Count)
                index = 0;

            _selectedClassIndex = index;
            UpdateClassSelectionUI();
        }

        private void UpdateClassSelectionUI()
        {
            for (int i = 0; i < _classButtons.Count; i++)
            {
                bool selected = i == _selectedClassIndex;
                var image = _classButtons[i].GetComponent<Image>();
                if (image != null)
                    image.color = selected ? buttonHoverColor : buttonNormalColor;

                if (i < _classMarkerTexts.Count)
                {
                    _classMarkerTexts[i].text = selected
                        ? char.ToUpperInvariant(_classKeys[i][0]).ToString()
                        : string.Empty;
                }
            }
        }

        private void ApplySelectedClassAndStartGame()
        {
            if (_selectedClassIndex < 0 || _selectedClassIndex >= _classKeys.Count)
                return;

            PlayerSelectionState.SetSelectedPlayer(_classKeys[_selectedClassIndex]);
            CloseClassSelector();
            StartNewGame();
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
            _navLeftAction?.Disable();
            _navLeftAction?.Dispose();
            _navRightAction?.Disable();
            _navRightAction?.Dispose();
            _confirmAction?.Disable();
            _confirmAction?.Dispose();
            _cancelAction?.Disable();
            _cancelAction?.Dispose();
        }
    }
}
