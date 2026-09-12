using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Save;
using Valkur.Infrastructure;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The pre-game menu: press-to-start, title screen, Options (Audio / Video / Controls /
    /// Gameplay), Load game, the class selector and the credits.
    ///
    /// <para><b>What this was.</b> A port of the Python <c>MenuRenderer</c>, kept literally: a
    /// 2 s carousel, a flat dark rectangle for every panel, seven private colour constants, the
    /// whole UI in English in front of a Spanish game, and a logo PNG that said
    /// <c>ROGUELIKE 1.0</c> — the prototype's name — on all ten screens. It was audited on
    /// 2026-09-12 at 3.4/10 (<c>.github/FRONTEND_MENUS_AUDIT_2026-09-12.md</c>).</para>
    ///
    /// <para><b>What it is now.</b> One style asset (<see cref="MenuStyle"/>), one generated
    /// atlas (<see cref="MenuArt"/>), one string table (<c>MenuText</c>), one shared widget kit
    /// (<c>Kit/</c>), one mote layer, and a title drawn out of particles from a stroke font
    /// rather than out of a texture.</para>
    ///
    /// <para><b>Nothing here has its own Update.</b> Everything that moves is advanced from
    /// <see cref="TickShell"/>, once per frame, so a test can step the whole screen and so two
    /// animations cannot disagree about what time it is.</para>
    /// </summary>
    public partial class MainMenuUI : MonoBehaviour
    {
        private const string MENU_SCENE_NAME = "MainMenu";

        [Header("Scene")]
        [SerializeField] private string gameplaySceneName = "MainGameplay";

        /// <summary>
        /// The background carousel. Five class portraits plus the vampire, who was missing: she
        /// has been playable since wave11 and the shipped list had the other five.
        /// </summary>
        private static readonly string[] BgPaths =
        {
            "UI/Intro/Intro_elven",
            "UI/Intro/Intro_drwaft",
            "UI/Intro/intro_mague",
            "UI/Intro/Intro_valkyrie",
            "UI/Intro/Intro_barbarian",
            "UI/Intro/Intro_vampire",
        };

        private readonly Image[] _bgImages = new Image[2];
        private int _carouselSlot;
        private int _bgIndex;

        // Menu
        private int _selectedIndex;
        private string[] _menuOptions;
        private GameObject _menuPanelGo;
        private Transform _canvasTransform;

        // ── Palette ──────────────────────────────────────────────────────────
        //
        // These used to be seven private `static readonly Color` constants — part of the 67 raw
        // literals the audit counted across the menus, with nothing tying them to the game's own
        // theme. They are DERIVED from MenuStyle now, so the menu and the HUD cannot disagree
        // about what gold is, and the screens that still read them (the load panel, the class
        // selector) are reading the one source of truth while their own art is rebuilt.

        private Color PanelBg => Style.Panel;
        private Color TextNormal => Style.TextPrimary;
        private Color TextSelected => Style.textOnSelection;
        private Color AccentGold => Style.Gold;
        private Color PillColor => Style.Gold;
        private Color VersionCol => Style.TextMuted;

        // Class selector
        private GameObject _classSelectionPanel;
        private readonly List<string> _classKeys = new List<string>();
        private int _selectedClassIndex;
        private bool _showingClassSelector;

        // Menu input goes through Valkur.Core.Input.InputCompat, which already ORs the new
        // InputSystem with the legacy backend — so this UI needs no InputActions of its own.

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
        }

        private void Start()
        {
            EnsureCamera();
            EnsureAudioManager();
            GameSettings.Instance?.ApplyVideoSettings();

            // Drop phantom Lv.0/Lobby run folders left by sessions that exited without doing
            // anything worth saving. The pause menu's Exit gate stops new ones; legacy junk on
            // disk would still pollute the Load Game panel until it is pruned.
            try { SaveFileManager.PrunePhantomRuns(); }
            catch (System.Exception ex)
            { Debug.LogWarning($"[MainMenu] PrunePhantomRuns failed: {ex.Message}"); }

            BuildMenuOptions();
            BuildUI();
            PlayMenuMusic();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TickShell(dt);

            if (HandlePressToStart()) return;
            if (_showingClassSelector) { HandleClassSelectorInput(); return; }

            // Universal Esc for sub-screens. Each screen also reads Cancel, but reading it here
            // first guarantees Esc always returns to the parent even when the EventSystem is
            // holding a Selectable's focus (a slider clicked in Audio, say). LoadGame has its
            // own modal semantics (Rename / ConfirmDelete) and is left alone.
            if (InputCompat.CancelPressed())
            {
                switch (_menuScreen)
                {
                    case MenuScreen.Options:
                    case MenuScreen.Audio:
                    case MenuScreen.Video:
                    case MenuScreen.Gameplay:
                    case MenuScreen.Controls:
                    case MenuScreen.Credits:
                        OptionsGoBack();
                        return;
                }
            }

            switch (_menuScreen)
            {
                case MenuScreen.Main: HandleKeyboardNavigation(); break;
                case MenuScreen.Options: HandleOptionsListInput(); break;
                case MenuScreen.Audio: HandleAudioInput(); break;
                case MenuScreen.Video: HandleVideoInput(); break;
                case MenuScreen.Gameplay: HandleGameplayInput(); break;
                case MenuScreen.Controls: HandleControlsInput(); break;
                case MenuScreen.Credits: HandleCreditsInput(); break;
                case MenuScreen.LoadGame: HandleMMLoadInput(); break;
            }
        }

        /// <summary>
        /// Frees only what this menu MADE.
        ///
        /// <para>It used to destroy every sprite in the portrait cache, and most of those are
        /// shipped assets under <c>Resources/</c> that the menu merely borrowed: Unity refused
        /// with <c>Destroying assets is not permitted to avoid data loss</c>, six times per
        /// session, on a console this project requires to be clean. The message suggests
        /// <c>DestroyImmediate(theObject, true)</c>, which would have deleted the PNGs from the
        /// project — so the fix is the opposite of what the error asks for, and it lives at the
        /// point where ownership is still known (<c>LoadSprite</c>'s <c>ownedByUs</c>) rather
        /// than being guessed here.</para>
        /// </summary>
        private void OnDestroy()
        {
            foreach (var s in _ownedPortraitSprites)
                if (s != null) Destroy(s);
            _ownedPortraitSprites.Clear();
            _portraitSpriteCache.Clear();
        }

        private void HandleCreditsInput()
        {
            if (InputCompat.ConfirmPressed() || InputCompat.CancelPressed()) OptionsGoBack();
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

            if (AudioManager.HasInstance)
            {
                ServiceLocator.Register<IAudioService>(AudioManager.Instance);
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
        }

        private void PlayMenuMusic() => ServiceLocator.Get<IAudioService>()?.PlayMenuMusic();
    }
}
