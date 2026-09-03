using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A cone breath: sustained damage inside a wedge in front of the caster, for as long as
    /// the breath lasts.
    ///
    /// <para>The controller owns the HIT GEOMETRY and the lifecycle; <see cref="FlameConeFX"/>
    /// owns everything drawn. The split matters because the two used to be one class in which
    /// a <c>LineRenderer</c> drew a wire outline of a shape the damage query never agreed
    /// with — the rig is now asked for its own half-width at a distance
    /// (<see cref="FlameConeFX.HalfWidthAt"/>) and the damage test uses that same number, so
    /// the wedge on screen is the wedge that hurts.</para>
    ///
    /// <para>A TARGET IS TESTED AT ITS NEAREST POINT, not at its pivot. An entity's transform
    /// sits at its feet, so testing the pivot against a cone drawn at chest height makes a
    /// large enemy standing squarely in the fire immune whenever its origin falls a degree
    /// outside the arc.</para>
    /// </summary>
    public class ConeBreathController : MonoBehaviour, ISpellEffectDissipates
    {
        /// <summary>
        /// How often the breath refreshes its burn, in seconds. It used to reapply on EVERY
        /// damage tick — five times a second per target — and <c>StatusEffectManager.Apply</c>
        /// replaces rather than stacks, so each of those did a full remove + reapply and fired
        /// two events. The burn was never stronger for it; the churn was pure cost, and the
        /// tint layer it drives was being torn down and rebuilt ten times a second.
        /// </summary>
        private const float BURN_REFRESH_SECONDS = 0.6f;

        private const float BURN_DURATION = 2f;
        private const int BURN_DAMAGE = 3;

        /// <summary>
        /// Minimum gap between the little kicks a connecting breath sends to the camera, and
        /// the same cadence the per-target impact puffs run on. Spawning one impact per target
        /// per tick is thirty a second against six enemies, which is both a cost and a smear.
        /// </summary>
        private const float FEEDBACK_INTERVAL = 0.22f;

        /// <summary>
        /// Ceiling on targets considered per tick. The buffer is per instance rather than
        /// static: a static one would need a Domain-Reload reset hook, and with maxInstances 1
        /// there is at most a cone or two alive to own one.
        /// </summary>
        private const int MAX_TARGETS_PER_TICK = 32;

        private readonly Collider2D[] _hitBuffer = new Collider2D[MAX_TARGETS_PER_TICK];

        private float _remaining;
        private float _arc;
        private float _length;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private float _burnTimer;
        private float _feedbackTimer;
        private Vector2 _direction;
        private Transform _caster;
        private LayerMask _targetLayers;
        private string _element;
        private SpellElement? _damageElement;
        private Color _swatch;
        private FlameConeFX _fx;
        private bool _dissipating;

        // The cone follows the caster every frame, so it needs the spell's own origin settings
        // rather than the system defaults. Initialize keeps its loose-value signature; the
        // executor hands these over separately.
        private SpellCastAnchor _castAnchor = SpellCastAnchor.Hands;
        private float _castForwardOffset = ProjectileExecutor.CAST_FORWARD_OFFSET;

        /// <summary>Reach in world units. Read by tests, which cannot see the private field.</summary>
        public float Length { get { return _length; } }

        /// <summary>Full opening angle in degrees.</summary>
        public float ArcDegrees { get { return _arc; } }

        /// <summary>Adopt a spell's cast anchor, forward clearance and colour. Null keeps the defaults.</summary>
        public void SetCastOrigin(SpellDefinition spell)
        {
            if (spell == null) return;
            _castAnchor = spell.castAnchor;
            _castForwardOffset = ProjectileExecutor.ResolveCastForwardOffset(spell);
        }

        /// <summary>
        /// The colour the fire is drawn in. Authored through <c>particleColor</c>; an
        /// unauthored swatch (opaque white — the project-wide sentinel) falls back to the
        /// element's own palette, so a fire breath is orange without anyone having to say so
        /// and a designer who picks a colour gets it.
        /// </summary>
        public void SetSwatch(SpellDefinition spell)
        {
            if (spell != null && !KiPalette.IsUnauthored(spell.particleColor))
            {
                _swatch = spell.particleColor;
                return;
            }

            var element = ProjectileExecutor.ResolveElement(spell);
            _swatch = element.HasValue
                ? ElementPalette.For(element.Value).core
                : new Color(1f, 0.52f, 0.14f, 1f);
        }

        public void Initialize(float duration, float arc, float length, int damagePerTick,
            float tickPeriod, Vector2 direction, Transform caster, LayerMask targetLayers, string element,
            SpellElement? damageElement = null)
        {
            _remaining = duration;
            _arc = arc;
            _length = length;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _burnTimer = 0f;
            _feedbackTimer = 0f;
            _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            _caster = caster;
            _targetLayers = targetLayers;
            _element = element;
            _damageElement = damageElement;
            if (_swatch.a <= 0f) SetSwatch(null);

            transform.position = ResolveOrigin();
            _fx = FlameConeFX.Attach(transform, _direction, _length, _arc, _swatch);

            PlayIgnitionAudio();

            // No cast beat is fired here on purpose. CameraFeelDirector already decides whether
            // a cast is heavy — from prepareDuration, cooldown and manaCost against the profile
            // — and fires CastHeavy as a RECOIL, away from the facing. A second one from the
            // controller would double the shake and push it the other way, which is the
            // two-owners-for-one-value bug this project keeps paying for. What the director
            // cannot know is that a SUSTAINED cone connected, and that is the only beat below.
        }

        /// <summary>
        /// A breath that is evicted, zoned out of, or outlived by its caster gets the same tail
        /// it would have had on its own clock — the registry destroys the object outright
        /// otherwise, and the hard cut is what a player actually sees whenever
        /// <c>maxInstances</c> is 1 and the cooldown is shorter than the duration.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled || _dissipating || _fx == null) return false;

            _dissipating = true;
            _remaining = Mathf.Min(_remaining, Mathf.Max(0.01f, seconds));
            _fx.StopEmitting();
            return true;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f || _caster == null)
            {
                Destroy(gameObject);
                return;
            }

            // Follow the same moving muzzle point Fireball uses, so visuals and hit geometry
            // stay aligned while the caster walks.
            transform.position = ResolveOrigin();
            if (_fx != null) _fx.Tick(Time.deltaTime, _remaining);

            if (_dissipating) return;

            _feedbackTimer -= Time.deltaTime;
            _burnTimer -= Time.deltaTime;
            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                DamageTick();
                _tickTimer = _tickPeriod;
            }
        }

        private Vector3 ResolveOrigin()
        {
            return ProjectileExecutor.ResolveCastStart(_caster, _direction, _castAnchor, _castForwardOffset);
        }

        private void PlayIgnitionAudio()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;

            // Probed, not called blind. An explicit id that fails to resolve is reported as a
            // warning by AudioManager, and neither breath id has ever existed in the catalog —
            // so every first cast of a session pushed a warning into a console the project
            // requires to be clean. HasSfx is the gate the interface documents for exactly this.
            string id = _element == "fire" ? "spell_flame_breath_loop" : "spell_frost_breath_loop";
            if (audio.HasSfx(id)) audio.PlaySfxById(id);
        }

        private void DamageTick()
        {
            if (_caster == null) return;

            Vector2 origin = transform.position;
            bool refreshBurn = _element == "fire" && _burnTimer <= 0f;
            bool showImpacts = _feedbackTimer <= 0f;
            bool connected = false;

            // NonAlloc: this runs five times a second for as long as the breath lasts, and
            // OverlapCircleAll hands back a fresh array every one of them.
            int count = Physics2D.OverlapCircleNonAlloc(origin, _length, _hitBuffer, _targetLayers);
            for (int i = 0; i < count; i++)
            {
                var hit = _hitBuffer[i];
                if (hit == null || hit.gameObject == _caster.gameObject) continue;
                var health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 point;
                if (!InsideCone(hit, origin, out point)) continue;

                connected = true;
                health.TakeDotDamage(_damagePerTick, _caster.gameObject, _damageElement);
                GameEvents.FireHitDealt(_caster.gameObject, hit.gameObject, _damagePerTick);

                if (refreshBurn)
                {
                    // Off `health` (the entity root), not off the collider — the same fix
                    // PuddleController.DamageTick carries.
                    var statusMgr = health.GetComponent<StatusEffectManager>();
                    if (statusMgr != null)
                        statusMgr.Apply(new BurnEffect(BURN_DURATION, BURN_DAMAGE, applier: _caster.gameObject));
                }

                if (showImpacts) SpawnImpact(point);
            }

            if (refreshBurn && connected) _burnTimer = BURN_REFRESH_SECONDS;

            if (connected && _feedbackTimer <= 0f)
            {
                _feedbackTimer = FEEDBACK_INTERVAL;
                CameraFeel.Cue(CameraFeelCue.ImpactLight, _direction, 0.45f);
            }
        }

        /// <summary>
        /// True when <paramref name="hit"/> reaches into the wedge, with the nearest point of
        /// contact written to <paramref name="point"/>. Both the reach and the half-width come
        /// from the rig, so the test is against the shape actually on screen.
        /// </summary>
        private bool InsideCone(Collider2D hit, Vector2 origin, out Vector2 point)
        {
            point = hit.ClosestPoint(origin);
            Vector2 offset = point - origin;

            float along = Vector2.Dot(offset, _direction);
            if (along < 0f || along > _length) return false;

            float across = Mathf.Abs(offset.x * -_direction.y + offset.y * _direction.x);
            float halfWidth = _fx != null
                ? _fx.HalfWidthAt(along)
                : Mathf.Tan(_arc * 0.5f * Mathf.Deg2Rad) * along;

            return across <= halfWidth;
        }

        private void SpawnImpact(Vector2 point)
        {
            if (VFXManager.Instance == null) return;
            // Where the fire actually LANDED. The old rig puffed at a fixed 0.6 of the reach
            // whether or not anything was there, so the one moment worth reading — a hit — was
            // drawn in the same place as a miss.
            VFXManager.Instance.SpawnImpact(point, _swatch, 0.18f, _length * 0.22f);
        }

        private void OnDestroy()
        {
            if (_fx != null) _fx.Dispose();
        }
    }
}
