using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Unconscious state: entity is at 0 HP, plays death animation, then transitions to DeathState.
    /// Maps to Python's UnconsciousState with configurable disappear timer.
    /// </summary>
    public class UnconsciousState : IState
    {
        private float _timer;
        private float _disappearTime;

        public void Enter(StateMachine fsm)
        {
            _timer = 0f;
            _disappearTime = fsm.GetContextFloat("death_disappear_time", 10f);

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Rb != null)
            {
                c.Rb.velocity = Vector2.zero;
                // Stop physics on the corpse so collisions don't shove it around
                // during the despawn window. Static body keeps the transform fixed
                // without paying for kinematic integration every frame.
                c.Rb.bodyType = RigidbodyType2D.Static;
            }

            CleanupForCorpse(fsm.Owner);
        }

        // Tear down everything the player can interact with while the corpse is
        // visible: solid colliders that would block movement, world-UI bars that
        // would still render an empty fill above the body, and combat components
        // that could keep firing on a dead entity. We deliberately leave the
        // SpriteRenderer + DirectionalAnimator + GrayscaleDeath alone — those are
        // the corpse's visual story for the despawn window.
        private static void CleanupForCorpse(GameObject owner)
        {
            if (owner == null) return;

            // Disable every Collider2D in the hierarchy. Triggers used for hit-
            // detection and the body collider that walls the player out are both
            // killed; an inert corpse should not block pathing.
            var colliders = owner.GetComponentsInChildren<Collider2D>(includeInactive: false);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }

            // World-UI bars (health, mana, dash) auto-hide on full HP / IsDead but
            // belt-and-braces: we explicitly disable each one's component so the
            // SpriteRenderers parented under "HealthBar"/"ManaBar"/"DashBar" stop
            // rendering immediately even if a stale subscription would otherwise
            // re-show them.
            DisableComponent(owner.GetComponent<WorldHealthBar>());
            DisableComponent(owner.GetComponent<WorldManaBar>());
            DisableComponent(owner.GetComponent<WorldDashBar>());

            // Hide the actual bar children (the "HealthBar"/"ManaBar"/"DashBar"
            // child GameObjects that hold the SpriteRenderers). Disabling the
            // MonoBehaviour above stops Update; this stops the renderers.
            HideChild(owner.transform, "HealthBar");
            HideChild(owner.transform, "ManaBar");
            HideChild(owner.transform, "DashBar");

            // Stop any combat components from firing while the corpse is visible.
            DisableComponent(owner.GetComponent<MeleeCombat>());
            DisableComponent(owner.GetComponent<NPCAutoCast>());
        }

        private static void DisableComponent(Behaviour b)
        {
            if (b != null) b.enabled = false;
        }

        private static void HideChild(Transform parent, string childName)
        {
            if (parent == null) return;
            var child = parent.Find(childName);
            if (child != null) child.gameObject.SetActive(false);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            _timer += dt;
            if (_timer >= _disappearTime)
            {
                fsm.ChangeState(new DeathState());
            }
        }

        public void Exit(StateMachine fsm) { }
    }
}
