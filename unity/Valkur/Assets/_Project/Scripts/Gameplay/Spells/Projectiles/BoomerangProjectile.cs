using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Runtime behaviour for the boomerang spell.
    /// Phase 1 (Outbound): bows out to one side of the aim line and arrives at maxRange back on
    /// it — cut short by an obstacle or, when it does not pass through, by the first victim.
    /// Phase 2 (Returning): bows back along the OTHER side and is caught in the caster's hand.
    /// The two legs together draw a closed loop, which is the whole reason the throw reads as a
    /// boomerang rather than as a bullet that reversed down its own line.
    ///
    /// <para>MOTION IS INTEGRATED, NOT DELEGATED. The flight used to be a
    /// <c>Rigidbody2D.velocity</c> written every <c>FixedUpdate</c>, which had three costs.
    /// The path is scripted, so handing it to the solver bought nothing; a second component on
    /// the same object could silently win the write (see <see cref="BoomerangExecutor"/>, which
    /// spawns the shared ball prefab and used to leave its <see cref="Projectile"/> aboard);
    /// and nothing outside Play Mode could measure the arc, so the spell shipped with no
    /// behavioural test at all. <see cref="Step"/> takes the delta as a parameter for the same
    /// reason <c>VortexFieldController.Tick</c> does — a model that reads
    /// <c>Time.deltaTime</c> itself cannot be measured from a test or from <c>execute_code</c>.</para>
    ///
    /// <para>DAMAGE IS RATE-LIMITED PER VICTIM. The overlap query used to run every
    /// <c>Update</c> with no memory of who it had already hit, so a victim inside the 0.75-unit
    /// circle took the full damage on every rendered frame — the throw's damage scaled with the
    /// player's framerate — and the return leg never stopped hitting, because only the outbound
    /// leg ever checked the phase.</para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class BoomerangProjectile : MonoBehaviour
    {
        internal enum Phase { Outbound, Returning }

        /// <summary>How close the blade has to get to the caster's pivot to be caught.</summary>
        private const float CatchRadius = 0.6f;

        /// <summary>
        /// The blade's PHYSICAL half-width, used only against walls.
        ///
        /// <para>Deliberately not <c>hitRadius</c>. That field answers a different question —
        /// how far from the blade a victim can still be hurt — and it is authored generously
        /// (0.75, a 1.5-unit circle) because a near miss on a moving target should still land.
        /// Reusing it as the obstacle probe made the boomerang sweep a corridor five times
        /// wider than every other projectile in the game (<see cref="Projectile"/> sweeps its
        /// 0.15 collider), so it caught on geometry nobody aimed at: measured over 24 headings
        /// from one spot in the shipped world, <b>16 of them turned the blade back early</b>,
        /// one after 2.66 units of a 10-unit throw. One number doing two jobs, again.</para>
        /// </summary>
        private const float ObstacleRadius = 0.18f;

        /// <summary>
        /// Where the bow is sampled for clearance, as a fraction along the outbound leg. The
        /// half-sine peaks at the middle, so that is where the blade is furthest off the aim
        /// line and the first place it can catch on something.
        /// </summary>
        private const float BowClearanceProbeAlong = 0.5f;

        /// <summary>Turn rate of the blade. Two full turns a second reads as thrown, not fired.</summary>
        private const float SpinDegreesPerSecond = 720f;

        /// <summary>
        /// How far each leg bows off its own straight line at its widest, as a fraction of THAT
        /// LEG's length.
        ///
        /// <para>Of the leg, not of the throw's range, and that is the difference between a
        /// spell that reads the same everywhere and one that does not. A leg cut short by a wall
        /// three units away would otherwise carry a bow sized for a ten-unit throw — a bulge
        /// wider than the run it decorates. Scaling with the leg makes every throw draw the same
        /// lens at whatever size it got.</para>
        ///
        /// <para>It replaced a clamp that measured the room to the side and narrowed the loop to
        /// fit. That protected the flight and destroyed the spell: measured from where the
        /// player actually stands in the shipped town, <b>17 of 24 headings came back with less
        /// than half the authored bow</b>, most under a tenth of it — a boomerang flying in a
        /// straight line, which is the thing this whole rebuild exists to stop being.</para>
        ///
        /// <para>THE PATH IS A LENS, NOT A LINE. A blade that flies straight out, stops, and
        /// comes back down its own line does not read as a boomerang — it reads as a bullet
        /// that bounced, and the return is invisible because it retraces pixels the eye has
        /// already filed. The outbound leg bows to one side and the return leg bows to the
        /// other, so the two legs are separate strokes on screen and the whole throw draws a
        /// closed loop.</para>
        ///
        /// <para>Both legs START and END on the aim line — the bow is a half-sine, zero at both
        /// ends — so the reach is still exactly <c>range</c> in exactly the direction the player
        /// aimed. A curve that bent the tip off-aim would be a boomerang nobody can throw at
        /// anything.</para>
        /// </summary>
        private const float ArcAmplitude = 0.38f;

        /// <summary>
        /// The path a lens of <see cref="ArcAmplitude"/> actually traces, over the straight-line
        /// distance it covers.
        ///
        /// <para>Both legs divide their progress rate by it, so <c>speed</c> means SPEED THROUGH
        /// THE WORLD rather than speed along the chord. Without that division the bow is free
        /// distance: measured, a throw authored at 20 u/s covered 24.84 units in 0.98 s — 25.3
        /// u/s on screen — and the field would have been one more number that does not mean what
        /// it says, which is the whole class of defect this spell was rebuilt out of. It also
        /// makes the round trip honestly <c>2 * range / speed * ArcPathFactor</c>, which is what
        /// the cooldown has to outlast.</para>
        ///
        /// <para>Measured, not derived: a 10-unit throw walks 25.87 units of path against a
        /// 20-unit round trip. The closed form of a half-sine's arc length is an elliptic
        /// integral, and this number is only ever a rate divisor and a bound.</para>
        /// </summary>
        internal const float ArcPathFactor = 1.3f;

        /// <summary>
        /// The same victim cannot be damaged again inside this window. A cooldown rather than a
        /// once-per-pass set because the turn happens ON a victim: clearing a set at the turn
        /// would hand whoever caused it a free second hit on the very next frame. Long enough
        /// that the out and back legs land one hit each at any sane speed.
        /// </summary>
        private const float PerVictimHitCooldown = 0.4f;

        /// <summary>
        /// Wall-clock ceiling, derived from the throw itself: three round trips, with a floor.
        /// A boomerang whose caster outruns it (or is teleported) would otherwise chase
        /// forever, and nothing else in the flight can end it.
        /// </summary>
        private const float LifetimeRoundTrips = 3f;
        private const float LifetimeFloorSeconds = 2f;

        // Reused query buffers — a throw runs one overlap and one sweep per frame and must not
        // allocate for either. Deliberately not readonly: the Domain-Reload static scanner only
        // recognises a whole-field assignment as a reset.
        private static Collider2D[] _overlapBuffer = new Collider2D[16];
        private static RaycastHit2D[] _sweepBuffer = new RaycastHit2D[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _overlapBuffer = new Collider2D[16];
            _sweepBuffer = new RaycastHit2D[8];
        }

        private Transform _caster;
        private Vector2 _origin;
        private Vector2 _direction;
        private float _speed;
        private float _returnSpeed;
        private float _damage;
        private float _maxRange;
        private float _hitRadius;
        private bool _passesThrough;
        private LayerMask _targetLayers;
        private Color _vfxColor;
        private SpellElement? _element;
        private StatusApplication[] _statusApplications;
        private string _impactPreset;

        private Phase _phase = Phase.Outbound;
        private Rigidbody2D _rb;
        private bool _expired;
        private float _age;
        private float _maxLifetime = LifetimeFloorSeconds;
        private float _spin;

        // The lens. `_right` is the side the outbound leg bows toward; the return bows the
        // other way. Progress is a 0..1 parameter along each leg's chord rather than a raw
        // position step, so both legs are laid out from their own endpoints and the return
        // lands EXACTLY in the hand instead of asymptotically near it.
        private Vector2 _aim;
        private Vector2 _right;
        private float _bowSign = 1f;
        private Vector2 _turnPoint;
        private float _outboundProgress;
        private float _returnProgress;
        private readonly Dictionary<int, float> _lastHitAt = new Dictionary<int, float>();

        /// <summary>Damage type consulted against the victim's Health.resistances on hit.</summary>
        public void SetElement(SpellElement? element) => _element = element;

        /// <summary>Status effects rolled against a victim on a successful hit.</summary>
        public void SetStatusApplications(StatusApplication[] applications) => _statusApplications = applications;

        /// <summary>Particle preset played where a victim is struck. Empty means none.</summary>
        public void SetImpactPreset(string preset) => _impactPreset = preset;

        /// <summary>Which leg of the throw is being flown. Internal so a test can assert the turn.</summary>
        internal Phase CurrentPhase => _phase;

        /// <summary>True once the blade has been caught, timed out, or lost its caster.</summary>
        internal bool IsExpired => _expired;

        /// <summary>True when the flight ended in the caster's hand rather than by timeout.</summary>
        internal bool WasCaught { get; private set; }

        /// <summary>Seconds of flight so far.</summary>
        internal float Age => _age;

        /// <summary>The furthest the blade is allowed from where it was thrown.</summary>
        internal float MaxRange => _maxRange;

        public void Initialize(Transform caster, Vector2 direction, float speed, float returnSpeed,
                               float damage, float maxRange, float hitRadius, bool passesThrough,
                               LayerMask targetLayers, Color vfxColor)
        {
            _caster      = caster;
            _direction   = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
            _speed       = speed > 0 ? speed : 8f;
            _returnSpeed = returnSpeed > 0 ? returnSpeed : _speed;
            _damage      = damage;
            _maxRange    = maxRange > 0 ? maxRange : 6f;
            _hitRadius   = hitRadius > 0 ? hitRadius : 0.25f;
            _passesThrough = passesThrough;
            _targetLayers = targetLayers;
            _vfxColor    = vfxColor;
            _origin      = transform.position;
            _phase       = Phase.Outbound;
            _age         = 0f;
            _lastHitAt.Clear();

            // The aim never changes; the travel heading does, every frame, because the path is
            // a curve. Clockwise perpendicular to the aim is the side the outbound leg bows
            // toward — fixed rather than random, because a player learns one shape and leads
            // with it.
            _aim = _direction;
            _right = new Vector2(_aim.y, -_aim.x);
            _bowSign = ChooseBowSide();
            _turnPoint = _origin;
            _outboundProgress = 0f;
            _returnProgress = 0f;

            // Out and back at the two authored speeds, times a generous factor. Derived rather
            // than constant so a slow, long throw is not cut short by its own guard.
            float roundTrip = _maxRange / Mathf.Max(0.01f, _speed) + _maxRange / Mathf.Max(0.01f, _returnSpeed);
            _maxLifetime = Mathf.Max(LifetimeFloorSeconds, roundTrip * LifetimeRoundTrips);

            _spin = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, _spin);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null)
            {
                // The path is written by Step; the solver must not add to it or damp it.
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.gravityScale = 0f;
                _rb.velocity = Vector2.zero;
                _rb.angularVelocity = 0f;
                _rb.freezeRotation = false;
            }
            _origin = transform.position;
        }

        private void Update() => Step(Time.deltaTime);

        /// <summary>
        /// Advance the flight by <paramref name="deltaTime"/> seconds: move, spin, and resolve
        /// whatever the blade now overlaps. Internal so an EditMode test can fly a whole throw
        /// deterministically, with no physics step and no frame clock.
        /// </summary>
        internal void Step(float deltaTime)
        {
            if (_expired || deltaTime <= 0f) return;

            _age += deltaTime;
            if (_age >= _maxLifetime) { Expire(caught: false); return; }

            SpinBy(deltaTime);

            Vector2 pos = transform.position;

            if (_phase == Phase.Outbound)
            {
                Vector2 tip = _origin + _aim * _maxRange;
                _outboundProgress += _speed * deltaTime / (_maxRange * ArcPathFactor);

                if (_outboundProgress >= 1f)
                {
                    // The far end of the throw: back on the aim line, exactly `range` out.
                    MoveTo(pos, tip);
                    BeginReturn(tip);
                    ResolveHits(tip);
                    return;
                }

                Vector2 next = Bow(_origin, tip, _outboundProgress, +1f);
                if (SweptIntoObstacle(pos, next, out Vector2 stopAt))
                {
                    MoveTo(pos, stopAt);
                    BeginReturn(stopAt);
                    ResolveHits(stopAt);
                    return;
                }

                MoveTo(pos, next);
                ResolveHits(next);
            }
            else
            {
                if (_caster == null) { Expire(caught: false); return; }

                Vector2 home = _caster.position;
                float span = Mathf.Max(0.01f, Vector2.Distance(_turnPoint, home));
                _returnProgress += _returnSpeed * deltaTime / (span * ArcPathFactor);

                // The bow closes to zero at both ends, so a finished return sits ON the hand
                // rather than near it. The catch is an arrival, not a fade-out at some radius.
                if (_returnProgress >= 1f) { MoveTo(pos, home); Expire(caught: true); return; }

                Vector2 next = Bow(_turnPoint, home, _returnProgress, -1f);
                MoveTo(pos, next);

                if (Vector2.Distance(next, home) <= CatchRadius) { Expire(caught: true); return; }
                ResolveHits(next);
            }
        }

        /// <summary>
        /// A point along one leg: the straight line from <paramref name="from"/> to
        /// <paramref name="to"/>, pushed sideways by a half-sine that is zero at both ends.
        /// <paramref name="side"/> is +1 for the outbound leg and -1 for the return, which is
        /// what turns an out-and-back into a closed loop.
        /// </summary>
        private Vector2 Bow(Vector2 from, Vector2 to, float progress01, float side)
        {
            Vector2 straight = Vector2.Lerp(from, to, progress01);
            // Sized off THIS leg, not off the throw's range. A leg cut short by a wall is a
            // short lens, not a full-width bulge on a two-unit run: the shape stays the same
            // proportion whatever happens to the flight, which is the only way the spell reads
            // the same everywhere.
            float bow = ArcAmplitude * Vector2.Distance(from, to) * Mathf.Sin(Mathf.PI * progress01);
            return straight + _right * (side * _bowSign * bow);
        }

        /// <summary>
        /// Which way the loop turns. Clockwise by default — one shape a player can learn and
        /// lead with — but flipped when that side is walled and the other is not.
        ///
        /// <para>A fixed side is a behaviour that changes with the heading for a reason the
        /// player cannot see: throw down a corridor with the wall on the bow side and the blade
        /// clips out after a couple of units, throw the same distance the other way and it flies
        /// the full arc. Sampling both sides once, at the widest point of the bow, makes the
        /// spell behave the same in both directions. Decided once at cast time rather than per
        /// frame: a side that could flip mid-flight is a blade that jinks.</para>
        /// </summary>
        private float ChooseBowSide()
        {
            Vector2 along = _origin + _aim * (_maxRange * BowClearanceProbeAlong);
            float reach = ArcAmplitude * _maxRange;
            int blocking = WorldCollisionLayers.BlockingMask();

            bool clockwiseBlocked = Physics2D.OverlapCircle(along + _right * reach, ObstacleRadius, blocking) != null;
            bool counterBlocked = Physics2D.OverlapCircle(along - _right * reach, ObstacleRadius, blocking) != null;

            return clockwiseBlocked && !counterBlocked ? -1f : 1f;
        }

        /// <summary>
        /// Move to <paramref name="next"/> and remember which way that was.
        ///
        /// <para><see cref="_direction"/> is the TRAVEL heading, which on a curve changes every
        /// frame — the obstacle sweep and the visual rig's trail both read it. It is deliberately
        /// not the same field as <see cref="_aim"/>: laying the outbound leg out from a heading
        /// that the curve itself keeps rewriting would make the throw spiral away instead of
        /// bowing back onto the line it was aimed down.</para>
        /// </summary>
        private void MoveTo(Vector2 pos, Vector2 next)
        {
            Vector2 travel = next - pos;
            if (travel.sqrMagnitude > 1e-8f) _direction = travel.normalized;
            ApplyPosition(next);
        }

        private void SpinBy(float deltaTime)
        {
            _spin += SpinDegreesPerSecond * deltaTime;
            transform.rotation = Quaternion.Euler(0f, 0f, _spin);
        }

        private void ApplyPosition(Vector2 pos)
        {
            transform.position = pos;
            if (_rb != null) _rb.position = pos;
        }

        private void BeginReturn(Vector2 turnPoint)
        {
            if (_phase == Phase.Returning) return;
            _phase = Phase.Returning;
            _turnPoint = turnPoint;
            _returnProgress = 0f;
        }

        /// <summary>
        /// True when the step would carry the blade into a wall, a building or a painted
        /// collision cell — the blocking set <see cref="Projectile"/> already answers to.
        /// Without it the blade flew through every wall in the world, which is the one thing a
        /// thrown object may never do.
        /// </summary>
        private bool SweptIntoObstacle(Vector2 from, Vector2 to, out Vector2 stopAt)
        {
            stopAt = from;
            Vector2 travel = to - from;
            float step = travel.magnitude;
            if (step <= 1e-5f) return false;
            Vector2 direction = travel / step;

            bool previousHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = false;
            int count = Physics2D.CircleCastNonAlloc(from, ObstacleRadius, direction, _sweepBuffer,
                                                     step, WorldCollisionLayers.BlockingMask());
            Physics2D.queriesHitTriggers = previousHitTriggers;

            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _sweepBuffer[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                // Physics2D.queriesStartInColliders is on project-wide, so a sweep that starts
                // already overlapping reports distance 0. A blade thrown from inside a doorway
                // must not turn round on its first frame.
                if (hit.distance <= Mathf.Epsilon) continue;
                if (hit.distance < best) { best = hit.distance; stopAt = from + direction * hit.distance; }
            }

            return best < float.PositiveInfinity;
        }

        private void ResolveHits(Vector2 pos)
        {
            if (_caster == null) { Expire(caught: false); return; }

            int count = Physics2D.OverlapCircleNonAlloc(pos, _hitRadius, _overlapBuffer, _targetLayers);
            bool hitSomething = false;

            for (int i = 0; i < count; i++)
            {
                var collider = _overlapBuffer[i];
                if (collider == null) continue;
                if (collider.transform == _caster || collider.transform.IsChildOf(_caster)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                int victimId = health.gameObject.GetInstanceID();
                if (_lastHitAt.TryGetValue(victimId, out float last) && _age - last < PerVictimHitCooldown)
                    continue;
                _lastHitAt[victimId] = _age;

                int dealt = Mathf.RoundToInt(_damage);
                GameObject casterGo = _caster.gameObject;
                health.TakeDamage(dealt, casterGo, _element);
                Valkur.Core.GameEvents.FireHitDealt(casterGo, health.gameObject, dealt);
                StatusApplicationFactory.ApplyAll(_statusApplications, health.gameObject, casterGo);
                SpawnHitFeedback(collider.transform.position);
                hitSomething = true;
            }

            if (hitSomething && !_passesThrough && _phase == Phase.Outbound)
                BeginReturn(pos);
        }

        /// <summary>
        /// The blade landing on someone. Spawned here rather than through
        /// <c>IProjectileVisual.OnImpact</c> because that seam is a one-shot — it exists so
        /// <see cref="Projectile"/> can announce the single impact that ends its flight, and a
        /// boomerang strikes several victims and keeps going.
        /// </summary>
        private void SpawnHitFeedback(Vector3 worldPos)
        {
            ElementalImpactFX.Spawn(worldPos, SpellElement.Boomerang);

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnImpact(worldPos, _vfxColor, 0.25f);
                if (!string.IsNullOrEmpty(_impactPreset))
                    VFXManager.Instance.SpawnParticlePreset(_impactPreset, worldPos);
            }

            PlayOneShot(BoomerangAudio.Impact(), worldPos);
        }

        private void Expire(bool caught)
        {
            if (_expired) return;
            _expired = true;
            WasCaught = caught;

            if (caught)
            {
                // The catch needs a pixel of its own. Without one the blade simply stops
                // existing at the hand, and "it came back" is a thing the player has to infer
                // from an absence. Smaller and shorter than a hit — an arrival, not a blow.
                PlayOneShot(BoomerangAudio.Catch(), transform.position);
                if (VFXManager.Instance != null)
                    VFXManager.Instance.SpawnImpact(transform.position, _vfxColor, 0.18f, 0.55f);
            }

            // Object.Destroy is deferred and Unity refuses it outside Play Mode with an error.
            // An EditMode test flies the whole throw, so the un-played branch has to answer too.
            if (Application.isPlaying) Destroy(gameObject);
            else                       DestroyImmediate(gameObject);
        }

        private static void PlayOneShot(AudioClip clip, Vector3 worldPos)
        {
            if (clip == null) return;
            var audio = Valkur.Core.ServiceLocator.Get<Valkur.Core.IAudioService>();
            if (audio != null) audio.PlaySFXAtPosition(clip, worldPos);
        }
    }
}
