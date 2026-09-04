using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Generic projectile that moves in a direction, deals damage on hit, and expires.
    /// Maps to Python's projectile spell type (fireball, iceball, darkball, etc.).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Projectile : MonoBehaviour
    {
        [Header("Runtime (set by SpellCaster)")]
        [SerializeField] private float speed = 10f;
        [SerializeField] private float damage = 20f;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float range = 20f;
        [SerializeField] private LayerMask targetLayers;

        // Layers that block the projectile but don't receive damage (walls, buildings, tiles).
        // World(11) + Building(14) alone cover only the building boxes: every painted
        // collision cell is re-emitted by WorldCollisionBaker onto WorldL0..WorldAll,
        // so a projectile masked on the two legacy layers flies through painted walls.
        private static LayerMask ObstacleLayers => World.Layering.WorldCollisionLayers.BlockingMask();

        private Vector2 _direction;
        private Vector2 _origin;
        private float _timer;
        private Rigidbody2D _rb;
        private CircleCollider2D _circle;
        private float _radius = 0.15f;
        private bool _expired;
        private Color _vfxColor = new Color(1f, 0.6f, 0.2f, 0.8f);
        private string _poolKey;
        private readonly System.Collections.Generic.List<string> _impactPresets =
            new System.Collections.Generic.List<string>();
        // Caster transform — projectiles MUST never damage their own caster, even
        // when the caster has a child collider on a layer included in targetLayers
        // (e.g. a hurtbox / perception trigger). Without this, GetComponentInParent
        // <Health> would walk up from the child collider and find the caster's
        // own Health, producing the "fireball blew up in my face" regression.
        private Transform _caster;

        // Damage type + status effects the caster's SpellDefinition authored, so the impact
        // seam can consult the victim's elemental resistances and roll any status
        // applications. Set by the spawning executor via SetElement / SetStatusApplications;
        // both default to "none", which reproduces the pre-existing unmitigated,
        // status-free behaviour for a caller that never sets them.
        private SpellElement? _element;
        private StatusApplication[] _statusApplications;

        // Override impact position used for VFX (set when sweep produces a real hit point).
        // When unset (default), VFX spawns at transform.position.
        private Vector3? _impactVfxPos;

        // Scale multiplier applied to the impact particle preset.
        private const float ImpactPresetScale = 5f;

        // Acceleration: increases speed over time (world units/s²). 0 = constant speed.
        private float _acceleration;

        // Explosion AOE on impact (radius = 0 means no AOE).
        private float _explosionRadius;
        private float _explosionDamage;

        // ── Piercing ─────────────────────────────────────────────────────────
        // Bodies this shot may still pass THROUGH. 0 (the default) stops at the first
        // target, which is how every projectile in this game has always behaved.
        //
        // Obstacles are deliberately NOT counted here: a wall stops a piercing shot dead,
        // because the alternative is a spell that shoots through the level.
        private int _pierceRemaining;
        private float _pierceFalloff;
        // Colliders already pierced. The sweep runs every FixedUpdate and a target the
        // projectile is currently INSIDE keeps being returned, so without this list one
        // enemy would consume the whole pierce budget in three frames.
        private readonly System.Collections.Generic.List<Collider2D> _pierced =
            new System.Collections.Generic.List<Collider2D>(8);

        // ── Homing ───────────────────────────────────────────────────────────
        // Turn rate in degrees/second toward the acquired target. 0 (the default) flies
        // straight. Both this and _homingRange must be non-zero: a shot with a turn rate
        // and no acquisition radius finds nothing and flies straight, which looks like the
        // field not working rather than like a spell that missed.
        private float _homingStrength;
        private float _homingRange;
        private Transform _homingTarget;
        // Borrowed from PhysicsScratch rather than declared here. Three buffers written in
        // one batch all took the obvious `static readonly` shape and all three failed the
        // Domain-Reload ratchet; the shared home is what makes the wrong shape unavailable.
        // The two sibling buffers below are grandfathered in the baseline and left alone.
        // Re-acquisition cadence. Every frame would be a physics query per projectile per
        // frame for a spell that fires several at once; a fifth of a second is far below
        // the time it takes a target to travel out of a 6-unit radius.
        private const float ACQUIRE_INTERVAL = 0.2f;
        private float _acquireTimer;

        /// <summary>Raised each time this shot pierces a body, at the contact point. The
        /// pierce is an EVENT and the visual has to say so, or the falloff is a number the
        /// player can never see.</summary>
        public event System.Action<Vector3, int> OnPierced;

        /// <summary>Fired when this shot first locks onto a target. A homing shot that
        /// acquires silently reads as one that happens to be flying the same way.</summary>
        public event System.Action<Transform> OnHomingAcquired;

        // Reused buffer for swept collision queries (no per-frame allocations)
        private static readonly RaycastHit2D[] _sweepHits = new RaycastHit2D[8];
        // Reused buffer for explosion overlap queries
        private static readonly Collider2D[] _explosionHits = new Collider2D[16];

        /// <summary>
        /// Set the pool key so the projectile returns to pool instead of being destroyed.
        /// </summary>
        public void SetPoolKey(string key) => _poolKey = key;

        /// <summary>
        /// Set the VFX color for impact effects.
        /// </summary>
        public void SetVFXColor(Color color) => _vfxColor = color;

        /// <summary>
        /// Set the particle preset played on impact (e.g. "explosion_small").
        /// </summary>
        public void SetImpactPreset(string preset)
        {
            _impactPresets.Clear();
            if (!string.IsNullOrEmpty(preset)) _impactPresets.Add(preset);
        }

        /// <summary>
        /// Set the whole impact preset stack. An impact reads as one event but is built
        /// from several: a flash, an expanding shockwave, debris, a lingering smoke puff.
        /// Order is draw order.
        /// </summary>
        public void SetImpactPresets(System.Collections.Generic.List<string> presets)
        {
            _impactPresets.Clear();
            if (presets != null) _impactPresets.AddRange(presets);
        }

        /// <summary>Constant acceleration applied to speed each second (world units/s²).</summary>
        public void SetAcceleration(float accel) => _acceleration = accel;

        /// <summary>Enable AOE explosion on impact that damages all targets within radius.</summary>
        public void SetExplosion(float radius, float dmg) { _explosionRadius = radius; _explosionDamage = dmg; }

        /// <summary>
        /// How many bodies this shot passes through before it stops, and how much damage it
        /// sheds per body. Both default to 0, which is the historical behaviour.
        /// </summary>
        public void SetPiercing(int count, float falloff)
        {
            _pierceRemaining = Mathf.Max(0, count);
            _pierceFalloff = Mathf.Clamp01(falloff);
            _pierced.Clear();
        }

        /// <summary>
        /// Turn rate and acquisition radius for a seeking shot. Zero on either disables
        /// homing entirely — see the field comment for why both are required.
        /// </summary>
        public void SetHoming(float degreesPerSecond, float acquireRange)
        {
            _homingStrength = Mathf.Max(0f, degreesPerSecond);
            _homingRange = Mathf.Max(0f, acquireRange);
            _homingTarget = null;
            _acquireTimer = 0f;
        }

        /// <summary>Current seek target, or null. Read by the visual so the rig can show a lock.</summary>
        public Transform HomingTarget => _homingTarget;

        /// <summary>Bodies this shot can still pass through. Read by the visual so the core
        /// can dim as the budget is spent.</summary>
        public int PierceRemaining => _pierceRemaining;

        /// <summary>
        /// Bind the caster so the projectile never damages it or any of its
        /// children, even when a child collider lives on a layer inside
        /// <see cref="targetLayers"/>. Required by every executor that spawns
        /// a Projectile to keep self-damage impossible by construction.
        /// </summary>
        public void SetCaster(Transform caster) => _caster = caster;

        /// <summary>Damage type consulted against the victim's Health.resistances on impact.</summary>
        public void SetElement(SpellElement? element) => _element = element;

        /// <summary>Status effects rolled against the victim on a successful hit.</summary>
        public void SetStatusApplications(StatusApplication[] applications) => _statusApplications = applications;

        public void Initialize(Vector2 direction, float spd, float dmg, float life, float rng, LayerMask targets)
        {
            _direction = direction.normalized;
            speed = spd;
            damage = dmg;
            lifetime = life;
            range = rng;
            targetLayers = targets;
            _origin = transform.position;

            // Rotate sprite to face direction
            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            // Continuous CCD also helps physics-driven cases (e.g. when speed is low
            // and we still rely on Unity's solver). Swept queries below are the primary
            // defence against tunneling.
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            _circle = GetComponent<CircleCollider2D>();
            if (_circle != null)
                _radius = Mathf.Max(0.01f, _circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y));

            _origin = transform.position;
        }

        private void FixedUpdate()
        {
            if (_expired) return;

            // Steer BEFORE the sweep, so the sweep is cast along the heading the shot is
            // actually taking this step rather than the one it had last step.
            SteerTowardTarget(Time.fixedDeltaTime);

            // Apply acceleration before computing the step this frame.
            if (_acceleration > 0f)
                speed += _acceleration * Time.fixedDeltaTime;

            float step = speed * Time.fixedDeltaTime;
            if (step <= 0f) return;

            // Sweep a circle from current position along the direction by 'step' units.
            // Save & restore queriesHitTriggers so we always detect target colliders even
            // if they happen to be triggers (defensive — most NPCs use solid colliders).
            int sweepMask = targetLayers | ObstacleLayers;
            bool prevHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            int hitCount = Physics2D.CircleCastNonAlloc(
                (Vector2)transform.position,
                _radius,
                _direction,
                _sweepHits,
                step,
                sweepMask);
            Physics2D.queriesHitTriggers = prevHitTriggers;

            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = _sweepHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue; // ignore self
                if (IsCasterCollider(hit.collider)) continue;              // ignore caster + caster-children
                if (_pierced.Count > 0 && _pierced.Contains(hit.collider)) continue; // already went through it
                // Skip "start-inside" overlaps. Physics2D.queriesStartInColliders is enabled
                // project-wide, so a sweep that begins overlapping a collider returns it with
                // distance == 0. Without this guard, a fireball spawned at/near the caster's
                // collider (or near a wall) would detonate on its first FixedUpdate step.
                if (hit.distance <= Mathf.Epsilon) continue;
                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    best = hit;
                }
            }

            if (best.collider != null)
            {
                // Move the projectile so its centre rests against the impact surface.
                Vector2 centrePos = (Vector2)transform.position + _direction * Mathf.Max(0f, best.distance);
                _rb.position = centrePos;
                // Use the actual contact point on the obstacle surface for VFX so the
                // explosion visually sits ON the wall/tile, not at the projectile centre.
                _impactVfxPos = best.point;
                ResolveHit(best.collider);
                return;
            }

            // No hit — advance via velocity for visual smoothness/interpolation.
            _rb.velocity = _direction * speed;
        }

        // Publish the hit on the global channel so everything listening there —
        // combo counter, combat audio, feedback — sees spell damage too. Without
        // this only MeleeCombat ever reported a hit, which is why the player's
        // spells never built a combo.
        private void ReportHit(GameObject victim, int dealt)
        {
            if (_caster == null || victim == null || dealt <= 0) return;
            Valkur.Core.GameEvents.FireHitDealt(_caster.gameObject, victim, dealt);
        }

        private void ResolveHit(Collider2D other)
        {
            int hitMask = 1 << other.gameObject.layer;

            if ((hitMask & targetLayers) != 0)
            {
                var health = other.GetComponent<Health>() ?? other.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                {
                    int dealt = Mathf.RoundToInt(damage);
                    GameObject casterGo = _caster != null ? _caster.gameObject : null;
                    health.TakeDamage(dealt, casterGo, _element);
                    ReportHit(health.gameObject, dealt);
                    StatusApplicationFactory.ApplyAll(_statusApplications, health.gameObject, casterGo);

                    // A piercing shot survives a BODY and never an obstacle. Returning here
                    // is the whole mechanic: everything below this block is the expire path.
                    if (_pierceRemaining > 0)
                    {
                        ContinueThrough(other);
                        return;
                    }
                }
                else if (_pierceRemaining > 0 && health != null)
                {
                    // A corpse must not consume the budget, and must not stop the shot
                    // either — otherwise a piercing lance is cancelled by whatever died
                    // in front of it a moment ago.
                    _pierced.Add(other);
                    _impactVfxPos = null;
                    return;
                }
            }
            else
            {
                // Obstacle hits do no damage — unless the obstacle is one that can be
                // attacked down. A spell wall has to live on Building to block anything at
                // all, and Building is in ObstacleLayers, so this branch is the only place a
                // projectile can ever reach it. See IDestructibleObstacle.
                var obstacle = other.GetComponentInParent<IDestructibleObstacle>();
                if (obstacle != null && obstacle.AcceptsDamage)
                {
                    Vector2 contact = _impactVfxPos.HasValue
                        ? (Vector2)_impactVfxPos.Value
                        : (Vector2)transform.position;
                    obstacle.ApplyObstacleDamage(Mathf.RoundToInt(damage),
                        _caster != null ? _caster.gameObject : null, contact, _element);
                }
            }

            Expire();
        }

        // ── Piercing and homing ──────────────────────────────────────────────

        /// <summary>
        /// Spend one pierce and keep flying. The falloff is applied AFTER the body that
        /// triggered it has already been damaged, so the first target always takes the full
        /// authored value and only the ones behind it are discounted.
        /// </summary>
        private void ContinueThrough(Collider2D victim)
        {
            _pierced.Add(victim);
            _pierceRemaining--;

            if (_pierceFalloff > 0f)
                damage = Mathf.Max(1f, damage * (1f - _pierceFalloff));

            Vector3 contact = _impactVfxPos ?? transform.position;
            OnPierced?.Invoke(contact, _pierceRemaining);

            // Clear the override so the eventual real impact resolves its own contact
            // point instead of reusing the last body this shot went through.
            _impactVfxPos = null;
        }

        /// <summary>
        /// Turn the heading toward the acquired target by at most
        /// <c>_homingStrength * deltaTime</c> degrees.
        ///
        /// <para>The clamp is the entire design. Rotating straight to the bearing would be a
        /// shot that cannot be dodged and does not read as seeking — what the eye recognises
        /// as hunting is the LAG, a curve the projectile visibly has to work through. At the
        /// shipped 220 deg/s a shard takes about a sixth of a second to reverse 45 degrees,
        /// which is long enough to see and short enough to land.</para>
        /// </summary>
        private void SteerTowardTarget(float deltaTime)
        {
            if (_homingStrength <= 0f || _homingRange <= 0f) return;

            _acquireTimer -= deltaTime;
            if (_homingTarget == null || !_homingTarget.gameObject.activeInHierarchy || _acquireTimer <= 0f)
            {
                _acquireTimer = ACQUIRE_INTERVAL;
                AcquireTarget();
            }
            if (_homingTarget == null) return;

            Vector2 toTarget = (Vector2)_homingTarget.position - (Vector2)transform.position;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float maxTurn = _homingStrength * deltaTime;
            Vector2 desired = toTarget.normalized;
            float signed = Vector2.SignedAngle(_direction, desired);
            float applied = Mathf.Clamp(signed, -maxTurn, maxTurn);

            _direction = (Vector2)(Quaternion.Euler(0f, 0f, applied) * _direction);
            _direction.Normalize();

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Nearest living target inside the acquisition radius, preferring one the shot is
        /// already roughly pointed at. Purely nearest would make a seeking shot turn around
        /// for something behind the caster, which reads as the spell malfunctioning.
        /// </summary>
        private void AcquireTarget()
        {
            Transform previous = _homingTarget;
            _homingTarget = null;

            bool prevHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position, _homingRange, Combat.PhysicsScratch.HomingAcquire, targetLayers);
            Physics2D.queriesHitTriggers = prevHitTriggers;

            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var col = Combat.PhysicsScratch.HomingAcquire[i];
                if (col == null) continue;
                if (IsCasterCollider(col)) continue;
                if (_pierced.Count > 0 && _pierced.Contains(col)) continue;

                var health = col.GetComponent<Health>() ?? col.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 to = (Vector2)col.transform.position - (Vector2)transform.position;
                float dist = to.magnitude;
                if (dist < 0.0001f) continue;

                // Behind-the-shot penalty: dot runs +1 straight ahead to -1 straight back,
                // so this adds up to a full radius of virtual distance to a target the shot
                // would have to turn around for.
                float dot = Vector2.Dot(_direction, to / dist);
                float score = dist + (1f - dot) * 0.5f * _homingRange;
                if (score >= bestScore) continue;

                bestScore = score;
                _homingTarget = health.transform;
            }

            if (_homingTarget != null && _homingTarget != previous)
                OnHomingAcquired?.Invoke(_homingTarget);
        }

        private void Update()
        {
            if (_expired) return;

            _timer += Time.deltaTime;

            // Expire by time or range
            float distSq = ((Vector2)transform.position - _origin).sqrMagnitude;
            if (_timer >= lifetime || distSq >= range * range)
            {
                Expire();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Fallback for cases where the swept query missed (e.g. spawned overlapping a target).
            if (_expired) return;

            int hitMask = 1 << other.gameObject.layer;
            if (((hitMask & targetLayers) | (hitMask & ObstacleLayers)) == 0) return;
            if (other.transform.IsChildOf(transform)) return;
            if (IsCasterCollider(other)) return;

            // Use the closest point on the obstacle to get a surface-accurate VFX position.
            _impactVfxPos = other.ClosestPoint(transform.position);
            ResolveHit(other);
        }

        private void Expire()
        {
            _expired = true;
            _rb.velocity = Vector2.zero;

            // Spawn impact VFX at the actual contact point when available, otherwise at
            // the projectile centre (fallback for OnTriggerEnter2D path or lifetime expiry).
            Vector3 vfxPos = _impactVfxPos ?? transform.position;

            // AOE explosion: damage all targets within explosion radius.
            if (_explosionRadius > 0f)
            {
                int count = Physics2D.OverlapCircleNonAlloc((Vector2)vfxPos, _explosionRadius, _explosionHits, targetLayers);
                for (int i = 0; i < count; i++)
                {
                    var col = _explosionHits[i];
                    if (col == null) continue;
                    if (IsCasterCollider(col)) continue; // never AOE-damage the caster
                    var h = col.GetComponent<Health>()
                         ?? col.GetComponentInParent<Health>();
                    if (h != null && !h.IsDead)
                    {
                        int dealt = Mathf.RoundToInt(_explosionDamage > 0f ? _explosionDamage : damage);
                        GameObject casterGo = _caster != null ? _caster.gameObject : null;
                        h.TakeDamage(dealt, casterGo, _element);
                        ReportHit(h.gameObject, dealt);
                        StatusApplicationFactory.ApplyAll(_statusApplications, h.gameObject, casterGo);
                    }
                }
            }

            // Hand the impact to whatever visual is riding this projectile.
            // ParticleProjectileVisual (every ball spell) only stops its trail here —
            // the shockwave, flash, burst and smoke are the impactPreset stack spawned
            // below. ElementalProjectileVisual (boomerang) draws its own impact rig.
            // Returns null when the projectile has no visual attached at all.
            // NOTE: nothing on this path produces camera trauma, hit-stop or a light
            // pulse; a spell that wants those has to ask CameraFeel.Cue itself.
            var projVisual = GetComponent<IProjectileVisual>();
            if (projVisual != null) projVisual.OnImpact(vfxPos);

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnImpact(vfxPos, _vfxColor, 0.25f, 0.8f);

                // Play the spell's impact particle preset (e.g. explosion_small) scaled up.
                for (int i = 0; i < _impactPresets.Count; i++)
                {
                    if (string.IsNullOrEmpty(_impactPresets[i])) continue;
                    VFXManager.Instance.SpawnParticlePreset(_impactPresets[i], vfxPos, -1f, ImpactPresetScale);
                }
            }

            // Return to pool or destroy
            if (!string.IsNullOrEmpty(_poolKey) && VFXManager.Instance != null)
            {
                gameObject.SetActive(false);
                ResetState();
                VFXManager.Instance.Despawn(_poolKey, gameObject);
            }
            else
            {
                gameObject.SetActive(false);
                // Object.Destroy is deferred and Unity refuses it outside Play Mode with an
                // error. An un-pooled projectile is exactly what an EditMode test builds, so
                // the un-pooled branch is the one that has to answer for both modes. Same
                // guard the runtime editors already use when they clear their UI.
                if (Application.isPlaying) Destroy(gameObject);
                else                       DestroyImmediate(gameObject);
            }
        }

        private void ResetState()
        {
            _direction = Vector2.zero;
            _origin = Vector2.zero;
            _timer = 0f;
            _expired = false;
            _impactVfxPos = null;
            _acceleration = 0f;
            _explosionRadius = 0f;
            _explosionDamage = 0f;
            _impactPresets.Clear();   // pool reuse: never inherit the last spell's explosion
            _caster = null; // pool reuse: drop the previous caster so the next
                            // shooter doesn't inherit a stale ignore-target.
            _element = null;
            _statusApplications = null; // pool reuse: never inherit the last spell's statuses
            // Pool reuse: a shot that inherited a pierce budget would fly through the next
            // caster's target, and one that inherited a subscriber would drive a rig
            // belonging to a projectile that finished several casts ago.
            _pierceRemaining = 0;
            _pierceFalloff = 0f;
            _pierced.Clear();
            _homingStrength = 0f;
            _homingRange = 0f;
            _homingTarget = null;
            _acquireTimer = 0f;
            OnPierced = null;
            OnHomingAcquired = null;
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        /// <summary>
        /// Returns true when <paramref name="other"/> belongs to the caster's
        /// hierarchy (the caster itself or any descendant). Cheap — at most one
        /// IsChildOf walk through Transform parents.
        /// </summary>
        private bool IsCasterCollider(Collider2D other)
        {
            if (_caster == null || other == null) return false;
            var t = other.transform;
            return t == _caster || t.IsChildOf(_caster);
        }
    }
}
