using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Infrastructure;
using Valkur.Gameplay.Save;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Main menu UI that mirrors the Python MenuRenderer visual style:
    ///   - Animated background carousel (5 images, 2s interval, 0.6s crossfade)
    ///   - game_name.png logo at the top
    ///   - Dark panel with gold 4-px left-bar + translucent gold pill for selected row
    ///   - Dynamic options: "Continuar" / "Cargar juego" only when saves exist
    ///   - Mouse + keyboard supported
    /// </summary>
    public partial class MainMenuUI : MonoBehaviour
    {
        private const string MENU_SCENE_NAME = "MainMenu";

        [Header("Scene")]
        [SerializeField] private string gameplaySceneName = "MainGameplay";

        // Colors matching Python MenuRenderer
        private static readonly Color PanelBg      = new Color(22 / 255f, 24 / 255f, 28 / 255f, 235 / 255f);
        private static readonly Color OverlayColor  = new Color(0f, 0f, 0f, 140 / 255f);
        private static readonly Color TextNormal    = new Color(230 / 255f, 233 / 255f, 240 / 255f, 1f);
        private static readonly Color TextSelected  = new Color(255 / 255f, 200 / 255f,   0 / 255f, 1f);
        private static readonly Color AccentGold    = new Color(255 / 255f, 200 / 255f,   0 / 255f, 1f);
        private static readonly Color PillColor     = new Color(255 / 255f, 200 / 255f,   0 / 255f, 38 / 255f);
        private static readonly Color VersionCol    = new Color(0.5f, 0.5f, 0.5f, 0.7f);

        // Carousel
        private static readonly string[] BgPaths =
        {
            "UI/Intro/Intro_elven",
            "UI/Intro/Intro_drwaft",
            "UI/Intro/intro_mague",
            "UI/Intro/Intro_valkyrie",
            "UI/Intro/Intro_barbarian",
        };

        private const float CAROUSEL_INTERVAL  = 2.0f;
        private const float CAROUSEL_CROSSFADE = 0.6f;

        private readonly Image[] _bgImages = new Image[2];
        private int _carouselSlot;
        private int _bgIndex;

        // Menu
        private int                  _selectedIndex;
        private string[]             _menuOptions;
        private Image[]              _pillImages;
        private Image[]              _accentBars;
        private TextMeshProUGUI[]    _menuTexts;
        private GameObject           _menuPanelGo;
        private Transform            _canvasTransform;

        // Class selector
        private GameObject _classSelectionPanel;
        private readonly List<Button>          _classButtons     = new List<Button>();
        private readonly List<TextMeshProUGUI> _classMarkerTexts = new List<TextMeshProUGUI>();
        private readonly List<string>          _classKeys        = new List<string>();
        private int  _selectedClassIndex;
        private bool _showingClassSelector;

        // Class selector – enhanced visuals (Python parity)
        private Image _classHeaderPortrait;
        private readonly List<Image> _classCardBorderImages = new List<Image>();
        private readonly List<RectTransform> _classCardBgRects = new List<RectTransform>();
        private readonly Dictionary<string, Sprite> _portraitSpriteCache = new Dictionary<string, Sprite>();

        // Input actions
        private InputAction _navUpAction;
        private InputAction _navDownAction;
        private InputAction _navLeftAction;
        private InputAction _navRightAction;
        private InputAction _confirmAction;
        private InputAction _cancelAction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == MENU_SCENE_NAME)
                CreateInstance();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MENU_SCENE_NAME) CreateInstance();
        }

        private static void CreateInstance()
        {
            if (FindObjectOfType<MainMenuUI>() != null) return;
            var go = new GameObject("MainMenuUI");
            go.AddComponent<MainMenuUI>();
            Debug.Log("[MainMenuUI] Auto-bootstrapped.");
        }

        private void Start()
        {
            SetupInputActions();
            EnsureCamera();
            EnsureAudioManager();
            BuildMenuOptions();
            BuildUI();
            PlayMenuMusic();
        }

        private void Update()
        {
            if (HandlePressToStart()) return;
            if (_showingClassSelector) { HandleClassSelectorInput(); return; }
            switch (_menuScreen)
            {
                case MenuScreen.Main:     HandleKeyboardNavigation(); break;
                case MenuScreen.Options:  HandleOptionsListInput();   break;
                case MenuScreen.Sounds:   HandleOptionsSoundsInput(); break;
                case MenuScreen.Inputs:   HandleOptionsInputsInput(); break;
                case MenuScreen.LoadGame: HandleMMLoadInput();        break;
            }
        }

        private void OnDestroy()
        {
            _navUpAction?.Disable();    _navUpAction?.Dispose();
            _navDownAction?.Disable();  _navDownAction?.Dispose();
            _navLeftAction?.Disable();  _navLeftAction?.Dispose();
            _navRightAction?.Disable(); _navRightAction?.Dispose();
            _confirmAction?.Disable();  _confirmAction?.Dispose();
            _cancelAction?.Disable();   _cancelAction?.Dispose();

            _optRebinder?.Dispose(); _optRebinder = null;

            foreach (var s in _portraitSpriteCache.Values)
                if (s != null) Destroy(s);
            _portraitSpriteCache.Clear();
        }



        private void EnsureCamera()
        {
            if (Camera.main != null) return;
            var camGo = new GameObject("MainMenuCamera");
            camGo.transform.SetParent(transform);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.depth = -1f;
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        private void EnsureAudioManager()
        {
            if (ServiceLocator.Get<IAudioService>() != null) return;

            var catalogAsset = Resources.Load<AudioCatalogSO>("AudioCatalog");
            if (catalogAsset == null)
            {
                Debug.LogWarning("[MainMenuUI] AudioCatalog not found in Resources/ — menu music skipped.");
                return;
            }

            var go = new GameObject("AudioManager");
            var mgr = go.AddComponent<AudioManager>();
            mgr.SetCatalog(catalogAsset);
            Debug.Log("[MainMenuUI] AudioManager bootstrapped for menu.");
        }

        private void PlayMenuMusic()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            audio.PlayMenuMusic();
        }

        private void BuildMenuOptions()
        {
            bool hasSaves = SaveFileManager.ListSaves().Count > 0;
            var opts = new List<string>();
            if (hasSaves) opts.Add("Continuar");
            opts.Add("Nuevo juego");
            opts.Add("Opciones");
            opts.Add("Salir");
            _menuOptions = opts.ToArray();
        }

        /// <summary>
        /// Rebuilds the main menu panel (e.g. after deleting all saves so the
        /// "Continuar" entry must disappear). Safe to call any time after BuildUI().
        /// </summary>
        private void RebuildMenuPanel()
        {
            if (_canvasTransform == null) return;
            BuildMenuOptions();
            if (_menuPanelGo != null) Destroy(_menuPanelGo);
            BuildMenuPanel(_canvasTransform);
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _menuOptions.Length - 1));
            UpdateSelection();
        }



    }
}
