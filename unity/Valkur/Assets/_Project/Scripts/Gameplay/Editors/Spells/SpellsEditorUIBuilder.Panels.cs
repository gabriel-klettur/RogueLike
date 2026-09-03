using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Spells
{
    public static partial class SpellsEditorUIBuilder
    {
        // ── Menu Bar ──────────────────────────────────────────────────────────────

        private static void BuildMenuBar(Transform canvasT, ref UIRefs refs,
            Action<string> onToggle, Action onPerfToggle)
        {
            var go = CreateUI("SpellsMenuBar", canvasT);
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

            var brand = CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = TITLE_BTN_W;
            var brandTmp              = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text             = "SPELLS EDITOR";
            brandTmp.fontSize         = 11f;
            brandTmp.fontStyle        = FontStyles.Bold;
            brandTmp.alignment        = TextAlignmentOptions.Left;
            brandTmp.color            = ACCENT;
            brandTmp.characterSpacing = 2f;

            AddMenuDivider(t);

            refs.ModesMenuBtnImg    = AddMenuBtn(t, "Modes v",      MODES_BTN_W,
                () => onToggle?.Invoke("modes"),    out refs.ModesMenuBtnTmp);
            refs.SpellsMenuBtnImg   = AddMenuBtn(t, "Spells v",     SPELLS_BTN_W,
                () => onToggle?.Invoke("spells"),   out refs.SpellsMenuBtnTmp);
            refs.PropsMenuBtnImg    = AddMenuBtn(t, "Properties v", PROPS_BTN_W,
                () => onToggle?.Invoke("props"),    out refs.PropsMenuBtnTmp);
            refs.ViewMenuBtnImg     = AddMenuBtn(t, "View v",       VIEW_BTN_W,
                () => onToggle?.Invoke("view"),     out refs.ViewMenuBtnTmp);
            refs.TutorialMenuBtnImg = AddMenuBtn(t, "Tutorial v",   TUTORIAL_BTN_W,
                () => onToggle?.Invoke("tutorial"), out refs.TutorialMenuBtnTmp);

            CreateUI("Spacer", t).AddComponent<LayoutElement>().flexibleWidth = 1f;

            AddMenuDivider(t);
            AddMenuBtn(t, "?", HELP_BTN_W, () => onToggle?.Invoke("tutorial"), out _);
            AddMenuDivider(t);
            refs.PerfProbeMenuBtnImg = AddMenuBtn(t, "PERF", PERF_BTN_W,
                () => onPerfToggle?.Invoke(), out refs.PerfProbeMenuBtnTmp);
        }

        // ── Modes Panel ───────────────────────────────────────────────────────────

        private static void BuildModesPanel(Transform canvasT, ref UIRefs refs,
            Action onAdd, Action onRemove, Action onReload,
            Action onUndo, Action onRedo, Action onSave)
        {
            refs.ModesDropdown = MakeDrop("SpellsModesPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                MODES_W, MODES_H, "Modes",
                out var t, out refs.ModesPanelDrag, narrowPanel: true);

            refs.AddBtnImg    = AddToolBtn(t, "Add",  "+", BTN_H, onAdd);
            refs.RemoveBtnImg = AddDangerToolBtn(t, "Rem", "-", BTN_H, onRemove);

            AddInlineSeparator(t);
            AddSectionLabel(t, "DATA");

            refs.ReloadBtnImg = AddToolBtn(t, "Rld", "json", BTN_H, onReload);

            AddInlineSeparator(t);
            AddSectionLabel(t, "EDIT");

            refs.UndoBtnImg = AddToolBtn(t, "Undo", "Z", BTN_H, onUndo);
            refs.RedoBtnImg = AddToolBtn(t, "Redo", "Y", BTN_H, onRedo);

            AddInlineSeparator(t);
            AddSectionLabel(t, "FILE");

            refs.SaveBtnImg = AddToolBtn(t, "Save", "to disk", BTN_H, onSave);

            refs.ModesDropdown.SetActive(false);
        }

        // ── Properties Panel (TabStrip) ───────────────────────────────────────────

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("SpellsPropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Build tab-content containers FIRST so AddTab can hide them.
            var tab1 = CreateUI("PropsTab", t);
            var tab1Le = tab1.AddComponent<LayoutElement>();
            tab1Le.flexibleHeight = 1f;
            var tab1Vlg = tab1.AddComponent<VerticalLayoutGroup>();
            tab1Vlg.childForceExpandWidth = true;
            tab1Vlg.childForceExpandHeight = false;
            tab1Vlg.childControlWidth = true; tab1Vlg.childControlHeight = true;
            tab1Vlg.spacing = 2f; tab1Vlg.padding = new RectOffset(0, 0, 0, 0);

            var (pScroll, pContent) = EditorUIHelpers.MakeScrollView(tab1.transform, "PropsScroll");
            EnsureFlexibleHeight(pScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(pScroll);
            refs.PropsForm = PropertyForm.Create(pContent, "PropsForm");

            var tab2 = CreateUI("AssetsTab", t);
            var tab2Le = tab2.AddComponent<LayoutElement>();
            tab2Le.flexibleHeight = 1f;
            var tab2Vlg = tab2.AddComponent<VerticalLayoutGroup>();
            tab2Vlg.childForceExpandWidth = true;
            tab2Vlg.childForceExpandHeight = false;
            tab2Vlg.childControlWidth = true; tab2Vlg.childControlHeight = true;
            tab2Vlg.spacing = 6f; tab2Vlg.padding = new RectOffset(8, 8, 8, 8);

            // Gather tab — the cast flourish. A header naming the resolved family, a button
            // row, then a scrolling form of one row per knob. Its own PropertyForm rather
            // than a section inside tab1 because its keys address CastFlourishProfile, not
            // SpellDefinition, and sharing a form would put two meanings on one ValueChanged.
            var tab3 = CreateUI("GatherTab", t);
            var tab3Le = tab3.AddComponent<LayoutElement>();
            tab3Le.flexibleHeight = 1f;
            var tab3Vlg = tab3.AddComponent<VerticalLayoutGroup>();
            tab3Vlg.childForceExpandWidth = true;
            tab3Vlg.childForceExpandHeight = false;
            tab3Vlg.childControlWidth = true; tab3Vlg.childControlHeight = true;
            tab3Vlg.spacing = 4f; tab3Vlg.padding = new RectOffset(6, 6, 6, 4);

            var famGo = CreateUI("GatherFamily", tab3.transform);
            famGo.AddComponent<LayoutElement>().preferredHeight = 34f;
            var famTmp = famGo.AddComponent<TextMeshProUGUI>();
            famTmp.text                = "(no spell selected)";
            famTmp.fontSize            = 11f;
            famTmp.alignment           = TextAlignmentOptions.TopLeft;
            famTmp.color               = TEXT_MUTED;
            famTmp.enableWordWrapping  = true;
            refs.GatherFamilyTmp = famTmp;

            // Buttons are added by the editor, which owns what they do.
            var gatherBtnRow = CreateUI("GatherButtons", tab3.transform);
            var gatherBtnLe = gatherBtnRow.AddComponent<LayoutElement>();
            gatherBtnLe.preferredHeight = 26f;
            // Explicitly rigid. A LayoutGroup is itself an ILayoutElement, and one with
            // childForceExpandHeight reports flexibleHeight = 1 — which a LayoutElement
            // leaving the field at -1 does not override. Measured before this line: the row
            // asked for 26, advertised flexible 1, and the parent split the leftover space
            // with the scroll view, so two 24px buttons rendered 246px tall.
            gatherBtnLe.flexibleHeight = 0f;
            var gatherBtnLayout = gatherBtnRow.AddComponent<HorizontalLayoutGroup>();
            gatherBtnLayout.spacing = 4f;
            gatherBtnLayout.childForceExpandWidth = true;
            gatherBtnLayout.childForceExpandHeight = false;
            gatherBtnLayout.childControlWidth = true; gatherBtnLayout.childControlHeight = true;
            refs.PropsGatherRoot = (RectTransform)gatherBtnRow.transform;

            var (gScroll, gContent) = EditorUIHelpers.MakeScrollView(tab3.transform, "GatherScroll");
            EnsureFlexibleHeight(gScroll.gameObject);
            EditorUIHelpers.AddVerticalScrollbar(gScroll);
            refs.PropsGatherForm = PropertyForm.Create(gContent, "GatherForm");

            tab1.transform.SetAsLastSibling();
            tab2.transform.SetAsLastSibling();
            tab3.transform.SetAsLastSibling();

            refs.PropsAssetsRoot = (RectTransform)tab2.transform;

            var previewWrap = CreateUI("PreviewWrap", tab2.transform);
            previewWrap.AddComponent<LayoutElement>().preferredHeight = 180f;
            var previewLayout = previewWrap.AddComponent<HorizontalLayoutGroup>();
            previewLayout.childAlignment = TextAnchor.MiddleCenter;
            previewLayout.childForceExpandWidth = false;
            previewLayout.childForceExpandHeight = false;
            previewLayout.childControlWidth = true; previewLayout.childControlHeight = true;

            var previewGo = CreateUI("Preview", previewWrap.transform);
            var previewLe = previewGo.AddComponent<LayoutElement>();
            previewLe.preferredWidth = 180f; previewLe.preferredHeight = 180f;
            var previewBg = previewGo.AddComponent<Image>();
            previewBg.color = EditorUIHelpers.BG_SURFACE;
            var iconGo = CreateUI("Icon", previewGo.transform);
            EditorUIHelpers.StretchFill(iconGo);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            iconImg.enabled = false;
            refs.AssetPreviewImage = iconImg;

            var nameGo = CreateUI("AssetName", tab2.transform);
            nameGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = "(no spell selected)";
            nameTmp.fontSize  = 13f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = ACCENT;
            refs.AssetNameTmp = nameTmp;

            var hintGo = CreateUI("AssetHint", tab2.transform);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 40f;
            var hintTmp = hintGo.AddComponent<TextMeshProUGUI>();
            hintTmp.text      = "Asset picker — phase 2";
            hintTmp.fontSize  = 11f;
            hintTmp.fontStyle = FontStyles.Italic;
            hintTmp.alignment = TextAlignmentOptions.Center;
            hintTmp.color     = TEXT_MUTED;
            hintTmp.enableWordWrapping = true;

            var tabs = TabStrip.Create(t, "PropsTabs");
            tabs.transform.SetSiblingIndex(0);
            tabs.AddTab("props",  "Properties",        tab1);
            tabs.AddTab("assets", "Assets / Particles", tab2);
            tabs.AddTab("gather", "Gather",            tab3);
            refs.PropsTabStrip = tabs;

            refs.PropsDropdown.SetActive(false);
        }

        // ── Tutorial Panel ────────────────────────────────────────────────────────

        private static void BuildTutorialPanel(Transform canvasT, ref UIRefs refs,
            Action onPrev, Action onNext, Action onClose)
        {
            float xOff = PANEL_GAP + MODES_W + PANEL_GAP + SPELLS_W + PANEL_GAP;
            refs.TutorialDropdown = MakeDrop("SpellsTutorialPanel", canvasT,
                PanelDock.TopLeft, xOff, PANEL_TOP_OFFSET + 80f,
                TUT_W, TUT_H, "Tutorial",
                out var t, out refs.TutorialPanelDrag);

            var stepGo = CreateUI("Step", t);
            stepGo.AddComponent<LayoutElement>().preferredHeight = 24f;
            var stepTmp = stepGo.AddComponent<TextMeshProUGUI>();
            stepTmp.fontSize  = 13f;
            stepTmp.fontStyle = FontStyles.Bold;
            stepTmp.alignment = TextAlignmentOptions.Left;
            stepTmp.color     = ACCENT;
            refs.TutorialStepLabel = stepTmp;

            var bodyGo = CreateUI("Body", t);
            bodyGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var bodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            bodyTmp.fontSize           = 12f;
            bodyTmp.color              = TEXT_PRIMARY;
            bodyTmp.alignment          = TextAlignmentOptions.TopLeft;
            bodyTmp.enableWordWrapping = true;
            refs.TutorialBodyTmp = bodyTmp;

            var nav = CreateUI("Nav", t);
            nav.AddComponent<LayoutElement>().preferredHeight = 30f;
            var navHlg = nav.AddComponent<HorizontalLayoutGroup>();
            navHlg.spacing = 6f;
            navHlg.childForceExpandWidth = true;
            navHlg.childControlWidth = true; navHlg.childControlHeight = true;

            refs.TutorialPrevBtn  = EditorUIHelpers.MakeButton(nav.transform, "<= Prev", () => onPrev?.Invoke(), 28f, 11f);
            refs.TutorialNextBtn  = EditorUIHelpers.MakeButton(nav.transform, "Next =>", () => onNext?.Invoke(), 28f, 11f);
            refs.TutorialCloseBtn = EditorUIHelpers.MakeButton(nav.transform, "Close",  () => onClose?.Invoke(), 28f, 11f);

            refs.TutorialDropdown.SetActive(false);
        }
    }
}
