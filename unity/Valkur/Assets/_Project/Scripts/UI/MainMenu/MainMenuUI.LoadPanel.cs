using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;
using Valkur.UI.Loading;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Load game state ───────────────────────────────────────────────────────
        private GameObject _mmLoadOverlay;

        // Two-level data: Runs (left column) → Saves of selected run (right column)
        private List<RunGroupInfo>  _mmLoadRuns      = new List<RunGroupInfo>();
        private int  _mmLoadRunSel    = 0;
        private int  _mmLoadRunScroll = 0;
        private int  _mmLoadSaveSel   = 0;

        private const int MM_RUN_ROWS  = 7;
        private const int MM_SAVE_ROWS = 5;

        // Run list widgets (left column)
        private Image[]            _mmRunPills;
        private Image[]            _mmRunBars;
        private TextMeshProUGUI[]  _mmRunTexts;
        private RawImage[]         _mmRunFaceImages;
        private Image[][]          _mmRunHoverBorders;
        private int                _mmRunHover = -1;

        // Save list widgets (right column, top)
        private Image[]            _mmSavePills;
        private Image[]            _mmSaveBars;
        private TextMeshProUGUI[]  _mmSaveTexts;
        private Image[][]          _mmSaveHoverBorders;
        private int                _mmSaveHover = -1;

        private TextMeshProUGUI    _mmLoadDetailText;
        private TextMeshProUGUI    _mmLoadTargetLabel;

        // ── Sub-modes (rename / delete confirm) ──────────────────────────────────
        private enum LoadPanelMode { List, Rename, ConfirmDelete }
        private LoadPanelMode _mmLoadMode = LoadPanelMode.List;

        // Rename overlay
        private GameObject       _mmRenameOverlay;
        private TMP_InputField   _mmRenameInput;
        private TextMeshProUGUI  _mmRenameError;

        // Confirm-delete overlay
        private GameObject       _mmConfirmOverlay;
        private TextMeshProUGUI  _mmConfirmText;
        private int              _mmConfirmSel;
        private Image[]          _mmConfirmPills;
        private TextMeshProUGUI[] _mmConfirmTexts;

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Returns true and fills save when a save is selected.</summary>
        private bool TryGetSelectedSave(out SaveSlotInfo save)
        {
            save = default;
            if (_mmLoadRunSel < 0 || _mmLoadRunSel >= _mmLoadRuns.Count) return false;
            var run = _mmLoadRuns[_mmLoadRunSel];
            if (_mmLoadSaveSel < 0 || _mmLoadSaveSel >= run.saves.Count) return false;
            save = run.saves[_mmLoadSaveSel];
            return true;
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        private void BuildLoadGameSubmenu(Transform canvas)
        {
            _mmLoadOverlay = CreateUIObject("LoadOverlay", canvas);
            StretchFull(_mmLoadOverlay);
            _mmLoadOverlay.AddComponent<Image>().color = OverlayColor;

            const float panelW = 700f;
            const float panelH = 480f;
            const float splitX = 0.41f;

            var panel = CreateUIObject("LoadPanel", _mmLoadOverlay.transform);
            var pr = panel.GetComponent<RectTransform>();
            // Anchored below the ROGUELIKE 1.0 logo (logo bottom = -260 from canvas top).
            pr.anchorMin = new Vector2(0.5f, 1f); pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 1f); pr.anchoredPosition = new Vector2(0f, -280f);
            pr.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            var titleGo = CreateUIObject("LoadTitle", panel.transform);
            var tR = titleGo.GetComponent<RectTransform>();
            tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
            tR.pivot = new Vector2(0.5f, 1f);
            tR.anchoredPosition = new Vector2(0f, -8f);
            tR.sizeDelta = new Vector2(0f, 40f);
            var titleTMP = titleGo.AddComponent<TextMeshProUGUI>();
            titleTMP.text = "Load Game"; titleTMP.fontSize = 28f;
            titleTMP.alignment = TextAlignmentOptions.Center;
            titleTMP.color = AccentGold; titleTMP.fontStyle = FontStyles.Bold;

            // Column separator
            var sep = CreateUIObject("ColSep", panel.transform);
            var sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(splitX + 0.005f, 0.10f);
            sepRt.anchorMax = new Vector2(splitX + 0.005f, 0.90f);
            sepRt.pivot = new Vector2(0.5f, 0.5f); sepRt.sizeDelta = new Vector2(1f, 0f);
            sep.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            BuildMMColHeader("RUNS",  panel.transform, 0.01f, splitX);
            BuildMMColHeader("SAVES", panel.transform, splitX + 0.02f, 0.98f);

            BuildRunListRows(panel.transform, splitX);
            BuildSaveListRows(panel.transform, splitX);
            BuildDetailPanel(panel.transform, splitX);

            // Target label (just above action buttons)
            var targetGo = CreateUIObject("MMTargetLabel", panel.transform);
            var targetRt = targetGo.GetComponent<RectTransform>();
            targetRt.anchorMin = new Vector2(splitX + 0.02f, 0f);
            targetRt.anchorMax = new Vector2(0.98f,          0f);
            targetRt.pivot = new Vector2(0.5f, 0f);
            targetRt.anchoredPosition = new Vector2(0f, 76f);
            targetRt.sizeDelta = new Vector2(0f, 22f);
            _mmLoadTargetLabel = targetGo.AddComponent<TextMeshProUGUI>();
            _mmLoadTargetLabel.fontSize = 13f;
            _mmLoadTargetLabel.alignment = TextAlignmentOptions.Center;
            _mmLoadTargetLabel.color = AccentGold;
            _mmLoadTargetLabel.text = "";
            _mmLoadTargetLabel.raycastTarget = false;

            // Action buttons (bottom of right column)
            float bL = splitX + 0.02f;
            float bW = (0.96f - splitX) / 3f;
            AddMMLoadButton(panel.transform, "Load",
                new Vector2(bL,              0f), new Vector2(bL + bW,          0f),
                new Color(0.24f, 0.47f, 0.2f, 1f), MMLoadSelectedSave);
            AddMMLoadButton(panel.transform, "Rename",
                new Vector2(bL + bW + 0.01f, 0f), new Vector2(bL + bW * 2f + 0.01f, 0f),
                new Color(0.30f, 0.40f, 0.55f, 1f), BeginRenameSelectedSave);
            AddMMLoadButton(panel.transform, "Delete",
                new Vector2(bL + bW * 2f + 0.02f, 0f), new Vector2(0.97f, 0f),
                new Color(0.47f, 0.2f, 0.2f, 1f), RequestDeleteSelectedSave);

            BuildRenameOverlay(_mmLoadOverlay.transform);
            BuildDeleteConfirmOverlay(_mmLoadOverlay.transform);

            _mmLoadOverlay.SetActive(false);
        }

        private void BuildMMColHeader(string label, Transform parent, float anchorL, float anchorR)
        {
            var go = CreateUIObject($"ColHdr_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            // Lowered from [0.88, 0.94] to [0.82, 0.87] so the title has clear airspace above.
            rt.anchorMin = new Vector2(anchorL, 0.82f); rt.anchorMax = new Vector2(anchorR, 0.87f);
            rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 12f; tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left; tmp.color = VersionCol;
            tmp.raycastTarget = false;
        }

        private void AddMMLoadButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject($"MMLoadBtn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorMin.x, 0f);
            rt.anchorMax = new Vector2(anchorMax.x, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 36f);
            rt.sizeDelta = new Vector2(0f, 32f);
            var img = go.AddComponent<Image>(); img.color = bg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);

            var txtGo = CreateUIObject("Label", go.transform);
            var txtR = txtGo.GetComponent<RectTransform>();
            txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
            txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold; tmp.raycastTarget = false;
        }

        /// <summary>
        /// Generic overlay button placed by absolute pivot/anchor inside an overlay panel.
        /// Used for the Rename overlay's Cancel/OK pair so every action is reachable with the mouse.
        /// </summary>
        private void BuildOverlayButton(Transform parent, string label,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject($"OverlayBtn_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>(); img.color = bg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);

            var lblGo = CreateUIObject("Label", go.transform);
            var lblR  = lblGo.GetComponent<RectTransform>();
            lblR.anchorMin = Vector2.zero; lblR.anchorMax = Vector2.one;
            lblR.sizeDelta = Vector2.zero; lblR.anchoredPosition = Vector2.zero;
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white; tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
        }
    }
}
