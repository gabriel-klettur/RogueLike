using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Floating panel that lets the player tune the cinematic look of any
    /// day phase at runtime. For each of the 7 phases the player can edit
    /// 5 properties via labeled sliders:
    ///   • Tono (Hue)         0..360°
    ///   • Saturación         0..1
    ///   • Brillo (Intensity) 0..1.5
    ///   • Calidez            -1..+1   (color temperature shift)
    ///   • Viñeta             0..1     (per-phase screen-edge tint strength)
    ///
    /// Built lazily, hidden by default. Toggled visible from the gear button
    /// on <see cref="DayNightShortcutsHUD"/>'s title bar via
    /// <see cref="ToggleVisible"/>. All edits flow through
    /// <see cref="DayNightCycle.SetPhaseLook"/> so the changes persist for the
    /// rest of the session and the live world updates immediately.
    /// </summary>
    public sealed class DayNightPhaseSettingsHUD : MonoBehaviour
    {
        // ── Layout (anchored top-right, hangs below the FASES panel) ─────────
        private const float MARGIN_RIGHT = 24f + 110f + 10f;             // = 144 (same as FASES)
        private const float MARGIN_TOP   = 24f + 372f + 10f;             // FASES panel ≈ 372px tall + gap
        private const float PANEL_WIDTH  = 250f;
        private const float PANEL_PAD    = 8f;
        private const float TITLE_H      = 18f;
        private const float TAB_ROW_H    = 24f;
        private const float NAME_ROW_H   = 18f;
        private const float SLIDER_LBL_H = 14f;
        private const float SLIDER_H     = 16f;
        private const float SLIDER_GAP   = 6f;
        private const float ACTION_ROW_H = 22f;
        private const float ACTION_GAP   = 8f;

        // ── Palette ──────────────────────────────────────────────────────────
        private static readonly Color BG_PANEL    = new Color(0.04f, 0.05f, 0.08f, 0.78f);
        private static readonly Color TITLE_COLOR = new Color(0.85f, 0.88f, 0.95f, 0.9f);
        private static readonly Color LABEL_COLOR = new Color(0.92f, 0.94f, 0.98f, 1f);
        private static readonly Color VALUE_COLOR = new Color(0.95f, 0.78f, 0.40f, 1f);
        private static readonly Color TAB_OFF     = new Color(0.10f, 0.12f, 0.18f, 0.95f);
        private static readonly Color TAB_ON      = new Color(0.95f, 0.78f, 0.40f, 1f);
        private static readonly Color TRACK_COLOR = new Color(0.45f, 0.48f, 0.55f, 0.95f);
        private static readonly Color HANDLE_COLOR = new Color(0.95f, 0.78f, 0.40f, 1f);
        private static readonly Color ACTION_BTN  = new Color(0.18f, 0.22f, 0.28f, 0.95f);
        private static readonly Color ACTION_BTN_HOVER = new Color(0.28f, 0.32f, 0.40f, 1f);

        // ── Phase order shown in the tab strip (matches DayNightCycle's
        //    natural day flow rather than enum declaration order). ─────────────
        private static readonly DayNightCycle.DayPhase[] PHASE_ORDER = new[]
        {
            DayNightCycle.DayPhase.Dawn,
            DayNightCycle.DayPhase.GoldenMorning,
            DayNightCycle.DayPhase.Day,
            DayNightCycle.DayPhase.GoldenEvening,
            DayNightCycle.DayPhase.Dusk,
            DayNightCycle.DayPhase.BlueHour,
            DayNightCycle.DayPhase.Night,
        };

        private static readonly string[] PHASE_LABELS = new[]
        {
            "Amanecer",
            "Dorada AM",
            "Día",
            "Dorada PM",
            "Atardecer",
            "Hora azul",
            "Noche",
        };

        // ── Slider definitions ───────────────────────────────────────────────
        private const int IDX_HUE     = 0;
        private const int IDX_SAT     = 1;
        private const int IDX_BRIGHT  = 2;
        private const int IDX_WARMTH  = 3;
        private const int IDX_VIGN    = 4;
        private const int SLIDER_COUNT = 5;

        private static readonly string[] SLIDER_LABELS = { "Tono", "Saturación", "Brillo", "Calidez", "Viñeta" };
        private static readonly float[]  SLIDER_MINS   = {  0f,    0f,           0f,       -1f,       0f    };
        private static readonly float[]  SLIDER_MAXS   = {  360f,  1f,           1.5f,      1f,       1f    };
        private static readonly string[] SLIDER_FORMAT = { "{0:0}°", "{0:0.00}",  "{0:0.00}", "{0:+0.00;-0.00;0.00}", "{0:0.00}" };

        // ── State ────────────────────────────────────────────────────────────
        private Canvas         _canvas;
        private RectTransform  _root;
        private Image[]        _tabImgs;
        private TextMeshProUGUI _nameTmp;
        private Image          _swatchImg;
        private Slider[]       _sliders   = new Slider[SLIDER_COUNT];
        private TextMeshProUGUI[] _values = new TextMeshProUGUI[SLIDER_COUNT];
        private int  _selectedIdx;
        private bool _suppressEvents;
        private bool _uiBuilt;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public void ToggleVisible()
        {
            if (!_uiBuilt) { BuildUI(); _uiBuilt = true; }
            _root.gameObject.SetActive(!_root.gameObject.activeSelf);
            if (_root.gameObject.activeSelf) RefreshAllForSelectedPhase();
        }

        public void Show()
        {
            if (!_uiBuilt) { BuildUI(); _uiBuilt = true; }
            _root.gameObject.SetActive(true);
            RefreshAllForSelectedPhase();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Start()
        {
            // Hidden until the gear button on FASES requests Show / Toggle.
            // Don't build UI eagerly so the canvas + raycaster don't intercept
            // gameplay clicks for a HUD piece nobody opened.
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("DayNightPhaseSettingsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 106;   // sits above the row of HUD panels at 105

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            float panelHeight = PANEL_PAD * 2f
                              + TITLE_H + 4f
                              + TAB_ROW_H + 6f
                              + NAME_ROW_H + 4f
                              + SLIDER_COUNT * (SLIDER_LBL_H + SLIDER_H + SLIDER_GAP)
                              + ACTION_GAP + ACTION_ROW_H;

            _root = NewUI("Root", canvasGo.transform).GetComponent<RectTransform>();
            _root.anchorMin        = new Vector2(1f, 1f);
            _root.anchorMax        = new Vector2(1f, 1f);
            _root.pivot            = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT, -MARGIN_TOP);
            _root.sizeDelta        = new Vector2(PANEL_WIDTH, panelHeight);

            var bg = _root.gameObject.AddComponent<Image>();
            bg.color         = BG_PANEL;
            bg.raycastTarget = true;

            // Title
            var title = AddText(_root, "Title", "AJUSTES DE FASE", 9f, FontStyles.Bold | FontStyles.UpperCase);
            title.alignment        = TextAlignmentOptions.Center;
            title.color            = TITLE_COLOR;
            title.characterSpacing = 2f;
            var titleRt            = title.rectTransform;
            titleRt.anchorMin      = new Vector2(0f, 1f);
            titleRt.anchorMax      = new Vector2(1f, 1f);
            titleRt.pivot          = new Vector2(0.5f, 1f);
            titleRt.sizeDelta      = new Vector2(0f, TITLE_H);
            titleRt.anchoredPosition = new Vector2(0f, -PANEL_PAD);

            // Tab strip
            float yCursor = -PANEL_PAD - TITLE_H - 4f;
            BuildTabRow(yCursor);
            yCursor -= TAB_ROW_H + 6f;

            // Selected-phase name + swatch
            BuildNameRow(yCursor);
            yCursor -= NAME_ROW_H + 4f;

            // 5 sliders
            for (int i = 0; i < SLIDER_COUNT; i++)
            {
                BuildSliderRow(i, yCursor);
                yCursor -= SLIDER_LBL_H + SLIDER_H + SLIDER_GAP;
            }

            // Reset button
            yCursor -= ACTION_GAP - SLIDER_GAP;
            BuildResetRow(yCursor);

            // Default selection (first tab)
            SetSelectedTab(0);
        }

        private void BuildTabRow(float yTop)
        {
            var rowGo = NewUI("TabRow", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, TAB_ROW_H);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 2f;
            hlg.childAlignment         = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            _tabImgs = new Image[PHASE_ORDER.Length];
            for (int i = 0; i < PHASE_ORDER.Length; i++)
            {
                int captured = i;
                var phase   = PHASE_ORDER[i];
                var tabGo   = NewUI($"Tab_{phase}", rowGo.transform);
                var tabImg  = tabGo.AddComponent<Image>();
                tabImg.color         = TAB_OFF;
                tabImg.raycastTarget = true;
                _tabImgs[i]          = tabImg;

                var btn = tabGo.AddComponent<Button>();
                var c   = btn.colors;
                c.normalColor      = TAB_OFF;
                c.highlightedColor = new Color(0.20f, 0.22f, 0.30f, 1f);
                c.pressedColor     = TAB_ON;
                c.selectedColor    = TAB_OFF;
                c.fadeDuration     = 0.05f;
                btn.colors         = c;
                btn.targetGraphic  = tabImg;
                btn.onClick.AddListener(() => SetSelectedTab(captured));

                // Tab number (1..7) so the tabs stay readable when very narrow.
                var lbl = AddText(tabGo.transform, "Lbl", (i + 1).ToString(), 10f, FontStyles.Bold);
                lbl.alignment       = TextAlignmentOptions.Center;
                lbl.color           = LABEL_COLOR;
                lbl.raycastTarget   = false;
                var lblRt           = lbl.rectTransform;
                lblRt.anchorMin     = Vector2.zero;
                lblRt.anchorMax     = Vector2.one;
                lblRt.offsetMin     = Vector2.zero;
                lblRt.offsetMax     = Vector2.zero;
            }
        }

        private void BuildNameRow(float yTop)
        {
            var rowGo = NewUI("NameRow", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, NAME_ROW_H);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.padding                = new RectOffset(4, 4, 0, 0);
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var swGo = NewUI("Swatch", rowGo.transform);
            swGo.AddComponent<LayoutElement>().preferredWidth = NAME_ROW_H - 4f;
            _swatchImg       = swGo.AddComponent<Image>();
            _swatchImg.color = Color.white;

            var nameGo = NewUI("Name", rowGo.transform);
            nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            _nameTmp.text     = "—";
            _nameTmp.fontSize = 12f;
            _nameTmp.fontStyle = FontStyles.Bold;
            _nameTmp.color    = VALUE_COLOR;
            _nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            _nameTmp.enableWordWrapping = false;
            _nameTmp.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void BuildSliderRow(int idx, float yTop)
        {
            // Label + value display row
            var lblGo = NewUI($"Slider_{idx}_Lbl", _root);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin        = new Vector2(0f, 1f);
            lblRt.anchorMax        = new Vector2(1f, 1f);
            lblRt.pivot            = new Vector2(0.5f, 1f);
            lblRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, SLIDER_LBL_H);
            lblRt.anchoredPosition = new Vector2(0f, yTop);

            var hlg = lblGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var lblNameGo = NewUI("Name", lblGo.transform);
            lblNameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTmp     = lblNameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text    = SLIDER_LABELS[idx];
            nameTmp.fontSize = 10f;
            nameTmp.color   = LABEL_COLOR;
            nameTmp.alignment = TextAlignmentOptions.MidlineLeft;
            nameTmp.raycastTarget = false;

            var lblValGo = NewUI("Val", lblGo.transform);
            lblValGo.AddComponent<LayoutElement>().preferredWidth = 60f;
            var valTmp     = lblValGo.AddComponent<TextMeshProUGUI>();
            valTmp.text    = "—";
            valTmp.fontSize = 10f;
            valTmp.fontStyle = FontStyles.Bold;
            valTmp.color   = VALUE_COLOR;
            valTmp.alignment = TextAlignmentOptions.MidlineRight;
            valTmp.raycastTarget = false;
            _values[idx]   = valTmp;

            // Slider row, just below the label row.
            var sliderHostGo = NewUI($"Slider_{idx}", _root);
            var sliderHostRt = sliderHostGo.GetComponent<RectTransform>();
            sliderHostRt.anchorMin        = new Vector2(0f, 1f);
            sliderHostRt.anchorMax        = new Vector2(1f, 1f);
            sliderHostRt.pivot            = new Vector2(0.5f, 1f);
            sliderHostRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, SLIDER_H);
            sliderHostRt.anchoredPosition = new Vector2(0f, yTop - SLIDER_LBL_H);

            // Track
            var trackGo = NewUI("Track", sliderHostGo.transform);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin        = new Vector2(0f, 0.5f);
            trackRt.anchorMax        = new Vector2(1f, 0.5f);
            trackRt.pivot            = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta        = new Vector2(0f, 1.5f);
            trackRt.anchoredPosition = Vector2.zero;
            var trackImg             = trackGo.AddComponent<Image>();
            trackImg.color           = TRACK_COLOR;
            trackImg.raycastTarget   = false;

            // Slide area + handle (Unity Slider expects a child structure)
            var slideAreaGo = NewUI("HandleSlideArea", sliderHostGo.transform);
            var slideAreaRt = slideAreaGo.GetComponent<RectTransform>();
            slideAreaRt.anchorMin = new Vector2(0f, 0f);
            slideAreaRt.anchorMax = new Vector2(1f, 1f);
            slideAreaRt.offsetMin = Vector2.zero;
            slideAreaRt.offsetMax = Vector2.zero;

            var handleGo = NewUI("Handle", slideAreaGo.transform);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(10f, 12f);
            var handleImg          = handleGo.AddComponent<Image>();
            handleImg.color        = HANDLE_COLOR;
            handleImg.raycastTarget = true;

            var slider = sliderHostGo.AddComponent<Slider>();
            slider.fillRect      = null;
            slider.handleRect    = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.minValue      = SLIDER_MINS[idx];
            slider.maxValue      = SLIDER_MAXS[idx];
            slider.wholeNumbers  = false;
            slider.value         = SLIDER_MINS[idx];

            int captured = idx;
            slider.onValueChanged.AddListener(v => OnSliderChanged(captured, v));
            _sliders[idx] = slider;
        }

        private void BuildResetRow(float yTop)
        {
            var rowGo = NewUI("ResetRow", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, ACTION_ROW_H);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            var img = rowGo.AddComponent<Image>();
            img.color         = ACTION_BTN;
            img.raycastTarget = true;

            var btn = rowGo.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = ACTION_BTN;
            c.highlightedColor = ACTION_BTN_HOVER;
            c.pressedColor     = TAB_ON;
            c.selectedColor    = ACTION_BTN;
            c.fadeDuration     = 0.05f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(OnResetClicked);

            var lbl = AddText(rowGo.transform, "Lbl", "RESET FASE ACTUAL", 10f, FontStyles.Bold);
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.color            = LABEL_COLOR;
            lbl.characterSpacing = 1.5f;
            var lblRt            = lbl.rectTransform;
            lblRt.anchorMin      = Vector2.zero;
            lblRt.anchorMax      = Vector2.one;
            lblRt.offsetMin      = Vector2.zero;
            lblRt.offsetMax      = Vector2.zero;
            lbl.raycastTarget    = false;
        }

        // ── Interaction ──────────────────────────────────────────────────────

        private void SetSelectedTab(int idx)
        {
            _selectedIdx = Mathf.Clamp(idx, 0, PHASE_ORDER.Length - 1);
            for (int i = 0; i < _tabImgs.Length; i++)
            {
                bool on = i == _selectedIdx;
                _tabImgs[i].color = on ? TAB_ON : TAB_OFF;
                var btn = _tabImgs[i].GetComponent<Button>();
                var c   = btn.colors;
                c.normalColor   = on ? TAB_ON : TAB_OFF;
                c.selectedColor = on ? TAB_ON : TAB_OFF;
                btn.colors      = c;
            }
            RefreshAllForSelectedPhase();
        }

        // Pull the currently-selected phase's PhaseLook from the cycle and
        // push the values into the UI without retriggering OnSliderChanged.
        private void RefreshAllForSelectedPhase()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null || _sliders[0] == null) return;

            var phase = PHASE_ORDER[_selectedIdx];
            var look  = cycle.GetPhaseLook(phase);

            if (_nameTmp != null)   _nameTmp.text   = PHASE_LABELS[_selectedIdx];
            if (_swatchImg != null) _swatchImg.color = new Color(look.color.r, look.color.g, look.color.b, 1f);

            // Decompose the color into HSV — Hue and Saturation are the two
            // sliders we expose; Value is folded into the Brightness slider so
            // a player who pumps Brightness up sees the world get brighter
            // without altering the hue family.
            Color.RGBToHSV(look.color, out float h, out float s, out _);

            _suppressEvents = true;
            try
            {
                _sliders[IDX_HUE].value     = h * 360f;
                _sliders[IDX_SAT].value     = s;
                _sliders[IDX_BRIGHT].value  = look.intensity;
                _sliders[IDX_WARMTH].value  = look.warmth;
                _sliders[IDX_VIGN].value    = look.vignetteAlpha;
            }
            finally { _suppressEvents = false; }

            RefreshValueLabels();
        }

        private void OnSliderChanged(int sliderIdx, float v)
        {
            if (_suppressEvents) return;
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;

            var phase = PHASE_ORDER[_selectedIdx];
            var look  = cycle.GetPhaseLook(phase);

            // Recompose color from current slider state for any color-affecting
            // change (Hue / Sat). Other sliders just write their field directly.
            switch (sliderIdx)
            {
                case IDX_HUE:
                {
                    Color.RGBToHSV(look.color, out _, out float s, out _);
                    look.color = Color.HSVToRGB(Mathf.Clamp01(v / 360f), s, 1f);
                    break;
                }
                case IDX_SAT:
                {
                    Color.RGBToHSV(look.color, out float h, out _, out _);
                    look.color = Color.HSVToRGB(h, Mathf.Clamp01(v), 1f);
                    break;
                }
                case IDX_BRIGHT: look.intensity     = Mathf.Clamp(v, 0f, 1.5f);  break;
                case IDX_WARMTH: look.warmth        = Mathf.Clamp(v, -1f, 1f);    break;
                case IDX_VIGN:   look.vignetteAlpha = Mathf.Clamp01(v);           break;
            }

            cycle.SetPhaseLook(phase, look);
            if (_swatchImg != null && (sliderIdx == IDX_HUE || sliderIdx == IDX_SAT))
                _swatchImg.color = new Color(look.color.r, look.color.g, look.color.b, 1f);
            RefreshValueLabels();
        }

        private void RefreshValueLabels()
        {
            for (int i = 0; i < SLIDER_COUNT; i++)
            {
                if (_values[i] == null || _sliders[i] == null) continue;
                _values[i].text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    SLIDER_FORMAT[i],
                    _sliders[i].value);
            }
        }

        // Reset the active phase to the *prefab* defaults baked into
        // DayNightCycle (the SerializeField defaults from the original
        // ComputePhaseAndColor model). Cheap to maintain because the defaults
        // live in one place and we always know what they should be.
        private void OnResetClicked()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            var phase = PHASE_ORDER[_selectedIdx];
            cycle.SetPhaseLook(phase, DefaultLookFor(phase));
            RefreshAllForSelectedPhase();
        }

        private static DayNightCycle.PhaseLook DefaultLookFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn          => new DayNightCycle.PhaseLook { color = new Color(0.74f, 0.76f, 0.86f), intensity = 0.55f, warmth = -0.20f, vignetteAlpha = 0.22f },
            DayNightCycle.DayPhase.GoldenMorning => new DayNightCycle.PhaseLook { color = new Color(1.00f, 0.86f, 0.70f), intensity = 0.85f, warmth =  0.45f, vignetteAlpha = 0.35f },
            DayNightCycle.DayPhase.Day           => new DayNightCycle.PhaseLook { color = new Color(0.97f, 0.97f, 0.95f), intensity = 1.00f, warmth =  0.05f, vignetteAlpha = 0.05f },
            DayNightCycle.DayPhase.GoldenEvening => new DayNightCycle.PhaseLook { color = new Color(1.00f, 0.78f, 0.58f), intensity = 0.85f, warmth =  0.55f, vignetteAlpha = 0.40f },
            DayNightCycle.DayPhase.Dusk          => new DayNightCycle.PhaseLook { color = new Color(0.86f, 0.62f, 0.55f), intensity = 0.60f, warmth =  0.30f, vignetteAlpha = 0.42f },
            DayNightCycle.DayPhase.BlueHour      => new DayNightCycle.PhaseLook { color = new Color(0.45f, 0.52f, 0.78f), intensity = 0.45f, warmth = -0.55f, vignetteAlpha = 0.36f },
            DayNightCycle.DayPhase.Night         => new DayNightCycle.PhaseLook { color = new Color(0.28f, 0.34f, 0.55f), intensity = 0.35f, warmth = -0.40f, vignetteAlpha = 0.30f },
            _                                     => new DayNightCycle.PhaseLook { color = Color.white, intensity = 1f, warmth = 0f, vignetteAlpha = 0.05f },
        };

        // ── Helpers ──────────────────────────────────────────────────────────

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, string text,
            float fontSize, FontStyles style)
        {
            var go  = NewUI(name, parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = text;
            tmp.fontSize      = fontSize;
            tmp.fontStyle     = style;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
