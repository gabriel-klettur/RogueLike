using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The window the CHARACTER and RECORDS tabs share: canvas, veil, stone, header, footer, the
    /// texel counter-scale, and the fade.
    ///
    /// <para><b>Why a helper and not a base class.</b> Both panels are
    /// <c>SingletonMonoBehaviour&lt;T&gt;</c> of their own type, so they cannot share one; and
    /// copying the chrome into each is how two views of one window drift apart by a padding.
    /// The chrome owns the outside, the panel owns the inside.</para>
    ///
    /// <para><b>The counter-scale is set LAST and never through <c>HudRect.Place</c>.</b> That
    /// helper ends with <c>localScale = Vector3.one</c> — correct for every texel-space child and
    /// fatal for the one rect whose job is the scale. It cost the talents board a whole capture:
    /// the panel was sized 908x544 while its content stayed at scale 1 and drew at half size in
    /// the bottom-left quadrant of its own stone.</para>
    /// </summary>
    public sealed class SheetPanelChrome
    {
        private readonly SheetHudStyle _style;
        private readonly HudTheme _theme;
        private readonly PlayerHudStyle _grid;
        private readonly HudArt _art;

        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly CanvasGroup _group;
        private readonly RectTransform _panelRect;
        private readonly RectTransform _pixels;
        private readonly RawImage _stone;
        private Texture2D _stoneTex;

        private readonly HudPixelText _title;
        private readonly HudPixelText _footer;

        private int _pixelScale = 1;
        private int _screenW, _screenH;
        private float _rootScale = 1f;
        private float _fade, _fadeTarget;

        public GameObject Root => _root;
        public RectTransform Pixels => _pixels;
        public RectTransform PanelRect => _panelRect;
        public HudArt Art => _art;
        public HudTheme Theme => _theme;
        public SheetHudStyle Style => _style;
        public int PixelScale => _pixelScale;
        public Material Additive { get; }

        /// <summary>Y of the first texel the body may use, and the height it has.</summary>
        public int BodyBottom => _style.paddingTexels + _style.footerTexels;
        public int BodyHeight => _style.heightTexels - _style.paddingTexels * 2
                               - _style.titleBarTexels - _style.footerTexels;
        public int BodyWidth => _style.widthTexels - _style.paddingTexels * 2;
        public int BodyLeft => _style.paddingTexels;

        public SheetPanelChrome(Transform owner, string name)
        {
            _style = SheetHudStyle.Active;
            _theme = HudTheme.Active;
            _grid = PlayerHudStyle.Active;
            _art = HudArt.Get(_grid);

            var shader = _grid.hudFxShader != null ? _grid.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                Additive = new Material(shader) { name = name + "Additive", hideFlags = HideFlags.DontSave };
                Additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                Additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            _root = new GameObject(name + "_Root");
            _root.transform.SetParent(owner, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = HudLayout.CharacterSheetSortingOrder;
            HudLayout.ApplyScaler(_root.AddComponent<CanvasScaler>());
            _root.AddComponent<GraphicRaycaster>();
            _group = _root.AddComponent<CanvasGroup>();

            // The veil: says the window is modal, stops the world animating through the plate,
            // and eats a click that misses the panel so it never reaches the world behind it.
            var veilRt = HudRect.Make("Veil", _root.transform, 0, 0, 1, 1);
            veilRt.anchorMin = Vector2.zero;
            veilRt.anchorMax = Vector2.one;
            veilRt.offsetMin = veilRt.offsetMax = Vector2.zero;
            var veil = veilRt.gameObject.AddComponent<Image>();
            veil.sprite = _art.White;
            veil.color = new Color(0f, 0f, 0f, _style.veilAlpha);
            veil.raycastTarget = true;

            var panelGo = new GameObject(name + "Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            _panelRect = (RectTransform)panelGo.transform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            var nested = panelGo.AddComponent<Canvas>();
            nested.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            panelGo.AddComponent<GraphicRaycaster>();

            _pixels = HudRect.Make("Pixels", _panelRect, 0, 0, _style.widthTexels, _style.heightTexels);

            var stoneRt = HudRect.Make("Stone", _pixels, 0, 0, _style.widthTexels, _style.heightTexels);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            _stone.raycastTarget = true;
            _stoneTex = HudArt.BakePanel(_style.widthTexels, _style.heightTexels, _grid);
            _stone.texture = _stoneTex;

            int titleY = _style.heightTexels - _style.paddingTexels - _style.titleBarTexels;
            // SMALL face: the large one carries digits and punctuation and NOT ONE LETTER, and
            // HudPixelText silently skips a character its face cannot spell — which is how the
            // talents board shipped with an invisible title.
            _title = HudPixelText.Create(_pixels, "Title", _art, HudFontFace.Small, HudTextAlign.Left,
                                         _style.paddingTexels + 2, titleY,
                                         BodyWidth, _style.titleBarTexels);
            _title.color = _theme.text;

            _footer = HudPixelText.Create(_pixels, "Footer", _art, HudFontFace.Small, HudTextAlign.Left,
                                          _style.paddingTexels + 2, _style.paddingTexels,
                                          BodyWidth, _style.footerTexels);
            _footer.color = _theme.textDim;

            UILayerHelper.SetUILayerRecursive(_root);
            _root.SetActive(false);
            _group.alpha = 0f;
        }

        public void SetTitle(string text) => _title.SetText(text == null ? string.Empty : text.ToUpperInvariant());
        public void SetFooter(string text) => _footer.SetText(text == null ? string.Empty : text.ToUpperInvariant());

        /// <summary>A body container placed in texel space under the header.</summary>
        public RectTransform MakeBody(string name) =>
            HudRect.Make(name, _pixels, BodyLeft, BodyBottom, BodyWidth, BodyHeight);

        public void Show()
        {
            _root.SetActive(true);
            _fadeTarget = 1f;
            Refit(force: true);
        }

        public void Hide()
        {
            _fadeTarget = 0f;
            _fade = 0f;
            _group.alpha = 0f;
            // Hidden on the frame it closes: the tab strip swaps panels in one frame, and a
            // panel still fading out under the next tab reads as a ghost.
            _root.SetActive(false);
        }

        /// <summary>Advances the fade. R7 forbids an alpha jump on show.</summary>
        public void Tick(float unscaledDelta)
        {
            if (!Mathf.Approximately(_fade, _fadeTarget))
            {
                _fade = Mathf.MoveTowards(_fade, _fadeTarget, unscaledDelta / Mathf.Max(0.01f, _style.fadeSeconds));
                _group.alpha = _fade;
            }
            Refit();
        }

        /// <summary>Re-derives the whole-pixel scale, and centres the window on a whole pixel.</summary>
        public void Refit(bool force = false)
        {
            if (_canvas == null) return;
            int sw = Screen.width, sh = Screen.height;
            float root = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale)) return;

            _screenW = sw; _screenH = sh; _rootScale = root;
            _pixelScale = _grid.HudPixelScaleFor(sw, sh);

            float perTexel = _pixelScale / root;
            _pixels.sizeDelta = new Vector2(_style.widthTexels, _style.heightTexels);
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            _panelRect.sizeDelta = new Vector2(_style.widthTexels * perTexel, _style.heightTexels * perTexel);
            _panelRect.anchoredPosition = Vector2.zero;
        }

        public void Dispose()
        {
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(Additive);
            _stoneTex = null;
        }

        /// <summary>An image placed on whole texels and painted in one call.</summary>
        public static Image Tinted(string name, Transform parent, Sprite sprite, int x, int y,
                                   int w, int h, Color colour, Image.Type type = Image.Type.Simple)
        {
            var img = HudRect.MakeImage(name, parent, sprite, x, y, w, h, type);
            img.color = colour;
            return img;
        }

        /// <summary>Destroys a UI object from either mode — Destroy is an ERROR in Edit Mode.</summary>
        public static void DestroyUI(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
