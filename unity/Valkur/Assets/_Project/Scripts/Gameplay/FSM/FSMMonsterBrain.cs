using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// MonoBehaviour that drives a monster using the full FSM system.
    /// Replaces the inline MonsterAI state logic with proper FSM states.
    /// Wires MonsterDefinition stats into FSM context.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class FSMMonsterBrain : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField] private MonsterDefinition definition;

        private StateMachine _fsm;
        private Health _health;
        private DirectionalAnimator _animator;
        private EntityCulling _culling;

        public StateMachine FSM => _fsm;
        public MonsterDefinition Definition => definition;
        public string CurrentStateName => _fsm?.CurrentState?.GetType().Name.Replace("State", "") ?? "";

        private void Awake()
        {
            _health = GetComponent<Health>();
            _animator = GetComponent<DirectionalAnimator>();
            _culling = GetComponent<EntityCulling>();
            if (_culling == null)
                _culling = gameObject.AddComponent<EntityCulling>();

            var rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        private void Start()
        {
            // If already initialized by EntitySetup.ConfigureMonster, skip
            if (_fsm == null)
            {
                if (definition != null)
                    Initialize(definition);
                else
                    InitializeDefault();
            }

            // Subscribe events only if not already subscribed
            _health.OnDeath -= OnDeath;
            _health.OnDamaged -= OnDamaged;
            _health.OnDeath += OnDeath;
            _health.OnDamaged += OnDamaged;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnDeath -= OnDeath;
                _health.OnDamaged -= OnDamaged;
            }
        }

        public void Initialize(MonsterDefinition def)
        {
            definition = def;
            _health.Initialize(def.stats.hp);

            var combat = GetComponent<MeleeCombat>();
            if (combat != null)
                combat.Initialize(def.stats.meleeDamage, def.stats.meleeCooldown, def.stats.meleeRange);

            gameObject.name = def.displayName;

            // Create FSM with context from definition
            _fsm = new StateMachine(gameObject, new IdleState());
            _fsm.SetContext("aggro_range", def.stats.aggroRange);
            _fsm.SetContext("melee_range", (float)def.stats.meleeRange);
            _fsm.SetContext("speed", def.stats.speed);
            _fsm.SetContext("chasing_speed", def.stats.chasingSpeed);
            _fsm.SetContext("damage_duration", def.stats.damageDuration);
            _fsm.SetContext("damage_stop_probability", def.stats.damageStopProbability);
            _fsm.SetContext("death_disappear_time", def.stats.deathDisappearTime);
            _fsm.SetContext("attack_windup_s", def.stats.attackWindupSeconds);
            _fsm.SetContext("faction", def.stats.faction);

            _fsm.OnStateChanged += OnFSMStateChanged;
        }

        private void InitializeDefault()
        {
            _fsm = new StateMachine(gameObject, new IdleState());
            _fsm.SetContext("aggro_range", 5f);
            _fsm.SetContext("melee_range", 1.5f);
            _fsm.SetContext("speed", 2f);
            _fsm.SetContext("chasing_speed", 3f);
            _fsm.SetContext("damage_duration", 0.25f);
            _fsm.SetContext("damage_stop_probability", 0.25f);
            _fsm.SetContext("death_disappear_time", 10f);
            _fsm.SetContext("attack_windup_s", 0.2f);

            _fsm.OnStateChanged += OnFSMStateChanged;
        }

        private void Update()
        {
            if (_culling != null && !_culling.ShouldUpdate)
                return;

            _fsm?.Update(Time.deltaTime);
        }

        private void OnDeath()
        {
            _culling?.ForceActiveNextFrame();
            _fsm?.QueueEvent(new FSMEvent { Type = FSMEventType.OnDeath });
        }

        private void OnDamaged(int amount)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            bool fromLeft = false;
            if (player != null)
                fromLeft = player.transform.position.x < transform.position.x;

            _culling?.ForceActiveNextFrame();
            _fsm?.QueueEvent(new FSMEvent
            {
                Type = FSMEventType.OnHit,
                FromLeft = fromLeft,
                Damage = amount
            });
        }

        private void OnFSMStateChanged(IState oldState, IState newState)
        {
            if (_animator == null) return;

            var animState = newState switch
            {
                IdleState => DirectionalAnimator.AnimState.Idle,
                PatrolState => DirectionalAnimator.AnimState.Walk,
                ChaseState => DirectionalAnimator.AnimState.Chase,
                AlertChaseState => DirectionalAnimator.AnimState.Chase,
                AttackState => DirectionalAnimator.AnimState.Attack,
                DamageState => DirectionalAnimator.AnimState.Damage,
                UnconsciousState => DirectionalAnimator.AnimState.Death,
                DeathState => DirectionalAnimator.AnimState.Death,
                FleeState => DirectionalAnimator.AnimState.Walk,
                _ => DirectionalAnimator.AnimState.Idle
            };

            _animator.SetState(animState, _animator.CurrentDirection);
        }
    }
}
