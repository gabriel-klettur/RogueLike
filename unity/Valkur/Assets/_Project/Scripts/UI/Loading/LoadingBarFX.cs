using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// The loading bar: a rectangular gold housing divided into the boot's ETAPAS, a fill that
    /// reads as molten light, and particles that answer what the bar is doing.
    ///
    /// <para><b>Layers, back to front</b> — uGUI draws in hierarchy order, so this list IS the
    /// sorting: a halo breathing behind the whole bar; the frame (<see cref="BevelFrameGraphic"/>, with side gems);
    /// the fill (a vertical-gradient <c>Image.Type.Filled</c> — the type the controller's
    /// fixtures pin — and an additive gloss on its upper half); the etapas
    /// (<see cref="LoadingBarSegmentsGraphic"/>: dividers, notches, the target zone, the flow);
    /// a sheen travelling along the fill; the leading edge (a white-hot core line, a glow and a
    /// horizontal lens streak); pooled flashes for events; and the particle layer on top.</para>
    ///
    /// <para><b>Particles answer EVENTS and MOTION, never rest</b> — the rule
    /// <see cref="MenuFxLayer"/> writes down. Sparks spray off the leading edge in proportion to
    /// how fast it is moving, so a bar that stalls stops throwing them, which is itself the signal.
    /// Embers rise off the filled part, more of them the further the load has come. An etapa
    /// completing bursts at its notch and flashes across its span; the boot becoming READY bursts
    /// along the whole bar and lights the end gem. Nothing else emits.</para>
    ///
    /// <para><b>The fill tint is read back from the fill every tick</b>, because the controller
    /// owns it: amber while the boot is stalled, red when it failed. Sparks, embers, gems and the
    /// halo all take that colour, so the warning is the whole bar changing mood rather than one
    /// rectangle changing colour.</para>
    ///
    /// <para>Its own delta, sub-stepped, <c>unscaledDeltaTime</c> — the same contract as
    /// <see cref="LoadingFireFX"/>, for the same boot running at a dozen frames a second.</para>
    /// </summary>
    public sealed class LoadingBarFX
    {
        private const float MaxStep = 1f / 30f;
        private const int MaxSubSteps = 6;

        private const float PadX = 90f;
        private const float PadBottom = 60f;
        private const float PadTop = 150f;

        private const int ParticleCapacity = 260;
        private const int FlashCount = 6;

        private static Color EmberDeep => FrontendPalette.EmberDeep;

        public RectTransform Root { get; }
        public Image Fill { get; }

        /// <summary>Bar fraction currently drawn.</summary>
        public float Progress => _progress;

        /// <summary>Live particles. For fixtures and the budget.</summary>
        public int ParticleCount => _particles != null ? _particles.Alive : 0;

        /// <summary>True once the boot has been declared ready and the finale has fired.</summary>
        public bool Completed => _completed;

        public int SegmentCount => _segments != null ? _segments.SegmentCount : 0;

        private readonly float _width;
        private readonly float _height;
        private readonly float _thickness;
        private readonly BevelFrameGraphic _frame;
        private readonly LoadingBarSegmentsGraphic _segments;
        private readonly Image _gloss;
        private readonly Image _halo;
        private readonly Image _sheen;
        private readonly RectTransform _edge;
        private readonly Image _edgeCore;
        private readonly Image _edgeGlow;
        private readonly Image _edgeFlare;
        private readonly MenuFxLayer _particles;
        private readonly Image[] _flashes = new Image[FlashCount];
        private readonly float[] _flashLife = new float[FlashCount];
        private readonly float[] _flashMax = new float[FlashCount];
        private readonly Vector2[] _flashSize = new Vector2[FlashCount];
        private readonly Material _additive;

        private float _progress;
        private float _lastTickProgress;
        private float _lastEventProgress;
        private float _activity;
        private float _time;
        private float _accumulator;
        private float _sparkDebt;
        private float _emberDebt;
        private float _completeFlash;
        private float _endGem;
        private float _frameGlow;
        private bool _completed;

        // ── Construction ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds the bar under <paramref name="parent"/>, anchored bottom-centre at
        /// <paramref name="anchoredPosition"/>, <paramref name="width"/> by <paramref name="height"/>.
        /// </summary>
        public static LoadingBarFX Build(Transform parent, Vector2 anchoredPosition, float width, float height,
                                         MenuStyle style, Color fillColor)
        {
            if (parent == null) return null;
            return new LoadingBarFX(parent, anchoredPosition, width, height, style, fillColor);
        }

        private LoadingBarFX(Transform parent, Vector2 anchoredPosition, float width, float height,
                             MenuStyle style, Color fillColor)
        {
            _width = width;
            _height = height;
            _thickness = Mathf.Clamp(Mathf.Round(height * 0.16f), 3f, 6f);
            var art = MenuArt.Get(style);
            // Shared with every menu surface: the additive material, and a glow whose alpha is
            // exactly zero at its rim (see FrontendKit for why it is not the title's mote).
            var kit = FrontendKit.Get(style);
            _additive = kit.Additive;
            Sprite soft = kit.Radial;

            var rootGo = new GameObject("LoadingBar", typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            Root = (RectTransform)rootGo.transform;
            Root.anchorMin = new Vector2(0.5f, 0f);
            Root.anchorMax = new Vector2(0.5f, 0f);
            Root.pivot = new Vector2(0.5f, 0f);
            Root.anchoredPosition = anchoredPosition;
            Root.sizeDelta = new Vector2(width, height);

            _halo = SoftImage(Root, "BarHalo", soft, fillColor, 0f);
            Place(_halo.rectTransform, new Vector2(width * 0.5f, height * 0.5f), new Vector2(width * 1.12f, height * 4.6f));

            _frame = BevelFrameGraphic.Create(Root, "BarFrame");
            _frame.SideGems = true;
            _frame.Thickness = _thickness;
            _frame.Tint = fillColor;

            var fillArea = new GameObject("BarFillArea", typeof(RectTransform));
            fillArea.transform.SetParent(Root, false);
            var fillAreaRt = (RectTransform)fillArea.transform;
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(_thickness, _thickness);
            fillAreaRt.offsetMax = new Vector2(-_thickness, -_thickness);

            var fillGo = new GameObject("BarFill", typeof(RectTransform));
            fillGo.transform.SetParent(fillArea.transform, false);
            Fill = fillGo.AddComponent<Image>();
            Fill.sprite = kit.BarRamp;
            Fill.color = fillColor;
            Fill.type = Image.Type.Filled;
            Fill.fillMethod = Image.FillMethod.Horizontal;
            Fill.fillOrigin = 0;
            Fill.fillAmount = 0f;
            Fill.raycastTarget = false;
            Stretch(Fill.rectTransform);

            var glossGo = new GameObject("BarGloss", typeof(RectTransform));
            glossGo.transform.SetParent(fillArea.transform, false);
            _gloss = glossGo.AddComponent<Image>();
            _gloss.sprite = kit.BarGloss;
            _gloss.type = Image.Type.Filled;
            _gloss.fillMethod = Image.FillMethod.Horizontal;
            _gloss.fillOrigin = 0;
            _gloss.fillAmount = 0f;
            _gloss.raycastTarget = false;
            _gloss.color = new Color(1f, 0.93f, 0.75f, 0.30f);
            if (_additive != null) _gloss.material = _additive;
            Stretch(_gloss.rectTransform);

            _segments = LoadingBarSegmentsGraphic.Create(Root);
            _segments.Thickness = _thickness;
            _segments.Tint = fillColor;
            _segments.SetStarts(null);

            _sheen = SoftImage(Root, "BarSheen", soft, Color.white, 0f);

            var edgeGo = new GameObject("BarEdge", typeof(RectTransform));
            edgeGo.transform.SetParent(Root, false);
            _edge = (RectTransform)edgeGo.transform;
            _edge.anchorMin = _edge.anchorMax = Vector2.zero;
            _edge.pivot = new Vector2(0.5f, 0.5f);
            _edge.sizeDelta = Vector2.zero;
            float ih = height - 2f * _thickness;
            _edgeFlare = SoftImage(_edge, "EdgeFlare", soft, Color.white, 0f);
            _edgeFlare.rectTransform.sizeDelta = new Vector2(ih * 8f, ih * 0.7f);
            _edgeGlow = SoftImage(_edge, "EdgeGlow", soft, Color.white, 0f);
            _edgeGlow.rectTransform.sizeDelta = new Vector2(ih * 2.3f, ih * 2.3f);
            var coreGo = new GameObject("EdgeCore", typeof(RectTransform));
            coreGo.transform.SetParent(_edge, false);
            _edgeCore = coreGo.AddComponent<Image>();
            _edgeCore.raycastTarget = false;
            if (_additive != null) _edgeCore.material = _additive;
            _edgeCore.color = new Color(1f, 1f, 1f, 0f);
            _edgeCore.rectTransform.sizeDelta = new Vector2(2f, ih + 2f);

            for (int i = 0; i < FlashCount; i++)
                _flashes[i] = SoftImage(Root, "BarFlash" + i, soft, Color.white, 0f);

            _particles = MenuFxLayer.Create(Root, art, ParticleCapacity, _additive);
            _particles.name = "BarParticles";
            _particles.UseSoftMotes = true;
            var prt = _particles.rectTransform;
            prt.offsetMin = new Vector2(-PadX, -PadBottom);
            prt.offsetMax = new Vector2(PadX, PadTop);

            PushVisuals();
        }

        // ── Driving ──────────────────────────────────────────────────────────

        /// <summary>The fraction to draw. The controller owns smoothing and monotonicity.</summary>
        public void SetProgress(float p)
        {
            _progress = Mathf.Clamp01(p);
            if (Fill != null) Fill.fillAmount = _progress;
            if (_gloss != null) _gloss.fillAmount = _progress;
        }

        /// <summary>Where each etapa starts on the bar (bar fractions, first is 0).</summary>
        public void SetSegments(IReadOnlyList<float> starts)
        {
            if (_segments == null) return;
            _segments.SetStarts(starts);
        }

        /// <summary>The boot is ready: light the end gem and throw the finale. Idempotent.</summary>
        public void Complete()
        {
            if (_completed) return;
            _completed = true;
            _completeFlash = 1f;
            _frameGlow = 1f;

            float ih = _height - 2f * _thickness;
            Color tint = Tint;
            Color hot = Color.Lerp(tint, Color.white, 0.6f);
            for (int i = 0; i < 90; i++)
            {
                float x = _thickness + Random.value * (_width - 2f * _thickness);
                float y = _thickness + Random.value * ih;
                float a = Random.Range(40f, 140f) * Mathf.Deg2Rad;
                float v = Random.Range(90f, 320f);
                EmitAt(new Vector2(x, y), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v,
                       Color.Lerp(hot, EmberDeep, Random.value * 0.5f), Random.Range(0.6f, 1.4f),
                       Random.value < 0.4f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                       gravity: 160f, drag: 1.3f, size: Random.Range(0.18f, 0.5f));
            }
            FireFlash(new Vector2(_width * 0.5f, _height * 0.5f), new Vector2(_width * 0.5f, _height * 2f),
                      new Vector2(_width * 1.25f, _height * 5f), 0.8f, Color.Lerp(tint, Color.white, 0.3f) * 0.6f);
            FireFlash(new Vector2(_width + 17f, _height * 0.5f), new Vector2(_height, _height),
                      new Vector2(_height * 6f, _height * 6f), 0.8f, hot);
        }

        private Color Tint => Fill != null ? Fill.color : Color.white;

        public void Tick(float dt)
        {
            if (dt <= 0f || Root == null) return;

            // Motion is measured once per real frame: it is what the eye sees move.
            float moved = Mathf.Abs(_progress - _lastTickProgress);
            _lastTickProgress = _progress;
            float speed = moved / dt;                                    // bar fractions per second
            float wantActivity = Mathf.Clamp01(speed * 9f);
            _activity = Mathf.MoveTowards(_activity, wantActivity, dt * (wantActivity > _activity ? 8f : 1.6f));

            FireSegmentEvents();

            _accumulator += dt;
            int steps = 0;
            while (_accumulator > 0f && steps < MaxSubSteps)
            {
                float step = Mathf.Min(_accumulator, MaxStep);
                _accumulator -= step;
                steps++;
                Step(step);
            }
            _accumulator = 0f;

            PushVisuals();
        }

        private void Step(float dt)
        {
            _time += dt;
            Emit(dt);
            _particles.Tick(dt);
            _segments.Decay(dt);
            _completeFlash = Mathf.Max(0f, _completeFlash - dt / 0.9f);
            _frameGlow = Mathf.Max(0f, _frameGlow - dt / 0.8f);
            if (_completed) _endGem = Mathf.MoveTowards(_endGem, 1f, dt * 3f);

            for (int i = 0; i < FlashCount; i++)
            {
                if (_flashLife[i] <= 0f) continue;
                _flashLife[i] -= dt;
            }
        }

        // ── Events ───────────────────────────────────────────────────────────

        /// <summary>Every etapa whose end the fill crossed since the last tick bursts, once.</summary>
        private void FireSegmentEvents()
        {
            if (_progress <= _lastEventProgress) { _lastEventProgress = Mathf.Min(_lastEventProgress, _progress); return; }
            int n = _segments.SegmentCount;
            for (int i = 0; i < n - 1; i++)
            {
                float end = _segments.EndOf(i);
                if (end > _lastEventProgress && end <= _progress) SegmentBurst(i, end);
            }
            _lastEventProgress = _progress;
        }

        private void SegmentBurst(int segment, float end)
        {
            _segments.Flash(segment);
            _frameGlow = Mathf.Max(_frameGlow, 0.55f);

            float iw = _width - 2f * _thickness, ih = _height - 2f * _thickness;
            float x = _thickness + iw * end;
            Color tint = Tint;
            Color hot = Color.Lerp(tint, Color.white, 0.65f);
            for (int i = 0; i < 26; i++)
            {
                float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float v = Random.Range(70f, 230f);
                EmitAt(new Vector2(x, _thickness + Random.value * ih),
                       new Vector2(Mathf.Cos(a) * v, Mathf.Abs(Mathf.Sin(a)) * v * 1.2f),
                       Color.Lerp(hot, tint, Random.value * 0.6f), Random.Range(0.45f, 0.95f),
                       Random.value < 0.55f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                       gravity: 210f, drag: 1.6f, size: Random.Range(0.16f, 0.42f));
            }
            // The notches, top and bottom, throw a vertical pair of streaks.
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                EmitAt(new Vector2(x, side > 0f ? _height + 1f : -1f),
                       new Vector2(Random.Range(-12f, 12f), side * Random.Range(90f, 170f)),
                       hot, Random.Range(0.3f, 0.5f), MenuMoteShape.Spark, gravity: 0f, drag: 3f, size: 0.4f);
            }
            FireFlash(new Vector2(x, _height * 0.5f), new Vector2(ih, ih), new Vector2(ih * 7f, ih * 7f), 0.45f, hot);
        }

        private void FireFlash(Vector2 at, Vector2 from, Vector2 to, float life, Color colour)
        {
            int slot = 0;
            float oldest = float.MaxValue;
            for (int i = 0; i < FlashCount; i++)
            {
                if (_flashLife[i] <= 0f) { slot = i; break; }
                if (_flashLife[i] < oldest) { oldest = _flashLife[i]; slot = i; }
            }
            var img = _flashes[slot];
            if (img == null) return;
            Place(img.rectTransform, at, from);
            _flashLife[slot] = life;
            _flashMax[slot] = life;
            _flashSize[slot] = to;
            var c = colour; c.a = 0f;
            img.color = c;
            img.rectTransform.localScale = Vector3.one;
            // Remember where it started growing from in the scale, not the size, so Place stays pure.
            img.rectTransform.sizeDelta = from;
            _flashStart[slot] = from;
        }

        private readonly Vector2[] _flashStart = new Vector2[FlashCount];

        // ── Emission ─────────────────────────────────────────────────────────

        private void Emit(float dt)
        {
            float iw = _width - 2f * _thickness, ih = _height - 2f * _thickness;
            float fillW = iw * _progress;
            Color tint = Tint;
            // Additive light saturates: a spark already half white reads as a white dot. Keep the
            // hue and let the stacking make the hot core.
            Color hot = Color.Lerp(tint, Color.white, 0.25f) * 0.9f;
            Color warm = Color.Lerp(tint, EmberDeep, 0.45f) * 0.9f;

            bool edgeLive = _progress > 0.002f && !_completed;
            if (edgeLive)
            {
                // A trickle even at rest keeps the edge alive; motion is what makes it spray.
                _sparkDebt += (3.5f + 70f * _activity) * dt;
                float ex = _thickness + fillW;
                while (_sparkDebt >= 1f)
                {
                    _sparkDebt -= 1f;
                    float a = Random.Range(95f, 175f) * Mathf.Deg2Rad;
                    float v = Random.Range(60f, 120f + 170f * _activity);
                    bool spark = Random.value < 0.6f;
                    EmitAt(new Vector2(ex + Random.Range(-2f, 1f), _thickness + Random.Range(0.1f, 0.9f) * ih),
                           new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v,
                           Color.Lerp(hot, warm, Random.value), Random.Range(0.3f, 0.75f),
                           spark ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                           gravity: 280f, drag: 1.1f, size: spark ? Random.Range(0.18f, 0.34f) : Random.Range(0.08f, 0.16f));
                }
            }
            else _sparkDebt = 0f;

            if (fillW > 12f)
            {
                _emberDebt += (4f + 14f * _progress) * dt;
                while (_emberDebt >= 1f)
                {
                    _emberDebt -= 1f;
                    float x = _thickness + Random.value * fillW;
                    EmitAt(new Vector2(x, _height - _thickness + Random.Range(-2f, 2f)),
                           new Vector2(Random.Range(-10f, 10f), Random.Range(18f, 48f)),
                           Color.Lerp(tint, EmberDeep, Random.Range(0.1f, 0.7f)), Random.Range(1.0f, 2.3f),
                           MenuMoteShape.Dot, gravity: -14f, drag: 0.35f, size: Random.Range(0.09f, 0.2f));
                }
            }
            else _emberDebt = 0f;
        }

        private void EmitAt(Vector2 barLocal, Vector2 velocity, Color colour, float life, MenuMoteShape shape,
                            float gravity, float drag, float size)
        {
            colour.a = 1f;
            _particles.Emit(new Vector2(barLocal.x + PadX, barLocal.y + PadBottom), velocity, colour, life, shape,
                            gravity, drag, size, twinkle: true);
        }

        // ── Per-frame visuals ────────────────────────────────────────────────

        private void PushVisuals()
        {
            float iw = _width - 2f * _thickness, ih = _height - 2f * _thickness;
            float fillW = iw * _progress;
            Color tint = Tint;
            Color hot = Color.Lerp(tint, Color.white, 0.7f);
            float flicker = 0.85f + 0.1f * Mathf.Sin(_time * 23f) + 0.05f * Mathf.Sin(_time * 57f + 1.3f);

            _frame.Tint = tint;
            _frame.Glow = _frameGlow;
            _frame.EndGemLit = _endGem;
            _frame.StartGemLit = 0.75f + 0.25f * Mathf.Sin(_time * 2.2f);

            _segments.Tint = tint;
            _segments.Progress = _progress;
            _segments.Clock = _time;
            _segments.CompleteFlash = _completeFlash;
            int current = -1;
            for (int i = 0; i < _segments.SegmentCount; i++)
                if (_progress >= _segments.StartOf(i) && _progress < _segments.EndOf(i)) { current = i; break; }
            _segments.Current = current;
            _segments.Refresh();

            var halo = tint;
            halo.a = 0.05f + 0.07f * _progress + 0.03f * Mathf.Sin(_time * 1.7f) + 0.25f * _completeFlash;
            _halo.color = halo;

            // The sheen: a soft oval sliding along the fill every few seconds.
            float period = 2.8f;
            float phase = Mathf.Repeat(_time, period) / period;
            float sheenW = ih * 3.2f;
            float sx = Mathf.Lerp(-sheenW * 0.5f, fillW + sheenW * 0.5f, phase);
            float sAlpha = Mathf.Sin(phase * Mathf.PI) * Mathf.Clamp01(fillW / 90f)
                         * Mathf.Clamp01((fillW - sx) / (sheenW * 0.6f) + 0.2f);
            Place(_sheen.rectTransform, new Vector2(_thickness + Mathf.Min(sx, fillW), _height * 0.55f),
                  new Vector2(sheenW, ih * 1.25f));
            _sheen.color = new Color(1f, 0.97f, 0.88f, 0.42f * Mathf.Clamp01(sAlpha));

            bool edgeLive = _progress > 0.002f && !_completed;
            _edge.anchoredPosition = new Vector2(_thickness + fillW, _height * 0.5f);
            float e = edgeLive ? (0.45f + 0.55f * _activity) * flicker : 0f;
            _edgeCore.color = FrontendMesh.WithAlpha(FrontendPalette.WarmWhite, 0.9f * e);
            var glow = Color.Lerp(tint, Color.white, 0.35f); glow.a = 0.32f * e;
            _edgeGlow.color = glow;
            var flare = Color.Lerp(tint, Color.white, 0.25f); flare.a = (0.07f + 0.16f * _activity) * e;
            _edgeFlare.color = flare;
            float pulse = 1f + 0.12f * _activity * Mathf.Sin(_time * 15f);
            _edgeGlow.rectTransform.localScale = new Vector3(pulse, pulse, 1f);

            for (int i = 0; i < FlashCount; i++)
            {
                var img = _flashes[i];
                if (img == null) continue;
                if (_flashLife[i] <= 0f)
                {
                    if (img.color.a > 0f) { var c0 = img.color; c0.a = 0f; img.color = c0; }
                    continue;
                }
                float t = 1f - _flashLife[i] / Mathf.Max(0.0001f, _flashMax[i]);    // 0 at birth
                float ease = 1f - (1f - t) * (1f - t);
                img.rectTransform.sizeDelta = Vector2.Lerp(_flashStart[i], _flashSize[i], ease);
                var c = img.color;
                c.a = (t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f) * 0.85f;
                img.color = c;
            }
        }

        // ── Teardown ─────────────────────────────────────────────────────────

        public void Dispose()
        {
            // The material and the sprites belong to FrontendKit, shared with the menu; the
            // GameObjects go with the canvas they hang from.
            _particles?.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private Image SoftImage(Transform parent, string name, Sprite sprite, Color colour, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            if (_additive != null) img.material = _additive;
            colour.a = alpha;
            img.color = colour;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        /// <summary>Centre <paramref name="rt"/> at a point in the bar's own bottom-left space.</summary>
        private static void Place(RectTransform rt, Vector2 at, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
