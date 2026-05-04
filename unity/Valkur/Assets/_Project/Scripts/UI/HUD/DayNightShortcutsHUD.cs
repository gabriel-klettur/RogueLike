using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Vertical button column that lets the player jump the day/night cycle to
    /// any of the canonical phase entry points (Dawn, Golden Hour, Noon, …).
    /// Anchored to the right of the screen, immediately left of
    /// <see cref="DayNightClockHUD"/>.
    ///
    /// Each row pairs a colored phase swatch with a localised label and the
    /// HH:MM the click will jump to. Driving <see cref="DayNightCycle.SetTimeNormalized(float)"/>
    /// keeps the lighting + every downstream phase consumer (HUD ring, vignette,
    /// particles, ambient audio) in sync without bespoke wiring.
    /// </summary>
    public sealed class DayNightShortcutsHUD : MonoBehaviour
    {
        // ── Layout ───────────────────────────────────────────────────────────
        // Horizontal offset = clock margin + clock width + small gap so the
        // panel sits one widget over from the screen edge.
        private const float MARGIN_RIGHT_FROM_CLOCK = 24f + 110f + 10f; // = 144
        private const float MARGIN_TOP              = 24f;
        private const float PANEL_WIDTH             = 132f;
        private const float ROW_HEIGHT              = 26f;
        private const float ROW_SPACING             = 3f;
        private const float PANEL_PAD               = 8f;
        private const float OFF_ROW_H               = 22f;
        private const float OFF_ROW_GAP             = 6f;

        // ── Palette ──────────────────────────────────────────────────────────
        private static readonly Color BG_PANEL       = new Color(0.04f, 0.05f, 0.08f, 0.65f);
        private static readonly Color ROW_BG         = new Color(0.10f, 0.12f, 0.18f, 0.85f);
        private static readonly Color ROW_BG_HOVER   = new Color(0.18f, 0.20f, 0.28f, 0.95f);
        private static readonly Color ROW_BG_PRESS   = new Color(0.95f, 0.78f, 0.40f, 1.00f);
        private static readonly Color LABEL_COLOR    = new Color(0.92f, 0.94f, 0.98f, 1.00f);
        private static readonly Color HOUR_COLOR     = new Color(0.78f, 0.82f, 0.88f, 0.85f);
        private static readonly Color TITLE_COLOR    = new Color(0.85f, 0.88f, 0.95f, 0.85f);
        private static readonly Color OFF_ROW_ON     = new Color(0.55f, 0.58f, 0.65f, 1f);
        private static readonly Color OFF_LABEL_ON   = new Color(0.10f, 0.10f, 0.12f, 1f);

        // ── Phase entries ────────────────────────────────────────────────────
        // Each entry is (label, normalized t to jump to, swatch color).
        // Times chosen to land in the middle of each phase's window (per the
        // 6-phase cinematic boundaries in DayNightCycle.ComputePhaseAndColor).
        private struct Shortcut
        {
            public string label;
            public string hourText;   // "HH:MM" for the trailing hint
            public float  normalizedTime;
            public Color  swatch;

            public Shortcut(string label, int hour, int minute, Color swatch)
            {
                this.label          = label;
                this.hourText       = $"{hour:D2}:{minute:D2}";
                this.normalizedTime = (hour * 60f + minute) / DayNightCycle.MinutesPerDay;
                this.swatch         = swatch;
            }
        }

        // Eight presets covering the full cinematic 24h. Times kept inside
        // their phase windows so the click visibly lands on the right tint.
        private static readonly Shortcut[] SHORTCUTS = new[]
        {
            new Shortcut("Amanecer",     4, 30, new Color(0.78f, 0.78f, 0.92f)),
            new Shortcut("Hora dorada",  6,  0, new Color(1.00f, 0.85f, 0.55f)),
            new Shortcut("Mañana",       9,  0, new Color(0.97f, 0.97f, 0.95f)),
            new Shortcut("Mediodía",    12,  0, new Color(1.00f, 1.00f, 0.95f)),
            new Shortcut("Hora dorada", 17,  0, new Color(1.00f, 0.72f, 0.45f)),
            new Shortcut("Atardecer",   18, 30, new Color(0.86f, 0.62f, 0.55f)),
            new Shortcut("Hora azul",   19, 30, new Color(0.45f, 0.52f, 0.78f)),
            new Shortcut("Medianoche",   0,  0, new Color(0.28f, 0.34f, 0.55f)),
        };

        // ── State ────────────────────────────────────────────────────────────
        private Canvas         _canvas;
        private RectTransform  _root;
        private int            _activeIdx = -1;
        private Image[]        _rowBgs;
        private TextMeshProUGUI[] _rowLabels;
        private Image            _offRowImg;
        private TextMeshProUGUI  _offRowTmp;

        private void Start()
        {
            BuildUI();
        }

        private void Update()
        {
            // Track the active phase entry so the highlight matches the live
            // cycle (works when the time was set from anywhere — Lighting
            // Editor, this panel, or a save load).
            var cycle = DayNightCycle.Instance;
            if (cycle == null || _rowBgs == null) return;

            // When LightingEnabled is OFF, no phase row is "current" — the
            // OFF row owns the highlight instead.
            if (!cycle.LightingEnabled)
            {
                if (_activeIdx != -1) SetActive(-1);
                RepaintOffRow(true);
                return;
            }

            int nearest = NearestShortcutIdx(cycle.TimeNormalized);
            if (nearest != _activeIdx) SetActive(nearest);
            RepaintOffRow(false);
        }

        private void RepaintOffRow(bool on)
        {
            if (_offRowImg == null) return;
            _offRowImg.color = on ? OFF_ROW_ON : ROW_BG;
            if (_offRowTmp != null)
                _offRowTmp.color = on ? OFF_LABEL_ON : LABEL_COLOR;
            var btn = _offRowImg.GetComponent<Button>();
            var c = btn.colors;
            c.normalColor   = on ? OFF_ROW_ON : ROW_BG;
            c.selectedColor = on ? OFF_ROW_ON : ROW_BG;
            btn.colors      = c;
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("DayNightShortcutsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            float panelHeight = PANEL_PAD * 2f
                              + 18f                       // title row
                              + 4f                        // gap below title
                              + (ROW_HEIGHT * SHORTCUTS.Length)
                              + (ROW_SPACING * (SHORTCUTS.Length - 1))
                              + OFF_ROW_GAP + OFF_ROW_H; // trailing OFF row

            _root = NewUI("Root", canvasGo.transform).GetComponent<RectTransform>();
            _root.anchorMin        = new Vector2(1f, 1f);
            _root.anchorMax        = new Vector2(1f, 1f);
            _root.pivot            = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT_FROM_CLOCK, -MARGIN_TOP);
            _root.sizeDelta        = new Vector2(PANEL_WIDTH, panelHeight);

            // BG panel
            var bg = AddImage(_root, "Bg", null);
            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            bg.color = BG_PANEL;
            bg.raycastTarget = true;     // catches stray clicks; doesn't propagate to gameplay

            // Title (centered) + small AJUSTES gear button anchored top-right
            // of the title row. The button toggles the floating phase-settings
            // panel, which lets the player tune the 5 properties of any phase.
            var titleTmp                          = AddText(_root, "Title", "FASES", 9f, FontStyles.Bold | FontStyles.UpperCase);
            titleTmp.alignment                    = TextAlignmentOptions.Center;
            titleTmp.color                        = TITLE_COLOR;
            titleTmp.characterSpacing             = 2f;
            var titleRt                           = titleTmp.rectTransform;
            titleRt.anchorMin                     = new Vector2(0f, 1f);
            titleRt.anchorMax                     = new Vector2(1f, 1f);
            titleRt.pivot                         = new Vector2(0.5f, 1f);
            titleRt.sizeDelta                     = new Vector2(0f, 18f);
            titleRt.anchoredPosition              = new Vector2(0f, -PANEL_PAD);

            BuildSettingsButton(titleRt);

            // Rows
            _rowBgs    = new Image[SHORTCUTS.Length];
            _rowLabels = new TextMeshProUGUI[SHORTCUTS.Length];
            float yCursor = -PANEL_PAD - 18f - 4f;
            for (int i = 0; i < SHORTCUTS.Length; i++)
            {
                BuildRow(i, yCursor);
                yCursor -= ROW_HEIGHT + ROW_SPACING;
            }

            // OFF row at the bottom — disables the global Light2D tint and
            // pauses the cycle so the world reads at native colors. Clicking
            // any phase row above re-enables both. Visually separated from the
            // phase group with a small gap so it's read as a "global" action.
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
            _offRowImg.color         = ROW_BG;
            _offRowImg.raycastTarget = true;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = OFF_ROW_ON;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = _offRowImg;
            btn.onClick.AddListener(OnOffClicked);

            _offRowTmp                = AddText(rowGo.transform, "Lbl", "OFF · SIN FILTRO", 10f, FontStyles.Bold);
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
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            cycle.LightingEnabled = false;
            cycle.Paused          = true;
            // Drop any phase-row highlight; the OFF row owns the visual focus
            // until the user picks a phase shortcut again.
            SetActive(-1);
            RepaintOffRow(true);
        }

        private void BuildRow(int idx, float yTop)
        {
            int captured = idx;
            var s = SHORTCUTS[idx];

            var rowGo = NewUI($"Row_{idx}", _root);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin        = new Vector2(0f, 1f);
            rowRt.anchorMax        = new Vector2(1f, 1f);
            rowRt.pivot            = new Vector2(0.5f, 1f);
            rowRt.sizeDelta        = new Vector2(-PANEL_PAD * 2f, ROW_HEIGHT);
            rowRt.anchoredPosition = new Vector2(0f, yTop);

            var img = rowGo.AddComponent<Image>();
            img.color         = ROW_BG;
            img.raycastTarget = true;
            _rowBgs[idx]      = img;

            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = ROW_BG;
            c.highlightedColor = ROW_BG_HOVER;
            c.pressedColor     = ROW_BG_PRESS;
            c.selectedColor    = ROW_BG;
            c.fadeDuration     = 0.06f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(() => OnRowClicked(captured));

            // Phase swatch (left edge): a thin colored vertical bar.
            var swGo = NewUI("Swatch", rowGo.transform);
            var swRt = swGo.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(0f, 0f);
            swRt.anchorMax = new Vector2(0f, 1f);
            swRt.pivot     = new Vector2(0f, 0.5f);
            swRt.sizeDelta = new Vector2(4f, 0f);
            swRt.anchoredPosition = Vector2.zero;
            var swImg     = swGo.AddComponent<Image>();
            swImg.color   = s.swatch;
            swImg.raycastTarget = false;

            // Label
            var lblGo = NewUI("Label", rowGo.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.offsetMin = new Vector2(10f, 0f);
            lblRt.offsetMax = new Vector2(-44f, 0f);
            var lblTmp     = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text    = s.label;
            lblTmp.fontSize = 11f;
            lblTmp.fontStyle = FontStyles.Bold;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.color   = LABEL_COLOR;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode = TextOverflowModes.Ellipsis;
            lblTmp.raycastTarget = false;
            _rowLabels[idx] = lblTmp;

            // Hour suffix
            var hrGo = NewUI("Hour", rowGo.transform);
            var hrRt = hrGo.GetComponent<RectTransform>();
            hrRt.anchorMin = new Vector2(1f, 0f);
            hrRt.anchorMax = new Vector2(1f, 1f);
            hrRt.pivot     = new Vector2(1f, 0.5f);
            hrRt.sizeDelta = new Vector2(40f, 0f);
            hrRt.anchoredPosition = new Vector2(-6f, 0f);
            var hrTmp     = hrGo.AddComponent<TextMeshProUGUI>();
            hrTmp.text    = s.hourText;
            hrTmp.fontSize = 9f;
            hrTmp.alignment = TextAlignmentOptions.MidlineRight;
            hrTmp.color   = HOUR_COLOR;
            hrTmp.enableWordWrapping = false;
            hrTmp.raycastTarget = false;
        }

        // ── Interaction ──────────────────────────────────────────────────────

        private void BuildSettingsButton(RectTransform titleRt)
        {
            // Small "AJUSTES" pill anchored to the right edge of the title row.
            // The text intentionally reads as a verb so the player understands
            // it opens a deeper settings UI rather than navigating somewhere.
            var btnGo = NewUI("SettingsBtn", _root);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin        = new Vector2(1f, 1f);
            btnRt.anchorMax        = new Vector2(1f, 1f);
            btnRt.pivot            = new Vector2(1f, 1f);
            btnRt.sizeDelta        = new Vector2(54f, 16f);
            btnRt.anchoredPosition = new Vector2(-PANEL_PAD, -PANEL_PAD - 1f);

            var img = btnGo.AddComponent<Image>();
            img.color         = new Color(0.18f, 0.20f, 0.28f, 0.95f);
            img.raycastTarget = true;

            var btn = btnGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = new Color(0.18f, 0.20f, 0.28f, 0.95f);
            c.highlightedColor = new Color(0.28f, 0.32f, 0.40f, 1f);
            c.pressedColor     = OFF_ROW_ON;
            c.selectedColor    = new Color(0.18f, 0.20f, 0.28f, 0.95f);
            c.fadeDuration     = 0.05f;
            btn.colors         = c;
            btn.targetGraphic  = img;
            btn.onClick.AddListener(OnSettingsClicked);

            var lbl = AddText(btnGo.transform, "Lbl", "⚙ AJ", 9f, FontStyles.Bold);
            lbl.alignment       = TextAlignmentOptions.Center;
            lbl.color           = TITLE_COLOR;
            lbl.characterSpacing = 1f;
            lbl.raycastTarget   = false;
            var lblRt           = lbl.rectTransform;
            lblRt.anchorMin     = Vector2.zero;
            lblRt.anchorMax     = Vector2.one;
            lblRt.offsetMin     = Vector2.zero;
            lblRt.offsetMax     = Vector2.zero;
        }

        private void OnSettingsClicked()
        {
            var settings = FindObjectOfType<DayNightPhaseSettingsHUD>();
            if (settings == null) return;
            settings.ToggleVisible();
        }

        private void OnRowClicked(int idx)
        {
            var cycle = DayNightCycle.Instance;
            if (cycle == null) return;
            // Picking a phase row implicitly re-enables lighting + resumes the
            // cycle so the player sees the tint they just asked for.
            cycle.LightingEnabled = true;
            cycle.Paused          = false;
            cycle.SetTimeNormalized(SHORTCUTS[idx].normalizedTime);
            SetActive(idx);
            RepaintOffRow(false);
        }

        private int NearestShortcutIdx(float t)
        {
            // Wrap-aware distance on the unit circle so the row that
            // includes 23:30 still wins when the clock reads 00:05.
            int    best = 0;
            float  bestD = float.PositiveInfinity;
            for (int i = 0; i < SHORTCUTS.Length; i++)
            {
                float d = Mathf.Abs(SHORTCUTS[i].normalizedTime - t);
                if (d > 0.5f) d = 1f - d;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void SetActive(int idx)
        {
            if (idx == _activeIdx) return;
            _activeIdx = idx;
            for (int i = 0; i < _rowBgs.Length; i++)
            {
                if (_rowBgs[i] == null) continue;
                bool on = i == idx;
                _rowBgs[i].color = on ? ROW_BG_PRESS : ROW_BG;
                if (_rowLabels[i] != null)
                    _rowLabels[i].color = on ? new Color(0.10f, 0.10f, 0.12f, 1f) : LABEL_COLOR;
                var btn = _rowBgs[i].GetComponent<Button>();
                var c = btn.colors;
                c.normalColor   = on ? ROW_BG_PRESS : ROW_BG;
                c.selectedColor = on ? ROW_BG_PRESS : ROW_BG;
                btn.colors = c;
            }
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
