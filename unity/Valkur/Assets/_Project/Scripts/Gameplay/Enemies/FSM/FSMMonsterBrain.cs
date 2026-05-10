using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Enemies.FSM;

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
        private SpriteRenderer _sr;
        private bool _lastDamageFromLeft;

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
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = GetComponentInChildren<SpriteRenderer>();

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

            // Create FSM. Prefer the JSON-driven factory (Phase 3 of the FSM
            // data migration: drives initial state + allowed-state guard from
            // StreamingAssets/FSM/sets.json). Fall back to the legacy
            // hard-coded boot whenever the archetype isn't seeded — keeps
            // brand-new monster prefabs working before the designer runs the
            // generator.
            if (!FSMRuntimeFactory.TryBuildForArchetype(def.monsterKey, gameObject, out _fsm))
            {
                _fsm = new StateMachine(gameObject, new IdleState());
            }
            _fsm.SetContext(FSMComponents.KEY, new FSMComponents(gameObject));
            _fsm.SetContext("aggro_range", def.stats.aggroRange);
            _fsm.SetContext("melee_range", (float)def.stats.meleeRange);
            _fsm.SetContext("speed", def.stats.speed);
            _fsm.SetContext("chasing_speed", def.stats.chasingSpeed);
            _fsm.SetContext("damage_duration", def.stats.damageDuration);
            _fsm.SetContext("damage_stop_probability", def.stats.damageStopProbability);
            _fsm.SetContext("death_disappear_time", def.stats.deathDisappearTime);
            _fsm.SetContext("attack_windup_s", def.stats.attackWindupSeconds);
            _fsm.SetContext("faction", def.stats.faction);

            // Generate patrol waypoints from definition
            if (!string.IsNullOrEmpty(def.patrolType))
            {
                Vector2 spawnPos = transform.position;
                var waypoints = PatrolWaypointGenerator.Generate(spawnPos, def.patrolType);
                _fsm.SetContext("patrol_waypoints", waypoints);
            }

            _fsm.OnStateChanged += OnFSMStateChanged;
        }

        private void InitializeDefault()
        {
            _fsm = new StateMachine(gameObject, new IdleState());
            _fsm.SetContext(FSMComponents.KEY, new FSMComponents(gameObject));
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

            // Flip the sprite horizontally so the corpse "falls" away from the
            // attacker. _lastDamageFromLeft tracks the direction of the most
            // recent OnDamaged event — which is the killing blow because Health
            // fires OnDamaged immediately before OnDeath in TakeDamage.
            //   fromLeft=false (struck from right)  → original orientation
            //   fromLeft=true  (struck from left)   → mirrored
            if (_sr != null) _sr.flipX = _lastDamageFromLeft;

            _fsm?.QueueEvent(new FSMEvent { Type = FSMEventType.OnDeath });
        }

        private void OnDamaged(int amount)
        {
            var player = EntityRegistry.Player;
            bool fromLeft = false;
            if (player != null)
                fromLeft = player.transform.position.x < transform.position.x;
            _lastDamageFromLeft = fromLeft;

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
                NPCCastState => DirectionalAnimator.AnimState.Cast,
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
