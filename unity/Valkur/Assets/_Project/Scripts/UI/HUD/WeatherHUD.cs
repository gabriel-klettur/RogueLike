using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World.Weather;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Panel of weather toggles anchored top-right, immediately left of
    /// <see cref="DayNightShortcutsHUD"/>. Each toggle drives the matching
    /// <see cref="WeatherManager.Set"/> entry and reflects the live state via
    /// the manager's <see cref="WeatherManager.OnWeatherChanged"/> event.
    ///
    /// Effects can stack — Wind + Rain reads as a wind-driven storm, Snow + Wind
    /// gives a blizzard.
    /// </summary>
    public sealed class WeatherHUD : MonoBehaviour
    {
        // ── Layout ───────────────────────────────────────────────────────────
        // Anchored further right-edge offset = clock margin + clock + gap
        // + shortcut panel + gap, so the three panels sit in a tidy row.
        private const float MARGIN_RIGHT = 24f + 110f + 10f + 132f + 10f; // = 286
        private const float MARGIN_TOP   = 24f;
        private const float PANEL_WIDTH  = 124f;
        private const float ROW_HEIGHT   = 36f;
        private const float ROW_SPACING  = 4f;
        private const float PANEL_PAD    = 8f;
        private const float TITLE_H      = 18f;
        private const float OFF_ROW_H    = 22f;
        private const float OFF_ROW_GAP  = 6f;

        // ── Palette ──────────────────────────────────────────────────────────
        private static readonly Color BG_PANEL     = new Color(0.04f, 0.05f, 0.08f, 0.65f);
        private static readonly Color ROW_OFF      = new Color(0.12f, 0.14f, 0.20f, 0.85f);
        private static readonly Color ROW_HOVER    = new Color(0.20f, 0.22f, 0.30f, 0.95f);
        private static readonly Color ROW_ON       = new Color(0.30f, 0.55f, 0.85f, 1.00f);
        private static readonly Color LABEL_COLOR  = new Color(0.92f, 0.94f, 0.98f, 1.00f);
        private static readonly Color TITLE_COLOR  = new Color(0.85f, 0.88f, 0.95f, 0.85f);

        // Per-weather accent — used as the swatch + ON-state background tint.
        private static readonly Color WIND_TINT  = new Color(0.78f, 0.85f, 0.95f, 1f);
        private static readonly Color RAIN_TINT  = new Color(0.45f, 0.65f, 0.95f, 1f);
        private static readonly Color SNOW_TINT  = new Color(0.95f, 0.95f, 1.00f, 1f);
        // OFF / clear-all row colors. The active state (no weathers) uses a
        // muted neutral so it reads as "this is the default, calm state",
        // distinct from the bright per-weather accents.
        private static readonly Color OFF_ON     = new Color(0.55f, 0.58f, 0.65f, 1f);
        private static readonly Color OFF_LABEL  = new Color(0.10f, 0.10f, 0.12f, 1f);

        private struct Toggle
        {
            public WeatherType type;
            public string      label;
            public string      icon;     // ASCII glyph hint, fits the project font
            public Color       accent;
        }

        private static readonly Toggle[] TOGGLES = new[]
        {
            new Toggle { type = WeatherType.Wind, label = "Viento", icon = "≈", accent = WIND_TINT },
            new Toggle { type = WeatherType.Rain, label = "Lluvia", icon = "/",  accent = RAIN_TINT },
            new Toggle { type = WeatherType.Snow, label = "Nieve",  icon = "*",  accent = SNOW_TINT },
        };

        private Canvas        _canvas;
        private RectTransform _root;
        private Image[]       _rowBgs;
        private TextMeshProUGUI[] _rowLabels;
        private Image            _offRowImg;
        private TextMeshProUGUI  _offRowTmp;

        private void Start()
        {
            BuildUI();
            // Pick up the live state in case a designer enabled weather from
            // the inspector before the panel was built.
            for (int i = 0; i < TOGGLES.Length; i++)
                Repaint(i, WeatherManager.Instance != null && WeatherManager.Instance.IsActive(TOGGLES[i].type));
            RepaintOffRow();
        }

        private void OnEnable()  => WeatherManager.OnWeatherChanged += HandleWeatherChanged;
        private void OnDisable() => WeatherManager.OnWeatherChanged -= HandleWeatherChanged;

        private void HandleWeatherChanged(WeatherType type, bool active)
        {
            for (int i = 0; i < TOGGLES.Length; i++)
                if (TOGGLES[i].type == type) { Repaint(i, active); break; }
            RepaintOffRow();
        }

        // The OFF row reads as "active" when no weather is currently running —
        // it represents the clear / calm state.
        private void RepaintOffRow()
        {
            if (_offRowImg == null) return;
            bool noneActive = true;
            if (WeatherManager.Instance != null)
            {
                for (int i = 0; i < TOGGLES.Length; i++)
                    if (WeatherManager.Instance.IsActive(TOGGLES[i].type)) { noneActive = false; break; }
            }
            _offRowImg.color = noneActive ? OFF_ON : ROW_OFF;
            if (_offRowTmp != null)
                _offRowTmp.color = noneActive ? OFF_LABEL : LABEL_COLOR;
            var btn = _offRowImg.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = noneActive ? OFF_ON : ROW_OFF;
            c.selectedColor = noneActive ? OFF_ON : ROW_OFF;
            btn.colors      = c;
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("WeatherHUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            float panelHeight = PANEL_PAD * 2f + TITLE_H + 4f
                              + ROW_HEIGHT * TOGGLES.Length
                              + ROW_SPACING * (TOGGLES.Length - 1)
                              + OFF_ROW_GAP + OFF_ROW_H;

            _root = NewUI("Root", canvasGo.transform).GetComponent<RectTransform>();
            _root.anchorMin        = new Vector2(1f, 1f);
            _root.anchorMax        = new Vector2(1f, 1f);
            _root.pivot            = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT, -MARGIN_TOP);
            _root.sizeDelta        = new Vector2(PANEL_WIDTH, panelHeight);

            // BG
            var bg = AddImage(_root, "Bg", null);
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bg.color = BG_PANEL;
            bg.raycastTarget = true;

            // Title
            var titleTmp                          = AddText(_root, "Title", "CLIMA", 9f, FontStyles.Bold | FontStyles.UpperCase);
            titleTmp.alignment                    = TextAlignmentOptions.Center;
            titleTmp.color                        = TITLE_COLOR;
            titleTmp.characterSpacing             = 2f;
            var titleRt                           = titleTmp.rectTransform;
            titleRt.anchorMin                     = new Vector2(0f, 1f);
            titleRt.anchorMax                     = new Vector2(1f, 1f);
            titleRt.pivot                         = new Vector2(0.5f, 1f);
            titleRt.sizeDelta                     = new Vector2(0f, TITLE_H);
            titleRt.anchoredPosition              = new Vector2(0f, -PANEL_PAD);

            _rowBgs    = new Image[TOGGLES.Length];
            _rowLabels = new TextMeshProUGUI[TOGGLES.Length];
            float yCursor = -PANEL_PAD - TITLE_H - 4f;
            for (int i = 0; i < TOGGLES.Length; i++)
            {
                BuildRow(i, yCursor);
                yCursor -= ROW_HEIGHT + ROW_SPACING;
            }

            // OFF row at the bottom — clears every active weather. Visually
            // sits below the toggle group with a small gap so the player
            // perceives it as a "global" action rather than a fourth weather.
            yCursor -= OFF_ROW_GAP - ROW_SPACING; // Cancel the trailing inter-row spacing.
            BuildOffRow(yCursor);
        }

        private void BuildOffRow(float yTop)
        {
            var rowGo = NewUI("Row_Off", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, OFF_ROW_H);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            _offRowImg = rowGo.AddComponent<Image>();
            _offRowImg.color         = ROW_OFF;
            _offRowImg.raycastTarget = true;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_OFF;
            c.highlightedColor = ROW_HOVER;
            c.pressedColor     = OFF_ON;
            c.selectedColor    = ROW_OFF;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = _offRowImg;
            btn.onClick.AddListener(OnOffClicked);

            _offRowTmp                = AddText(rowGo.transform, "Lbl", "OFF · DESPEJADO", 10f, FontStyles.Bold);
            _offRowTmp.alignment      = TextAlignmentOptions.Center;
            _offRowTmp.color          = LABEL_COLOR;
            _offRowTmp.characterSpacing = 1.5f;
            var lblRt                 = _offRowTmp.rectTransform;
            lblRt.anchorMin           = Vector2.zero;
            lblRt.anchorMax           = Vector2.one;
            lblRt.offsetMin           = Vector2.zero;
            lblRt.offsetMax           = Vector2.zero;
        }

        private void OnOffClicked()
        {
            if (WeatherManager.Instance != null) WeatherManager.Instance.ClearAll();
            // Repaint each toggle row immediately — ClearAll fires
            // OnWeatherChanged for each previously-active one, but a row that
            // was already off won't fire and we want them all in sync.
            for (int i = 0; i < TOGGLES.Length; i++) Repaint(i, false);
            RepaintOffRow();
        }

        private void BuildRow(int idx, float yTop)
        {
            int captured = idx;
            var t = TOGGLES[idx];

            var rowGo = NewUI($"Row_{t.type}", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, ROW_HEIGHT);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            var img = rowGo.AddComponent<Image>();
            img.color         = ROW_OFF;
            img.raycastTarget = true;
            _rowBgs[idx]      = img;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_OFF;
            c.highlightedColor = ROW_HOVER;
            c.pressedColor     = ROW_ON;
            c.selectedColor    = ROW_OFF;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(() => OnRowClicked(captured));

            // Accent swatch (left)
            var swGo = NewUI("Swatch", rowGo.transform);
            var swRt = swGo.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(0f, 0f);
            swRt.anchorMax = new Vector2(0f, 1f);
            swRt.pivot     = new Vector2(0f, 0.5f);
            swRt.sizeDelta = new Vector2(4f, 0f);
            swRt.anchoredPosition = Vector2.zero;
            var swImg     = swGo.AddComponent<Image>();
            swImg.color   = t.accent;
            swImg.raycastTarget = false;

            // Icon glyph (large, centered-left)
            var icoGo = NewUI("Icon", rowGo.transform);
            var icoRt = icoGo.GetComponent<RectTransform>();
            icoRt.anchorMin = new Vector2(0f, 0f);
            icoRt.anchorMax = new Vector2(0f, 1f);
            icoRt.pivot     = new Vector2(0f, 0.5f);
            icoRt.sizeDelta = new Vector2(28f, 0f);
            icoRt.anchoredPosition = new Vector2(8f, 0f);
            var icoTmp     = icoGo.AddComponent<TextMeshProUGUI>();
            icoTmp.text    = t.icon;
            icoTmp.fontSize = 18f;
            icoTmp.fontStyle = FontStyles.Bold;
            icoTmp.alignment = TextAlignmentOptions.Center;
            icoTmp.color   = t.accent;
            icoTmp.raycastTarget = false;

            // Label
            var lblGo = NewUI("Label", rowGo.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = new Vector2(40f, 0f);
            lblRt.offsetMax = new Vector2(-6f, 0f);
            var lblTmp     = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text    = t.label;
            lblTmp.fontSize = 12f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.color   = LABEL_COLOR;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode = TextOverflowModes.Ellipsis;
            lblTmp.raycastTarget = false;
            _rowLabels[idx] = lblTmp;
        }

        // ── Interaction ──────────────────────────────────────────────────────

        private void OnRowClicked(int idx)
        {
            if (WeatherManager.Instance == null) return;
            var t  = TOGGLES[idx];
            bool on = WeatherManager.Instance.Toggle(t.type);
            Repaint(idx, on);
        }

        private void Repaint(int idx, bool on)
        {
            if (_rowBgs == null || idx < 0 || idx >= _rowBgs.Length) return;
            var img = _rowBgs[idx];
            if (img == null) return;

            // Mix in the per-weather accent so the active row reads as "this
            // is the rain toggle" rather than just "active row #2".
            var accent = TOGGLES[idx].accent;
            var activeColor = Color.Lerp(ROW_ON, accent, 0.35f);
            img.color = on ? activeColor : ROW_OFF;
            if (_rowLabels[idx] != null)
                _rowLabels[idx].color = on ? new Color(0.10f, 0.10f, 0.12f, 1f) : LABEL_COLOR;
            var btn = img.GetComponent<Button>();
            var c   = btn.colors;
            c.normalColor   = on ? activeColor : ROW_OFF;
            c.selectedColor = on ? activeColor : ROW_OFF;
            btn.colors      = c;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite)
        {
            var go  = NewUI(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
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
