using Valkur.Core;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Skills;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Reach N % in a gathering skill." The counter is the skill's current whole percent, so
    /// the log reads "12 / 15".
    ///
    /// <para>Polled, for the reason <see cref="ReachLevelObjective"/> is: a player who is already
    /// past the mark when the quest is accepted never gains it again, so waiting for a gain event
    /// would leave the objective at zero forever.</para>
    /// </summary>
    public sealed class ReachSkillObjective : ObjectiveBase
    {
        public string SkillKey { get; }

        public override bool IsPollable => true;

        public ReachSkillObjective(string id, string description, int percent, string skillKey)
            : base(id, description, percent)
        {
            SkillKey = skillKey ?? string.Empty;
        }

        public override void Poll()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            var skills = PlayerSkills.Peek(player.gameObject);
            SetCurrent(skills != null ? skills.GetTenths(SkillKey) / 10 : 0);
        }
    }
}
