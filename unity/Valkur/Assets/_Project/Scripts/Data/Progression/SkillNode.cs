using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One talent of a class's skill tree: a permanent, purely NUMERIC upgrade to the
    /// character, bought with skill points.
    ///
    /// A skill node never grants a new verb. Unlocking a spell is the grimoire's job
    /// (<see cref="SpellNode"/>), and the split is not cosmetic — see
    /// <see cref="SkillTree"/> for why the two progressions are separate assets with
    /// separate currencies.
    ///
    /// Ranks are the reason this is scalable. A tree of 40 single-purchase nodes needs
    /// 40 assets, 40 icons and 40 descriptions to express what 8 five-rank nodes say
    /// better, and a rank gives the player a dial ("three more points here or one point
    /// there") rather than a checklist. <see cref="modifiersPerRank"/> is multiplied by
    /// the rank held, so one authored row describes every step.
    ///
    /// Why the effects are a flat <see cref="StatModifier"/> array rather than an
    /// inheritance hierarchy: a tree with 100+ nodes built from subclasses balloons into
    /// 100 separate asset files tracked by GUID. The flat shape keeps a node one asset
    /// and makes copy / rename / data-driven overlays straightforward.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillNode", menuName = "Valkur/Progression/Skill Node")]
    public sealed class SkillNode : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used for save persistence. NEVER rename after release " +
                 "or every save that learned this node breaks.")]
        public string skillId;

        [Tooltip("UI label.")]
        public string displayName;

        [TextArea(2, 5)]
        [Tooltip("Flavour text. The mechanical effect is generated from modifiersPerRank " +
                 "rather than written here, so a tuning change can never leave the " +
                 "description lying about what the node does.")]
        public string description;

        public Sprite icon;

        [Header("Cost")]
        [Tooltip("Skill points needed for ONE rank of this node.")]
        [Min(1)] public int pointCost = 1;

        [Tooltip("How many times this node can be bought. 1 = a single-purchase node.")]
        [Min(1)] public int maxRank = 1;

        [Tooltip("Player level required to buy the FIRST rank. 0 = no gate.")]
        [Min(0)] public int levelRequirement;

        [Tooltip("Extra player levels required per rank beyond the first. 0 = every rank " +
                 "is available as soon as the node is. Used to pace a capstone across " +
                 "the levelling curve instead of letting it be maxed the moment it opens.")]
        [Min(0)] public int levelPerRank;

        [Header("Prerequisites")]
        [Tooltip("Nodes that must be at their max rank before this one opens. " +
                 "Empty = a root node, available as soon as levelRequirement is met.")]
        public SkillNode[] prerequisites = Array.Empty<SkillNode>();

        [Header("Effects")]
        [Tooltip("Applied once PER RANK held. A +5 Max HP entry on a 5-rank node is " +
                 "+25 Max HP at full rank.")]
        public StatModifier[] modifiersPerRank = Array.Empty<StatModifier>();

        [Tooltip("AuraRegistry keys applied when the node reaches rank 1. Auras are not " +
                 "scaled by rank — an aura either exists on the character or it does not — " +
                 "so a node that wants a scaling aura should also carry the stat modifiers " +
                 "that scale it.")]
        public string[] passiveAuras = Array.Empty<string>();

        [Header("Layout")]
        [Tooltip("Row in the tree view. Lower is nearer the root.")]
        public int row;

        [Tooltip("Column in the tree view.")]
        public int column;

        /// <summary>Player level needed for <paramref name="rank"/> (1-based).</summary>
        public int LevelRequirementForRank(int rank)
            => levelRequirement + Mathf.Max(0, rank - 1) * levelPerRank;

        /// <summary>
        /// Every modifier this node contributes at <paramref name="rank"/>, with the
        /// per-rank values already multiplied out. Returns an empty array at rank 0 so
        /// callers never have to special-case "not learned".
        /// </summary>
        public StatModifier[] ModifiersAtRank(int rank)
        {
            if (rank <= 0 || modifiersPerRank == null || modifiersPerRank.Length == 0)
                return Array.Empty<StatModifier>();

            var result = new StatModifier[modifiersPerRank.Length];
            for (int i = 0; i < modifiersPerRank.Length; i++)
            {
                var m = modifiersPerRank[i];
                result[i] = new StatModifier(m.stat, m.op, m.value * rank);
            }
            return result;
        }

        /// <summary>
        /// Generated mechanical summary, e.g. "+10 Max HP, +5% Melee Damage". Generated
        /// rather than authored for the reason <see cref="description"/> records: a
        /// hand-written effect line is a second source of truth that silently rots the
        /// first time someone retunes the node.
        /// </summary>
        public string DescribeRank(int rank)
        {
            var mods = ModifiersAtRank(Mathf.Max(1, rank));
            if (mods.Length == 0)
                return passiveAuras != null && passiveAuras.Length > 0 ? "Passive effect" : string.Empty;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < mods.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(mods[i].Describe());
            }
            return sb.ToString();
        }
    }
}
