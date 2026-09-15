using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Generation;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// The strip across the top every other runtime editor carries: brand, the two panels, the
    /// editor's main verbs, the lab switch and the latest status line, plus a close button.
    ///
    /// <para><b>The lab switch is up here as well as in "Construir"</b> because two verbs depend
    /// on it and one of them lives in the OTHER panel. With the switch only at the bottom of the
    /// Mundo tab, "Visualizar mapa" looked clickable and silently did nothing — the reason sat in
    /// a panel the author was not looking at.</para>
    ///
    /// <para><b>The status is mirrored here</b> for the same reason: a refusal written only at the
    /// foot of the parameters panel is a refusal nobody reads.</para>
    /// </summary>
    public partial class SeedWorldRuntimeEditor
    {
        private const float BRAND_W = 110f;
        private const float MENU_PANEL_BTN_W = 100f;
        private const float MENU_VERB_BTN_W = 120f;
        private const float MENU_LAB_BTN_W = 150f;
        private const float MENU_CLOSE_BTN_W = 34f;

        private Image _paramsMenuImg;
        private TextMeshProUGUI _paramsMenuTmp;
        private Image _previewMenuImg;
        private TextMeshProUGUI _previewMenuTmp;
        private Image _labMenuImg;
        private TextMeshProUGUI _labMenuTmp;
        private TextMeshProUGUI _menuStatus;

        private void BuildMenuBar(Transform parent)
        {
            var go = EditorUIHelpers.CreateUI("SeedWorldMenuBar", parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(0f, TileEditorUIHelpers.MENUBAR_HEIGHT);

            var bg = go.AddComponent<Image>();
            bg.color = TileEditorUIHelpers.MENUBAR_BG;
            bg.raycastTarget = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = TileEditorUIHelpers.BORDER;
            outline.effectDistance = new Vector2(0f, -1f);

            var chrome = go.AddComponent<MenuBarChrome>();
            chrome.BgImage = bg;
            chrome.BorderOutline = outline;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            int pad = (int)TileEditorUIHelpers.MENUBAR_PAD_H;
            hlg.padding = new RectOffset(pad, pad, 0, 0);
            hlg.spacing = TileEditorUIHelpers.MENUBAR_SPACING;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var t = go.transform;

            var brand = EditorUIHelpers.CreateUI("Brand", t);
            brand.AddComponent<LayoutElement>().preferredWidth = BRAND_W;
            var brandTmp = brand.AddComponent<TextMeshProUGUI>();
            brandTmp.text = "SEED WORLD";
            brandTmp.fontSize = 11f;
            brandTmp.fontStyle = FontStyles.Bold;
            brandTmp.alignment = TextAlignmentOptions.Left;
            brandTmp.color = UITheme.ACCENT;
            brandTmp.characterSpacing = 2f;
            brandTmp.raycastTarget = false;

            EditorUIHelpers.AddMenuDivider(t);

            _paramsMenuImg = EditorUIHelpers.AddMenuBtn(t, "Parametros v", MENU_PANEL_BTN_W,
                () => TogglePanel(_paramsPanel), out _paramsMenuTmp);
            _previewMenuImg = EditorUIHelpers.AddMenuBtn(t, "Vista previa v", MENU_PANEL_BTN_W,
                () => TogglePanel(_previewPanel), out _previewMenuTmp);

            EditorUIHelpers.AddMenuDivider(t);

            EditorUIHelpers.AddMenuBtn(t, "Visualizar mapa", MENU_VERB_BTN_W, BeginViewing, out _)
                .gameObject.name = "MenuBtn_ViewMap";
            EditorUIHelpers.AddMenuBtn(t, "Construir mundo", MENU_VERB_BTN_W, OpenBuildSection, out _)
                .gameObject.name = "MenuBtn_Build";

            EditorUIHelpers.AddMenuDivider(t);

            _labMenuImg = EditorUIHelpers.AddMenuBtn(t, "Laboratorio", MENU_LAB_BTN_W, ToggleLab, out _labMenuTmp);
            _labMenuImg.gameObject.name = "MenuBtn_Lab";

            EditorUIHelpers.AddMenuDivider(t);

            var status = EditorUIHelpers.CreateUI("MenuStatus", t);
            status.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _menuStatus = status.AddComponent<TextMeshProUGUI>();
            _menuStatus.fontSize = 10f;
            _menuStatus.color = EditorUIHelpers.TEXT_SECONDARY;
            _menuStatus.alignment = TextAlignmentOptions.MidlineLeft;
            _menuStatus.enableWordWrapping = false;
            _menuStatus.overflowMode = TextOverflowModes.Ellipsis;
            _menuStatus.raycastTarget = false;

            EditorUIHelpers.AddMenuDivider(t);
            EditorUIHelpers.AddMenuBtn(t, "X", MENU_CLOSE_BTN_W, Deactivate, out _).gameObject.name = "MenuBtn_Close";

            RefreshMenuBar();
        }

        private void TogglePanel(DraggablePanel panel)
        {
            if (panel == null) return;
            bool open = !panel.gameObject.activeSelf;
            panel.gameObject.SetActive(open);
            if (open) { panel.MarkOpened(); panel.transform.SetAsLastSibling(); }
            RefreshMenuBar();
        }

        /// <summary>"Construir mundo" lives in the Mundo tab; the bar takes the author there.</summary>
        private void OpenBuildSection()
        {
            if (_paramsPanel != null && !_paramsPanel.gameObject.activeSelf) TogglePanel(_paramsPanel);
            SetTab(Tab.World);
            if (_bodyScrollContent != null)
            {
                var scroll = _bodyScrollContent.GetComponentInParent<ScrollRect>();
                if (scroll != null) scroll.verticalNormalizedPosition = 0f;
            }
            SetStatus(SeedWorldLab.Enabled
                ? "Construir: al final de la pestana Mundo."
                : "Construir: enciende el Laboratorio primero (barra superior).");
        }

        internal void ToggleLab()
        {
            SeedWorldLab.SetEnabled(!SeedWorldLab.Enabled);
            _armedOverwriteSlot = null;
            SetStatus(SeedWorldLab.Enabled
                ? "Laboratorio ENCENDIDO: ya puedes Construir y Visualizar mapa."
                : "Laboratorio APAGADO: solo vista previa.");
            RebuildBody();
        }

        private void RefreshMenuBar()
        {
            EditorUIHelpers.ApplyMenuBtnStyle(_paramsMenuImg, _paramsMenuTmp,
                _paramsPanel != null && _paramsPanel.gameObject.activeSelf);
            EditorUIHelpers.ApplyMenuBtnStyle(_previewMenuImg, _previewMenuTmp,
                _previewPanel != null && _previewPanel.gameObject.activeSelf);

            bool lab = SeedWorldLab.Enabled;
            if (_labMenuTmp != null) _labMenuTmp.text = lab ? "Laboratorio: ON" : "Laboratorio: OFF";
            EditorUIHelpers.ApplyMenuBtnStyle(_labMenuImg, _labMenuTmp, lab);
            if (!lab && _labMenuTmp != null) _labMenuTmp.color = EditorUIHelpers.TEXT_MUTED;
        }
    }
}
