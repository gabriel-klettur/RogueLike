using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The first two beats of a leap: the push-off, and the part a top-down projection cannot
    /// draw — being in the air.
    ///
    /// <para>THE SHADOW IS THE WHOLE ILLUSION. A camera looking straight down has no way to
    /// show height: a character two metres up occupies exactly the same pixels as one standing
    /// still. What the eye reads instead is the gap between a body and its shadow, and the
    /// shadow SHRINKING as that gap opens. Drop the shadow and there is no jump at all — every
    /// beat that follows becomes decoration on a spell that never left the ground, which is
    /// what makes this the one layer in the rig that is not optional.</para>
    ///
    /// <para>THE BODY IS NOT MOVED BY THIS RIG. <c>DashExecutor</c> teleports it in one
    /// physics step, exactly as the ordinary dash has always done, because a caster whose
    /// position is driven over several frames is a caster fighting <c>PlayerController</c> for
    /// its own <c>Rigidbody2D</c>. So the real renderer is hidden through
    /// <see cref="SpriteTintStack"/> and a GHOST copy of it flies the arc — which also means
    /// nothing here writes the entity's transform, and the player's own sprite lives on the
    /// character's ROOT GameObject, where a transform write would move the collider with it.
    /// The cost is that the hitbox arrives 0.35 s before the picture; that is the same trade
    /// the dash's afterimages already make.</para>
    ///
    /// <para>The dark shadow is the rig's ONE opaque layer (L3) and the only thing here not on
    /// <c>LAYER_VFX</c> — a shadow drawn on the VFX layer paints over the character's legs,
    /// which is not a shadow. It goes on FloorDecals, above the ground and below entities.</para>
    /// </summary>
    internal sealed partial class LeapFlightFX : MonoBehaviour
    {
        /// <summary>Seconds the dust ring lingers after the body has landed.</summary>
        private const float DUST_TAIL = 0.34f;

        /// <summary>Height of the arc, before the distance term.</summary>
        private const float ARC_BASE = 0.55f;
        private const float ARC_PER_UNIT = 0.20f;
        private const float ARC_CEILING = 2.0f;

        /// <summary>How long the sprite stays compressed at each end, in seconds.</summary>
        private const float SQUASH_SECONDS = 0.06f;
        private const float SQUASH_Y = 0.80f;
        private const float SQUASH_X = 1.16f;

        private const int DUST_MOTE_COUNT = 9;
        private const int ORDER_DUST = 41;
        private const int ORDER_MOTE = 44;
        private const int ORDER_SHADOW = 220;   // FloorDecals: above painted decals, below entities

        private Transform _caster;
        private SpriteRenderer _body;
        private SpriteTintStack _bodyTint;
        private Color _ghostTint;

        private Vector3 _from;
        private Vector3 _to;
        private Vector3 _feet;
        private float _duration;
        private float _arcHeight;
        private float _age;
        private bool _landed;
        private System.Action<Vector2> _onLanded;

        private RootPalette _palette;
        private SpriteRenderer _ghost;
        private Vector3 _ghostRestScale;
        private SpriteRenderer _shadow;
        private float _shadowRestWidth;
        private SpriteRenderer _dustRing;
        private SpriteRenderer[] _motes;
        private Vector3[] _moteDrift;

        /// <summary>
        /// Fly the jump from <paramref name="from"/> to <paramref name="to"/> and report the
        /// landing. Refused outside Play Mode: the sequence advances from Update, and a rig
        /// built there would hide the caster's body permanently.
        /// </summary>
        public static void Play(Transform caster, Vector3 from, Vector3 to, SpriteRenderer body,
                                float duration, SpellDefinition spell,
                                System.Action<Vector2> onLanded)
        {
            if (caster == null || !Application.isPlaying)
            {
                onLanded?.Invoke(to);
                return;
            }

            var go = new GameObject("LeapFlightFX");
            go.transform.position = from;

            var fx = go.AddComponent<LeapFlightFX>();
            fx._caster = caster;
            fx._body = body;
            fx._from = from;
            fx._to = to;
            fx._feet = DashStreakFX.FeetOffset(caster, body);
            fx._duration = Mathf.Max(0.08f, duration);
            fx._onLanded = onLanded;
            // RootPalette rather than ElementPalette: what a slam throws up is EARTH, and this
            // is the ramp that derives soil, bark, leaf and sap from the one authored swatch.
            // leap_slam authors a dust tan, which is exactly what it is for.
            fx._palette = RootPalette.From(spell != null ? spell.particleColor : Color.white);

            float distance = Vector3.Distance(from, to);
            fx._arcHeight = Mathf.Min(ARC_CEILING, ARC_BASE + ARC_PER_UNIT * distance);

            fx.Build();
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void Build()
        {
            ElementalSprites.EnsureAll();

            BuildGhost();
            BuildShadow();
            BuildLaunchDust();

            Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.DashLaunch, (_to - _from).normalized);
        }

        /// <summary>
        /// The airborne copy. It re-reads the real renderer every frame, so the character goes
        /// on animating while it is in the air instead of freezing on the take-off pose.
        /// </summary>
        private void BuildGhost()
        {
            if (_body == null || _body.sprite == null) return;

            // Captured BEFORE the hide layer goes on, or the ghost inherits alpha 0.
            _ghostTint = _body.color;

            var go = new GameObject("LeapGhost");
            // Parented so a rig torn down mid-jump cannot leave a second copy of the
            // character standing in the world; its position is written in world space anyway.
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = _from;
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sprite = _body.sprite;
            // The character's own material, so an airborne body is lit by the day/night cycle
            // exactly as the standing one is.
            _ghost.sharedMaterial = _body.sharedMaterial;
            _ghost.sortingLayerID = _body.sortingLayerID;
            _ghost.sortingOrder = _body.sortingOrder;
            _ghost.color = _ghostTint;
            _ghostRestScale = _body.transform.lossyScale;
            go.transform.localScale = _ghostRestScale;

            _bodyTint = SpriteTintStack.Attach(_body.gameObject);
            // TintLayer.Teleport: a leaping character is displaced, and the layer is otherwise
            // free — the transporter rig is the only other owner and cannot be running at the
            // same time as a dash. Cleared in OnDestroy whatever happens.
            _bodyTint?.Set(TintLayer.Teleport, new Color(1f, 1f, 1f, 0f));
        }

        private void BuildShadow()
        {
            var go = new GameObject("LeapShadow");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = _from + _feet;

            _shadow = go.AddComponent<SpriteRenderer>();
            _shadow.sprite = ElementalSprites.Glow;
            _shadow.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            _shadow.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            _shadow.sortingOrder = ORDER_SHADOW;
            // Dark, not faint: on an OPAQUE surface a shadow is authored by darkening the
            // colour, and dropping the alpha instead would make it vanish over dark ground.
            _shadow.color = new Color(0.04f, 0.04f, 0.06f, 0.55f);

            _shadowRestWidth = _body != null && _body.sprite != null
                ? Mathf.Max(0.35f, _body.bounds.size.x * 0.85f)
                : 0.7f;
            go.transform.localScale = new Vector3(_shadowRestWidth, _shadowRestWidth * 0.42f, 1f);
        }

        private void BuildLaunchDust()
        {
            _dustRing = MakeAdditive("LaunchDust", ElementalSprites.Ring, _palette.Leaf, ORDER_DUST);
            _dustRing.transform.position = _from + _feet;

            _motes = new SpriteRenderer[DUST_MOTE_COUNT];
            _moteDrift = new Vector3[DUST_MOTE_COUNT];
            for (int i = 0; i < DUST_MOTE_COUNT; i++)
            {
                var sr = MakeAdditive($"LaunchMote{i}", ElementalSprites.Wisp, _palette.Bark, ORDER_MOTE);
                sr.transform.position = _from + _feet
                                      + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.1f, 0.2f), 0f);
                sr.transform.localScale = Vector3.one * Random.Range(0.16f, 0.34f);
                float angle = Random.Range(0f, Mathf.PI * 2f);
                _moteDrift[i] = new Vector3(Mathf.Cos(angle) * Random.Range(0.6f, 1.9f),
                                            Mathf.Abs(Mathf.Sin(angle)) * Random.Range(0.3f, 1.1f), 0f);
                _motes[i] = sr;
            }
        }

        private SpriteRenderer MakeAdditive(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, worldPositionStays: true);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            sr.color = WithAlpha(color, 0f);
            return sr;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
