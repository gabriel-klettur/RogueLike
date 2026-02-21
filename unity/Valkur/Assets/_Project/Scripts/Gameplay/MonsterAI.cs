using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Basic monster AI with FSM stub.
    /// Maps to Python's FSM states: Idle, Patrol, Chase, Attack, Damage, Death.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class MonsterAI : MonoBehaviour
    {
        public enum State { Idle, Patrol, Chase, Attack, Damage, Death }

        [Header("Definition")]
        [SerializeField] private MonsterDefinition definition;

        [Header("Runtime")]
        [SerializeField] private State currentState = State.Idle;
        [SerializeField] private float stateTimer;

        private Health _health;
        private MeleeCombat _combat;
        private Rigidbody2D _rb;
        private SpriteRenderer _spriteRenderer;
        private Transform _target;

        public State CurrentState => currentState;
        public MonsterDefinition Definition => definition;

        private float _aggroRange;
        private float _meleeRange;
        private float _speed;
        private float _chasingSpeed;
        private float _deathDisappearTime;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _combat = GetComponent<MeleeCombat>();
            _rb = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
        }

        private void Start()
        {
            if (definition != null)
                InitializeFromDefinition(definition);

            _health.OnDeath += HandleDeath;
            _health.OnDamaged += HandleDamaged;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnDeath -= HandleDeath;
                _health.OnDamaged -= HandleDamaged;
            }
        }

        public void InitializeFromDefinition(MonsterDefinition def)
        {
            definition = def;
            _health.Initialize(def.stats.hp);
            _aggroRange = def.stats.aggroRange;
            _meleeRange = def.stats.meleeRange;
            _speed = def.stats.speed;
            _chasingSpeed = def.stats.chasingSpeed;
            _deathDisappearTime = def.stats.deathDisappearTime;

            if (_combat != null)
                _combat.Initialize(def.stats.meleeDamage, def.stats.meleeCooldown, def.stats.meleeRange);

            gameObject.name = def.displayName;
        }

        private void Update()
        {
            if (_health.IsDead)
            {
                if (currentState != State.Death)
                    TransitionTo(State.Death);
                return;
            }

            FindTarget();
            UpdateState();
        }

        private void FixedUpdate()
        {
            if (_health.IsDead || currentState == State.Death)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            switch (currentState)
            {
                case State.Chase:
                    MoveTowardsTarget();
                    break;
                case State.Patrol:
                    // Patrol stub — simple idle for now
                    _rb.velocity = Vector2.zero;
                    break;
                default:
                    _rb.velocity = Vector2.zero;
                    break;
            }
        }

        private void FindTarget()
        {
            if (_target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    _target = player.transform;
            }
        }

        private void UpdateState()
        {
            stateTimer += Time.deltaTime;

            switch (currentState)
            {
                case State.Idle:
                    if (_target != null && DistanceToTarget() <= _aggroRange)
                        TransitionTo(State.Chase);
                    break;

                case State.Patrol:
                    if (_target != null && DistanceToTarget() <= _aggroRange)
                        TransitionTo(State.Chase);
                    break;

                case State.Chase:
                    if (_target == null || DistanceToTarget() > _aggroRange * 1.5f)
                    {
                        TransitionTo(State.Idle);
                    }
                    else if (DistanceToTarget() <= _meleeRange)
                    {
                        TransitionTo(State.Attack);
                    }
                    break;

                case State.Attack:
                    if (_combat != null && _combat.CanAttack && _target != null)
                    {
                        Vector2 dir = (_target.position - transform.position).normalized;
                        _combat.TryAttack(dir);
                    }
                    if (_target == null || DistanceToTarget() > _meleeRange * 1.5f)
                        TransitionTo(State.Chase);
                    break;

                case State.Damage:
                    if (stateTimer >= (definition != null ? definition.stats.damageDuration : 0.5f))
                        TransitionTo(State.Chase);
                    break;

                case State.Death:
                    _rb.velocity = Vector2.zero;
                    if (stateTimer >= _deathDisappearTime)
                        Destroy(gameObject);
                    break;
            }
        }

        private void MoveTowardsTarget()
        {
            if (_target == null) return;

            Vector2 dir = ((Vector2)_target.position - (Vector2)transform.position).normalized;
            _rb.velocity = dir * _chasingSpeed;

            if (_spriteRenderer != null)
                _spriteRenderer.flipX = dir.x < 0;
        }

        private float DistanceToTarget()
        {
            if (_target == null) return float.MaxValue;
            return Vector2.Distance(transform.position, _target.position);
        }

        private void TransitionTo(State newState)
        {
            currentState = newState;
            stateTimer = 0f;
        }

        private void HandleDeath()
        {
            TransitionTo(State.Death);
        }

        private void HandleDamaged(int amount)
        {
            if (currentState != State.Death)
            {
                float stopProb = definition != null ? definition.stats.damageStopProbability : 0.25f;
                if (Random.value < stopProb)
                    TransitionTo(State.Damage);
            }
        }
    }
}
