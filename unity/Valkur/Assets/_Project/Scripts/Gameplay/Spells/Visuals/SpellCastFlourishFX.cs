using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The flourish a character makes when they cast — a different one per spell.
    ///
    /// <para>WHY IT EXISTS. Every spell in the game produced its effect somewhere ELSE — a
    /// projectile leaving, a wall rising three units away, an aura settling on the ground —
    /// and nothing happened on the caster themself beyond a pose change. A pose alone does
    /// not read as casting in a 16-PPU top-down game: the character is forty pixels tall and
    /// the difference between their idle frame and their cast frame is a few of them. What
    /// reads is LIGHT gathering on the body and then leaving it.</para>
    ///
    /// <para>TWO INDEPENDENT AXES, and keeping them apart is the whole design.
    /// <see cref="ElementPalette"/> supplies the COLOUR and answers "what element is this".
    /// <see cref="CastFlourishProfile"/> supplies the SHAPE and answers "what is the caster
    /// doing" — where the motes come from, whether the ground circle draws in or pushes out,
    /// which way the lance points, where the shockwave leaves from, how long the whole thing
    /// takes. They are genuinely orthogonal: an ice wall and an ice bolt are the same blue and
    /// nothing like the same gesture, while a summoned totem and a summoned wall are different
    /// colours and the same gesture. The first version of this folded them together, which
    /// meant one flourish per ELEMENT and read as decoration rather than as the character
    /// casting a particular spell.</para>
    ///
    /// <para>Eight families, each a sentence: <b>Hurl</b> throws, <b>Edge</b> cuts,
    /// <b>Conjure</b> lays something down, <b>Invoke</b> calls something from the sky,
    /// <b>Ward</b> keeps power rather than spending it, <b>Surge</b> makes the body the
    /// projectile, <b>Vanish</b> implodes, <b>Channel</b> holds. See <see cref="CastFlourishFamilies"/>.</para>
    ///
    /// <para>Like <see cref="WeaponSwapFlashFX"/> it FOLLOWS its owner rather than being
    /// parented to them — most spells allow movement, and parenting would inherit the entity
    /// scale and take the <c>Light2D</c> radius with it.</para>
    /// </summary>
    internal sealed partial class SpellCastFlourishFX : MonoBehaviour
    {
        /// <summary>
        /// Drag on a departing mote. Total travel is its speed divided by this, so the pair
        /// sets how far a family's afterglow reaches: Hurl's 4-10 units per second against 3.0
        /// spreads from 1.3 to 3.3 units, which is far enough to read as the spell leaving the
        /// caster. At the first value tried (4.5) the furthest mote travelled 1.2 units — a
        /// throw the eye cannot separate from the hand it left.
        /// </summary>
        private const float MOTE_DRAG = 3.0f;

        private const int ORDER_AURA = 58;
        private const int ORDER_LANCE = 60;
        private const int ORDER_BURST = 61;
        private const int ORDER_HAND = 62;
        private const int ORDER_HAND_HOT = 63;
        private const int ORDER_MOTE = 64;

        /// <summary>
        /// Ring's bright band peaks at normalized radius 0.78, so this scale puts the drawn
        /// contour at exactly <paramref name="radius"/> world units — the constant CLAUDE.md
        /// records, and the reason a sigil can be pinned to a real distance at any size.
        /// Internal so a test can pin the number rather than trusting a literal.
        /// </summary>
        internal static float RingScaleFor(float radius) => radius / 0.39f;

        /// <summary>
        /// Whether a spell gets a flourish at all.
        ///
        /// <para>Three types are refused and each would be actively wrong.
        /// <see cref="SpellType.WeaponLoadout"/> already has <see cref="WeaponSwapFlashFX"/>,
        /// which exists to cover a specific cut and would be fighting this for the same
        /// pixels; <see cref="SpellType.AnimationProbe"/> exists so an animation can be
        /// WATCHED in the Spells Editor, and covering the character in light defeats the only
        /// thing a probe is for; <see cref="SpellType.EnergyCharge"/> opens with an ignition
        /// flare of its own that runs for twice as long, and two systems lighting the same
        /// silhouette in the same half-second read as one of them being broken.</para>
        ///
        /// <para>A predicate rather than an inline check inside <see cref="Play"/> because
        /// <c>Play</c> also refuses to build anything outside Play Mode, and a test in Edit
        /// Mode cannot tell the two refusals apart.</para>
        /// </summary>
        internal static bool AppliesTo(SpellDefinition spell)
            => spell != null
               && spell.type != SpellType.WeaponLoadout
               && spell.type != SpellType.AnimationProbe
               && spell.type != SpellType.EnergyCharge;

        private Transform _owner;
        private Vector3 _bodyOffset;      // owner-relative centre of the silhouette
        private Vector3 _handOffset;      // owner-relative cast anchor
        private Vector3 _anchor;          // whichever of the two this family gathers on
        private Vector2 _direction;
        private Vector2 _bodySize;
        private ElementPalette _palette;
        private CastFlourishProfile _profile;
        private float _age;

        private SpriteRenderer _sigilOuter;
        private SpriteRenderer _sigilInner;
        private SpriteRenderer _aura;
        private SpriteRenderer _hand;
        private SpriteRenderer _handHot;
        private SpriteRenderer _burst;
        private SpriteRenderer _lance;
        private Component _light;

        private SpriteTintStack _bodyTint;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private float[] _moteAngle;
        private float[] _moteRadius;
        private float[] _moteSpin;
        private float[] _moteSize;
        private Vector2[] _moteFlight;    // where each one goes after the release

        /// <summary>The family this flourish resolved to. Read by tests and the DevConsole.</summary>
        internal string FamilyName => _profile.FamilyName;

        // ── Entry point ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Play the flourish for <paramref name="spell"/> on <paramref name="caster"/>.
        /// See <see cref="AppliesTo"/> for which spells are refused and why.
        /// </summary>
        public static void Play(SpellDefinition spell, Transform caster, Vector2 direction)
        {
            if (caster == null || !AppliesTo(spell)) return;

            // The rig destroys itself from Update, which never runs outside Play Mode.
            // Building one anyway leaves a permanent cluster of objects in the scene.
            if (!Application.isPlaying) return;

            Vector3 hand = ProjectileExecutor.ResolveCastStart(caster, direction, spell);
            // The element chooses the palette; the spell's own swatch then overrides its HUE.
            // Without the second half the gather is the wrong colour for every spell that
            // authored one — a green laser gathered arcane violet — and a spell with no element
            // at all, which is most of them, had no way to say what colour it was.
            var palette = ElementPalette.For(ProjectileExecutor.ResolveElement(spell) ?? SpellElement.Arcane)
                                        .RecolouredTo(ResolveSwatch(spell));
            Play(caster, direction, hand, palette, CastFlourishProfile.Build(spell));
        }

        /// <summary>
        /// The colour this spell IS, which is not always the raw <c>particleColor</c>.
        ///
        /// <para>Two types resolve a tint of their own rather than using the raw field, and
        /// both apply a default of their own when the swatch is unauthored — so reading
        /// <c>particleColor</c> here would let the gather disagree with the thing it announces.
        /// That is not hypothetical: plain <c>slash</c> leaves the swatch untouched, so the
        /// blade fell back to a pale blue-white while the gather read the same field as
        /// unauthored and stayed arcane violet. A totem keeps its original gold the same way.</para>
        ///
        /// <para>Every other type answers with the field itself. A type that grows a resolved
        /// tint later belongs in this switch rather than inline at the call site — one place
        /// that answers "what colour is this spell".</para>
        ///
        /// <para>Internal so a test can assert the agreement across the shipped catalog rather
        /// than re-deriving the rule and testing its own copy of it.</para>
        /// </summary>
        internal static Color ResolveSwatch(SpellDefinition spell)
        {
            if (spell == null) return Color.white;

            switch (spell.type)
            {
                case SpellType.Slash: return SlashExecutor.ResolveTint(spell);
                case SpellType.Totem: return TotemExecutor.ResolveTint(spell);
                default: return spell.particleColor;
            }
        }

        /// <summary>Explicit overload, so tests and tooling need no SpellDefinition.</summary>
        public static SpellCastFlourishFX Play(Transform caster, Vector2 direction,
            Vector3 handWorld, ElementPalette palette, CastFlourishProfile profile)
        {
            if (caster == null || !Application.isPlaying) return null;

            SpriteRenderer body = ResolveBodyRenderer(caster);
            Vector2 size = body != null && body.sprite != null
                ? (Vector2)body.bounds.size
                : new Vector2(0.9f, 1.6f);
            Vector3 bodyOffset = body != null && body.sprite != null
                ? body.bounds.center - caster.position
                : new Vector3(0f, 0.8f, 0f);

            var go = new GameObject("SpellCastFlourishFX");
            go.transform.position = caster.position;

            var fx = go.AddComponent<SpellCastFlourishFX>();
            fx._owner = caster;
            fx._bodyOffset = bodyOffset;
            fx._handOffset = handWorld - caster.position;
            fx._direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector2.right;
            fx._bodySize = new Vector2(Mathf.Max(0.3f, size.x), Mathf.Max(0.5f, size.y));
            fx._palette = palette;
            fx._profile = profile;
            // A ward blooms out of the whole caster; a bolt is held in front of them. Which of
            // the two a family is decides where every mote converges and where the glow sits.
            fx._anchor = profile.HandAnchored ? fx._handOffset : bodyOffset;
            fx._bodyTint = SpriteTintStack.Attach(caster.gameObject);
            fx.BuildRig();
            return fx;
        }

        private static SpriteRenderer ResolveBodyRenderer(Transform owner)
        {
            var sr = owner.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in owner.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
        }

        // ── Construction ──────────────────────────────────────────────────────────────

        private void BuildRig()
        {
            ElementalSprites.EnsureAll();

            // Every piece a family switched off is simply never built. A renderer held at
            // alpha 0 for the whole cast still costs a draw call, and it has to be kept at
            // zero by every branch that touches it — a missed branch is a stray glow.
            if (_profile.Sigil != SigilMotion.None)
            {
                _sigilOuter = CreateSprite("SigilOuter", ElementalSprites.Ring, _palette.core,
                    70, SortingConfig.LAYER_FLOOR_DECALS);
                _sigilInner = CreateSprite("SigilInner", ElementalSprites.Ring, _palette.hotCore,
                    71, SortingConfig.LAYER_FLOOR_DECALS);
            }

            _aura = CreateSprite("Aura", ElementalSprites.Halo, _palette.halo, ORDER_AURA,
                SortingConfig.LAYER_VFX);
            _aura.transform.localScale = new Vector3(_bodySize.x * 3.2f, _bodySize.y * 2.1f, 1f);

            if (_profile.Lance != LanceAim.None)
                _lance = CreateSprite("Lance", ElementalSprites.Glow, _palette.core, ORDER_LANCE,
                    SortingConfig.LAYER_VFX);

            if (_profile.Burst != BurstOrigin.None)
                _burst = CreateSprite("Burst", ElementalSprites.Ring, _palette.core, ORDER_BURST,
                    SortingConfig.LAYER_VFX);

            _hand = CreateSprite("Hand", ElementalSprites.Glow, _palette.core, ORDER_HAND,
                SortingConfig.LAYER_VFX);
            _handHot = CreateSprite("HandCore", ElementalSprites.HotCore, _palette.hotCore,
                ORDER_HAND_HOT, SortingConfig.LAYER_VFX);

            BuildMotes();
            BuildLight();
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;
            try
            {
                _light = gameObject.AddComponent(lightType);
                var typeProperty = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4 — the documented trap.
                if (typeProperty != null)
                    typeProperty.SetValue(_light, System.Enum.ToObject(typeProperty.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _palette.lightOuter * 1.4f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
            }
            catch { _light = null; }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color,
            int order, string sortingLayer)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            // Additive throughout: on the alpha material the brightest pixel a glow can make
            // is its own colour, so light that is meant to wash the character out cannot blow
            // out. SharedAdditiveMaterial is SrcAlpha/One, so alpha still fades it.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            return sr;
        }

        private static Color WithAlpha(Color color, float alpha)
            => new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
    }
}
