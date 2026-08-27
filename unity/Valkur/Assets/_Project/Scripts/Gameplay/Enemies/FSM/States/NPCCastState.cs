using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Spell-cast FSM state for NPCs. Mirrors Python's CastState +
    /// nested PrepareSpellState / ChannelSpellState / CooldownState chain
    /// at FSM granularity: while the underlying <see cref="SpellCaster"/>
    /// works through prepare/channel/cooldown phases, the brain stays
    /// pinned in <see cref="NPCCastState"/> and movement is suppressed.
    /// When the caster returns to <see cref="SpellCaster.CastPhase.Ready"/>,
    /// control passes back to <see cref="ChaseState"/> (or
    /// <see cref="AttackState"/> if the player has stepped into melee
    /// range during the cast).
    ///
    /// Why a state and not just an inline timer in <see cref="NPCAutoCast"/>:
    /// the FSM is the single source of truth for "what is this NPC doing
    /// right now?" — animator hooks (DirectionalAnimator switches to
    /// AnimState.Cast via FSMMonsterBrain.OnFSMStateChanged), suppression
    /// guards (NPCAutoCast.IsSupressed), and external observers (tests,
    /// FX, debug overlays) all read <see cref="StateMachine.CurrentState"/>.
    /// Bypassing the FSM would mean NPCs cast without ever announcing it.
    ///
    /// Death / damage interrupts: handled via the standard FSM event queue
    /// (OnHit / OnDeath) in <see cref="StateMachine"/>; this state does
    /// not need to special-case them. <see cref="DamageState"/> and
    /// <see cref="UnconsciousState"/> are always allowed to preempt.
    /// </summary>
    public class NPCCastState : IState
    {
        private SpellCaster _caster;

        // Hard cap so a misconfigured spell (cooldown 0, prepare 0, channel 0)
        // can never freeze the NPC forever. 30 seconds is well above the
        // longest legitimate cast in the catalog (meteor_shower at ~7.5 s).
        private const float MaxStateDuration = 30f;
        private float _stateTimer;

        public void Enter(StateMachine fsm)
        {
            _caster = fsm.Owner.GetComponent<SpellCaster>();
            _stateTimer = 0f;

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);

            // Suppress movement during the entire cast. Python applies a
            // StunComponent for the channel duration; the simpler Unity
            // approach is to zero velocity here and refrain from writing
            // any new velocity in Execute.
            c?.StopMovement();

            // Face the player so the cast direction lines up with the
            // animator's idle-facing pose. The animator state itself is
            // driven by FSMMonsterBrain.OnFSMStateChanged → AnimState.Cast.
            FacePlayer(c, fsm);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _stateTimer += dt;

            // Hold velocity at zero in case some other system nudged it.
            c?.StopMovement();

            // SpellCaster missing or invalid configuration → fall through
            // immediately so the NPC doesn't get stuck. Defensive only;
            // ConfigureMonsterAutoCast guarantees the component exists.
            if (_caster == null || _caster.CurrentPhase == SpellCaster.CastPhase.Ready
                                || _stateTimer >= MaxStateDuration)
            {
                ReturnToHostileState(fsm);
                return;
            }
        }

        public void Exit(StateMachine fsm) { }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void FacePlayer(FSMComponents c, StateMachine fsm)
        {
            if (c?.Animator == null) return;
            var player = EntityRegistry.Player;
            if (player == null) return;
            Vector2 toPlayer = (Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position;
            if (toPlayer.sqrMagnitude < 0.0001f) return;
            var dir = c.Animator.ResolveDirectionFromVector(toPlayer);
            c.Animator.SetState(DirectionalAnimator.AnimState.Cast, dir);
        }

        // After cooldown the NPC returns to the most appropriate hostile
        // state: Attack if the player is already inside melee range,
        // Chase otherwise. Patrol/Idle aren't valid here — the NPC was
        // aggro'd to begin a cast, so re-entering passive states would
        // ignore that aggression.
        private static void ReturnToHostileState(StateMachine fsm)
        {
            var player = EntityRegistry.Player;
            if (player == null)
            {
                fsm.ChangeState(new ChaseState());
                return;
            }

            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            float distSq = ((Vector2)player.transform.position - (Vector2)fsm.Owner.transform.position).sqrMagnitude;
            if (distSq <= meleeRange * meleeRange)
                fsm.ChangeState(new AttackState());
            else
                fsm.ChangeState(new ChaseState());
        }
    }
}
