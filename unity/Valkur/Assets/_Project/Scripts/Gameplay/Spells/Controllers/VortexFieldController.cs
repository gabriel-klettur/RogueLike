using UnityEngine;
using Valkur.Core;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A vortex field: a tornado standing in the world that drags bodies into it or shoves
    /// them out of it.
    ///
    /// <para>THE FORCE SPIRALS, it does not vacuum along a straight line. A purely radial pull
    /// slings a body through the centre and out the far side, which reads as a bug; adding a
    /// tangential term makes it orbit inward instead, and that is the motion the funnel over
    /// it is already drawing.</para>
    ///
    /// <para>THE SPEED IS CLAMPED because nothing else bounds it. Every NPC body in this
    /// project ships <c>mass 1, drag 0</c>, so <c>AddForce</c> integrates without limit — over
    /// a two-second pull the old field accumulated tens of units per second and fired enemies
    /// off the map. Drag on the bodies themselves is not an option: it would slow their own
    /// walking everywhere else in the game.</para>
    /// </summary>
    public class VortexFieldController : MonoBehaviour
    {
        /// <summary>The tail of the effect, spent being torn apart rather than dimmed.</summary>
        private const float DISSIPATE_SECONDS = 1.20f;

        // ── drift ────────────────────────────────────────────────────────────────────
        // A tornado TRACKS. Standing perfectly still for eight seconds is the single thing
        // that gives away a spinning decal, and the longer the field lives the more it gives
        // away — at two seconds nobody noticed.

        /// <summary>Ground speed, world units per second. Slow enough to be walked away from.</summary>
        private const float DRIFT_SPEED = 1.15f;

        /// <summary>Degrees per second the heading may wander. It TURNS, it does not jitter:
        /// the heading is integrated from smooth noise, so the track is a curve and not a
        /// random walk, which is the difference between weather and a fly.</summary>
        private const float DRIFT_TURN_DEGREES = 42f;

        /// <summary>How far from where it was cast the funnel may roam. Without a leash an
        /// eight-second drift covers nine units and the spell simply leaves.</summary>
        private const float DRIFT_LEASH = 4.5f;

        /// <summary>Fraction of the leash at which it starts being steered home, so it curves
        /// back instead of hitting an invisible wall.</summary>
        private const float DRIFT_LEASH_SOFT = 0.65f;

        private const float DRIFT_HOMING = 5.5f;

        // Radial and tangential weights. Pull is nearly an even mix, which is what turns the
        // approach into a spiral; push is mostly outward, because a shove that curved would
        // read as a second, unrelated force.
        private const float PULL_INWARD = 0.86f;
        private const float PULL_SWIRL = 0.51f;
        private const float PUSH_OUTWARD = 0.94f;
        private const float PUSH_SWIRL = 0.34f;

        private const float MAX_DRAGGED_SPEED = 5.0f;
        private const float MAX_SHOVED_SPEED = 9.0f;

        /// <summary>
        /// How much of the force still acts at the very rim. A falloff that reaches 0 there
        /// means the vortex cannot GRAB anything — measured, a body parked at 95% of the radius
        /// kept 0.05 of the force and drifted half a unit in the whole two seconds, which reads
        /// as the spell having missed. The edge is precisely where it has to bite.
        /// </summary>
        private const float RIM_GRIP = 0.45f;

        /// <summary>
        /// Velocity the field bleeds off per second, as turbulent air would. Without it the
        /// tangential term hands the body orbital momentum that nothing removes, so a PULL
        /// captures an enemy into a stable orbit and never actually gathers it — measured, a
        /// body released at the rim settled at 2.0 units and circled there for the whole cast.
        /// It also stops a shoved body coasting away forever once the vortex is gone.
        /// </summary>
        private const float FIELD_DAMPING = 2.6f;

        /// <summary>Inside this fraction of the radius a body is in the eye: the radial term is
        /// dropped, or the direction flips every frame as it crosses the centre and the body
        /// buzzes in place.</summary>
        private const float EYE_FRACTION = 0.14f;

        /// <summary>The radius a touchdown at full camera intensity corresponds to. Both shipped
        /// vortices sit just under it, so they land hard without maxing the shake.</summary>
        private const float TOUCHDOWN_REFERENCE_RADIUS = 4.5f;

        private float _duration;
        private float _remaining;
        private float _radius;
        private float _force;
        private bool _isPull;
        private Transform _followTarget;
        private LayerMask _targetLayers;

        private VortexFunnelFX _funnel;

        private Vector2 _driftOrigin;
        private float _driftHeadingDegrees;
        private float _driftSeed;
        private Vector2 _lastPosition;

        public void Initialize(float duration, float radius, float force, bool isPull,
            Transform followTarget, LayerMask targetLayers, Color swatch)
        {
            _duration = Mathf.Max(0.1f, duration);
            _remaining = _duration;
            _radius = Mathf.Max(0.4f, radius);
            _force = force;
            _isPull = isPull;
            _followTarget = followTarget;
            _targetLayers = targetLayers;

            _driftOrigin = transform.position;
            _lastPosition = transform.position;
            _driftHeadingDegrees = Random.Range(0f, 360f);
            _driftSeed = Random.Range(0f, 100f);

            _funnel = VortexFunnelFX.Attach(transform, _radius, isPull, swatch);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(isPull ? "spell_vortex_pull" : "spell_vortex_push");

            // A vortex TOUCHES DOWN. Without a beat on the frame it arrives, the funnel simply
            // starts existing, and nothing on screen says the world was struck. Scaled by the
            // spell's own radius so a small vortex does not hit like a large one. Kick and
            // shake only — CLAUDE.md: there is no seam-legal zoom punch in a 16-PPU game.
            CameraFeel.Cue(CameraFeelCue.ImpactMedium, Vector2.zero,
                Mathf.Clamp01(_radius / TOUCHDOWN_REFERENCE_RADIUS));
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                // Every piece of the rig is a child of this object, so one Destroy takes
                // the whole funnel with it.
                Destroy(gameObject);
                return;
            }

            float age = _duration - _remaining;

            // A followed vortex is already being moved by the thing it follows; anything else
            // tracks across the ground on its own.
            if (_followTarget != null) transform.position = _followTarget.position;
            else Drift(Time.deltaTime, age);

            Vector2 position = transform.position;
            _funnel?.SetTravel(Time.deltaTime > 0f
                ? (position - _lastPosition) / Time.deltaTime
                : Vector2.zero);
            _lastPosition = position;

            float grip = Mathf.Clamp01(age / VortexFunnelFX.SpinUpSeconds);
            float dissipate = _remaining < DISSIPATE_SECONDS
                ? 1f - Mathf.Clamp01(_remaining / DISSIPATE_SECONDS)
                : 0f;

            // The force lets go as the funnel comes apart, so the last thing the player sees is
            // the enemies coasting out of it rather than being held by an invisible field.
            ApplyForce(grip * (1f - dissipate), Time.deltaTime);

            _funnel?.Tick(Time.deltaTime, grip, dissipate);
        }

        /// <summary>
        /// Advance the ground track. The heading is INTEGRATED from smooth noise rather than
        /// sampled from it, so the funnel commits to a direction for a while and then leans out
        /// of it — sampling a direction per frame gives a shape that vibrates in place.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> and taking its own <paramref name="age"/> so a test can walk the
        /// whole eight-second track without a Play Mode clock — the same reason
        /// <see cref="ApplyForce"/> takes its delta rather than reading <c>Time</c>.
        /// </remarks>
        internal void Drift(float deltaTime, float age)
        {
            float wander = Mathf.PerlinNoise(_driftSeed, age * 0.35f) * 2f - 1f;
            _driftHeadingDegrees += wander * DRIFT_TURN_DEGREES * deltaTime;

            Vector2 away = (Vector2)transform.position - _driftOrigin;
            float leash01 = Mathf.Clamp01(away.magnitude / DRIFT_LEASH);
            if (leash01 > DRIFT_LEASH_SOFT && away.sqrMagnitude > 1e-4f)
            {
                float homeward = Mathf.Atan2(-away.y, -away.x) * Mathf.Rad2Deg;
                float pull = (leash01 - DRIFT_LEASH_SOFT) / (1f - DRIFT_LEASH_SOFT);
                _driftHeadingDegrees = Mathf.LerpAngle(_driftHeadingDegrees, homeward,
                    Mathf.Clamp01(pull * DRIFT_HOMING * deltaTime));
            }

            float radians = _driftHeadingDegrees * Mathf.Deg2Rad;
            transform.position += new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f)
                                  * DRIFT_SPEED * deltaTime;
        }

        /// <summary>
        /// <paramref name="deltaTime"/> is passed in rather than read from <c>Time</c> so the
        /// whole force model is a pure function of it. A test that drives this through
        /// <c>Physics2D.Simulate</c> otherwise measures the editor's own frame time — which is
        /// about a tenth of a real frame while nothing is rendering, and understates the
        /// force by that factor without failing.
        /// </summary>
        private void ApplyForce(float strength, float deltaTime)
        {
            if (strength <= 0f) return;

            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            Vector2 centre = transform.position;
            float maxSpeed = _isPull ? MAX_DRAGGED_SPEED : MAX_SHOVED_SPEED;

            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                var health = hit.GetComponentInParent<Health>();
                if (health != null && health.IsDead) continue;

                Vector2 toCentre = centre - rb.position;
                float distance = toCentre.magnitude;
                if (distance < 1e-4f) continue;

                Vector2 radial = toCentre / distance;
                // Perpendicular, signed so the orbit agrees with the way the funnel turns.
                Vector2 tangent = new Vector2(-radial.y, radial.x) * (_isPull ? 1f : -1f);

                // Only a PULL needs the eye: a body crossing the centre has its inward
                // direction reverse every frame and buzzes in place. A push points away from
                // the centre, which stays well defined right up to it.
                Vector2 aim;
                if (!_isPull) aim = tangent * PUSH_SWIRL - radial * PUSH_OUTWARD;
                else if (distance < _radius * EYE_FRACTION) aim = tangent;
                else aim = radial * PULL_INWARD + tangent * PULL_SWIRL;

                float falloff = Mathf.Lerp(RIM_GRIP, 1f, 1f - Mathf.Clamp01(distance / _radius));

                rb.velocity *= Mathf.Clamp01(1f - FIELD_DAMPING * strength * deltaTime);
                rb.AddForce(aim.normalized * _force * falloff * strength * deltaTime,
                    ForceMode2D.Impulse);

                if (rb.velocity.sqrMagnitude > maxSpeed * maxSpeed)
                    rb.velocity = rb.velocity.normalized * maxSpeed;
            }
        }
    }
}
