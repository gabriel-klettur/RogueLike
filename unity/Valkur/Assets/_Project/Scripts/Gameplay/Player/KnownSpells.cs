using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Per-player grimoire state: which <see cref="SpellNode"/>s have been learned, which
    /// spell keys that adds up to, and how many arcane points are unspent.
    ///
    /// It is the twin of <see cref="LearnedSkills"/> and deliberately NOT the same
    /// component. The two progressions have different currencies, different shapes (a
    /// talent has ranks, a spell is learned once), different owners (a class tree versus
    /// shared schools) and different questions for the player. Merging them into one
    /// component would force every caller to say which half it means on every call, which
    /// is how the distinction erodes.
    ///
    /// The list of known keys is the single answer to "may this character cast X". Before
    /// it existed, <c>EntitySetup</c> registered all 77 shipped spells on the player's
    /// SpellCaster in the frame they spawned, so the answer was always yes.
    /// </summary>
    public sealed class KnownSpells : MonoBehaviour
    {
        [Tooltip("Every school of the grimoire. Assigned from ProgressionCatalog at spawn.")]
        [SerializeField] private List<SpellTree> trees = new List<SpellTree>();

        [Tooltip("Arcane points currently available to spend.")]
        [SerializeField] private int availablePoints;

        [SerializeField] private int spentPoints;

        [Tooltip("Class key used to resolve each school's affinity surcharge.")]
        [SerializeField] private string classKey;

        private readonly HashSet<string> _learnedNodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Spell keys the character knows without having paid for them: the starting kit.
        // Kept separate from the learned set so a respec cannot refund them and a save
        // written before a designer added one still picks it up on the next load.
        private readonly HashSet<string> _innateSpellKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int AvailablePoints => availablePoints;
        public int SpentPoints => spentPoints;
        public string ClassKey => classKey;
        public IReadOnlyList<SpellTree> Trees => trees;
        public IReadOnlyCollection<string> LearnedNodeIds => _learnedNodes;

        /// <summary>Fires (nodeId) when a node is learned.</summary>
        public event Action<string> OnNodeLearned;

        /// <summary>Fires (spellKey) when a spell becomes castable. Separate from
        /// <see cref="OnNodeLearned"/> because a node may carry no spell at all, and the
        /// spell bar only cares about the ones that do.</summary>
        public event Action<string> OnSpellLearned;

        public event Action<int> OnPointsChanged;

        /// <summary>Fires after any change that could alter stats or the castable set.</summary>
        public event Action OnLoadoutChanged;

        public void Configure(IEnumerable<SpellTree> schools, string playerClassKey,
                              IEnumerable<string> innateSpellKeys)
        {
            trees.Clear();
            if (schools != null)
            {
                foreach (var t in schools)
                    if (t != null) trees.Add(t);
            }

            classKey = playerClassKey ?? string.Empty;

            _innateSpellKeys.Clear();
            if (innateSpellKeys != null)
            {
                foreach (var k in innateSpellKeys)
                    if (!string.IsNullOrWhiteSpace(k)) _innateSpellKeys.Add(k);
            }

            OnLoadoutChanged?.Invoke();
        }

        public void AddPoints(int amount)
        {
            if (amount <= 0) return;
            availablePoints += amount;
            OnPointsChanged?.Invoke(availablePoints);
        }

        public bool IsNodeLearned(string nodeId)
            => !string.IsNullOrEmpty(nodeId) && _learnedNodes.Contains(nodeId);

        public bool IsNodeLearned(SpellNode node) => node != null && IsNodeLearned(node.nodeId);

        /// <summary>The single answer to "may this character cast that spell".</summary>
        public bool KnowsSpell(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return false;
            if (_innateSpellKeys.Contains(spellKey)) return true;

            foreach (var tree in trees)
            {
                if (tree == null) continue;
                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.spell == null) continue;
                    if (!_learnedNodes.Contains(node.nodeId)) continue;
                    if (string.Equals(node.spell.spellKey, spellKey,
                            StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Every spell key the character may cast right now.</summary>
        public void CollectKnownSpellKeys(List<string> into)
        {
            if (into == null) return;
            foreach (var k in _innateSpellKeys) into.Add(k);

            foreach (var tree in trees)
            {
                if (tree == null) continue;
                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.spell == null) continue;
                    if (!_learnedNodes.Contains(node.nodeId)) continue;
                    if (string.IsNullOrEmpty(node.spell.spellKey)) continue;
                    if (!into.Contains(node.spell.spellKey)) into.Add(node.spell.spellKey);
                }
            }
        }

        /// <summary>What this node costs THIS character, affinity surcharge included.</summary>
        public int ResolveCost(SpellTree tree, SpellNode node)
        {
            if (tree == null || node == null) return 0;
            return tree.ResolveCost(node, classKey);
        }

        public bool CanLearn(SpellTree tree, SpellNode node, int playerLevel, out string reason)
        {
            if (node == null)                      { reason = "Null spell node."; return false; }
            if (string.IsNullOrEmpty(node.nodeId)) { reason = "Node has no id.";  return false; }
            if (_learnedNodes.Contains(node.nodeId)) { reason = "Already known."; return false; }

            int cost = ResolveCost(tree, node);
            if (availablePoints < cost)
            {
                // Say the surcharge out loud. A node that costs 2 in a panel whose other
                // rows cost 1 reads as a bug unless the reason names the affinity.
                bool surcharged = tree != null && !tree.HasAffinity(classKey) && cost > node.pointCost;
                reason = surcharged
                    ? $"Need {cost} arcane point(s) — {tree.displayName} is not a {classKey} school."
                    : $"Need {cost} arcane point(s), have {availablePoints}.";
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
                    if (!_learnedNodes.Contains(prereq.nodeId))
                    {
                        reason = $"Requires '{prereq.ResolveDisplayName()}'.";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        public bool TryLearn(SpellTree tree, SpellNode node, int playerLevel, out string reason)
        {
            if (!CanLearn(tree, node, playerLevel, out reason)) return false;

            int cost = ResolveCost(tree, node);
            _learnedNodes.Add(node.nodeId);
            availablePoints -= cost;
            spentPoints += cost;

            OnPointsChanged?.Invoke(availablePoints);
            OnNodeLearned?.Invoke(node.nodeId);
            if (node.spell != null && !string.IsNullOrEmpty(node.spell.spellKey))
                OnSpellLearned?.Invoke(node.spell.spellKey);
            OnLoadoutChanged?.Invoke();
            return true;
        }

        public void Respec()
        {
            if (spentPoints <= 0 && _learnedNodes.Count == 0) return;

            availablePoints += spentPoints;
            spentPoints = 0;
            _learnedNodes.Clear();

            OnPointsChanged?.Invoke(availablePoints);
            OnLoadoutChanged?.Invoke();
        }

        /// <summary>Modifiers the learned nodes contribute, for the Grimoire stat layer.</summary>
        public void CollectModifiers(List<StatModifier> into)
        {
            if (into == null) return;
            foreach (var tree in trees)
            {
                if (tree == null) continue;
                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.modifiers == null) continue;
                    if (!_learnedNodes.Contains(node.nodeId)) continue;
                    into.AddRange(node.modifiers);
                }
            }
        }

        // ── Save/load ─────────────────────────────────────────────────────────

        public void WriteTo(ProgressionSaveData data)
        {
            if (data == null) return;
            data.grimoireNodeIds = new List<string>(_learnedNodes);
            data.arcanePoints = availablePoints;
            data.arcanePointsSpent = spentPoints;
        }

        public void ReadFrom(ProgressionSaveData data)
        {
            _learnedNodes.Clear();
            availablePoints = 0;
            spentPoints = 0;
            if (data == null) { OnLoadoutChanged?.Invoke(); return; }

            availablePoints = Mathf.Max(0, data.arcanePoints);
            spentPoints = Mathf.Max(0, data.arcanePointsSpent);

            if (data.grimoireNodeIds != null)
            {
                foreach (var id in data.grimoireNodeIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (trees.Count > 0 && !ExistsInAnyTree(id))
                    {
                        Debug.LogWarning($"[KnownSpells] Save references unknown grimoire " +
                                         $"node '{id}' — skipping. A school may have been pruned.");
                        continue;
                    }
                    _learnedNodes.Add(id);
                }
            }

            OnPointsChanged?.Invoke(availablePoints);
            OnLoadoutChanged?.Invoke();
        }

        private bool ExistsInAnyTree(string nodeId)
        {
            foreach (var tree in trees)
            {
                if (tree != null && tree.TryGet(nodeId, out _)) return true;
            }
            return false;
        }
    }
}
