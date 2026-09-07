using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// "I saw you go behind that wall." The monster walks to the last place it had line of
    /// sight on its target, looks around, and gives up.
    ///
    /// <para>WHY THIS STATE EXISTS. Sight was checked on ACQUISITION only:
    /// <see cref="ChaseState"/>'s exits were distance, death and the leash, so once a monster
    /// committed, geometry stopped mattering entirely — it tracked the player through walls,
    /// around corners and across a building, and breaking line of sight was not a mechanic
    /// the player had. The two obvious alternatives are both worse than this state and both
    /// read as the AI being broken: keep chasing forever (what shipped), or drop the target
    /// the frame the raycast fails, which makes every doorway a hard reset.</para>
    ///
    /// <para>The search is deliberately DUMB — walk there, look around, leave. A monster that
    /// searched intelligently (sweeping cover, cutting off exits) would be a different game;
    /// what this buys is that hiding works, and that it visibly works, because the monster
    /// goes to the right place and is seen to lose you.</para>
    /// </summary>
    public class SearchState : IState
    {
        private readonly FSMPathFollower _path = new FSMPathFollower();
        private Vector2 _goal;
        private float _timer;
        private float _lookTimer;
        private bool _arrived;

        /// <summary>How close counts as having reached the last known position.</summary>
        private const float ArriveDistance = 0.6f;

        /// <summary>Seconds between the turns of the look-around.</summary>
        private const float LookInterval = 0.9f;

        /// <summary>
        /// Investigating is done at walking pace, not at chase speed. The monster has lost
        /// its target: sprinting to an empty spot reads as still knowing where you are, which
        /// is the exact impression this state exists to remove.
        /// </summary>
        private const float SearchSpeedFactor = 1f;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _lookTimer = 0f;
            _arrived = false;
            _path.Reset();

            // An alert (a nearby monster shouting) and a lost sighting are the same fact to
            // this state — somewhere to go and look. The alert wins when it is fresher.
            _goal = FSMTargetMemory.Position(fsm);
            if (FSMAlert.IsPending(fsm))
            {
                _goal = FSMAlert.Position(fsm);
                FSMAlert.Consume(fsm);
            }
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

            // Found them again. Acquisition runs the full perception test, so re-finding the
            // target obeys the same range, cone and line-of-sight rules as spotting it the
            // first time — the search cannot see through the wall it is walking around.
            if (FSMPerception.TryAcquire(fsm, out var found))
            {
                // Through the SHARED acquisition hook, not straight into Chase. This state
                // used to be the one place that re-found a target and told nobody: it neither
                // shouted nor refreshed the last-known position, so a monster that lost the
                // player, hunted them down and caught them again was the only one in the pack
                // that knew — and if it then lost them a second time it searched the position
                // from the FIRST sighting, which it had already walked to and abandoned.
                IdleState.OnAcquired(fsm, found);
                fsm.ChangeState(new ChaseState());
                return;
            }

            if (_timer >= FSMTuning.SearchDuration(fsm))
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            if (!_arrived && (myPos - _goal).sqrMagnitude <= ArriveDistance * ArriveDistance)
                _arrived = true;

            if (_arrived)
            {
                LookAround(c, dt);
                return;
            }

            float speed = fsm.GetContextFloat("speed", 2f) * SearchSpeedFactor;
            Vector2 moveDir = _path.Steer(fsm, myPos, _goal, dt);

            c?.SetVelocity(moveDir * speed);
            if (c?.Animator != null && moveDir.sqrMagnitude > 0.0001f)
            {
                var dir = c.Animator.ResolveDirectionFromVector(moveDir);
                c.Animator.SetState(DirectionalAnimator.AnimState.Walk, dir);
            }
        }

        /// <summary>
        /// Stand still and turn. The turn is what makes the search READ as a search — a
        /// monster standing motionless at the spot it lost you looks like a monster that has
        /// stopped working, and the animator's facing is also what the field-of-view test
        /// consults, so this genuinely sweeps for the target rather than miming it.
        /// </summary>
        private void LookAround(FSMComponents c, float dt)
        {
            c?.StopMovement();
            if (c?.Animator == null) return;

            _lookTimer += dt;
            if (_lookTimer < LookInterval) return;
            _lookTimer = 0f;

            // Three steps round the compass, so consecutive turns are never a mirror of each
            // other — alternating between two facings reads as a twitch, not as looking.
            var next = (DirectionalAnimator.Direction)
                (((int)c.Animator.CurrentDirection + 3) % 8);
            c.Animator.SetState(DirectionalAnimator.AnimState.Idle, next);
        }

        public void Exit(StateMachine fsm)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            c?.StopMovement();
        }
    }
}
