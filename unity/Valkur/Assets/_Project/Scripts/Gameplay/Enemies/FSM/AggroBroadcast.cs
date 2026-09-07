using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The context keys one monster writes into another when it spots something.
    ///
    /// Kept beside <see cref="AggroBroadcast"/> rather than inside it so the writer and the
    /// two readers (<see cref="IdleState"/>, <see cref="PatrolState"/>) name the same
    /// constants — a shout nobody hears is indistinguishable from no shout at all, and a
    /// typo in a context key is exactly that.
    /// </summary>
    public static class FSMAlert
    {
        public const string KeyTime = "alert_time";
        public const string KeyX    = "alert_x";
        public const string KeyY    = "alert_y";

        /// <summary>
        /// How long a shout stays worth reacting to. Long enough to cross a room, short
        /// enough that a monster woken by an alert does not re-take it after it has already
        /// been investigated and abandoned.
        /// </summary>
        public const float ValidSeconds = 4f;

        /// <summary>True when this machine is holding an alert it has not consumed yet.</summary>
        public static bool IsPending(StateMachine fsm)
        {
            if (fsm == null) return false;
            float at = fsm.GetContextFloat(KeyTime, -1f);
            return at >= 0f && Time.time - at <= ValidSeconds;
        }

        /// <summary>The position that was shouted about. Meaningless unless
        /// <see cref="IsPending"/>.</summary>
        public static Vector2 Position(StateMachine fsm)
            => new Vector2(fsm.GetContextFloat(KeyX), fsm.GetContextFloat(KeyY));

        /// <summary>
        /// Marks the alert as acted upon. A consumed alert must not be re-read, or a monster
        /// that investigates and returns to patrol re-enters the alert on its next tick and
        /// never patrols again.
        /// </summary>
        public static void Consume(StateMachine fsm)
        {
            if (fsm == null) return;
            fsm.SetContext(KeyTime, -1f);
        }

        /// <summary>Writes an alert into one machine. Public because tests drive it directly.</summary>
        public static void Raise(StateMachine fsm, Vector2 position)
        {
            if (fsm == null) return;
            fsm.SetContext(KeyTime, Time.time);
            fsm.SetContext(KeyX, position.x);
            fsm.SetContext(KeyY, position.y);
        }
    }

    /// <summary>
    /// One monster spotting the player tells the monsters around it.
    ///
    /// <para>Before this, a pack converging on the player was twenty monsters that happened
    /// to agree: each acquired independently, at its own aggro radius, with no notion that
    /// anything else in the room had already seen anyone. Grepping the project for aggro
    /// sharing, threat, reinforcement or squads returned nothing at all.</para>
    ///
    /// <para><b>The shout writes DATA, never a state.</b> Changing another entity's
    /// <see cref="StateMachine"/> from outside means running its <c>Enter</c> in the middle
    /// of the shouter's tick, against a machine that may be mid-swing, mid-flinch or dead —
    /// and it puts a <c>ChangeState</c> call in a file the built-in-transition census does
    /// not read, so the graph in F12 would be missing an edge that really fires. Instead the
    /// listener stores an alert and each brain decides on its OWN tick, which is also what
    /// makes the reaction obey the set's allowed-state guard.</para>
    ///
    /// <para>Line of sight is deliberately NOT required between shouter and listener: this
    /// is a shout, and a wall does not stop one. Distance does, and so does the listener
    /// having no <c>AlertChaseState</c> in its vocabulary — which is what keeps a vendor
    /// from being recruited into a fight by the monster outside her shop.</para>
    /// </summary>
    public static class AggroBroadcast
    {
        /// <summary>
        /// Tell every eligible monster within <paramref name="radius"/> of
        /// <paramref name="source"/> that something was spotted at
        /// <paramref name="targetPosition"/>. Returns how many were told, which is what the
        /// tests assert on and what the debug overlay reports.
        /// </summary>
        public static int Alert(GameObject source, Vector2 targetPosition, float radius)
        {
            if (source == null || radius <= 0f) return 0;

            var monsters = EntityRegistry.Monsters;
            if (monsters == null || monsters.Count == 0) return 0;

            bool sourceIsAllied = AlliedUnit.IsAllied(source);
            Vector2 from = source.transform.position;
            float radiusSq = radius * radius;
            int told = 0;

            for (int i = 0; i < monsters.Count; i++)
            {
                var other = monsters[i];
                if (other == null || other == source || !other.activeInHierarchy) continue;

                // Never recruit the other side. An ally shouting would otherwise summon the
                // monsters it is fighting, and a monster shouting would summon the summons.
                if (AlliedUnit.IsAllied(other) != sourceIsAllied) continue;

                if (((Vector2)other.transform.position - from).sqrMagnitude > radiusSq) continue;

                var brain = other.GetComponent<FSMMonsterBrain>();
                var fsm = brain != null ? brain.FSM : null;
                if (fsm == null) continue;

                // A set that cannot enter AlertChaseState must not be handed an alert: the
                // guard would refuse the transition on the listener's next tick and warn,
                // once per From>To pair, for a message the author cannot act on.
                if (!fsm.IsStateAllowed(nameof(AlertChaseState))) continue;

                // Already engaged, or dead. Re-alerting a monster mid-chase would reset it
                // to investigating a position it has long since passed.
                if (!IsAlertable(fsm)) continue;

                FSMAlert.Raise(fsm, targetPosition);
                told++;
            }

            return told;
        }

        /// <summary>
        /// Only a monster that is not already doing something about the player. Idle,
        /// patrolling and strolling are the three resting states; everything else is either
        /// already committed or is a corpse.
        /// </summary>
        private static bool IsAlertable(StateMachine fsm)
        {
            var s = fsm.CurrentState;
            return s is IdleState || s is PatrolState || s is StrollState;
        }
    }
}
