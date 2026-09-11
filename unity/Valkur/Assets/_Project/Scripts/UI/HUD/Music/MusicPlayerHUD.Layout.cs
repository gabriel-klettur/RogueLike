using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // Rows of the plaque, in texels from its bottom edge. Every widget reads these, so a row
        // moves as one piece.
        private const int Inset = 4;
        private const int TransportY = 4, TransportH = 9;
        private const int GrooveRowY = 15, RowH = 7;
        private const int ZoneRowY = 24;
        private const int TitleRowY = 31;
        private const int MedallionSize = 15, MedallionY = 23;
        private const int TimeBoxW = 17;
        private const int KeyW = 11, PlayKeyW = 13, SmallKey = 7;

        private HudPixelText _title, _zone, _status, _timeNow, _timeTotal, _volumeLabel;
        private Image _medallionFace, _medallionRim, _medallionGlow, _sigil, _shine;
        private MusicGroove _groove;
        private MusicVolumeNotches _volume;
        private MusicHudKey _prev, _play, _next, _mute, _resonanceKey, _close;
        private RectTransform _resonanceRoot;
        private MusicResonanceGraphic _resonance;
        private MusicSpectrum _spectrum;
        private HudMoteLayer _motes;
        private HudTooltip _tooltip;
        private int _titleX, _titleMaxW;

        private int _pixelScale;
        private int _screenW = -1, _screenH = -1;
        private float _rootScale = -1f;

        private void BuildCanvas()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            // A root canvas of its own, on the HUD's contract: same reference, same match, and a
            // sorting band declared beside every other HUD surface. The old canvas used Unity's
            // default 800x600 at match 0 — a scale of 2.0 against the rest of the HUD's 1.0.
            if (_canvas.isRootCanvas || transform.parent == null || transform.parent.GetComponentInParent<Canvas>() == null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(HudLayout.ReferenceWidth, HudLayout.ReferenceHeight);
                scaler.matchWidthOrHeight = HudLayout.Match;
            }
            else
            {
                _canvas.overrideSorting = true;
            }
            _canvas.sortingOrder = HudLayout.MusicSortingOrder;
            _canvas.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            var shader = _theme.hudFxShader != null ? _theme.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "MusicHudAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }
        }

        private void BuildWindow()
        {
            var go = new GameObject("Window", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _window = (RectTransform)go.transform;
            // Anchored and pivoted on the bottom-right: the panel docks there, and grows UPWARD
            // when the resonance opens, away from the tray under it.
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(1f, 0f);
            _group = go.AddComponent<CanvasGroup>();

            _pixels = HudRect.Make("Pixels", _window, 0, 0, _style.widthTexels, _style.plaqueTexels);
            // The whole plaque is the drag surface, so it carries the one invisible hit image; a
            // key or the groove on top of it answers its own clicks first.
            var hit = _pixels.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var drag = _pixels.gameObject.AddComponent<MusicHudPointer>();
            drag.CapturesDrag = true;
            drag.DragDelta = MoveBy;
            drag.EndDrag = SaveDock;
            drag.Scroll = dy => _volume?.Step(dy > 0f ? 1 : dy < 0f ? -1 : 0);

            int w = _style.widthTexels;
            _stoneCompact = MusicHudArt.BakePlaque(w, _style.plaqueTexels, 0, _theme);
            _stoneExpanded = MusicHudArt.BakePlaque(w, _style.HeightTexels(true), _style.plaqueTexels, _theme);
            var stoneRt = HudRect.Make("Stone", _pixels, 0, 0, w, _style.plaqueTexels);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            _stone.texture = _stoneCompact;
            _stone.raycastTarget = false;
        }

        private void BuildLayout()
        {
            var s = _style;
            var t = _theme;
            int w = s.widthTexels;

            // -- Medallion: the zone's sigil in a stone ring that turns gold for a new track.
            _medallionGlow = HudRect.MakeImage("MedallionGlow", _pixels, _art.MedallionGlow,
                                               Inset - 4, MedallionY - 4, 23, 23);
            if (_additive != null) _medallionGlow.material = _additive;
            _medallionGlow.color = new Color(t.gold.r, t.gold.g, t.gold.b, 0f);
            _medallionFace = HudRect.MakeImage("MedallionFace", _pixels, _art.MedallionFace,
                                               Inset, MedallionY, MedallionSize, MedallionSize);
            _sigil = HudRect.MakeImage("Sigil", _pixels, _art.SigilFor(MusicSigil.Note),
                                       Inset + 3, MedallionY + 3, 9, 9);
            _sigil.color = t.textDim;
            _medallionRim = HudRect.MakeImage("MedallionRim", _pixels, _art.MedallionRim,
                                              Inset, MedallionY, MedallionSize, MedallionSize);
            _medallionRim.color = t.stoneLight;

            // -- Title and zone lines, right of the medallion.
            _titleX = Inset + MedallionSize + 3;
            int closeX = w - Inset - SmallKey;
            _titleMaxW = closeX - 2 - _titleX;
            _title = HudPixelText.Create(_pixels, "Title", _hudArt, HudFontFace.Small, HudTextAlign.Left,
                                         _titleX, TitleRowY, _titleMaxW, RowH);
            _title.SetColour(t.text);
            _shine = HudRect.MakeImage("TitleShine", _pixels, _art.Shine, _titleX, TitleRowY, 3, RowH);
            if (_additive != null) _shine.material = _additive;
            _shine.enabled = false;

            int zoneW = w - Inset - _titleX;
            _zone = HudPixelText.Create(_pixels, "Zone", _hudArt, HudFontFace.Small, HudTextAlign.Left,
                                        _titleX, ZoneRowY, zoneW, RowH);
            _zone.SetColour(t.textDim);
            _status = HudPixelText.Create(_pixels, "Status", _hudArt, HudFontFace.Small, HudTextAlign.Right,
                                          _titleX, ZoneRowY, zoneW, RowH);
            _status.SetColour(t.text);

            _close = new MusicHudKey(_pixels, _art, "CloseKey", closeX, TitleRowY, SmallKey, SmallKey, _art.Close);
            _close.SetInk(t.textDim, t.textDim);
            _close.Clicked += () => SetHidden(true);

            // -- Groove row: time played, the groove, the length.
            _timeNow = HudPixelText.Create(_pixels, "TimeNow", _hudArt, HudFontFace.Small, HudTextAlign.Right,
                                           Inset, GrooveRowY, TimeBoxW, RowH);
            _timeNow.SetColour(t.text);
            int grooveX = Inset + TimeBoxW + 2;
            int totalX = w - Inset - TimeBoxW;
            _groove = new MusicGroove(_pixels, _art, grooveX, GrooveRowY, totalX - 2 - grooveX, RowH,
                                      Color.Lerp(t.stoneLight, Color.white, 0.42f));
            _groove.SeekCommitted += OnSeek;
            _groove.Hovered += OnGrooveHover;
            _timeTotal = HudPixelText.Create(_pixels, "TimeTotal", _hudArt, HudFontFace.Small, HudTextAlign.Left,
                                             totalX, GrooveRowY, TimeBoxW, RowH);
            _timeTotal.SetColour(t.textDim);

            // -- Transport: previous, play (the one wider key), next; then the volume.
            int x = Inset;
            _prev = new MusicHudKey(_pixels, _art, "PrevKey", x, TransportY, KeyW, TransportH, _art.Prev);
            x += KeyW + 1;
            _play = new MusicHudKey(_pixels, _art, "PlayKey", x, TransportY, PlayKeyW, TransportH, _art.Play);
            x += PlayKeyW + 1;
            _next = new MusicHudKey(_pixels, _art, "NextKey", x, TransportY, KeyW, TransportH, _art.Next);
            x += KeyW + 4;
            _mute = new MusicHudKey(_pixels, _art, "MuteKey", x, TransportY, KeyW + 1, TransportH, _art.SpeakerOn);
            x += KeyW + 1 + 3;
            var dim = new Color(t.textDim.r, t.textDim.g, t.textDim.b, 0.35f);
            _prev.SetInk(t.textDim, dim);
            _play.SetInk(t.text, dim);
            _next.SetInk(t.textDim, dim);
            _mute.SetInk(t.textDim, dim);
            _prev.Clicked += OnPrevious;
            _play.Clicked += OnPlayPause;
            _next.Clicked += OnNext;
            _mute.Clicked += OnMuteToggle;

            // An OFF notch is a notch, not a hole: at the recess colour the row read as one black bar
            // at 0 %, which says nothing about how far it could go.
            _volume = new MusicVolumeNotches(_pixels, _art, x, TransportY + 1, 8, s.notchOn,
                                             Color.Lerp(t.recess, t.stoneLight, 0.55f));
            _volume.LevelPicked += ApplyAndPersistVolume;
            x += _volume.Root.sizeDelta.x > 0 ? Mathf.RoundToInt(_volume.Root.sizeDelta.x) + 3 : 28;
            _volumeLabel = HudPixelText.Create(_pixels, "VolumeLabel", _hudArt, HudFontFace.Small, HudTextAlign.Left,
                                               x, TransportY + 1, 17, RowH);
            _volumeLabel.SetColour(t.textDim);

            _resonanceKey = new MusicHudKey(_pixels, _art, "ResonanceKey", w - Inset - KeyW, TransportY,
                                            KeyW, TransportH, _art.Resonance);
            _resonanceKey.SetInk(t.textDim, dim);
            _resonanceKey.Clicked += () => SetExpanded(!_expanded);

            // -- Resonance: its own well above the plaque, shown only while open.
            _resonanceRoot = HudRect.Make("ResonanceSlab", _pixels, 0, s.plaqueTexels, w, s.resonanceTexels);
            int wellH = s.resonanceTexels - 5;
            HudRect.MakeImage("ResonanceWell", _resonanceRoot, _art.Well, Inset, 2, w - Inset * 2, wellH, Image.Type.Sliced);
            _resonance = MusicResonanceGraphic.Create(_resonanceRoot, _art, s, Inset + 1, 3, w - Inset * 2 - 2, wellH - 2);
            _spectrum = new MusicSpectrum(s.bandCount);

            // -- Hover information over the medallion and the title.
            var info = HudRect.Make("InfoHover", _pixels, Inset, MedallionY, closeX - 2 - Inset, MedallionSize);
            var infoHit = info.gameObject.AddComponent<Image>();
            infoHit.color = Color.clear;
            var infoPointer = info.gameObject.AddComponent<MusicHudPointer>();
            infoPointer.Enter = ShowTrackTooltip;
            infoPointer.Exit = HideTooltip;

            WireKeyTooltips();

            _motes = HudMoteLayer.Create(_pixels, _hudArt, s.moteCapacity, _additive);
            _tooltip = new HudTooltip(_pixels, _hudArt, s.plaqueTexels);
            // Hover targets and keys must stay under the motes and the tooltip.
            _motes.transform.SetAsLastSibling();
            _tooltip.Root.SetAsLastSibling();
        }

        private void ApplyExpanded(bool force = false)
        {
            if (_resonanceRoot == null) return;
            int h = _style.HeightTexels(_expanded);
            HudRect.Place(_pixels, 0, 0, _style.widthTexels, h);
            HudRect.Place(_stone.rectTransform, 0, 0, _style.widthTexels, h);
            _stone.texture = _expanded ? _stoneExpanded : _stoneCompact;
            _resonanceRoot.gameObject.SetActive(_expanded);
            _resonanceKey.SetLatched(_expanded);
            if (_expanded) BindBeatClock(); else UnbindBeatClock();
            Refit(force: true);
        }

        /// <summary>
        /// Re-derives the whole-pixel scale and the panel's footprint when the screen or the
        /// canvas scale changed. The corner is placed on a whole screen pixel, so every texel
        /// edge inside lands on one too.
        /// </summary>
        public void Refit(bool force = false)
        {
            if (_window == null) return;
            int sw = Screen.width, sh = Screen.height;
            float root = _canvas != null && _canvas.rootCanvas != null && _canvas.rootCanvas.scaleFactor > 0f
                ? _canvas.rootCanvas.scaleFactor : 1f;
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale)) return;
            _screenW = sw;
            _screenH = sh;
            _rootScale = root;

            _pixelScale = _theme.HudPixelScaleFor(sw, sh);
            float perTexel = _pixelScale / root;
            // The Pixels child is the ONE object that carries the texel-to-canvas ratio; every
            // rect inside it is placed in whole texels at unit scale.
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            _window.sizeDelta = new Vector2(_style.widthTexels * perTexel, _style.HeightTexels(_expanded) * perTexel);
            ApplyDock();
        }
    }
}
