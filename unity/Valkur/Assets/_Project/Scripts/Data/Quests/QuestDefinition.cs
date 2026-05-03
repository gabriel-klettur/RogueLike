using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Designer-authored quest blueprint. Carries identity (questId,
    /// displayName, description) and a list of objectives expressed as
    /// flat (kind, target id, count) tuples. The QuestManager translates
    /// each objective entry into a concrete <c>IObjective</c> at runtime.
    ///
    /// Why a flat tuple list and not a polymorphic SO graph: a quest with
    /// 3 objectives would otherwise need 4 SOs (the quest + one per
    /// objective). For 50 quests that's 200 assets to author and track.
    /// The flat shape keeps everything in a single asset file per quest.
    ///
    /// Reward fields (xpReward, skillPointReward, itemRewards) are
    /// designer hints — the QuestManager consumes them on completion to
    /// fire the matching gameplay events. Empty fields are silent skips.
    /// </summary>
    [CreateAssetMenu(fileName = "NewQuest", menuName = "Valkur/Data/Quest Definition")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used by save persistence and quest log lookups. " +
                 "NEVER rename after release — every save in flight references this.")]
        public string questId;

        public string displayName;

        [TextArea(2, 6)]
        public string description;

        [Header("Objectives")]
        [Tooltip("Each entry produces one IObjective at runtime. The quest " +
                 "completes when ALL objectives complete (AND-semantics).")]
        public ObjectiveEntry[] objectives = Array.Empty<ObjectiveEntry>();

        [Header("Rewards")]
        public int xpReward;
        public int skillPointReward;
        [Tooltip("Item ids granted on completion. Resolved against ItemDefinition assets " +
                 "by id at runtime so a renamed item breaks loudly instead of silently dropping.")]
        public string[] itemRewards = Array.Empty<string>();
    }

    [Serializable]
    public struct ObjectiveEntry
    {
        public ObjectiveKind kind;

        [Tooltip("Kind-specific target id. KillCount: monsterKey to filter by " +
                 "(empty = any non-player). Reach / Collect: not yet implemented.")]
        public string targetId;

        [Tooltip("Required count to complete this objective.")]
        [Min(1)] public int count;

        [Tooltip("Optional player-facing description override. Empty = auto-generate.")]
        public string description;
    }

    public enum ObjectiveKind
    {
        KillCount,
        // Future: Reach (location), Collect (item), Talk (NPC), Survive (time).
        // Each kind grows the QuestManager.BuildObjective dispatcher.
    }
}
