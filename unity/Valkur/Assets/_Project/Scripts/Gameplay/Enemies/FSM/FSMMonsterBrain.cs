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

        /// <summary>True while a conversation is holding this character still.</summary>
        public bool ConversationPaused => _conversationPaused;

        /// <summary>
        /// Holds this character still for the duration of a conversation, and hands it back
        /// afterwards exactly where it was.
        ///
        /// <para>THE PAUSE IS THE TICK, NOT THE VELOCITY. Stun and Root refuse the feet
        /// inside <see cref="FSMComponents.SetVelocity"/> and deliberately let the machine
        /// keep running, which is right for them — a rooted monster should still swing. It
        /// is wrong here: a stroller whose Execute still runs goes on counting down its
        /// dwell, picks new headings against walls, and flips itself between its idle and
        /// walk phases while standing in front of you. Freezing the update instead means the
        /// phase she was in is the phase she resumes, so the conversation costs her nothing.
        /// </para>
        ///
        /// <para>Two things must still be done BY HAND on the way in, because the states
        /// re-assert them every tick and a frozen state asserts nothing. The body keeps its
        /// <c>Rigidbody2D.velocity</c> unless someone zeroes it, so an unstopped NPC coasts
        /// away mid-sentence; and <c>DirectionalAnimator</c> advances its own frames, so the
        /// walk cycle plays on in place — a character moonwalking through her own dialogue.
        /// Nothing has to be restored on the way out: <c>StrollState.DriveWalk</c> and every
        /// other state write both again on their next tick.</para>
        ///
        /// <para>ONE OWNER, WHICH IS WHAT MAKES A PLAIN BOOL SAFE HERE. <c>ChatSystem</c> is
        /// the only caller, exactly as <c>VulnerableEffect</c> is the only writer of
        /// <c>Health.SetVulnerability</c>. A second writer would reintroduce the
        /// <c>SetInvincible</c> defect — three independent owners of one bool, where whoever
        /// clears it switches off whatever the others were holding — and would then need
        /// save-and-restore rather than a set.</para>
        /// </summary>
        public void SetConversationPaused(bool paused)
        {
            if (_conversationPaused == paused) return;
            _conversationPaused = paused;
            if (!paused) return;

            _fsm?.GetContext<FSMComponents>(FSMComponents.KEY)?.StopMovement();

            if (_animator != null)
                _animator.SetState(DirectionalAnimator.AnimState.Idle, _animator.CurrentDirection);
        }

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

        /// <summary>
        /// This entity's <see cref="Valkur.Gameplay.Entities.PersistedEntityInstance.PlacementId"/>
        /// once one exists. Empty for a spawner-spawned monster, which has no placement
        /// identity and therefore no per-placement FSM override to look up.
        /// </summary>
        private string _placementId;

        /// <summary>
        /// Called by <see cref="Valkur.Gameplay.Entities.PersistedEntityInstance"/> the moment
        /// this entity gains a placement identity, which is AFTER
        /// <c>EntitySetup.ConfigureMonster</c> has already run <see cref="Initialize"/> — the
        /// F5 spawn path configures the monster and only then stamps the id onto it.
        /// So the id cannot be read during the first Initialize, and the only honest way to
        /// honour a <c>by_eid</c> override is to rebuild once the id arrives.
        ///
        /// Rebuilds NOTHING unless assignments.json actually names a set for this placement,
        /// which no shipped placement does — so the normal path is one dictionary probe.
        /// </summary>
        public void RebindFsmForPlacement(string placementId)
        {
            if (string.IsNullOrEmpty(placementId) || placementId == _placementId) return;
            _placementId = placementId;
            if (definition == null) return;
            if (!FSMRuntimeFactory.HasPlacementOverride(placementId)) return;
            Initialize(definition);
        }

        public void Initialize(MonsterDefinition def)
        {
            definition = def;

            // Only hp / meleeDamage / defense answer to MonsterDefinition.level; every
            // other stat below is read off `def.stats` on purpose, so a levelled copy
            // still moves, reaches and times exactly like the monster it is a copy of.
            // Level <= 1 (every shipped monster today) returns `stats` unchanged.
            var scaled = def.GetScaledStats();
            _health.Initialize(scaled.hp);

            var combat = GetComponent<MeleeCombat>();
            if (combat != null)
                combat.Initialize(scaled.meleeDamage, def.stats.meleeCooldown, def.stats.meleeRange);

            gameObject.name = def.displayName;

            // Create FSM. Prefer the JSON-driven factory (Phase 3 of the FSM
            // data migration: drives initial state + allowed-state guard from
            // StreamingAssets/FSM/sets.json). Fall back to the legacy
            // hard-coded boot whenever the archetype isn't seeded — keeps
            // brand-new monster prefabs working before the designer runs the
            // generator.
            // def.fsmSet is passed as the LAST-RESORT hint. It is authored on assets and was
            // read by nothing: knight_red says "Monster_Default" and still booted a bare
            // IdleState because only assignments.json resolved. See TryBuildForEntity.
            if (!FSMRuntimeFactory.TryBuildForEntity(_placementId, def.monsterKey, def.fsmSet,
                                                     gameObject, out _fsm))
            {
                _fsm = new StateMachine(gameObject, new IdleState());
            }
            _fsm.SetContext(FSMComponents.KEY, new FSMComponents(gameObject));
            _fsm.SetContext("aggro_range", def.stats.aggroRange);
            _fsm.SetContext("melee_range", def.stats.meleeRange);
            _fsm.SetContext("speed", def.stats.speed);
            _fsm.SetContext("chasing_speed", def.stats.chasingSpeed);
            _fsm.SetContext("damage_duration", def.stats.damageDuration);
            _fsm.SetContext("damage_stop_probability", def.stats.damageStopProbability);
            _fsm.SetContext("death_disappear_time", def.stats.deathDisappearTime);
            _fsm.SetContext("attack_windup_s", def.stats.attackWindupSeconds);
            _fsm.SetContext("faction", def.stats.faction);
            _fsm.SetContext("use_attack_telegraph", def.useAttackTelegraph);
            PublishBehaviourTuning(def.aiTuning);

            // The authored moveset, so AttackState can weigh and gate its variants instead
            // of rolling a uniform Random over whatever the animator happens to hold.
            if (def.assetConfig != null && def.assetConfig.attackVariants != null &&
                def.assetConfig.attackVariants.Count > 0)
            {
                // Fully qualified: this class exposes an `FSM` property of type StateMachine,
                // so a bare `FSM.AttackState` binds to that member instead of the namespace.
                _fsm.SetContext(Valkur.Gameplay.FSM.AttackState.AttackVariantContextKey,
                                def.assetConfig.attackVariants.ToArray());
            }

            // Where this entity belongs. Until now the spawn position was read once to
            // seed patrol waypoints and then thrown away, so nothing downstream could ask
            // "how far have I been pulled from home" — which is why ChaseState's own
            // docblock promised leash support that was never implemented, and a de-aggroed
            // monster simply patrolled from wherever it happened to stop.
            Vector2 spawnPos = transform.position;
            _fsm.SetContext(FSMHomeAnchor.KeyX, spawnPos.x);
            _fsm.SetContext(FSMHomeAnchor.KeyY, spawnPos.y);

            // Generate patrol waypoints from definition
            if (!string.IsNullOrEmpty(def.patrolType))
            {
                var waypoints = PatrolWaypointGenerator.Generate(spawnPos, def.patrolType);
                _fsm.SetContext("patrol_waypoints", waypoints);
            }

            _fsm.OnStateChanged += OnFSMStateChanged;

            // Everything the initial state reads is now published, so it is safe to
            // enter it. Deliberately last: an authored initial PatrolState reads the
            // waypoints set five lines up, and IdleState reads FSMComponents.
            _fsm.Begin();
        }

        /// <summary>
        /// Publishes only the feel knobs the author actually set.
        ///
        /// Zero means "use the engine default", so an unset field publishes NOTHING and
        /// <c>FSMTuning</c>'s default is what the state reads. That is the whole reason
        /// every shipped monster still behaves exactly as it did when these values were
        /// compile-time constants — writing a 0 into the context instead would have made
        /// every monster's aggro hysteresis, repath interval and flee duration zero.
        /// </summary>
        private void PublishBehaviourTuning(AIBehaviourTuning t)
        {
            void Publish(string key, float value)
            {
                if (value > 0f) _fsm.SetContext(key, value);
            }

            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyAggroExitHysteresis, t.aggroExitHysteresis);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyLeashRange,          t.leashRange);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyRepathInterval,      t.repathInterval);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyWaypointReachDist,   t.waypointReachDistance);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyAlertDuration,       t.alertDuration);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyFleeDuration,        t.fleeDuration);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyFleeSpeedMultiplier, t.fleeSpeedMultiplier);
            Publish(Valkur.Gameplay.FSM.FSMTuning.KeyReswingRangeFactor,  t.reswingRangeFactor);
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
            _fsm.Begin();
        }

        /// <summary>
        /// Longest span a single throttled tick may replay. An offscreen entity is
        /// let through every <c>offscreenUpdateInterval</c> frames, so the real
        /// accumulation is ~0.13 s; this only bounds a genuine hitch (a world load,
        /// a domain reload) so a monster cannot resolve a whole swing in one frame.
        /// </summary>
        private const float MaxCatchUpSeconds = 0.5f;

        private float _pendingDt;

        // Instance state on a MonoBehaviour, so Domain Reload being off costs nothing here:
        // it dies with the character rather than outliving a Play session.
        private bool _conversationPaused;

        private void Update()
        {
            // Accumulate EVERY frame, not just the ones that tick. The FSM used to be
            // handed a single frame's deltaTime on the 1-in-8 frame an offscreen
            // monster was allowed through, so attack windup, damage_duration,
            // death_disappear_time and the patrol dwell all ran at an eighth speed
            // whenever the camera looked away — behaviour you could not reproduce
            // because it depended on where the player was looking.
            _pendingDt += Time.deltaTime;

            // Discarded rather than accumulated. A conversation lasts minutes and the
            // catch-up clamp is half a second, so resuming from a banked debt would still
            // teleport her half a second's walk sideways the instant the panel closes —
            // which is the one frame the player is looking straight at her.
            if (_conversationPaused)
            {
                _pendingDt = 0f;
                return;
            }

            if (_culling != null && !_culling.ShouldUpdate)
                return;

            float dt = Mathf.Min(_pendingDt, MaxCatchUpSeconds);
            _pendingDt = 0f;
            _fsm?.Update(dt);
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
                // A stroller alternates Idle and Walk from inside its own Execute, so what
                // it enters ON is the resting half. Listed rather than left to the silent
                // `_ =>` default, which answers the same thing for a different reason.
                StrollState => DirectionalAnimator.AnimState.Idle,
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
