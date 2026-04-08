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
        private enum PauseScreen { None, Pause, Options, Sounds, Inputs }
        private PauseScreen _screen = PauseScreen.None;

        // ── UI roots ─────────────────────────────────────────────────────────
        private Canvas     _canvas;
        private GameObject _overlayRoot;
        private GameObject _pausePanel;
        private GameObject _optionsPanel;
        private GameObject _soundsPanel;
        private GameObject _inputsPanel;

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
            BuildCanvas();
            HideAll();
        }

        private void Update()
        {
            if (_pauseAction != null && _pauseAction.WasPerformedThisFrame())
                TogglePause();

            switch (_screen)
            {
                case PauseScreen.Pause:   HandleListInput(_pauseOptions.Length, ref _pauseSel, _pausePills, _pauseBars, _pauseTexts, ExecutePause); break;
                case PauseScreen.Options: HandleListInput(_optOptions.Length,   ref _optSel,   _optPills,   _optBars,   _optTexts,   ExecuteOption); break;
                case PauseScreen.Sounds:  HandleSoundsInput();  break;
                case PauseScreen.Inputs:  HandleInputsTabInput(); break;
            }
        }

        private void OnDestroy()
        {
            _pauseAction?.Disable(); _pauseAction?.Dispose();
            _navUp?.Disable();   _navUp?.Dispose();
            _navDown?.Disable(); _navDown?.Dispose();
            _navLeft?.Disable(); _navLeft?.Dispose();
            _navRight?.Disable(); _navRight?.Dispose();
            _confirm?.Disable(); _confirm?.Dispose();
            _cancel?.Disable();  _cancel?.Dispose();
        }

        // ════════════════════════════════════════════════════════════════════
        // Public API
        // ════════════════════════════════════════════════════════════════════

        public void TogglePause()
        {
            if (_screen == PauseScreen.None)
                OpenPause();
            else
                ClosePause();
        }

        public void OpenPause()
        {
            RebuildPauseOptions();
            ShowScreen(PauseScreen.Pause);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(true);
        }

        public void ClosePause()
        {
            ShowScreen(PauseScreen.None);
            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(false);
        }

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        private void ShowScreen(PauseScreen s)
        {
            _screen = s;
            _overlayRoot.SetActive(s != PauseScreen.None);
            _pausePanel.SetActive(s == PauseScreen.Pause);
            _optionsPanel.SetActive(s == PauseScreen.Options);
            _soundsPanel.SetActive(s == PauseScreen.Sounds);
            _inputsPanel.SetActive(s == PauseScreen.Inputs);

            if (s == PauseScreen.Pause)   { _pauseSel = 0;  UpdateListVisuals(_pauseSel,  _pausePills,  _pauseBars,  _pauseTexts); }
            if (s == PauseScreen.Options) { _optSel = 0;    UpdateListVisuals(_optSel,    _optPills,    _optBars,    _optTexts);   }
            if (s == PauseScreen.Sounds)  { _soundSel = 0;  UpdateSoundsPanel(); }
            if (s == PauseScreen.Inputs)  { _inputsTabSel = 0; _inputsRowSel = 0; UpdateInputsPanel(); }
        }

        private void HideAll() => ShowScreen(PauseScreen.None);

        // ════════════════════════════════════════════════════════════════════
        // Pause menu execution
        // ════════════════════════════════════════════════════════════════════

        private void RebuildPauseOptions()
        {
            bool hasSaves = SaveFileManager.ListSaves().Count > 0;
            var opts = new List<string> { "Continuar", "Nueva Partida", "Guardar partida" };
            if (hasSaves) opts.Add("Cargar juego");
            opts.Add("Opciones");
            opts.Add("Salir");
            _pauseOptions = opts.ToArray();
            // Rebuild panel rows to match new count
            RebuildPausePanelRows();
        }


        // ════════════════════════════════════════════════════════════════════
        // Generic list input

        // ════════════════════════════════════════════════════════════════════

        private void HandleListInput(int count, ref int sel,
            Image[] pills, Image[] bars, TextMeshProUGUI[] texts,
            System.Action<int> execute)
        {
            if (_navUp != null && _navUp.WasPerformedThisFrame())
            { sel = (sel - 1 + count) % count; UpdateListVisuals(sel, pills, bars, texts); }
            else if (_navDown != null && _navDown.WasPerformedThisFrame())
            { sel = (sel + 1) % count; UpdateListVisuals(sel, pills, bars, texts); }
            else if (_confirm != null && _confirm.WasPerformedThisFrame())
            { execute(sel); }
            else if (_cancel != null && _cancel.WasPerformedThisFrame())
            { GoBack(); }
        }

        private void UpdateListVisuals(int sel, Image[] pills, Image[] bars, TextMeshProUGUI[] texts)
        {
            if (pills == null) return;
            for (int i = 0; i < pills.Length; i++)
            {
                bool s = i == sel;
                if (pills != null && i < pills.Length) pills[i].color = s ? PillColor  : Color.clear;
                if (bars  != null && i < bars.Length)  bars[i].color  = s ? AccentGold : Color.clear;
                if (texts != null && i < texts.Length) texts[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void GoBack()
        {
            switch (_screen)
            {
                case PauseScreen.Options: ShowScreen(PauseScreen.Pause);   break;
                case PauseScreen.Sounds:  ShowScreen(PauseScreen.Options); break;
                case PauseScreen.Inputs:  ShowScreen(PauseScreen.Options); break;
                default:                  ClosePause();                     break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Sounds input
        // ════════════════════════════════════════════════════════════════════

        // Input setup and sounds/inputs handlers extracted to PauseMenuUI.Input.cs
        partial void SetupInputActions();

        // ════════════════════════════════════════════════════════════════════
        // UI Construction
        // ════════════════════════════════════════════════════════════════════

        // UI builder methods extracted to PauseMenuUI.Builder.cs
        partial void BuildCanvas();

        // Builder helpers extracted to PauseMenuUI.Builder.cs
        partial void RebuildPausePanelRows();
    }
}

