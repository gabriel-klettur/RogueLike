using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The dial's chrome, built in code: drop shadow, the map disc with its three quad layers,
    /// the bevelled ring and its flash overlay, cardinal letters, three buttons that surface on
    /// hover, and the plate with the zone name and a live subtitle.
    ///
    /// <para><b>No Mask.</b> The previous build clipped the map with a stencil Mask on a
    /// circle sprite, which gives an aliased edge and forces a material copy per graphic. The
    /// composite shader antialiases its own circle; glyphs are kept inside the rim by the
    /// projection; the ring's inner lip overlaps the map's edge so no seam can show.</para>
    /// </summary>
    public sealed partial class MinimapHUD
    {
        private const float DISC_SIZE     = Valkur.Core.UI.HudLayout.TopRightColumnWidth;
        private const float MAP_FRACTION  = 0.925f;   // of the disc; just under the ring's lip
        private const float INFO_BAND_H   = 40f;
        private const float INFO_BAND_GAP = 4f;
        private const float BUTTON_SIZE   = 20f;

        private Canvas              _canvas;
        private RectTransform       _root;
        private RectTransform       _discRt;
        private RawImage            _mapImage;
        private MinimapDiscInput    _discInput;
        private MinimapQuadGraphic  _fxUnder, _glyphs, _fxOver;
        private Image               _ring, _ringFlash, _plate;
        private CanvasGroup         _buttons;
        private CanvasGroup         _dialGroup;
        private TextMeshProUGUI     _zoneLabel, _coordsLabel;
        private TextMeshProUGUI[]   _cardinals;
        private CanvasGroup         _scaleGroup;
        private RectTransform       _scaleBar;
        private TextMeshProUGUI     _scaleLabel;
        private float               _scaleShow;
        private float               _scaleSeenRadius = -1f;

        private void BuildUI()
        {
            var canvasGo = new GameObject("MinimapHUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Valkur.Core.UI.HudLayout.ReferenceWidth, Valkur.Core.UI.HudLayout.ReferenceHeight);
            scaler.matchWidthOrHeight  = Valkur.Core.UI.HudLayout.Match;
            // The raycaster is what makes the pointer over the dial count as "over UI", which is
            // how the combat poll and the camera wheel know to leave it alone.
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = NewRect("Root", canvasGo.transform);
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-MARGIN_RIGHT, -MARGIN_TOP);
            _root.sizeDelta = new Vector2(DISC_SIZE, DISC_SIZE + INFO_BAND_GAP + INFO_BAND_H);
            _dialGroup = _root.gameObject.AddComponent<CanvasGroup>();

            BuildShadow();
            BuildMap();
            BuildRing();
            BuildCardinals();
            BuildButtons();
            BuildScaleBar();
            BuildPlate();
            BuildOverlays();
        }

        /// <summary>
        /// A scale bar that surfaces for a moment after the zoom changes: the one question a
        /// zoomed map raises ("how far is that?") answered where the eye already is, and gone
        /// again before it becomes furniture.
        /// </summary>
        private void BuildScaleBar()
        {
            var root = NewRect("Scale", _root);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = DiscCentre + new Vector2(0f, -DISC_SIZE * 0.29f);
            root.sizeDelta = new Vector2(90f, 22f);
            _scaleGroup = root.gameObject.AddComponent<CanvasGroup>();
            _scaleGroup.alpha = 0f;
            _scaleGroup.blocksRaycasts = false;

            var back = AddImage(root, "Back", MinimapIconAtlas.SpriteOf(MinimapIcon.Glow), new Color(0f, 0f, 0f, 0.55f));
            var br = back.rectTransform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0.5f);
            br.sizeDelta = new Vector2(96f, 34f);
            back.raycastTarget = false;

            var bar = AddImage(root, "Bar", MinimapIconAtlas.SpriteOf(MinimapIcon.Bar), _style.ringHighlight);
            _scaleBar = bar.rectTransform;
            _scaleBar.anchorMin = _scaleBar.anchorMax = _scaleBar.pivot = new Vector2(0.5f, 0.5f);
            _scaleBar.anchoredPosition = new Vector2(0f, -5f);
            _scaleBar.sizeDelta = new Vector2(40f, 2f);
            bar.raycastTarget = false;
            foreach (float side in new[] { -1f, 1f })
            {
                var tick = AddImage(_scaleBar, "Tick", MinimapIconAtlas.SpriteOf(MinimapIcon.Bar), _style.ringHighlight);
                var tr = tick.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.anchoredPosition = Vector2.zero;
                tr.sizeDelta = new Vector2(2f, 7f);
                tick.raycastTarget = false;
            }

            _scaleLabel = AddLabel(root, "Label", 10f, FontStyles.Bold, _style.ringHighlight);
            _scaleLabel.alignment = TextAlignmentOptions.Center;
            _scaleLabel.enableWordWrapping = false;
            _scaleLabel.outlineWidth = 0.2f;
            _scaleLabel.outlineColor = new Color32(0, 0, 0, 220);
            var lr = _scaleLabel.rectTransform;
            lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 0.5f);
            lr.anchoredPosition = new Vector2(0f, 5f);
            lr.sizeDelta = new Vector2(80f, 12f);
        }

        private const int ScaleStepCount = 7;
        private static float ScaleStep(int i)
        {
            switch (i)
            {
                case 0: return 2f;  case 1: return 5f;  case 2: return 10f; case 3: return 20f;
                case 4: return 25f; case 5: return 50f; default: return 100f;
            }
        }
        private float _scaleUnitsShown = -1f;

        private void UpdateScaleBar(float dt)
        {
            if (_scaleGroup == null || _view == null) return;
            float target = _manager.ViewRadius;
            if (_scaleSeenRadius >= 0f && !Mathf.Approximately(target, _scaleSeenRadius)) _scaleShow = 1.8f;
            _scaleSeenRadius = target;
            _scaleShow = Mathf.Max(0f, _scaleShow - dt);
            _scaleGroup.alpha = Mathf.Clamp01(_scaleShow / 0.4f);
            if (_scaleShow <= 0f) return;

            // The longest round distance that fits in ~70 px at the CURRENT (eased) zoom, so the
            // bar grows and shrinks with the map as it settles.
            float upw = _view.UnitsPerWorld;
            float units = ScaleStep(0);
            for (int i = 0; i < ScaleStepCount; i++) if (ScaleStep(i) * upw <= 70f) units = ScaleStep(i);
            _scaleBar.sizeDelta = new Vector2(Mathf.Max(8f, units * upw), 2f);
            if (!Mathf.Approximately(units, _scaleUnitsShown))
            {
                _scaleUnitsShown = units;
                _scaleLabel.text = units.ToString("0") + " m";
            }
        }

        private Vector2 DiscCentre => new Vector2(0f, -DISC_SIZE * 0.5f);

        private void BuildShadow()
        {
            var img = AddImage(_root, "DiscShadow", MinimapChromeSprites.DiscShadow(), new Color(1f, 1f, 1f, 0.72f));
            Place(img.rectTransform, DiscCentre + new Vector2(2f, -4f), DISC_SIZE + 18f);
            img.raycastTarget = false;
        }

        private void BuildMap()
        {
            _discRt = NewRect("Disc", _root);
            Place(_discRt, DiscCentre, DISC_SIZE * MAP_FRACTION);

            _mapImage = _discRt.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = true;
            _discInput = _discRt.gameObject.AddComponent<MinimapDiscInput>();
            _discInput.Clicked += ToggleWorldMap;

            _fxUnder = AddQuadLayer(_discRt, "FxUnder", _additiveMaterial);
            _glyphs  = AddQuadLayer(_discRt, "Glyphs", null);
            _fxOver  = AddQuadLayer(_discRt, "FxOver", _additiveMaterial);
        }

        private static MinimapQuadGraphic AddQuadLayer(RectTransform parent, string name, Material material)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var g = rt.gameObject.AddComponent<MinimapQuadGraphic>();
            g.raycastTarget = false;
            if (material != null) g.material = material;
            return g;
        }

        private void BuildRing()
        {
            _ring = AddImage(_root, "Ring", MinimapChromeSprites.Ring(_style), Color.white);
            Place(_ring.rectTransform, DiscCentre, DISC_SIZE);
            _ring.raycastTarget = false;

            _ringFlash = AddImage(_root, "RingFlash", MinimapChromeSprites.RingFlash(_style), new Color(1f, 1f, 1f, 0f));
            Place(_ringFlash.rectTransform, DiscCentre, DISC_SIZE);
            _ringFlash.raycastTarget = false;
            if (_additiveMaterial != null) _ringFlash.material = _additiveMaterial;
        }

        private void BuildCardinals()
        {
            float r = DISC_SIZE * 0.5f * (MinimapChromeSprites.RingInner + MinimapChromeSprites.RingOuter) * 0.5f;
            _cardinals = new[]
            {
                BuildCardinal("N", 90f, r, _style.northColor, 13f),
                BuildCardinal("E", 0f, r, _style.cardinalColor, 10f),
                BuildCardinal("S", 270f, r, _style.cardinalColor, 10f),
                BuildCardinal("O", 180f, r, _style.cardinalColor, 10f),
            };
        }

        private TextMeshProUGUI BuildCardinal(string glyph, float angleDeg, float radius, Color color, float size)
        {
            // A dark stud under the letter so it reads over the bright bevel.
            float a = angleDeg * Mathf.Deg2Rad;
            Vector2 pos = DiscCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            var stud = AddImage(_root, "Stud" + glyph, MinimapChromeSprites.Button(_style), Color.white);
            Place(stud.rectTransform, pos, size + 5f);
            stud.raycastTarget = false;

            var tmp = AddLabel(_root, "Cardinal" + glyph, size, FontStyles.Bold, color);
            tmp.text = glyph;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = new Color32(12, 8, 4, 255);
            Place(tmp.rectTransform, pos + new Vector2(0f, 0.5f), 18f);
            return tmp;
        }

        private void BuildButtons()
        {
            var groupRt = NewRect("Buttons", _root);
            groupRt.anchorMin = groupRt.anchorMax = new Vector2(0f, 1f);
            groupRt.pivot = new Vector2(0f, 1f);
            groupRt.anchoredPosition = Vector2.zero;
            groupRt.sizeDelta = new Vector2(DISC_SIZE, DISC_SIZE);
            _buttons = groupRt.gameObject.AddComponent<CanvasGroup>();
            _buttons.alpha = 0f;
            _buttons.interactable = true;
            _buttons.blocksRaycasts = true;

            float r = DISC_SIZE * 0.5f * 0.99f;
            // Local to the group, whose pivot is the top-left of the disc's square.
            Vector2 centre = new Vector2(DISC_SIZE * 0.5f, -DISC_SIZE * 0.5f);
            BuildButton(groupRt, "ZoomIn",  MinimapIcon.Plus,    centre, -22f, r, () => _manager.AdjustZoom(-1));
            BuildButton(groupRt, "ZoomOut", MinimapIcon.Minus,   centre, -44f, r, () => _manager.AdjustZoom(1));
            BuildButton(groupRt, "WorldMap", MinimapIcon.MapFold, centre, 214f, r, ToggleWorldMap);
        }

        private void BuildButton(RectTransform parent, string name, MinimapIcon icon, Vector2 centre, float angleDeg,
                                 float radius, UnityEngine.Events.UnityAction onClick)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            Vector2 pos = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;

            var img = AddImage(parent, name, MinimapChromeSprites.Button(_style), Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(BUTTON_SIZE, BUTTON_SIZE);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.8f, 1f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.5f, 1f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var glyph = AddImage(rt, "Icon", MinimapIconAtlas.SpriteOf(icon), _style.ringHighlight);
            var grt = glyph.rectTransform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta = new Vector2(BUTTON_SIZE * 0.72f, BUTTON_SIZE * 0.72f);
            glyph.raycastTarget = false;
        }

        private void BuildPlate()
        {
            _plate = AddImage(_root, "InfoPlate", MinimapChromeSprites.Plate(_style), Color.white);
            _plate.type = Image.Type.Sliced;
            _plate.pixelsPerUnitMultiplier = 2f;
            var rt = _plate.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(-8f, INFO_BAND_H);
            _plate.raycastTarget = false;

            _zoneLabel = AddLabel(_root, "ZoneLabel", 14f, FontStyles.Bold, UITheme.TEXT_PRIMARY);
            var zr = _zoneLabel.rectTransform;
            zr.anchorMin = new Vector2(0f, 0f); zr.anchorMax = new Vector2(1f, 0f);
            zr.pivot = new Vector2(0.5f, 0f);
            zr.anchoredPosition = new Vector2(0f, INFO_BAND_H - 21f);
            zr.sizeDelta = new Vector2(-18f, 17f);
            _zoneLabel.alignment = TextAlignmentOptions.Center;
            _zoneLabel.enableWordWrapping = false;
            _zoneLabel.overflowMode = TextOverflowModes.Ellipsis;
            // Shrink before truncating: a long place name is still a name, "House Interior
            // Small...." is not.
            _zoneLabel.enableAutoSizing = true;
            _zoneLabel.fontSizeMin = 10f;
            _zoneLabel.fontSizeMax = 14f;
            _zoneLabel.characterSpacing = 2f;
            _zoneLabel.outlineWidth = 0.18f;
            _zoneLabel.outlineColor = new Color32(0, 0, 0, 200);
            _zoneLabel.text = "—";

            _coordsLabel = AddLabel(_root, "SubtitleLabel", 10.5f, FontStyles.Normal, UITheme.TEXT_SECONDARY);
            var cr = _coordsLabel.rectTransform;
            cr.anchorMin = new Vector2(0f, 0f); cr.anchorMax = new Vector2(1f, 0f);
            cr.pivot = new Vector2(0.5f, 0f);
            cr.anchoredPosition = new Vector2(0f, 5f);
            cr.sizeDelta = new Vector2(-14f, 13f);
            _coordsLabel.alignment = TextAlignmentOptions.Center;
            _coordsLabel.enableWordWrapping = false;
            _coordsLabel.overflowMode = TextOverflowModes.Ellipsis;
            _coordsLabel.richText = true;
            _coordsLabel.text = string.Empty;
        }

        // ── Per-frame chrome ────────────────────────────────────────────────

        private void UpdateChrome(float dt, float now)
        {
            if (_ringFlash != null)
            {
                var c = _style.damageFlash;
                c.a = _flash * _flash * 0.9f;
                _ringFlash.color = c;
            }

            if (_ring != null)
            {
                // A spirit sees the dial cold: the gold drains toward moonlight.
                var target = _scene.PlayerIsSpirit ? new Color(0.72f, 0.84f, 1f, 1f) : Color.white;
                _ring.color = Color.Lerp(_ring.color, target, 1f - Mathf.Pow(0.02f, dt));
                if (_cardinals != null)
                    for (int i = 0; i < _cardinals.Length; i++)
                    {
                        var baseCol = i == 0 ? _style.northColor : _style.cardinalColor;
                        _cardinals[i].color = Color.Lerp(_cardinals[i].color, baseCol * target, 1f - Mathf.Pow(0.02f, dt));
                    }
            }

            bool hover = _discInput != null && _discInput.Hovered
                         || RectTransformUtility.RectangleContainsScreenPoint(_root, Valkur.Core.Input.MouseInputManager.GetScreenMousePosition(), null);
            _hoverAlpha = Mathf.MoveTowards(_hoverAlpha, hover ? 1f : 0f, dt * 6f);
            if (_buttons != null)
            {
                _buttons.alpha = _hoverAlpha;
                _buttons.blocksRaycasts = _hoverAlpha > 0.5f;
            }

            UpdateScaleBar(dt);

            // The world map shows everything the dial does, larger; the dial peeking out beside
            // its frame is the same information twice.
            if (_dialGroup != null)
                _dialGroup.alpha = Mathf.MoveTowards(_dialGroup.alpha, WorldMapOpen ? 0f : 1f, dt * 6f);

            if (_zoneLabel != null)
            {
                _zoneFlash = Mathf.Max(0f, _zoneFlash - dt / 1.6f);
                _zoneLabel.color = Color.Lerp(UITheme.TEXT_PRIMARY, _style.ringHighlight, _zoneFlash);
                float punch = 1f + 0.12f * _zoneFlash * _zoneFlash;
                _zoneLabel.rectTransform.localScale = new Vector3(punch, punch, 1f);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void Place(RectTransform rt, Vector2 centre, float size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
            rt.sizeDelta = new Vector2(size, size);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image AddImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI AddLabel(RectTransform parent, string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
