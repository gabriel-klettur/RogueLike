using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Something loud happened here. The world's version of a shout.
    ///
    /// <para><b>Perception was entirely visual.</b> Range, a view cone and line of sight — so a
    /// monster facing a wall was deaf to a fireball going off behind it, and the only way to
    /// start a fight was to walk into somebody's field of view. That makes every approach the
    /// same approach: there is no such thing as being noisy, so there is no such thing as being
    /// quiet, and a player has no lever over when a fight begins.</para>
    ///
    /// <para><b>It reuses the ALERT, it does not add a second mechanism.</b> A noise raises
    /// exactly the alert <see cref="AggroBroadcast"/> raises, so it inherits every property that
    /// was already thought through: the listener acts on its OWN tick rather than having its
    /// state changed from outside, the alert is consumed so a monster cannot re-investigate the
    /// same event forever, it expires, and a set with no <c>AlertChaseState</c> never receives
    /// one — which is what keeps a vendor from investigating a battle. A parallel "hearing"
    /// pathway would have had to re-derive all four, and would have got one of them wrong.</para>
    ///
    /// <para><b>Only the far side hears you.</b> A noise is raised against entities that are the
    /// emitter's ENEMIES: a monster swinging at a player does not summon the rest of its own
    /// pack, which is what makes this a stealth lever rather than a global aggro button. The
    /// side test is <see cref="EntityFaction"/>, so a charmed monster's noise wakes the horde it
    /// used to belong to.</para>
    ///
    /// <para><b>Loudness is a RADIUS, not a probability.</b> A roll would make the same action
    /// sometimes audible and sometimes not from the same spot, which is the one thing a player
    /// cannot learn. A radius is a rule they can play against.</para>
    /// </summary>
    public static class NoiseEvents
    {
        /// <summary>A footstep-scale event. Deliberately short — walking is not a mistake.</summary>
        public const float LoudnessFootstep = 3f;

        /// <summary>A melee swing connecting, or a small spell. The everyday combat noise.</summary>
        public const float LoudnessSwing = 7f;

        /// <summary>A heavy spell going off. Loud enough to pull a neighbouring patrol.</summary>
        public const float LoudnessSpell = 11f;

        /// <summary>Something died. The loudest thing in an ordinary fight.</summary>
        public const float LoudnessDeath = 14f;

        /// <summary>
        /// Tell whoever is close enough, and on the other side, that something happened at
        /// <paramref name="position"/>.
        /// </summary>
        /// <param name="source">
        /// Who made the noise. Used for the side test and to avoid alerting the emitter itself;
        /// a null source is treated as coming from the player's side, which is what a world
        /// hazard or an unowned explosion should read as.
        /// </param>
        /// <returns>How many entities were told. Asserted by tests, reported by the overlay.</returns>
        public static int Emit(GameObject source, Vector2 position, float loudness)
        {
            if (loudness <= 0f) return 0;

            var monsters = EntityRegistry.Monsters;
            if (monsters == null || monsters.Count == 0) return 0;

            var sourceSide = source != null ? EntityFaction.SideOf(source) : FactionSide.PlayerSide;
            float radiusSq = loudness * loudness;
            int told = 0;

            for (int i = 0; i < monsters.Count; i++)
            {
                var listener = monsters[i];
                if (listener == null || listener == source || !listener.activeInHierarchy) continue;

                // DISTANCE FIRST, and the order is the whole cost of this method. A noise is
                // emitted on every swing and every cast, and the side test costs two
                // GetComponent calls per listener while the range test costs a subtraction —
                // so testing allegiance before proximity would pay for the expensive question
                // about every monster on the map to answer it for the handful in earshot.
                if (((Vector2)listener.transform.position - position).sqrMagnitude > radiusSq) continue;

                // Your own side does not investigate you.
                if (!EntityFaction.AreEnemies(sourceSide, EntityFaction.SideOf(listener))) continue;

                var brain = listener.GetComponent<FSMMonsterBrain>();
                var fsm = brain != null ? brain.FSM : null;
                if (fsm == null) continue;

                // Same two gates the shout uses, for the same reasons: a set that cannot enter
                // AlertChaseState must not be handed an alert it will only refuse and warn
                // about, and a monster already fighting must not be reset to investigating a
                // position it has long since passed.
                if (!fsm.IsStateAllowed(nameof(AlertChaseState))) continue;
                if (!IsAlertable(fsm)) continue;

                FSMAlert.Raise(fsm, position);
                told++;
            }

            return told;
        }

        /// <summary>Convenience for the common "the noise is where the emitter is" case.</summary>
        public static int EmitAt(GameObject source, float loudness)
            => source != null ? Emit(source, source.transform.position, loudness) : 0;

        /// <summary>
        /// Only a monster that is not already doing something about somebody. Mirrors
        /// <see cref="AggroBroadcast"/>'s rule rather than restating it loosely — the two must
        /// agree on what "resting" means or a noise and a shout would wake different monsters.
        /// </summary>
        private static bool IsAlertable(StateMachine fsm)
        {
            var s = fsm.CurrentState;
            return s is IdleState || s is PatrolState || s is StrollState;
        }
    }
}
