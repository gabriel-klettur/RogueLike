using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;

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
    public class MapBackupBrowserUI : MonoBehaviour
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
        private static readonly Color BtnDanger   = new Color(0.55f, 0.15f, 0.15f, 1f);
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

        private MapBackupStore        _store;
        private List<MapBackupManifest> _backups = new List<MapBackupManifest>();
        private string                _selectedId;
        private System.Action         _onClose;

        // ── Built UI refs ────────────────────────────────────────────────────────

        private Canvas         _canvas;
        private GameObject     _root;
        private RectTransform  _listContent;
        private TextMeshProUGUI _detailHeader;
        private TextMeshProUGUI _detailBody;
        private RectTransform  _detailFilesContent;
        private Button         _restoreBtn;
        private Button         _deleteBtn;
        private TextMeshProUGUI _statusLine;

        // Delete dialog (three-stage)
        private GameObject       _delDialog;
        private TextMeshProUGUI  _delPrompt;
        private TMP_InputField   _delInput;
        private Button           _delConfirmBtn;
        private TextMeshProUGUI  _delConfirmLabel;
        private int              _delStage; // 1, 2, 3

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

        // ── Build ────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200; // above pause menu
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 800f);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = MakeStretch("Root", canvasGo.transform);
            _root.AddComponent<Image>().color = OverlayBg;

            // Main panel (centered, fixed size).
            var panel = MakeRect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1100f, 660f);
            panel.AddComponent<Image>().color = PanelBg;
            var ol = panel.AddComponent<Outline>();
            ol.effectColor    = Accent;
            ol.effectDistance = new Vector2(2f, 2f);

            BuildHeader(panel.transform);
            BuildBody(panel.transform);
            BuildStatusBar(panel.transform);
            BuildDeleteDialog(canvasGo.transform);
        }

        private void BuildHeader(Transform panel)
        {
            var bar = MakeRect("Header", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
            var rt = bar.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 56f);
            bar.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f, 1f);

            var title = AddText(bar.transform, "MAP BACKUPS", 22f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot     = new Vector2(0f, 0.5f);
            titleRt.anchoredPosition = new Vector2(20f, 0f);
            titleRt.sizeDelta = new Vector2(300f, 0f);

            // Create-backup button (left of close)
            var createBtn = AddButton(bar.transform, "+ Create backup", 200f, 36f, BtnNormal, BtnHover, () =>
            {
                var manifest = _store.CreateSnapshot(GuessActiveSlot(), "Manual snapshot",
                                                     MapBackupSchema.KindManual);
                if (manifest != null)
                {
                    SetStatus($"Created snapshot '{manifest.id}' ({MapBackupStore.FormatBytes(manifest.totalBytes)}).");
                    RefreshList();
                    SelectBackup(manifest.id);
                }
                else SetStatus("Snapshot failed — see console.");
            });
            var crRt = createBtn.GetComponent<RectTransform>();
            crRt.anchorMin = new Vector2(1f, 0.5f);
            crRt.anchorMax = new Vector2(1f, 0.5f);
            crRt.pivot     = new Vector2(1f, 0.5f);
            crRt.anchoredPosition = new Vector2(-130f, 0f);

            // Close button
            var closeBtn = AddButton(bar.transform, "Close", 100f, 36f, BtnNormal, BtnHover, Hide);
            var clRt = closeBtn.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(1f, 0.5f);
            clRt.anchorMax = new Vector2(1f, 0.5f);
            clRt.pivot     = new Vector2(1f, 0.5f);
            clRt.anchoredPosition = new Vector2(-20f, 0f);
        }

        private void BuildBody(Transform panel)
        {
            var body = MakeRect("Body", panel, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var rt = body.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(10f, 38f);  // leave room for status bar
            rt.offsetMax = new Vector2(-10f, -60f); // leave room for header

            // Left: list of backups
            var leftCol = MakeRect("LeftCol", body.transform, new Vector2(0f, 0f), new Vector2(0.45f, 1f));
            leftCol.GetComponent<RectTransform>().offsetMin = new Vector2(0f, 0f);
            leftCol.GetComponent<RectTransform>().offsetMax = new Vector2(-6f, 0f);
            leftCol.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 1f);
            BuildBackupList(leftCol.transform);

            // Right: details
            var rightCol = MakeRect("RightCol", body.transform, new Vector2(0.45f, 0f), new Vector2(1f, 1f));
            rightCol.GetComponent<RectTransform>().offsetMin = new Vector2(6f, 0f);
            rightCol.GetComponent<RectTransform>().offsetMax = new Vector2(0f, 0f);
            rightCol.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 1f);
            BuildDetails(rightCol.transform);
        }

        private void BuildBackupList(Transform parent)
        {
            var hdr = AddText(parent, "BACKUPS", 13f, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var hdrRt = hdr.rectTransform;
            hdrRt.anchorMin = new Vector2(0f, 1f); hdrRt.anchorMax = new Vector2(1f, 1f);
            hdrRt.pivot = new Vector2(0f, 1f);
            hdrRt.anchoredPosition = new Vector2(12f, -8f);
            hdrRt.sizeDelta = new Vector2(0f, 20f);

            var scrollGo = MakeRect("Scroll", parent, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -32f);

            var viewport = MakeRect("Viewport", scrollGo.transform, Vector2.zero, Vector2.one);
            viewport.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;
            _listContent = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 4, 4);
            v.spacing = 4f;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content  = contentRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
        }

        private void BuildDetails(Transform parent)
        {
            _detailHeader = AddText(parent, "Select a backup", 16f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var hRt = _detailHeader.rectTransform;
            hRt.anchorMin = new Vector2(0f, 1f); hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0f, 1f);
            hRt.anchoredPosition = new Vector2(12f, -8f);
            hRt.sizeDelta = new Vector2(-12f, 24f);

            _detailBody = AddText(parent, "", 12f, TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            var bRt = _detailBody.rectTransform;
            bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
            bRt.pivot = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(12f, -38f);
            bRt.sizeDelta = new Vector2(-24f, 100f);
            _detailBody.enableWordWrapping = true;

            // File list inside the details column.
            var filesHdr = AddText(parent, "FILES", 12f, TextDim, TextAlignmentOptions.Left, FontStyles.Bold);
            var fhRt = filesHdr.rectTransform;
            fhRt.anchorMin = new Vector2(0f, 1f); fhRt.anchorMax = new Vector2(1f, 1f);
            fhRt.pivot = new Vector2(0f, 1f);
            fhRt.anchoredPosition = new Vector2(12f, -148f);
            fhRt.sizeDelta = new Vector2(-24f, 18f);

            var filesScroll = MakeRect("FilesScroll", parent, new Vector2(0f, 0f), new Vector2(1f, 1f));
            var fsRt = filesScroll.GetComponent<RectTransform>();
            fsRt.offsetMin = new Vector2(12f, 60f);   // leave room for action row
            fsRt.offsetMax = new Vector2(-12f, -170f);

            var viewport = MakeRect("Viewport", filesScroll.transform, Vector2.zero, Vector2.one);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot     = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;
            _detailFilesContent = contentRt;

            var v = contentGo.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(8, 8, 6, 6);
            v.spacing = 1f;
            v.childControlWidth = true; v.childForceExpandWidth = true;
            v.childControlHeight = true; v.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = filesScroll.AddComponent<ScrollRect>();
            scroll.viewport   = viewport.GetComponent<RectTransform>();
            scroll.content    = contentRt;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.scrollSensitivity = 24f;

            // Action row at the bottom of the right column.
            var actionRow = MakeRect("Actions", parent, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var arRt = actionRow.GetComponent<RectTransform>();
            arRt.pivot = new Vector2(0.5f, 0f);
            arRt.anchoredPosition = new Vector2(0f, 12f);
            arRt.sizeDelta = new Vector2(-24f, 36f);

            _restoreBtn = AddButton(actionRow.transform, "Restore", 140f, 36f, BtnNormal, BtnHover, OnRestoreClicked);
            var rRt = _restoreBtn.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(0f, 0.5f); rRt.anchorMax = new Vector2(0f, 0.5f);
            rRt.pivot = new Vector2(0f, 0.5f);
            rRt.anchoredPosition = new Vector2(12f, 0f);

            _deleteBtn = AddButton(actionRow.transform, "Delete…", 140f, 36f, BtnDanger, BtnDangerH, OnDeleteClicked);
            var dRt = _deleteBtn.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(1f, 0.5f); dRt.anchorMax = new Vector2(1f, 0.5f);
            dRt.pivot = new Vector2(1f, 0.5f);
            dRt.anchoredPosition = new Vector2(-12f, 0f);

            SetActionButtonsEnabled(false);
        }

        private void BuildStatusBar(Transform panel)
        {
            var bar = MakeRect("Status", panel, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var rt = bar.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 28f);
            bar.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 1f);

            _statusLine = AddText(bar.transform, "", 12f, TextDim, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            var sRt = _statusLine.rectTransform;
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(12f, 0f); sRt.offsetMax = new Vector2(-12f, 0f);
        }

        // ── List rendering ───────────────────────────────────────────────────────

        private void RefreshList()
        {
            _backups = _store.ListBackups();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (_backups.Count == 0)
            {
                var empty = AddText(_listContent, "(no backups yet — click Create above)",
                                    12f, TextDim, TextAlignmentOptions.Center, FontStyles.Italic);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
                ClearDetails();
                SetActionButtonsEnabled(false);
                return;
            }

            foreach (var m in _backups)
                AddBackupRow(m);

            if (string.IsNullOrEmpty(_selectedId) ||
                !_backups.Exists(b => string.Equals(b.id, _selectedId, StringComparison.OrdinalIgnoreCase)))
            {
                SelectBackup(_backups[0].id);
            }
            else
            {
                ShowDetails(_selectedId);
            }
        }

        private void AddBackupRow(MapBackupManifest m)
        {
            var rowGo = new GameObject($"Row_{m.id}", typeof(RectTransform));
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 56f;

            var img = rowGo.AddComponent<Image>();
            img.color = (string.Equals(m.id, _selectedId, StringComparison.OrdinalIgnoreCase))
                ? RowBgActive : RowBg;
            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = img.color;
            c.highlightedColor = RowBgHover;
            c.pressedColor     = RowBgActive;
            c.selectedColor    = img.color;
            btn.colors = c;
            btn.targetGraphic = img;
            string capturedId = m.id;
            btn.onClick.AddListener(() => SelectBackup(capturedId));

            // Slot + kind
            var top = AddText(rowGo.transform, $"{m.slot}  ·  {m.kind}",
                              13f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var topRt = top.rectTransform;
            topRt.anchorMin = new Vector2(0f, 1f); topRt.anchorMax = new Vector2(1f, 1f);
            topRt.pivot = new Vector2(0f, 1f);
            topRt.anchoredPosition = new Vector2(10f, -8f);
            topRt.sizeDelta = new Vector2(-20f, 18f);

            // Timestamp + size
            string when = DateTimeOffset.FromUnixTimeSeconds(m.createdUnixSeconds).LocalDateTime
                          .ToString("yyyy-MM-dd  HH:mm:ss");
            var sub = AddText(rowGo.transform,
                              $"{when}    {MapBackupStore.FormatBytes(m.totalBytes)}  ·  {m.fileCount} files",
                              11f, TextPrimary, TextAlignmentOptions.Left, FontStyles.Normal);
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0f); subRt.anchorMax = new Vector2(1f, 0f);
            subRt.pivot = new Vector2(0f, 0f);
            subRt.anchoredPosition = new Vector2(10f, 8f);
            subRt.sizeDelta = new Vector2(-20f, 16f);
        }

        private void SelectBackup(string id)
        {
            _selectedId = id;
            // Recolor existing rows without rebuilding the list.
            for (int i = 0; i < _listContent.childCount; i++)
            {
                var child = _listContent.GetChild(i);
                if (child == null) continue;
                var img = child.GetComponent<Image>();
                if (img == null) continue;
                bool sel = child.gameObject.name == $"Row_{id}";
                img.color = sel ? RowBgActive : RowBg;
            }
            ShowDetails(id);
        }

        // ── Details ──────────────────────────────────────────────────────────────

        private void ShowDetails(string id)
        {
            var m = _backups.Find(b => string.Equals(b.id, id, StringComparison.OrdinalIgnoreCase));
            if (m == null) { ClearDetails(); SetActionButtonsEnabled(false); return; }

            _detailHeader.text = m.id;
            string when = DateTimeOffset.FromUnixTimeSeconds(m.createdUnixSeconds).LocalDateTime
                          .ToString("yyyy-MM-dd HH:mm:ss");
            _detailBody.text =
                $"<b>Slot:</b> {m.slot}\n" +
                $"<b>Kind:</b> {m.kind}\n" +
                $"<b>Created:</b> {when}\n" +
                $"<b>Size:</b> {MapBackupStore.FormatBytes(m.totalBytes)}\n" +
                $"<b>Files:</b> {m.fileCount}\n" +
                $"<b>Label:</b> {m.label}";

            for (int i = _detailFilesContent.childCount - 1; i >= 0; i--)
                Destroy(_detailFilesContent.GetChild(i).gameObject);

            foreach (var rel in m.files)
            {
                var t = AddText(_detailFilesContent, rel, 11f, TextPrimary, TextAlignmentOptions.Left, FontStyles.Normal);
                t.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
            }
            SetActionButtonsEnabled(true);
        }

        private void ClearDetails()
        {
            if (_detailHeader != null) _detailHeader.text = "Select a backup";
            if (_detailBody != null)   _detailBody.text   = "";
            if (_detailFilesContent != null)
                for (int i = _detailFilesContent.childCount - 1; i >= 0; i--)
                    Destroy(_detailFilesContent.GetChild(i).gameObject);
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            if (_restoreBtn != null) _restoreBtn.interactable = enabled;
            if (_deleteBtn != null)  _deleteBtn.interactable  = enabled;
        }

        // ── Restore ──────────────────────────────────────────────────────────────

        private void OnRestoreClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            // Always snap a safety backup before overwriting on-disk content.
            _store.CreateSnapshot(GuessActiveSlot(), "Pre-restore safety snapshot",
                                  MapBackupSchema.KindAutoBeforeRestore);
            bool ok = _store.RestoreBackup(_selectedId);
            SetStatus(ok
                ? $"Restored '{_selectedId}'. Reload the Map Editor to see the changes."
                : $"Restore failed for '{_selectedId}' — see console.");
            RefreshList();
        }

        // ── Delete (three-stage + type-to-confirm) ───────────────────────────────

        private void OnDeleteClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            _delStage = 1;
            _delPrompt.text =
                $"<b>Stage 1 of 3.</b>\n\n" +
                $"You are about to delete the snapshot:\n" +
                $"<b>{_selectedId}</b>\n\n" +
                $"This action cannot be undone. Proceed?";
            _delConfirmLabel.text = "Continue";
            _delInput.gameObject.SetActive(false);
            _delConfirmBtn.interactable = true;
            _delDialog.SetActive(true);
        }

        private void OnDeleteConfirmClicked()
        {
            if (_delStage == 1)
            {
                _delStage = 2;
                _delPrompt.text =
                    $"<b>Stage 2 of 3.</b>\n\n" +
                    $"Snapshots are written to your local AppData and there is no\n" +
                    $"versioned history once they're gone. Are you absolutely sure?";
                _delConfirmLabel.text = "I understand — continue";
                _delConfirmBtn.interactable = true;
                return;
            }
            if (_delStage == 2)
            {
                _delStage = 3;
                _delPrompt.text =
                    $"<b>Stage 3 of 3.</b>\n\n" +
                    $"Type the phrase <b>{DESTROY_PHRASE}</b>\n" +
                    $"exactly (case-sensitive) to enable the destroy button.";
                _delConfirmLabel.text = "Destroy";
                _delConfirmBtn.interactable = false;
                _delInput.text = "";
                _delInput.gameObject.SetActive(true);
                EventSystem.current?.SetSelectedGameObject(_delInput.gameObject);
                return;
            }
            // Stage 3 → only fires when the input text matches exactly.
            if (_delInput.text != DESTROY_PHRASE) return;

            string id = _selectedId;
            bool ok = _store.DeleteBackup(id);
            CloseDeleteDialog();
            SetStatus(ok ? $"Destroyed snapshot '{id}'." : $"Delete failed for '{id}'.");
            _selectedId = null;
            RefreshList();
        }

        private void CloseDeleteDialog()
        {
            _delStage = 0;
            if (_delDialog != null) _delDialog.SetActive(false);
        }

        private void BuildDeleteDialog(Transform canvas)
        {
            _delDialog = MakeStretch("DeleteDialog", canvas);
            _delDialog.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            var box = MakeRect("Box", _delDialog.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.GetComponent<RectTransform>().sizeDelta = new Vector2(620f, 320f);
            box.AddComponent<Image>().color = PanelBg;
            var ol = box.AddComponent<Outline>();
            ol.effectColor = new Color(1f, 0.32f, 0.36f, 1f);
            ol.effectDistance = new Vector2(2f, 2f);

            var title = AddText(box.transform, "DELETE BACKUP", 18f, new Color(1f, 0.42f, 0.42f, 1f),
                                TextAlignmentOptions.Center, FontStyles.Bold);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -16f);
            titleRt.sizeDelta = new Vector2(-32f, 28f);

            _delPrompt = AddText(box.transform, "", 14f, TextPrimary, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            var promptRt = _delPrompt.rectTransform;
            promptRt.anchorMin = new Vector2(0f, 0f); promptRt.anchorMax = new Vector2(1f, 1f);
            promptRt.offsetMin = new Vector2(20f, 90f);
            promptRt.offsetMax = new Vector2(-20f, -56f);
            _delPrompt.enableWordWrapping = true;

            // Type-to-confirm input (only visible at stage 3)
            var inputGo = new GameObject("DestroyPhraseInput", typeof(RectTransform));
            inputGo.transform.SetParent(box.transform, false);
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0f, 0f); inputRt.anchorMax = new Vector2(1f, 0f);
            inputRt.pivot = new Vector2(0.5f, 0f);
            inputRt.anchoredPosition = new Vector2(0f, 60f);
            inputRt.sizeDelta = new Vector2(-32f, 32f);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.06f, 0.07f, 0.09f, 1f);

            _delInput = inputGo.AddComponent<TMP_InputField>();
            _delInput.targetGraphic = inputBg;
            _delInput.lineType = TMP_InputField.LineType.SingleLine;
            _delInput.characterLimit = 80;

            var textArea = MakeRect("TextArea", inputGo.transform, new Vector2(0f, 0f), new Vector2(1f, 1f));
            textArea.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 4f);
            textArea.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -4f);
            textArea.AddComponent<RectMask2D>();

            var inputText = AddText(textArea.transform, "", 14f, TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            var itRt = inputText.rectTransform;
            itRt.anchorMin = Vector2.zero; itRt.anchorMax = Vector2.one;
            itRt.offsetMin = Vector2.zero; itRt.offsetMax = Vector2.zero;

            var placeholder = AddText(textArea.transform, $"Type '{DESTROY_PHRASE}' here…", 13f,
                                      new Color(0.4f, 0.43f, 0.5f, 1f),
                                      TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            var phRt = placeholder.rectTransform;
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;

            _delInput.textViewport     = textArea.GetComponent<RectTransform>();
            _delInput.textComponent    = inputText;
            _delInput.placeholder      = placeholder;
            _delInput.onValueChanged.AddListener(s =>
            {
                if (_delStage == 3 && _delConfirmBtn != null)
                    _delConfirmBtn.interactable = (s == DESTROY_PHRASE);
            });

            // Buttons row
            var btnRow = MakeRect("Buttons", box.transform, new Vector2(0f, 0f), new Vector2(1f, 0f));
            var brRt = btnRow.GetComponent<RectTransform>();
            brRt.pivot = new Vector2(0.5f, 0f);
            brRt.anchoredPosition = new Vector2(0f, 14f);
            brRt.sizeDelta = new Vector2(-32f, 36f);

            var cancelBtn = AddButton(btnRow.transform, "Cancel", 140f, 36f, BtnNormal, BtnHover, CloseDeleteDialog);
            var cnRt = cancelBtn.GetComponent<RectTransform>();
            cnRt.anchorMin = new Vector2(0f, 0.5f); cnRt.anchorMax = new Vector2(0f, 0.5f);
            cnRt.pivot = new Vector2(0f, 0.5f);
            cnRt.anchoredPosition = new Vector2(0f, 0f);

            _delConfirmBtn = AddButton(btnRow.transform, "Continue", 220f, 36f, BtnDanger, BtnDangerH, OnDeleteConfirmClicked);
            _delConfirmLabel = _delConfirmBtn.GetComponentInChildren<TextMeshProUGUI>();
            var ccRt = _delConfirmBtn.GetComponent<RectTransform>();
            ccRt.anchorMin = new Vector2(1f, 0.5f); ccRt.anchorMax = new Vector2(1f, 0.5f);
            ccRt.pivot = new Vector2(1f, 0.5f);
            ccRt.anchoredPosition = new Vector2(0f, 0f);

            _delDialog.SetActive(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

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

        // ── Tiny UI factory helpers ──────────────────────────────────────────────

        private static GameObject MakeStretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size,
            Color color, TextAlignmentOptions align, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            return t;
        }

        private static Button AddButton(Transform parent, string label, float w, float h,
            Color normal, Color hover, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = normal;
            var btn = go.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor = normal;
            c.highlightedColor = hover;
            c.pressedColor = hover;
            c.disabledColor = new Color(0.18f, 0.20f, 0.24f, 1f);
            btn.colors = c;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var labelTmp = AddText(go.transform, label, 13f, TextPrimary,
                                   TextAlignmentOptions.Center, FontStyles.Bold);
            var lRt = labelTmp.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
