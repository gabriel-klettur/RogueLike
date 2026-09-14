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

        /// <summary>
        /// Branches every class shares beside its own tree — today the "Barra de Guerra" branch.
        /// One pool of points and one rank table serve all of them: a branch is more of the
        /// same currency's question ("a wider bar, or more hit points?"), not a second economy.
        /// </summary>
        private readonly List<SkillTree> _branches = new List<SkillTree>(2);

        public SkillTree Tree => tree;

        /// <summary>The shared branches, in the order the talents board shows them.</summary>
        public IReadOnlyList<SkillTree> Branches => _branches;
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

        /// <summary>Installs the branches every class shares. Null entries are skipped.</summary>
        public void SetBranches(IEnumerable<SkillTree> branches)
        {
            _branches.Clear();
            if (branches != null)
                foreach (var b in branches)
                    if (b != null && b != tree && !_branches.Contains(b)) _branches.Add(b);
            OnLoadoutChanged?.Invoke();
        }

        /// <summary>
        /// The node with this id in the class tree or any shared branch. The ONE lookup every
        /// rank-to-node resolution goes through — the modifier collection, the save load, the
        /// console — so a branch talent is never a rank the character holds and nothing reads.
        /// </summary>
        public bool TryFindNode(string skillId, out SkillNode node)
        {
            node = null;
            if (string.IsNullOrEmpty(skillId)) return false;
            if (tree != null && tree.TryGet(skillId, out node) && node != null) return true;
            for (int i = 0; i < _branches.Count; i++)
                if (_branches[i].TryGet(skillId, out node) && node != null) return true;
            node = null;
            return false;
        }

        /// <summary>True when there is anything at all to look ids up in.</summary>
        private bool HasAnyTree => tree != null || _branches.Count > 0;

        /// <summary>
        /// Grants skill points — from a level, from a quest, from the console.
        ///
        /// <para>It raises <see cref="OnLoadoutChanged"/> as well as
        /// <see cref="OnPointsChanged"/>, and that second raise is not redundant: receiving a
        /// point changes WHAT CAN BE BOUGHT, which is the question every view of this tree is
        /// drawing. The talents panel subscribes only to the loadout event (the spells panel
        /// does the same), so before this a player who levelled up with the panel open watched
        /// it go on saying "0 puntos" and every node go on saying "te falta 1 punto" until they
        /// closed and reopened it. Neither event was wrong on its own; the composition was.</para>
        /// </summary>
        public void AddPoints(int amount)
        {
            if (amount <= 0) return;
            availablePoints += amount;
            OnPointsChanged?.Invoke(availablePoints);
            OnLoadoutChanged?.Invoke();
        }

        public int RankOf(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 0;
            return _ranks.TryGetValue(skillId, out int rank) ? rank : 0;
        }

        public int RankOf(SkillNode node) => node == null ? 0 : RankOf(node.skillId);

        public bool IsLearned(string skillId) => RankOf(skillId) > 0;

        /// <summary>
        /// Every reason the next rank of <paramref name="node"/> cannot be bought right now,
        /// appended to <paramref name="into"/>. Returns true when there are none.
        ///
        /// <para><b>The ORDER is a design decision and it used to be backwards.</b> The old
        /// body tested affordability FIRST and returned a single reason, so a character with
        /// no points was told "Need 2 skill point(s)" about every node in the tree — including
        /// the ones whose real gate is a prerequisite five ranks deep. Measured on the shipped
        /// dwarf: Bulwark reported a 2-point shortfall when what actually closes it is
        /// Stoneflesh at rank 5, which is eight points of commitment away. Level first, then
        /// the prerequisite, then the cost, because that is descending order of how little the
        /// player can do about it — the same ordering <c>CraftingService</c> and
        /// <c>KnownSpells</c> already use.</para>
        ///
        /// <para><b>Every reason, not the first.</b> Naming one gate at a time makes the player
        /// clear it and come back to find another, which is the same complaint the quest audit
        /// made about ingredient shortfalls.</para>
        /// </summary>
        public bool CollectLockReasons(SkillNode node, int playerLevel, List<SkillLock> into)
        {
            if (into == null) return false;
            int before = into.Count;

            if (node == null || string.IsNullOrEmpty(node.skillId))
            {
                into.Add(SkillLock.Malformed());
                return false;
            }

            int rank = RankOf(node.skillId);
            int maxRank = Mathf.Max(1, node.maxRank);
            if (rank >= maxRank)
            {
                into.Add(SkillLock.Maxed(rank));
                return false;
            }

            int levelNeeded = node.LevelRequirementForRank(rank + 1);
            if (playerLevel < levelNeeded)
                into.Add(SkillLock.Level(levelNeeded, playerLevel));

            // A prerequisite must be at FULL rank, not merely started. A partial
            // prerequisite would let a player reach a capstone with one point in each
            // node on the way to it, which makes the tree's shape decorative.
            if (node.prerequisites != null)
            {
                foreach (var prereq in node.prerequisites)
                {
                    if (prereq == null) continue;
                    int prereqMax = Mathf.Max(1, prereq.maxRank);
                    int prereqRank = RankOf(prereq.skillId);
                    if (prereqRank < prereqMax)
                        into.Add(SkillLock.Prerequisite(prereq, prereqMax - prereqRank));
                }
            }

            if (availablePoints < node.pointCost)
                into.Add(SkillLock.Points(node.pointCost, availablePoints));

            return into.Count == before;
        }

        private readonly List<SkillLock> _lockScratch = new List<SkillLock>(4);

        /// <summary>
        /// True when the next rank of <paramref name="node"/> can be bought right now.
        /// <paramref name="reason"/> carries the FIRST gate in English, for the console and for
        /// the callers that predate the list form; a view that draws the node uses
        /// <see cref="CollectLockReasons"/>, which names every one.
        /// </summary>
        public bool CanLearn(SkillNode node, int playerLevel, out string reason)
        {
            _lockScratch.Clear();
            if (CollectLockReasons(node, playerLevel, _lockScratch))
            {
                reason = string.Empty;
                return true;
            }

            reason = _lockScratch.Count > 0 ? _lockScratch[0].Describe() : "Cannot learn.";
            return false;
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
            if (into == null || !HasAnyTree) return;
            foreach (var pair in _ranks)
            {
                if (!TryFindNode(pair.Key, out var node)) continue;
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

                    if (HasAnyTree)
                    {
                        if (!TryFindNode(id, out var node))
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
