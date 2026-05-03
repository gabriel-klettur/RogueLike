using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Per-player runtime state: set of learned skill ids, available skill
    /// points, and the gating logic that decides whether a candidate node
    /// is currently learnable. Stat / spell / aura side-effects are the
    /// caller's responsibility — this component just owns "what does the
    /// player know".
    ///
    /// Persistence: <see cref="ToSnapshot"/> and <see cref="FromSnapshot"/>
    /// produce a flat string list + int that fits in any save schema. The
    /// SkillTree itself is content-addressed by skillId so a player who
    /// loads a save after a tree edit keeps their learned skills (unknown
    /// ids are silently dropped with a warning).
    /// </summary>
    public sealed class LearnedSkills : MonoBehaviour
    {
        [SerializeField] private SkillTree tree;

        [Tooltip("Skill points currently available to spend.")]
        [SerializeField] private int availablePoints;

        // HashSet for O(1) membership and to dedupe accidental duplicate adds.
        private readonly HashSet<string> _learned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SkillTree Tree => tree;
        public int AvailablePoints => availablePoints;
        public IReadOnlyCollection<string> LearnedIds => _learned;

        /// <summary>Fires (skillId) when a node is learned.</summary>
        public event Action<string> OnSkillLearned;

        /// <summary>Fires (newAvailable) when the available-points balance changes.</summary>
        public event Action<int> OnPointsChanged;

        public void SetTree(SkillTree newTree)
        {
            tree = newTree;
        }

        public void AddPoints(int amount)
        {
            if (amount <= 0) return;
            availablePoints += amount;
            OnPointsChanged?.Invoke(availablePoints);
        }

        public bool IsLearned(string skillId)
            => !string.IsNullOrEmpty(skillId) && _learned.Contains(skillId);

        /// <summary>
        /// Returns true if the player can learn the given node right now:
        /// node exists in the tree, isn't already learned, has enough points,
        /// meets the level gate (if any), and all prerequisites are learned.
        /// Set <paramref name="reason"/> on rejection so UI can surface
        /// "needs Strength I" instead of just "no".
        /// </summary>
        public bool CanLearn(SkillNode node, int playerLevel, out string reason)
        {
            if (node == null)                { reason = "Null skill node.";          return false; }
            if (string.IsNullOrEmpty(node.skillId)) { reason = "Skill has no id.";   return false; }
            if (_learned.Contains(node.skillId))    { reason = "Already learned.";   return false; }
            if (availablePoints < node.pointCost)
            {
                reason = $"Need {node.pointCost} skill point(s), have {availablePoints}.";
                return false;
            }
            if (playerLevel < node.levelRequirement)
            {
                reason = $"Requires level {node.levelRequirement}.";
                return false;
            }
            if (node.prerequisites != null)
            {
                foreach (var prereq in node.prerequisites)
                {
                    if (prereq == null) continue;
                    if (!_learned.Contains(prereq.skillId))
                    {
                        reason = $"Requires '{prereq.displayName}'.";
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

            availablePoints -= node.pointCost;
            _learned.Add(node.skillId);
            OnPointsChanged?.Invoke(availablePoints);
            OnSkillLearned?.Invoke(node.skillId);
            return true;
        }

        // ── Save/load ─────────────────────────────────────────────────────────

        [Serializable]
        public class Snapshot
        {
            public List<string> learned = new List<string>();
            public int availablePoints;
        }

        public Snapshot ToSnapshot()
        {
            return new Snapshot
            {
                learned = new List<string>(_learned),
                availablePoints = availablePoints,
            };
        }

        public void FromSnapshot(Snapshot snap)
        {
            _learned.Clear();
            availablePoints = 0;
            if (snap == null) return;
            availablePoints = Mathf.Max(0, snap.availablePoints);
            if (snap.learned == null) return;
            foreach (var id in snap.learned)
            {
                if (string.IsNullOrEmpty(id)) continue;
                // If we have a tree, drop ids that no longer exist (the
                // designer pruned a node since the player saved). Without
                // a tree assigned (rare but possible for headless tests),
                // accept any id at face value.
                if (tree != null && !tree.TryGet(id, out _))
                {
                    Debug.LogWarning($"[LearnedSkills] Save references unknown skill " +
                                     $"id '{id}' — skipping. Tree may have been pruned.");
                    continue;
                }
                _learned.Add(id);
            }
        }
    }
}
