using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// NPC idle state. Stands still, watches for something worth chasing, and reacts to a
    /// neighbour's shout.
    /// </summary>
    public class IdleState : IState
    {
        public void Enter(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
            if (c?.Animator != null)
                c.Animator.SetState(DirectionalAnimator.AnimState.Idle, c.Animator.CurrentDirection);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            // Somebody shouted. Investigating what a neighbour saw beats standing here, and
            // it is checked BEFORE perception so a monster facing the wrong way still joins
            // the fight — which is the entire reason the shout exists.
            if (FSMAlert.IsPending(fsm) && fsm.IsStateAllowed(nameof(AlertChaseState)))
            {
                fsm.ChangeState(new AlertChaseState());
                return;
            }

            // Range, then the field of view, then line of sight — all three live in
            // FSMPerception so that this state, PatrolState and SearchState cannot drift
            // apart on what "spotted" means. They already had: this one asked IsBlocked,
            // PatrolState asked IsClear, and only one of them checked the target was alive.
            if (!FSMPerception.TryAcquire(fsm, out var target)) return;

            OnAcquired(fsm, target);
            fsm.ChangeState(new ChaseState());
        }

        /// <summary>
        /// What every acquisition does besides changing state: remember where the target was
        /// (so losing it later has somewhere to search) and tell the neighbours.
        ///
        /// Shared by <see cref="IdleState"/> and <see cref="PatrolState"/> because an
        /// acquisition that only shouted from one of them would make a pack's behaviour
        /// depend on which resting state its members happened to be in.
        /// </summary>
        public static void OnAcquired(StateMachine fsm, GameObject target)
        {
            if (target == null) return;

            Vector2 seenAt = target.transform.position;
            FSMTargetMemory.Remember(fsm, seenAt);
            AggroBroadcast.Alert(fsm.Owner, seenAt, FSMTuning.AggroShareRadius(fsm));
        }

        public void Exit(StateMachine fsm) { }
    }
}
