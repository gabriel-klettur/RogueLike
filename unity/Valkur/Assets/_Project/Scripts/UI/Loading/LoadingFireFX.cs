using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.UI.MainMenu;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Brings the dragon's painted fire to life: embers riding the jet, a muzzle that breathes,
    /// smoke where the flame lands, and the warm light that landing throws back on the knight.
    ///
    /// <para><b>It does not replace the painting, and that is the whole design.</b> The plume in
    /// <c>background_ini</c> is better art than anything this can generate, so every piece here
    /// is drawn OVER it and anchored TO it. What a painting cannot do is move, and that is the
    /// only thing being added.</para>
    ///
    /// <para><b>Why not a ParticleSystem.</b> The rule this project has now written down four
    /// times — <c>HudMoteLayer</c>, <c>MinimapFx</c>, <c>TitleParticleField</c>,
    /// <c>MenuFxLayer</c>: a <c>ParticleSystem</c> is a world renderer and does not sort against
    /// the <c>Graphic</c>s of a Screen Space Overlay canvas. It would sit either in front of the
    /// progress bar or behind the painting, never between them. <see cref="MenuFxLayer"/> already
    /// is the pooled-quad answer, so this drives two of those rather than writing a third.</para>
    ///
    /// <para><b>Parented to the painting's own RectTransform</b>, which buys three things at
    /// once. A normalized anchor becomes a multiply instead of a cross-space transform. The
    /// effects follow <c>AspectRatioFitter.EnvelopeParent</c>'s crop for free, so they stay on
    /// the dragon's mouth at any window shape. And uGUI draws depth-first in hierarchy order, so
    /// children of the background land after the painting and before the bar and the status
    /// line — which is exactly where they belong, and is why nothing here needs a sorting
    /// decision.</para>
    ///
    /// <para><b>The frame step is CAPPED, and that is not a nicety here.</b> This screen runs
    /// while the main thread is building the world, so frames are long and irregular; the music
    /// panel already records that one long frame ate a whole flash. Without the cap an ember
    /// crosses the screen in a single step. It reads <c>unscaledDeltaTime</c> for the same
    /// reason the rest of this controller does: <c>Time.timeScale</c> during a load may be
    /// anything at all.</para>
    /// </summary>
    public sealed class LoadingFireFX
    {
        /// <summary>
        /// The longest step the effect will integrate AT ONCE. A three-second frame during a
        /// world build is still one frame, and an ember given three seconds of travel in one go
        /// is a streak across the screen.
        /// </summary>
        private const float MaxStep = 1f / 20f;

        /// <summary>
        /// How many of those the effect will run to catch up with one real frame.
        ///
        /// <para><b>Capping the step alone was half an answer and the other half was the
        /// stutter.</b> The emission debt is <c>rate x dt</c>, so clamping <c>dt</c> also clamps
        /// how many embers a frame is allowed to produce — and this screen runs at whatever the
        /// world build leaves it. Measured on a real boot: 4201 ms in 49 frames, 85.7 ms each,
        /// 11.7 fps, with individual frames of 145 ms. Against a 50 ms clamp that is
        /// <b>56 % of the intended embers</b>, and the shortfall tracks the frame length — a
        /// short frame emits at full rate and a long one is cut to a third, so the jet visibly
        /// thins and refills with the boot's own pacing. That is the stutter, and it is an
        /// artifact of the cap rather than of the frame rate.</para>
        ///
        /// <para>Sub-stepping pays the whole elapsed time in bounded pieces, so the same number
        /// of embers is born and each is placed along the path it would really have taken. Six
        /// of them is 300 ms of catch-up for 0.02 ms of work; past that the remainder is DROPPED
        /// rather than chased, because a stall is a stall and simulating a lost second at once
        /// puts the jet somewhere it was never seen travelling to.</para>
        /// </summary>
        private const int MaxSubSteps = 6;

        /// <summary>
        /// The frame time the emission is tuned for. Above it the jet is slowed and thinned in
        /// the same proportion, which keeps the picture and shortens the hop.
        /// </summary>
        private const float PaceTarget = 1f / 30f;

        /// <summary>The most the pacing may be stretched. Past ~2 the jet reads as syrup.</summary>
        private const float PaceMax = 2f;

        private const int EmberCapacity = 96;
        private const int SmokeCapacity = 24;

        /// <summary>Embers a second. Under the rate at which a viewer would call it an animation.</summary>
        private const float EmberRate = 46f;
        private const float SmokeRate = 2.4f;

        // The jet's own colours, read off the painting: near-white gold where it leaves the
        // mouth, deep orange-red by the time it lands.
        private static readonly Color EmberHot = new Color(1.00f, 0.76f, 0.34f, 1f);
        private static readonly Color EmberCool = new Color(0.98f, 0.30f, 0.07f, 1f);
        private static readonly Color MuzzleTint = new Color(1.00f, 0.54f, 0.17f, 1f);
        private static readonly Color WashTint = new Color(1.00f, 0.48f, 0.16f, 1f);

        // Smoke is the one piece that is NOT additive, and it cannot be: a dark pixel added to
        // what is behind it changes nothing, which is the rule KiAuraFX and VortexFunnelFX both
        // record for their own ground debris. It is therefore the only layer here that takes
        // light OUT of the picture, which is also what makes it read as something the fire is
        // DOING rather than as more of the fire.
        private static readonly Color SmokeTint = new Color(0.16f, 0.13f, 0.12f, 1f);

        private readonly RectTransform _art;
        private readonly LoadingArtAnchors.FireAnchor _anchor;
        private readonly MenuFxLayer _embers;
        private readonly MenuFxLayer _smoke;
        private readonly Image _muzzle;
        private readonly Image _wash;
        private readonly Material _additive;

        private float _time;
        private float _emberDebt;
        private Vector2 _laidOutFor = Vector2.zero;
        private float _accumulator;
        private float _frameTime = PaceTarget;
        private float _smokeDebt;

        /// <summary>True when there is an anchor for this art and the pieces were built.</summary>
        public bool IsLive => _anchor.IsValid && _embers != null;

        /// <summary>Live embers. For the fixtures and for the budget.</summary>
        public int EmberCount => _embers != null ? _embers.Alive : 0;

        /// <summary>Live smoke puffs.</summary>
        public int SmokeCount => _smoke != null ? _smoke.Alive : 0;

        /// <summary>
        /// Seconds of fire actually simulated so far. The stutter was the gap between this and
        /// the time that really passed, so it is the one number a fixture needs to see.
        /// </summary>
        public float SimulatedSeconds => _time;

        /// <summary>
        /// Builds the effect over <paramref name="artRect"/>, or returns null when the art has no
        /// measured anchor. A painting nobody has measured draws nothing at all rather than a jet
        /// of embers guessed at the middle of the screen.
        /// </summary>
        public static LoadingFireFX Attach(RectTransform artRect, string artName, MenuStyle style)
        {
            if (artRect == null) return null;
            var anchor = LoadingArtAnchors.FireFor(artName);
            if (!anchor.IsValid) return null;
            var art = MenuArt.Get(style);
            if (art == null) return null;
            return new LoadingFireFX(artRect, anchor, art, style);
        }

        private LoadingFireFX(RectTransform artRect, LoadingArtAnchors.FireAnchor anchor,
                              MenuArt art, MenuStyle style)
        {
            _art = artRect;
            _anchor = anchor;
            _additive = BuildAdditive(style);

            // Order matters and it is the only ordering decision in this file: the wash is the
            // furthest back because it is light falling ON the scene, then the smoke, then the
            // embers, and the muzzle last because it is the brightest thing and the source of
            // everything else.
            _wash = BuildSoftImage(art, "FireWash", WashTint, 0.16f);
            _smoke = MenuFxLayer.Create(artRect, art, SmokeCapacity, null);
            _smoke.name = "FireSmoke";
            _smoke.UseSoftMotes = true;
            _embers = MenuFxLayer.Create(artRect, art, EmberCapacity, _additive);
            _embers.name = "FireEmbers";
            _embers.UseSoftMotes = true;
            _muzzle = BuildSoftImage(art, "FireMuzzle", MuzzleTint, 0.34f);

            Layout();
        }

        /// <summary>
        /// One soft additive blob, sized and placed by <see cref="Layout"/>.
        ///
        /// <para>It draws from the title's bilinear page rather than the menu atlas for the
        /// reason <see cref="MenuFxLayer.UseSoftMotes"/> exists: the atlas is point-filtered on
        /// purpose, and a 9-pixel glow blown up to a couple of hundred units on a point filter is
        /// a staircase.</para>
        /// </summary>
        private Image BuildSoftImage(MenuArt art, string name, Color tint, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_art, false);
            var img = go.AddComponent<Image>();
            img.sprite = art.TitleMote != null ? art.TitleMote : art.MoteGlow;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            if (_additive != null) img.material = _additive;
            var c = tint;
            c.a = alpha;
            img.color = c;
            return img;
        }

        /// <summary>
        /// Places the two blobs from the anchor.
        ///
        /// <para><b>By ANCHORS, so the placement survives a change of resolution without any
        /// code running at all.</b> The first version wrote an <c>anchoredPosition</c> and a
        /// <c>sizeDelta</c> in units measured against whatever rect it happened to see at build
        /// time, and both go stale the moment the painting changes size. Measured on the shipped
        /// anchor: a muzzle sitting exactly on the dragon's mouth at 1600x1066 drifted to
        /// (0.515, 0.517) at 3840x2560 and to (0.555, 0.564) at 1024x683, its diameter running
        /// from 3.3 % of the width to 1.4 % and then 5.1 %, and the wash moved a QUARTER of the
        /// painting. Anchors that are pure fractions with zero offsets cannot drift, because
        /// there is no pixel left to be wrong.</para>
        ///
        /// <para>Still re-runnable, and <see cref="Tick"/> re-runs it whenever the rect changes.
        /// That is not only for resizes: <c>Attach</c> is called from the background's own build,
        /// BEFORE uGUI has laid anything out, so the first size this ever sees is not the
        /// painting's — it is whatever the RectTransform was constructed with.</para>
        /// </summary>
        public void Layout()
        {
            if (_art == null) return;
            var size = _art.rect.size;
            if (size.x <= 1f || size.y <= 1f) return;
            _laidOutFor = size;

            float aspect = size.x / size.y;

            // The jet's length as a fraction of the painting's WIDTH, derived rather than
            // measured in units — a length in units is exactly the thing that went stale:
            //   |span| = W * sqrt(dx^2 + (dy / aspect)^2)
            var d = _anchor.Tip - _anchor.Mouth;
            float dy = d.y / Mathf.Max(0.0001f, aspect);
            float spanFraction = Mathf.Sqrt(d.x * d.x + dy * dy);

            // Both of these shipped two and a half times this big and twice this bright, and the
            // first live capture is what settled it: additive light over a region the PAINTING
            // already draws as fire has nothing left to add, so it clips to white. The muzzle
            // became a bloom sitting on the dragon's snout and the wash bleached the whole left
            // of the picture, which cost the art the contrast the effect was supposed to serve.
            // An accent over bright paint has to be small and faint or it erases what it accents.
            Place(_muzzle, _anchor.Mouth, spanFraction * 0.17f, aspect);

            // The wash is the light the landing throws BACK, so it is centred PAST where the
            // fire arrives rather than on it: the tip is the brightest paint on the canvas, and
            // lighting that is lighting the one thing that needs no help. A fifth of a jet
            // beyond it is the knight, which is the surface the glow is actually about.
            Place(_wash, _anchor.Axis(1.2f), spanFraction * 0.85f, aspect);
        }

        /// <summary>
        /// One blob, expressed entirely as a fraction of the painting: the anchors carry the
        /// position AND the size and both offsets are zero, so there is no number left in the
        /// rect that a change of resolution could invalidate.
        ///
        /// <para><b>A circle stays a circle because the painting's ASPECT is constant.</b>
        /// <c>AspectRatioFitter.EnvelopeParent</c> preserves the art's own ratio at every window
        /// shape — only the SIZE changes — so a fraction of the width and that same fraction
        /// divided by the aspect describe the same square however big the picture gets. That
        /// fact is what this whole approach rests on; without it, anchor fractions would turn
        /// every blob into an ellipse the moment the window changed shape.</para>
        /// </summary>
        private static void Place(Image img, Vector2 uv, float diameterOfWidth, float aspect)
        {
            if (img == null) return;
            var rt = (RectTransform)img.transform;
            float halfX = diameterOfWidth * 0.5f;
            float halfY = halfX * aspect;                  // halfY * height == halfX * width
            rt.anchorMin = new Vector2(uv.x - halfX, uv.y - halfY);
            rt.anchorMax = new Vector2(uv.x + halfX, uv.y + halfY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A normalized anchor as a point in the painting's rect, bottom-left origin.</summary>
        private static Vector2 ToLocal(Vector2 uv, Vector2 size) => new Vector2(uv.x * size.x, uv.y * size.y);

        // There is deliberately no alpha of its own. Every piece here is a child of the
        // background, which is a child of the screen's CanvasGroup, and a CanvasGroup's alpha
        // already multiplies through every CanvasRenderer under it — so the fade-out fades the
        // fire for free. A second alpha would be a dial that agrees with that one until the day
        // somebody moves one of them.

        /// <summary>
        /// Advances everything. Public and taking its own delta, the rule <c>HudMoteLayer</c>,
        /// <c>WorldBarRig</c> and <c>TitleParticleField</c> all follow, so a fixture can drive it
        /// with no clock at all.
        /// </summary>
        public void Tick(float dt)
        {
            if (!IsLive || dt <= 0f) return;

            // The RAW frame time is what the pacing reads — it is the quantity the player is
            // actually seeing, and it is the one thing the sub-stepping below is about to hide.
            _frameTime = Mathf.Lerp(_frameTime, dt, 0.25f);

            // The painting changed size: a window resize, or — every single time — the first
            // layout pass after a build that ran before uGUI had laid anything out. One Vector2
            // compare a frame, against a decoration that would otherwise sit off the dragon's
            // mouth for the whole life of the screen.
            var size = _art.rect.size;
            bool live = size.x > 1f && size.y > 1f;
            if (live && size != _laidOutFor) Layout();

            _accumulator += dt;
            int steps = 0;
            while (_accumulator > 0f && steps < MaxSubSteps)
            {
                float step = Mathf.Min(_accumulator, MaxStep);
                _accumulator -= step;
                steps++;
                Step(step, size, live);
            }
            // Out of sub-steps: drop whatever is left rather than chase it. Catching up on a lost
            // second at once is exactly the streak the cap exists to prevent.
            _accumulator = 0f;

            Breathe();
        }

        /// <summary>One bounded slice of time. Never longer than <see cref="MaxStep"/>.</summary>
        private void Step(float dt, Vector2 size, bool live)
        {
            _time += dt;
            if (live)
            {
                EmitEmbers(dt, size);
                EmitSmoke(dt, size);
            }
            _embers.Tick(dt);
            _smoke.Tick(dt);
        }

        /// <summary>
        /// How far the emission is stretched for the frame rate it is living at.
        ///
        /// <para>At 12 fps a mouth ember crosses 29 units between two drawn frames and reads as a
        /// hop, not as a spark. The trade that fixes it keeps the jet's REACH — speed times life —
        /// and moves along it: divide the speed, multiply the life, and divide the RATE by the
        /// same number. Speed over pace shortens the hop; life times pace keeps the ember
        /// travelling the same distance before it dies; rate over pace keeps the same number of
        /// embers spread over the same jet, so the picture is unchanged and only the size of each
        /// step shrinks. Dropping the rate is the half that is easy to forget, and without it the
        /// longer lives simply double the count and the jet reads as a smear.</para>
        /// </summary>
        private float Pace => Mathf.Clamp(_frameTime / PaceTarget, 1f, PaceMax);

        /// <summary>
        /// Embers born along the painted jet and carried away from the mouth.
        ///
        /// <para>Three things make them read as riding a jet rather than as a spray. They are
        /// born ALONG the axis rather than at the mouth, with the births crowded toward the
        /// mouth (<c>u * u</c>), so the whole length of the plume is alive instead of only its
        /// root. They are launched FASTER the closer to the mouth they are born, which is what a
        /// jet does and what makes the far end look like it is slowing down. And they are given
        /// a small negative gravity, so once the throw has bled off through the drag what is
        /// left is a rise — an ember that keeps travelling forward forever reads as a tracer.</para>
        /// </summary>
        private void EmitEmbers(float dt, Vector2 size)
        {
            float pace = Pace;
            _emberDebt += (EmberRate / pace) * dt;
            var dir = ToLocal(_anchor.Tip, size) - ToLocal(_anchor.Mouth, size);
            float span = dir.magnitude;
            if (span < 1f) { _emberDebt = 0f; return; }
            dir /= span;
            var normal = new Vector2(-dir.y, dir.x);

            while (_emberDebt >= 1f)
            {
                _emberDebt -= 1f;
                float u = Random.value;
                float t = u * u;                                   // crowded at the mouth
                float half = _anchor.HalfWidthAt(t) * size.y;
                var at = ToLocal(_anchor.Axis(t), size)
                       + normal * Random.Range(-half, half) * 0.85f;

                float speed = Mathf.Lerp(340f, 90f, t) * Random.Range(0.55f, 1.15f) / pace;
                var colour = Color.Lerp(EmberHot, EmberCool, Mathf.Clamp01(t + Random.Range(-0.2f, 0.3f)));
                colour.a = 1f;

                _embers.Emit(at,
                             dir * speed + normal * Random.Range(-26f, 26f) / pace,
                             colour,
                             Random.Range(0.45f, 1.05f) * pace,
                             Random.value < 0.18f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                             // Both of these are per-TIME, so stretching the life changes the
                             // shape of the path unless they are stretched back. Position under
                             // a constant acceleration is v*t + a*t^2/2, so halving v and
                             // doubling t leaves the first term alone and quadruples the second:
                             // gravity goes by the SQUARE. Drag compounds as exp(-d*life), so it
                             // goes by the first power. Without these the slowed ember arcs up
                             // four times as hard and stops dead, which is a different effect
                             // wearing the same numbers.
                             gravity: -55f / (pace * pace), drag: 2.1f / pace,
                             size: Random.Range(0.14f, 0.34f), twinkle: true);
            }
        }

        /// <summary>
        /// Smoke where the fire lands. Slow, large, dark and few — it is the piece that says the
        /// flame is doing something to the world rather than merely existing in front of it.
        /// </summary>
        private void EmitSmoke(float dt, Vector2 size)
        {
            _smokeDebt += SmokeRate * dt;
            while (_smokeDebt >= 1f)
            {
                _smokeDebt -= 1f;
                float half = _anchor.TipHalfWidth * size.y;
                // Past the landing, for the same reason the wash is: smoke drawn over the
                // painted flame subtracts light from the brightest thing in the picture and
                // reads as the fire going out.
                var at = ToLocal(_anchor.Axis(1.06f), size)
                       + new Vector2(Random.Range(-half, half), Random.Range(-half * 0.5f, half));

                var colour = SmokeTint;
                colour.a = 0.11f;
                _smoke.Emit(at,
                            new Vector2(Random.Range(-16f, 6f), Random.Range(26f, 52f)),
                            colour,
                            Random.Range(2.1f, 3.4f), MenuMoteShape.Dot,
                            gravity: -6f, drag: 0.55f,
                            size: Random.Range(1.3f, 2.2f), twinkle: false);
            }
        }

        /// <summary>
        /// The muzzle and the wash breathe TOGETHER, because they are the same event seen at both
        /// ends: a jet surging is brighter at its source and throws more light where it lands.
        /// Two sines at incommensurable rates rather than one — a single sine is a pulse, and a
        /// pulse reads as a lamp with a flicker, which is the note <c>TitleParticleField</c>
        /// already carries for the word's own flame.
        /// </summary>
        private void Breathe()
        {
            float surge = 0.62f
                        + 0.26f * Mathf.Sin(_time * 4.7f)
                        + 0.12f * Mathf.Sin(_time * 11.3f + 1.7f);
            surge = Mathf.Clamp01(surge);

            if (_muzzle != null)
            {
                var c = MuzzleTint;
                c.a = Mathf.Lerp(0.11f, 0.27f, surge);
                _muzzle.color = c;
            }
            if (_wash != null)
            {
                var c = WashTint;
                c.a = Mathf.Lerp(0.05f, 0.13f, surge);
                _wash.color = c;
            }
        }

        /// <summary>Drops everything. The screen is torn down wholesale, so this is belt-and-braces.</summary>
        public void Dispose()
        {
            _embers?.Clear();
            _smoke?.Clear();
            if (_additive == null) return;
            // The branch this project requires everywhere a runtime object is torn down:
            // Object.Destroy is an outright ERROR in Edit Mode, and DestroyImmediate on a live
            // material during Play is the other half of the same mistake.
            if (Application.isPlaying) Object.Destroy(_additive);
            else Object.DestroyImmediate(_additive);
        }

        private static Material BuildAdditive(MenuStyle style)
        {
            var shader = style != null && style.hudFxShader != null
                ? style.hudFxShader
                : Shader.Find("Valkur/UI/HudFx");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "LoadingFireAdditive", hideFlags = HideFlags.DontSave };
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }
    }
}
