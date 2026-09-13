using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The name of the place, written across the upper screen the first time the player
    /// enters it: letter by letter, a thin gold rule drawing out beneath it, then held, then
    /// gone. Nothing in the game said where the player was before this except the minimap's
    /// caption, and a border crossed on foot deserves a sentence.
    ///
    /// Drawn in the HUD's pixel face at three texels per glyph pixel, on the HUD's own
    /// whole-pixel grid, so it belongs to the same instrument as the panel and the bars. It
    /// listens to <see cref="ZoneManager.OnZoneChanged"/> and announces the zone it finds
    /// itself in when it first binds — the town on spawn is the first place worth naming.
    /// </summary>
    public sealed class ZoneBannerHUD : MonoBehaviour
    {
        private const int   SortingOrder  = 108;
        private const int   TextScale     = 3;
        private const float TopFraction   = 0.20f;
        private const int   WidthTexels   = 200;
        private const int   GlyphHeight   = 5;

        public const float RevealSeconds = 0.9f;
        public const float HoldSeconds   = 1.9f;
        public const float FadeSeconds   = 0.7f;

        /// <summary>Texels the rule runs past the word on each side.</summary>
        private const int RulePadding = 6;

        private static readonly Color Gold = new Color(0.96f, 0.85f, 0.55f, 1f);

        private Canvas        _canvas;
        private RectTransform _root;
        private RectTransform _pixels;
        private HudPixelText  _name;
        private Image         _rule;
        private ZoneManager   _zones;
        private bool          _bound;
        private float         _lastScale = -1f;

        private readonly Dictionary<string, float> _shown = new Dictionary<string, float>();
        private string _text = "";
        private float  _t;
        private bool   _playing;

        /// <summary>The banner is on screen. Test seam.</summary>
        public bool IsPlaying => _playing;

        /// <summary>The letters shown so far. Test seam.</summary>
        public string ShownText => _name != null ? _name.Text : "";

        private void Awake() => EnsureBuilt();

        /// <summary>Build the canvas. Public for Edit Mode, where Awake never runs.</summary>
        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("ZoneBannerCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            HudLayout.ApplyScaler(canvasGo.AddComponent<CanvasScaler>());
            // No raycaster: a banner is read, never clicked.

            _root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(canvasGo.transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot     = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -HudLayout.ReferenceHeight * TopFraction);
            _root.sizeDelta = Vector2.zero;

            _pixels = new GameObject("Pixels", typeof(RectTransform)).GetComponent<RectTransform>();
            _pixels.SetParent(_root, false);
            _pixels.anchorMin = _pixels.anchorMax = new Vector2(0.5f, 0.5f);
            _pixels.pivot     = new Vector2(0.5f, 0.5f);
            _pixels.sizeDelta = Vector2.zero;

            var art = HudArt.Get();
            _name = HudPixelText.Create(_pixels, "Name", art, HudFontFace.Small, HudTextAlign.Centre,
                                        -WidthTexels / 2, 0, WidthTexels, GlyphHeight);
            _name.SetColour(Gold);

            _rule = HudRect.MakeImage("Rule", _pixels, null, -WidthTexels / 2, -3, WidthTexels, 1);
            _rule.color = Gold;
            _rule.rectTransform.pivot = new Vector2(0.5f, 0f);
            _rule.rectTransform.anchoredPosition = new Vector2(0f, -3f);

            SetVisible(false);
        }

        private void Update()
        {
            Bind();
            ApplyScale();
            Tick(Time.unscaledDeltaTime);
        }

        private void Bind()
        {
            if (_bound) return;
            if (_zones == null) _zones = FindObjectOfType<ZoneManager>();
            if (_zones == null) return;
            _bound = true;
            _zones.OnZoneChanged += OnZoneChanged;
            Announce(_zones.CurrentZone);
        }

        private void OnDestroy()
        {
            if (_zones != null) _zones.OnZoneChanged -= OnZoneChanged;
        }

        private void OnZoneChanged(string oldZone, string newZone) => Announce(newZone);

        /// <summary>Show <paramref name="zone"/>'s name if the rules say it is due.</summary>
        public void Announce(string zone)
        {
            if (!ZoneBannerRules.ShouldAnnounce(zone, Time.unscaledTime, _shown)) return;
            Play(ZoneBannerRules.Humanize(zone));
        }

        /// <summary>Write <paramref name="text"/> across the screen now. Public for the fixture.</summary>
        public void Play(string text)
        {
            EnsureBuilt();
            _text    = text ?? "";
            _t       = 0f;
            _playing = _text.Length > 0;
            if (_name != null)
            {
                // Measure the whole word once so the rule is as wide as the name and not as
                // wide as the screen — measured, a 200-texel rule spanned three quarters of it.
                _name.SetText(_text);
                int ink = Mathf.Max(8, _name.InkWidth + RulePadding * 2);
                _rule.rectTransform.sizeDelta = new Vector2(ink, 1f);
                _name.SetText("");
            }
            SetVisible(_playing);
            if (_playing) Tick(0f);
        }

        /// <summary>Advance the banner. Public so a test can run a showing out.</summary>
        public void Tick(float dt)
        {
            if (!_playing || _name == null) return;
            _t += dt;

            float reveal = Mathf.Clamp01(_t / RevealSeconds);
            int chars = Mathf.CeilToInt(reveal * _text.Length);
            _name.SetText(_text.Substring(0, Mathf.Clamp(chars, 0, _text.Length)));

            // The rule draws out under the letters, slightly ahead of them.
            _rule.rectTransform.localScale = new Vector3(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / (RevealSeconds * 0.8f))), 1f, 1f);

            float fadeStart = RevealSeconds + HoldSeconds;
            float alpha = 1f;
            if (_t > fadeStart) alpha = 1f - Mathf.Clamp01((_t - fadeStart) / FadeSeconds);

            var c = Gold; c.a = alpha;
            _name.SetColour(c);
            _rule.color = c;

            if (_t >= fadeStart + FadeSeconds)
            {
                _playing = false;
                SetVisible(false);
            }
        }

        private void SetVisible(bool on)
        {
            if (_pixels != null && _pixels.gameObject.activeSelf != on) _pixels.gameObject.SetActive(on);
        }

        private void ApplyScale()
        {
            if (_pixels == null) return;
            float root = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            int   px   = PlayerHudStyle.Active.HudPixelScaleFor(Screen.width, Screen.height);
            float s    = px * TextScale / root;
            if (Mathf.Approximately(s, _lastScale)) return;
            _lastScale = s;
            _pixels.localScale = new Vector3(s, s, 1f);
        }
    }
}
