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
    public partial class DeathScreenUI : SingletonMonoBehaviour<DeathScreenUI>
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
            _shown = false;
            Debug.Log("[DeathScreenUI] Restarting gameplay...");
            SceneTransitionManager.LoadScene(gameplaySceneName);
        }

        private void OnMainMenuClicked()
        {
            _shown = false;
            Debug.Log("[DeathScreenUI] Returning to main menu...");
            SceneTransitionManager.LoadScene(mainMenuSceneName);
        }

        private partial void BuildUI();

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
