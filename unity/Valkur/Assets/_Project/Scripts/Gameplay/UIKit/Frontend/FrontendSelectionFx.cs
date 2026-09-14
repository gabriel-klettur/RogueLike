using UnityEngine;
using UnityEngine.UI;
using Valkur.UI.MainMenu;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The selection highlight as the loading bar FILLED: the row's light profile in gold, a
    /// bright border, an accent core where the fill starts, bands of light flowing along it, a
    /// sheen that travels across every few seconds, and a glow breathing at its start — plus the
    /// two events a selection has: it MOVED, and it was CONFIRMED.
    ///
    /// <para><b>Particles answer those two events and nothing else</b>, the rule
    /// <see cref="MenuFxLayer"/> writes down: a spray of sparks off the edge the highlight is
    /// travelling toward when it moves, a burst and a flash when it is chosen. At rest the row
    /// flows and breathes — the bar does too — but emits nothing.</para>
    ///
    /// <para><b>Reduce motion switches off the flow, the sheen, the breathing and every mote</b>;
    /// what is left is a still, filled row, which is the whole of the information.</para>
    ///
    /// <para>Its own delta, sub-stepped, the same contract as <c>LoadingBarFX</c>. It owns no
    /// material and no texture — those are <see cref="FrontendKit"/>'s — only GameObjects under
    /// its root, which go with whatever panel they hang from.</para>
    /// </summary>
    public sealed class FrontendSelectionFx
    {
        private const float MaxStep = 1f / 30f;
        private const int MaxSubSteps = 6;
        private const float SheenPeriod = 3.2f;

        public RectTransform Root { get; }
        public FrontendFillGraphic Fill { get; }

        /// <summary>Motes currently alive in the layer this rig emits into. For the tests.</summary>
        public int ParticleCount => _motes != null ? _motes.Alive : 0;

        private readonly Image _sheen;
        private readonly Image _startGlow;
        private readonly Image _flash;
        private readonly MenuFxLayer _motes;
        private Color _tint;
        private bool _reduceMotion;
        private float _time;
        private float _accumulator;
        private float _flashLife;
        private float _glow;
        private bool _visible = true;

        public FrontendSelectionFx(Transform parent, string name, Color tint, FrontendKit kit, MenuFxLayer motes,
                                   bool reduceMotion)
        {
            _tint = tint;
            _motes = motes;
            _reduceMotion = reduceMotion;

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Root = (RectTransform)go.transform;

            Fill = FrontendFillGraphic.Create(Root, "Fill");
            Fill.Profile = FrontendFillProfile.Row;
            Fill.Border = true;
            Fill.StartCore = true;
            Fill.Tint = tint;

            _startGlow = GlowImage(Root, "StartGlow", kit);
            _sheen = GlowImage(Root, "Sheen", kit);
            _flash = GlowImage(Root, "Flash", kit);
            ApplyMotion();
        }

        public bool ReduceMotion
        {
            get => _reduceMotion;
            set { _reduceMotion = value; ApplyMotion(); }
        }

        /// <summary>Shown or hidden with the row it highlights (a scrolled-out row takes it along).</summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                Root.gameObject.SetActive(value);
            }
        }

        public Color Tint
        {
            get => _tint;
            set { _tint = value; Fill.Tint = value; }
        }

        private void ApplyMotion() => Fill.Flow = !_reduceMotion;

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _accumulator += dt;
            int steps = 0;
            while (_accumulator > 0f && steps < MaxSubSteps)
            {
                float step = Mathf.Min(_accumulator, MaxStep);
                _accumulator -= step;
                steps++;
                _time += step;
                _flashLife = Mathf.Max(0f, _flashLife - step);
                _glow = Mathf.Max(0f, _glow - step / 0.5f);
                if (_motes != null) _motes.Tick(step);
            }
            _accumulator = 0f;
            Push();
        }

        /// <summary>
        /// The highlight started moving: sparks spray off the edge it is heading toward
        /// (<paramref name="direction"/> -1 = up, +1 = down) and trail behind it.
        /// </summary>
        public void Moved(int direction)
        {
            _glow = Mathf.Max(_glow, 0.6f);
            if (_reduceMotion || _motes == null || !_visible) return;
            var r = Root.rect;
            Color hot = Hot, warm = Warm;
            float edgeY = direction < 0 ? r.yMax : r.yMin;
            float sign = direction < 0 ? 1f : -1f;
            for (int i = 0; i < 12; i++)
            {
                float x = r.xMin + Random.Range(0.02f, 0.55f) * r.width;
                float a = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                float v = Random.Range(50f, 150f);
                EmitLocal(new Vector2(x, edgeY), new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v * sign),
                          Color.Lerp(hot, warm, Random.value), Random.Range(0.25f, 0.55f),
                          Random.value < 0.6f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                          gravity: 220f, drag: 1.4f, size: Random.Range(0.14f, 0.3f));
            }
            // The accent core throws a short streak forward, which is what makes the move read
            // as the fill SHIFTING rather than as a rectangle teleporting.
            for (int i = 0; i < 4; i++)
                EmitLocal(new Vector2(r.xMin + 3f, Random.Range(r.yMin, r.yMax)),
                          new Vector2(Random.Range(140f, 260f), Random.Range(-12f, 12f)), hot,
                          Random.Range(0.18f, 0.32f), MenuMoteShape.Spark, gravity: 0f, drag: 3.2f, size: 0.32f);
        }

        /// <summary>The row was chosen: a burst out of the fill and a flash over it.</summary>
        public void Confirm()
        {
            _glow = 1f;
            if (_reduceMotion || !_visible) return;
            _flashLife = 0.42f;
            if (_motes == null) return;
            var r = Root.rect;
            Color hot = Hot, warm = Warm;
            for (int i = 0; i < 28; i++)
            {
                float x = r.xMin + Random.value * r.width;
                float y = r.yMin + Random.value * r.height;
                float a = Random.Range(25f, 155f) * Mathf.Deg2Rad;
                float v = Random.Range(80f, 260f);
                EmitLocal(new Vector2(x, y), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v,
                          Color.Lerp(hot, warm, Random.value * 0.6f), Random.Range(0.45f, 1.0f),
                          Random.value < 0.45f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                          gravity: 200f, drag: 1.4f, size: Random.Range(0.16f, 0.42f));
            }
        }

        private Color Hot => FrontendMotes.Hot(_tint);
        private Color Warm => FrontendMotes.Warm(_tint);

        private void EmitLocal(Vector2 rootLocal, Vector2 velocity, Color colour, float life, MenuMoteShape shape,
                               float gravity, float drag, float size)
            => FrontendMotes.EmitFrom(_motes, Root, rootLocal, velocity, colour, life, shape, gravity, drag, size);

        private void Push()
        {
            var r = Root.rect;
            float h = r.height, w = r.width;
            Fill.Clock = _time;
            Fill.Glow = _glow;

            float breathe = _reduceMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin(_time * 2.2f);
            // Centred half a row INSIDE the start, so the glow stays over the fill instead of
            // spilling past the panel's frame (the first capture showed it doing exactly that).
            Place(_startGlow.rectTransform, new Vector2(r.xMin + h * 0.55f, r.center.y), new Vector2(h * 1.1f, h * 1.25f));
            _startGlow.color = WithA(Color.Lerp(_tint, Color.white, 0.3f), (0.16f + 0.10f * breathe + 0.3f * _glow) * (_visible ? 1f : 0f));

            if (_reduceMotion) _sheen.color = WithA(Color.white, 0f);
            else
            {
                float phase = Mathf.Repeat(_time, SheenPeriod) / SheenPeriod;
                // Travels during the first 45 % of the period and rests for the remainder, so it
                // reads as a glint passing, not as a second moving highlight.
                float run = Mathf.Clamp01(phase / 0.45f);
                float sheenW = h * 3.2f;
                float sx = Mathf.Lerp(r.xMin - sheenW * 0.5f, r.xMax + sheenW * 0.5f, run);
                Place(_sheen.rectTransform, new Vector2(sx, r.yMin + h * 0.58f), new Vector2(sheenW, h * 1.15f));
                _sheen.color = WithA(FrontendPalette.WarmWhite, phase < 0.45f ? 0.26f * Mathf.Sin(run * Mathf.PI) : 0f);
            }

            float ft = _flashLife > 0f ? 1f - _flashLife / 0.42f : 1f;
            Place(_flash.rectTransform, r.center, new Vector2(w * Mathf.Lerp(0.6f, 1.25f, ft), h * Mathf.Lerp(1.2f, 3.4f, ft)));
            _flash.color = WithA(Color.Lerp(_tint, Color.white, 0.35f), _flashLife > 0f ? (ft < 0.15f ? ft / 0.15f : 1f - (ft - 0.15f) / 0.85f) * 0.55f : 0f);
        }

        private static Color WithA(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

        private static Image GlowImage(Transform parent, string name, FrontendKit kit)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = kit != null ? kit.Radial : null;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            if (kit != null && kit.Additive != null) img.material = kit.Additive;
            img.color = Color.clear;
            Place(img.rectTransform, Vector2.zero, Vector2.one);
            return img;
        }

        /// <summary>Centre <paramref name="rt"/> at a point of its parent's rect space.</summary>
        private static void Place(RectTransform rt, Vector2 at, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var parent = rt.parent as RectTransform;
            Vector2 centre = parent != null ? parent.rect.center : Vector2.zero;
            rt.anchoredPosition = at - centre;
            rt.sizeDelta = size;
        }
    }
}
