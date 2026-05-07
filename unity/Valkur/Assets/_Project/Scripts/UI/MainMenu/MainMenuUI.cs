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

        // Menu input is read directly from Valkur.Core.Input.InputCompat (which
        // already ORs the new InputSystem with the legacy backend) so this UI
        // doesn't need to spin up its own InputActions. The previous ad-hoc
        // _navUp/Down/Left/Right/_confirm/_cancel fields were removed in favour
        // of the centralized InputCompat helpers — single source of truth for
        // menu navigation.

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
            EnsureCamera();
            EnsureAudioManager();
            // Drop phantom Lv.0/Lobby run folders that accumulated from earlier
            // sessions where the player exited without doing anything worth
            // saving. The PauseMenu Exit gate now prevents new ones, but legacy
            // junk on disk would still pollute the Load Game panel until pruned.
            // Runs at MainMenu Start (no active SaveService run to protect).
            try { SaveFileManager.PrunePhantomRuns(); }
            catch (System.Exception ex)
            { Debug.LogWarning($"[MainMenu] PrunePhantomRuns failed: {ex.Message}"); }
            BuildMenuOptions();
            BuildUI();
            PlayMenuMusic();
        }

        private void Update()
        {
            if (HandlePressToStart()) return;
            if (_showingClassSelector) { HandleClassSelectorInput(); return; }

            // Universal ESC fallback for sub-screens. The per-screen handlers
            // also check Cancel, but reading it here at the top guarantees ESC
            // always returns the user to the parent screen even if the
            // EventSystem is holding a Selectable focus (e.g. a slider in
            // Sound Options that captured keyboard input on its last click).
            // Skipped while the Inputs panel is mid-rebind so ESC still
            // cancels the rebind dialog instead of leaving the Inputs panel.
            // LoadGame has its own modal ESC semantics (Rename / ConfirmDelete)
            // and is left alone here.
            if (Valkur.Core.Input.InputCompat.CancelPressed())
            {
                switch (_menuScreen)
                {
                    case MenuScreen.Options:
                    case MenuScreen.Sounds:
                        OptionsGoBack();
                        return;
                    case MenuScreen.Inputs:
                        if (_optRebinder == null || !_optRebinder.IsActive)
                        { OptionsGoBack(); return; }
                        break;
                }
            }

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
            // Nav/Confirm/Cancel actions live in InputCompat (no per-instance state
            // to dispose). Only the rebinder + portrait cache are owned here.
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
            // Reuse existing AudioManager if already instantiated in ServiceLocator
            if (ServiceLocator.Get<IAudioService>() != null)
            {
                Debug.Log("[MainMenuUI] AudioManager already running (singleton persists).");
                return;
            }

            // Check if AudioManager exists in scene (e.g., created by GlobalBootstrap)
            if (AudioManager.HasInstance)
            {
                ServiceLocator.Register<IAudioService>(AudioManager.Instance);
                Debug.Log("[MainMenuUI] AudioManager found in scene, registered with ServiceLocator.");
                return;
            }

            var catalogAsset = Resources.Load<AudioCatalogSO>("AudioCatalog");
            if (catalogAsset == null)
            {
                Debug.LogWarning("[MainMenuUI] AudioCatalog not found in Resources/ — menu music skipped.");
                return;
            }

            var go = new GameObject("AudioManager");
            var mgr = go.AddComponent<AudioManager>();
            mgr.SetCatalog(catalogAsset);
            Debug.Log("[MainMenuUI] AudioManager bootstrapped for menu (first creation).");
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
            if (hasSaves) opts.Add("Continue");
            opts.Add("New Game");
            opts.Add("Options");
            opts.Add("Exit");
            _menuOptions = opts.ToArray();
        }

        /// <summary>
        /// Rebuilds the main menu panel (e.g. after deleting all saves so the
        /// "Continuar" entry must disappear). Safe to call any time after BuildUI().
        ///
        /// The new <c>_menuPanelGo</c> is created as a sibling of the canvas
        /// (always at the end of the sibling list) and is freshly active by
        /// default. To prevent it from popping up over any open sub-screen
        /// (LoadGame, Options, ...), its <c>activeSelf</c> is forced to match
        /// the current <c>_menuScreen</c>: only visible when on the Main screen.
        /// </summary>
        private void RebuildMenuPanel()
        {
            if (_canvasTransform == null) return;
            BuildMenuOptions();
            if (_menuPanelGo != null)
            {
                if (Application.isPlaying) Destroy(_menuPanelGo);
                else DestroyImmediate(_menuPanelGo);
            }
            BuildMenuPanel(_canvasTransform);
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _menuOptions.Length - 1));
            UpdateSelection();

            // Honour the current screen so the rebuilt panel doesn't appear on
            // top of an open sub-screen (e.g. after deleting a save from the
            // Load Game panel).
            if (_menuPanelGo != null)
                _menuPanelGo.SetActive(_menuScreen == MenuScreen.Main);

            // If a sub-screen is open, make sure its overlay stays on top of the
            // freshly created main-menu sibling.
            switch (_menuScreen)
            {
                case MenuScreen.LoadGame:
                    if (_mmLoadOverlay != null) _mmLoadOverlay.transform.SetAsLastSibling();
                    break;
                case MenuScreen.Options:
                case MenuScreen.Sounds:
                case MenuScreen.Inputs:
                    if (_optOverlay != null) _optOverlay.transform.SetAsLastSibling();
                    break;
                case MenuScreen.ClassSelector:
                    if (_classSelectionPanel != null) _classSelectionPanel.transform.SetAsLastSibling();
                    break;
            }
        }



    }
}
