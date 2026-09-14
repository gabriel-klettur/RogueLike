using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Title
{
    /// <summary>
    /// The game's name at the top of every pre-game screen: the particle field and the beats
    /// that bring it in.
    ///
    /// <para>It owns three things and nothing else — WHERE the title sits, WHEN it gathers, and
    /// the embers that come off it once it has. The letterforms belong to
    /// <see cref="TitleGlyphStrokes"/> and the drawing to <see cref="TitleParticleField"/>, which
    /// is what lets the whole thing be tested without a scene.</para>
    ///
    /// <para><b>There is no tagline, and the machinery for one went with it.</b> A line of prose
    /// under the mark ("Un roguelike forjado en el norte") was drawn here, faded in behind the
    /// word and laid out under it. Leaving the label in place with an empty string would have
    /// left a TMP component, a fade that runs every frame and a layout line that all describe
    /// something nobody can see — the authored-and-inert shape this project has shipped a dozen
    /// times. The mark stands on its own.</para>
    /// </summary>
    public sealed class MenuTitle : MonoBehaviour
    {
        private MenuStyle _style;
        private MenuArt _art;
        private MenuFxLayer _fx;
        private TitleParticleField _field;
        private UnityEngine.UI.Image _halo;
        private RectTransform _root;

        private float _emberDebt;
        private float _dripDebt;
        private bool _reduceMotion;
        private float _alpha = 1f;

        /// <summary>The particle field, so a test can read the cloud without a lookup.</summary>
        public TitleParticleField Field => _field;

        /// <summary>0 while the word is scattered, 1 once it has settled.</summary>
        public float Assembly => _field != null ? _field.Assembly : 0f;

        /// <summary>Fades the whole title. Sub-screens dim it rather than hiding it.</summary>
        public float Alpha
        {
            get => _alpha;
            set
            {
                _alpha = Mathf.Clamp01(value);
                if (_field != null) _field.Alpha = _alpha;
                ApplyHaloColour();
            }
        }

        public static MenuTitle Create(Transform canvas, MenuArt art, MenuStyle style,
                                       Material additive, MenuFxLayer fx, string text,
                                       bool reduceMotion)
        {
            var go = new GameObject("MenuTitle", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(10f, 10f);

            var t = go.AddComponent<MenuTitle>();
            t._style = style;
            t._art = art;
            t._fx = fx;
            t._root = root;
            t._reduceMotion = reduceMotion;

            // The halo is built FIRST so it sits behind the motes. It is the one piece of the
            // title that is not additive: it has to take light OUT of the picture.
            var haloRt = new GameObject("Halo", typeof(RectTransform)).GetComponent<RectTransform>();
            haloRt.SetParent(go.transform, false);
            haloRt.anchorMin = haloRt.anchorMax = new Vector2(0.5f, 1f);
            haloRt.pivot = new Vector2(0.5f, 1f);
            t._halo = haloRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            t._halo.sprite = art.SoftPlate;
            t._halo.type = UnityEngine.UI.Image.Type.Simple;
            t._halo.raycastTarget = false;
            t._halo.color = new Color(0f, 0f, 0f, style.titleHaloStrength);

            t._field = TitleParticleField.Create(go.transform, art, style, additive);
            t._field.SetText(text, reduceMotion);

            t.Layout();
            return t;
        }

        /// <summary>Re-lays the word out — a language change, or a new title.</summary>
        public void SetText(string text)
        {
            if (_field != null) _field.SetText(text, _reduceMotion);
            Layout();
        }

        public void SetReduceMotion(bool reduce)
        {
            _reduceMotion = reduce;
            if (_field != null) _field.SetReduceMotion(reduce);
        }

        /// <summary>Gathers the word again. The title screen does it when it is returned to.</summary>
        public void Replay()
        {
            if (_field == null) return;
            _field.Replay();
        }

        /// <summary>
        /// Opens with the word already there. The brand plane assembled it seconds ago; the menu
        /// inheriting that state is what makes the two one introduction instead of two.
        /// </summary>
        public void SnapSettled()
        {
            _field?.SnapToSettled();
        }

        /// <summary>A sweep of light across the word. The confirm beat of the main menu.</summary>
        public void Sweep() => _field?.Sweep();

        private void Layout()
        {
            if (_field == null || _style == null) return;
            var frt = (RectTransform)_field.transform;
            var size = _field.TitleSize;

            // The field's rect is the word's own bounding box; the title is centred on the canvas
            // by moving the RECT, never by moving the motes, so the cloud's coordinates stay the
            // ones a test can compare against the stroke table.
            frt.anchoredPosition = new Vector2(0f, -_style.titleTopOffset);
            frt.pivot = new Vector2(0.5f, 1f);
            frt.sizeDelta = size;

            if (_halo != null)
            {
                // Sized from the WORD, not from a constant, so a longer title darkens more of the
                // art and a shorter one darkens less.
                // The halo belongs to the LOOK: red carries under a third of white's luminance
                // per unit of colour, so a fire word needs more plate under it than a cream one
                // to stay legible over the same painted carousel. One shared constant would be
                // tuned for whichever look was authored last.
                var look = _style.ResolveTitleLook();
                float padX = size.x * look.haloPadding;
                float padY = size.y * look.haloPadding * 1.6f;
                var hrt = (RectTransform)_halo.transform;
                hrt.sizeDelta = new Vector2(size.x + padX * 2f, size.y + padY * 2f);
                hrt.anchoredPosition = new Vector2(0f, -_style.titleTopOffset + padY);
                ApplyHaloColour();
            }
        }

        /// <summary>
        /// Advances the title. Public and taking its own delta so an EditMode test can drive it
        /// without a clock, exactly as <c>HudMoteLayer.Tick</c> and <c>WorldBarRig</c> do.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f || _field == null) return;
            _field.Tick(dt);

            EmitEmbers(dt);
            EmitDrips(dt);
        }

        /// <summary>
        /// Embers rising off the settled word. The ONE loop the menu allows itself, and it is
        /// kept under the attention floor by its rate: seven a second spread over six letters is
        /// a spark every couple of seconds in any one place, which reads as heat rather than as
        /// an animation. Off entirely under reduce motion.
        /// </summary>
        private void EmitEmbers(float dt)
        {
            if (_fx == null || _field == null || _reduceMotion || _alpha <= 0.05f) return;
            if (_field.Assembly < 1f) return;
            // The rate belongs to the LOOK: a bar of cooling metal throws the odd spark, and a
            // word that is on fire throws a lot more. One shared number would be tuned for
            // whichever of the two was authored last.
            float rate = _style != null ? _style.ResolveTitleLook().emberRate : 0f;
            if (rate <= 0f) return;

            _emberDebt += rate * dt;
            while (_emberDebt >= 1f)
            {
                _emberDebt -= 1f;
                if (!_field.TryPickEmberSource(out var local, out var colour)) return;

                _fx.Emit(ToFxSpace(local),
                         new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 34f)),
                         colour, Random.Range(0.9f, 1.9f),
                         Random.value < 0.25f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                         gravity: -4f, drag: 0.5f, size: Random.Range(0.7f, 1.15f), twinkle: true);
            }
        }

        /// <summary>
        /// Drips falling off the lower edge of the word. The one gesture that says MOLTEN rather
        /// than merely lit, which is why it belongs to the look and is zero on every look that is
        /// not: a cream word shedding drops is a word melting for no reason.
        ///
        /// <para>It borrows the ember pipeline rather than growing a second one — a drip is a
        /// mote with the gravity pointing the other way. Positive gravity, a small initial push
        /// DOWN so it separates from the letter before it accelerates, no twinkle (a drop of
        /// molten rock is not a spark), and a short life so it falls only a little way below the
        /// letter it left.</para>
        /// </summary>
        private void EmitDrips(float dt)
        {
            if (_fx == null || _field == null || _reduceMotion || _alpha <= 0.05f) return;
            if (_field.Assembly < 1f) return;
            float rate = _style != null ? _style.ResolveTitleLook().dripRate : 0f;
            if (rate <= 0f) return;

            _dripDebt += rate * dt;
            while (_dripDebt >= 1f)
            {
                _dripDebt -= 1f;
                if (!_field.TryPickDripSource(out var local, out var colour)) return;

                _fx.Emit(ToFxSpace(local),
                         new Vector2(Random.Range(-3f, 3f), Random.Range(-9f, -3f)),
                         colour, Random.Range(0.55f, 0.95f), MenuMoteShape.Dot,
                         gravity: 46f, drag: 0f, size: Random.Range(0.75f, 1.2f), twinkle: false);
            }
        }

        /// <summary>
        /// A point in the field's rect, in the mote layer's rect.
        ///
        /// <para>Through the WORLD, never by adding the two anchored positions: that shortcut
        /// works until the first time somebody moves one of the two rects, and then it is wrong
        /// in a way nothing logs.</para>
        /// </summary>
        private Vector2 ToFxSpace(Vector2 local)
        {
            var fieldRt = (RectTransform)_field.transform;
            var world = fieldRt.TransformPoint(new Vector3(local.x + fieldRt.rect.xMin,
                                                          local.y + fieldRt.rect.yMin, 0f));
            var fxRt = (RectTransform)_fx.transform;
            return (Vector2)fxRt.InverseTransformPoint(world) - fxRt.rect.min;
        }
    
        // ── The plate, solved rather than tuned ──────────────────────────────

        /// <summary>How bright the carousel frame under the word is. Measured, never assumed.</summary>
        private float _backdropLuminance = 0.18f;

        /// <summary>
        /// Tells the title which image the carousel is holding, so its plate can be solved for a
        /// contrast ratio instead of carrying a constant.
        ///
        /// <para>Called on every carousel change rather than every frame: the measurement is a
        /// GPU blit and one small readback, which is cheap once per seven seconds and wasteful
        /// sixty times a second — and the answer cannot change in between, because the image
        /// does not.</para>
        /// </summary>
        public void SetBackdrop(Sprite sprite)
        {
            float measured = BackdropLuminance.Measure(sprite);
            if (Mathf.Approximately(measured, _backdropLuminance)) return;
            _backdropLuminance = measured;
            ApplyHaloColour();
        }

        /// <summary>The plate's live colour: its own tint, at the alpha the contrast target asks for.</summary>
        private void ApplyHaloColour()
        {
            if (_halo == null) return;

            var look = _style != null ? _style.ResolveTitleLook() : null;
            if (look == null)
            {
                _halo.color = new Color(0f, 0f, 0f, 0.6f * _alpha);
                return;
            }

            // What the word READS as is its middle tone: most motes land near it, so that is the
            // ink the ratio has to be solved for rather than the white-hot core, which is a
            // minority of the cloud and would under-plate the word.
            float ink = BackdropLuminance.Luminance(look.Sample(0.5f));
            float plate = BackdropLuminance.Luminance(look.haloTint);
            float alpha = BackdropLuminance.PlateAlphaFor(ink, _backdropLuminance, plate,
                                                          look.contrastTarget,
                                                          look.haloStrength, look.haloCeiling);
            var tint = look.haloTint;
            _halo.color = new Color(tint.r, tint.g, tint.b, alpha * _alpha);
        }
}
}
