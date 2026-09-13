using System.Text;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The fields the Properties panel used not to have.
    ///
    /// <para>Audited 2026-09-12: <b>nine of nineteen <c>MonsterDefinition</c> fields had no UI at
    /// all</b>, and three of those had just been created by other audits for exactly this kind of
    /// authoring — <c>aiTuning</c> (nineteen fields of its own: the dodge, the standoff, the field
    /// of view, the leash), <c>coinReward</c> (the coin faucet of the whole economy, added because
    /// there had been none), and <c>levelHpGrowth</c>. <c>EntityStats</c> was missing another
    /// eight, including <c>resistances</c> and <c>statusImmunities</c>, which decide whether an
    /// elemental spell does anything to this monster at all.</para>
    ///
    /// <para>An editor whose job is authoring entities and whose answer to twenty of their knobs
    /// is "open the Inspector" is not authoring them.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>
        /// Reward and level rows — the two dials that are NOT the same question.
        ///
        /// <para><c>xpReward</c> is a DIFFICULTY knob (how much a kill is worth as progress) and
        /// <c>coinReward</c> is a WEALTH knob. They are deliberately independent, so tying one to
        /// the other would make every XP retune a silent economy retune; showing them side by
        /// side is what makes that visible rather than merely documented.</para>
        /// </summary>
        private void FillRewardSection(MonsterDefinition def)
        {
            AddIntStat(_ui.PropsRewardSection, "Level", def.level, 1, v => def.level = v, def);
            AddFloatStat(_ui.PropsRewardSection, "HP / lvl (frac)", def.levelHpGrowth, 0f,
                         v => def.levelHpGrowth = v, def);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsRewardSection, "Level curve",
                def.levelScaling != null ? def.levelScaling.name : "(none — uses HP / lvl)");

            AddIntStat(_ui.PropsRewardSection, "XP", def.xpReward, 0, v => def.xpReward = v, def);

            // -1 pays nothing, 0 is the heuristic, >0 is literal. The same three-way contract
            // xpReward uses, and the reason the hint says so: a 0 that silently means "compute
            // one" is indistinguishable from a 0 that means "none" without it.
            AddIntStat(_ui.PropsRewardSection, "Coins (-1 none, 0 auto)", def.coinReward, -1,
                       v => def.coinReward = v, def);

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsRewardSection, "Loot table",
                def.lootTable != null ? def.lootTable.name : "(none)");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsRewardSection, "Chat persona",
                def.chatPersona != null ? def.chatPersona.name : "(none)");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsRewardSection, "Vendor config",
                def.vendorConfig != null ? def.vendorConfig.name : "(none)");
        }

        /// <summary>
        /// The AI tuning block, grouped the way the behaviour groups.
        ///
        /// <para>Nineteen fields in one flat list is a wall, so they are ordered by the decision
        /// they belong to — acquiring, chasing, standing off, fleeing, dodging — which is also the
        /// order the FSM asks them in.</para>
        /// </summary>
        private void FillAITuningSection(MonsterDefinition def)
        {
            var s = _ui.PropsAITuningSection;

            // Acquiring.
            AddFloatStat(s, "Aggro exit hyst.", def.aiTuning.aggroExitHysteresis, 0f,
                         v => def.aiTuning.aggroExitHysteresis = v, def);
            // 360 is the historical omniscient default, and it is a real choice rather than an
            // absence: something fast that also sees behind itself has no counter but out-damaging
            // it, and the cone is what makes flanking work.
            AddFloatStat(s, "FOV degrees (360 = all)", def.aiTuning.fovDegrees, 0f,
                         v => def.aiTuning.fovDegrees = Mathf.Clamp(v, 0f, 360f), def);
            AddFloatStat(s, "Sight memory s", def.aiTuning.sightMemorySeconds, 0f,
                         v => def.aiTuning.sightMemorySeconds = v, def);
            AddFloatStat(s, "Search duration s", def.aiTuning.searchDuration, 0f,
                         v => def.aiTuning.searchDuration = v, def);
            AddFloatStat(s, "Aggro share radius", def.aiTuning.aggroShareRadius, 0f,
                         v => def.aiTuning.aggroShareRadius = v, def);
            AddFloatStat(s, "Alert duration s", def.aiTuning.alertDuration, 0f,
                         v => def.aiTuning.alertDuration = v, def);

            // Chasing.
            AddFloatStat(s, "Leash range", def.aiTuning.leashRange, 0f,
                         v => def.aiTuning.leashRange = v, def);
            AddFloatStat(s, "Repath interval s", def.aiTuning.repathInterval, 0f,
                         v => def.aiTuning.repathInterval = v, def);
            AddFloatStat(s, "Waypoint reach", def.aiTuning.waypointReachDistance, 0f,
                         v => def.aiTuning.waypointReachDistance = v, def);

            // Standing off. Zero means melee and is every melee monster unchanged.
            AddFloatStat(s, "Desired range (0 = melee)", def.aiTuning.desiredRange, 0f,
                         v => def.aiTuning.desiredRange = v, def);
            AddFloatStat(s, "Reswing range factor", def.aiTuning.reswingRangeFactor, 0f,
                         v => def.aiTuning.reswingRangeFactor = v, def);

            // Fleeing.
            AddFloatStat(s, "Flee duration s", def.aiTuning.fleeDuration, 0f,
                         v => def.aiTuning.fleeDuration = v, def);
            AddFloatStat(s, "Flee speed x", def.aiTuning.fleeSpeedMultiplier, 0f,
                         v => def.aiTuning.fleeSpeedMultiplier = v, def);
            AddFloatStat(s, "Regroup s", def.aiTuning.regroupSeconds, 0f,
                         v => def.aiTuning.regroupSeconds = v, def);

            // Dodging. Opt-in twice: a zero chance returns on FSMDodge's first line, and the
            // FSM set must also declare DodgeState -- no amount of tuning makes a barbol dodge.
            AddFloatStat(s, "Dodge chance 0..1", def.aiTuning.dodgeChance, 0f,
                         v => def.aiTuning.dodgeChance = Mathf.Clamp01(v), def);
            AddFloatStat(s, "Dodge cooldown s", def.aiTuning.dodgeCooldownSeconds, 0f,
                         v => def.aiTuning.dodgeCooldownSeconds = v, def);
            AddFloatStat(s, "Dodge threat radius", def.aiTuning.dodgeThreatRadius, 0f,
                         v => def.aiTuning.dodgeThreatRadius = v, def);
            // Sized in DISTANCE, not seconds: a duration authored beside a speed is two numbers
            // that must agree, and retuning chasingSpeed would silently double the sidestep.
            AddFloatStat(s, "Dodge distance", def.aiTuning.dodgeDistance, 0f,
                         v => def.aiTuning.dodgeDistance = v, def);
            AddFloatStat(s, "Dodge speed x", def.aiTuning.dodgeSpeedMultiplier, 0f,
                         v => def.aiTuning.dodgeSpeedMultiplier = v, def);

            // Asked of the FSM layer, not guessed from the set's NAME. A refused ChangeState is
            // silent, so a dodgeChance turned up on a set with no DodgeState does nothing at all
            // and looks authored -- and a warning derived from a name would be its own heuristic,
            // which is the defect this editor's category tabs already had.
            bool allows = Valkur.Gameplay.Enemies.FSM.FSMRuntimeFactory.AllowsState(
                def.monsterKey, def.fsmSet, "DodgeState", out bool known);
            if (known && !allows && def.aiTuning.dodgeChance > 0f)
                EntitiesEditorUIBuilder.AddPropertyRow(s, "!",
                    "This monster's FSM set does not declare DodgeState, so the dodge dials " +
                    "above do nothing. Add the node in the FSM editor.");
        }

        /// <summary>
        /// The eight <c>EntityStats</c> fields the panel never showed, plus the two lists.
        /// </summary>
        private void FillExtraStats(MonsterDefinition def)
        {
            var s = _ui.PropsStatsSection;
            AddFloatStat(s, "Damage stun s", def.stats.damageDuration, 0f,
                         v => def.stats.damageDuration = v, def);
            AddFloatStat(s, "Stun chance 0..1", def.stats.damageStopProbability, 0f,
                         v => def.stats.damageStopProbability = Mathf.Clamp01(v), def);
            AddFloatStat(s, "Corpse despawn s", def.stats.deathDisappearTime, 0f,
                         v => def.stats.deathDisappearTime = v, def);
            AddFloatStat(s, "Feet width x", def.stats.feetWidthFactor, 0f,
                         v => def.stats.feetWidthFactor = v, def);
            AddFloatStat(s, "Feet height x", def.stats.feetHeightFactor, 0f,
                         v => def.stats.feetHeightFactor = v, def);
            // Live since the chat layer: EntitySetup.ConfigureChat reads it as the fallback
            // behind NPCPersonaDefinition.chatRange.
            AddFloatStat(s, "Chat range", def.stats.chatRange, 0f,
                         v => def.stats.chatRange = v, def);

            EntitiesEditorUIBuilder.AddPropertyRow(s, "Resistances", DescribeResistances(def.stats));
            EntitiesEditorUIBuilder.AddPropertyRow(s, "Immunities", DescribeImmunities(def.stats));
        }

        private static string DescribeResistances(EntityStats stats)
        {
            if (stats.resistances == null || stats.resistances.Length == 0) return "(none)";
            var sb = new StringBuilder();
            for (int i = 0; i < stats.resistances.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(stats.resistances[i].element).Append(' ')
                  .Append((stats.resistances[i].multiplier * 100f).ToString("0")).Append('%');
            }
            return sb.ToString();
        }

        private static string DescribeImmunities(EntityStats stats)
        {
            if (stats.statusImmunities == null || stats.statusImmunities.Length == 0) return "(none)";
            var sb = new StringBuilder();
            for (int i = 0; i < stats.statusImmunities.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(stats.statusImmunities[i]);
            }
            return sb.ToString();
        }

    }
}
