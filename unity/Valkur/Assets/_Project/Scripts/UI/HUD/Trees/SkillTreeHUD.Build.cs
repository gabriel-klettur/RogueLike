using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The talents board's chrome: canvas, veil, stone, header, board, card, footer — all in the
    /// player panel's texel space, drawn from the shared generated atlas.
    /// </summary>
    public sealed partial class SkillTreeHUD
    {
        // Window geometry, in TEXELS. The board's own size comes from SkillTreeLayout, so the
        // window is as wide as the tree it holds plus the card beside it — a tree with four
        // columns widens the window instead of overflowing it.
        private const int MinBoardWidth = 288;
        private const int MinBoardHeight = 150;

        private bool _built;
        private GameObject _root;
        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _panelRect;
        private RectTransform _pixels;
        private RectTransform _boardRoot;
        private RectTransform _cardRoot;
        private RawImage _stone;
        private Texture2D _stoneTex;
        private Image _veil;
        private Material _additive;
        private HudMoteLayer _motes;

        private HudPixelText _titleLabel;
        private HudPixelText _pointsLabel;
        private Image _pointsMedallion;
        private Image _pointsMedallionGlow;
        private HudPixelText _flavourLabel;
        private HudPixelText _footerLabel;
        private Image _respecButton;
        private HudPixelText _respecLabel;

        private SkillsHudStyle _style;
        private HudTheme _theme;
        private PlayerHudStyle _gridStyle;
        private HudArt _art;

        private readonly List<SkillNodeView> _views = new List<SkillNodeView>(16);
        private readonly List<SkillNodePlacement> _placements = new List<SkillNodePlacement>(16);
        private readonly List<SkillEdgePlacement> _edges = new List<SkillEdgePlacement>(16);
        private readonly List<Image> _edgeImages = new List<Image>(48);

        private int _widthTexels = 452;
        private int _heightTexels = 246;
        private int _boardWidth = MinBoardWidth;
        private int _boardHeight = MinBoardHeight;

        private int _pixelScale = 1;
        private int _screenW, _screenH;
        private float _rootScale = 1f;
        private float _fade, _fadeTarget;

        /// <summary>Window size in texels. For the tests, which cannot ask uGUI in EditMode.</summary>
        public Vector2Int WindowTexels => new Vector2Int(_widthTexels, _heightTexels);

        /// <summary>Whole screen pixels per texel at the live resolution.</summary>
        public int PixelScale => _pixelScale;

        /// <summary>The node widgets on the board. For the tests and the motes.</summary>
        public IReadOnlyList<SkillNodeView> Views => _views;

        /// <summary>
        /// Builds the window once. Public because Unity never calls <c>Awake</c> on a component
        /// added in Edit Mode, so a fixture has to reach it directly.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _style = SkillsHudStyle.Active;
            _theme = HudTheme.Active;
            _gridStyle = PlayerHudStyle.Active;
            _art = HudArt.Get(_gridStyle);

            var shader = _gridStyle.hudFxShader != null ? _gridStyle.hudFxShader
                                                        : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "SkillsAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            _root = new GameObject("SkillTreeHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = HudLayout.CharacterSheetSortingOrder;

            // The canvas contract every HUD surface shares (HUD_VISUAL_LANGUAGE.md, R9), in one
            // call so the three values cannot drift apart here the way they have everywhere else.
            // This panel shipped on Unity's default 800x600 with match 0: a 2.0 scale factor at
            // 1600 wide against everything else's 1.0, and at sortingOrder 60 it also drew under
            // the minimap (105) and the music plaque (140). ApplyScaler also carries the player's
            // interface-size setting, which a hand-written scaler silently ignores.
            HudLayout.ApplyScaler(_root.AddComponent<CanvasScaler>());
            _root.AddComponent<GraphicRaycaster>();
            _group = _root.AddComponent<CanvasGroup>();

            BuildVeil();
            BuildPanel();

            _root.SetActive(false);
            _group.alpha = 0f;
        }

        /// <summary>
        /// A full-screen veil behind the window. Two jobs, and the second is the one the old
        /// panel had no answer for: it says the window is modal, and it stops the world animating
        /// through the plate and competing with the text. It is a raycast target, so a click that
        /// misses the board does not cast a spell into the room behind it.
        /// </summary>
        private void BuildVeil()
        {
            var rt = HudRect.Make("Veil", _root.transform, 0, 0, 1, 1);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _veil = rt.gameObject.AddComponent<Image>();
            _veil.sprite = _art.White;
            _veil.color = new Color(0f, 0f, 0f, _style.veilAlpha);
            _veil.raycastTarget = true;
        }

        private void BuildPanel()
        {
            var panelGo = new GameObject("SkillsPanel", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panelRect = (RectTransform)panelGo.transform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);

            // A nested canvas, so a mote burst rebuilds this window's batch and not the HUD's.
            var nested = panelGo.AddComponent<Canvas>();
            nested.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            panelGo.AddComponent<GraphicRaycaster>();

            ComputeGeometry(null);

            _pixels = HudRect.Make("Pixels", _panelRect, 0, 0, _widthTexels, _heightTexels);

            var stoneRt = HudRect.Make("Stone", _pixels, 0, 0, _widthTexels, _heightTexels);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            _stone.raycastTarget = true;   // the window eats the clicks that land on its stone
            BakeStone();

            BuildHeader();
            _boardRoot = HudRect.Make("Board", _pixels, _style.paddingTexels, FooterTop(),
                                      _boardWidth, _boardHeight);
            _cardRoot = HudRect.Make("Card", _pixels,
                                     _widthTexels - _style.paddingTexels - _style.cardWidthTexels,
                                     FooterTop(), _style.cardWidthTexels, _boardHeight);
            BuildCard();
            BuildFooter();

            _motes = HudMoteLayer.Create(_pixels, _art, _style.moteCapacity, _additive);

            UILayerHelper.SetUILayerRecursive(_root);
        }

        /// <summary>Y of the first texel the board may use — directly above the footer.</summary>
        private int FooterTop() => _style.paddingTexels + _style.footerTexels;

        /// <summary>
        /// Sizes the window from the tree it is about to draw. The board's own extent comes from
        /// <see cref="SkillTreeLayout"/>, so a class with a wider tree gets a wider window rather
        /// than a clipped one — which is the whole failure mode of the list this replaces.
        /// </summary>
        private void ComputeGeometry(SkillTree tree)
        {
            var size = SkillTreeLayout.Build(tree, null, null);
            _boardWidth = Mathf.Max(MinBoardWidth, size.x);
            _boardHeight = Mathf.Max(MinBoardHeight, size.y);

            int pad = _style.paddingTexels;
            _widthTexels = pad * 2 + _boardWidth + _style.cardGapTexels + _style.cardWidthTexels;
            _heightTexels = pad * 2 + _style.titleBarTexels + _style.flavourTexels +
                            _boardHeight + _style.footerTexels;
        }

        private void BakeStone()
        {
            HudLifetime.Release(_stoneTex);
            _stoneTex = HudArt.BakePanel(_widthTexels, _heightTexels, _gridStyle);
            if (_stone != null) _stone.texture = _stoneTex;
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void BuildHeader()
        {
            int pad = _style.paddingTexels;
            int titleY = _heightTexels - pad - _style.titleBarTexels;

            // SMALL face, not Large. The large face carries DIGITS ONLY (0-9 and / + - . :) and
            // no letters, and HudPixelText silently skips a character the face cannot spell — so
            // the title drew literally nothing and the window opened with no name on it.
            _titleLabel = HudPixelText.Create(_pixels, "Title", _art, HudFontFace.Small,
                                              HudTextAlign.Left, pad + 2, titleY,
                                              _widthTexels - pad * 2 - 60, _style.titleBarTexels);
            _titleLabel.color = _theme.text;

            // The purse is a medallion because that is what the player panel already uses for a
            // resource the player spends, and gold because R6 says gold is importance. It is one
            // of exactly three places gold appears in this window.
            int medSize = _style.titleBarTexels;
            int medX = _widthTexels - pad - medSize;
            _pointsMedallion = Tinted("PointsMedallion", _pixels, _art.Medallion,
                                      medX, titleY, medSize, medSize, _theme.gold);
            _pointsMedallionGlow = Tinted("PointsGlow", _pixels, _art.MedallionGlow,
                                          medX, titleY, medSize, medSize, Transparent(_theme.gold));

            _pointsLabel = HudPixelText.Create(_pixels, "Points", _art, HudFontFace.Large,
                                               HudTextAlign.Centre, medX, titleY, medSize,
                                               _style.titleBarTexels);
            _pointsLabel.color = _theme.text;

            _flavourLabel = HudPixelText.Create(_pixels, "Flavour", _art, HudFontFace.Small,
                                                HudTextAlign.Left, pad + 2,
                                                titleY - _style.flavourTexels,
                                                _widthTexels - pad * 2, _style.flavourTexels);
            _flavourLabel.color = _theme.textDim;
        }

        // ── Footer ────────────────────────────────────────────────────────────

        private void BuildFooter()
        {
            int pad = _style.paddingTexels;

            _footerLabel = HudPixelText.Create(_pixels, "Footer", _art, HudFontFace.Small,
                                               HudTextAlign.Left, pad + 2, pad,
                                               _widthTexels / 2, _style.footerTexels);
            _footerLabel.color = _theme.textDim;

            int w = 108, h = _style.footerTexels;
            int x = _boardWidth + pad - w;
            _respecButton = Tinted("Respec", _pixels, _art.Slot, x, pad, w, h,
                                   _theme.stoneDark, Image.Type.Sliced);
            _respecButton.raycastTarget = true;
            var btn = _respecButton.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnRespecClicked);

            _respecLabel = HudPixelText.Create(_respecButton.rectTransform, "RespecLabel", _art,
                                               HudFontFace.Small, HudTextAlign.Centre, 0, 0, w, h);
            _respecLabel.color = _theme.textDim;
            _respecLabel.SetText(SkillText.Respec.ToUpperInvariant());
        }

        // ── Fit ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-derives the whole-pixel scale when the screen or the canvas scale changed, and
        /// centres the window on a whole screen pixel, so every texel inside is exactly
        /// <see cref="PixelScale"/> pixels — the player panel's rule (R1).
        /// </summary>
        private void Refit(bool force = false)
        {
            if (!_built || _canvas == null) return;
            int sw = Screen.width, sh = Screen.height;
            float root = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale))
                return;

            int oldScale = _pixelScale;
            _screenW = sw;
            _screenH = sh;
            _rootScale = root;
            _pixelScale = _gridStyle.HudPixelScaleFor(sw, sh);

            float perTexel = _pixelScale / root;
            _pixels.sizeDelta = new Vector2(_widthTexels, _heightTexels);
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            _panelRect.sizeDelta = new Vector2(_widthTexels * perTexel, _heightTexels * perTexel);
            _panelRect.anchoredPosition = Vector2.zero;

            if (oldScale != _pixelScale)
                for (int i = 0; i < _views.Count; i++) _views[i].ApplyIcon(_pixelScale);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Image Tinted(string name, Transform parent, Sprite sprite, int x, int y,
                                    int w, int h, Color colour, Image.Type type = Image.Type.Simple)
        {
            var img = HudRect.MakeImage(name, parent, sprite, x, y, w, h, type);
            img.color = colour;
            return img;
        }

        private static Color Transparent(Color c) => new Color(c.r, c.g, c.b, 0f);

        private static void DestroyUI(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
