using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Provoked pursuit: a monster that was shot from out of range, or that was told by a
    /// neighbour, closes on the target for <c>alert_duration</c> seconds ignoring its aggro
    /// ring entirely. Reaching melee range hands over to <see cref="AttackState"/>; running
    /// out of time hands back to <see cref="PatrolState"/>.
    ///
    /// <para>It is unreachable from code — grep returns no <c>new AlertChaseState(</c> in any
    /// state class. The two ways in are the authored <c>t_any_alert</c> edge in
    /// <c>sets.json</c> and <see cref="AggroBroadcast"/>, which is why the alert is CONSUMED
    /// here: a shout that stayed pending would be re-taken by <see cref="PatrolState"/> the
    /// moment this state times out, and the monster would never patrol again.</para>
    /// </summary>
    public class AlertChaseState : IState
    {
        private float _timer;
        private readonly FSMPathFollower _path = new FSMPathFollower();

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _path.Reset();
            FSMAlert.Consume(fsm);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _timer += dt;
            if (_timer >= FSMTuning.AlertDuration(fsm))
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            if (c == null || !c.HasViableTarget(fsm))
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var target = c.Target(fsm);
            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 targetPos = target.transform.position;
            Vector2 delta = targetPos - myPos;

            // Seeing the target while alerted is worth remembering: if this pursuit times out
            // and something later sends the monster searching, the last sighting is the only
            // place it has to go.
            if (FSMPerception.HasLineOfSight(fsm.Owner, target))
                FSMTargetMemory.Remember(fsm, targetPos);

            // Reaching the target has to mean the same thing here as it does in ChaseState.
            // Without this branch an alerted monster sprinted to the player and then stood
            // inside melee range doing nothing for the whole alert window, because the only
            // exits from this state were death, the timer and losing the target. That made
            // t_any_alert — the highest-priority authored edge in the shipped data — lead to a
            // state that could not fight, so being shot from out of range turned a monster
            // passive for five seconds instead of provoking it. Tested BEFORE the movement
            // block for the same reason ChaseState tests it before its own: a monster already
            // in reach must not take another step.
            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            if (delta.sqrMagnitude <= meleeRange * meleeRange)
            {
                fsm.ChangeState(new AttackState());
                return;
            }

            // Same contract as ChaseState: chasing_speed is the speed, not a base to scale.
            // See the note there on the removed hidden 1.5 multiplier.
            float chaseSpeed = fsm.GetContextFloat("chasing_speed", 4.5f);
            Vector2 moveDir = _path.Steer(fsm, myPos, targetPos, dt);

            c.SetVelocity(moveDir * chaseSpeed);

            if (c.Animator != null && moveDir.sqrMagnitude > 0.0001f)
            {
                var dir = c.Animator.ResolveDirectionFromVector(moveDir);
                c.Animator.SetState(DirectionalAnimator.AnimState.Chase, dir);
            }
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }
    }
}
