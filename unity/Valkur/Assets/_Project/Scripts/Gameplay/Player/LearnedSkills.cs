using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Per-player talent state: which <see cref="SkillNode"/>s are held and at what RANK,
    /// how many skill points are unspent, and the gating that decides whether the next
    /// rank of a node can be bought right now.
    ///
    /// It owns the decision and NOT the consequence: what a learned rank does to the
    /// character is <see cref="PlayerProgression"/>'s job, which rebuilds the whole
    /// <see cref="StatLayer.Skill"/> layer from this component's state. That split is what
    /// makes a refund exact — the layer is rebuilt from scratch, so nothing has to
    /// remember what to undo.
    ///
    /// Ranks are stored as id → rank rather than a set of ids because a five-rank node
    /// held at three is a different character from one held at five, and a set cannot say
    /// which. The save format changed with it; <see cref="ProgressionSaveData"/> carries
    /// both lists, so a pre-rank save still loads — every id it names comes back at rank 1.
    /// </summary>
    public sealed class LearnedSkills : MonoBehaviour
    {
        [SerializeField] private SkillTree tree;

        [Tooltip("Skill points currently available to spend.")]
        [SerializeField] private int availablePoints;

        [Tooltip("Skill points spent so far. Tracked separately from the available pool " +
                 "because a respec has to hand back exactly what was spent, and summing " +
                 "the nodes at refund time would silently lose the points sunk into a node " +
                 "a designer has since deleted from the tree.")]
        [SerializeField] private int spentPoints;

        private readonly Dictionary<string, int> _ranks =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public SkillTree Tree => tree;
        public int AvailablePoints => availablePoints;
        public int SpentPoints => spentPoints;
        public IReadOnlyDictionary<string, int> Ranks => _ranks;

        /// <summary>Fires (skillId, newRank) when a rank is bought.</summary>
        public event Action<string, int> OnSkillRankChanged;

        /// <summary>Fires (newAvailable) when the available-points balance changes.</summary>
        public event Action<int> OnPointsChanged;

        /// <summary>Fires after any change that could alter the character's stats —
        /// a purchase, a respec or a save being loaded.</summary>
        public event Action OnLoadoutChanged;

        public void SetTree(SkillTree newTree)
        {
            tree = newTree;
            OnLoadoutChanged?.Invoke();
        }

        public void AddPoints(int amount)
        {
            if (amount <= 0) return;
            availablePoints += amount;
            OnPointsChanged?.Invoke(availablePoints);
        }

        public int RankOf(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            return _ranks.TryGetValue(skillId, out int rank) ? rank : 0;
        }

        public int RankOf(SkillNode node) => node == null ? 0 : RankOf(node.skillId);

        public bool IsLearned(string skillId) => RankOf(skillId) > 0;

        /// <summary>
        /// True when the next rank of <paramref name="node"/> can be bought right now.
        /// <paramref name="reason"/> is always set on rejection so the tree view can say
        /// "Requires level 12" instead of greying a button out with no explanation — a
        /// locked node with no stated reason is the thing that makes a tree feel broken.
        /// </summary>
        public bool CanLearn(SkillNode node, int playerLevel, out string reason)
        {
            if (node == null)                       { reason = "Null skill node.";  return false; }
            if (string.IsNullOrEmpty(node.skillId)) { reason = "Skill has no id.";  return false; }

            int rank = RankOf(node.skillId);
            int nextRank = rank + 1;

            if (rank >= Mathf.Max(1, node.maxRank))
            {
                reason = "Already at max rank.";
                return false;
            }
            if (availablePoints < node.pointCost)
            {
                reason = $"Need {node.pointCost} skill point(s), have {availablePoints}.";
                return false;
            }

            int levelNeeded = node.LevelRequirementForRank(nextRank);
            if (playerLevel < levelNeeded)
            {
                reason = $"Requires level {levelNeeded}.";
                return false;
            }

            // A prerequisite must be at FULL rank, not merely started. A partial
            // prerequisite would let a player reach a capstone with one point in each
            // node on the way to it, which makes the tree's shape decorative.
            if (node.prerequisites != null)
            {
                foreach (var prereq in node.prerequisites)
                {
                    if (prereq == null) continue;
                    if (RankOf(prereq.skillId) < Mathf.Max(1, prereq.maxRank))
                    {
                        reason = $"Requires '{prereq.displayName}' at max rank.";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        public bool TryLearn(SkillNode node, int playerLevel, out string reason)
        {
            if (!CanLearn(node, playerLevel, out reason)) return false;

            int newRank = RankOf(node.skillId) + 1;
            _ranks[node.skillId] = newRank;
            availablePoints -= node.pointCost;
            spentPoints += node.pointCost;

            OnPointsChanged?.Invoke(availablePoints);
            OnSkillRankChanged?.Invoke(node.skillId, newRank);
            OnLoadoutChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Refunds every point and forgets every rank. The points come from
        /// <see cref="spentPoints"/> rather than from re-walking the tree, so a node the
        /// designer removed between two builds does not eat the points a player sank into
        /// it — which would be an unrecoverable loss on a live save.
        /// </summary>
        public void Respec()
        {
            if (spentPoints <= 0 && _ranks.Count == 0) return;

            availablePoints += spentPoints;
            spentPoints = 0;
            _ranks.Clear();

            OnPointsChanged?.Invoke(availablePoints);
            OnLoadoutChanged?.Invoke();
        }

        /// <summary>Every modifier the held ranks contribute, ready to be handed to
        /// <c>PlayerStats.SetLayer(StatLayer.Skill, …)</c> wholesale.</summary>
        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null || tree == null) return;
            foreach (var pair in _ranks)
            {
                if (!tree.TryGet(pair.Key, out var node) || node == null) continue;
                into.AddRange(node.ModifiersAtRank(pair.Value));
            }
        }

        // ── Save/load ─────────────────────────────────────────────────────────

        /// <summary>Writes this component's half of the shared progression document.</summary>
        public void WriteTo(ProgressionSaveData data)
        {
            if (data == null) return;

            data.skillIds = new List<string>(_ranks.Count);
            data.skillRanks = new List<int>(_ranks.Count);
            foreach (var pair in _ranks)
            {
                data.skillIds.Add(pair.Key);
                data.skillRanks.Add(pair.Value);
            }
            data.skillPoints = availablePoints;
            data.skillPointsSpent = spentPoints;
        }

        /// <summary>
        /// Rehydrates from the shared document. Ids the tree no longer contains are
        /// dropped with a warning rather than kept: a rank on a node that does not exist
        /// contributes nothing and would sit in the save forever, growing on every load.
        /// </summary>
        public void ReadFrom(ProgressionSaveData data)
        {
            _ranks.Clear();
            availablePoints = 0;
            spentPoints = 0;
            if (data == null) { OnLoadoutChanged?.Invoke(); return; }

            availablePoints = Mathf.Max(0, data.skillPoints);
            spentPoints = Mathf.Max(0, data.skillPointsSpent);

            if (data.skillIds != null)
            {
                for (int i = 0; i < data.skillIds.Count; i++)
                {
                    string id = data.skillIds[i];
                    if (string.IsNullOrEmpty(id)) continue;

                    // A save written before ranks existed carries no rank list at all.
                    // Reading it as rank 1 is the only interpretation that does not
                    // silently delete the player's progress.
                    int rank = (data.skillRanks != null && i < data.skillRanks.Count)
                        ? Mathf.Max(1, data.skillRanks[i])
                        : 1;

                    if (tree != null)
                    {
                        if (!tree.TryGet(id, out var node) || node == null)
                        {
                            Debug.LogWarning($"[LearnedSkills] Save references unknown skill " +
                                             $"id '{id}' — skipping. Tree may have been pruned.");
                            continue;
                        }
                        // A designer who lowered maxRank between two builds must not leave
                        // the player holding a rank the tree no longer offers.
                        rank = Mathf.Min(rank, Mathf.Max(1, node.maxRank));
                    }

                    _ranks[id] = rank;
                }
            }

            OnPointsChanged?.Invoke(availablePoints);
            OnLoadoutChanged?.Invoke();
        }
    }
}
