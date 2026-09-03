using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.UIKit;

namespace Valkur.Gameplay.MapEditor.Backups
{
    /// <summary>
    /// Self-contained backup browser. Spawned on demand by the pause menu;
    /// owns its own canvas so it can sort above gameplay HUDs without
    /// touching the pause menu's layout.
    ///
    /// Layout:
    ///   ┌─ MAP BACKUPS ───────────────────────────────────────┐
    ///   │ [Create backup of current map]            [Close]   │
    ///   ├──────────────────────────┬──────────────────────────┤
    ///   │  list of backups         │  details + Restore /     │
    ///   │  (newest first)          │  Delete (3-step + type)  │
    ///   └──────────────────────────┴──────────────────────────┘
    /// </summary>
    public partial class MapBackupBrowserUI : MonoBehaviour
    {
        public static MapBackupBrowserUI Instance { get; private set; }

        // Visual constants — kept in sync with the pause-menu palette so the
        // browser feels like a first-class screen, not a hand-rolled debug UI.
        private static readonly Color PanelBg     = new Color(22f/255f, 24f/255f, 28f/255f, 235f/255f);
        private static readonly Color OverlayBg   = new Color(0f, 0f, 0f, 200f/255f);
        private static readonly Color RowBg       = new Color(0.13f, 0.14f, 0.18f, 1f);
        private static readonly Color RowBgHover  = new Color(0.18f, 0.20f, 0.26f, 1f);
        private static readonly Color RowBgActive = new Color(0.30f, 0.25f, 0.06f, 1f);
        private static readonly Color BtnNormal   = new Color(0.22f, 0.24f, 0.30f, 1f);
        private static readonly Color BtnHover    = new Color(0.30f, 0.32f, 0.40f, 1f);
        private static readonly Color BtnDanger   = UITheme.DANGER_IDLE;
        private static readonly Color BtnDangerH  = new Color(0.75f, 0.20f, 0.20f, 1f);
        private static readonly Color TextPrimary = new Color(230f/255f, 233f/255f, 240f/255f, 1f);
        private static readonly Color TextDim     = new Color(0.60f, 0.65f, 0.72f, 1f);
        private static readonly Color Accent      = new Color(255f/255f, 200f/255f,   0f/255f, 1f);

        private const string DESTROY_PHRASE = "I WANT TO DESTROY THIS BACKUP";

        // ── Static spawn API ─────────────────────────────────────────────────────

        public static MapBackupBrowserUI Open()
        {
            if (Instance != null) { Instance.Show(); return Instance; }
            var go = new GameObject(nameof(MapBackupBrowserUI));
            DontDestroyOnLoad(go);
            var ui = go.AddComponent<MapBackupBrowserUI>();
            ui.Show();
            return ui;
        }

        // ── State ────────────────────────────────────────────────────────────────

        private MapBackupStore          _store;
        private List<MapBackupManifest> _backups = new List<MapBackupManifest>();
        private string                  _selectedId;
        private System.Action           _onClose;

        // ── Built UI refs ────────────────────────────────────────────────────────

        private Canvas          _canvas;
        private GameObject      _root;
        private RectTransform   _listContent;
        private TextMeshProUGUI _detailHeader;
        private TextMeshProUGUI _detailBody;
        private RectTransform   _detailFilesContent;
        private Button          _restoreBtn;
        private Button          _deleteBtn;
        private TextMeshProUGUI _statusLine;

        // Delete dialog (three-stage)
        private GameObject      _delDialog;
        private TextMeshProUGUI _delPrompt;
        private TMP_InputField  _delInput;
        private Button          _delConfirmBtn;
        private TextMeshProUGUI _delConfirmLabel;
        private int             _delStage; // 1, 2, 3

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _store = new MapBackupStore();
            BuildUI();
            _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetOnClose(System.Action onClose) { _onClose = onClose; }

        public void Show()
        {
            if (_root == null) return;
            _root.SetActive(true);
            RefreshList();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
            _onClose?.Invoke();
        }

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;

            // ESC closes either the delete dialog (rolling back stage) or the
            // browser. We poll the keyboard directly because this UI is opened
            // outside the InputService action map.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_delDialog != null && _delDialog.activeSelf) CloseDeleteDialog();
                else Hide();
            }
        }

        private void SetStatus(string s)
        {
            if (_statusLine != null) _statusLine.text = s;
        }

        private static string GuessActiveSlot()
        {
            // Reads the persistent _active.txt that the Map Editor's slot
            // store keeps. Avoids forcing this UI to depend on the live
            // MapEditorManager (it may not even be loaded outside the
            // gameplay scene).
            try
            {
                string p = System.IO.Path.Combine(
                    Application.persistentDataPath, "Maps", "_active.txt");
                if (System.IO.File.Exists(p))
                {
                    var s = System.IO.File.ReadAllText(p)?.Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { /* fall through to default */ }
            return "default";
        }
    }
}
