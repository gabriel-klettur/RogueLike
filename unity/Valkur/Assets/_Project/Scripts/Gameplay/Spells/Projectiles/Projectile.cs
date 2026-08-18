using UnityEngine;
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

        // Layers that block the projectile but don't receive damage (walls, buildings, tiles)
        private static readonly LayerMask ObstacleLayers =
            (1 << 11) | (1 << 14); // World=11, Building=14

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
        /// Bind the caster so the projectile never damages it or any of its
        /// children, even when a child collider lives on a layer inside
        /// <see cref="targetLayers"/>. Required by every executor that spawns
        /// a Projectile to keep self-damage impossible by construction.
        /// </summary>
        public void SetCaster(Transform caster) => _caster = caster;

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

        private void ResolveHit(Collider2D other)
        {
            int hitMask = 1 << other.gameObject.layer;

            if ((hitMask & targetLayers) != 0)
            {
                var health = other.GetComponent<Health>() ?? other.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                    health.TakeDamage(Mathf.RoundToInt(damage));
            }
            // Obstacle hits do no damage but still expire.

            Expire();
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
                        h.TakeDamage(Mathf.RoundToInt(_explosionDamage > 0f ? _explosionDamage : damage));
                }
            }

            // Epic impact for any procedural projectile visual (fireball, darkball,
            // iceball, lightball, lightning, boomerang...). Each visual implements
            // IProjectileVisual.OnImpact() with its own shockwave + flash + element
            // burst + light pulse + camera shake. Returns null when the projectile
            // has no procedural visual attached (legacy sprite-only spells).
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
                Destroy(gameObject);
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
