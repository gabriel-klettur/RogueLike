using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Flee state: runs away from player when HP is low.
    /// Maps to Python's FleeState (triggered at 30% HP in AggroState).
    /// </summary>
    public class FleeState : IState
    {
        private float _fleeTimer;
        private const float FLEE_DURATION = 3f;

        public void Enter(StateMachine fsm)
        {
            _fleeTimer = 0f;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var health = fsm.Owner.GetComponent<Health>();
            if (health != null && health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _fleeTimer += dt;
            if (_fleeTimer >= FLEE_DURATION)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var player = EntityRegistry.Player;
            if (player == null)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 playerPos = player.transform.position;
            Vector2 fleeDir = (myPos - playerPos).normalized;

            float speed = fsm.GetContextFloat("speed", 2f) * 1.5f;
            var rb = fsm.Owner.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.velocity = fleeDir * speed;

            var sr = fsm.Owner.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
                sr.flipX = fleeDir.x < 0;
        }

        public void Exit(StateMachine fsm)
        {
            var rb = fsm.Owner.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
        }
    }
}
