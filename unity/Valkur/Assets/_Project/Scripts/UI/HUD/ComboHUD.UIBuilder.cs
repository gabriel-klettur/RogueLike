using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Builds the combo badge hierarchy procedurally — no prefab, no scene
    /// authoring. Everything is anchored explicitly rather than laid out by a
    /// LayoutGroup: the badge repaints several times a second while a streak is
    /// running, and a layout rebuild per repaint is pure waste on a widget whose
    /// geometry never changes.
    ///
    /// <code>
    /// root ─ CanvasGroup, sized by HUDManager
    ///  ├ Edge      soft tier-tinted outline, 2 px larger than the body
    ///  └ Panel     rounded dark body
    ///     ├ Accent      tier-coloured bar down the left edge
    ///     ├ Glow        radial halo behind the number
    ///     ├ Count       "12x"  (big, tier-coloured, punches on every hit)
    ///     ├ Title       "SAVAGE"
    ///     ├ Pips        one dot per tier, lit up to the current rung
    ///     └ TimerBg ─ TimerFill    combo window draining left-to-right
    /// </code>
    /// </summary>
    public sealed partial class ComboHUD
    {
        // ── Layout ────────────────────────────────────────────────────────
        private const float PadLeft      = 14f;
        private const float PadRight     = 12f;
        private const float PadTop       = 8f;
        private const float PadBottom    = 8f;
        private const float HeaderHeight = 30f;
        private const float TimerHeight  = 8f;
        private const float TimerGap     = 6f;
        private const float AccentWidth  = 3f;
        private const float CountWidth   = 96f;
        private const float GlowSizeUI   = 88f;
        private const float PipSize      = 7f;
        private const float PipGap       = 4f;
        private const float EdgeOutset   = 2f;

        /// <summary>Height the badge needs. HUDManager sizes the root with it.</summary>
        public const float PreferredHeight =
            PadTop + HeaderHeight + TimerGap + TimerHeight + PadBottom;

        private static readonly Color PanelBodyColor = new Color(0.043f, 0.047f, 0.063f, 0.82f);
        private static readonly Color TimerTrackColor = new Color(0.10f, 0.10f, 0.13f, 0.90f);
        private static readonly Color PipOffColor = new Color(1f, 1f, 1f, 0.16f);

        // ── Built refs ────────────────────────────────────────────────────
        private CanvasGroup      _canvasGroup;
        private GameObject       _panelGo;
        private RectTransform    _panelRt;
        private Image            _edgeImage;
        private Image            _accentImage;
        private Image            _glowImage;
        private TextMeshProUGUI  _countText;
        private RectTransform    _countRt;
        private TextMeshProUGUI  _titleText;
        private RectTransform    _pipsRoot;
        private Image[]          _pipImages;
        private Image            _timerFillImage;
        private bool             _built;

        /// <summary>
        /// Build the hierarchy once. Public and idempotent, matching the other
        /// procedurally-built HUDs — EditMode tests call it because Awake does
        /// not fire reliably outside play mode.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            var rootRt = GetComponent<RectTransform>();
            if (rootRt == null) rootRt = gameObject.AddComponent<RectTransform>();

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            // Purely informational widget — never steal a click from the game.
            _canvasGroup.interactable   = false;
            _canvasGroup.blocksRaycasts = false;

            BuildEdge(rootRt);
            BuildPanel(rootRt);
            BuildAccent();
            BuildGlow();
            BuildCount();
            BuildTitle();
            BuildPipsRoot();
            BuildTimerBar();

            RebuildPips();
        }

        private void BuildEdge(RectTransform parent)
        {
            var go = NewChild("Edge", parent);
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt, EdgeOutset);

            _edgeImage = go.AddComponent<Image>();
            _edgeImage.sprite        = RoundedSprite();
            _edgeImage.type          = Image.Type.Sliced;
            _edgeImage.raycastTarget = false;
        }

        private void BuildPanel(RectTransform parent)
        {
            _panelGo = NewChild("Panel", parent);
            _panelRt = _panelGo.GetComponent<RectTransform>();
            Stretch(_panelRt, 0f);

            var img = _panelGo.AddComponent<Image>();
            img.sprite        = RoundedSprite();
            img.type          = Image.Type.Sliced;
            img.color         = PanelBodyColor;
            img.raycastTarget = false;
        }

        private void BuildAccent()
        {
            var go = NewChild("Accent", _panelRt);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(5f, 7f);
            rt.offsetMax = new Vector2(5f + AccentWidth, -7f);

            _accentImage = go.AddComponent<Image>();
            _accentImage.sprite        = SolidSprite();
            _accentImage.raycastTarget = false;
        }

        private void BuildGlow()
        {
            var go = NewChild("Glow", _panelRt);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(GlowSizeUI, GlowSizeUI);
            // Centred over the first two digits of the number.
            rt.anchoredPosition = new Vector2(PadLeft + 24f, -(PadTop + HeaderHeight * 0.5f));

            _glowImage = go.AddComponent<Image>();
            _glowImage.sprite        = GlowSprite();
            _glowImage.raycastTarget = false;
            _glowImage.color         = new Color(1f, 1f, 1f, 0f);
        }

        private void BuildCount()
        {
            var go = NewChild("Count", _panelRt);
            _countRt = go.GetComponent<RectTransform>();
            _countRt.anchorMin = new Vector2(0f, 1f);
            _countRt.anchorMax = new Vector2(0f, 1f);
            _countRt.pivot     = new Vector2(0f, 1f);
            _countRt.sizeDelta = new Vector2(CountWidth, HeaderHeight);
            _countRt.anchoredPosition = new Vector2(PadLeft, -PadTop);

            _countText = go.AddComponent<TextMeshProUGUI>();
            _countText.fontSize      = 28f;
            _countText.fontStyle     = FontStyles.Bold;
            _countText.alignment     = TextAlignmentOptions.MidlineLeft;
            _countText.enableWordWrapping = false;
            _countText.richText      = true;   // the "x" suffix is size-tagged
            _countText.raycastTarget = false;
            _countText.text          = CountLabel(0);
        }

        private void BuildTitle()
        {
            var go = NewChild("Title", _panelRt);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(PadLeft + CountWidth, -(PadTop + HeaderHeight));
            rt.offsetMax = new Vector2(-(PadRight + PipsWidth()), -PadTop);

            _titleText = go.AddComponent<TextMeshProUGUI>();
            _titleText.fontSize        = 14f;
            _titleText.fontStyle       = FontStyles.Bold;
            _titleText.characterSpacing = 9f;
            _titleText.alignment       = TextAlignmentOptions.MidlineLeft;
            _titleText.enableWordWrapping = false;
            _titleText.overflowMode    = TextOverflowModes.Ellipsis;
            _titleText.raycastTarget   = false;
            _titleText.text            = string.Empty;
        }

        private void BuildPipsRoot()
        {
            var go = NewChild("Pips", _panelRt);
            _pipsRoot = go.GetComponent<RectTransform>();
            _pipsRoot.anchorMin = new Vector2(1f, 1f);
            _pipsRoot.anchorMax = new Vector2(1f, 1f);
            _pipsRoot.pivot     = new Vector2(1f, 0.5f);
            _pipsRoot.sizeDelta = new Vector2(PipsWidth(), PipSize);
            _pipsRoot.anchoredPosition = new Vector2(-PadRight, -(PadTop + HeaderHeight * 0.5f));
        }

        private void BuildTimerBar()
        {
            var trackGo = NewChild("TimerBg", _panelRt);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0f);
            trackRt.anchorMax = new Vector2(1f, 0f);
            trackRt.pivot     = new Vector2(0.5f, 0f);
            trackRt.offsetMin = new Vector2(PadLeft, PadBottom);
            trackRt.offsetMax = new Vector2(-PadRight, PadBottom + TimerHeight);

            var track = trackGo.AddComponent<Image>();
            track.sprite        = SolidSprite();
            track.color         = TimerTrackColor;
            track.raycastTarget = false;

            var fillGo = NewChild("TimerFill", trackRt);
            var fillRt = fillGo.GetComponent<RectTransform>();
            Stretch(fillRt, 0f);

            _timerFillImage = fillGo.AddComponent<Image>();
            _timerFillImage.sprite        = SolidSprite();
            _timerFillImage.type          = Image.Type.Filled;
            _timerFillImage.fillMethod    = Image.FillMethod.Horizontal;
            _timerFillImage.fillOrigin    = (int)Image.OriginHorizontal.Left;
            _timerFillImage.fillAmount    = 0f;
            _timerFillImage.raycastTarget = false;
        }

        /// <summary>
        /// Rebuilds the pip row so it always has exactly one dot per configured
        /// tier. Called on build and whenever <see cref="SetTiers"/> changes the
        /// ladder, so a designer adding a rung never has to touch the layout.
        /// </summary>
        private void RebuildPips()
        {
            if (_pipsRoot == null) return;

            int wanted = TierCount;

            if (_pipImages != null)
            {
                for (int i = 0; i < _pipImages.Length; i++)
                    if (_pipImages[i] != null) DestroySafely(_pipImages[i].gameObject);
            }

            _pipImages = new Image[wanted];
            _pipsRoot.sizeDelta = new Vector2(PipsWidth(), PipSize);

            if (_titleText != null)
            {
                var titleRt = _titleText.rectTransform;
                titleRt.offsetMax = new Vector2(-(PadRight + PipsWidth() + PipGap), -PadTop);
            }

            for (int i = 0; i < wanted; i++)
            {
                var go = NewChild("Pip_" + i, _pipsRoot);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot     = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(PipSize, PipSize);
                // Index 0 is the lowest rung, so it sits leftmost.
                rt.anchoredPosition = new Vector2(-(wanted - 1 - i) * (PipSize + PipGap), 0f);

                var img = go.AddComponent<Image>();
                img.sprite        = DotSprite();
                img.color         = PipOffColor;
                img.raycastTarget = false;
                _pipImages[i] = img;
            }
        }

        private float PipsWidth()
        {
            int n = TierCount;
            return n <= 0 ? 0f : n * PipSize + (n - 1) * PipGap;
        }

        // ── Small helpers ─────────────────────────────────────────────────


        private static void DestroySafely(Object obj)
        {
            if (obj == null) return;
            // EditMode tests rebuild these rows outside play mode, where
            // Object.Destroy is illegal.
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt, float outset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-outset, -outset);
            rt.offsetMax = new Vector2(outset, outset);
        }
    }
}
