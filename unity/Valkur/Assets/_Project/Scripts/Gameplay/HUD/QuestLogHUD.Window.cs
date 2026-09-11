using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.UI;
using Valkur.UIKit;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The tracker's chrome: the bar you drag it by, the two corner controls, the grip that
    /// resizes it, and the four numbers that survive a restart.
    ///
    /// <para><b>The window is TOP-LEFT pivoted although it lives in the top-RIGHT corner</b>,
    /// and that is not an oversight. A panel grows away from its pivot and never towards it,
    /// so the pivot decides which corner a grip can be in — <c>PanelResizeHandle</c> says so in
    /// its own enum. A top-right pivot would pin the right and top edges and leave a grip able
    /// to pull only leftwards and down, which is the one gesture nobody makes. Top-left pivot,
    /// bottom-right grip, and the default POSITION is computed so it opens exactly where the
    /// old fixed panel sat.</para>
    ///
    /// <para><b>Geometry is written on the END of a gesture</b>, never per frame: these are
    /// PlayerPrefs, i.e. a file, and a drag is sixty of them a second.</para>
    /// </summary>
    public sealed partial class QuestLogHUD
    {
        /// <summary>Width the tracker opens at, matched to the top-right column it stacks under.</summary>
        private const float DEFAULT_W = 300f;

        /// <summary>
        /// Height it opens at: EXACTLY the free band between the minimap above and the space
        /// the bottom HUD reserves.
        ///
        /// <para>DERIVED rather than chosen, because a default taller than the band opens the
        /// tracker on top of the music widget — measured, a 210 px window reached 38 px into
        /// it — and this project has already paid for that once, when moving the old tracker
        /// out from under the minimap put it under the widget instead. What the PLAYER drags
        /// it to is their business; what it does unasked is not.</para>
        /// </summary>
        private const float DEFAULT_H =
            HudLayout.ReferenceHeight - HudLayout.BelowMinimapTop - HudLayout.BottomReserved;

        /// <summary>Smallest it may be dragged to: a title bar and one objective line.</summary>
        private const float MIN_W = 200f;
        private const float MIN_H = 96f;

        private const float TITLE_BAR_H = 22f;
        private const float PADDING = 6f;

        /// <summary>Side of the two square controls in the title bar.</summary>
        private const float CHROME_BTN = 16f;

        /// <summary>Side of the resize grip's hit box in the bottom-right corner.</summary>
        private const float GRIP = 14f;

        private const string PrefKeyX         = "valkur.questlog.x";
        private const string PrefKeyY         = "valkur.questlog.y";
        private const string PrefKeyW         = "valkur.questlog.width";
        private const string PrefKeyH         = "valkur.questlog.height";
        private const string PrefKeyMinimized = "valkur.questlog.minimized";
        private const string PrefKeyClosed    = "valkur.questlog.closed";

        private Canvas _canvas;
        private GameObject _root;
        private RectTransform _windowRect;
        private GameObject _body;
        private RectTransform _rowsContent;
        private ScrollRect _scroll;
        private TextMeshProUGUI _titleLabel;
        private Button _minimizeButton;
        private TextMeshProUGUI _minimizeGlyph;
        private PanelResizeHandle _resizeHandle;

        private bool _prefsLoaded;
        private bool _minimized;
        private bool _closed;
        private Vector2 _windowPos;
        private Vector2 _windowSize;

        /// <summary>True while the tracker is on screen at all. Read by the Quest Editor.</summary>
        public bool IsWindowVisible => !_closed;

        /// <summary>True while it is collapsed to its title bar.</summary>
        public bool IsMinimized => _minimized;

        /// <summary>Where the panel sits and how big it is, in canvas units. For tests and the editor.</summary>
        public Rect WindowRect => new Rect(_windowPos.x, _windowPos.y, _windowSize.x, _windowSize.y);

        // ── Persistence ────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the remembered geometry. Called BEFORE the build, so the window is created at
        /// the size it will keep rather than snapping to it a frame later.
        /// </summary>
        private void LoadWindowPrefs()
        {
            // Guarded because EnsureBuilt also calls it. Unity runs no Awake on a component
            // added in Edit Mode, so a fixture that builds this panel would otherwise open a
            // window sized (0,0) and every measurement taken off it would be about nothing.
            if (_prefsLoaded) return;
            _prefsLoaded = true;

            _windowSize = new Vector2(
                Mathf.Max(MIN_W, PlayerPrefs.GetFloat(PrefKeyW, DEFAULT_W)),
                Mathf.Max(MIN_H, PlayerPrefs.GetFloat(PrefKeyH, DEFAULT_H)));

            // The default lands the window exactly where the fixed panel used to be: hard
            // against the right margin, directly under the minimap block. DERIVED from
            // HudLayout rather than a literal, so moving the minimap moves this with it.
            float defaultX = HudLayout.ReferenceWidth - HudLayout.ScreenMargin - _windowSize.x;
            _windowPos = new Vector2(
                PlayerPrefs.GetFloat(PrefKeyX, defaultX),
                PlayerPrefs.GetFloat(PrefKeyY, -HudLayout.BelowMinimapTop));

            _minimized = PlayerPrefs.GetInt(PrefKeyMinimized, 0) != 0;
            _closed    = PlayerPrefs.GetInt(PrefKeyClosed, 0) != 0;
        }

        private void SaveWindowGeometry()
        {
            PlayerPrefs.SetFloat(PrefKeyX, _windowPos.x);
            PlayerPrefs.SetFloat(PrefKeyY, _windowPos.y);
            PlayerPrefs.SetFloat(PrefKeyW, _windowSize.x);
            PlayerPrefs.SetFloat(PrefKeyH, _windowSize.y);
            PlayerPrefs.Save();
        }

        // ── The three window verbs ─────────────────────────────────────────────

        /// <summary>
        /// Collapses to the title bar, or expands again. Remembered.
        ///
        /// <para>This is the verb a player reaching for CLOSE usually wants: the panel gets out
        /// of the way and stays reachable, so nothing has to bring it back.</para>
        /// </summary>
        public void SetMinimized(bool minimized)
        {
            _minimized = minimized;
            PlayerPrefs.SetInt(PrefKeyMinimized, _minimized ? 1 : 0);
            PlayerPrefs.Save();
            ApplyWindowState();
        }

        public void ToggleMinimized() => SetMinimized(!_minimized);

        /// <summary>
        /// Puts the tracker away entirely, or brings it back.
        ///
        /// <para>Closing is remembered, so it survives a restart — which is exactly why
        /// accepting a quest re-opens it (see <c>OnQuestStarted</c>). A HUD element that can be
        /// dismissed permanently with no way back is the defect <c>DraggablePanel</c>'s own
        /// close button shipped in the Controls editor, and it is worse here, because the
        /// panel that could reopen it only exists in the Editor.</para>
        /// </summary>
        public void SetClosed(bool closed)
        {
            _closed = closed;
            PlayerPrefs.SetInt(PrefKeyClosed, _closed ? 1 : 0);
            PlayerPrefs.Save();
            if (!_closed) _armedDropId = null;
            Refresh();
        }

        public void ToggleClosed() => SetClosed(!_closed);

        // ── Build ──────────────────────────────────────────────────────────────

        public void EnsureBuilt()
        {
            if (_canvas != null) return;
            LoadWindowPrefs();

            _root = new GameObject("QuestLogHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;

            // The reference resolution is NOT cosmetic and this shipped on Unity's
            // default 800x600 with match 0. At a 1600-wide window that is a scale
            // factor of 2.0 against every other HUD canvas's 1.0, so the log's text
            // rendered at double size and its corner drifted away from the minimap's
            // on every resize. HudLayout is what the two now share.
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HudLayout.ReferenceWidth, HudLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = HudLayout.Match;

            _root.AddComponent<GraphicRaycaster>();

            BuildWindow();
            ApplyWindowState();
            _root.SetActive(false);
        }

        private void BuildWindow()
        {
            var win = new GameObject("Window");
            win.transform.SetParent(_root.transform, false);
            _windowRect = win.AddComponent<RectTransform>();
            _windowRect.anchorMin = new Vector2(0f, 1f);
            _windowRect.anchorMax = new Vector2(0f, 1f);
            _windowRect.pivot     = new Vector2(0f, 1f);
            _windowRect.anchoredPosition = _windowPos;
            _windowRect.sizeDelta = _windowSize;

            var winImg = win.AddComponent<Image>();
            winImg.color = new Color(0.05f, 0.06f, 0.09f, 0.86f);
            // A raycast target now, unlike the old label — the whole point is that it can be
            // grabbed. It is bounded by the window rather than by the old full-column
            // rectangle, so the camera's wheel-zoom keeps the rest of the corner.
            winImg.raycastTarget = true;

            BuildTitleBar(win.transform);
            BuildBody(win.transform);
            BuildResizeGrip(win.transform);
        }

        private void BuildTitleBar(Transform parent)
        {
            var bar = new GameObject("TitleBar");
            bar.transform.SetParent(parent, false);
            var barRt = bar.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 1f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot     = new Vector2(0.5f, 1f);
            barRt.sizeDelta = new Vector2(0f, TITLE_BAR_H);
            barRt.anchoredPosition = Vector2.zero;

            var barImg = bar.AddComponent<Image>();
            barImg.color = new Color(0.11f, 0.13f, 0.10f, 0.95f);

            // The bar is the drag surface, not the whole window: a panel you can grab
            // anywhere is one you move by accident every time you reach for a button in it.
            var drag = bar.AddComponent<WindowDragHandler>();
            drag.Target = _windowRect;
            drag.ClampToParent = true;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(bar.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = new Vector2(PADDING, 0f);
            titleRt.offsetMax = new Vector2(-(CHROME_BTN * 2f + PADDING * 2f), 0f);
            _titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            _titleLabel.text = QuestLogTitle;
            _titleLabel.fontSize = 13;
            _titleLabel.fontStyle = FontStyles.Bold;
            _titleLabel.color = new Color(0.72f, 0.80f, 0.60f);
            _titleLabel.alignment = TextAlignmentOptions.Left;
            // Not a raycast target: it covers most of the bar, and a label that eats the
            // pointer is a title bar that cannot be dragged by its title.
            _titleLabel.raycastTarget = false;

            _minimizeButton = MakeChromeButton(bar.transform, "Minimize", "–",
                -(CHROME_BTN + PADDING * 2f), new Color(0.22f, 0.24f, 0.20f, 1f), ToggleMinimized,
                out _minimizeGlyph);

            MakeChromeButton(bar.transform, "Close", "✕",
                -PADDING, new Color(0.34f, 0.18f, 0.16f, 1f), () => SetClosed(true), out _);
        }

        /// <summary>
        /// One square control in the title bar. Anchored to the bar's RIGHT edge, so the
        /// pair stays put when the window is resized.
        /// </summary>
        private Button MakeChromeButton(Transform parent, string name, string glyph, float x,
                                        Color color, UnityEngine.Events.UnityAction onClick,
                                        out TextMeshProUGUI label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(CHROME_BTN, CHROME_BTN);
            rt.anchoredPosition = new Vector2(x, 0f);

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            // Image and TMP on the same GameObject is a NullReferenceException in this
            // project — parent carries the Image and the Button, a child carries the glyph.
            var textGo = new GameObject("Glyph");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            label = textGo.AddComponent<TextMeshProUGUI>();
            label.text = glyph;
            label.fontSize = 12;
            label.color = new Color(0.90f, 0.90f, 0.86f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            return btn;
        }

        private void BuildBody(Transform parent)
        {
            _body = new GameObject("Body");
            _body.transform.SetParent(parent, false);
            var bodyRt = _body.AddComponent<RectTransform>();
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = new Vector2(0f, GRIP);
            bodyRt.offsetMax = new Vector2(0f, -TITLE_BAR_H);

            var built = UIFactory.MakeScrollView(_body.transform, "QuestRows");
            _scroll = built.scroll;
            _rowsContent = built.content;
            UIFactory.AddVerticalScrollbar(_scroll);

            // The scroll view paints its own opaque surface, which over a HUD reads as a
            // second panel inside the first.
            var bg = _scroll.GetComponent<Image>();
            if (bg != null) bg.color = new Color(0f, 0f, 0f, 0f);
        }

        private void BuildResizeGrip(Transform parent)
        {
            var go = new GameObject("ResizeGrip");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(GRIP, GRIP);
            rt.anchoredPosition = Vector2.zero;

            // TriangleHandleGraphic mirrors its own MESH rather than being rotated or
            // negatively scaled into its corner: both of those turn the rect about its pivot
            // and swing the grip outside the window it resizes.
            var tri = go.AddComponent<TriangleHandleGraphic>();
            tri.color = new Color(0.55f, 0.58f, 0.45f, 0.75f);
            tri.Corner = ResizeGripCorner.BottomRight;

            _resizeHandle = go.AddComponent<PanelResizeHandle>();
            _resizeHandle.Target = _windowRect;
            _resizeHandle.Corner = ResizeGripCorner.BottomRight;
            _resizeHandle.MinSize = new Vector2(MIN_W, MIN_H);
            _resizeHandle.MaxSize = new Vector2(HudLayout.ReferenceWidth * 0.6f,
                                                HudLayout.ReferenceHeight * 0.9f);
            _resizeHandle.Resized += size =>
            {
                _windowSize = size;
                SaveWindowGeometry();
            };
        }

        // ── Applying state ─────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current window state onto the rects: body hidden while minimized, and
        /// the bar's height standing in for the window's.
        /// </summary>
        private void ApplyWindowState()
        {
            if (_windowRect == null) return;

            if (_body != null) _body.SetActive(!_minimized);
            if (_resizeHandle != null) _resizeHandle.gameObject.SetActive(!_minimized);
            if (_minimizeGlyph != null) _minimizeGlyph.text = _minimized ? "□" : "–";

            // The REMEMBERED size is not touched while minimized — the collapse is a view,
            // not a resize, so expanding restores the height the player chose rather than a
            // title bar's worth of it.
            if (_minimized)
            {
                _windowRect.sizeDelta = new Vector2(_windowSize.x, TITLE_BAR_H);
                return;
            }

            // Expanded, and which way this writes MATTERS: this method runs on every refresh,
            // and a quest can tick while the grip is being dragged. Forcing _windowSize back
            // onto the rect there would snap the window out from under the cursor, because
            // PanelResizeHandle only reports at the END of the gesture. So the rect is the
            // authority while it holds a real size, and the remembered value follows it — the
            // one case that writes the other way is coming back OUT of a collapse, which is
            // exactly when the rect is holding a title bar's height and nothing else.
            if (Mathf.Abs(_windowRect.sizeDelta.y - TITLE_BAR_H) < 0.01f)
                _windowRect.sizeDelta = _windowSize;
            else
                _windowSize = _windowRect.sizeDelta;
        }

        /// <summary>
        /// Records where a drag left the window. Polled rather than hooked, because
        /// <c>WindowDragHandler</c> writes <c>localPosition</c> directly and raises no event —
        /// and a poll that only writes when the value CHANGED is not a file write per frame.
        /// </summary>
        private void LateUpdate()
        {
            if (_windowRect == null || _closed) return;
            var pos = _windowRect.anchoredPosition;
            if ((pos - _windowPos).sqrMagnitude < 0.01f) return;
            _windowPos = pos;
            SaveWindowGeometry();
        }
    }
}
