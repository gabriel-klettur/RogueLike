using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Designer-authored quest blueprint. Carries identity (questId,
    /// displayName, description), the offer/turn-in contract (who gives it,
    /// who closes it, what the player must already be) and a list of
    /// objectives expressed as flat (kind, target id, count) tuples. The
    /// QuestManager translates each objective entry into a concrete
    /// <c>IObjective</c> at runtime.
    ///
    /// <para>Why a flat tuple list and not a polymorphic SO graph: a quest with
    /// 3 objectives would otherwise need 4 SOs (the quest + one per
    /// objective). For 50 quests that's 200 assets to author and track.
    /// The flat shape keeps everything in a single asset file per quest.</para>
    ///
    /// <para>Reward fields are designer hints — the QuestManager consumes them on
    /// completion to fire the matching gameplay events. Empty fields are silent
    /// skips.</para>
    ///
    /// <para><b>The turn-in is not an objective the author writes.</b> Setting
    /// <see cref="turnInPersonaId"/> makes the manager APPEND a Talk objective
    /// after the authored ones, gated on all of them being complete — so
    /// "vuelve con Smith" appears in the log in the right place and cannot tick
    /// early by walking past him on the way out. Authoring it by hand would
    /// tick on the conversation that handed the quest over.</para>
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

        [Tooltip("One line the giver says when offering it. Shown in the offer list " +
                 "and posted into the conversation. Empty falls back to description.")]
        [TextArea(2, 4)]
        public string hookLine;

        [Tooltip("One line the turn-in character says when the quest closes. " +
                 "Empty = a generic acknowledgement.")]
        [TextArea(2, 4)]
        public string completionLine;

        [Header("Offer contract")]
        [Tooltip("personaId of the character who OFFERS this quest (see " +
                 "NPCPersonaDefinition.personaId). Empty = not offered by anyone; " +
                 "reachable only from the console or from another quest's chain.")]
        public string giverPersonaId;

        [Tooltip("personaId of the character the player must return to. Empty = " +
                 "the quest closes the instant its last authored objective does.")]
        public string turnInPersonaId;

        [Tooltip("Player level required before this quest is offered at all. " +
                 "0 = no requirement.")]
        [Min(0)] public int requiredLevel;

        [Tooltip("Suggested level, shown to the player. Purely informational — " +
                 "nothing gates on it, so a bold player can try anyway.")]
        [Min(0)] public int recommendedLevel;

        [Tooltip("questIds that must already be COMPLETED before this one is offered. " +
                 "This is what makes a quest line a line rather than a pile.")]
        public string[] prerequisiteQuestIds = Array.Empty<string>();

        [Tooltip("Free-text tag grouping a chain (e.g. 'fosa_roja'). Display only.")]
        public string questLine;

        [Header("Objectives")]
        [Tooltip("Each entry produces one IObjective at runtime. The quest " +
                 "completes when ALL objectives complete (AND-semantics).")]
        public ObjectiveEntry[] objectives = Array.Empty<ObjectiveEntry>();

        [Header("Rewards")]
        public int xpReward;
        public int skillPointReward;

        [Tooltip("Arcane points — the grimoire currency. Separate from skill points " +
                 "on purpose: a talent is a number, a spell is a verb.")]
        public int arcanePointReward;

        [Tooltip("Coins paid on completion. The quest layer is the first gold faucet " +
                 "in the project that is not a corpse.")]
        [Min(0)] public int coinReward;

        [Tooltip("Item ids granted on completion. Resolved against ItemDefinition assets " +
                 "by id at runtime so a renamed item breaks loudly instead of silently dropping.")]
        public string[] itemRewards = Array.Empty<string>();

        [Tooltip("How many of each item reward to grant. Index-aligned with itemRewards; " +
                 "a missing or non-positive entry means 1.")]
        public int[] itemRewardCounts = Array.Empty<int>();
    }

    [Serializable]
    public struct ObjectiveEntry
    {
        public ObjectiveKind kind;

        [Tooltip("Kind-specific target id:\n" +
                 "  KillCount  — monsterKey (empty = any non-player)\n" +
                 "  Collect    — itemId\n" +
                 "  Reach      — zone name, as ZoneManager reports it\n" +
                 "  Talk       — personaId\n" +
                 "  Craft      — recipeId (empty = any recipe)\n" +
                 "  CastSpell  — spellKey (empty = any spell)\n" +
                 "  Survive    — unused\n" +
                 "  ReachLevel — unused (count IS the level)\n" +
                 "  EarnCoins  — unused (count IS the amount)")]
        public string targetId;

        [Tooltip("Required count to complete this objective. For Survive it is SECONDS, " +
                 "for ReachLevel it is the level, for EarnCoins it is the purse total.")]
        [Min(1)] public int count;

        [Tooltip("Optional player-facing description override. Empty = auto-generate.")]
        public string description;

        [Tooltip("Collect only: take the items out of the bag when the quest closes. " +
                 "This is what turns 'collect' into 'deliver' — without it the player " +
                 "keeps the ore AND gets paid for it.")]
        public bool consumeOnComplete;
    }

    /// <summary>
    /// The closed vocabulary of objective kinds. Every value must have a case in
    /// <c>QuestManager.BuildObjective</c> — an unmapped kind logs and drops the
    /// objective, which silently makes a quest easier rather than failing loudly,
    /// so <c>QuestObjectiveKindCoverageTests</c> walks the enum against the
    /// dispatcher.
    ///
    /// <para>Append only, never renumber: these values are serialized inside every
    /// shipped quest asset.</para>
    /// </summary>
    public enum ObjectiveKind
    {
        /// <summary>Kill N of a monsterKey (or N of anything).</summary>
        KillCount = 0,

        /// <summary>Hold N of an itemId in the bag. Polled, so it does not care
        /// whether the item was looted, bought, crafted or harvested.</summary>
        Collect = 1,

        /// <summary>Set foot in a named zone.</summary>
        Reach = 2,

        /// <summary>Open a conversation with a personaId.</summary>
        Talk = 3,

        /// <summary>Craft N of a recipe (or N of anything).</summary>
        Craft = 4,

        /// <summary>Cast a spellKey N times (or cast anything N times).</summary>
        CastSpell = 5,

        /// <summary>Stay alive for N seconds after the objective begins.</summary>
        Survive = 6,

        /// <summary>Be player level N or higher.</summary>
        ReachLevel = 7,

        /// <summary>Hold N coins in the purse at once.</summary>
        EarnCoins = 8,
    }
}
