using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Be level N." The counter is the player's CURRENT level, so the log reads
    /// "7 / 10" and tells the player how far they still are rather than how many
    /// levels they have gained since accepting.
    ///
    /// <para>Polled instead of listening to <c>GameEvents.OnLevelUp</c>, and the
    /// reason is the restore path: a player who is already level 12 when the quest
    /// is accepted never levels up again in this quest's lifetime, so an
    /// event-driven version would sit at 0 forever waiting for something that has
    /// already happened. Asking the level is the only formulation that is correct
    /// at both ends.</para>
    /// </summary>
    public sealed class ReachLevelObjective : ObjectiveBase
    {
        public override bool IsPollable => true;

        public ReachLevelObjective(string id, string description, int level)
            : base(id, description, level)
        {
        }

        public override void Poll()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            var xp = player.GetComponent<Experience>();
            if (xp == null) return;
            SetCurrent(xp.Level);
        }
    }
}
