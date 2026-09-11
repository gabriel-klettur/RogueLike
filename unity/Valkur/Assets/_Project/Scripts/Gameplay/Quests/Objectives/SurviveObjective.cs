using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Hold out for N seconds." The counter IS the elapsed whole seconds, so the
    /// quest log reads "34 / 60" with no extra formatting.
    ///
    /// <para>Polled, because a clock has no event to subscribe to. It accumulates
    /// <c>Time.deltaTime</c> inside <see cref="Poll"/> rather than reading
    /// <c>Time.time</c> against a stored start: the vigil has to STOP while the
    /// player is dead, and a wall-clock difference cannot express that — a player
    /// who died at second 10 of a 60-second watch would come back to find it
    /// finished itself while they were a ghost.</para>
    ///
    /// <para>Dying does not RESET it either, and that is a deliberate softening: a
    /// reset would make a long vigil a memory test of the last attempt, and the
    /// death already cost the player everything <c>DeathTuning</c> charges.</para>
    /// </summary>
    public sealed class SurviveObjective : ObjectiveBase
    {
        public override bool IsPollable => true;

        private float _elapsed;

        public SurviveObjective(string id, string description, int seconds)
            : base(id, description, seconds)
        {
        }

        public override void Poll()
        {
            if (IsComplete) return;
            if (IsPlayerDeadOrAbsent()) return;

            _elapsed += Time.deltaTime;
            SetCurrent(Mathf.FloorToInt(_elapsed));
        }

        /// <summary>
        /// True while there is nobody alive to be surviving. Reads <c>Health</c>
        /// rather than <c>DeathSequenceController</c>'s phase so the objective stays
        /// in <c>Valkur.Gameplay</c>'s combat vocabulary and works for a fixture
        /// that builds a bare player.
        /// </summary>
        private static bool IsPlayerDeadOrAbsent()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return true;
            var health = player.GetComponent<Health>();
            return health != null && health.IsDead;
        }

        /// <summary>
        /// Restoring from a save has to put the FLOAT back too, or the first poll
        /// after a reload rounds the accumulated seconds away and the vigil restarts
        /// from whatever whole second the save happened to catch.
        /// </summary>
        public void RestoreElapsed(int seconds)
        {
            _elapsed = Mathf.Max(0, seconds);
            RestoreProgress(seconds);
        }
    }
}
