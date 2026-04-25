using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.UI.PauseMenu
{
    /// <summary>
    /// In-game pause menu opened with ESC.
    /// Mirrors Python pause mode: Continuar / Nueva Partida / Guardar partida /
    /// Cargar juego (conditional) / Opciones / Salir.
    /// Opciones opens a submenu with Inputs (keybindings) and Sounds (audio settings).
    /// Visual style matches Python MenuRenderer exactly.
    /// </summary>
    public partial class PauseMenuUI : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────
        public static PauseMenuUI Instance { get; private set; }

        private const string GAMEPLAY_SCENE = "MainGameplay";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == GAMEPLAY_SCENE)
                EnsureInstance();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == GAMEPLAY_SCENE) EnsureInstance();
        }

        private static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("PauseMenuUI");
            go.AddComponent<PauseMenuUI>();
            Debug.Log("[PauseMenuUI] Auto-spawned.");
        }

        // ── Colors matching Python MenuRenderer ──────────────────────────────
        private static readonly Color PanelBg      = new Color(22/255f, 24/255f, 28/255f, 235/255f);
        private static readonly Color OverlayBg    = new Color(0f, 0f, 0f, 140/255f);
        private static readonly Color TextNormal   = new Color(230/255f, 233/255f, 240/255f, 1f);
        private static readonly Color TextSelected = new Color(255/255f, 200/255f,   0/255f, 1f);
        private static readonly Color AccentGold   = new Color(255/255f, 200/255f,   0/255f, 1f);
        private static readonly Color PillColor    = new Color(255/255f, 200/255f,   0/255f, 38/255f);
        private static readonly Color VersionCol   = new Color(0.5f, 0.5f, 0.5f, 0.7f);

        private const string MAIN_SCENE = "MainMenu";

        // ── State ─────────────────────────────────────────────────────────────
        private enum PauseScreen { None, Pause, Options, Sounds, Inputs, LoadGame }
        private PauseScreen _screen = PauseScreen.None;

        // ── UI roots ─────────────────────────────────────────────────────────
        private Canvas     _canvas;
        private GameObject _overlayRoot;
        private GameObject _pausePanel;
        private GameObject _optionsPanel;
        private GameObject _soundsPanel;
        private GameObject _inputsPanel;
        private GameObject _loadGamePanel;

        // ── Pause panel ───────────────────────────────────────────────────────
        private string[] _pauseOptions;
        private int      _pauseSel;
        private Image[]  _pausePills;
        private Image[]  _pauseBars;
        private TextMeshProUGUI[] _pauseTexts;

        // ── Options panel ─────────────────────────────────────────────────────
        private readonly string[] _optOptions = { "Inputs", "Sonido", "Volver" };
        private int      _optSel;
        private Image[]  _optPills;
        private Image[]  _optBars;
        private TextMeshProUGUI[] _optTexts;

        // ── Sounds panel ─────────────────────────────────────────────────────
        // Each row: label | value display | - btn | + btn
        private struct SoundRow
        {
            public TextMeshProUGUI valueText;
            public float min, max, step;
            public System.Func<float> get;
            public System.Action<float> set;
        }
        private readonly List<SoundRow> _soundRows = new List<SoundRow>();
        private int _soundSel;
        private Image[] _soundPills;
        private Image[] _soundBars;
        private TextMeshProUGUI[] _soundRowLabels;

        // ── Inputs panel ─────────────────────────────────────────────────────
        private int _inputsTabSel;
#pragma warning disable CS0414
        private int _inputsRowSel;
#pragma warning restore CS0414
        private TextMeshProUGUI[] _tabLabels;

        // ── Load game panel ──────────────────────────────────────────────────
        private List<SaveSlotInfo> _loadSaves = new List<SaveSlotInfo>();
        private int _loadSel;
        private Image[] _loadPills;
        private Image[] _loadBars;
        private TextMeshProUGUI[] _loadTexts;
        private TextMeshProUGUI _loadDetailText;
        private int _loadScrollOffset;
        private const int LOAD_VISIBLE_ROWS = 8;

        // ── Input actions ────────────────────────────────────────────────────
        private InputAction _pauseAction;
        private InputAction _navUp;
        private InputAction _navDown;
        private InputAction _navLeft;
        private InputAction _navRight;
        private InputAction _confirm;
        private InputAction _cancel;

        // ════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            SetupInputActions();
            try
            {
                BuildCanvas();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PauseMenuUI] BuildCanvas failed: {e}");
            }
            HideAll();
        }

    }
}