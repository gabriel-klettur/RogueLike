using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Builds the UI shell for the Boss Editor.
    /// Architecture mirrors ParticlesEditorUIBuilder:
    ///   • 30 px menu bar — brand + 3 dropdown buttons + tutorial shortcut
    ///   • Three DraggablePanel floating panels:
    ///       Bosses (left) — scrollable BossDefinition list
    ///       Phases &amp; Charts (middle) — phase + chart tree
    ///       Cue Inspector (right) — per-chart cue table + save button
    /// </summary>
    public static class BossEditorUIBuilder
    {
        // ── UIRefs ─────────────────────────────────────────────────────────────

        public struct UIRefs
        {
            // Menu bar
            public GameObject      MenuBar;
            public Image           BossesMenuBtnImg;   public TextMeshProUGUI BossesMenuBtnTmp;
            public Image           PhasesMenuBtnImg;   public TextMeshProUGUI PhasesMenuBtnTmp;
            public Image           CuesMenuBtnImg;     public TextMeshProUGUI CuesMenuBtnTmp;

            // Panel roots + drag components
            public GameObject      BossesDropdown;     public DraggablePanel BossesPanelDrag;
            public GameObject      PhasesDropdown;     public DraggablePanel PhasesPanelDrag;
            public GameObject      CuesDropdown;       public DraggablePanel CuesPanelDrag;

            // Bosses panel
            public RectTransform   BossListContent;

            // Phases & Charts panel
            public RectTransform   PhasesContent;

            // Cue Inspector panel
            public RectTransform   CuesContent;
            public TextMeshProUGUI StatusText;

            // Tools
            public Image           UndoBtnImg;         public TextMeshProUGUI UndoBtnLabel;
            public Image           RedoBtnImg;         public TextMeshProUGUI RedoBtnLabel;

            // Live preview button (menu bar)
            public Image           PreviewBtnImg;      public TextMeshProUGUI PreviewBtnTmp;
        }

        // ── Panel sizes ────────────────────────────────────────────────────────

        private const float BOSSES_W = 220f;
        private const float BOSSES_H = 480f + PANEL_HDR_H;
        private const float PHASES_W = 260f;
        private const float PHASES_H = 480f + PANEL_HDR_H;
        private const float CUES_W   = 360f;
        private const float CUES_H   = 560f + PANEL_HDR_H;

        // ── Menu button widths ─────────────────────────────────────────────────

        private const float TITLE_BTN_W    = 120f;
        private const float BOSSES_BTN_W  = 76f;
        private const float PHASES_BTN_W  = 96f;
        private const float CUES_BTN_W    = 90f;
        private const float PREVIEW_BTN_W = 92f;
        private const float TUTORIAL_BTN_W = 40f;

        // ── BuildAll ──────────────────────────────────────────────────────────

        public static UIRefs BuildAll(
            Transform      canvasT,
            Action<string> onDropdownToggle,
            Action         onUndo,
            Action         onRedo,
            Action         onSaveChart,
            Action         onAddPhase,
            Action         onAddChart,
            Action         onAddCue,
            Action         onToggleTutorial,
            Action         onToggleLivePreview = null)
        {
            DraggablePanel.TopReservedPx = MENUBAR_HEIGHT;

            var refs = new UIRefs();

            BuildMenuBar(canvasT, ref refs, onDropdownToggle, onToggleTutorial, onToggleLivePreview);
            BuildBossesPanel(canvasT, ref refs);
            BuildPhasesPanel(canvasT, ref refs, onAddPhase, onAddChart);
            BuildCuesPanel(canvasT, ref refs, onUndo, onRedo, onSaveChart, onAddCue);

            return refs;
        }

        // ── Menu Bar ──────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onTutorial, Action onPreview = null)
        {
            var go = UIFactory.CreateUI("BossMenuBar", canvasT);
            var r  = go.GetComponent<RectTransform>();
            r.anchorMin        = new Vector2(0f, 1f);
            r.anchorMax        = new Vector2(1f, 1f);
            r.pivot            = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta        = new Vector2(0f, MENUBAR_HEIGHT);
            refs.MenuBar       = go;

            var bg           = go.AddComponent<Image>();
            bg.color         = MENUBAR_BG;
            bg.raycastTarget = true;

            var ol            = go.AddComponent<Outline>();
            ol.effectColor    = BORDER;
            ol.effectDistance = new Vector2(0f, -1f);

            var chrome           = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset((int)MENUBAR_PAD_H, (int)MENUBAR_PAD_H, 0, 0);
            hlg.spacing             = MENUBAR_SPACING;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var t = go.transform;

            // Brand
            var brand = UIFactory.CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "BOSS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            EditorUIHelpers.AddMenuDivider(t);

            refs.BossesMenuBtnImg = EditorUIHelpers.AddMenuBtn(t, "Bosses v",          BOSSES_BTN_W,
                () => onToggle?.Invoke("bosses"), out refs.BossesMenuBtnTmp);
            refs.PhasesMenuBtnImg = EditorUIHelpers.AddMenuBtn(t, "Phases & Charts v", PHASES_BTN_W,
                () => onToggle?.Invoke("phases"), out refs.PhasesMenuBtnTmp);
            refs.CuesMenuBtnImg   = EditorUIHelpers.AddMenuBtn(t, "Cue Inspector v",   CUES_BTN_W,
                () => onToggle?.Invoke("cues"),   out refs.CuesMenuBtnTmp);

            UIFactory.CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            EditorUIHelpers.AddMenuDivider(t);
            refs.PreviewBtnImg = EditorUIHelpers.AddMenuBtn(t, "Live Preview", PREVIEW_BTN_W,
                () => onPreview?.Invoke(), out refs.PreviewBtnTmp);
            EditorUIHelpers.AddMenuDivider(t);
            EditorUIHelpers.AddMenuBtn(t, "?", TUTORIAL_BTN_W, () => onTutorial?.Invoke(), out _);
        }

        // ── Bosses Panel (left) ───────────────────────────────────────────────

        private static void BuildBossesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.BossesDropdown = EditorUIHelpers.MakeDropPanel(
                "BossBossesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                BOSSES_W, BOSSES_H, "Bosses",
                out var t, out refs.BossesPanelDrag);

            var hint = UILabel.Add(t, "All BossDefinition assets.\nClick to select.", 10f);
            hint.color              = UITheme.TEXT_MUTED;
            hint.enableWordWrapping = true;
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            UISeparator.Build(t);

            var (scroll, content) = UIFactory.MakeScrollView(t, "BossListScroll");
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UIFactory.AddVerticalScrollbar(scroll);
            // UIFactory.MakeScrollView already adds VerticalLayoutGroup + ContentSizeFitter
            // on the content. Adjust spacing to our preferred values.
            var existingVlg = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (existingVlg != null)
            {
                existingVlg.spacing             = 2f;
                existingVlg.childForceExpandWidth  = true;
                existingVlg.childForceExpandHeight = false;
                existingVlg.childControlWidth      = true;
                existingVlg.childControlHeight     = true;
            }

            refs.BossListContent = content;
            refs.BossesDropdown.SetActive(false);
        }

        // ── Phases & Charts Panel (middle) ────────────────────────────────────

        private static void BuildPhasesPanel(Transform canvasT, ref UIRefs refs,
            Action onAddPhase, Action onAddChart)
        {
            float x = PANEL_GAP + BOSSES_W + PANEL_GAP;
            refs.PhasesDropdown = EditorUIHelpers.MakeDropPanel(
                "BossPhasesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                PHASES_W, PHASES_H, "Phases & Charts",
                out var t, out refs.PhasesPanelDrag);

            // Action buttons
            var btnRow = UIFactory.CreateUI("BtnRow", t);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            EditorUIHelpers.AddActionBtn(btnRow.transform, "+ Phase", 28f, onAddPhase, out _);
            EditorUIHelpers.AddActionBtn(btnRow.transform, "+ Chart", 28f, onAddChart, out _);

            UISeparator.Build(t);

            var (scroll, content) = UIFactory.MakeScrollView(t, "PhasesScroll");
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UIFactory.AddVerticalScrollbar(scroll);
            // Adjust existing VerticalLayoutGroup created by MakeScrollView.
            var phasesVlg = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (phasesVlg != null)
            {
                phasesVlg.spacing             = 2f;
                phasesVlg.childForceExpandWidth  = true;
                phasesVlg.childForceExpandHeight = false;
                phasesVlg.childControlWidth      = true;
                phasesVlg.childControlHeight     = true;
            }

            refs.PhasesContent = content;
            refs.PhasesDropdown.SetActive(false);
        }

        // ── Cue Inspector Panel (right) ───────────────────────────────────────

        private static void BuildCuesPanel(Transform canvasT, ref UIRefs refs,
            Action onUndo, Action onRedo, Action onSave, Action onAddCue)
        {
            refs.CuesDropdown = EditorUIHelpers.MakeDropPanel(
                "BossCuesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                CUES_W, CUES_H, "Cue Inspector",
                out var t, out refs.CuesPanelDrag);

            // Toolbar row: Undo / Redo / Save / + Cue
            var toolRow = UIFactory.CreateUI("Tools", t);
            toolRow.AddComponent<LayoutElement>().preferredHeight = 28f;
            var hlg = toolRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;

            refs.UndoBtnImg = EditorUIHelpers.AddActionBtn(toolRow.transform, "Undo", 28f, onUndo,
                out refs.UndoBtnLabel);
            refs.RedoBtnImg = EditorUIHelpers.AddActionBtn(toolRow.transform, "Redo", 28f, onRedo,
                out refs.RedoBtnLabel);
            EditorUIHelpers.AddActionBtn(toolRow.transform, "Save Chart", 28f, onSave, out _);
            EditorUIHelpers.AddActionBtn(toolRow.transform, "+ Cue",      28f, onAddCue, out _);

            UISeparator.Build(t);

            // Cues scroll area
            var (scroll, content) = UIFactory.MakeScrollView(t, "CuesScroll");
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UIFactory.AddVerticalScrollbar(scroll);
            // Adjust existing VerticalLayoutGroup created by MakeScrollView.
            var cuesVlg = content.gameObject.GetComponent<VerticalLayoutGroup>();
            if (cuesVlg != null)
            {
                cuesVlg.spacing             = 3f;
                cuesVlg.childForceExpandWidth  = true;
                cuesVlg.childForceExpandHeight = false;
                cuesVlg.childControlWidth      = true;
                cuesVlg.childControlHeight     = true;
            }

            refs.CuesContent = content;

            // Status bar
            refs.StatusText = UILabel.MakeStatus(t);

            refs.CuesDropdown.SetActive(false);
        }

    }
}
