using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Alert chase state: 5-second chase after taking ranged damage, ignores aggro range.
    /// Maps to Python's AlertChaseState.
    /// </summary>
    public class AlertChaseState : IState
    {
        private float _timer;
        private const float ALERT_DURATION = 5f;
        private const float CHASE_SPEED_MULTIPLIER = 1.5f;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var health = fsm.Owner.GetComponent<Health>();
            if (health != null && health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _timer += dt;
            if (_timer >= ALERT_DURATION)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            var playerHealth = player.GetComponent<Health>();
            if (playerHealth != null && playerHealth.IsDead)
            {
                fsm.ChangeState(new PatrolState());
                return;
            }

            Vector2 myPos = fsm.Owner.transform.position;
            Vector2 playerPos = player.transform.position;
            Vector2 delta = playerPos - myPos;

            float baseSpeed = fsm.GetContextFloat("chasing_speed", 3f);
            float chaseSpeed = baseSpeed * CHASE_SPEED_MULTIPLIER;
            var rb = fsm.Owner.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.velocity = delta.normalized * chaseSpeed;

            var sr = fsm.Owner.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
                sr.flipX = delta.x < 0;
        }

        public void Exit(StateMachine fsm)
        {
            var rb = fsm.Owner.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
        }
    }
}
