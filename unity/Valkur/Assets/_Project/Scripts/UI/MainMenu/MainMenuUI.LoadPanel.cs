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
        // â”€â”€ Load game state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private GameObject _mmLoadOverlay;

        // Two-level data: Runs (left column) â†’ Saves of selected run (right column)
        private List<RunGroupInfo>  _mmLoadRuns      = new List<RunGroupInfo>();
        private int  _mmLoadRunSel    = 0;   // selected run index
        private int  _mmLoadRunScroll = 0;   // scroll offset for run list
        private int  _mmLoadSaveSel   = 0;   // selected save index within the run

        private const int MM_RUN_ROWS  = 7;  // visible run rows in left column
        private const int MM_SAVE_ROWS = 5;  // visible save rows in right column

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

        // â”€â”€ Sub-modes (rename / delete confirm) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private enum LoadPanelMode { List, Rename, ConfirmDelete }
        private LoadPanelMode _mmLoadMode = LoadPanelMode.List;

        // Rename overlay
        private GameObject       _mmRenameOverlay;
        private TMP_InputField   _mmRenameInput;
        private TextMeshProUGUI  _mmRenameError;

        // Confirm-delete overlay
        private GameObject       _mmConfirmOverlay;
        private TextMeshProUGUI  _mmConfirmText;
        private int              _mmConfirmSel; // 0 = Cancelar, 1 = Borrar
        private Image[]          _mmConfirmPills;
        private TextMeshProUGUI[] _mmConfirmTexts;

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Returns true and fills <paramref name="save"/> when a save is selected.</summary>
        private bool TryGetSelectedSave(out SaveSlotInfo save)
        {
            save = default;
            if (_mmLoadRunSel < 0 || _mmLoadRunSel >= _mmLoadRuns.Count) return false;
            var run = _mmLoadRuns[_mmLoadRunSel];
            if (_mmLoadSaveSel < 0 || _mmLoadSaveSel >= run.saves.Count) return false;
            save = run.saves[_mmLoadSaveSel];
            return true;
        }

        // â”€â”€ Build â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void BuildLoadGameSubmenu(Transform canvas)
        {
            _mmLoadOverlay = CreateUIObject("LoadOverlay", canvas);
            StretchFull(_mmLoadOverlay);
            _mmLoadOverlay.AddComponent<Image>().color = OverlayColor;

            const float panelW = 700f;
            const float panelH = 480f;
            const float splitX = 0.41f; // right edge of left (run) column

            var panel = CreateUIObject("LoadPanel", _mmLoadOverlay.transform);
            var pr = panel.GetComponent<RectTransform>();
            // Anchored below the ROGUELIKE 1.0 logo (logo bottom = -260 from canvas top).
            pr.anchorMin = new Vector2(0.5f, 1f); pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 1f); pr.anchoredPosition = new Vector2(0f, -280f);
            pr.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            // Title — anchored hard to the top of the panel; matches the
            // column-header band below so they never collide visually.
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

            // Column headers
            BuildMMColHeader("RUNS", panel.transform, 0.01f, splitX);
            BuildMMColHeader("SAVES",    panel.transform, splitX + 0.02f, 0.98f);

            // â”€â”€ LEFT: run list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var runList = CreateUIObject("MMRunList", panel.transform);
            var rlR = runList.GetComponent<RectTransform>();
            rlR.anchorMin = new Vector2(0.01f, 0.12f); rlR.anchorMax = new Vector2(splitX, 0.81f);
            rlR.pivot = new Vector2(0f, 1f); rlR.sizeDelta = Vector2.zero;
            rlR.anchoredPosition = Vector2.zero;

            _mmRunPills        = new Image[MM_RUN_ROWS];
            _mmRunBars         = new Image[MM_RUN_ROWS];
            _mmRunTexts        = new TextMeshProUGUI[MM_RUN_ROWS];
            _mmRunFaceImages   = new RawImage[MM_RUN_ROWS];
            _mmRunHoverBorders = new Image[MM_RUN_ROWS][];

            float runRowH = 37f, runGap = 3f;
            for (int i = 0; i < MM_RUN_ROWS; i++)
            {
                float cy = -i * (runRowH + runGap);

                var pillGo = CreateUIObject($"RnPill_{i}", runList.transform);
                var pRt = pillGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
                pRt.pivot = new Vector2(0.5f, 1f);
                pRt.anchoredPosition = new Vector2(0f, cy); pRt.sizeDelta = new Vector2(0f, runRowH);
                _mmRunPills[i] = pillGo.AddComponent<Image>(); _mmRunPills[i].color = Color.clear;

                var barGo = CreateUIObject($"RnBar_{i}", runList.transform);
                var bRt = barGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(0f, 1f);
                bRt.pivot = new Vector2(0f, 1f);
                bRt.anchoredPosition = new Vector2(0f, cy); bRt.sizeDelta = new Vector2(4f, runRowH);
                _mmRunBars[i] = barGo.AddComponent<Image>(); _mmRunBars[i].color = Color.clear;

                // Character face thumbnail (crops portrait to face area via uvRect)
                float faceSize = runRowH - 4f;
                var faceGo = CreateUIObject($"RnFace_{i}", runList.transform);
                var faceRt = faceGo.GetComponent<RectTransform>();
                faceRt.anchorMin = new Vector2(0f, 1f); faceRt.anchorMax = new Vector2(0f, 1f);
                faceRt.pivot = new Vector2(0f, 1f);
                faceRt.anchoredPosition = new Vector2(6f, cy - 2f);
                faceRt.sizeDelta = new Vector2(faceSize, faceSize);
                _mmRunFaceImages[i] = faceGo.AddComponent<RawImage>();
                _mmRunFaceImages[i].color = Color.clear;

                var txtGo = CreateUIObject($"RnTxt_{i}", runList.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(46f, cy); txtR.sizeDelta = new Vector2(-46f, runRowH);
                _mmRunTexts[i] = txtGo.AddComponent<TextMeshProUGUI>();
                _mmRunTexts[i].text = ""; _mmRunTexts[i].fontSize = 12f;
                _mmRunTexts[i].alignment = TextAlignmentOptions.Left; _mmRunTexts[i].color = TextNormal;
                _mmRunTexts[i].enableWordWrapping = false;

                var runHitGo = CreateUIObject($"RnHit_{i}", runList.transform);
                var runHitRt = runHitGo.GetComponent<RectTransform>();
                runHitRt.anchorMin = new Vector2(0f, 1f); runHitRt.anchorMax = new Vector2(1f, 1f);
                runHitRt.pivot = new Vector2(0.5f, 1f);
                runHitRt.anchoredPosition = new Vector2(0f, cy); runHitRt.sizeDelta = new Vector2(0f, runRowH);
                var runHitImg = runHitGo.AddComponent<Image>(); runHitImg.color = Color.clear;
                var runBtn = runHitGo.AddComponent<Button>(); runBtn.targetGraphic = runHitImg;
                int rCap = i;
                runBtn.onClick.AddListener(() =>
                {
                    int idx = _mmLoadRunScroll + rCap;
                    if (idx < 0 || idx >= _mmLoadRuns.Count) return;
                    _mmLoadRunSel = idx; _mmLoadSaveSel = 0; UpdateMMLoadVisuals();
                });
                var runTrig = runHitGo.AddComponent<EventTrigger>();
                var rEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                rEnter.callback.AddListener(_ =>
                {
                    int di = _mmLoadRunScroll + rCap;
                    _mmRunHover = (di >= 0 && di < _mmLoadRuns.Count) ? rCap : -1;
                    UpdateMMLoadHoverBorders();
                });
                runTrig.triggers.Add(rEnter);
                var rExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                rExit.callback.AddListener(_ => { _mmRunHover = -1; UpdateMMLoadHoverBorders(); });
                runTrig.triggers.Add(rExit);

                _mmRunHoverBorders[i] = BuildHoverBorderStrips(runList.transform, cy, runRowH);
            }

            // â”€â”€ RIGHT TOP: save list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var saveList = CreateUIObject("MMSaveList", panel.transform);
            var svR = saveList.GetComponent<RectTransform>();
            svR.anchorMin = new Vector2(splitX + 0.02f, 0.51f); svR.anchorMax = new Vector2(0.98f, 0.81f);
            svR.pivot = new Vector2(0f, 1f); svR.sizeDelta = Vector2.zero;
            svR.anchoredPosition = Vector2.zero;

            _mmSavePills        = new Image[MM_SAVE_ROWS];
            _mmSaveBars         = new Image[MM_SAVE_ROWS];
            _mmSaveTexts        = new TextMeshProUGUI[MM_SAVE_ROWS];
            _mmSaveHoverBorders = new Image[MM_SAVE_ROWS][];

            float svRowH = 31f, svGap = 3f;
            for (int i = 0; i < MM_SAVE_ROWS; i++)
            {
                float cy = -i * (svRowH + svGap);

                var pillGo = CreateUIObject($"SvPill_{i}", saveList.transform);
                var pRt = pillGo.GetComponent<RectTransform>();
                pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
                pRt.pivot = new Vector2(0.5f, 1f);
                pRt.anchoredPosition = new Vector2(0f, cy); pRt.sizeDelta = new Vector2(0f, svRowH);
                _mmSavePills[i] = pillGo.AddComponent<Image>(); _mmSavePills[i].color = Color.clear;

                var barGo = CreateUIObject($"SvBar_{i}", saveList.transform);
                var bRt = barGo.GetComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(0f, 1f);
                bRt.pivot = new Vector2(0f, 1f);
                bRt.anchoredPosition = new Vector2(0f, cy); bRt.sizeDelta = new Vector2(4f, svRowH);
                _mmSaveBars[i] = barGo.AddComponent<Image>(); _mmSaveBars[i].color = Color.clear;

                var txtGo = CreateUIObject($"SvTxt_{i}", saveList.transform);
                var txtR = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = new Vector2(0f, 1f); txtR.anchorMax = new Vector2(1f, 1f);
                txtR.pivot = new Vector2(0f, 1f);
                txtR.anchoredPosition = new Vector2(12f, cy); txtR.sizeDelta = new Vector2(-12f, svRowH);
                _mmSaveTexts[i] = txtGo.AddComponent<TextMeshProUGUI>();
                _mmSaveTexts[i].text = ""; _mmSaveTexts[i].fontSize = 14f;
                _mmSaveTexts[i].alignment = TextAlignmentOptions.Left; _mmSaveTexts[i].color = TextNormal;
                _mmSaveTexts[i].enableWordWrapping = false;

                var svHitGo = CreateUIObject($"SvHit_{i}", saveList.transform);
                var svHitRt = svHitGo.GetComponent<RectTransform>();
                svHitRt.anchorMin = new Vector2(0f, 1f); svHitRt.anchorMax = new Vector2(1f, 1f);
                svHitRt.pivot = new Vector2(0.5f, 1f);
                svHitRt.anchoredPosition = new Vector2(0f, cy); svHitRt.sizeDelta = new Vector2(0f, svRowH);
                var svHitImg = svHitGo.AddComponent<Image>(); svHitImg.color = Color.clear;
                var svBtn = svHitGo.AddComponent<Button>(); svBtn.targetGraphic = svHitImg;
                int sCap = i;
                svBtn.onClick.AddListener(() =>
                {
                    if (_mmLoadRunSel < 0 || _mmLoadRunSel >= _mmLoadRuns.Count) return;
                    if (sCap >= _mmLoadRuns[_mmLoadRunSel].saves.Count) return;
                    _mmLoadSaveSel = sCap; UpdateMMLoadVisuals();
                });
                var svTrig = svHitGo.AddComponent<EventTrigger>();
                var sEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                sEnter.callback.AddListener(_ =>
                {
                    int saveCount = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                        ? _mmLoadRuns[_mmLoadRunSel].saves.Count : 0;
                    _mmSaveHover = (sCap < saveCount) ? sCap : -1;
                    UpdateMMLoadHoverBorders();
                });
                svTrig.triggers.Add(sEnter);
                var sExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                sExit.callback.AddListener(_ => { _mmSaveHover = -1; UpdateMMLoadHoverBorders(); });
                svTrig.triggers.Add(sExit);

                _mmSaveHoverBorders[i] = BuildHoverBorderStrips(saveList.transform, cy, svRowH);
            }

            // â”€â”€ RIGHT BOTTOM: detail panel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var detC = CreateUIObject("MMSaveDetails", panel.transform);
            var dcR = detC.GetComponent<RectTransform>();
            dcR.anchorMin = new Vector2(splitX + 0.02f, 0.11f); dcR.anchorMax = new Vector2(0.98f, 0.49f);
            dcR.pivot = new Vector2(0f, 1f); dcR.sizeDelta = Vector2.zero;
            dcR.anchoredPosition = Vector2.zero;

            var detGo = CreateUIObject("MMDetailText", detC.transform);
            var detRt = detGo.GetComponent<RectTransform>();
            detRt.anchorMin = Vector2.zero; detRt.anchorMax = Vector2.one;
            detRt.sizeDelta = Vector2.zero; detRt.anchoredPosition = Vector2.zero;
            _mmLoadDetailText = detGo.AddComponent<TextMeshProUGUI>();
            _mmLoadDetailText.fontSize = 14f;
            _mmLoadDetailText.alignment = TextAlignmentOptions.TopLeft;
            _mmLoadDetailText.color = TextNormal;
            _mmLoadDetailText.text = "Select a save.";

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
            // Sits below the title band (which spans roughly y ∈ [0.92, 1.00]).
            // Lowering the headers from [0.88, 0.94] to [0.82, 0.87] gives the
            // title clear airspace at the top — they used to overlap visually.
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
        /// Generic overlay button placed by absolute pivot/anchor inside an
        /// overlay panel. Used for the Rename overlay's Cancelar/Aceptar pair so
        /// every action is reachable with the mouse (keyboard parity unchanged).
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

        // â”€â”€ Input â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void HandleMMLoadInput()
        {
            switch (_mmLoadMode)
            {
                case LoadPanelMode.Rename:        HandleRenameInput();        return;
                case LoadPanelMode.ConfirmDelete: HandleConfirmDeleteInput(); return;
            }

            // OR new-InputSystem actions with legacy fallback (InputCompat) so the
            // panel still navigates when the new pipeline drops OS event delivery.
            if (InputCompat.CancelPressed() || Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); return; }

            if (_mmLoadRuns.Count == 0) return;

            // W/S navigate runs (left column)
            if (InputCompat.NavUpPressed() || Valkur.Core.Input.InputCompat.NavUpPressed())
            {
                _mmLoadRunSel = Mathf.Max(0, _mmLoadRunSel - 1);
                _mmLoadSaveSel = 0;
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            else if (InputCompat.NavDownPressed() || Valkur.Core.Input.InputCompat.NavDownPressed())
            {
                _mmLoadRunSel = Mathf.Min(_mmLoadRuns.Count - 1, _mmLoadRunSel + 1);
                _mmLoadSaveSel = 0;
                EnsureMMLoadScroll();
                UpdateMMLoadVisuals();
            }
            // A/D navigate saves within selected run (right column)
            else if (InputCompat.NavLeftPressed() || Valkur.Core.Input.InputCompat.NavLeftPressed())
            {
                if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                {
                    int saves = _mmLoadRuns[_mmLoadRunSel].saves.Count;
                    if (saves > 0) { _mmLoadSaveSel = Mathf.Max(0, _mmLoadSaveSel - 1); UpdateMMLoadVisuals(); }
                }
            }
            else if (InputCompat.NavRightPressed() || Valkur.Core.Input.InputCompat.NavRightPressed())
            {
                if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                {
                    int saves = _mmLoadRuns[_mmLoadRunSel].saves.Count;
                    if (saves > 0) { _mmLoadSaveSel = Mathf.Min(saves - 1, _mmLoadSaveSel + 1); UpdateMMLoadVisuals(); }
                }
            }
            else if (InputCompat.ConfirmPressed() || Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                MMLoadSelectedSave();
            }
            else if (Valkur.Core.Input.KeyboardInputManager.WasDeletePressedThisFrame())
            {
                RequestDeleteSelectedSave();
            }
            else if (Valkur.Core.Input.KeyboardInputManager.WasF2PressedThisFrame())
            {
                BeginRenameSelectedSave();
            }
        }

        private void EnsureMMLoadScroll()
        {
            if (_mmLoadRunSel < _mmLoadRunScroll) _mmLoadRunScroll = _mmLoadRunSel;
            if (_mmLoadRunSel >= _mmLoadRunScroll + MM_RUN_ROWS)
                _mmLoadRunScroll = _mmLoadRunSel - MM_RUN_ROWS + 1;
        }

        // â”€â”€ Data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RefreshMMLoadPanel()
        {
            // Try to preserve the previously selected save by file path.
            string prevSavePath = null;
            if (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
            {
                var pr = _mmLoadRuns[_mmLoadRunSel];
                if (_mmLoadSaveSel >= 0 && _mmLoadSaveSel < pr.saves.Count)
                    prevSavePath = pr.saves[_mmLoadSaveSel].path;
            }

            _mmLoadRuns     = SaveFileManager.ListSavesByRun();
            _mmLoadRunSel   = 0;
            _mmLoadSaveSel  = 0;
            _mmLoadRunScroll = 0;

            // Try to restore previously selected save
            if (!string.IsNullOrEmpty(prevSavePath))
            {
                for (int ri = 0; ri < _mmLoadRuns.Count; ri++)
                {
                    var grp = _mmLoadRuns[ri];
                    for (int si = 0; si < grp.saves.Count; si++)
                    {
                        if (string.Equals(grp.saves[si].path, prevSavePath,
                                          System.StringComparison.OrdinalIgnoreCase))
                        { _mmLoadRunSel = ri; _mmLoadSaveSel = si; break; }
                    }
                }
            }

            EnsureMMLoadScroll();
            SetLoadMode(LoadPanelMode.List);
            UpdateMMLoadVisuals();
        }

        private void UpdateMMLoadVisuals()
        {
            if (_mmRunPills == null) return;

            // â”€â”€ Left column: run list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            for (int i = 0; i < MM_RUN_ROWS; i++)
            {
                int dataIdx = _mmLoadRunScroll + i;
                bool hasRun  = dataIdx < _mmLoadRuns.Count;
                bool selRun  = dataIdx == _mmLoadRunSel;

                _mmRunPills[i].color = selRun && hasRun ? PillColor  : Color.clear;
                _mmRunBars[i].color  = selRun && hasRun ? AccentGold : Color.clear;
                _mmRunTexts[i].color = selRun && hasRun ? TextSelected : TextNormal;

                if (hasRun)
                {
                    var run = _mmLoadRuns[dataIdx];
                    if (run.isLegacy)
                    {
                        if (_mmRunFaceImages?[i] != null) _mmRunFaceImages[i].color = Color.clear;
                        _mmRunTexts[i].text = "<color=#808080>Legacy</color>";
                    }
                    else
                    {
                        if (_mmRunFaceImages?[i] != null)
                        {
                            var tex = GetCachedPortraitTexture(run.playerClass);
                            _mmRunFaceImages[i].texture = tex;
                            _mmRunFaceImages[i].uvRect  = GetFaceUvRect(run.playerClass);
                            _mmRunFaceImages[i].color   = tex != null ? Color.white : Color.clear;
                        }
                        _mmRunTexts[i].text = $"<color=#808080>Lv.{run.maxLevel}</color>";
                    }
                }
                else
                {
                    if (_mmRunFaceImages?[i] != null) _mmRunFaceImages[i].color = Color.clear;
                    _mmRunTexts[i].text = "";
                }
            }

            // â”€â”€ Right column: save list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var currentRun = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                ? _mmLoadRuns[_mmLoadRunSel] : null;

            for (int i = 0; i < MM_SAVE_ROWS; i++)
            {
                bool hasSave = currentRun != null && i < currentRun.saves.Count;
                bool selSave = i == _mmLoadSaveSel;

                _mmSavePills[i].color = selSave && hasSave ? PillColor  : Color.clear;
                _mmSaveBars[i].color  = selSave && hasSave ? AccentGold : Color.clear;
                _mmSaveTexts[i].color = selSave && hasSave ? TextSelected : TextNormal;

                if (hasSave)
                {
                    var sv = currentRun.saves[i];
                    string display = sv.isAutoSave
                        ? $"<b><color=#FFC800>{Valkur.Gameplay.Save.SaveFileManager.AUTOSAVE_DISPLAY}</color></b>"
                        : sv.fileName;
                    _mmSaveTexts[i].text = sv.isCorrupted
                        ? $"<color=#FF6666>[Corrupted]</color> {display}"
                        : $"{display}  <color=#808080><size=12>{sv.timestamp}</size></color>";
                }
                else _mmSaveTexts[i].text = "";
            }

            // â”€â”€ Target label â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_mmLoadTargetLabel != null)
            {
                if (TryGetSelectedSave(out var tsv))
                {
                    string label = tsv.isAutoSave ? Valkur.Gameplay.Save.SaveFileManager.AUTOSAVE_DISPLAY : tsv.fileName;
                    _mmLoadTargetLabel.text = $"Will operate on: <b>{label}</b>";
                }
                else
                    _mmLoadTargetLabel.text = "";
            }

            // â”€â”€ Detail panel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_mmLoadDetailText != null)
            {
                if (_mmLoadRuns.Count == 0)
                {
                    _mmLoadDetailText.text = "No saved games.";
                }
                else if (TryGetSelectedSave(out var info))
                {
                    if (info.isCorrupted)
                    {
                        _mmLoadDetailText.text =
                            "<color=#FF6666><b>Corrupted save</b></color>\n\n" +
                            $"<color=#FFC800>File:</color> {info.fileName}\n\n" +
                            "This save cannot be loaded.\n" +
                            "You can delete it with <b>Del</b>.";
                    }
                    else
                    {
                        string cls  = FormatClassName(info.playerClass);
                        string zone = string.IsNullOrEmpty(info.currentZone) ? "â€”" : info.currentZone;
                        string hp   = info.maxHp > 0 ? $"{info.hp}/{info.maxHp}" : "â€”";
                        _mmLoadDetailText.text =
                            $"<color=#FFC800>Class:</color> {cls}\n" +
                            $"<color=#FFC800>Zone:</color>  {zone}\n\n" +
                            $"<color=#FFC800>Level:</color> {info.level}     " +
                            $"<color=#FFC800>XP:</color>  {info.experience}\n" +
                            $"<color=#FFC800>HP:</color>    {hp}\n\n" +
                            $"<color=#FFC800>Saved:</color> {info.timestamp}\n\n" +
                            $"<color=#808080><size=12>{info.fileName}</size></color>";
                    }
                }
                else
                {
                    _mmLoadDetailText.text = "Select a save.";
                }
            }

            UpdateMMLoadHoverBorders();
        }

        // â”€â”€ Hover border helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly Color HoverBorderColor = new Color(1f, 0.84f, 0f, 0.85f);

        /// <summary>Creates 4 thin strip Images around a row rect to form an outline.</summary>
        private Image[] BuildHoverBorderStrips(Transform parent, float cy, float rowH)
        {
            const float T = 2f; // border thickness in pixels
            var strips = new Image[4];
            // top
            strips[0] = MakeBorderStrip($"BT", parent, new Vector2(0f,1f), new Vector2(1f,1f),
                new Vector2(0.5f,1f), new Vector2(0f, cy),      new Vector2(0f, T));
            // bottom
            strips[1] = MakeBorderStrip($"BB", parent, new Vector2(0f,1f), new Vector2(1f,1f),
                new Vector2(0.5f,1f), new Vector2(0f, cy-rowH+T), new Vector2(0f, T));
            // left
            strips[2] = MakeBorderStrip($"BL", parent, new Vector2(0f,1f), new Vector2(0f,1f),
                new Vector2(0f,1f),   new Vector2(0f, cy),      new Vector2(T, rowH));
            // right
            strips[3] = MakeBorderStrip($"BR", parent, new Vector2(1f,1f), new Vector2(1f,1f),
                new Vector2(1f,1f),   new Vector2(0f, cy),      new Vector2(T, rowH));
            return strips;
        }

        private Image MakeBorderStrip(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = pivot; rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = false;
            return img;
        }

        private void UpdateMMLoadHoverBorders()
        {
            // Run column borders
            if (_mmRunHoverBorders != null)
            {
                for (int i = 0; i < MM_RUN_ROWS; i++)
                {
                    var strips = _mmRunHoverBorders[i];
                    if (strips == null) continue;
                    int dataIdx = _mmLoadRunScroll + i;
                    bool isSel = dataIdx == _mmLoadRunSel && dataIdx < _mmLoadRuns.Count;
                    Color c = (i == _mmRunHover && !isSel) ? HoverBorderColor : Color.clear;
                    foreach (var img in strips) if (img != null) img.color = c;
                }
            }

            // Save column borders
            if (_mmSaveHoverBorders != null)
            {
                var cr = (_mmLoadRunSel >= 0 && _mmLoadRunSel < _mmLoadRuns.Count)
                    ? _mmLoadRuns[_mmLoadRunSel] : null;
                for (int i = 0; i < MM_SAVE_ROWS; i++)
                {
                    var strips = _mmSaveHoverBorders[i];
                    if (strips == null) continue;
                    bool hasSave = cr != null && i < cr.saves.Count;
                    bool isSel = i == _mmLoadSaveSel && hasSave;
                    Color c = (i == _mmSaveHover && !isSel) ? HoverBorderColor : Color.clear;
                    foreach (var img in strips) if (img != null) img.color = c;
                }
            }
        }

        // â”€â”€ Actions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void MMLoadSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (info.isCorrupted)
            {
                Debug.LogWarning($"[MainMenu] Cannot load corrupted save: {info.fileName}");
                return;
            }
            Debug.Log($"[MainMenu] Loading save: {info.path}");
            PendingSaveLoad.Path        = info.path;
            PendingSaveLoad.PlayerClass = info.playerClass;
            TransitionAudioToGame();
            LoadingScreenController.Show(gameplaySceneName);
        }

        private void MMDeleteSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            Debug.Log($"[MainMenu] Deleting save: {info.path}");
            SaveFileManager.DeleteSave(info.path);
            RefreshMMLoadPanel();
            // Rebuild main menu so "Continuar" disappears when no saves remain
            RebuildMenuPanel();
        }

        // â”€â”€ Rename flow â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void BeginRenameSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (info.isCorrupted)
            {
                Debug.LogWarning("[MainMenu] Cannot rename corrupted save.");
                return;
            }
            if (info.isAutoSave)
            {
                Debug.LogWarning("[MainMenu] The Auto-Save entry cannot be renamed.");
                return;
            }
            if (_mmRenameInput != null)
            {
                _mmRenameInput.text = info.fileName;
                _mmRenameInput.Select();
                _mmRenameInput.ActivateInputField();
            }
            if (_mmRenameError != null) _mmRenameError.text = "";
            SetLoadMode(LoadPanelMode.Rename);
        }

        private void HandleRenameInput()
        {
            // Esc cancels
            if (Valkur.Core.Input.InputCompat.CancelPressed())
            {
                CancelRename();
                return;
            }
            // Enter confirms (when input field has focus, Enter inserts newline by default
            // for multiline fields â€” TMP_InputField single-line fires onSubmit instead)
            if (Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                CommitRename();
            }
        }

        private void CancelRename()
        {
            if (_mmRenameInput != null) _mmRenameInput.DeactivateInputField();
            SetLoadMode(LoadPanelMode.List);
        }

        private void CommitRename()
        {
            if (!TryGetSelectedSave(out var info)) { CancelRename(); return; }
            string newName = _mmRenameInput != null ? _mmRenameInput.text : null;
            string sanitized = SaveFileManager.SanitizeSaveName(newName);
            if (sanitized == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "Invalid name.";
                return;
            }
            if (string.Equals(sanitized, info.fileName, System.StringComparison.OrdinalIgnoreCase))
            {
                CancelRename(); // no change
                return;
            }
            string newPath = SaveFileManager.RenameSave(info.path, sanitized);
            if (newPath == null)
            {
                if (_mmRenameError != null) _mmRenameError.text = "Could not rename (duplicate name?).";
                return;
            }
            // Re-list and try to keep the renamed slot selected
            _mmLoadRuns = SaveFileManager.ListSavesByRun();
            for (int ri = 0; ri < _mmLoadRuns.Count; ri++)
            {
                var grp = _mmLoadRuns[ri];
                for (int si = 0; si < grp.saves.Count; si++)
                {
                    if (string.Equals(grp.saves[si].path, newPath,
                                      System.StringComparison.OrdinalIgnoreCase))
                    { _mmLoadRunSel = ri; _mmLoadSaveSel = si; break; }
                }
            }
            EnsureMMLoadScroll();
            CancelRename();
        }

        // â”€â”€ Delete confirmation flow â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RequestDeleteSelectedSave()
        {
            if (!TryGetSelectedSave(out var info)) return;
            if (_mmConfirmText != null)
                _mmConfirmText.text = $"Delete the save\n<b>{info.fileName}</b>?\nThis action cannot be undone.";
            _mmConfirmSel = 0; // default to Cancel
            UpdateConfirmVisuals();
            SetLoadMode(LoadPanelMode.ConfirmDelete);
        }

        private void HandleConfirmDeleteInput()
        {
            if (InputCompat.CancelPressed() || Valkur.Core.Input.InputCompat.CancelPressed())
            { SetLoadMode(LoadPanelMode.List); return; }

            if (InputCompat.NavLeftPressed() || InputCompat.NavRightPressed()
                || Valkur.Core.Input.InputCompat.NavLeftPressed() || Valkur.Core.Input.InputCompat.NavRightPressed())
            { _mmConfirmSel = 1 - _mmConfirmSel; UpdateConfirmVisuals(); }

            if (InputCompat.ConfirmPressed() || Valkur.Core.Input.InputCompat.ConfirmPressed())
            {
                if (_mmConfirmSel == 1) MMDeleteSelectedSave();
                else SetLoadMode(LoadPanelMode.List);
            }
        }

        private void UpdateConfirmVisuals()
        {
            if (_mmConfirmPills == null) return;
            for (int i = 0; i < _mmConfirmPills.Length; i++)
            {
                bool sel = i == _mmConfirmSel;
                _mmConfirmPills[i].color = sel ? PillColor    : new Color(1f, 1f, 1f, 0.04f);
                _mmConfirmTexts[i].color = sel ? TextSelected : TextNormal;
                _mmConfirmTexts[i].fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // â”€â”€ Mode switching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SetLoadMode(LoadPanelMode mode)
        {
            _mmLoadMode = mode;
            if (_mmRenameOverlay  != null) _mmRenameOverlay.SetActive(mode == LoadPanelMode.Rename);
            if (_mmConfirmOverlay != null) _mmConfirmOverlay.SetActive(mode == LoadPanelMode.ConfirmDelete);
        }

        // â”€â”€ Builders for sub-panels â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void BuildRenameOverlay(Transform parent)
        {
            _mmRenameOverlay = CreateUIObject("MMRenameOverlay", parent);
            StretchFull(_mmRenameOverlay);
            _mmRenameOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = CreateUIObject("MMRenameBox", _mmRenameOverlay.transform);
            var br = box.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.5f); br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(520f, 260f);
            box.AddComponent<Image>().color = PanelBg;

            var titleGo = CreateUIObject("Title", box.transform);
            var tr = titleGo.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f); tr.anchoredPosition = new Vector2(0f, -14f);
            tr.sizeDelta = new Vector2(0f, 36f);
            var ttmp = titleGo.AddComponent<TextMeshProUGUI>();
            ttmp.text = "Rename Save"; ttmp.fontSize = 22f;
            ttmp.alignment = TextAlignmentOptions.Center;
            ttmp.color = AccentGold; ttmp.fontStyle = FontStyles.Bold;

            // Input field background
            var fieldGo = CreateUIObject("Field", box.transform);
            var fr = fieldGo.GetComponent<RectTransform>();
            fr.anchorMin = new Vector2(0.5f, 0.5f); fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.pivot = new Vector2(0.5f, 0.5f); fr.anchoredPosition = new Vector2(0f, 30f);
            fr.sizeDelta = new Vector2(460f, 40f);
            fieldGo.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);

            var textArea = CreateUIObject("TextArea", fieldGo.transform);
            var taR = textArea.GetComponent<RectTransform>();
            taR.anchorMin = Vector2.zero; taR.anchorMax = Vector2.one;
            taR.offsetMin = new Vector2(10f, 6f); taR.offsetMax = new Vector2(-10f, -6f);
            textArea.AddComponent<RectMask2D>();

            var textGo = CreateUIObject("Text", textArea.transform);
            var txR = textGo.GetComponent<RectTransform>();
            txR.anchorMin = Vector2.zero; txR.anchorMax = Vector2.one;
            txR.sizeDelta = Vector2.zero;
            var txTMP = textGo.AddComponent<TextMeshProUGUI>();
            txTMP.fontSize = 18f; txTMP.color = TextNormal;
            txTMP.alignment = TextAlignmentOptions.Left;

            var phGo = CreateUIObject("Placeholder", textArea.transform);
            var phR = phGo.GetComponent<RectTransform>();
            phR.anchorMin = Vector2.zero; phR.anchorMax = Vector2.one;
            phR.sizeDelta = Vector2.zero;
            var phTMP = phGo.AddComponent<TextMeshProUGUI>();
            phTMP.text = "Save name..."; phTMP.fontSize = 18f;
            phTMP.color = new Color(1f, 1f, 1f, 0.35f); phTMP.fontStyle = FontStyles.Italic;
            phTMP.alignment = TextAlignmentOptions.Left;

            _mmRenameInput = fieldGo.AddComponent<TMP_InputField>();
            _mmRenameInput.textViewport = taR;
            _mmRenameInput.textComponent = txTMP;
            _mmRenameInput.placeholder = phTMP;
            _mmRenameInput.lineType = TMP_InputField.LineType.SingleLine;
            _mmRenameInput.characterLimit = 64;
            _mmRenameInput.onSubmit.AddListener(_ => CommitRename());

            // Error / hint line (between field and buttons)
            var errGo = CreateUIObject("Error", box.transform);
            var er = errGo.GetComponent<RectTransform>();
            er.anchorMin = new Vector2(0f, 0.5f); er.anchorMax = new Vector2(1f, 0.5f);
            er.pivot = new Vector2(0.5f, 0.5f); er.anchoredPosition = new Vector2(0f, -10f);
            er.sizeDelta = new Vector2(0f, 22f);
            _mmRenameError = errGo.AddComponent<TextMeshProUGUI>();
            _mmRenameError.fontSize = 14f;
            _mmRenameError.alignment = TextAlignmentOptions.Center;
            _mmRenameError.color = new Color(1f, 0.45f, 0.45f, 1f);
            _mmRenameError.text = "";

            // Mouse-clickable buttons (Cancel / OK) — keyboard parity: Esc / Enter
            BuildOverlayButton(box.transform, "Cancel", new Vector2(0.5f, 0f),
                new Vector2(-110f, 60f), new Vector2(180f, 38f),
                new Color(0.30f, 0.30f, 0.30f, 1f), CancelRename);
            BuildOverlayButton(box.transform, "OK",  new Vector2(0.5f, 0f),
                new Vector2( 110f, 60f), new Vector2(180f, 38f),
                new Color(0.24f, 0.47f, 0.20f, 1f), CommitRename);

            var hintGo = CreateUIObject("Hint", box.transform);
            var hr = hintGo.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 14f);
            hr.sizeDelta = new Vector2(0f, 22f);
            var htmp = hintGo.AddComponent<TextMeshProUGUI>();
            htmp.text = "Enter Confirm  |  Esc Cancel";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmRenameOverlay.SetActive(false);
        }        private void BuildDeleteConfirmOverlay(Transform parent)
        {
            _mmConfirmOverlay = CreateUIObject("MMConfirmOverlay", parent);
            StretchFull(_mmConfirmOverlay);
            _mmConfirmOverlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var box = CreateUIObject("MMConfirmBox", _mmConfirmOverlay.transform);
            var br = box.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.5f); br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(540f, 220f);
            box.AddComponent<Image>().color = PanelBg;

            var msgGo = CreateUIObject("Msg", box.transform);
            var mr = msgGo.GetComponent<RectTransform>();
            mr.anchorMin = new Vector2(0f, 0.35f); mr.anchorMax = new Vector2(1f, 1f);
            mr.offsetMin = new Vector2(20f, 0f); mr.offsetMax = new Vector2(-20f, -16f);
            _mmConfirmText = msgGo.AddComponent<TextMeshProUGUI>();
            _mmConfirmText.fontSize = 18f;
            _mmConfirmText.alignment = TextAlignmentOptions.Center;
            _mmConfirmText.color = TextNormal;

            // Two buttons: Cancel (0) / Delete (1)
            _mmConfirmPills = new Image[2];
            _mmConfirmTexts = new TextMeshProUGUI[2];
            string[] labels = { "Cancel", "Delete" };
            float[]  xPos   = { 0.25f, 0.75f };
            for (int i = 0; i < 2; i++)
            {
                int cap = i;
                var btnGo = CreateUIObject($"BtnConfirm_{i}", box.transform);
                var btnR  = btnGo.GetComponent<RectTransform>();
                btnR.anchorMin = new Vector2(xPos[i], 0f); btnR.anchorMax = new Vector2(xPos[i], 0f);
                btnR.pivot = new Vector2(0.5f, 0f); btnR.anchoredPosition = new Vector2(0f, 22f);
                btnR.sizeDelta = new Vector2(180f, 40f);
                _mmConfirmPills[i] = btnGo.AddComponent<Image>();
                _mmConfirmPills[i].color = new Color(1f, 1f, 1f, 0.04f);
                var btn = btnGo.AddComponent<Button>(); btn.targetGraphic = _mmConfirmPills[i];
                btn.onClick.AddListener(() =>
                {
                    _mmConfirmSel = cap; UpdateConfirmVisuals();
                    if (cap == 1) MMDeleteSelectedSave();
                    else SetLoadMode(LoadPanelMode.List);
                });

                var lblGo = CreateUIObject("Lbl", btnGo.transform);
                var lblR = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = Vector2.zero; lblR.anchorMax = Vector2.one;
                lblR.sizeDelta = Vector2.zero;
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = labels[i]; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Center;
                lblTMP.color = TextNormal; lblTMP.raycastTarget = false;
                _mmConfirmTexts[i] = lblTMP;
            }

            var hintGo = CreateUIObject("Hint", box.transform);
            var hr = hintGo.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 4f);
            hr.sizeDelta = new Vector2(0f, 20f);
            var htmp = hintGo.AddComponent<TextMeshProUGUI>();
            htmp.text = "<- -> Choose  |  Enter Confirm  |  Esc Cancel";
            htmp.fontSize = 13f;
            htmp.alignment = TextAlignmentOptions.Center;
            htmp.color = VersionCol;

            _mmConfirmOverlay.SetActive(false);
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string FormatClassName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "â€”";
            return char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant();
        }

        // â”€â”€ Character face crop helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // UV rects for each class portrait image (1536Ã—1024 group portraits).
        // Each rect crops the specific character's face from their highlighted portrait.
        // Format: Rect(x_left, y_bottom, width, height) â€” Unity UV origin = bottom-left.
        // All crops are ~280Ã—280px (square) for distortion-free display in square containers.
        private static readonly System.Collections.Generic.Dictionary<string, Rect> ClassFaceUvRects =
            new System.Collections.Generic.Dictionary<string, Rect>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "barbarian", new Rect(0.000f, 0.552f, 0.182f, 0.273f) },
                { "elven",     new Rect(0.156f, 0.566f, 0.182f, 0.273f) },
                { "mague",     new Rect(0.352f, 0.449f, 0.182f, 0.273f) },
                { "valkyrie",  new Rect(0.592f, 0.576f, 0.182f, 0.273f) },
                { "dwarf",     new Rect(0.801f, 0.547f, 0.182f, 0.273f) },
            };

        private static Rect GetFaceUvRect(string playerClass)
        {
            if (!string.IsNullOrEmpty(playerClass) &&
                ClassFaceUvRects.TryGetValue(playerClass, out var rect))
                return rect;
            return new Rect(0f, 0f, 1f, 1f);
        }

        private Texture2D GetCachedPortraitTexture(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return null;
            // Re-use the same sprite cache entry if already loaded
            if (_portraitSpriteCache.TryGetValue(playerKey, out var cached) && cached != null)
                return cached.texture;
            // Otherwise load the texture directly (Resources caches internally)
            if (!ClassPortraitPaths.TryGetValue(playerKey, out var path)) return null;
            return Resources.Load<Texture2D>(path);
        }
    }
}
