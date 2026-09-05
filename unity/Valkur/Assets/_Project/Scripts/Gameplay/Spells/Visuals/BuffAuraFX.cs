using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The look of a sustained self-buff. One rig, five silhouettes, chosen by
    /// <see cref="BuffAuraProfile"/> from what the spell actually does.
    ///
    /// <para>WHY IT IS QUIET. A buff lasts eight to fifteen seconds, and CLAUDE.md's L4
    /// records what happens to an effect made only of continuous motion: after about a second
    /// the eye files it as one texture. The answer for a BURST is a busy event layer; the
    /// answer for a STATE is the opposite — one mote every 0.4 to 0.8 s, so the effect stays
    /// legible as "something is on me" without competing with the fight for attention.</para>
    ///
    /// <para>WHY THE COMPONENT SITS ON THE CASTER AND THE PICTURE DOES NOT. The rig has to
    /// FOLLOW rather than parent: parenting inherits the entity scale, which would scale a
    /// <c>Light2D</c> radius with it — the trap that once rendered the vortex's light at 367
    /// world units. But the previous version created the rig as a ROOT object and then looked
    /// for an existing one with <c>owner.GetComponentInChildren&lt;BuffAuraFX&gt;()</c>, which
    /// could never hit: two live buffs meant two rigs, doubled additive alpha, and both writing
    /// the single shared <see cref="TintLayer.Buff"/>. The behaviour now lives on the caster,
    /// where <c>GetComponent</c> finds it, and drives a separate unparented root. Deduplication
    /// is structural rather than hopeful.</para>
    ///
    /// <para>ONE BODY, ONE PICTURE. A second buff cast over a live one REPLACES the rig rather
    /// than stacking with it. The stat layers still compose correctly — that is
    /// <c>TimedBuffSource</c>'s job and it keys by buff — but two silhouettes at once would
    /// sum their additive layers past the ~3 ceiling and fight over the one tint layer, which
    /// is the defect above with extra steps.</para>
    ///
    /// <para>WHY THE LAST 1.5 SECONDS ARE DIFFERENT. A buff that simply stops is a buff the
    /// player cannot plan around. Every silhouette expresses the warning in its own material —
    /// the shell flickers and its rime retreats, bark withers back down the body, the halo's
    /// ring speeds up and contracts, the shout's rim cools out — but they all read the same
    /// <c>warn</c> ramp, so the beat is one decision rather than five.</para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed partial class BuffAuraFX : MonoBehaviour
    {
        /// <summary>Seconds of visible warning before the buff ends.</summary>
        private const float WARN_SECONDS = 1.5f;

        /// <summary>How flat a circle drawn on the ground plane is. Shared with the vortex and the cone.</summary>
        private const float GROUND_SQUASH = 0.34f;

        /// <summary>
        /// <c>ElementalSprites.Ring</c>'s bright band peaks at normalized radius 0.78, so a
        /// world radius becomes a scale by dividing by this. Getting it wrong is invisible in
        /// code and brutal on screen — it is what left the arcane flame's only hard contour
        /// 40 % inside the circle that actually hurt.
        /// </summary>
        private const float RING_BAND_RADIUS = 0.39f;

        /// <summary>Ring revolutions per second at rest, and at the end of the warning.</summary>
        private const float SPIN_CALM = 0.35f;
        private const float SPIN_WARN = 2.4f;

        // Orders on LAYER_VFX. Small, because SortingConfig.Z_SKY is a Z depth and passing it
        // as a sorting order is what drew every lightning bolt under the wall tops.
        private const int ORDER_GROUND = 38;
        private const int ORDER_RIM = 41;
        private const int ORDER_MOTE = 44;
        private const int ORDER_COLUMN = 46;

        // Offsets from the CASTER's own sorting order, for the two silhouettes whose pieces
        // have to enclose the body. Anything negative is behind them.
        private const int ORDER_BEHIND_CASTER = -3;
        private const int ORDER_INFRONT_CASTER = 3;

        private BuffAuraProfile _profile;
        private bool _built;

        private Transform _owner;
        private SpriteRenderer _bodyRenderer;
        private int _lastBodyOrder = int.MinValue;

        private Vector3 _centerOffset;
        private Vector2 _size;
        private float _duration;
        private float _age;

        /// <summary>The unparented picture. Followed, never parented — see the class doc.</summary>
        private Transform _root;

        private SpriteTintStack _bodyTint;
        private Component _light;

        // Shared layers. Which of these exist is the profile's decision; a silhouette that
        // switches a piece off never builds it, the way CastFlourishProfile's families do.
        private Transform _groundPlane;
        private SpriteRenderer _groundRing;
        private SpriteRenderer _rim;
        private float _spin;

        private SpriteRenderer[] _motes;
        private float[] _moteAge;
        private Vector3[] _moteDrift;
        private int _nextMote;
        private float _moteTimer;
        private Color _moteColor;

        /// <summary>
        /// A silhouette's momentary multiplier on the shared body tint, written by its own tick
        /// and consumed by <c>TickBodyTint</c>. It exists so a punch (the shout's warm flash)
        /// and a state (the rime, the bark) can share ONE <see cref="TintLayer"/> — the body's
        /// colour has exactly one owner, and a second layer per silhouette would be four
        /// systems writing what nine used to.
        /// </summary>
        private float _tintBoost = 1f;

        /// <summary>
        /// Build or refresh the rig for <paramref name="spell"/> on <paramref name="owner"/>.
        /// Refused outside Play Mode: the rig destroys itself from Update, which never runs
        /// there, so building one would leave a permanent cluster in the scene rather than a
        /// timed effect. Same guard <c>WeaponSwapFlashFX</c> uses.
        /// </summary>
        public static void Attach(Transform owner, SpellDefinition spell)
        {
            if (owner == null || spell == null || spell.duration <= 0f) return;
            if (!Application.isPlaying) return;

            var existing = owner.GetComponent<BuffAuraFX>();
            if (existing == null) existing = owner.gameObject.AddComponent<BuffAuraFX>();
            existing.Restart(spell);
        }

        /// <summary>
        /// Re-resolve and rebuild only when the SHAPE changed; a recast of the same buff keeps
        /// its picture and restarts its clock. Same contract as
        /// <c>SpellProjectileVisual.Configure</c>, and for the same reason: tearing down and
        /// rebuilding an identical rig makes a refresh look like an interruption.
        /// </summary>
        private void Restart(SpellDefinition spell)
        {
            var next = BuffAuraProfile.Resolve(spell);
            bool reshape = !_built || next.Silhouette != _profile.Silhouette;
            _profile = next;

            _duration = spell.duration;
            _age = 0f;
            _expired = false;
            enabled = true;   // an expired rig parks itself disabled; see Expire

            if (reshape)
            {
                ResolveOwner();
                Rebuild();
            }

            ReplayOnset();
        }

        /// <summary>
        /// Re-arm whatever a silhouette fires ONCE per cast. Separate from
        /// <see cref="Rebuild"/> because a recast of the same buff deliberately keeps its rig,
        /// and a shout that made no noise the second time would be a spell that visibly
        /// stopped working. Everything driven off <c>_age</c> — the shell assembling, the
        /// column descending, bark climbing — replays for free because the clock was reset.
        /// </summary>
        private void ReplayOnset()
        {
            if (_profile.Silhouette == BuffSilhouette.Fervor) ReplayFervorOnset();
        }

        private void ResolveOwner()
        {
            _owner = transform;
            _bodyTint = SpriteTintStack.Attach(gameObject);
            _bodyRenderer = SpriteTintStack.ResolveBodyRenderer(gameObject);

            bool measurable = _bodyRenderer != null && _bodyRenderer.sprite != null;
            Vector2 size = measurable ? (Vector2)_bodyRenderer.bounds.size : new Vector2(0.9f, 1.6f);
            _centerOffset = measurable
                ? _bodyRenderer.bounds.center - _owner.position
                : new Vector3(0f, 0.8f, 0f);

            // A floor under both axes: an entity caught mid-frame with a degenerate sprite
            // would otherwise size every piece of the rig to nothing, silently.
            _size = new Vector2(Mathf.Max(0.3f, size.x), Mathf.Max(0.5f, size.y));
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void Rebuild()
        {
            TearDownRig();

            var go = new GameObject("BuffAuraFX");
            go.transform.position = _owner.position + _centerOffset;
            _root = go.transform;

            if (_profile.GroundRingRadius > 0f) BuildGroundPlane();
            if (_profile.MotePool > 0) BuildMotes();

            switch (_profile.Silhouette)
            {
                case BuffSilhouette.Shell: BuildShell(); break;
                case BuffSilhouette.Growth: BuildGrowth(); break;
                case BuffSilhouette.Radiance: BuildRadiance(); break;
                case BuffSilhouette.Fervor: BuildFervor(); break;
                default: BuildNeutralAura(); break;
            }

            if (_profile.HasLight) BuildLight();

            _lastBodyOrder = int.MinValue;   // force one rebase on the first frame
            _built = true;
        }

        /// <summary>
        /// The ground circle lives under ONE squash parent with the rotation on its CHILD,
        /// never a squash per item: a ring squashed on its own axis is foreshortened in length
        /// without being turned, and slides across the floor instead of lying on it.
        /// </summary>
        private void BuildGroundPlane()
        {
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(_root, false);
            plane.transform.localPosition = new Vector3(0f, -_size.y * 0.42f, 0f);
            plane.transform.localScale = new Vector3(1f, GROUND_SQUASH, 1f);
            _groundPlane = plane.transform;

            _groundRing = MakeSprite(_groundPlane, "GroundRing", ElementalSprites.Ring,
                                     _profile.Palette.core, SortingConfig.LAYER_VFX, ORDER_GROUND, true);
            SetRingRadius(_groundRing, _profile.GroundRingRadius);
        }

        private void BuildMotes()
        {
            _motes = new SpriteRenderer[_profile.MotePool];
            _moteAge = new float[_profile.MotePool];
            _moteDrift = new Vector3[_profile.MotePool];
            _moteColor = MoteColour();
            Sprite sprite = MoteSprite();

            for (int i = 0; i < _profile.MotePool; i++)
            {
                _motes[i] = MakeSprite(_root, "Mote" + i, sprite, _moteColor,
                                       SortingConfig.LAYER_VFX, ORDER_MOTE, MotesAreAdditive());
                _motes[i].transform.localScale = Vector3.one * _profile.MoteSize;
                _moteAge[i] = _profile.MoteLife;   // start spent, so none pop on frame one
            }
        }

        /// <summary>The old rig, kept verbatim as the fallback for a buff that says nothing.</summary>
        private void BuildNeutralAura()
        {
            _rim = MakeSprite(_root, "Rim", ElementalSprites.Glow, _profile.Palette.hotCore,
                              SortingConfig.LAYER_VFX, ORDER_RIM, true);
            _rim.transform.localScale = new Vector3(_size.x * 1.5f, _size.y * 1.25f, 1f);
        }

        private void BuildLight()
        {
            try
            {
                var lightType = ElementalProjectileVisual.GetLight2DType();
                if (lightType == null) return;

                var go = new GameObject("BuffLight");
                go.transform.SetParent(_root, false);
                _light = go.AddComponent(lightType);

                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. The wrong value here is what
                // left the whole day/night cycle unlit for months.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _profile.Palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _profile.LightRadius);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.1f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.9f);
            }
            catch { _light = null; }
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        private SpriteRenderer MakeSprite(Transform parent, string objectName, Sprite sprite,
                                          Color color, string sortingLayer, int order, bool additive)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            // Additive: alpha is COVERAGE and colour is BRIGHTNESS. The unlit material is for
            // the one opaque layer a rig is allowed — matter rather than light.
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(sortingLayer);
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Scale a Ring sprite so its bright band lands exactly on <paramref name="radius"/>.</summary>
        private static void SetRingRadius(SpriteRenderer ring, float radius)
        {
            if (ring == null) return;
            float scale = radius / RING_BAND_RADIUS;
            ring.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void TearDownRig()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _groundPlane = null;
            _groundRing = null;
            _rim = null;
            _light = null;
            _motes = null;
            _moteAge = null;
            _moteDrift = null;
            _nextMote = 0;
            _moteTimer = 0f;
            _spin = 0f;
            _tintBoost = 1f;
            ClearShellState();
            ClearGrowthState();
            ClearRadianceState();
            ClearFervorState();
        }

        /// <summary>
        /// Clearing the tint HERE rather than in Update is what makes the rig safe on the five
        /// exit paths a persistent effect has: its own timer, a zone change, the caster dying,
        /// scene unload, and being replaced by a recast. Only OnDestroy is on all of them, so a
        /// body left tinted by any of the other four would stay tinted for the rest of the run.
        /// The rig root goes with it: it is unparented, so nothing else would ever collect it.
        /// </summary>
        private void OnDestroy()
        {
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Buff);
            if (_root != null) Destroy(_root.gameObject);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
