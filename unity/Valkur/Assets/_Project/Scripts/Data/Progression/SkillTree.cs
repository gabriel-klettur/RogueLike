using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// A class's talent tree: the numeric half of progression, bought with SKILL POINTS
    /// earned by levelling. One asset per playable class.
    ///
    /// **Why this is separate from <see cref="SpellTree"/>, and why that separation is
    /// the load-bearing design decision here.** Folding both into one tree makes every
    /// "+5 % melee damage" node compete for the same currency as "unlock Meteor Shower",
    /// and those are not comparable choices: one tunes a build the player already has,
    /// the other hands them a verb they have never used. A player who spends every point
    /// on numbers never sees half the game's content; a player who spends every point on
    /// spells has 46 abilities and the stats of a level-1 character. Both are the game
    /// failing to ask a real question.
    ///
    /// They also have different SHAPES, which is the practical half of the argument:
    /// - A talent is per class. The dwarf's tree says what being a dwarf means.
    /// - A spell is shared. All five classes can learn Fireball, so a per-class copy of
    ///   the spell graph would be five assets drifting apart on the first retune.
    ///
    /// So: talents are per class and bought with skill points, spells are per SCHOOL and
    /// bought with arcane points. Two currencies, two trees, two questions.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillTree", menuName = "Valkur/Progression/Skill Tree")]
    public sealed class SkillTree : ScriptableObject
    {
        [Tooltip("Player-facing label, e.g. 'Path of the Mountain'.")]
        public string displayName;

        [Tooltip("PlayerDefinition.playerKey this tree belongs to ('dwarf', 'mague', …). " +
                 "Resolved at spawn by PlayerProgression, so a class with no tree of its " +
                 "own is a warning at boot rather than a silently empty panel.")]
        public string classKey;

        [TextArea(2, 4)]
        [Tooltip("One or two lines on the fantasy of this class, shown above the tree.")]
        public string flavour;

        [Tooltip("All nodes. Order is irrelevant at runtime — lookup is by skillId and " +
                 "the view lays itself out from each node's row/column.")]
        [SerializeField] private SkillNode[] nodes = System.Array.Empty<SkillNode>();

        public IReadOnlyList<SkillNode> Nodes => nodes;
        public int Count => nodes != null ? nodes.Length : 0;

        private Dictionary<string, SkillNode> _lookup;

        public SkillNode GetById(string skillId)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(skillId)) return null;
            _lookup.TryGetValue(skillId, out var node);
            return node;
        }

        public bool TryGet(string skillId, out SkillNode node)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(skillId)) { node = null; return false; }
            return _lookup.TryGetValue(skillId, out node);
        }

        /// <summary>Total skill points needed to max every node — the tree's "size" in
        /// the only unit the player cares about.</summary>
        public int TotalPointCost()
        {
            int total = 0;
            if (nodes == null) return 0;
            foreach (var n in nodes)
            {
                if (n == null) continue;
                total += n.pointCost * Mathf.Max(1, n.maxRank);
            }
            return total;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, SkillNode>(System.StringComparer.OrdinalIgnoreCase);
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.skillId)) continue;
                _lookup[n.skillId] = n;
            }
        }

        private void OnValidate() { _lookup = null; } // rebuild on next access

#if UNITY_EDITOR
        public void EditorSetNodes(SkillNode[] newNodes)
        {
            nodes = newNodes ?? System.Array.Empty<SkillNode>();
            _lookup = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
