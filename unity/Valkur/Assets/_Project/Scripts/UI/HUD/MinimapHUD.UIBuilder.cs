using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Visual chrome for the top-right minimap. Mirrors the design language of
    /// <see cref="DayNightClockHUD"/>: a circular dial up top (the live map) and
    /// a rectangular info plate beneath (zone name + coords). The dial wears an
    /// accent ring with N/E/S/W cardinal letters so the player can orient even
    /// when the world is monochrome (spirit world / dungeon dark zones).
    ///
    /// Every sprite — disc, ring, and cardinal-tick fills — is generated in
    /// code, so no scene assets are required. All sprites are white and tinted
    /// at runtime by Image.color (UITheme palette).
    /// </summary>
    public sealed partial class MinimapHUD
    {
        // ── Layout (mirrors DayNightClockHUD's dial proportions) ────────────
        private const float DISC_SIZE        = 192f;
        private const float RING_THICK       = 6f;
        private const float MAP_INSET        = 4f;
        private const float INFO_BAND_H      = 40f;
        private const float INFO_BAND_GAP    = 4f;
        private const float CARDINAL_SIZE    = 16f;
        private const float CARDINAL_INSET   = 3f;
        private const float ARROW_SIZE       = 14f;

        // ── Theme handles (kept local so the file stays self-contained) ─────
        private static readonly Color DISC_BG        = new Color(0.06f, 0.07f, 0.10f, 0.94f);
        private static readonly Color RING_OUTER     = new Color(0.90f, 0.76f, 0.38f, 0.55f);
        private static readonly Color CARDINAL_TINT  = new Color(0.95f, 0.85f, 0.45f, 0.95f);
        private static readonly Color INFO_BG        = new Color(0.04f, 0.05f, 0.08f, 0.92f);
        private static readonly Color INFO_BORDER    = new Color(0.90f, 0.76f, 0.38f, 0.45f);
        private static readonly Color ARROW_TINT     = new Color(0.95f, 0.97f, 1.00f, 0.95f);

        // ── Sprite cache (Domain Reload OFF → reset hook below) ─────────────
        private static Sprite _solidSprite;
        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        private static Sprite _arrowSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpriteCacheOnPlayModeEnter()
        {
            _solidSprite = _circleSprite = _ringSprite = _arrowSprite = null;
        }

        // ── UI handles (extra ones for the dial layout) ─────────────────────
        private TextMeshProUGUI _coordsLabel;
        private RectTransform   _discRt;
        private float           _mapDiameter;

        // ── Build entry point ───────────────────────────────────────────────
        private void BuildUI()
        {
            // Own canvas: same sortingOrder as DayNightClockHUD so the two
            // top-corner widgets share a layer and never fight z-order.
            var canvasGo = new GameObject("MinimapHUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;
            // GraphicRaycaster is needed so EventSystem.IsPointerOverGameObject()
            // returns true when the cursor is over the disc — that's how
            // CameraSetup.cs:262 knows to skip its own wheel-zoom logic while
            // the player is scrolling to zoom the minimap.
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = NewRect("Root", canvasGo.transform);
            _root.anchorMin        = new Vector2(1f, 1f);
            _root.anchorMax        = new Vector2(1f, 1f);
            _root.pivot            = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT, -MARGIN_TOP);
            _root.sizeDelta        = new Vector2(DISC_SIZE, DISC_SIZE + INFO_BAND_GAP + INFO_BAND_H);

            // Build order matters for z-ordering: later siblings render on top.
            BuildDiscWithMap();
            BuildOuterRing();
            BuildCardinalLetters();
            BuildInfoPanel();
        }

        // ── Disc (circular bg + Mask) + RawImage map + heading arrow ────────
        private void BuildDiscWithMap()
        {
            // Disc itself: circular sprite acts as both the dark backdrop and
            // the alpha-shape mask that clips the inner RawImage to a circle.
            // showMaskGraphic = true keeps the disc visible behind the map.
            _discRt = NewRect("Disc", _root);
            var discRt = _discRt;
            discRt.anchorMin = new Vector2(0.5f, 1f);
            discRt.anchorMax = new Vector2(0.5f, 1f);
            discRt.pivot     = new Vector2(0.5f, 1f);
            discRt.anchoredPosition = Vector2.zero;
            discRt.sizeDelta = new Vector2(DISC_SIZE, DISC_SIZE);

            _bgPanel = discRt.gameObject.AddComponent<Image>();
            _bgPanel.sprite = CircleSprite();
            _bgPanel.color  = DISC_BG;
            // raycastTarget = true so EventSystem.IsPointerOverGameObject()
            // detects hover; the disc is the wheel-zoom hit shape. The sprite's
            // alpha provides the round hit area (everywhere outside the circle
            // is alpha 0 → not hit).
            _bgPanel.raycastTarget = true;
            _bgPanel.alphaHitTestMinimumThreshold = 0.5f;

            var mask = discRt.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true; // keep disc visible behind the clipped content

            // Inner RawImage (hosts MinimapManager.Texture2D). Sized to fit
            // INSIDE the accent ring so the ring is never overdrawn by map.
            float mapSize = DISC_SIZE - (RING_THICK + MAP_INSET) * 2f;
            _mapDiameter = mapSize;
            var mapRt = NewRect("Map", discRt);
            mapRt.anchorMin = new Vector2(0.5f, 0.5f);
            mapRt.anchorMax = new Vector2(0.5f, 0.5f);
            mapRt.pivot     = new Vector2(0.5f, 0.5f);
            mapRt.anchoredPosition = Vector2.zero;
            mapRt.sizeDelta = new Vector2(mapSize, mapSize);
            _mapImage = mapRt.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = false;

            // Heading arrow (child of the disc so it's clipped if it ever
            // strays out — but at 14px it never will). Drawn over the map.
            _headingArrow = AddImage(discRt, "HeadingArrow", ArrowSprite(), ARROW_TINT);
            var arrowRt = _headingArrow.rectTransform;
            arrowRt.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRt.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRt.pivot     = new Vector2(0.5f, 0.5f);
            arrowRt.anchoredPosition = Vector2.zero;
            arrowRt.sizeDelta = new Vector2(ARROW_SIZE, ARROW_SIZE);
        }

        // ── Outer accent ring ───────────────────────────────────────────────
        private void BuildOuterRing()
        {
            _bgBorder = AddImage(_root, "OuterRing", RingSprite(), RING_OUTER);
            var rt = _bgBorder.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(DISC_SIZE, DISC_SIZE);
            _bgBorder.raycastTarget = false;
        }

        // ── Cardinal letters (N/E/S/W) ─────────────────────────────────────
        private void BuildCardinalLetters()
        {
            // Anchored to the *root* so they sit on top of the ring and aren't
            // clipped by the disc's Mask. Positions use the disc center + the
            // disc radius pushed inward by RING_THICK so the letters straddle
            // the ring nicely. The disc is anchored top-center of the root.
            float discCenterX = _root.sizeDelta.x * 0.5f;
            float discCenterY = -DISC_SIZE * 0.5f; // disc is top-aligned, pivot top
            float r = DISC_SIZE * 0.5f - RING_THICK - CARDINAL_INSET;

            BuildCardinalLetter("CardinalN", "N", new Vector2(discCenterX, discCenterY + r));
            BuildCardinalLetter("CardinalS", "S", new Vector2(discCenterX, discCenterY - r));
            BuildCardinalLetter("CardinalE", "E", new Vector2(discCenterX + r, discCenterY));
            BuildCardinalLetter("CardinalW", "W", new Vector2(discCenterX - r, discCenterY));
        }

        private void BuildCardinalLetter(string name, string glyph, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text                  = glyph;
            tmp.color                 = CARDINAL_TINT;
            tmp.fontSize              = 11;
            tmp.fontStyle             = FontStyles.Bold;
            tmp.alignment             = TextAlignmentOptions.Center;
            tmp.enableWordWrapping    = false;
            tmp.raycastTarget         = false;

            var rt = tmp.rectTransform;
            // Anchor to top-left of root and use anchoredPosition in screen-down y
            // (pivot top-left ⇒ y is negative going down, matching discCenterY).
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(CARDINAL_SIZE, CARDINAL_SIZE);
        }

        // ── Bottom info panel (zone + coords) ───────────────────────────────
        private void BuildInfoPanel()
        {
            // Background plate — solid dark with a top accent line that visually
            // ties it to the ring above.
            _labelBand = AddImage(_root, "InfoPlate", SolidSprite(), INFO_BG);
            var bandRt = _labelBand.rectTransform;
            bandRt.anchorMin = new Vector2(0f, 0f);
            bandRt.anchorMax = new Vector2(1f, 0f);
            bandRt.pivot     = new Vector2(0.5f, 0f);
            bandRt.anchoredPosition = Vector2.zero;
            bandRt.sizeDelta = new Vector2(0f, INFO_BAND_H);
            _labelBand.raycastTarget = false;

            // Thin gold separator at the top of the plate (matches DayNightClock
            // BG_BOTTOM seam style).
            var seam = AddImage(_root, "InfoSeam", SolidSprite(), INFO_BORDER);
            var seamRt = seam.rectTransform;
            seamRt.anchorMin = new Vector2(0f, 0f);
            seamRt.anchorMax = new Vector2(1f, 0f);
            seamRt.pivot     = new Vector2(0.5f, 0f);
            seamRt.anchoredPosition = new Vector2(0f, INFO_BAND_H - 1f);
            seamRt.sizeDelta = new Vector2(0f, 1f);
            seam.raycastTarget = false;

            // Zone name (top line, bold, primary color)
            _zoneLabel = AddLabel(_root, "ZoneLabel", 13, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            var zoneRt = _zoneLabel.rectTransform;
            zoneRt.anchorMin = new Vector2(0f, 0f);
            zoneRt.anchorMax = new Vector2(1f, 0f);
            zoneRt.pivot     = new Vector2(0.5f, 0f);
            zoneRt.anchoredPosition = new Vector2(0f, INFO_BAND_H - 20f);
            zoneRt.sizeDelta = new Vector2(-12f, 16f);
            _zoneLabel.alignment = TextAlignmentOptions.Center;
            _zoneLabel.enableWordWrapping = false;
            _zoneLabel.overflowMode = TextOverflowModes.Ellipsis;
            _zoneLabel.text = "—";

            // Coords (bottom line, smaller, secondary color)
            _coordsLabel = AddLabel(_root, "CoordsLabel", 10, FontStyles.Normal, UITheme.TEXT_SECONDARY);
            var coordsRt = _coordsLabel.rectTransform;
            coordsRt.anchorMin = new Vector2(0f, 0f);
            coordsRt.anchorMax = new Vector2(1f, 0f);
            coordsRt.pivot     = new Vector2(0.5f, 0f);
            coordsRt.anchoredPosition = new Vector2(0f, 4f);
            coordsRt.sizeDelta = new Vector2(-12f, 12f);
            _coordsLabel.alignment = TextAlignmentOptions.Center;
            _coordsLabel.enableWordWrapping = false;
            _coordsLabel.text = "—";
        }

        // ── Helpers ─────────────────────────────────────────────────────────
        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image AddImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color  = color;
            return img;
        }

        private static TextMeshProUGUI AddLabel(RectTransform parent, string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.color     = color;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ── Sprite factory (white, tinted by Image.color) ───────────────────
        private static Sprite SolidSprite()
        {
            if (_solidSprite != null) return _solidSprite;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return _solidSprite;
        }

        // Soft-edge filled circle. The Mask component on the disc uses the
        // sprite's alpha for clipping, so anti-aliased edges give a clean
        // round perimeter even when the widget is scaled by CanvasScaler.
        private static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int N = 128;
            var tex = NewIconTex(N);
            var px  = new Color32[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(r - d);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        // Hollow ring. The two soft thresholds give a clean band that scales
        // smoothly. RING_THICK_PX matches the visual ring width at design size.
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int N = 128;
            // Map design-space RING_THICK into 0..N texture space. DISC_SIZE is
            // the design-space disc diameter; the texture diameter is N. So the
            // ring thickness in texture pixels is RING_THICK * (N / DISC_SIZE).
            float ringThickTex = RING_THICK * (N / DISC_SIZE);

            var tex = NewIconTex(N);
            var px  = new Color32[N * N];
            float r       = N * 0.5f;
            float ringIn  = r - ringThickTex;
            float ringOut = r - 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float aIn  = Mathf.Clamp01(d - ringIn);
                float aOut = Mathf.Clamp01(ringOut - d);
                float a    = Mathf.Min(aIn, aOut);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _ringSprite;
        }

        private static Sprite ArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;

            // Up-pointing triangle in a 16×16 RGBA texture. Pivot (0.5, 0.5) so
            // the arrow rotates about its visual center when LateUpdate sets
            // localRotation.z from PlayerController.FacingDirection.
            const int W = 16;
            const int H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];

            Vector2 apex = new Vector2(W * 0.5f, H - 1.5f);
            Vector2 baseL = new Vector2(2f, 2f);
            Vector2 baseR = new Vector2(W - 2f, 2f);

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!PointInTriangle(p, apex, baseL, baseR))
                {
                    px[y * W + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float dMin = Mathf.Min(
                    DistToSeg(p, apex, baseL),
                    Mathf.Min(DistToSeg(p, baseL, baseR), DistToSeg(p, baseR, apex)));
                float a = Mathf.Clamp01(dMin);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
            return _arrowSprite;
        }

        private static Texture2D NewIconTex(int n) =>
            new Texture2D(n, n, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float s2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float s3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
            bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
            return !(hasNeg && hasPos);
        }

        private static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
