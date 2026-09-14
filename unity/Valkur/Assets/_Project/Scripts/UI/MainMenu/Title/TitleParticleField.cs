using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Title
{
    /// <summary>
    /// The game's name, drawn by a few thousand points of light instead of by a texture.
    ///
    /// <para><b>What this replaces.</b> <c>Resources/UI/Intro/game_name.png</c> — a 1024 × 453
    /// rendered rótulo that said <c>ROGUELIKE 1.0</c>, the name of the Python prototype, on all
    /// ten screens the player sees before the game starts. It also cost 18 ms every time the menu
    /// opened, because it was built with <c>Sprite.Create</c> at its default
    /// <c>SpriteMeshType.Tight</c>, which traced the alpha outline of the whole logo into a
    /// 147-vertex mesh nothing read.</para>
    ///
    /// <para><b>Why not a ParticleSystem.</b> The rule this project already wrote down twice, in
    /// <c>HudMoteLayer</c> and <c>MinimapFx</c>: a <c>ParticleSystem</c> is a world renderer and
    /// does not sort against the <c>Graphic</c>s of a Screen Space Overlay canvas, so it would sit
    /// either in front of every panel or behind the background — never between the art and the
    /// menu, which is exactly where a title lives. One <see cref="MaskableGraphic"/> emitting
    /// quads sorts by hierarchy, shares the menu atlas and costs one draw call.</para>
    ///
    /// <para><b>Three beats.</b> The motes SCATTER off-screen, are drawn in along their own
    /// stroke's direction — which is why the letterforms are strokes and not a mask, see
    /// <see cref="TitleGlyphStrokes"/> — and then SETTLE, where the only thing that moves is a
    /// shimmer under a pixel wide and a sweep of light that crosses the word every few seconds.
    /// A title that keeps animating is a screensaver; a title that is perfectly still is a PNG
    /// with extra steps.</para>
    ///
    /// <para><b>Colour is a property of the pen.</b> Each mote knows how far it sits from the
    /// middle of its stroke (<c>TitlePoint.Depth</c>), so the word is drawn like hot metal —
    /// near-white at the core, gold through the body, ember at the rim. Baking that into a
    /// texture is what the old PNG did; deriving it means the palette is three fields of
    /// <see cref="MenuStyle"/>.</para>
    ///
    /// <para><b>Reduce motion is honoured at the top</b>: the assembly is skipped, the shimmer
    /// stops and the sweep never runs. What remains is the word, drawn the same way.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleParticleField : MaskableGraphic
    {
        private struct Mote
        {
            public Vector2 Home;        // where it belongs, in the field's own space
            public Vector2 Position;
            public Vector2 Start;       // where it flew in from
            public Vector2 Swirl;       // the sideways push that makes the path a curve
            // Depth and Height are kept INSTEAD of a baked colour: a look that flickers has a
            // different colour every frame, and baking one at build time is exactly what made
            // the shipped word a painted gradient that could never burn.
            public float Depth;         // 0 at the middle of its stroke, 1 at the edge
            public float Height01;      // 0 at the foot of the word, 1 at its cap line
            public float Across;        // 0 at the first letter, 1 at the last
            public float Delay;         // 0..1 of the assembly, so the word writes itself
            public float Phase;         // this mote's own place in the shimmer
            public float ShimmerScale;
            public byte Shape;          // 0 dot, 1 spark
        }

        private Mote[] _motes = System.Array.Empty<Mote>();
        private int _count;

        private MenuArt _art;
        private MenuStyle _style;
        private float _time;
        private float _assembly;        // 0 scattered, 1 settled
        private float _sweepClock;
        private float _sweepAt = -1f;   // <0 while no sweep is running
        private float _ignition = 1f;   // 0 cold, 1 fully alight. 1 when a look does not ignite
        private bool _reduceMotion;
        private float _alpha = 1f;
        private Vector2 _size;

        /// <summary>How many motes the word is made of. For the tests and the budget.</summary>
        public int MoteCount => _count;

        /// <summary>0 while scattered, 1 once the word has finished assembling.</summary>
        public float Assembly => _assembly;

        /// <summary>The laid-out size of the word, in canvas units.</summary>
        public Vector2 TitleSize => _size;

        /// <summary>True while a sweep of light is crossing the word.</summary>
        public bool Sweeping => _sweepAt >= 0f;

        /// <summary>
        /// The title draws from its OWN small bilinear texture, not from the menu's point-filtered
        /// atlas. One extra draw call, and it is what separates embers from gravel: a 2x2 atlas
        /// dot point-sampled up to ~4 units is a hard square, and a few thousand hard squares read
        /// as confetti however good the colour is.
        /// </summary>
        public override Texture mainTexture
        {
            get
            {
                var mote = _art != null ? _art.TitleMote : null;
                if (mote != null && mote.texture != null) return mote.texture;
                return _art != null && _art.Atlas != null ? _art.Atlas : s_WhiteTexture;
            }
        }

        /// <summary>Fades the whole word without touching the motes. Used by the screen changes.</summary>
        public float Alpha
        {
            get => _alpha;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _alpha)) return;
                _alpha = v;
                SetVerticesDirty();
            }
        }

        // ── Build ────────────────────────────────────────────────────────────

        public static TitleParticleField Create(Transform parent, MenuArt art, MenuStyle style, Material material)
        {
            var go = new GameObject("TitleParticles", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(10f, 10f);
            var field = go.AddComponent<TitleParticleField>();
            field._art = art;
            field._style = style;
            field.raycastTarget = false;
            if (material != null) field.material = material;
            return field;
        }

        /// <summary>
        /// Lays <paramref name="text"/> out and builds the cloud. Safe to call again with a
        /// different word — a rename, or a language whose title differs.
        /// </summary>
        public void SetText(string text, bool reduceMotion)
        {
            var style = _style != null ? _style : MenuStyle.Active;
            _reduceMotion = reduceMotion;
            // Re-resolved on every build, so switching the look is a rebuild rather than a
            // restart — and so a cached look cannot outlive the style that named it.
            _look = null;

            var points = TitleGlyphStrokes.Sample(
                text,
                capHeight: style.titleCapHeight,
                strokeWidth: style.titleStrokeWidth,
                spacing: style.titlePointSpacing,
                rowsAcross: style.titleRowsAcross,
                tracking: style.titleTracking,
                seed: 8117,
                weightContrast: style.titleWeightContrast,
                terminalFlare: style.titleTerminalFlare);

            // Thin EVENLY to the budget rather than truncating. A truncated cloud draws the
            // first letters at full density and drops the last ones entirely, which reads as a
            // word that failed to finish loading.
            int budget = Mathf.Max(16, style.titleMaxPoints);
            int stride = points.Count <= budget ? 1 : Mathf.CeilToInt(points.Count / (float)budget);

            int kept = 0;
            for (int i = 0; i < points.Count; i += stride) kept++;
            if (_motes.Length < kept) _motes = new Mote[kept];
            _count = 0;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i].Position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            if (points.Count == 0) { minX = maxX = minY = maxY = 0f; }

            _size = new Vector2(Mathf.Max(1f, maxX - minX), Mathf.Max(1f, maxY - minY));
            var rt = (RectTransform)transform;
            rt.sizeDelta = _size;

            // The field's own rect has its origin at the bottom-left of the word's bounding box,
            // so a mote's home is measured from there and the caller only has to place the rect.
            var origin = new Vector2(minX, minY);
            float scatter = Mathf.Max(1f, style.titleScatterRadius) * Mathf.Max(_size.x, 600f);
            uint hash = 0x9E3779B9u;

            for (int i = 0; i < points.Count; i += stride)
            {
                var pt = points[i];
                var home = pt.Position - origin;

                // Flying in from a ring around the word rather than from a random cloud: a cloud
                // has motes that start ON the word and have nowhere to travel from, and those
                // read as pixels that were already correct while everything else moved.
                float ang = NextUnit(ref hash) * Mathf.PI * 2f;
                float dist = scatter * (0.45f + 0.55f * NextUnit(ref hash));
                var start = home + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.55f) * dist;

                // The swirl is PERPENDICULAR to the stroke the mote belongs to, so the whole
                // word turns in the direction it is written rather than every mote curving the
                // same way, which reads as a single rotation of the entire cloud.
                var n = new Vector2(-pt.Tangent.y, pt.Tangent.x);
                var swirl = n * (dist * 0.28f) * (NextUnit(ref hash) < 0.5f ? -1f : 1f);

                _motes[_count++] = new Mote
                {
                    Home = home,
                    Position = _reduceMotion ? home : start,
                    Start = start,
                    Swirl = swirl,
                    Depth = pt.Depth,
                    // Height over the WORD's own box, not over the glyph's: a flame does not
                    // restart at the foot of every letter, and measuring per glyph would make
                    // the dot of an i as hot as the base of a V.
                    Height01 = Mathf.Clamp01(home.y / Mathf.Max(1f, _size.y)),
                    Across = pt.Across,
                    // The word writes itself left to right, with a little slop so the leading
                    // edge is a front of embers rather than a wipe.
                    Delay = Mathf.Clamp01(pt.Across * 0.62f + NextUnit(ref hash) * 0.22f),
                    Phase = NextUnit(ref hash) * Mathf.PI * 2f,
                    ShimmerScale = 0.55f + NextUnit(ref hash) * 0.9f,
                    Shape = (byte)(NextUnit(ref hash) < 0.06f ? 1 : 0),
                };
            }

            _assembly = _reduceMotion ? 1f : 0f;
            // A look that ignites arrives COLD and lights once it has gathered. One that does
            // not is alight from its first frame, which is the shipped behaviour.
            _ignition = (_reduceMotion || Look.ignitionSeconds <= 0f) ? 1f : 0f;
            _time = 0f;
            _sweepClock = 0f;
            _sweepAt = -1f;
            SetVerticesDirty();
        }

        /// <summary>
        /// Puts the word straight into its settled state without touching the reduce-motion
        /// switch. Used when the brand plane has already shown the assembly seconds earlier:
        /// gathering it a second time reads as a loop rather than as an introduction, and
        /// forcing reduce motion to get there would also kill the shimmer and the sweep.
        /// </summary>
        public void SnapToSettled()
        {
            _assembly = 1f;
            for (int i = 0; i < _count; i++) _motes[i].Position = _motes[i].Home;
            SetVerticesDirty();
        }

        /// <summary>Puts the word back on the far side of its assembly, so it can gather again.</summary>
        public void Replay()
        {
            if (_reduceMotion) return;
            _assembly = 0f;
            _time = 0f;
            for (int i = 0; i < _count; i++) _motes[i].Position = _motes[i].Start;
            SetVerticesDirty();
        }

        /// <summary>Honours the accessibility switch without rebuilding the cloud.</summary>
        public void SetReduceMotion(bool reduce)
        {
            if (_reduceMotion == reduce) return;
            _reduceMotion = reduce;
            if (reduce)
            {
                _assembly = 1f;
                _sweepAt = -1f;
                for (int i = 0; i < _count; i++) _motes[i].Position = _motes[i].Home;
                SetVerticesDirty();
            }
        }

        /// <summary>The look the word is wearing. Resolved once per build, never per mote.</summary>
        private TitleLook Look => _look ?? (_look = (_style != null ? _style : MenuStyle.Active).ResolveTitleLook());

        private TitleLook _look;

        /// <summary>
        /// One mote's colour, now: its place across the stroke, its height up the word, and its
        /// own flicker, folded into a single temperature the look's ramp is sampled at.
        ///
        /// <para>Two lerps rather than one inside <see cref="TitleLook.Sample"/>, so the hot tone
        /// keeps its own weight: a single ramp from white to ember makes the middle of every
        /// stroke the average of the two, and the word loses the hot line that makes it read as
        /// matter rather than as paint.</para>
        /// </summary>
        private Color ColourOf(in Mote m, TitleLook look)
        {
            float flicker = 0f;
            if (look.flickerAmount > 0f)
            {
                // Two sines at incommensurable rates rather than one: a single sine is a pulse,
                // and a pulse on a title is a lamp with a flicker rather than a fire. The SHARED
                // term is what makes the whole word surge together the way a fire does when it
                // draws air; the per-mote term is what stops it being one object breathing.
                float asym = look.flickerAsymmetry;
                float own = FireWave(m.Phase + _time * look.flickerSpeed * 6.2831853f, asym)
                          * FireWave(m.Phase * 1.7f + _time * look.flickerSpeed * 2.3f, asym);
                float together = FireWave(_time * look.flickerSpeed * 1.9f, asym);
                flicker = Mathf.Lerp(own, together, look.flickerTogether) * look.flickerAmount;
            }
            var lit = look.Sample(look.TemperatureOf(m.Depth, m.Height01, flicker));
            if (_ignition >= 1f) return lit;

            // The front is a little ahead of the mote it is lighting, and it carries a narrow
            // white band: fire spreading along a fuse is brightest exactly where it is arriving,
            // and without that band the ignition reads as a wipe of colour.
            float front = _ignition * 1.28f - 0.14f;
            float d = front - m.Across;
            if (d <= 0f) return look.ember;                       // not caught yet: cold

            float caught = Mathf.Clamp01(d / 0.18f);
            var c = Color.Lerp(look.ember, lit, Mathf.SmoothStep(0f, 1f, caught));
            if (d < 0.10f)
            {
                float flash = 1f - d / 0.10f;
                c = Color.Lerp(c, look.Sample(1f) * 1.25f, flash * flash);
            }
            return c;
        }

        /// <summary>
        /// A sine that brightens fast and decays slow, which is what combustion does.
        ///
        /// <para>Phase distortion rather than amplitude shaping: <c>sin(p + a·sin p)</c> has a
        /// slope of <c>1 + a</c> at its rising zero crossing and <c>1 - a</c> at its falling one,
        /// so the peak arrives early and the return is long. Shaping the amplitude instead
        /// (raising it to a power, say) changes the MEAN as well, so the word would also get
        /// colder as the asymmetry went up — two things moving from one dial. This keeps the
        /// range exactly [-1,1] and its average at zero, and at <c>a = 0</c> it is
        /// <c>Mathf.Sin</c> sample for sample, which is what lets every shipped look keep the
        /// waveform it was tuned with.</para>
        ///
        /// <para>Public so a fixture can assert the shape directly: the property that matters is
        /// WHEN the peak arrives, and nothing observable from outside the cloud reports that.</para>
        /// </summary>
        public static float FireWave(float p, float asymmetry)
            => asymmetry <= 0f ? Mathf.Sin(p) : Mathf.Sin(p + asymmetry * Mathf.Sin(p));

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advances the word. Public and taking its own delta so an EditMode test can drive it
        /// without a clock — the rule <c>HudMoteLayer.Tick</c> and <c>WorldBarRig</c> already
        /// follow, and the reason this file has no <c>Update</c> of its own.
        /// </summary>
        public void Tick(float dt)
        {
            if (_count == 0 || dt <= 0f) return;
            var style = _style != null ? _style : MenuStyle.Active;
            _time += dt;

            if (_reduceMotion)
            {
                if (_assembly < 1f) { _assembly = 1f; SetVerticesDirty(); }
                return;
            }

            if (_assembly < 1f)
            {
                float span = Mathf.Max(0.05f, style.titleAssembleSeconds);
                _assembly = Mathf.Clamp01(_assembly + dt / span);
                for (int i = 0; i < _count; i++)
                {
                    ref var m = ref _motes[i];
                    // Each mote runs its own 0..1 inside the shared clock, so the ones that were
                    // dealt a late delay are still flying while the first letters are already set.
                    float t = Mathf.Clamp01((_assembly - m.Delay) / Mathf.Max(0.0001f, 1f - m.Delay));
                    float e = EaseOutBack(t);
                    var straight = Vector2.LerpUnclamped(m.Start, m.Home, e);
                    // The swirl peaks in the middle of the flight and is gone at both ends, so
                    // the mote leaves its start cleanly and arrives without an overshoot sideways.
                    float bow = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    m.Position = straight + m.Swirl * bow * (1f - e * 0.35f);
                }
                SetVerticesDirty();
                return;
            }

            // ── Settled ──────────────────────────────────────────────────────
            var look = Look;

            // The word has gathered; now it catches. A FRONT runs left to right rather than the
            // whole word brightening at once, because a uniform fade is a dissolve and a front
            // is a thing spreading — which is the difference between a title appearing and a
            // name being set alight.
            if (_ignition < 1f)
            {
                _ignition = Mathf.Clamp01(_ignition + dt / Mathf.Max(0.05f, look.ignitionSeconds));
            }
            float amp = look.shimmerAmplitude;
            float speed = look.shimmerSpeed;
            if (amp > 0f || look.riseBias != 0f)
            {
                for (int i = 0; i < _count; i++)
                {
                    ref var m = ref _motes[i];
                    float a = m.Phase + _time * speed * Mathf.PI * 2f;
                    // An ellipse, not a circle: a mote wandering equally on both axes reads as
                    // noise. Which WAY the ellipse leans is the look's own decision — below 1 it
                    // is heat shivering off a bar, above 1 it is flame. The rise is separate and
                    // steady, and it is scaled by the mote's HEIGHT: a flame's foot is anchored
                    // to what is burning and only its crown moves, so biasing every mote equally
                    // detaches the whole word from its own baseline.
                    var wander = new Vector2(Mathf.Cos(a), Mathf.Sin(a * 1.37f) * look.driftAspect)
                                 * (amp * m.ShimmerScale);
                    float rise = look.riseBias * m.Height01 * m.ShimmerScale;
                    m.Position = m.Home + wander + new Vector2(0f, rise);
                }
            }

            if (look.sweepInterval > 0f)
            {
                if (_sweepAt >= 0f)
                {
                    _sweepAt += dt / Mathf.Max(0.05f, look.sweepSeconds);
                    if (_sweepAt > 1.35f) { _sweepAt = -1f; _sweepClock = 0f; }
                }
                else
                {
                    _sweepClock += dt;
                    if (_sweepClock >= look.sweepInterval) _sweepAt = -0.35f + 0.35f;
                }
            }
            else
            {
                _sweepAt = -1f;
            }

            SetVerticesDirty();
        }

        /// <summary>Starts a sweep of light across the word now. The confirm beat uses it.</summary>
        public void Sweep()
        {
            if (_reduceMotion) return;
            _sweepAt = 0f;
            _sweepClock = 0f;
        }

        /// <summary>
        /// Where an ember should be born, so the caller's mote layer can throw one off the word
        /// without knowing anything about letterforms. Returns false while the word is not settled.
        /// </summary>
        public bool TryPickEmberSource(out Vector2 localPosition, out Color colour)
        {
            localPosition = default;
            colour = Color.white;
            if (_count == 0 || _assembly < 1f) return false;
            int i = Random.Range(0, _count);
            localPosition = _motes[i].Position;
            // The ember leaves with the colour that mote HAS at this instant, flicker included,
            // so a spark thrown off a burning word is the same temperature as the letter it came
            // from rather than an average of the whole title — then pushed toward the hottest
            // tone by however much the look asks for. An ember ABANDONING a fire is the hottest
            // thing in the picture, and on a red look the crown it was picked from is dark
            // enough that an unbiased spark is invisible: that is an accident of where the
            // random index landed, not a property of embers.
            var look = Look;
            colour = ColourOf(_motes[i], look);
            if (look.emberHeat > 0f) colour = Color.Lerp(colour, look.Sample(1f), look.emberHeat);
            return true;
        }

        /// <summary>
        /// Where a DRIP should be born: a point on the lower edge of some stroke, with the
        /// colour it is wearing. False while the word is not settled, or while the look does not
        /// drip.
        ///
        /// <para><b>The lower edge is sampled, not computed.</b> A mote knows how far it sits
        /// across its own stroke (<see cref="Mote.Depth"/>) and how high it sits up the word, and
        /// nothing in the cloud records which SIDE of a stroke it is on — the stroke normal is
        /// consumed by the sampler and never stored. Taking a handful of rim candidates and
        /// keeping the lowest is one line against a second per-mote field, and it is exact often
        /// enough for a thing that happens twice a second: what a drip must never do is fall out
        /// of the TOP of a letter, and a lowest-of-eight rim sample cannot.</para>
        ///
        /// <para>It is deliberately not the same pick as an ember. An ember leaves from anywhere
        /// and rises; a drip leaves from underneath and falls, and picking both from one place
        /// would make the word shed sparks downward out of its own middle.</para>
        /// </summary>
        public bool TryPickDripSource(out Vector2 localPosition, out Color colour)
        {
            localPosition = default;
            colour = Color.white;
            if (_count == 0 || _assembly < 1f) return false;

            int best = -1;
            for (int k = 0; k < 8; k++)
            {
                int i = Random.Range(0, _count);
                if (_motes[i].Depth < 0.7f) continue;             // not on a rim
                if (best < 0 || _motes[i].Position.y < _motes[best].Position.y) best = i;
            }
            if (best < 0) return false;

            localPosition = _motes[best].Position;
            colour = ColourOf(_motes[best], Look);
            return true;
        }

        private static float EaseOutBack(float t)
        {
            // A small overshoot on arrival. Without it the motes decelerate into place and the
            // word arrives looking like it was always there; with too much they bounce and the
            // letterforms wobble. 1.24 is the largest value at which VALKUR's stems still read
            // as straight at the moment of arrival.
            const float c1 = 1.24f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ── Mesh ─────────────────────────────────────────────────────────────

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_count == 0 || _art == null || _alpha <= 0f) return;

            var dot = _art.TitleMote != null ? _art.TitleMote : _art.MoteDot;
            var spark = _art.TitleSpark != null ? _art.TitleSpark : _art.MoteSpark;
            if (dot == null) return;

            var tex = dot.texture;
            var rect = GetPixelAdjustedRect();
            float assemblyGlow = 1f - _assembly;
            var look = Look;
            var sizeStyle = _style != null ? _style : MenuStyle.Active;
            float moteSize = Mathf.Max(0.5f, sizeStyle.titleMoteSize);
            float sparkScale = Mathf.Max(1f, sizeStyle.titleSparkScale);

            // A second, larger, dimmer quad behind every mote. Additive blending SUMS, so the
            // two can be emitted in any order — there is no back-to-front to respect — and the
            // halo always draws from the DOT, never from the glint: a spark's rays scaled two
            // and a half times read as a star, which is a shape the word is not made of.
            // Budget: two quads per mote is eight vertices, so the shipped 5000-point ceiling
            // is 40000 against uGUI's 65535 per mesh.
            float glowAlpha = look.glowStrength;
            float glowScale = Mathf.Max(1f, look.glowScale);
            var dr = dot.textureRect;
            var glowUv = new Rect(dr.x / tex.width, dr.y / tex.height, dr.width / tex.width, dr.height / tex.height);

            for (int i = 0; i < _count; i++)
            {
                var m = _motes[i];
                // Both shapes are baked on their own texture, so a mote that is a spark has to
                // draw from THAT texture — and a Graphic has exactly one. The spark therefore
                // falls back to the dot whenever the two are not on the same page, which is the
                // only way one draw call can carry both.
                var sprite = m.Shape == 1 && spark != null && spark.texture == tex ? spark : dot;
                var r = sprite.textureRect;

                var c = ColourOf(m, look);

                // While it is still flying, a mote is COOLER and dimmer — an ember on its way in
                // rather than a piece of the finished word. Without it the assembly looks like
                // the title sliding into place, which is a transition, not a gathering.
                if (assemblyGlow > 0f)
                {
                    float t = Mathf.Clamp01((_assembly - m.Delay) / Mathf.Max(0.0001f, 1f - m.Delay));
                    c = Color.Lerp(look.ember, c, Mathf.SmoothStep(0f, 1f, t));
                    c.a *= 0.25f + 0.75f * t;
                }

                if (_sweepAt >= 0f)
                {
                    // A narrow band of extra light travelling left to right. It ADDS rather than
                    // replaces, so the sweep is the word getting brighter, not a different colour
                    // passing over it.
                    float d = Mathf.Abs(m.Across - _sweepAt);
                    if (d < 0.16f)
                    {
                        float k = 1f - d / 0.16f;
                        c = Color.Lerp(c, Color.white, k * k * 0.75f);
                    }
                }

                c.a *= _alpha;
                Color32 c32 = c;

                // The quad's size comes from the STYLE, never from the sprite's own texel count.
                // Drawing each mote at its atlas size is what made the word a constellation: a
                // 2x2 dot on a grid whose pitch is about 4 covers a quarter of the stroke it is
                // supposed to fill, so the letters read as perforated outlines however many
                // points are sampled. A mote slightly wider than the pitch overlaps its
                // neighbours and the stroke closes into matter.
                float size = moteSize * (m.Shape == 1 ? sparkScale : 1f);
                float w = size, h = size;
                float x = rect.xMin + m.Position.x - w * 0.5f;
                float y = rect.yMin + m.Position.y - h * 0.5f;
                var uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);

                if (glowAlpha > 0f)
                {
                    var gc = c;
                    gc.a *= glowAlpha;
                    float gs = size * glowScale;
                    AddQuad(vh, rect.xMin + m.Position.x - gs * 0.5f,
                                rect.yMin + m.Position.y - gs * 0.5f, gs, gs, gc, glowUv);
                }

                AddQuad(vh, x, y, w, h, c32, uv);
            }
        }

        /// <summary>One mote's quad. Shared by the body and by the halo behind it.</summary>
        private static void AddQuad(VertexHelper vh, float x, float y, float w, float h,
                                    Color32 c, Rect uv)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(x, y + h), c, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(x + w, y + h), c, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(x + w, y), c, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        /// <summary>xorshift32, local so nothing else in the game can move the sequence.</summary>
        private static float NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>Every mote's home, for a test that wants to check the shape of the word.</summary>
        public IEnumerable<Vector2> Homes()
        {
            for (int i = 0; i < _count; i++) yield return _motes[i].Home;
        }
    }
}
