using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.UI;
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
        private Graphic[]          _mmRunPills;
        private TextMeshProUGUI[]  _mmRunTexts;
        private RawImage[]         _mmRunFaceImages;
        private Graphic[][]        _mmRunHoverBorders;
        private int                _mmRunHover = -1;

        // Save list widgets (right column, top)
        private Graphic[]          _mmSavePills;
        private TextMeshProUGUI[]  _mmSaveTexts;
        private Graphic[][]        _mmSaveHoverBorders;
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
        private Frontend.BevelFrameGraphic[] _mmConfirmPills;
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
            // TRANSPARENT. The shell owns the one veil over the art and deepens it while any
            // sub-screen is open; this overlay used to paint a second 55 % black of its own, so
            // the load panel sat on a painting darkened twice.
            var blocker = _mmLoadOverlay.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, 0f);

            float panelW = Style.widePanelWidth;
            const float panelH = 520f;
            const float splitX = 0.41f;

            // The panel's frame, header band and hint bar all come from MenuArt now, at the one
            // anchor every sub-screen shares. What this replaces is a bare Image of a flat colour
            // with square corners, anchored by a comment that named the old game's logo.
            var panelRt = Kit.MenuUIKit.Rect("LoadPanel", _mmLoadOverlay.transform);
            panelRt.anchorMin = new Vector2(0.5f, 1f);
            panelRt.anchorMax = new Vector2(0.5f, 1f);
            panelRt.pivot = new Vector2(0.5f, 1f);
            panelRt.anchoredPosition = new Vector2(0f, -Style.panelTopOffset);
            panelRt.sizeDelta = new Vector2(panelW, panelH);
            var panel = panelRt.gameObject;

            var frame = Kit.MenuUIKit.Panel("Frame", panelRt, _art, Style);
            var frameRt = (RectTransform)frame.transform;
            frameRt.anchorMin = Vector2.zero; frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = Vector2.zero; frameRt.offsetMax = Vector2.zero;

            Kit.MenuUIKit.PanelHeader(panelRt, _art, Style, MenuText.LoadTitle);
            Kit.MenuUIKit.PanelHint(panelRt, Style, MenuText.LoadHint);

            // Column separator
            // The etapa divider stood on end: a groove, a light line and a notch at each end.
            var sep = Frontend.FrontendRuleGraphic.Create(panelRt, "ColSep", vertical: true, Style.Gold);
            var sepRt = sep.rectTransform;
            sepRt.anchorMin = new Vector2(splitX + 0.005f, 0.10f);
            sepRt.anchorMax = new Vector2(splitX + 0.005f, 0.86f);
            sepRt.pivot = new Vector2(0.5f, 0.5f); sepRt.sizeDelta = new Vector2(8f, 0f);
            sepRt.anchoredPosition = Vector2.zero;

            BuildMMColHeader(MenuText.LoadRuns,  panel.transform, 0.01f, splitX);
            BuildMMColHeader(MenuText.LoadSaves, panel.transform, splitX + 0.02f, 0.98f);

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
            AddMMLoadButton(panel.transform, MenuText.LoadLoad,
                new Vector2(bL,              0f), new Vector2(bL + bW,          0f),
                Style.Success, MMLoadSelectedSave);
            AddMMLoadButton(panel.transform, MenuText.LoadRename,
                new Vector2(bL + bW + 0.01f, 0f), new Vector2(bL + bW * 2f + 0.01f, 0f),
                Style.PanelBevel, BeginRenameSelectedSave);
            // Danger is drawn DIM at rest and only lights on hover: a permanently saturated red
            // button beside two neutral ones is the loudest thing on a panel whose job is to
            // load, not to delete.
            AddMMLoadButton(panel.transform, MenuText.LoadDelete,
                new Vector2(bL + bW * 2f + 0.02f, 0f), new Vector2(0.97f, 0f),
                new Color(Style.Danger.r * 0.55f, Style.Danger.g * 0.28f, Style.Danger.b * 0.28f, 1f),
                RequestDeleteSelectedSave);

            BuildRenameOverlay(_mmLoadOverlay.transform);
            BuildDeleteConfirmOverlay(_mmLoadOverlay.transform);

            _mmLoadOverlay.SetActive(false);
        }

        private void BuildMMColHeader(string label, Transform parent, float anchorL, float anchorR)
        {
            var go = CreateUIObject($"ColHdr_{label}", parent);
            var rt = go.GetComponent<RectTransform>();
            // Lowered from [0.88, 0.94] to [0.82, 0.87] so the title has clear airspace above.
            rt.anchorMin = new Vector2(anchorL, 0.83f); rt.anchorMax = new Vector2(anchorR, 0.88f);
            rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            var tmp = MenuTypography.Label(go, Style, label, Style.detailFontSize - 1f,
                                           Style.TextMuted, TextAlignmentOptions.Left, bold: true);
            tmp.characterSpacing = 8f;
        }

        private void AddMMLoadButton(Transform parent, string label,
            Vector2 anchorMin, Vector2 anchorMax, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var btn = Kit.MenuUIKit.Button("MMLoadBtn_" + label, parent, _art, Style, label, bg, action);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(anchorMin.x, 0f);
            rt.anchorMax = new Vector2(anchorMax.x, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, Style.hintBarHeight + 6f);
            rt.sizeDelta = new Vector2(0f, 34f);
        }

        /// <summary>
        /// Generic overlay button placed by absolute pivot/anchor inside an overlay panel.
        /// Used for the Rename overlay's Cancel/OK pair so every action is reachable with the mouse.
        /// </summary>
        private void BuildOverlayButton(Transform parent, string label,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, Color bg,
            UnityEngine.Events.UnityAction action)
        {
            var btn = Kit.MenuUIKit.Button($"OverlayBtn_{label}", parent, _art, Style, label, bg, action);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        /// <summary>
        /// A modal box in the kit's housing: the same frame as every panel, with a lit header rule.
        /// Replaces a flat <c>Image</c> in the panel colour with square corners.
        /// </summary>
        private GameObject BuildModalBox(string name, Transform parent, Vector2 size, float headerHeight)
        {
            var box = CreateUIObject(name, parent);
            var br = box.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.5f); br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = Vector2.zero;
            br.sizeDelta = size;
            var frame = Kit.MenuUIKit.Panel("Frame", box.transform, _art, Style);
            frame.HeaderHeight = headerHeight;
            frame.HeaderGemLit = headerHeight > 0f ? 0.85f : 0f;
            // The frame is drawn but must still swallow clicks meant for the panel under the box.
            frame.raycastTarget = true;
            return box;
        }
    }
}
